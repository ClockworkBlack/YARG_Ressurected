// Error collection for phones.
//
// The game already writes a log, but on Android it lands in the app's private folder
// (Android/data/<app>/files/release/logs), which file managers cannot open on Android 11 and
// later. So when something goes wrong on a phone there is nothing to look at or send.
//
// This keeps a second copy where the songs live, in Internal storage/YARG/logs:
//
//   yarg-log-<date>_<time>.txt   everything the game logged in one run (the newest 10 are kept)
//   yarg-errors.txt              only the errors and exceptions, from every run, with the build
//                                and device they happened on
//
// It collects from three places: the game's own logger (errors that were caught and logged),
// Unity's log (exceptions nobody caught, with their stack trace), and the runtime's last-chance
// handlers. Every line is written to disk straight away, so it survives the app being killed.
//
// On Windows and macOS the same files go to Documents/YARG/logs, and on Linux (and Chromebooks)
// to ~/YARG/logs, which are easier to find than the game's own log.
//
// Nothing leaves the phone: the files are only ever written to local storage.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Core.Logging;

namespace YARG.Helpers
{
    public static class SharedErrorLog
    {
        private const int KEEP_SESSION_LOGS = 10;
        private const long MAX_ERRORS_FILE_BYTES = 1024 * 1024;

        // Lines held back while the folder cannot be written yet (before the all-files
        // permission is granted, for example)
        private const int MAX_PENDING_LINES = 4000;
        private const double RETRY_SECONDS = 5;

        private static readonly object Lock = new();
        private static readonly List<string> PendingLog = new();
        private static readonly List<string> PendingErrors = new();

        private static bool _started;
        private static string _directory;
        private static string _header;
        private static string _scene = "(starting)";
        private static StreamWriter _log;
        private static StreamWriter _errors;
        private static DateTime _nextTry = DateTime.MinValue;
        private static int _errorCount;

        /// <summary>The folder the logs are written to, or null when this is not running.</summary>
        public static string LogFolder => _directory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Start()
        {
            if (_started || (Application.platform != RuntimePlatform.Android &&
                Application.platform != RuntimePlatform.WindowsPlayer &&
                Application.platform != RuntimePlatform.OSXPlayer &&
                Application.platform != RuntimePlatform.LinuxPlayer))
            {
                return;
            }
            _started = true;

            try
            {
                _directory = Path.Combine(FindStorageRoot(), "YARG", "logs");
                _header = BuildHeader();

                lock (Lock)
                {
                    PendingLog.Add(_header);
                    TryOpen();
                }

                YargLogger.AddLogListener(new Listener());
                Application.logMessageReceivedThreaded += OnUnityLog;
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                SceneManager.activeSceneChanged += OnSceneChanged;
                Application.lowMemory += OnLowMemory;
            }
            catch (Exception e)
            {
                // Never let the error collector be the thing that breaks startup
                Debug.LogException(e);
            }
        }

        // Internal storage, e.g. /storage/emulated/0: the app's own folder sits under
        // <root>/Android/data/<app>/files
        private static string FindStorageRoot()
        {
            if (Application.platform == RuntimePlatform.WindowsPlayer)
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return string.IsNullOrEmpty(documents) ? Application.persistentDataPath : documents;
            }

            if (Application.platform == RuntimePlatform.LinuxPlayer)
            {
                // ~/YARG/logs: on a Chromebook that is "Linux files > YARG > logs" in the Files app
                string home = Environment.GetEnvironmentVariable("HOME");
                return string.IsNullOrEmpty(home) ? Application.persistentDataPath : home;
            }

            if (Application.platform == RuntimePlatform.OSXPlayer)
            {
                // Mono maps MyDocuments to the home folder on macOS, so name ~/Documents directly
                string home = Environment.GetEnvironmentVariable("HOME");
                return string.IsNullOrEmpty(home) ? Application.persistentDataPath : Path.Combine(home, "Documents");
            }

            string persistent = Application.persistentDataPath;
            if (!string.IsNullOrEmpty(persistent))
            {
                int index = persistent.Replace('\\', '/').IndexOf("/Android/", StringComparison.Ordinal);
                if (index > 0)
                {
                    return persistent.Substring(0, index);
                }
            }
            return "/storage/emulated/0";
        }

        private static string BuildHeader()
        {
            var text = new StringBuilder();
            text.Append("=== YARG run started ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" ===\n");
            text.Append("Build: ").Append(Application.version)
                .Append(" (").Append(Application.buildGUID).Append("), Unity ").Append(Application.unityVersion).Append('\n');
            text.Append("Device: ").Append(SystemInfo.deviceModel).Append(", ").Append(SystemInfo.operatingSystem).Append('\n');
            text.Append("CPU: ").Append(SystemInfo.processorType).Append(" x").Append(SystemInfo.processorCount)
                .Append(", RAM ").Append(SystemInfo.systemMemorySize).Append(" MB\n");
            text.Append("GPU: ").Append(SystemInfo.graphicsDeviceName).Append(" (").Append(SystemInfo.graphicsDeviceVersion).Append(")\n");
            text.Append("Screen: ").Append(Screen.width).Append('x').Append(Screen.height);
            return text.ToString();
        }

        // Call with the lock held
        private static bool TryOpen()
        {
            if (_log != null)
            {
                return true;
            }

            var now = DateTime.UtcNow;
            if (now < _nextTry)
            {
                return false;
            }
            _nextTry = now.AddSeconds(RETRY_SECONDS);

            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                RemoveOldLogs();

                string errorsPath = Path.Combine(_directory, "yarg-errors.txt");
                try
                {
                    // Start over when it has grown large, keeping the previous one
                    if (File.Exists(errorsPath) && new FileInfo(errorsPath).Length > MAX_ERRORS_FILE_BYTES)
                    {
                        string older = Path.Combine(_directory, "yarg-errors-older.txt");
                        File.Delete(older);
                        File.Move(errorsPath, older);
                    }
                }
                catch (Exception)
                {
                    // Keep appending to the large file
                }

                string logPath = Path.Combine(_directory,
                    "yarg-log-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
                var log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false));
                StreamWriter errors;
                try
                {
                    errors = new StreamWriter(
                        new FileStream(errorsPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                        new UTF8Encoding(false));
                }
                catch (Exception)
                {
                    log.Dispose();
                    throw;
                }

                _log = log;
                _errors = errors;

                foreach (string line in PendingLog)
                {
                    _log.WriteLine(line);
                }
                PendingLog.Clear();
                _log.Flush();

                foreach (string line in PendingErrors)
                {
                    _errors.WriteLine(line);
                }
                PendingErrors.Clear();
                _errors.Flush();
                return true;
            }
            catch (Exception)
            {
                // Usually the all-files permission has not been granted yet; try again later
                return false;
            }
        }

        private static void RemoveOldLogs()
        {
            try
            {
                var files = System.IO.Directory.GetFiles(_directory, "yarg-log-*.txt");
                // The names sort by date
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - (KEEP_SESSION_LOGS - 1); i++)
                {
                    File.Delete(files[i]);
                }
            }
            catch (Exception)
            {
                // Old logs staying around is harmless
            }
        }

        private static void WriteLog(string text)
        {
            lock (Lock)
            {
                try
                {
                    if (TryOpen())
                    {
                        _log.WriteLine(text);
                        _log.Flush();
                    }
                    else if (PendingLog.Count < MAX_PENDING_LINES)
                    {
                        PendingLog.Add(text);
                    }
                }
                catch (Exception)
                {
                    CloseAfterFailure();
                }
            }
        }

        private static void WriteError(string source, string text)
        {
            lock (Lock)
            {
                try
                {
                    _errorCount++;
                    var entry = new StringBuilder();
                    if (_errorCount == 1)
                    {
                        // Each run's first error says which build and device it came from
                        entry.Append('\n').Append(_header).Append('\n');
                    }
                    entry.Append("\n[").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ")
                        .Append(source).Append(" | screen: ").Append(_scene).Append('\n').Append(text);

                    string line = entry.ToString();
                    if (TryOpen())
                    {
                        _errors.WriteLine(line);
                        _errors.Flush();
                    }
                    else if (PendingErrors.Count < MAX_PENDING_LINES)
                    {
                        PendingErrors.Add(line);
                    }
                }
                catch (Exception)
                {
                    CloseAfterFailure();
                }
            }
        }

        // Call with the lock held. Drops the files so the next write opens them again.
        private static void CloseAfterFailure()
        {
            try
            {
                _log?.Dispose();
            }
            catch (Exception)
            {
            }
            try
            {
                _errors?.Dispose();
            }
            catch (Exception)
            {
            }
            _log = null;
            _errors = null;
        }

        // Unity's log: this is where exceptions that nothing caught end up, with their stack trace
        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            {
                return;
            }

            // Messages from the game's own logger arrive here without a stack trace, and the
            // listener below has already written them
            if (string.IsNullOrEmpty(stackTrace))
            {
                return;
            }

            string text = condition + "\n" + stackTrace.TrimEnd();
            WriteError("Unity " + type, text);
            WriteLog("--------------- UNITY " + type.ToString().ToUpperInvariant() + " ---------------\n" + text);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            string text = args.ExceptionObject?.ToString() ?? "(no details)";
            WriteError("Unhandled exception" + (args.IsTerminating ? " (the app is closing)" : ""), text);
            WriteLog("--------------- UNHANDLED EXCEPTION ---------------\n" + text);
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            string text = args.Exception?.ToString() ?? "(no details)";
            WriteError("Background task exception", text);
            WriteLog("--------------- BACKGROUND TASK EXCEPTION ---------------\n" + text);
        }

        private static void OnSceneChanged(Scene from, Scene to)
        {
            _scene = to.name;
            WriteLog("=== Screen: " + to.name + " ===");
        }

        private static void OnLowMemory()
        {
            WriteError("Low memory", "Android reported that the device is running out of memory.");
            WriteLog("=== Low memory warning from Android ===");
        }

        // The game's own logger: everything goes to the run's log, errors also to the errors file
        private sealed class Listener : BaseYargLogListener
        {
            public Listener() : base(new DetailedYargLogFormatter())
            {
            }

            public override void WriteLogItem(ref Utf16ValueStringBuilder output, LogItem item)
            {
                string text = output.ToString();
                WriteLog(text);

                if (item.Level >= LogLevel.Error)
                {
                    WriteError("Game " + item.Level.AsLevelString(), text);
                }
            }
        }
    }
}
