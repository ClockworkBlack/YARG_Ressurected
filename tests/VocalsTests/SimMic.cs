// The microphone, as the tests model it: audio reaches the game late and in bursts, and comes
// out the other end as timed pitch inputs. Two versions: the one this repo adds (VoiceInput),
// and YARG's original way of doing it, for comparison.
using System;
using System.Collections.Generic;
using YARG.Audio.PitchDetection;
using YARG.Audio.Vocals;

namespace VocalsTests
{
    /// <summary>One input as the engine receives it (time already on the song's clock).</summary>
    public struct SimInput
    {
        public double Time;
        public bool IsHit;
        public float Midi; // below 0: sound without a tone
    }

    public sealed class MicSetup
    {
        /// <summary>Seconds from a sound being made to its audio reaching the game.</summary>
        public double Latency = 0.030;

        /// <summary>Extra random delay of each delivery, up to this many seconds.</summary>
        public double Jitter = 0.004;

        /// <summary>How much audio arrives at once (samples).</summary>
        public int Block = 480;

        /// <summary>The Microphone Sensitivity setting.</summary>
        public float SensitivityDb = 2f;

        /// <summary>Chance that a block is lost on the way (phones over Wi-Fi).</summary>
        public double DropChance;

        /// <summary>The phone's built-in mic, as this repo assumes it.</summary>
        public static MicSetup Phone => new MicSetup();

        /// <summary>Another phone's browser over Wi-Fi: later, burstier, sometimes lossy.</summary>
        public static MicSetup WiFi => new MicSetup { Latency = 0.070, Jitter = 0.030, DropChance = 0.01 };
    }

    public static class SimMic
    {
        /// <summary>
        /// This repo's microphone path. <paramref name="hold"/> and <paramref name="assumedLatency"/>
        /// are what the game believes; the engine sees each input at its stamped time minus both.
        /// </summary>
        public static List<SimInput> Improved(float[] audio, int rate, MicSetup setup, bool strict,
            double hold, double assumedLatency, int seed)
        {
            var random = new Random(seed);
            var input = new VoiceInput(rate) { Strict = strict, HoldSeconds = hold };
            var frames = new List<VoiceInputFrame>();
            var result = new List<SimInput>();

            double now = 0;
            long missing = 0;
            for (int i = 0; i < audio.Length; i += setup.Block)
            {
                int count = Math.Min(setup.Block, audio.Length - i);
                if (setup.DropChance > 0 && random.NextDouble() < setup.DropChance)
                {
                    missing += count;
                    continue;
                }

                double arrival = (double) (i + count) / rate + setup.Latency + random.NextDouble() * setup.Jitter;
                now = Math.Max(now, arrival);

                frames.Clear();
                input.Feed(new ReadOnlySpan<float>(audio, i, count), now, setup.SensitivityDb, frames, missing);
                missing = 0;

                foreach (var frame in frames)
                {
                    result.Add(new SimInput
                    {
                        // The game hands a frame to the engine when its time has come; one
                        // that is already late goes in "now"
                        Time = Math.Max(frame.Time, now) - hold - assumedLatency,
                        IsHit = frame.IsHit,
                        Midi = frame.IsHit ? 0f : (frame.PitchHz > 0f ? VoiceAnalyzer.HzToMidi(frame.PitchHz) : -1f),
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// YARG's original microphone path: 40 ms of audio at a time, the original pitch tracker,
        /// the last pitch repeated while there is sound but no pitch, and each result stamped
        /// with the moment it was worked out.
        /// </summary>
        public static List<SimInput> Classic(float[] audio, int rate, MicSetup setup, double compensation, int seed)
        {
            var random = new Random(seed);
            var tracker = new PitchTracker(rate);
            var result = new List<SimInput>();

            int chunk = rate * 40 / 1000;
            var buffer = new float[chunk];
            int fill = 0;
            float? lastPitch = null;
            float? lastLevel = null;
            double now = 0;

            for (int i = 0; i < audio.Length; i += setup.Block)
            {
                int count = Math.Min(setup.Block, audio.Length - i);
                double arrival = (double) (i + count) / rate + setup.Latency + random.NextDouble() * setup.Jitter;
                now = Math.Max(now, arrival);

                if (setup.DropChance > 0 && random.NextDouble() < setup.DropChance)
                {
                    continue; // the original has no idea audio went missing
                }

                for (int k = 0; k < count; k++)
                {
                    buffer[fill++] = audio[i + k];
                    if (fill < chunk)
                    {
                        continue;
                    }
                    fill = 0;

                    float sum = 0f;
                    int n = 0;
                    for (int s = 0; s < chunk; s += 4, n++)
                    {
                        sum += buffer[s] * buffer[s];
                    }
                    float level = 20f * MathF.Log10(MathF.Sqrt(sum / n) * 180f);
                    if (level < -160f)
                    {
                        level = -160f;
                    }

                    if (level > lastLevel && MathF.Abs(level - lastLevel.Value) >= 25f)
                    {
                        result.Add(new SimInput { Time = now - compensation, IsHit = true });
                    }
                    lastLevel = level;

                    if (level < setup.SensitivityDb)
                    {
                        lastPitch = null;
                        continue;
                    }

                    var pitch = tracker.ProcessBuffer(buffer);
                    if (pitch != null)
                    {
                        lastPitch = pitch;
                    }
                    if (lastPitch == null)
                    {
                        continue;
                    }

                    result.Add(new SimInput { Time = now - compensation, Midi = VoiceAnalyzer.HzToMidi(lastPitch.Value) });
                }
            }

            return result;
        }
    }
}
