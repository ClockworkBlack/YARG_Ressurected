// YARG Resurrected - microphones recorded through Android itself, so two can be used at once.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using YARG.Audio.PitchDetection;
using YARG.Audio.Vocals;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Input;
using YARG.Menu.Persistent;
using YARG.Settings;

namespace YARG.Audio
{
    /// <summary>
    /// The game's sound library records from one microphone only: the one Android prefers at the
    /// moment, which is a USB mic whenever one is plugged in. For two singers on one phone (one on
    /// a USB mic, one on the phone's own) each microphone is opened here through Android's own
    /// recorder instead, which can be told exactly which input to listen to
    /// (see YargMicCapture.java).
    ///
    /// They are offered in a profile's Add Device list next to "This phone's mic":
    ///   "Built-in mic"        the phone's own microphone, even with a USB mic plugged in
    ///   "USB mic: (name)"     a microphone on USB (or a USB-C adapter)
    ///   "Headset mic (plug)"  the mic of a wired headset
    /// A profile remembers its microphone by that name.
    /// </summary>
    public static class AndroidMics
    {
        /// <summary>Device ids at or above this are Android microphones (above the phone-remote mics' ids).</summary>
        public const int ID_BASE = 1 << 25;

        public const int KIND_BUILT_IN = 0;
        public const int KIND_USB = 1;
        public const int KIND_WIRED = 2;

        public const string BUILT_IN_NAME = "Built-in mic";
        public const string WIRED_NAME = "Headset mic (plug)";
        public const string USB_PREFIX = "USB mic: ";

        private const string JAVA_CLASS = "in.yarg.resurrected.YargMicCapture";

        public static bool IsAndroidId(int deviceId) => deviceId >= ID_BASE;

        /// <summary>The name a microphone goes by in the game.</summary>
        public static string DisplayName(int kind, string name)
        {
            return kind == KIND_BUILT_IN ? BUILT_IN_NAME : kind == KIND_WIRED ? WIRED_NAME : USB_PREFIX + name;
        }

        /// <summary>Adds the microphones Android can record from right now to the game's microphone list.</summary>
        public static void AppendDevices(List<(int id, string name)> list)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                string[] lines;
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var capture = new AndroidJavaClass(JAVA_CLASS))
                {
                    if (!capture.CallStatic<bool>("isSupported"))
                    {
                        return;
                    }
                    lines = capture.CallStatic<string[]>("listInputs", activity);
                }

                if (lines == null)
                {
                    return;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    if (TryParse(lines[i], out int kind, out string name))
                    {
                        list.Add((ID_BASE + i, DisplayName(kind, name)));
                    }
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't list Android's microphones");
            }
#endif
        }

        private static bool TryParse(string line, out int kind, out string name)
        {
            kind = -1;
            name = string.Empty;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            int bar = line.IndexOf('|');
            if (bar <= 0 || !int.TryParse(line.Substring(0, bar), out kind))
            {
                return false;
            }

            name = line.Substring(bar + 1);
            return kind >= KIND_BUILT_IN && kind <= KIND_WIRED;
        }

        /// <summary>
        /// The microphone that goes by <paramref name="name"/>, or null when the name isn't one of
        /// ours. It doesn't have to be connected yet: it starts recording as soon as it is.
        /// </summary>
        public static MicDevice FindByName(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (name == BUILT_IN_NAME)
            {
                return new AndroidMicDevice(name, KIND_BUILT_IN, string.Empty);
            }

            if (name == WIRED_NAME)
            {
                return new AndroidMicDevice(name, KIND_WIRED, string.Empty);
            }

            if (name.StartsWith(USB_PREFIX, StringComparison.Ordinal))
            {
                return new AndroidMicDevice(name, KIND_USB, name.Substring(USB_PREFIX.Length));
            }
#endif
            return null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        internal static AndroidJavaObject Open(int kind, string name, out string error)
        {
            error = string.Empty;
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var capture = new AndroidJavaClass(JAVA_CLASS))
            {
                var opened = capture.CallStatic<AndroidJavaObject>("open", activity, kind, name ?? string.Empty,
                    MicDevice.RECORD_PERIOD_MS);
                if (opened == null)
                {
                    error = capture.CallStatic<string>("getLastError");
                }
                return opened;
            }
        }
#endif
    }

    /// <summary>
    /// One microphone recorded through Android. The sound is fetched and listened to (pitch and
    /// loudness, exactly as for the game's other microphones) each time the game asks for the
    /// singer's input, which it does once a frame during a song.
    /// </summary>
    public sealed class AndroidMicDevice : MicDevice, IVoiceMic
    {
        private const float MIC_HIT_INPUT_THRESHOLD = 25f;
        private const float RETRY_SECONDS = 2f;

        // Sound reaches the game a chunk (40 ms) at a time, and is fetched once a video frame
        private const int DELIVERY_MS = RECORD_PERIOD_MS + 17;

        private readonly int _kind;
        private readonly string _name;
        private readonly ConcurrentQueue<MicOutputFrame> _frameQueue = new ConcurrentQueue<MicOutputFrame>();

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _capture;
#endif
        private PitchTracker _pitchDetector;
        private float[] _chunk;
        private float _nextOpen;
        private int _lastPumpFrame = -1;
        private bool _warnedRouting;
        private bool _warnedOpen;
        private float _openedAt;

        private float? _lastPitch;
        private float? _lastAmplitude;

        // The new voice analysis (see Vocals/VoiceInput.cs), used while "Improved Singing
        // Detection" is on. With it off, this class listens the way it always did.
        private VoiceInput _voiceInput;
        private readonly List<VoiceInputFrame> _voiceFrames = new List<VoiceInputFrame>();
        private float[] _voiceSamples = new float[2048];
        private bool _useVoiceInput;
        private bool _strict;
        private double _holdSeconds;

        // The recorder's count of the sample the next chunk should begin at (below 0: not known).
        // The recorder throws sound away when the game was too busy to fetch it; a jump in the
        // count says how much, so the timing of everything after it stays right.
        private long _nextSample = -1;

        internal AndroidMicDevice(string displayName, int kind, string name) : base(displayName)
        {
            _kind = kind;
            _name = name;
        }

        /// <summary>
        /// Called by the vocals player when a song starts (main thread). See <see cref="IVoiceMic"/>.
        /// </summary>
        public double BeginSong(bool strict)
        {
            double extra = SettingsManager.Settings.MicrophoneDelay.Value / 1000.0;

            _strict = strict;
            _useVoiceInput = SettingsManager.Settings.ImprovedSingingDetection.Value;
            if (!_useVoiceInput)
            {
                return extra;
            }

            double analysis = _voiceInput != null ? _voiceInput.AnalysisSeconds : 0.021;
            _holdSeconds = VoiceMicTiming.DeviceHoldSeconds(analysis, DELIVERY_MS);
            if (_voiceInput != null)
            {
                _voiceInput.Reset();
                _voiceInput.Strict = strict;
                _voiceInput.HoldSeconds = _holdSeconds;
            }
            _nextSample = -1;

            return _holdSeconds + VoiceMicTiming.DEVICE_CAPTURE_SECONDS + extra;
        }

        /// <summary>Opens the microphone if it isn't open (or went away), trying again every couple of seconds.</summary>
        private bool EnsureOpen()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_capture != null)
            {
                bool dead = false;
                try
                {
                    dead = _capture.Call<bool>("isDead");
                }
                catch (Exception)
                {
                    dead = true;
                }

                if (!dead)
                {
                    return true;
                }

                Close();
            }

            if (Time.unscaledTime < _nextOpen)
            {
                return false;
            }
            _nextOpen = Time.unscaledTime + RETRY_SECONDS;

            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                return false;
            }

            try
            {
                _capture = AndroidMics.Open(_kind, _name, out string error);
                if (_capture == null)
                {
                    if (!_warnedOpen)
                    {
                        _warnedOpen = true;
                        // (Joined by hand: with two strings to fill in, the logger can't tell its
                        // one-value form with a file name from its two-value form)
                        YargLogger.LogWarning("Couldn't open microphone '" + DisplayName + "': " + error);
                    }
                    return false;
                }

                int sampleRate = _capture.Call<int>("getSampleRate");
                int chunkSamples = _capture.Call<int>("getChunkSamples");
                _pitchDetector = new PitchTracker(sampleRate);
                _chunk = new float[Math.Max(64, chunkSamples)];
                _voiceInput = sampleRate >= 8000
                    ? new VoiceInput(sampleRate) { Strict = _strict, HoldSeconds = _holdSeconds }
                    : null;
                _nextSample = -1;
                _openedAt = Time.unscaledTime;
                _warnedOpen = false;
                _lastPitch = null;
                _lastAmplitude = null;
                return true;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't open an Android microphone");
                Close();
                return false;
            }
#else
            return false;
#endif
        }

        private void Close()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_capture == null)
            {
                return;
            }

            try
            {
                _capture.Call("close");
            }
            catch (Exception)
            {
                // Already gone
            }

            _capture.Dispose();
            _capture = null;
#endif
        }

        /// <summary>Fetches the sound recorded since the last frame and listens to it.</summary>
        private void Pump()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Once a frame is plenty; the game asks until the queue is empty
            if (_lastPumpFrame == Time.frameCount)
            {
                return;
            }
            _lastPumpFrame = Time.frameCount;

            if (!EnsureOpen())
            {
                return;
            }

            try
            {
                int waiting = _capture.Call<int>("available");
                for (int i = 0; i < waiting && i < 8; i++)
                {
                    short[] samples = _capture.Call<short[]>("take");
                    if (samples == null || samples.Length == 0)
                    {
                        break;
                    }

                    // Like the game's other microphones: only listened to while a song wants it
                    if (!IsRecordingOutput)
                    {
                        continue;
                    }

                    if (_useVoiceInput && _voiceInput != null)
                    {
                        ListenWithVoiceInput(samples, _capture.Call<long>("getLastTakeFirst"));
                        continue;
                    }

                    int count = Math.Min(samples.Length, _chunk.Length);
                    for (int s = 0; s < count; s++)
                    {
                        _chunk[s] = samples[s] / 32768f;
                    }
                    for (int s = count; s < _chunk.Length; s++)
                    {
                        _chunk[s] = 0f;
                    }

                    CalculatePitchAndAmplitude(_chunk);
                }

                // Some phones can only listen to one microphone at a time, and quietly feed every
                // recorder from the same one. Say so, once, rather than leave two singers puzzled.
                if (!_warnedRouting && Time.unscaledTime > _openedAt + 1.5f)
                {
                    _warnedRouting = true;
                    if (!_capture.Call<bool>("isRoutedAsAsked"))
                    {
                        ToastManager.ToastWarning("Android is not recording \"" + DisplayName +
                            "\" from that microphone. This phone may only be able to use one microphone at a time.");
                    }
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Reading an Android microphone failed");
                Close();
            }
#endif
        }

        // The new analysis: every result is stamped with the moment its sound arrived, counted
        // in samples, so it does not matter that the sound is fetched a video frame at a time.
        private void ListenWithVoiceInput(short[] samples, long firstSample)
        {
            // Sound the recorder threw away since the last chunk (the game stuttered, or was
            // not listening for a moment) is counted as silence. Below zero: the count started
            // again, and so does the timing.
            long missing = 0;
            if (firstSample >= 0)
            {
                if (_nextSample >= 0)
                {
                    missing = firstSample >= _nextSample ? firstSample - _nextSample : -1;
                }

                _nextSample = firstSample + samples.Length;
            }

            if (_voiceSamples.Length < samples.Length)
            {
                _voiceSamples = new float[samples.Length];
            }

            for (int s = 0; s < samples.Length; s++)
            {
                _voiceSamples[s] = samples[s] / 32768f;
            }

            _voiceFrames.Clear();
            _voiceInput.Feed(new ReadOnlySpan<float>(_voiceSamples, 0, samples.Length), InputManager.CurrentInputTime,
                SettingsManager.Settings.MicrophoneSensitivity.Value, _voiceFrames, missing);

            for (int i = 0; i < _voiceFrames.Count; i++)
            {
                var frame = _voiceFrames[i];

                // A pitch of 0 means "sound, but no sung tone" (see MicInputContext)
                _frameQueue.Enqueue(new MicOutputFrame(frame.Time, frame.IsHit,
                    frame.IsHit ? -1f : frame.PitchHz, frame.LevelDb));
            }
        }

        // Same as BassMicDevice.CalculatePitchAndAmplitude, but the samples are already floats.
        private void CalculatePitchAndAmplitude(float[] buffer)
        {
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < buffer.Length; i += 4, count++)
            {
                sum += buffer[i] * buffer[i];
            }
            sum = Mathf.Sqrt(sum / count);

            float amplitude = 20f * Mathf.Log10(sum * 180f);
            if (amplitude < -160f)
            {
                amplitude = -160f;
            }

            if (amplitude > _lastAmplitude && Mathf.Abs(amplitude - _lastAmplitude.Value) >= MIC_HIT_INPUT_THRESHOLD)
            {
                _frameQueue.Enqueue(new MicOutputFrame(InputManager.CurrentInputTime, true, -1f, -1f));
            }
            _lastAmplitude = amplitude;

            if (amplitude < SettingsManager.Settings.MicrophoneSensitivity.Value)
            {
                _lastPitch = null;
                return;
            }

            var pitchOutput = _pitchDetector.ProcessBuffer(buffer);
            if (pitchOutput != null)
            {
                _lastPitch = pitchOutput;
            }

            if (_lastPitch == null)
            {
                return;
            }

            _frameQueue.Enqueue(new MicOutputFrame(InputManager.CurrentInputTime, false, _lastPitch.Value, amplitude));
        }

        private void Flush()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _capture?.Call("flush");
            }
            catch (Exception)
            {
                // Nothing waiting to forget
            }
#endif
        }

        public override int Reset()
        {
            _lastPitch = null;
            _lastAmplitude = null;
            _pitchDetector?.Reset();
            _voiceInput?.Reset();
            _nextSample = -1;
            // Have the microphone running before the first line, not after it
            EnsureOpen();
            Flush();
            ClearOutputQueue();
            return 0;
        }

        public override bool DequeueOutputFrame(out MicOutputFrame frame)
        {
            Pump();

            // The new analysis stamps a frame with the moment it is due (see VoiceInput), which
            // can be a little ahead: hand it over when that moment has come
            if (_useVoiceInput)
            {
                if (!_frameQueue.TryPeek(out frame) || frame.Time > InputManager.InputUpdateTime)
                {
                    frame = default;
                    return false;
                }
            }

            return _frameQueue.TryDequeue(out frame);
        }

        public override void ClearOutputQueue()
        {
            Flush();
            while (_frameQueue.TryDequeue(out _))
            {
            }
        }

        public override void SetMonitoringLevel(float volume)
        {
            // Hearing your own voice back isn't offered for these microphones.
        }

        public override SerializedMic Serialize()
        {
            return new SerializedMic(DisplayName);
        }

        protected override void DisposeManagedResources()
        {
            Close();
        }
    }
}
