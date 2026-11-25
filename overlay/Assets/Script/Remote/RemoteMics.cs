// YARG Resurrected - microphones streamed from other phones' browsers.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using YARG.Audio.PitchDetection;
using YARG.Audio.Vocals;
using YARG.Core.Audio;
using YARG.Input;
using YARG.Settings;

namespace YARG.Remote
{
    /// <summary>
    /// Keeps track of phone mics and the <see cref="RemoteMicDevice"/>s YARG created for them.
    /// BassAudioManager lists these next to the real microphones.
    /// </summary>
    public static class RemoteMics
    {
        /// <summary>Device ids at or above this are phone mics (BASS uses small indices).</summary>
        public const int ID_BASE = 1 << 24;

        private sealed class Entry
        {
            public RemoteClient Client;
            public string Name;
            public int Number;
            public int SampleRate;
            public bool Active;
            public readonly List<RemoteMicDevice> Devices = new List<RemoteMicDevice>();
            public string DisplayName => DisplayNameFor(Name);
        }

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>();

        // Devices for a saved profile whose phone hasn't connected yet, by display name.
        private static readonly Dictionary<string, List<RemoteMicDevice>> _pendingByName =
            new Dictionary<string, List<RemoteMicDevice>>();

        public static string DisplayNameFor(string phoneName) => "Phone mic: " + phoneName;

        public static bool IsRemoteId(int deviceId) => deviceId >= ID_BASE;

        /// <summary>Number of phones currently streaming their mic.</summary>
        public static int ActiveCount
        {
            get
            {
                lock (_lock)
                {
                    int count = 0;
                    foreach (var entry in _byKey.Values)
                    {
                        if (entry.Active)
                        {
                            count++;
                        }
                    }
                    return count;
                }
            }
        }

        /// <summary>Adds the phone mics that are streaming right now to YARG's microphone list.</summary>
        public static void AppendDevices(List<(int id, string name)> list)
        {
            lock (_lock)
            {
                foreach (var entry in _byKey.Values)
                {
                    if (entry.Active && entry.Client != null && !entry.Client.IsClosed)
                    {
                        list.Add((ID_BASE + entry.Number, entry.DisplayName));
                    }
                }
            }
        }

        public static MicDevice Create(int deviceId, string name)
        {
            lock (_lock)
            {
                foreach (var entry in _byKey.Values)
                {
                    if (ID_BASE + entry.Number == deviceId)
                    {
                        var device = new RemoteMicDevice(entry.DisplayName);
                        device.SetSampleRate(entry.SampleRate);
                        entry.Devices.Add(device);
                        return device;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Used when a saved profile asks for its mic by name. If that phone isn't connected yet,
        /// returns a device that starts working as soon as a phone with that name starts its mic.
        /// Returns null if the name isn't a phone mic.
        /// </summary>
        public static MicDevice FindByName(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith(DisplayNameFor(""), StringComparison.Ordinal))
            {
                return null;
            }

            lock (_lock)
            {
                foreach (var entry in _byKey.Values)
                {
                    if (entry.DisplayName == name)
                    {
                        var device = new RemoteMicDevice(name);
                        device.SetSampleRate(entry.SampleRate);
                        entry.Devices.Add(device);
                        return device;
                    }
                }

                var pending = new RemoteMicDevice(name);
                if (!_pendingByName.TryGetValue(name, out var list))
                {
                    list = new List<RemoteMicDevice>();
                    _pendingByName[name] = list;
                }
                list.Add(pending);
                return pending;
            }
        }

        /// <summary>True if <paramref name="device"/> gets its audio from <paramref name="client"/>.</summary>
        public static bool IsFedBy(RemoteMicDevice device, RemoteClient client)
        {
            lock (_lock)
            {
                return _byKey.TryGetValue(client.Key, out var entry) && entry.Client == client &&
                    entry.Devices.Contains(device);
            }
        }

        internal static void Start(RemoteClient client, int sampleRate)
        {
            lock (_lock)
            {
                if (!_byKey.TryGetValue(client.Key, out var entry))
                {
                    entry = new Entry();
                    _byKey[client.Key] = entry;
                }

                entry.Client = client;
                entry.Name = client.Name;
                entry.Number = client.Number;
                entry.SampleRate = sampleRate;
                entry.Active = true;

                // Attach devices that a saved profile created before this phone connected.
                if (_pendingByName.TryGetValue(entry.DisplayName, out var pending))
                {
                    entry.Devices.AddRange(pending);
                    _pendingByName.Remove(entry.DisplayName);
                }

                foreach (var device in entry.Devices)
                {
                    device.SetSampleRate(sampleRate);
                }
            }
        }

        internal static void Stop(RemoteClient client)
        {
            lock (_lock)
            {
                if (_byKey.TryGetValue(client.Key, out var entry) && entry.Client == client)
                {
                    entry.Active = false;
                }
            }
        }

        /// <summary>The network hold (ms) to assume for a phone mic that is not connected yet.</summary>
        public const double DEFAULT_NETWORK_HOLD_MS = 20;

        /// <param name="firstSample">
        /// The phone's running count of the first sample in this packet, or -1 if the page did
        /// not send one (an older copy of the page). It shows where packets were lost.
        /// </param>
        internal static void Feed(RemoteClient client, byte[] data, int offset, int count, long firstSample = -1)
        {
            RemoteMicDevice[] devices;
            int sampleRate;
            lock (_lock)
            {
                if (!_byKey.TryGetValue(client.Key, out var entry) || entry.Client != client ||
                    !entry.Active || entry.Devices.Count == 0)
                {
                    return;
                }
                devices = entry.Devices.ToArray();
                sampleRate = entry.SampleRate;
            }

            foreach (var device in devices)
            {
                device.Feed(data, offset, count, sampleRate, firstSample);
            }
        }

        internal static void Remove(RemoteMicDevice device)
        {
            lock (_lock)
            {
                foreach (var entry in _byKey.Values)
                {
                    entry.Devices.Remove(device);
                }
                foreach (var list in _pendingByName.Values)
                {
                    list.Remove(device);
                }
            }
        }
    }

    /// <summary>
    /// A microphone whose audio comes from a phone's browser (16-bit mono PCM over the WebSocket).
    /// Pitch and volume detection match <c>BassMicDevice</c>, so scoring behaves the same.
    /// </summary>
    public sealed class RemoteMicDevice : MicDevice, IVoiceMic
    {
        private const float MIC_HIT_INPUT_THRESHOLD = 25f;

        // The original pitch tracker cannot work below this rate (it would never finish)
        private const int CLASSIC_MIN_SAMPLE_RATE = 16000;

        private readonly object _lock = new object();
        private readonly ConcurrentQueue<MicOutputFrame> _frameQueue = new ConcurrentQueue<MicOutputFrame>();

        private PitchTracker _pitchDetector;
        private int _sampleRate;
        private float[] _chunk;
        private int _chunkFill;

        private float? _lastPitch;
        private float? _lastAmplitude;

        // The new voice analysis (see VoiceInput.cs), used while "Improved Singing Detection"
        // is on. With it off, the code below works as it did before.
        private VoiceInput _voiceInput;
        private readonly List<VoiceInputFrame> _voiceFrames = new List<VoiceInputFrame>();
        private float[] _voiceSamples = new float[1024];
        private bool _useVoiceInput;
        private volatile bool _gateFrames;
        private bool _strict;
        private double _holdSeconds;
        private long _nextSample = -1;

        internal RemoteMicDevice(string displayName) : base(displayName)
        {
        }

        /// <summary>
        /// Called by the vocals player when a song starts (main thread). See <see cref="IVoiceMic"/>.
        /// </summary>
        public double BeginSong(bool strict)
        {
            bool improved = SettingsManager.Settings.ImprovedSingingDetection.Value;
            double extra = SettingsManager.Settings.MicrophoneDelay.Value / 1000.0;
            double networkHoldMs = RemotePlayManager.GetMicNetworkHoldMs(this);

            lock (_lock)
            {
                _strict = strict;
                _useVoiceInput = improved;
                _gateFrames = improved;
                _nextSample = -1;

                if (!improved)
                {
                    // As before: the mic's usual network delay plus one 10 ms packet
                    return (networkHoldMs + 10) / 1000.0 + extra;
                }

                double analysis = _voiceInput != null ? _voiceInput.AnalysisSeconds : 0.021;
                _holdSeconds = VoiceMicTiming.RemoteHoldSeconds(analysis, networkHoldMs);
                if (_voiceInput != null)
                {
                    _voiceInput.Reset();
                    _voiceInput.Strict = strict;
                    _voiceInput.HoldSeconds = _holdSeconds;
                }

                return _holdSeconds + VoiceMicTiming.BROWSER_CAPTURE_SECONDS + extra;
            }
        }

        internal void SetSampleRate(int sampleRate)
        {
            lock (_lock)
            {
                SetSampleRateLocked(sampleRate);
            }
        }

        private void SetSampleRateLocked(int sampleRate)
        {
            if (sampleRate <= 0 || sampleRate == _sampleRate)
            {
                return;
            }

            _sampleRate = sampleRate;
            _pitchDetector = sampleRate >= CLASSIC_MIN_SAMPLE_RATE ? new PitchTracker(sampleRate) : null;
            _chunk = new float[Math.Max(64, sampleRate * RECORD_PERIOD_MS / 1000)];
            _chunkFill = 0;

            _voiceInput = new VoiceInput(sampleRate)
            {
                Strict = _strict,
                HoldSeconds = _holdSeconds
            };
            _nextSample = -1;
        }

        internal void Feed(byte[] data, int offset, int count, int sampleRate, long firstSample = -1)
        {
            lock (_lock)
            {
                SetSampleRateLocked(sampleRate);

                // Like BassMicDevice: only analyse once the vocals player asks for output.
                if (!IsRecordingOutput || _chunk == null)
                {
                    _chunkFill = 0;
                    _nextSample = -1;
                    return;
                }

                int samples = count / 2;

                if (_useVoiceInput || _pitchDetector == null)
                {
                    FeedVoiceInput(data, offset, samples, firstSample);
                    return;
                }

                for (int i = 0; i < samples; i++)
                {
                    int index = offset + i * 2;
                    short sample = (short) (data[index] | (data[index + 1] << 8));
                    _chunk[_chunkFill++] = sample / 32768f;

                    if (_chunkFill == _chunk.Length)
                    {
                        CalculatePitchAndAmplitude(_chunk);
                        _chunkFill = 0;
                    }
                }
            }
        }

        // The new analysis. Every result is stamped with the moment its sound arrived, counted
        // in samples, so bursts and jitter on the Wi-Fi do not move it; packets the phone had
        // to drop are counted as silence.
        private void FeedVoiceInput(byte[] data, int offset, int samples, long firstSample)
        {
            double now = InputManager.CurrentInputTime;

            if (samples <= 0 || _voiceInput == null)
            {
                return;
            }

            // Packets the phone dropped (it counts its samples) are fed as silence, which
            // keeps the timing of everything after them right
            long missing = VoiceMicTiming.MissingSamples(_nextSample, firstSample);
            if (firstSample >= 0)
            {
                _nextSample = (firstSample + samples) & 0xFFFFFFFFL;
            }

            if (_voiceSamples.Length < samples)
            {
                _voiceSamples = new float[samples];
            }

            for (int i = 0; i < samples; i++)
            {
                int index = offset + i * 2;
                _voiceSamples[i] = (short) (data[index] | (data[index + 1] << 8)) / 32768f;
            }

            _voiceFrames.Clear();
            _voiceInput.Feed(new ReadOnlySpan<float>(_voiceSamples, 0, samples), now,
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

        public override int Reset()
        {
            lock (_lock)
            {
                _chunkFill = 0;
                _lastPitch = null;
                _lastAmplitude = null;
                _pitchDetector?.Reset();
                _voiceInput?.Reset();
                _nextSample = -1;
            }
            ClearOutputQueue();
            return 0;
        }

        public override bool DequeueOutputFrame(out MicOutputFrame frame)
        {
            // The new analysis stamps a frame with the moment it is due (see VoiceInput), which
            // is a little ahead: hand it over when that moment has come.
            // (Only the game's main thread takes frames out, so peeking first is safe.)
            if (_gateFrames)
            {
                if (!_frameQueue.TryPeek(out frame) || frame.Time > InputManager.CurrentInputTime)
                {
                    frame = default;
                    return false;
                }
            }

            return _frameQueue.TryDequeue(out frame);
        }

        public override void ClearOutputQueue()
        {
            while (_frameQueue.TryDequeue(out _))
            {
            }
        }

        public override void SetMonitoringLevel(float volume)
        {
            // Hearing your own voice back isn't supported for phone mics (Wi-Fi delay would make it echo).
        }

        public override SerializedMic Serialize()
        {
            return new SerializedMic(DisplayName);
        }

        protected override void DisposeManagedResources()
        {
            RemoteMics.Remove(this);
        }
    }
}
