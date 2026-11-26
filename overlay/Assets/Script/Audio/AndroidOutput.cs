// YARG Resurrected - play the game's sound on the output the player picks (phone speaker, a TV
// over HDMI, USB, Bluetooth, headphones).
//
// Android sends all sound to the last sound device plugged in, and a USB microphone with a
// headphone jack (a Fifine AM8, say) counts as one: plug it in and the phone speaker and the TV go
// quiet. The game's sound library can't choose an output on Android, so this takes its finished
// mix, the sound of everything playing, just before it goes out, plays it on the chosen output
// through Android's own low-latency audio (AAudio), and silences the original. If the chosen
// output isn't connected, or goes away, the sound is simply left where Android puts it.
//
// On Automatic, a USB microphone never takes the sound: it goes to Bluetooth, wired headphones,
// the TV or the phone speaker instead (in that order, whichever is connected). Pick "USB Device"
// to hear the game through headphones plugged into the microphone.
//
// The two outputs run on clocks of their own, so the mix is very slightly sped up or slowed down
// to keep a steady amount waiting, never by dropping or repeating samples (which crackles). How
// much is kept waiting follows how unevenly the mix arrives, and if the sound library itself
// stops (Android stalls it when a microphone starts on the same USB device), it is restarted.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using AOT;
using ManagedBass;
using YARG.Audio.BASS;
using YARG.Core.Logging;
using YARG.Menu.Persistent;
#endif

namespace YARG.Audio
{
    public enum SoundOutputDevice
    {
        Automatic,
        PhoneSpeaker,
        Tv,
        Usb,
        Bluetooth,
        Headphones,
    }

    public static class AndroidOutput
    {
        private static SoundOutputDevice _choice = SoundOutputDevice.Automatic;

        /// <summary>
        /// True while the game's sound is played on the phone speaker or a TV, out loud in the
        /// room, where a microphone would hear it again. (False when it is not known.)
        /// </summary>
        public static bool PlaysOutLoud { get; private set; }

        /// <summary>
        /// The Android output device (its id) the game's sound is playing on, or 0 when Android
        /// chooses. A singer's voice played back follows it, so it comes out of the same place.
        /// </summary>
        public static int DeviceId
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return _active ? _streamDevice : 0;
#else
                return 0;
#endif
            }
        }

        /// <summary>The setting changed (or was loaded).</summary>
        public static void Apply(SoundOutputDevice choice)
        {
            _choice = choice;
#if UNITY_ANDROID && !UNITY_EDITOR
            _changed = true;
            EnsurePoller();
#endif
        }

        /// <summary>The sound library is up and playing on its device.</summary>
        public static void OnBassReady()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _bassReady = true;
            _changed = true;
            EnsurePoller();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string JAVA_CLASS = "in.yarg.resurrected.YargAudioRoute";
        private const string AAUDIO = "aaudio";
        private const string BASS = "bass";

        private const int AAUDIO_DIRECTION_OUTPUT = 0;
        private const int AAUDIO_SHARING_MODE_SHARED = 1;
        private const int AAUDIO_PERFORMANCE_MODE_LOW_LATENCY = 12;
        private const int AAUDIO_FORMAT_PCM_I16 = 1;
        private const int AAUDIO_FORMAT_PCM_FLOAT = 2;
        private const int AAUDIO_USAGE_GAME = 14;
        private const int AAUDIO_CONTENT_TYPE_MUSIC = 2;

        [DllImport(AAUDIO)] private static extern int AAudio_createStreamBuilder(out IntPtr builder);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setDeviceId(IntPtr builder, int deviceId);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setDirection(IntPtr builder, int direction);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setSharingMode(IntPtr builder, int mode);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setPerformanceMode(IntPtr builder, int mode);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setSampleRate(IntPtr builder, int rate);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setChannelCount(IntPtr builder, int channels);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setFormat(IntPtr builder, int format);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setUsage(IntPtr builder, int usage);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setContentType(IntPtr builder, int type);
        [DllImport(AAUDIO)] private static extern void AAudioStreamBuilder_setBufferCapacityInFrames(IntPtr builder, int frames);
        [DllImport(AAUDIO)] private static extern int AAudioStreamBuilder_openStream(IntPtr builder, out IntPtr stream);
        [DllImport(AAUDIO)] private static extern int AAudioStreamBuilder_delete(IntPtr builder);
        [DllImport(AAUDIO)] private static extern int AAudioStream_requestStart(IntPtr stream);
        [DllImport(AAUDIO)] private static extern int AAudioStream_requestStop(IntPtr stream);
        [DllImport(AAUDIO)] private static extern int AAudioStream_close(IntPtr stream);
        [DllImport(AAUDIO)] private static extern int AAudioStream_getFramesPerBurst(IntPtr stream);
        [DllImport(AAUDIO)] private static extern int AAudioStream_setBufferSizeInFrames(IntPtr stream, int frames);
        [DllImport(AAUDIO)] private static extern int AAudioStream_getDeviceId(IntPtr stream);
        [DllImport(AAUDIO)] private static extern long AAudioStream_getFramesWritten(IntPtr stream);
        [DllImport(AAUDIO)] private static extern long AAudioStream_getFramesRead(IntPtr stream);
        [DllImport(AAUDIO)] private static extern int AAudioStream_getXRunCount(IntPtr stream);
        [DllImport(AAUDIO)] private static extern int AAudioStream_getBufferCapacityInFrames(IntPtr stream);
        [DllImport(AAUDIO, EntryPoint = "AAudioStream_write")]
        private static extern int AAudioStream_writeFloats(IntPtr stream, float[] buffer, int frames, long timeoutNanos);
        [DllImport(AAUDIO, EntryPoint = "AAudioStream_write")]
        private static extern int AAudioStream_writeShorts(IntPtr stream, short[] buffer, int frames, long timeoutNanos);
        [DllImport(AAUDIO, EntryPoint = "AAudioStream_write")]
        private static extern int AAudioStream_writePtr(IntPtr stream, IntPtr buffer, int frames, long timeoutNanos);
        [DllImport(AAUDIO, EntryPoint = "AAudioStream_write")]
        private static extern int AAudioStream_writeBytes(IntPtr stream, byte[] buffer, int frames, long timeoutNanos);

        // STREAMPROC_DEVICE: a stand-in stream for the device's final mix, for DSP on all of it
        [DllImport(BASS)] private static extern int BASS_StreamCreate(int freq, int chans, int flags, IntPtr proc, IntPtr user);

        private enum Status { Usual, Missing, Already, Redirected, Failed }

        private static bool _bassReady;
        private static volatile bool _changed;
        private static Status _status = Status.Usual;
        private static int _statusDevice;

        private static AndroidJavaClass _route;
        private static AndroidJavaObject _activity;

        private static int _deviceStream;
        private static bool _dspSet;

        private static readonly object _lock = new object();
        private static IntPtr _stream;
        private static int _streamDevice;
        private static int _bytesPerFrame;
        private static int _channels;
        private static bool _isFloat;
        private static int _burst;
        private static int _capacity;
        private static long _target;
        private static long _maxTarget;
        private static int _xruns;
        private static double _smoothedWaiting;
        private static long _minTarget;
        private static long _windowMin;
        private static long _windowFrames;
        private static long _dropped;
        private static double _position;    // where the next output frame falls, in frames of _work
        private static float[] _work = new float[0];
        private static float[] _outFloats = new float[0];
        private static short[] _inShorts = new short[0];
        private static short[] _outShorts = new short[0];
        private static long _lastCallback;
        private static float _nextKick;
        private static int _kicks;
        private static double _maxStepSeen = 1.0, _minStepSeen = 1.0;
        private static volatile bool _active;
        private static volatile bool _lost;
        private static byte[] _zeros = new byte[1 << 16];
        private static int _adjusted;
        private static int _rate;
        private static long _lastRead;
        private static long _stalled;
        private static string _lostReason = "";
        private static int _restarts;
        private static float _restartWindow;
        private static float _retryAfter;

        private sealed class Poller : MonoBehaviour
        {
            private float _next;

            private void Update()
            {
                try
                {
                    Watchdog();
                }
                catch (Exception e)
                {
                    YargLogger.LogWarning("Sound output watchdog failed: " + e);
                }

                if (!_changed && !_lost && Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 2f;
                _changed = false;
                try
                {
                    Check();
                }
                catch (Exception e)
                {
                    YargLogger.LogWarning("Sound output failed: " + e);
                    Stop();
                }
            }

            private void OnApplicationQuit()
            {
                Stop();
            }
        }

        private static Poller _poller;

        private static void EnsurePoller()
        {
            if (_poller != null) return;
            var holder = new GameObject("Sound Output");
            UnityEngine.Object.DontDestroyOnLoad(holder);
            _poller = holder.AddComponent<Poller>();
        }

        private static int Kind(SoundOutputDevice choice) => choice switch
        {
            SoundOutputDevice.PhoneSpeaker => 1,
            SoundOutputDevice.Tv => 2,
            SoundOutputDevice.Usb => 3,
            SoundOutputDevice.Bluetooth => 4,
            SoundOutputDevice.Headphones => 5,
            _ => 0,
        };

        private static string ChoiceName(SoundOutputDevice choice) => choice switch
        {
            SoundOutputDevice.PhoneSpeaker => "phone speaker",
            SoundOutputDevice.Tv => "TV (HDMI)",
            SoundOutputDevice.Usb => "USB sound device",
            SoundOutputDevice.Bluetooth => "Bluetooth speaker or headphones",
            SoundOutputDevice.Headphones => "wired headphones",
            _ => "usual output",
        };

        private static bool Java()
        {
            if (_route != null) return true;
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            _route = new AndroidJavaClass(JAVA_CLASS);
            YargLogger.LogInfo("Sound outputs: " + _route.CallStatic<string>("describe", _activity));
            return _route != null;
        }

        private static void Report(Status status, int device, string text, bool warning = false)
        {
            if (status == _status && device == _statusDevice) return;
            _status = status;
            _statusDevice = device;
            YargLogger.LogInfo("Sound output: " + text);
            if (text == null) return;
            if (warning) ToastManager.ToastWarning(text);
            else ToastManager.ToastInformation(text);
        }

        private static void Check()
        {
            if (!_bassReady) return;

            int kind = Kind(_choice);
            if (!Java()) return;

            // Automatic: a USB microphone is not a place to play the game, whatever Android thinks
            bool avoidingMic = false;
            if (kind == 0 && _route.CallStatic<bool>("soundGoesToUsbMic", _activity))
            {
                foreach (int k in new[] { 4, 5, 2, 1 })
                {
                    if (_route.CallStatic<int>("find", _activity, k) != 0)
                    {
                        kind = k;
                        avoidingMic = true;
                        break;
                    }
                }
            }

            if (kind == 0)
            {
                Stop();
                PlaysOutLoud = false;
                Report(Status.Usual, 0, _status == Status.Redirected ? "Sound: wherever Android sends it" : null);
                return;
            }

            // Out loud: the phone speaker or the TV
            PlaysOutLoud = kind == 1 || kind == 2;

            int id = _route.CallStatic<int>("find", _activity, kind);
            if (id == 0)
            {
                Stop();
                Report(Status.Missing, 0, "No " + ChoiceName(_choice) + " is connected, so the sound plays where Android sends it.", true);
                return;
            }

            string name = _route.CallStatic<string>("name", _activity, id);
            if (_route.CallStatic<bool>("isDefault", _activity, id))
            {
                // Android already plays there: nothing to do, and no delay added
                Stop();
                Report(Status.Already, id, "Sound: " + name);
                return;
            }

            // The sound library's device was restarted: the hook into its final mix went with it
            if (_deviceStream != 0 && !Bass.ChannelGetInfo(_deviceStream, out _))
            {
                YargLogger.LogInfo("Sound output: the final-mix stream is gone, attaching again");
                _deviceStream = 0;
                _dspSet = false;
                Stop();
            }

            if (_active && !_lost && _streamDevice == id) return;

            if (_lost)
            {
                // Android stops or stalls a stream when something else changes (a microphone starting
                // to record, a device plugged in): open it again, but not in a tight loop
                YargLogger.LogInfo("Sound output: stream lost (" + _lostReason + "), opening it again");
                float now = Time.unscaledTime;
                if (now - _restartWindow > 20f)
                {
                    _restartWindow = now;
                    _restarts = 0;
                }
                if (++_restarts > 6) _retryAfter = now + 10f;
            }

            Stop();
            _lost = false;
            if (Time.unscaledTime < _retryAfter)
            {
                Report(Status.Failed, id, "The sound keeps dropping out on " + name + ", so it plays where Android sends it for now.", true);
                return;
            }

            if (Start(id))
            {
                Report(Status.Redirected, id, "Sound: " + name + (avoidingMic ? " (not the USB microphone)" : ""));
            }
            else
            {
                Report(Status.Failed, id, "Couldn't play the sound on " + name + ", so it plays where Android sends it.", true);
            }
        }

        private const int AAUDIO_PERFORMANCE_MODE_NONE = 10;

        private static IntPtr Open(int deviceId, int rate, int channels, bool isFloat, int performanceMode, out int result)
        {
            result = -1;
            if (AAudio_createStreamBuilder(out var builder) != 0 || builder == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                AAudioStreamBuilder_setDeviceId(builder, deviceId);
                AAudioStreamBuilder_setDirection(builder, AAUDIO_DIRECTION_OUTPUT);
                AAudioStreamBuilder_setSharingMode(builder, AAUDIO_SHARING_MODE_SHARED);
                AAudioStreamBuilder_setPerformanceMode(builder, performanceMode);
                AAudioStreamBuilder_setSampleRate(builder, rate);
                AAudioStreamBuilder_setChannelCount(builder, channels);
                AAudioStreamBuilder_setFormat(builder, isFloat ? AAUDIO_FORMAT_PCM_FLOAT : AAUDIO_FORMAT_PCM_I16);
                // Room for half a second, so a late or bunched-up block of the mix is never thrown away
                AAudioStreamBuilder_setBufferCapacityInFrames(builder, rate / 2);
                try
                {
                    // Android 9 and later
                    AAudioStreamBuilder_setUsage(builder, AAUDIO_USAGE_GAME);
                    AAudioStreamBuilder_setContentType(builder, AAUDIO_CONTENT_TYPE_MUSIC);
                }
                catch (EntryPointNotFoundException)
                {
                }
                result = AAudioStreamBuilder_openStream(builder, out var stream);
                return result == 0 ? stream : IntPtr.Zero;
            }
            finally
            {
                AAudioStreamBuilder_delete(builder);
            }
        }

        private static bool Start(int deviceId)
        {
            if (_deviceStream == 0)
            {
                _deviceStream = BASS_StreamCreate(0, 0, 0, new IntPtr(-2), IntPtr.Zero);
                if (_deviceStream == 0)
                {
                    YargLogger.LogWarning("Sound output: no final-mix stream: " + Bass.LastError);
                    return false;
                }
            }

            var info = Bass.ChannelGetInfo(_deviceStream);
            bool isFloat = (info.Flags & BassFlags.Float) != 0;
            int rate = info.Frequency;
            int channels = Math.Max(1, info.Channels);
            int bytesPerFrame = channels * (isFloat ? 4 : 2);

            // The quick path first. Some phones give it only a few milliseconds of room, which is
            // not enough to carry sound from one output's clock to another's: then the ordinary
            // path, which has plenty.
            string path = "low-latency";
            IntPtr stream = Open(deviceId, rate, channels, isFloat, AAUDIO_PERFORMANCE_MODE_LOW_LATENCY, out int result);
            if (stream != IntPtr.Zero && AAudioStream_getBufferCapacityInFrames(stream) < rate / 5)
            {
                AAudioStream_close(stream);
                stream = IntPtr.Zero;
            }
            if (stream == IntPtr.Zero)
            {
                path = "ordinary";
                stream = Open(deviceId, rate, channels, isFloat, AAUDIO_PERFORMANCE_MODE_NONE, out result);
            }

            if (stream == IntPtr.Zero)
            {
                YargLogger.LogWarning("Sound output: AAudio could not open device " + deviceId + ": " + result);
                return false;
            }

            int actual = AAudioStream_getDeviceId(stream);
            if (actual != deviceId)
            {
                YargLogger.LogWarning("Sound output: asked for device " + deviceId + ", AAudio gave " + actual);
            }

            // Keep about 40 ms waiting to be played to begin with. From then on the amount follows
            // how unevenly the mix really arrives (see Redirect): more when it runs low, less
            // again when there is room to spare.
            int burst = Math.Max(32, AAudioStream_getFramesPerBurst(stream));
            int capacity = AAudioStream_getBufferCapacityInFrames(stream);
            if (capacity <= 0) capacity = rate / 5;
            AAudioStream_setBufferSizeInFrames(stream, capacity);
            int target = Math.Min(Math.Max(2 * burst, rate * 4 / 100), capacity / 2);
            int maxTarget = Math.Max(target, Math.Min(capacity * 2 / 3, rate / 4));

            if (AAudioStream_requestStart(stream) != 0)
            {
                AAudioStream_close(stream);
                return false;
            }

            if (_zeros.Length < maxTarget * bytesPerFrame) _zeros = new byte[maxTarget * bytesPerFrame];
            AAudioStream_writeBytes(stream, _zeros, target, 0);

            lock (_lock)
            {
                BuildFilter();
                _stream = stream;
                _streamDevice = deviceId;
                _bytesPerFrame = bytesPerFrame;
                _channels = channels;
                _isFloat = isFloat;
                _burst = burst;
                _capacity = capacity;
                _target = target;
                _minTarget = target;
                _maxTarget = maxTarget;
                _xruns = AAudioStream_getXRunCount(stream);
                _smoothedWaiting = target;
                _windowMin = long.MaxValue;
                _windowFrames = 0;
                _dropped = 0;
                _position = TAPS / 2 - 1;
                _work = new float[TAPS * channels];
                _maxStepSeen = _minStepSeen = 1.0;
                _lost = false;
                _adjusted = 0;
                _rate = rate;
                _lastRead = -1;
                _stalled = 0;
                Volatile.Write(ref _lastCallback, System.Diagnostics.Stopwatch.GetTimestamp());
                _active = true;
            }

            if (!_dspSet)
            {
                // Lowest priority, so it sees the mix after everything else has had its say
                _dspSet = BassDirect.ChannelSetDSP(_deviceStream, Redirect, IntPtr.Zero, -1000) != 0;
                if (!_dspSet)
                {
                    YargLogger.LogWarning("Sound output: could not attach to the final mix: " + Bass.LastError);
                    Stop();
                    return false;
                }
            }

            YargLogger.LogInfo("Sound output: playing on device " + deviceId + " at " + rate + " Hz, " + channels +
                " channel(s), " + (isFloat ? "float" : "16-bit") + ", " + path + " path, room for " + capacity +
                " frames, " + target + " kept waiting");
            return true;
        }

        private static void Stop()
        {
            _active = false;
            lock (_lock)
            {
                if (_stream == IntPtr.Zero) return;
                AAudioStream_requestStop(_stream);
                AAudioStream_close(_stream);
                _stream = IntPtr.Zero;
                _streamDevice = 0;
                YargLogger.LogInfo("Sound output: closed. Speed changes of " +
                    ((_minStepSeen - 1.0) * 100).ToString("0.###") + "% to " + ((_maxStepSeen - 1.0) * 100).ToString("0.###") +
                    "% evened out the clocks; ran dry " + _adjusted + " time(s); " + _dropped + " frame(s) let go; " +
                    (_rate > 0 ? _target * 1000 / _rate : 0) + " ms kept waiting at the end");
            }
        }

        // The sound library has stopped handing over its mix (Android stalls its output when a
        // microphone starts recording on the same USB device, or the USB link hiccups): nudge it.
        private static void Watchdog()
        {
            if (!_active || _lost) return;

            double silent = (System.Diagnostics.Stopwatch.GetTimestamp() - Volatile.Read(ref _lastCallback)) /
                (double) System.Diagnostics.Stopwatch.Frequency;
            float now = Time.unscaledTime;
            if (silent < 0.75 || now < _nextKick) return;

            _nextKick = now + 1.5f;
            _kicks++;
            bool hard = _kicks % 2 == 0;
            if (hard) Bass.Pause();
            bool started = Bass.Start();
            YargLogger.LogInfo("Sound output: no sound from the sound library for " + silent.ToString("0.0") +
                " s, restarting its output (" + (hard ? "pause and start" : "start") + "): " +
                (started ? "ok" : Bass.LastError.ToString()));
        }

        // Runs on the sound library's mixing thread with each block of the final mix
        [MonoPInvokeCallback(typeof(DSPProcedure))]
        private static void Redirect(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            Volatile.Write(ref _lastCallback, System.Diagnostics.Stopwatch.GetTimestamp());
            if (!_active || length <= 0 || buffer == IntPtr.Zero) return;
            if (!Monitor.TryEnter(_lock)) return;
            try
            {
                if (_stream == IntPtr.Zero || _bytesPerFrame <= 0) return;

                int frames = length / _bytesPerFrame;
                if (frames <= 0) return;

                long read = AAudioStream_getFramesRead(_stream);
                if (read != _lastRead)
                {
                    _lastRead = read;
                    _stalled = 0;
                }
                else if ((_stalled += frames) > _rate * 3 / 10)
                {
                    // Nothing has been played for a third of a second: the output has stopped taking
                    // sound without saying so. Leave the sound on the usual output and reopen.
                    _lostReason = "stalled";
                    _lost = true;
                    _active = false;
                    return;
                }

                long waiting = AAudioStream_getFramesWritten(_stream) - read;

                // How low the waiting sound gets just before each block arrives says how unevenly
                // the mix is coming. Once a second: keep more waiting if it nearly ran out, a
                // little less if there was plenty to spare.
                if (waiting < _windowMin) _windowMin = waiting;
                _windowFrames += frames;
                if (_windowFrames >= _rate)
                {
                    long margin = _rate / 100;
                    if (_windowMin < margin)
                    {
                        _target = Math.Min(_maxTarget, _target + (margin - _windowMin) + margin / 2);
                    }
                    else if (_windowMin > 3 * margin && _target > _minTarget)
                    {
                        _target = Math.Max(_minTarget, _target - (_windowMin - 2 * margin) / 8);
                    }
                    _windowMin = long.MaxValue;
                    _windowFrames = 0;
                }

                // The output ran dry (a click): keep more waiting from now on, and fill up at
                // once rather than limp along nearly empty
                int xruns = AAudioStream_getXRunCount(_stream);
                if (xruns > _xruns)
                {
                    _xruns = xruns;
                    _adjusted++;
                    _target = Math.Min(_maxTarget, _target + _rate / 100);
                    if (waiting < _target)
                    {
                        int fill = (int) Math.Min(_target - waiting, _zeros.Length / _bytesPerFrame);
                        if (fill > 0 && AAudioStream_writeBytes(_stream, _zeros, fill, 0) > 0)
                        {
                            waiting += fill;
                        }
                    }
                    _smoothedWaiting = waiting;
                }

                // (Smoothed over a second or so, so the speed changes far too slowly to hear)
                _smoothedWaiting += (waiting - _smoothedWaiting) * 0.01;

                int written;
                if (waiting + frames + _burst > _capacity)
                {
                    // No room (the output was held up): let this block go
                    _dropped += frames;
                    written = 0;
                }
                else
                {
                    // Played a hair faster when too much is waiting, slower when too little: at
                    // most 0.3%, which no one can hear (a twentieth of the smallest step in pitch)
                    double error = (_smoothedWaiting - _target) / _rate;
                    double step = 1.0 + Math.Max(-0.003, Math.Min(0.003, error * 0.1));
                    if (step > _maxStepSeen) _maxStepSeen = step;
                    if (step < _minStepSeen) _minStepSeen = step;
                    written = WriteResampled(buffer, frames, step);
                }

                if (written < 0)
                {
                    // The output went away: leave this block (and the rest) on the usual output
                    _lostReason = "error " + written;
                    _lost = true;
                    _active = false;
                    return;
                }

                // Silence the original
                for (int done = 0; done < length; done += _zeros.Length)
                {
                    Marshal.Copy(_zeros, 0, IntPtr.Add(buffer, done), Math.Min(_zeros.Length, length - done));
                }
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }

        // ---- Changing the speed by a hair without touching the sound ----
        //
        // Each output sample is worked out from the 16 input samples around it with a windowed
        // sinc filter: flat to past 20 kHz, so nothing is dulled or roughened, however the two
        // clocks drift. (Simply sliding between neighbouring samples dulls the treble, by an
        // amount that comes and goes.)

        private const int TAPS = 16;
        private const int PHASES = 256;
        private static float[] _filter;

        private static void BuildFilter()
        {
            if (_filter != null) return;
            var filter = new float[(PHASES + 1) * TAPS];
            for (int p = 0; p <= PHASES; p++)
            {
                double fraction = (double) p / PHASES;
                double sum = 0;
                var row = new double[TAPS];
                for (int t = 0; t < TAPS; t++)
                {
                    double x = t - (TAPS / 2 - 1) - fraction;
                    double sinc = Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                    double r = x / (TAPS / 2);
                    double window = Math.Abs(r) >= 1.0 ? 0.0 : BesselI0(9.0 * Math.Sqrt(1.0 - r * r)) / BesselI0(9.0);
                    row[t] = sinc * window;
                    sum += row[t];
                }
                for (int t = 0; t < TAPS; t++) filter[p * TAPS + t] = (float) (row[t] / sum);
            }
            _filter = filter;
        }

        private static double BesselI0(double x)
        {
            double sum = 1.0, term = 1.0;
            for (int k = 1; k < 40; k++)
            {
                term *= (x / (2.0 * k)) * (x / (2.0 * k));
                sum += term;
            }
            return sum;
        }

        // Writes one block of the mix to the output, read at "step" input frames per output
        // frame. The last TAPS frames of each block are kept for the next. Under the lock.
        private static int WriteResampled(IntPtr buffer, int frames, double step)
        {
            int channels = _channels;
            int samples = frames * channels;
            int kept = TAPS * channels;
            int most = (int) (frames / step) + 3;

            // _work: the TAPS frames kept from before, then this block
            if (_work.Length < kept + samples)
            {
                var bigger = new float[kept + samples];
                Array.Copy(_work, bigger, kept);
                _work = bigger;
            }
            if (_outFloats.Length < most * channels) _outFloats = new float[most * channels];
            if (_isFloat)
            {
                Marshal.Copy(buffer, _work, kept, samples);
            }
            else
            {
                if (_inShorts.Length < samples) _inShorts = new short[samples];
                Marshal.Copy(buffer, _inShorts, 0, samples);
                for (int i = 0; i < samples; i++) _work[kept + i] = _inShorts[i];
            }

            // An output frame at "position" uses frames floor(position) - 7 to floor(position) + 8
            int total = TAPS + frames;
            int before = TAPS / 2 - 1;
            double position = _position;
            int count = 0;
            var filter = _filter;
            while (count < most)
            {
                int index = (int) position;
                if (index + TAPS / 2 > total - 1) break;

                double phase = (position - index) * PHASES;
                int p = (int) phase;
                float blend = (float) (phase - p);
                int row = p * TAPS;
                int first = (index - before) * channels;
                int o = count * channels;
                for (int c = 0; c < channels; c++)
                {
                    float sum = 0f;
                    int at = first + c;
                    for (int t = 0; t < TAPS; t++)
                    {
                        float a = filter[row + t];
                        sum += _work[at] * (a + (filter[row + TAPS + t] - a) * blend);
                        at += channels;
                    }
                    _outFloats[o + c] = sum;
                }
                count++;
                position += step;
            }
            _position = position - frames;
            Array.Copy(_work, frames * channels, _work, 0, kept);

            if (count == 0) return 0;
            int written;
            if (_isFloat)
            {
                written = AAudioStream_writeFloats(_stream, _outFloats, count, 0);
            }
            else
            {
                int outSamples = count * channels;
                if (_outShorts.Length < outSamples) _outShorts = new short[outSamples];
                for (int i = 0; i < outSamples; i++)
                {
                    float v = _outFloats[i];
                    _outShorts[i] = (short) (v > 32767f ? 32767f : v < -32768f ? -32768f : v);
                }
                written = AAudioStream_writeShorts(_stream, _outShorts, count, 0);
            }

            if (written >= 0 && written < count) _dropped += count - written;
            return written;
        }
#endif
    }
}
