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
using System.Collections.Generic;

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
        /// Makes the rendered guitar rise and fall with the song. A band plays quietly in a quiet
        /// intro and digs in for the chorus; a guitar at one level all the way through sits on
        /// top of the record instead of in it (far too loud over the quiet parts, the first thing
        /// that makes it sound stuck on). The guitar moves a little over half as far as the song
        /// does, never more than 10 dB down or 3 dB up, and only as fast as a player would.
        /// </summary>
        /// <param name="stereo">The rendered guitar (left and right alternating), changed in place.</param>
        /// <param name="song">The song, mono, starting at the same moment.</param>
        public static void FollowSong(float[] stereo, float[] song, int songRate)
        {
            const double HOP_SECONDS = 0.25;
            const double FOLLOW = 0.6, MAX_DOWN_DB = -10, MAX_UP_DB = 3;
            int hop = (int) (songRate * HOP_SECONDS);
            int frames = hop > 0 ? song.Length / hop : 0;
            if (frames < 8 || stereo.Length < 2)
            {
                return;
            }

            // The song's level, a second at a time (four hops around each point)
            var power = new double[frames];
            for (int f = 0; f < frames; f++)
            {
                double p = 0;
                int from = f * hop;
                for (int i = 0; i < hop; i++)
                {
                    double v = song[from + i];
                    p += v * v;
                }
                power[f] = p / hop;
            }

            var level = new double[frames];
            double loudest = double.NegativeInfinity;
            for (int f = 0; f < frames; f++)
            {
                double p = 0;
                int count = 0;
                for (int k = Math.Max(0, f - 2); k <= Math.Min(frames - 1, f + 1); k++)
                {
                    p += power[k];
                    count++;
                }
                level[f] = 10 * Math.Log10(p / count + 1e-12);
                loudest = Math.Max(loudest, level[f]);
            }

            // Its usual level: the middle one of the moments it is really playing
            var playing = new List<double>(frames);
            foreach (double l in level)
            {
                if (l > loudest - 30)
                {
                    playing.Add(l);
                }
            }
            if (playing.Count < 4)
            {
                return;
            }
            playing.Sort();
            double usual = playing[playing.Count / 2];

            var gain = new double[frames];
            for (int f = 0; f < frames; f++)
            {
                gain[f] = Math.Max(MAX_DOWN_DB, Math.Min(MAX_UP_DB, FOLLOW * (level[f] - usual)));
            }

            // Smoothed forwards and backwards (about 0.6 s), so it swells and never jumps
            const double KEEP = 0.66;
            for (int f = 1; f < frames; f++)
            {
                gain[f] = KEEP * gain[f - 1] + (1 - KEEP) * gain[f];
            }
            for (int f = frames - 2; f >= 0; f--)
            {
                gain[f] = KEEP * gain[f + 1] + (1 - KEEP) * gain[f];
            }

            int n = stereo.Length / 2;
            double perSample = (double) songRate / SAMPLE_RATE / hop;
            for (int i = 0; i < n; i++)
            {
                // Each gain belongs to the middle of its hop
                double at = i * perSample - 0.5;
                int f0 = (int) Math.Floor(at);
                double t = at - f0;
                double g0 = gain[Math.Max(0, Math.Min(frames - 1, f0))];
                double g1 = gain[Math.Max(0, Math.Min(frames - 1, f0 + 1))];
                float g = (float) Math.Pow(10, (g0 + (g1 - g0) * t) / 20);
                stereo[2 * i] *= g;
                stereo[2 * i + 1] *= g;
            }
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
