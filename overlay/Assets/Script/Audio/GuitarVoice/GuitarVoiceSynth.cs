// Guitar voice, part 2: play the chart on an electric guitar (or a bass) and hand back the recording.
//
// Plain .NET only (no Unity, no BASS) so the automated tests can run it.
//
// The sound is modelled rather than sampled:
//   - each note is a plucked string (a delay line the length of one vibration, with a filter
//     in the loop that lets high overtones die away first, the way a real string does);
//   - where the pick strikes shapes the tone, long notes get a little finger vibrato,
//     hammer-ons are softer than picked notes, and chords are strummed, not struck at once;
//   - all strings go through one amplifier (so chords growl like a real overdriven amp),
//     a speaker-cabinet filter and a touch of room.

using System;

namespace YARG.Audio.GuitarVoice
{
    public enum GuitarVoiceTone
    {
        Clean,
        Rock,
        Heavy,
    }

    public static class GuitarVoiceSynth
    {
        public const int SAMPLE_RATE = 44100;

        /// <summary>
        /// Renders the notes to stereo audio (left and right samples alternating).
        /// </summary>
        /// <param name="cents">Tuning of the song relative to A = 440 Hz.</param>
        /// <param name="targetRms">Loudness to aim for while notes are sounding.</param>
        public static float[] Render(VoiceNote[] notes, double totalSeconds, GuitarVoiceTone tone, bool bass,
            float cents, float targetRms)
        {
            double drive, bright, sustain;
            if (bass)
            {
                drive = 1.2; bright = 0.25; sustain = 1.0;
            }
            else if (tone == GuitarVoiceTone.Clean)
            {
                drive = 1.6; bright = 0.75; sustain = 1.0;
            }
            else if (tone == GuitarVoiceTone.Heavy)
            {
                drive = 18.0; bright = 0.5; sustain = 1.8;
            }
            else
            {
                drive = 6.0; bright = 0.6; sustain = 1.3;
            }

            int n = Math.Max(0, (int) (totalSeconds * SAMPLE_RATE));
            var y = new float[n];
            if (n == 0)
            {
                return new float[0];
            }

            uint rng = 12345;
            double tune = Math.Pow(2, cents / 1200.0);
            int strumGap = (int) (0.006 * SAMPLE_RATE);

            for (int i = 0; i < notes.Length; i++)
            {
                var note = notes[i];
                int start = (int) (note.Time * SAMPLE_RATE);
                if (start < 0 || start >= n)
                {
                    continue;
                }

                // A sustain rings for its length. A plain note rings until the next one (up to 1.2 s).
                double untilNext = i + 1 < notes.Length ? notes[i + 1].Time - note.Time : 4.0;
                double held = note.Length > 0.12
                    ? note.Length
                    : Math.Min(Math.Max(untilNext, 0.08), 1.2);

                int strings = bass || note.FretCount <= 1 ? 1 : note.FretCount == 2 ? 2 : 3;
                for (int s = 0; s < strings; s++)
                {
                    // Power chord: root, fifth, octave
                    int pitch = note.Pitch + (s == 1 ? 7 : s == 2 ? 12 : 0);
                    double freq = 440.0 * Math.Pow(2, (pitch - 69) / 12.0) * tune;
                    double t60 = (bass ? 5.0 : 3.5) * sustain * Math.Pow(110.0 / freq, 0.25);
                    // High strings are thinner and quieter; make up some of the difference
                    double lift = Math.Min(1.6, Math.Max(1.0, Math.Pow(freq / 110.0, 0.25)));
                    double velocity = (note.Soft ? 0.6 : 1.0) * (s == 0 ? 1.0 : 0.8) * lift;
                    Pluck(y, start + s * strumGap, freq, held, velocity, bright, t60, ref rng,
                        note.Soft, strings == 1);
                }
            }

            var stereo = new float[n * 2];
            if (bass)
            {
                BassAmp(y, drive);
                for (int i = 0; i < n; i++)
                {
                    stereo[2 * i] = y[i];
                    stereo[2 * i + 1] = y[i];
                }
            }
            else
            {
                GuitarAmp(y, drive);
                Room(y, stereo, 0.10f);
            }

            Level(stereo, targetRms);
            return stereo;
        }

        /// <summary>
        /// Renders each note on its own, as a short mono 16-bit recording, for playing live
        /// (freestyle). Only <see cref="VoiceNote.Pitch"/>, <see cref="VoiceNote.FretCount"/> and
        /// <see cref="VoiceNote.Soft"/> are used. All recordings go through the same amplifier
        /// settings and come out balanced against each other.
        /// </summary>
        public static short[][] RenderBank(VoiceNote[] notes, double seconds, GuitarVoiceTone tone, bool bass,
            float cents, float targetRms)
        {
            // Laid end to end with a gap, played once, then cut apart
            double slot = seconds + 0.5;
            var sequence = new VoiceNote[notes.Length];
            for (int i = 0; i < notes.Length; i++)
            {
                sequence[i] = notes[i];
                sequence[i].Time = i * slot;
                sequence[i].Length = seconds - 0.2;
            }

            var audio = Render(sequence, notes.Length * slot, tone, bass, cents, targetRms);
            int frames = (int) (seconds * SAMPLE_RATE);
            int fade = (int) (0.03 * SAMPLE_RATE);
            var bank = new short[notes.Length][];
            for (int i = 0; i < notes.Length; i++)
            {
                int start = (int) (i * slot * SAMPLE_RATE);
                var one = new short[frames];
                for (int f = 0; f < frames; f++)
                {
                    int at = (start + f) * 2;
                    if (at + 1 >= audio.Length)
                    {
                        break;
                    }
                    float v = 0.5f * (audio[at] + audio[at + 1]);
                    if (f >= frames - fade)
                    {
                        v *= (frames - f) / (float) fade;
                    }
                    v = v > 1f ? 1f : v < -1f ? -1f : v;
                    one[f] = (short) Math.Round(v * 32767f);
                }
                bank[i] = one;
            }
            return bank;
        }

        /// <summary>16-bit WAV file. With two channels the samples alternate left, right.</summary>
        public static byte[] ToWav(float[] samples, int sampleRate, int channels)
        {
            int dataBytes = samples.Length * 2;
            var wav = new byte[44 + dataBytes];
            void Text(int at, string s)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    wav[at + i] = (byte) s[i];
                }
            }
            void Int(int at, int v, int bytes)
            {
                for (int i = 0; i < bytes; i++)
                {
                    wav[at + i] = (byte) (v >> (8 * i));
                }
            }

            Text(0, "RIFF");
            Int(4, 36 + dataBytes, 4);
            Text(8, "WAVE");
            Text(12, "fmt ");
            Int(16, 16, 4);
            Int(20, 1, 2);              // PCM
            Int(22, channels, 2);
            Int(24, sampleRate, 4);
            Int(28, sampleRate * 2 * channels, 4); // bytes per second
            Int(32, 2 * channels, 2);              // bytes per frame
            Int(34, 16, 2);             // bits
            Text(36, "data");
            Int(40, dataBytes, 4);

            for (int i = 0; i < samples.Length; i++)
            {
                float v = samples[i];
                if (v > 1f)
                {
                    v = 1f;
                }
                else if (v < -1f)
                {
                    v = -1f;
                }
                int s = (int) Math.Round(v * 32767f);
                wav[44 + i * 2] = (byte) s;
                wav[45 + i * 2] = (byte) (s >> 8);
            }
            return wav;
        }

        private static double Noise(ref uint state)
        {
            uint s = state;
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            state = s;
            return s / 2147483648.0 - 1.0;
        }

        // One plucked string, added into output from `start`. After `held` seconds it is damped.
        private static void Pluck(float[] output, int start, double freq, double held, double velocity,
            double bright, double t60, ref uint rng, bool soft, bool vibrato)
        {
            double period = SAMPLE_RATE / freq;
            int length = (int) Math.Round(period);
            if (length < 4 || start >= output.Length)
            {
                return;
            }

            int size = 16;
            while (size < length + 8)
            {
                size <<= 1;
            }
            int mask = size - 1;
            var buffer = new double[size];

            // The pluck. A string pulled aside by a pick and let go has a known shape, and a guitar
            // pickup hears how fast the string moves: over one vibration that is a pulse as wide as
            // the distance from the bridge to the pick (a fifth of the string here). This gives the
            // real instrument's overtones: strongest at the bottom, falling away evenly, with every
            // fifth one missing. A pinch of noise keeps two plucks from being identical, and the
            // corners are rounded by how hard the pick is (softer for hammer-ons).
            const double PICK_POSITION = 0.2;
            int pick = Math.Max(1, (int) Math.Round(PICK_POSITION * length));
            var burst = new double[length];
            for (int i = 0; i < length; i++)
            {
                burst[i] = (i < pick ? 1 - PICK_POSITION : -PICK_POSITION) + 0.04 * Noise(ref rng);
            }
            double cutoff = soft ? 1200.0 : 2200.0 + 3800.0 * bright;
            double k = 1 - Math.Exp(-2 * Math.PI * cutoff / SAMPLE_RATE);
            // Smoothed forwards then backwards, twice around the loop each way, so the shape
            // stays symmetrical and has no seam where the loop closes
            double lp = burst[length - 1];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < length; i++)
                {
                    lp += k * (burst[i] - lp);
                    burst[i] = lp;
                }
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = length - 1; i >= 0; i--)
                {
                    lp += k * (burst[i] - lp);
                    burst[i] = lp;
                }
            }
            double mean = 0;
            for (int i = 0; i < length; i++)
            {
                mean += burst[i];
            }
            mean /= length;

            double peak = 0, power = 0;
            for (int i = 0; i < length; i++)
            {
                double v = burst[i] - mean;
                buffer[i] = v;
                power += v * v;
                if (Math.Abs(v) > peak)
                {
                    peak = Math.Abs(v);
                }
            }
            if (peak <= 0)
            {
                return;
            }
            // Every note equally loud whatever its pitch, without any single spike over full scale
            double rms = Math.Sqrt(power / length);
            double scale = Math.Min(velocity * 0.30 / rms, velocity * 1.2 / peak);
            for (int i = 0; i < length; i++)
            {
                buffer[i] *= scale;
            }

            // The loop filter: a blend of this sample and the last (high overtones fade first),
            // times a gain that sets how long the string rings.
            // (less of it for high notes, whose overtones would otherwise vanish in a moment)
            double blend = (0.5 - 0.32 * bright) * Math.Min(1.0, 220.0 / freq);
            double gainHeld = Math.Pow(10, -3.0 / (freq * t60));
            double gainDamped = Math.Pow(10, -3.0 / (freq * 0.06));
            int heldSamples = (int) (held * SAMPLE_RATE);
            int total = Math.Min(heldSamples + (int) (0.12 * SAMPLE_RATE), output.Length - start);
            bool useVibrato = vibrato && held > 0.45;
            double vibratoRate = 2 * Math.PI * 5.3 / SAMPLE_RATE;
            int vibratoFrom = (int) (0.3 * SAMPLE_RATE);
            double baseDelay = period - blend;

            int write = length & mask;
            double previous = 0;
            for (int i = 0; i < total; i++)
            {
                double delay = baseDelay;
                if (useVibrato && i > vibratoFrom)
                {
                    // Grows in over half a second, to about 14 cents either way
                    double depth = Math.Min(1.0, (i - vibratoFrom) / (0.5 * SAMPLE_RATE)) * 0.008;
                    delay *= 1.0 + depth * Math.Sin(vibratoRate * i);
                }

                double readAt = write - delay;
                int i0 = (int) Math.Floor(readAt);
                double f = readAt - i0;
                double xm1 = buffer[(i0 - 1) & mask];
                double x0 = buffer[i0 & mask];
                double x1 = buffer[(i0 + 1) & mask];
                double x2 = buffer[(i0 + 2) & mask];
                // Four-point interpolation between samples, so any pitch is exactly in tune
                double sample = -f * (f - 1) * (f - 2) / 6 * xm1
                    + (f + 1) * (f - 1) * (f - 2) / 2 * x0
                    - (f + 1) * f * (f - 2) / 2 * x1
                    + (f + 1) * f * (f - 1) / 6 * x2;

                double gain = i < heldSamples ? gainHeld : gainDamped;
                buffer[write] = gain * ((1 - blend) * sample + blend * previous);
                previous = sample;
                write = (write + 1) & mask;
                output[start + i] += (float) sample;
            }
        }

        private struct Biquad
        {
            private double _b0, _b1, _b2, _a1, _a2, _z1, _z2;

            public static Biquad LowPass(double hz, double q)
            {
                double w = 2 * Math.PI * hz / SAMPLE_RATE, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + alpha;
                return new Biquad
                {
                    _b0 = (1 - c) / 2 / a0, _b1 = (1 - c) / a0, _b2 = (1 - c) / 2 / a0,
                    _a1 = -2 * c / a0, _a2 = (1 - alpha) / a0,
                };
            }

            public static Biquad HighPass(double hz, double q)
            {
                double w = 2 * Math.PI * hz / SAMPLE_RATE, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + alpha;
                return new Biquad
                {
                    _b0 = (1 + c) / 2 / a0, _b1 = -(1 + c) / a0, _b2 = (1 + c) / 2 / a0,
                    _a1 = -2 * c / a0, _a2 = (1 - alpha) / a0,
                };
            }

            public static Biquad Peak(double hz, double q, double db)
            {
                double amp = Math.Pow(10, db / 40);
                double w = 2 * Math.PI * hz / SAMPLE_RATE, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + alpha / amp;
                return new Biquad
                {
                    _b0 = (1 + alpha * amp) / a0, _b1 = -2 * c / a0, _b2 = (1 - alpha * amp) / a0,
                    _a1 = -2 * c / a0, _a2 = (1 - alpha / amp) / a0,
                };
            }

            public double Run(double x)
            {
                double o = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * o + _z2;
                _z2 = _b2 * x - _a2 * o;
                return o;
            }
        }

        // Smooth clipping, like an overdriven valve
        private static double Saturate(double x)
        {
            if (x > 3)
            {
                return 1;
            }
            if (x < -3)
            {
                return -1;
            }
            return x * (27 + x * x) / (27 + 9 * x * x);
        }

        // Tone shaping, overdrive (computed at twice the sample rate to keep it clean) and speaker cabinet
        private static void GuitarAmp(float[] y, double drive)
        {
            var inputHigh = Biquad.HighPass(90, 0.707);
            var inputMid = Biquad.Peak(850, 0.8, 6.0);
            var cabHigh = Biquad.HighPass(85, 0.707);
            var cabPresence = Biquad.Peak(2300, 1.2, 4.0);
            var cabLow1 = Biquad.LowPass(4300, 0.8);
            var cabLow2 = Biquad.LowPass(5200, 0.6);

            const double BIAS = 0.12;
            double biasOut = Saturate(BIAS);
            double previous = 0;
            for (int i = 0; i < y.Length; i++)
            {
                double v = inputMid.Run(inputHigh.Run(y[i])) * drive;
                double between = 0.5 * (previous + v);
                double a = Saturate(between + BIAS) - biasOut;
                double b = Saturate(v + BIAS) - biasOut;
                previous = v;
                double o = 0.5 * (a + b);
                o = cabLow2.Run(cabLow1.Run(cabPresence.Run(cabHigh.Run(o))));
                y[i] = (float) o;
            }
        }

        private static void BassAmp(float[] y, double drive)
        {
            var high = Biquad.HighPass(35, 0.707);
            var low = Biquad.LowPass(2200, 0.7);
            var body = Biquad.Peak(90, 0.9, 3.0);
            for (int i = 0; i < y.Length; i++)
            {
                double v = Saturate(high.Run(y[i]) * drive);
                y[i] = (float) body.Run(low.Run(v));
            }
        }

        // A small room: four echoing delays into two diffusers, mixed in quietly. The guitar itself
        // stays in the middle; the room is slightly different left and right, which gives it width.
        private static void Room(float[] y, float[] stereo, float mix)
        {
            int[] combs = { 1557, 1617, 1491, 1422 };
            int[] diffusers = { 225, 556 };
            const float FEEDBACK = 0.78f;

            var wet = new float[y.Length];
            for (int side = 0; side < 2; side++)
            {
                int spread = side * 23;
                Array.Clear(wet, 0, wet.Length);
                foreach (int length in combs)
                {
                    int d = length + spread;
                    var buffer = new float[d];
                    int p = 0;
                    float lp = 0;
                    for (int i = 0; i < y.Length; i++)
                    {
                        float o = buffer[p];
                        lp = o * 0.7f + lp * 0.3f;
                        buffer[p] = y[i] + lp * FEEDBACK;
                        if (++p == d)
                        {
                            p = 0;
                        }
                        wet[i] += o * 0.25f;
                    }
                }
                foreach (int length in diffusers)
                {
                    int d = length + spread;
                    var buffer = new float[d];
                    int p = 0;
                    for (int i = 0; i < wet.Length; i++)
                    {
                        float b = buffer[p];
                        float o = -0.5f * wet[i] + b;
                        buffer[p] = wet[i] + 0.5f * o;
                        if (++p == d)
                        {
                            p = 0;
                        }
                        wet[i] = o;
                    }
                }
                for (int i = 0; i < y.Length; i++)
                {
                    stereo[2 * i + side] = y[i] * (1 - mix) + wet[i] * mix;
                }
            }
        }

        // Brings the sounding parts to the wanted loudness and rounds off any peaks.
        // "Sounding" means the stretches within 12 dB of the loudest one, so that quiet tails and
        // gaps between notes do not make a sparse part come out louder than a busy one.
        private static void Level(float[] y, float targetRms)
        {
            const int WINDOW = 4096;
            int windows = y.Length / WINDOW;
            if (windows == 0)
            {
                return;
            }

            var windowPower = new double[windows];
            double loudest = 0;
            for (int w = 0; w < windows; w++)
            {
                double p = 0;
                int from = w * WINDOW;
                for (int i = 0; i < WINDOW; i++)
                {
                    float v = y[from + i];
                    p += (double) v * v;
                }
                windowPower[w] = p;
                if (p > loudest)
                {
                    loudest = p;
                }
            }
            if (loudest <= 0)
            {
                return;
            }

            double power = 0;
            long active = 0;
            for (int w = 0; w < windows; w++)
            {
                if (windowPower[w] >= loudest * 0.0625)
                {
                    power += windowPower[w];
                    active += WINDOW;
                }
            }

            float gain = (float) (targetRms / Math.Sqrt(power / active));
            const float KNEE = 0.7f;
            const float ROOM = 0.28f;
            for (int i = 0; i < y.Length; i++)
            {
                float v = y[i] * gain;
                float mag = Math.Abs(v);
                if (mag > KNEE)
                {
                    mag = KNEE + ROOM * (float) Math.Tanh((mag - KNEE) / ROOM);
                    v = v < 0 ? -mag : mag;
                }
                y[i] = v;
            }
        }
    }
}
