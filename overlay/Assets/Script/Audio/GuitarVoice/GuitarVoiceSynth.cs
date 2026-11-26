// Guitar voice, part 2: play the chart on an electric guitar (or a bass) and hand back the recording.
//
// Plain .NET only (no Unity, no BASS) so the automated tests can run it.
//
// The sound is made the way the real thing is made:
//   - the notes are recordings of a real guitar's strings, taken straight from the instrument
//     (GuitarVoiceStrings), played the way a hand plays them: hammer-ons without a pick click,
//     strummed chords, each note stopped as the pick lands for the next;
//   - all strings go through one amplifier (GuitarVoiceAmp), a measured speaker cabinet
//     (GuitarVoiceCabinets) and a room, so chords growl like a real overdriven amp and the tone
//     has a real speaker's shape.

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
        /// <param name="strings">The recorded notes to play (null: a modelled string stands in).</param>
        public static float[] Render(VoiceNote[] notes, double totalSeconds, GuitarVoiceTone tone, bool bass,
            float cents, float targetRms, StringBank strings = null)
        {
            int n = Math.Max(0, (int) (totalSeconds * SAMPLE_RATE));
            if (n == 0)
            {
                return new float[0];
            }

            // Only used by the modelled string
            double bright = bass ? 0.25 : tone == GuitarVoiceTone.Clean ? 0.75 : tone == GuitarVoiceTone.Heavy ? 0.5 : 0.6;
            double sustain = bass || tone == GuitarVoiceTone.Clean ? 1.0 : tone == GuitarVoiceTone.Heavy ? 1.8 : 1.3;

            // The bare guitar signal, then the amplifier, then the room
            var y = new float[n];
            double tune = Math.Pow(2, cents / 1200.0);
            StringPlayer.Play(y, notes, strings, bass, tune, bright, sustain);

            var amp = AmpSettings.For(tone, bass);
            GuitarAmp.Run(y, amp);

            var stereo = new float[n * 2];
            GuitarAmp.Room(y, stereo, amp.RoomMix, amp.RoomSeconds);

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
            float cents, float targetRms, StringBank strings = null)
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

            var audio = Render(sequence, notes.Length * slot, tone, bass, cents, targetRms, strings);
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

        // Brings the sounding parts to the wanted loudness and rounds off any peaks.
        // "Sounding" means the stretches within 12 dB of the loudest one, so that quiet tails and
        // gaps between notes do not make a sparse part come out louder than a busy one.
        //
        // Loudness is measured the way the ear weighs it (the weighting broadcast loudness meters
        // use: highs count for more, deep bass for less). An overdriven guitar has much of its
        // energy around 3 kHz, where hearing is keenest, and would otherwise come out sounding
        // far louder than a clean guitar or a bass set to the same level.
        private static void Level(float[] y, float targetRms)
        {
            const int WINDOW = 4096;
            int windows = y.Length / WINDOW;
            if (windows == 0)
            {
                return;
            }

            // One pair of weighting filters for the left channel, one for the right
            var highs = new[] { Filter.HighShelf(1682, 0.707, 4.0, SAMPLE_RATE), Filter.HighShelf(1682, 0.707, 4.0, SAMPLE_RATE) };
            var rumble = new[] { Filter.HighPass(38, 0.5, SAMPLE_RATE), Filter.HighPass(38, 0.5, SAMPLE_RATE) };

            var windowPower = new double[windows];
            double loudest = 0;
            for (int w = 0; w < windows; w++)
            {
                double p = 0;
                int from = w * WINDOW;
                for (int i = 0; i < WINDOW; i++)
                {
                    int channel = i & 1;
                    double v = rumble[channel].Run(highs[channel].Run(y[from + i]));
                    p += v * v;
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
