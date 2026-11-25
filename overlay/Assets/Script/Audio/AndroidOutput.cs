// YARG Resurrected - play the game's sound on the output the player picks (phone speaker, a TV
// over HDMI, USB, Bluetooth, headphones).
//
// Android sends all sound to the last sound device plugged in, and a USB microphone with a
// headphone jack (a Fifine AM8, say) counts as one: plug it in and the phone speaker and the TV go
// quiet. The game's sound library can't choose an output on Android, so this takes its finished
// mix, the sound of everything playing, just before it goes out, plays it on the chosen output
// through Android's own low-latency audio (AAudio), and silences the original. If the chosen
// output isn't connected, or goes away, the sound is simply left where Android puts it.

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
        private static long _low, _high;
        private static volatile bool _active;
        private static volatile bool _lost;
        private static byte[] _zeros = new byte[1 << 16];
        private static int _adjusted;

        private sealed class Poller : MonoBehaviour
        {
            private float _next;

            private void Update()
            {
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
            if (kind == 0)
            {
                Stop();
                Report(Status.Usual, 0, _status == Status.Redirected ? "Sound: wherever Android sends it" : null);
                return;
            }

            if (!Java()) return;

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

            if (_active && !_lost && _streamDevice == id) return;

            Stop();
            if (Start(id))
            {
                Report(Status.Redirected, id, "Sound: " + name);
            }
            else
            {
                Report(Status.Failed, id, "Couldn't play the sound on " + name + ", so it plays where Android sends it.", true);
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

            if (AAudio_createStreamBuilder(out var builder) != 0 || builder == IntPtr.Zero) return false;
            IntPtr stream;
            int result;
            try
            {
                AAudioStreamBuilder_setDeviceId(builder, deviceId);
                AAudioStreamBuilder_setDirection(builder, AAUDIO_DIRECTION_OUTPUT);
                AAudioStreamBuilder_setSharingMode(builder, AAUDIO_SHARING_MODE_SHARED);
                AAudioStreamBuilder_setPerformanceMode(builder, AAUDIO_PERFORMANCE_MODE_LOW_LATENCY);
                AAudioStreamBuilder_setSampleRate(builder, rate);
                AAudioStreamBuilder_setChannelCount(builder, channels);
                AAudioStreamBuilder_setFormat(builder, isFloat ? AAUDIO_FORMAT_PCM_FLOAT : AAUDIO_FORMAT_PCM_I16);
                try
                {
                    // Android 9 and later
                    AAudioStreamBuilder_setUsage(builder, AAUDIO_USAGE_GAME);
                    AAudioStreamBuilder_setContentType(builder, AAUDIO_CONTENT_TYPE_MUSIC);
                }
                catch (EntryPointNotFoundException)
                {
                }
                result = AAudioStreamBuilder_openStream(builder, out stream);
            }
            finally
            {
                AAudioStreamBuilder_delete(builder);
            }

            if (result != 0 || stream == IntPtr.Zero)
            {
                YargLogger.LogWarning("Sound output: AAudio could not open device " + deviceId + ": " + result);
                return false;
            }

            int actual = AAudioStream_getDeviceId(stream);
            if (actual != deviceId)
            {
                YargLogger.LogWarning("Sound output: asked for device " + deviceId + ", AAudio gave " + actual);
            }

            // Keep about 25 ms waiting to be played: enough to ride out the uneven way the mix arrives
            int burst = Math.Max(32, AAudioStream_getFramesPerBurst(stream));
            int target = Math.Max(2 * burst, rate / 40);
            AAudioStream_setBufferSizeInFrames(stream, 2 * target + burst);

            if (AAudioStream_requestStart(stream) != 0)
            {
                AAudioStream_close(stream);
                return false;
            }

            if (_zeros.Length < target * bytesPerFrame) _zeros = new byte[target * bytesPerFrame];
            AAudioStream_writeBytes(stream, _zeros, target, 0);

            lock (_lock)
            {
                _stream = stream;
                _streamDevice = deviceId;
                _bytesPerFrame = bytesPerFrame;
                _low = burst;
                _high = target + target / 2;
                _lost = false;
                _adjusted = 0;
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
                " channel(s), " + (isFloat ? "float" : "16-bit") + ", " + target + " frames buffered");
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
                if (_adjusted > 0) YargLogger.LogInfo("Sound output: clock drift evened out by " + _adjusted + " frame(s)");
            }
        }

        // Runs on the sound library's mixing thread with each block of the final mix
        [MonoPInvokeCallback(typeof(DSPProcedure))]
        private static void Redirect(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            if (!_active || length <= 0 || buffer == IntPtr.Zero) return;
            if (!Monitor.TryEnter(_lock)) return;
            try
            {
                if (_stream == IntPtr.Zero || _bytesPerFrame <= 0) return;

                int frames = length / _bytesPerFrame;
                if (frames <= 0) return;

                // The two outputs run on separate clocks; drop or repeat a single frame now and then
                // so the waiting sound neither piles up nor runs out
                long waiting = AAudioStream_getFramesWritten(_stream) - AAudioStream_getFramesRead(_stream);
                IntPtr from = buffer;
                int count = frames;
                if (waiting > _high && count > 1)
                {
                    from = IntPtr.Add(buffer, _bytesPerFrame);
                    count--;
                    _adjusted++;
                }
                else if (waiting < _low)
                {
                    AAudioStream_writePtr(_stream, buffer, 1, 0);
                    _adjusted++;
                }

                int written = AAudioStream_writePtr(_stream, from, count, 0);
                if (written < 0)
                {
                    // The output went away: leave this block (and the rest) on the usual output
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
#endif
    }
}
