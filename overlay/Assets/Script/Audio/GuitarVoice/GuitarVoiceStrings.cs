// Guitar voice, part 2a: the strings.
//
// Plain .NET only (no Unity, no BASS) so the automated tests can run it.
//
// The notes are real: recordings of an electric guitar (and a bass guitar) taken straight from
// the instrument's output, one note at a time, before any amplifier. They are kept in a small
// file (a "string bank") that ships with the game. Each note was recorded several times over,
// picked hard and picked softly, and those takes are all there.
//
// Playing a chart means choosing, for every note, the recorded note closest in pitch, playing it
// a little faster or slower so it lands exactly on the note and on the song's tuning, and
// treating it the way a hand would: the same note twice in a row is a different take each time
// (no two picks sound alike), hammer-ons and pull-offs use the softly played takes, a note is
// damped when the next one takes over the string, chords are strummed across the strings, and
// long single notes get a little finger vibrato.
//
// What comes out of here is the bare guitar signal. The amplifier (GuitarVoiceAmp) makes the
// sound from it, which is why chords growl the way they do on a real amp: the strings are mixed
// first and distorted together.
//
// If the bank is missing, a modelled string (a delay line the length of one vibration) stands in.

using System;
using System.IO;

namespace YARG.Audio.GuitarVoice
{
    /// <summary>Recorded notes of one instrument.</summary>
    public sealed class StringBank
    {
        /// <summary>One take of one recorded note.</summary>
        public sealed class Recording
        {
            /// <summary>The note that was played, as a MIDI note number (69 = the A at 440 Hz).</summary>
            public int Note;

            /// <summary>The pitch this take really sounds at (a string is never dead on the note).</summary>
            public float Pitch;

            /// <summary>Played softly (used for hammer-ons and pull-offs) rather than picked hard.</summary>
            public bool Soft;

            /// <summary>Multiply the samples (as -1..1) by this to get every take equally loud.</summary>
            public float Gain;

            public short[] Samples;
        }

        // All takes of one note, hard and soft
        internal sealed class Note
        {
            public int Number;
            public Recording[] Picked;
            public Recording[] Soft;
        }

        private const int BLOCK = 2048;
        private const int ESCAPE = 24;
        private const int ESCAPE_BITS = 20;
        private const int MAX_ORDER = 8;
        private const int WEIGHT_BITS = 13;

        public int SampleRate { get; }
        public bool IsBass { get; }

        /// <summary>Every take, lowest note first.</summary>
        public Recording[] Recordings { get; }

        /// <summary>The takes grouped by note, lowest first.</summary>
        internal Note[] Notes { get; }

        public StringBank(int sampleRate, bool bass, Recording[] recordings)
        {
            if (recordings == null || recordings.Length == 0)
            {
                throw new ArgumentException("A string bank needs at least one recording");
            }
            SampleRate = sampleRate;
            IsBass = bass;
            Recordings = (Recording[]) recordings.Clone();
            // (a stable order: takes of one note stay in the order they were given)
            var order = new int[Recordings.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            var given = recordings;
            Array.Sort(order, (a, b) => given[a].Note != given[b].Note
                ? given[a].Note.CompareTo(given[b].Note)
                : a.CompareTo(b));
            for (int i = 0; i < order.Length; i++)
            {
                Recordings[i] = given[order[i]];
            }

            var notes = new System.Collections.Generic.List<Note>();
            int from = 0;
            while (from < Recordings.Length)
            {
                int to = from;
                var picked = new System.Collections.Generic.List<Recording>();
                var soft = new System.Collections.Generic.List<Recording>();
                while (to < Recordings.Length && Recordings[to].Note == Recordings[from].Note)
                {
                    (Recordings[to].Soft ? soft : picked).Add(Recordings[to]);
                    to++;
                }
                // A note recorded only softly still has to serve for picked notes
                if (picked.Count == 0)
                {
                    picked.AddRange(soft);
                }
                notes.Add(new Note { Number = Recordings[from].Note, Picked = picked.ToArray(), Soft = soft.ToArray() });
                from = to;
            }
            Notes = notes.ToArray();
        }

        public static StringBank Load(string path)
        {
            return Load(File.ReadAllBytes(path));
        }

        /// <summary>Reads a bank file. Throws <see cref="InvalidDataException"/> if it is damaged.</summary>
        public static StringBank Load(byte[] data)
        {
            if (data == null || data.Length < 16 || data[0] != 'Y' || data[1] != 'G' || data[2] != 'S' || data[3] != 'B')
            {
                throw new InvalidDataException("Not a string bank");
            }
            int version = BitConverter.ToUInt16(data, 4);
            if (version != 1)
            {
                throw new InvalidDataException("String bank version " + version + " is not supported");
            }

            bool bass = BitConverter.ToUInt16(data, 6) == 1;
            int sampleRate = BitConverter.ToInt32(data, 8);
            int count = BitConverter.ToInt32(data, 12);
            const int HEADER = 16, RECORD = 24;
            if (sampleRate < 8000 || sampleRate > 96000 || count <= 0 || count > 4096
                || data.Length < HEADER + count * RECORD)
            {
                throw new InvalidDataException("String bank header is damaged");
            }

            var recordings = new Recording[count];
            for (int i = 0; i < count; i++)
            {
                int at = HEADER + i * RECORD;
                var recording = new Recording
                {
                    Pitch = BitConverter.ToSingle(data, at),
                    Gain = BitConverter.ToSingle(data, at + 4),
                    Note = data[at + 12],
                    Soft = data[at + 13] != 0,
                };
                int frames = BitConverter.ToInt32(data, at + 8);
                int offset = BitConverter.ToInt32(data, at + 16);
                int bytes = BitConverter.ToInt32(data, at + 20);
                if (frames < 8 || frames > 60 * sampleRate || offset < 0 || bytes < 0
                    || (long) offset + bytes > data.Length || float.IsNaN(recording.Pitch)
                    || Math.Abs(recording.Pitch - recording.Note) > 1.5f || recording.Note > 127
                    || !(recording.Gain > 0) || float.IsInfinity(recording.Gain))
                {
                    throw new InvalidDataException("String bank recording " + i + " is damaged");
                }
                recording.Samples = Unpack(data, offset, bytes, frames);
                recordings[i] = recording;
            }
            return new StringBank(sampleRate, bass, recordings);
        }

        // The takes are stored without loss in about 40% of the space: each sample is guessed
        // from the few before it (with weights worked out for every stretch of the take) and
        // only the miss is written, in as few bits as it takes.
        private static short[] Unpack(byte[] data, int offset, int bytes, int frames)
        {
            var samples = new short[frames];
            int end = offset + bytes;
            int at = offset;
            var weights = new int[MAX_ORDER];
            for (int start = 0; start < frames; start += BLOCK)
            {
                if (at >= end)
                {
                    throw new InvalidDataException("String bank data ends early");
                }
                int header = data[at++];
                int order = header >> 4, k = header & 15;
                if (order > MAX_ORDER)
                {
                    throw new InvalidDataException("String bank data is damaged");
                }

                // Each block starts on a whole byte; from here on it is a stream of bits
                uint window = 0;
                int have = 0;
                int shift = 0;
                if (order > 0)
                {
                    if (at >= end)
                    {
                        throw new InvalidDataException("String bank data ends early");
                    }
                    shift = data[at++];
                    if (shift > 15)
                    {
                        throw new InvalidDataException("String bank data is damaged");
                    }
                    for (int j = 0; j < order; j++)
                    {
                        int weight = 0;
                        for (int b = 0; b < WEIGHT_BITS; b++)
                        {
                            if (have == 0)
                            {
                                if (at >= end)
                                {
                                    throw new InvalidDataException("String bank data ends early");
                                }
                                window = data[at++];
                                have = 8;
                            }
                            have--;
                            weight = (weight << 1) | (int) ((window >> have) & 1);
                        }
                        // 13 bits, the top one being the sign
                        weights[j] = (weight << (32 - WEIGHT_BITS)) >> (32 - WEIGHT_BITS);
                    }
                }

                int count = Math.Min(BLOCK, frames - start);
                for (int i = 0; i < count; i++)
                {
                    // A run of 1 bits ended by a 0 gives the high part of the miss
                    int high = 0;
                    while (true)
                    {
                        if (have == 0)
                        {
                            if (at >= end)
                            {
                                throw new InvalidDataException("String bank data ends early");
                            }
                            window = data[at++];
                            have = 8;
                        }
                        have--;
                        if (((window >> have) & 1) == 0)
                        {
                            break;
                        }
                        if (++high == ESCAPE)
                        {
                            break;
                        }
                    }

                    int bits = high == ESCAPE ? ESCAPE_BITS : k;
                    uint low = 0;
                    for (int b = 0; b < bits; b++)
                    {
                        if (have == 0)
                        {
                            if (at >= end)
                            {
                                throw new InvalidDataException("String bank data ends early");
                            }
                            window = data[at++];
                            have = 8;
                        }
                        have--;
                        low = (low << 1) | ((window >> have) & 1);
                    }

                    uint folded = high == ESCAPE ? low : ((uint) high << k) | low;
                    int miss = (int) (folded >> 1) ^ -(int) (folded & 1);

                    // The guess, from the samples just before this one (zero before the start)
                    long guess = 0;
                    int here = start + i;
                    if (here >= order)
                    {
                        for (int j = 0; j < order; j++)
                        {
                            guess += (long) weights[j] * samples[here - 1 - j];
                        }
                    }
                    else
                    {
                        for (int j = 0; j < order && j < here; j++)
                        {
                            guess += (long) weights[j] * samples[here - 1 - j];
                        }
                    }

                    long value = (guess >> shift) + miss;
                    if (value < short.MinValue || value > short.MaxValue)
                    {
                        throw new InvalidDataException("String bank data is damaged");
                    }
                    samples[here] = (short) value;
                }
            }
            return samples;
        }
    }

    internal static class StringPlayer
    {
        private const int RATE = GuitarVoiceSynth.SAMPLE_RATE;

        // A plain note rings until the next one, but not for longer than this
        private const double RING = 1.2;

        /// <summary>
        /// Plays the notes into <paramref name="output"/> (one channel, bare guitar signal).
        /// </summary>
        /// <param name="tune">Frequency ratio of the song's tuning to A = 440 Hz.</param>
        public static void Play(float[] output, VoiceNote[] notes, StringBank bank, bool bass, double tune,
            double bright, double sustain)
        {
            uint rng = 12345;
            int n = output.Length;
            double cents = 1200 * Math.Log(tune, 2);

            // Which take of each note comes next (picked and soft takes counted apart)
            int[] turn = bank == null ? null : new int[bank.Notes.Length * 2];
            if (turn != null)
            {
                for (int i = 0; i < turn.Length; i++)
                {
                    turn[i] = (int) ((Noise(ref rng) + 1.0) * 1000);
                }
            }

            double lastChordTime = double.NegativeInfinity;
            bool lastChordUp = false;

            for (int i = 0; i < notes.Length; i++)
            {
                var note = notes[i];
                int start = (int) (note.Time * RATE);
                if (start < 0 || start >= n)
                {
                    continue;
                }

                // A sustain rings for its length. A plain note rings until the next one.
                double untilNext = i + 1 < notes.Length ? notes[i + 1].Time - note.Time : double.PositiveInfinity;
                double held = note.Length > 0.12
                    ? note.Length
                    : Math.Min(Math.Max(untilNext, 0.08), RING);

                // When the next note comes while this one rings, it takes over the strings: this
                // one stops right there, quickly. Otherwise the hand damps it, a little slower.
                bool taken = untilNext < held + 0.03;
                double ring = taken ? Math.Max(untilNext, 0.02) : held;
                double fade = taken ? 0.012 : 0.07;

                int strings = bass || note.FretCount <= 1 ? 1 : note.FretCount == 2 ? 2 : 3;

                // Chords in quick succession are strummed down, up, down, up
                bool up = false;
                if (strings > 1)
                {
                    up = note.Time - lastChordTime < 0.17 && !lastChordUp;
                    lastChordTime = note.Time;
                    lastChordUp = up;
                }

                // Long single notes get some finger vibrato
                bool vibrato = strings == 1 && !bass && held > 0.45;

                for (int s = 0; s < strings; s++)
                {
                    // Power chord: root, fifth, octave
                    int pitch = note.Pitch + (s == 1 ? 7 : s == 2 ? 12 : 0);
                    // The pick crosses the strings one after the other (the first on the beat)
                    int order = up ? strings - 1 - s : s;
                    double gap = order * (0.007 + 0.0015 * Noise(ref rng));
                    int at = start + (int) (gap * RATE);
                    if (at >= n)
                    {
                        continue;
                    }

                    // No two plucks are alike: a little louder or softer, a hair sharp or flat
                    double velocity = (note.Soft ? 0.62 : 1.0) * (s == 0 ? 1.0 : 0.82) * (up ? 0.9 : 1.0)
                        * (1.0 + 0.06 * Noise(ref rng));
                    double detune = 1.5 * Noise(ref rng);

                    if (bank == null)
                    {
                        double freq = 440.0 * Math.Pow(2, (pitch - 69) / 12.0) * tune;
                        double t60 = (bass ? 5.0 : 3.5) * sustain * Math.Pow(110.0 / freq, 0.25);
                        // High strings are thinner and quieter; make up some of the difference
                        double lift = Math.Min(1.6, Math.Max(1.0, Math.Pow(freq / 110.0, 0.25)));
                        ModelledString(output, at, freq, ring, velocity * lift, bright, t60, ref rng,
                            note.Soft, vibrato);
                        continue;
                    }

                    double target = pitch + (cents + detune) / 100.0;
                    double seconds = ring - gap + fade;
                    var take = Choose(bank, target, note.Soft, seconds, turn);
                    // A hammer-on played from a picked take has its pick click taken off
                    Recorded(output, at, bank, take, target, velocity, ring - gap, fade,
                        note.Soft && !take.Soft, vibrato, ref rng);
                }
            }
        }

        // A take of the recorded note nearest in pitch: the softly played ones for hammer-ons,
        // each take in turn so the same note never sounds the same twice running, and only
        // takes long enough for the note (long notes have fewer to choose from).
        private static StringBank.Recording Choose(StringBank bank, double target, bool soft, double seconds,
            int[] turn)
        {
            var notes = bank.Notes;
            int nearest = 0;
            for (int i = 1; i < notes.Length; i++)
            {
                if (Math.Abs(notes[i].Number - target) < Math.Abs(notes[nearest].Number - target))
                {
                    nearest = i;
                }
            }

            var note = notes[nearest];
            double speed = Math.Pow(2, (target - note.Number) / 12.0);
            double needed = seconds * speed * bank.SampleRate;

            // Softly played takes are short: a long hammered note falls back on the picked ones
            bool useSoft = soft && note.Soft.Length > 0 && Longest(note.Soft).Samples.Length >= needed;
            var takes = useSoft ? note.Soft : note.Picked;

            int enough = 0;
            for (int i = 0; i < takes.Length; i++)
            {
                if (takes[i].Samples.Length >= needed)
                {
                    enough++;
                }
            }
            if (enough == 0)
            {
                return Longest(takes);
            }

            int which = turn[nearest * 2 + (useSoft ? 1 : 0)]++ % enough;
            for (int i = 0; i < takes.Length; i++)
            {
                if (takes[i].Samples.Length >= needed && which-- == 0)
                {
                    return takes[i];
                }
            }
            return takes[0];
        }

        private static StringBank.Recording Longest(StringBank.Recording[] takes)
        {
            var longest = takes[0];
            for (int i = 1; i < takes.Length; i++)
            {
                if (takes[i].Samples.Length > longest.Samples.Length)
                {
                    longest = takes[i];
                }
            }
            return longest;
        }

        // One recorded note, added into output from `start`
        private static void Recorded(float[] output, int start, StringBank bank, StringBank.Recording recording,
            double target, double velocity, double ring, double fade, bool noPick, bool vibrato, ref uint rng)
        {
            var data = recording.Samples;
            int frames = data.Length;
            double rate = Math.Pow(2, (target - recording.Pitch) / 12.0) * bank.SampleRate / RATE;
            double gain = velocity * recording.Gain / 32768.0;

            // A hammer-on or pull-off has no pick: start just past the click, ease in, and
            // take the edge off the top
            double position = noPick ? 0.016 * bank.SampleRate : 0.0;
            int attack = noPick ? (int) (0.006 * RATE) : 0;
            double dull = noPick ? 1 - Math.Exp(-2 * Math.PI * 2600.0 / RATE) : 1.0;
            double smooth = 0;

            int ringSamples = Math.Max(1, (int) (ring * RATE));
            int fadeSamples = Math.Max(1, (int) (fade * RATE));
            int total = Math.Min(ringSamples + fadeSamples, output.Length - start);

            // Vibrato: about five times a second, a little unevenly (as a hand's is), growing in
            // over half a second, and centred on the note so it stays in tune with the song
            bool useVibrato = vibrato && ring > 0.45;
            int vibratoFrom = (int) (0.32 * RATE);
            double vibratoPhase = 0;
            double vibratoSpeed = 2 * Math.PI * (5.0 + 0.5 * Noise(ref rng)) / RATE;
            double vibratoDepth = (0.9 + 0.25 * Noise(ref rng)) * (Math.Pow(2, 10.0 / 1200.0) - 1);
            double wander = 0;

            for (int i = 0; i < total; i++)
            {
                int i0 = (int) position;
                if (i0 + 2 >= frames)
                {
                    break;
                }
                double f = position - i0;
                double xm1 = data[i0 > 0 ? i0 - 1 : 0];
                double x0 = data[i0];
                double x1 = data[i0 + 1];
                double x2 = data[i0 + 2];

                // A smooth curve through the four nearest samples
                double c1 = 0.5 * (x1 - xm1);
                double c2 = xm1 - 2.5 * x0 + 2 * x1 - 0.5 * x2;
                double c3 = 0.5 * (x2 - xm1) + 1.5 * (x0 - x1);
                double sample = ((c3 * f + c2) * f + c1) * f + x0;

                if (noPick)
                {
                    smooth += dull * (sample - smooth);
                    sample = smooth;
                }

                double level = gain;
                if (i < attack)
                {
                    level *= (i + 1) / (double) attack;
                }
                if (i >= ringSamples)
                {
                    double left = 1.0 - (i - ringSamples) / (double) fadeSamples;
                    level *= left * left;
                }
                output[start + i] += (float) (sample * level);

                double step = rate;
                if (useVibrato && i > vibratoFrom)
                {
                    double depth = Math.Min(1.0, (i - vibratoFrom) / (0.5 * RATE)) * vibratoDepth;
                    // The speed drifts a little
                    wander += 0.00002 * (Noise(ref rng) - wander * 0.02);
                    vibratoPhase += vibratoSpeed * (1.0 + 40.0 * wander);
                    step *= 1.0 + depth * Math.Sin(vibratoPhase);
                }
                position += step;
            }
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

        // A modelled string, for when there are no recordings to play. Added into output from
        // `start`; after `held` seconds it is damped.
        private static void ModelledString(float[] output, int start, double freq, double held, double velocity,
            double bright, double t60, ref uint rng, bool soft, bool vibrato)
        {
            double period = RATE / freq;
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
            double k = 1 - Math.Exp(-2 * Math.PI * cutoff / RATE);
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
            int heldSamples = (int) (held * RATE);
            int total = Math.Min(heldSamples + (int) (0.12 * RATE), output.Length - start);
            bool useVibrato = vibrato && held > 0.45;
            double vibratoRate = 2 * Math.PI * 5.3 / RATE;
            int vibratoFrom = (int) (0.3 * RATE);
            double baseDelay = period - blend;

            int write = length & mask;
            double previous = 0;
            for (int i = 0; i < total; i++)
            {
                double delay = baseDelay;
                if (useVibrato && i > vibratoFrom)
                {
                    // Grows in over half a second, to about 14 cents either way
                    double depth = Math.Min(1.0, (i - vibratoFrom) / (0.5 * RATE)) * 0.008;
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
    }
}
