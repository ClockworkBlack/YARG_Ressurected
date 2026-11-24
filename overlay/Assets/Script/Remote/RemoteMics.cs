// YARG Resurrected - microphones streamed from other phones' browsers.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using YARG.Audio.PitchDetection;
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

        internal static void Feed(RemoteClient client, byte[] data, int offset, int count)
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
                device.Feed(data, offset, count, sampleRate);
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
    public sealed class RemoteMicDevice : MicDevice
    {
        private const float MIC_HIT_INPUT_THRESHOLD = 25f;

        private readonly object _lock = new object();
        private readonly ConcurrentQueue<MicOutputFrame> _frameQueue = new ConcurrentQueue<MicOutputFrame>();

        private PitchTracker _pitchDetector;
        private int _sampleRate;
        private float[] _chunk;
        private int _chunkFill;

        private float? _lastPitch;
        private float? _lastAmplitude;

        internal RemoteMicDevice(string displayName) : base(displayName)
        {
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
            _pitchDetector = new PitchTracker(sampleRate);
            _chunk = new float[Math.Max(64, sampleRate * RECORD_PERIOD_MS / 1000)];
            _chunkFill = 0;
        }

        internal void Feed(byte[] data, int offset, int count, int sampleRate)
        {
            lock (_lock)
            {
                SetSampleRateLocked(sampleRate);

                // Like BassMicDevice: only analyse once the vocals player asks for output.
                if (!IsRecordingOutput || _chunk == null)
                {
                    _chunkFill = 0;
                    return;
                }

                int samples = count / 2;
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
            }
            ClearOutputQueue();
            return 0;
        }

        public override bool DequeueOutputFrame(out MicOutputFrame frame)
        {
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
