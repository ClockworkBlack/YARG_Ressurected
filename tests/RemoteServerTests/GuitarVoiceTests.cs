// Tests for the guitar voice (overlay/Assets/Script/Audio/GuitarVoice): does it hear the right
// notes in a song, and does the guitar it plays come out in tune, at the right level, on time?

using System;
using System.Collections.Generic;
using System.IO;
using YARG.Audio.GuitarVoice;

namespace RemoteServerTests
{
    internal static class GuitarVoiceTests
    {
        private static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };

        private sealed class TestSong
        {
            public float[] Audio;
            public List<(double Time, double Length, int Midi)> Melody = new();
            public List<(double Time, int Midi)> Bass = new();
        }

        private static uint _seed;

        private static double Rand()
        {
            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            return _seed / 4294967296.0;
        }

        // A small band with known notes: a low sine bass (like an 808), a chord pad, a lead melody,
        // a kick and hi-hats. Minor key rooted on `keyRoot`, detuned by `cents`.
        private static TestSong MakeSong(int keyRoot, double cents, uint seed)
        {
            _seed = seed;
            const int SR = SongHarmony.SAMPLE_RATE;
            const double SECONDS = 40.0, BPM = 96;
            int n = (int) (SECONDS * SR);
            var x = new float[n];
            var song = new TestSong { Audio = x };
            double tune = Math.Pow(2, cents / 1200.0);
            double beat = 60 / BPM;
            int[] progression = { 0, 5, 2, 6 };

            void Tone(int midi, double start, double length, double amp, int kind)
            {
                int i0 = (int) (start * SR), i1 = Math.Min(n, (int) ((start + length) * SR));
                double hz = 440.0 * Math.Pow(2, (midi - 69) / 12.0) * tune;
                for (int i = i0; i < i1; i++)
                {
                    double t = (i - i0) / (double) SR;
                    double env = Math.Exp(-t * 2.0) * (1 - Math.Exp(-t * 200));
                    double s = 0;
                    if (kind == 0)
                    {
                        s = Math.Sin(2 * Math.PI * hz * t) + 0.3 * Math.Sin(4 * Math.PI * hz * t);
                    }
                    else if (kind == 1)
                    {
                        for (int h = 1; h <= 8; h++)
                        {
                            s += Math.Sin(2 * Math.PI * hz * h * t) / h;
                        }
                    }
                    else
                    {
                        for (int h = 1; h < 12; h += 2)
                        {
                            s += Math.Sin(2 * Math.PI * hz * h * t) / h;
                        }
                    }
                    x[i] += (float) (amp * env * s);
                }
            }

            int beats = (int) (SECONDS / beat);
            for (int b = 0; b < beats; b++)
            {
                int degree = progression[(b / 4) % 4];
                int root = 36 + keyRoot + Minor[degree];
                if (b % 2 == 0)
                {
                    Tone(root, b * beat, beat * 1.8, 0.5, 0);
                    song.Bass.Add((b * beat, root));
                }
                if (b % 4 == 0)
                {
                    for (int d = 0; d <= 4; d += 2)
                    {
                        int m = 48 + keyRoot + Minor[(degree + d) % 7] + (degree + d >= 7 ? 12 : 0);
                        Tone(m, b * beat, beat * 4, 0.12, 1);
                    }
                }
                for (int e = 0; e < 2; e++)
                {
                    if (Rand() < 0.8)
                    {
                        int step = (int) (Rand() * 10);
                        if (e == 0)
                        {
                            step = degree + 2 * (int) (Rand() * 3);
                        }
                        int m = 60 + keyRoot + Minor[step % 7] + 12 * (step / 7);
                        double at = (b + e * 0.5) * beat;
                        Tone(m, at, beat * 0.45, 0.35, 2);
                        song.Melody.Add((at, beat * 0.4, m));
                    }

                    // hi-hat
                    int h0 = (int) ((b + e * 0.5) * beat * SR);
                    for (int i = 0; i < (int) (0.04 * SR) && h0 + i < n; i++)
                    {
                        x[h0 + i] += (float) (0.15 * (Rand() * 2 - 1) * Math.Exp(-i / (double) SR * 90));
                    }
                }

                // kick
                int k0 = (int) (b * beat * SR);
                for (int i = 0; i < (int) (0.15 * SR) && k0 + i < n; i++)
                {
                    double t = i / (double) SR;
                    x[k0 + i] += (float) (0.5 * Math.Sin(2 * Math.PI * (120 * Math.Exp(-t * 30) + 45) * t) * Math.Exp(-t * 25));
                }
            }
            return song;
        }

        // Frets follow the melody's local shape, the way a chart does
        private static VoiceNote[] ChartFor(TestSong song)
        {
            var notes = new VoiceNote[song.Melody.Count];
            for (int i = 0; i < notes.Length; i++)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                for (int j = Math.Max(0, i - 6); j < Math.Min(notes.Length, i + 7); j++)
                {
                    lo = Math.Min(lo, song.Melody[j].Midi);
                    hi = Math.Max(hi, song.Melody[j].Midi);
                }
                int fret = hi == lo ? 1 : 1 + (int) Math.Round(4.0 * (song.Melody[i].Midi - lo) / (hi - lo));
                notes[i] = new VoiceNote
                {
                    Time = song.Melody[i].Time, Length = 0, LowFret = fret, FretCount = 1,
                };
            }
            return notes;
        }

        // The frequency near `expected` that is strongest in x[start .. start + length), in cents off
        private static double CentsOff(float[] x, int step, int offset, int start, int length, double expected, int sampleRate)
        {
            double bestMag = -1, best = 0;
            for (int c = -80; c <= 80; c++)
            {
                double hz = expected * Math.Pow(2, c / 1200.0);
                double w = 2 * Math.PI * hz / sampleRate;
                double re = 0, im = 0;
                for (int i = 0; i < length; i++)
                {
                    double window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / length);
                    double v = x[(start + i) * step + offset] * window;
                    re += v * Math.Cos(w * i);
                    im -= v * Math.Sin(w * i);
                }
                double mag = re * re + im * im;
                if (mag > bestMag)
                {
                    bestMag = mag;
                    best = c;
                }
            }
            return best;
        }

        // How far a note is from `expected`, in cents. A real string through an overdriven amp does
        // not have one clean lowest tone: its overtones run a few cents sharp of exact multiples
        // (stiffness), and the amp makes extra tones from every pair of them, a cluster of which
        // lands around the lowest one. So the pitch is taken as the middle value of what the
        // first three overtones say, which is also closer to how the ear takes it.
        private static double PitchOff(float[] x, int step, int offset, int start, int length, double expected, int sampleRate)
        {
            var off = new double[3];
            for (int h = 0; h < 3; h++)
            {
                off[h] = CentsOff(x, step, offset, start, length, expected * (h + 1), sampleRate);
            }
            Array.Sort(off);
            return off[1];
        }

        // Loudness of stereo[from .. to) (in frames) as the ear weighs it: the two weighting
        // filters of broadcast loudness meters (ITU-R BS.1770), written here from the published
        // formulas so that this check does not lean on the game's own filters.
        private static double EarWeightedRms(float[] stereo, int from, int to, int sampleRate)
        {
            double f0 = 1681.974450955533, gain = 3.999843853973347, q = 0.7071752369554196;
            double k = Math.Tan(Math.PI * f0 / sampleRate), vh = Math.Pow(10, gain / 20), vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1 + k / q + k * k;
            double sb0 = (vh + vb * k / q + k * k) / a0, sb1 = 2 * (k * k - vh) / a0, sb2 = (vh - vb * k / q + k * k) / a0;
            double sa1 = 2 * (k * k - 1) / a0, sa2 = (1 - k / q + k * k) / a0;
            f0 = 38.13547087602444;
            q = 0.5003270373238773;
            k = Math.Tan(Math.PI * f0 / sampleRate);
            double ha1 = 2 * (k * k - 1) / (1 + k / q + k * k), ha2 = (1 - k / q + k * k) / (1 + k / q + k * k);

            double power = 0;
            long count = 0;
            for (int channel = 0; channel < 2; channel++)
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, v1 = 0, v2 = 0;
                for (int i = 0; i < to && (i * 2 + channel) < stereo.Length; i++)
                {
                    double x = stereo[i * 2 + channel];
                    double y = sb0 * x + sb1 * x1 + sb2 * x2 - sa1 * y1 - sa2 * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    double v = y - 2 * u1 + u2 - ha1 * v1 - ha2 * v2;
                    u2 = u1; u1 = y; v2 = v1; v1 = v;
                    if (i >= from)
                    {
                        power += v * v;
                        count++;
                    }
                }
            }
            return count == 0 ? 0 : Math.Sqrt(power / count);
        }

        private static double Rms(float[] x, int from, int to)
        {
            double power = 0;
            int count = 0;
            for (int i = Math.Max(0, from); i < Math.Min(x.Length, to); i++)
            {
                power += (double) x[i] * x[i];
                count++;
            }
            return count == 0 ? 0 : Math.Sqrt(power / count);
        }

        // Share of the energy of the left channel of stereo[from .. to) (in frames) lying between
        // lo and hi Hz
        private static double BandShare(float[] stereo, int from, int to, double lo, double hi, int sampleRate)
        {
            const int N = 2048;
            var cos = new double[N];
            var sin = new double[N];
            for (int i = 0; i < N; i++)
            {
                cos[i] = Math.Cos(2 * Math.PI * i / N);
                sin[i] = Math.Sin(2 * Math.PI * i / N);
            }

            double inBand = 0, total = 0;
            var windowed = new double[N];
            for (int start = from; start + N <= to; start += N)
            {
                for (int i = 0; i < N; i++)
                {
                    windowed[i] = stereo[(start + i) * 2] * (0.5 - 0.5 * cos[i]);
                }
                // (a plain transform is quick enough for a few windows)
                for (int k = 1; k < N / 2; k++)
                {
                    double re = 0, im = 0;
                    int angle = 0;
                    for (int i = 0; i < N; i++)
                    {
                        re += windowed[i] * cos[angle];
                        im -= windowed[i] * sin[angle];
                        angle = (angle + k) & (N - 1);
                    }
                    double power = re * re + im * im;
                    double hz = k * (double) sampleRate / N;
                    total += power;
                    if (hz >= lo && hz < hi)
                    {
                        inBand += power;
                    }
                }
            }
            return total > 0 ? inBand / total : 0;
        }

        private static float Peak(float[] x, int from, int to)
        {
            float peak = 0;
            for (int i = Math.Max(0, from); i < Math.Min(x.Length, to); i++)
            {
                peak = Math.Max(peak, Math.Abs(x[i]));
            }
            return peak;
        }

        // The recorded notes ship in overlay/Assets/StreamingAssets/guitar-voice
        private static string BankPath(string name)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "overlay", "Assets", "StreamingAssets", "guitar-voice", name);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            throw new FileNotFoundException("Could not find " + name + " above " + AppContext.BaseDirectory);
        }

        public static void Run(Action<bool, string> check, string outputFolder)
        {
            const int SR = GuitarVoiceSynth.SAMPLE_RATE;
            var guitarBank = StringBank.Load(BankPath("guitar.bank"));
            var bassBank = StringBank.Load(BankPath("bass.bank"));

            // ---- Hearing the song
            var cases = new (int Key, double Cents, int Scale)[]
            {
                (5, 23.0, 8), (9, -31.0, 0), (0, 0.0, 3), (2, 48.0, 5),
            };
            TestSong firstSong = null;
            VoiceNote[] firstNotes = null;
            SongHarmony firstHarmony = null;
            foreach (var (key, cents, scale) in cases)
            {
                var song = MakeSong(key, cents, 77);
                var harmony = SongHarmony.Analyze(song.Audio);
                string label = $"song in key {key}, tuned {cents:+0;-0;0} cents";

                double tuningError = Math.Abs(harmony.Cents - cents);
                tuningError = Math.Min(tuningError, 100 - tuningError);
                check(tuningError < 4, $"{label}: tuning heard as {harmony.Cents:+0.0;-0.0} cents");
                check(harmony.Key == scale, $"{label}: scale found (root {harmony.Key})");

                var notes = ChartFor(song);
                GuitarVoicePitcher.Assign(harmony, notes, false);
                int nameRight = 0, inScale = 0, shapeRight = 0, shapeTotal = 0;
                bool inRange = true;
                for (int i = 0; i < notes.Length; i++)
                {
                    int p = notes[i].Pitch;
                    nameRight += p % 12 == song.Melody[i].Midi % 12 ? 1 : 0;
                    inScale += harmony.InScale[p % 12] ? 1 : 0;
                    inRange &= p >= 40 && p <= 76;
                    if (i > 0 && notes[i].LowFret != notes[i - 1].LowFret)
                    {
                        shapeTotal++;
                        shapeRight += Math.Sign(p - notes[i - 1].Pitch) == Math.Sign(notes[i].LowFret - notes[i - 1].LowFret) ? 1 : 0;
                    }
                }
                check(nameRight >= notes.Length * 0.75, $"{label}: guitar plays the melody's note ({nameRight}/{notes.Length})");
                check(inScale >= notes.Length * 0.97, $"{label}: guitar stays in key ({inScale}/{notes.Length})");
                check(shapeRight >= shapeTotal * 0.95, $"{label}: pitch rises and falls with the frets ({shapeRight}/{shapeTotal})");
                check(inRange, $"{label}: all notes within a guitar's range");

                var bassNotes = new VoiceNote[song.Bass.Count];
                for (int i = 0; i < bassNotes.Length; i++)
                {
                    bassNotes[i] = new VoiceNote { Time = song.Bass[i].Time, Length = 0.5, LowFret = 1, FretCount = 1 };
                }
                GuitarVoicePitcher.Assign(harmony, bassNotes, true);
                int bassRight = 0;
                bool bassRange = true;
                for (int i = 0; i < bassNotes.Length; i++)
                {
                    bassRight += bassNotes[i].Pitch % 12 == song.Bass[i].Midi % 12 ? 1 : 0;
                    bassRange &= bassNotes[i].Pitch >= 28 && bassNotes[i].Pitch <= 52;
                }
                check(bassRight >= bassNotes.Length * 0.9, $"{label}: bass plays the bass line's note ({bassRight}/{bassNotes.Length})");
                check(bassRange, $"{label}: all notes within a bass's range");

                if (firstSong == null)
                {
                    firstSong = song;
                    firstNotes = notes;
                    firstHarmony = harmony;
                }
            }

            // Silence and very short audio must not crash anything
            {
                var quiet = SongHarmony.Analyze(new float[SongHarmony.SAMPLE_RATE * 6]);
                var notes = new[]
                {
                    new VoiceNote { Time = 1, LowFret = 1, FretCount = 1 },
                    new VoiceNote { Time = 2, LowFret = 3, FretCount = 2 },
                    new VoiceNote { Time = 3, LowFret = 0, FretCount = 1 },
                };
                GuitarVoicePitcher.Assign(quiet, notes, false);
                check(notes[1].Pitch > notes[0].Pitch && notes[2].Pitch < notes[1].Pitch,
                    "silent song: pitch still follows the frets");
                SongHarmony.Analyze(new float[100]);
                GuitarVoicePitcher.Assign(quiet, new VoiceNote[0], false);
                check(true, "silent and tiny inputs handled");
            }

            // ---- The recorded notes that ship with the game
            {
                check(!guitarBank.IsBass && bassBank.IsBass, "recorded notes: one bank for guitar, one for bass");
                foreach (var (bank, name, lowest, highest) in new[] { (guitarBank, "guitar", 40, 84), (bassBank, "bass", 28, 62) })
                {
                    var notes = bank.Notes;
                    bool takes = true, spaced = true, tuned = true, clean = true;
                    for (int i = 0; i < notes.Length; i++)
                    {
                        takes &= notes[i].Picked.Length >= 3;
                        spaced &= i == 0 || notes[i].Number - notes[i - 1].Number <= 4;
                    }
                    foreach (var take in bank.Recordings)
                    {
                        tuned &= Math.Abs(take.Pitch - take.Note) < 0.35f && take.Gain > 0.05f && take.Gain < 20f;
                        // Starts and ends at rest, and has a note in between
                        var data = take.Samples;
                        int loudest = 0;
                        foreach (short v in data)
                        {
                            loudest = Math.Max(loudest, Math.Abs((int) v));
                        }
                        clean &= Math.Abs((int) data[0]) < 400 && Math.Abs((int) data[data.Length - 1]) < 40 && loudest > 20000;
                    }
                    check(notes[0].Number <= lowest && notes[notes.Length - 1].Number >= highest,
                        $"recorded {name}: notes {notes[0].Number} to {notes[notes.Length - 1].Number} cover the range played");
                    check(spaced, $"recorded {name}: never more than four semitones between recorded notes");
                    check(takes, $"recorded {name}: at least three picked takes of every note ({bank.Recordings.Length} takes in all)");
                    check(tuned, $"recorded {name}: every take's own pitch is known");
                    check(clean, $"recorded {name}: every take starts and ends at rest");
                }

                // A cut-off or scrambled file must be refused, not crash or hang
                var file = File.ReadAllBytes(BankPath("bass.bank"));
                int refused = 0, tried = 0;
                foreach (int length in new[] { 0, 3, 15, 16, 200, file.Length / 3, file.Length - 1 })
                {
                    tried++;
                    var cut = new byte[length];
                    Array.Copy(file, cut, length);
                    try
                    {
                        StringBank.Load(cut);
                    }
                    catch (InvalidDataException)
                    {
                        refused++;
                    }
                }
                check(refused == tried, $"recorded notes: a cut-off file is refused ({refused} of {tried})");
                var scrambled = (byte[]) file.Clone();
                for (int i = 16; i < scrambled.Length; i += 97)
                {
                    scrambled[i] ^= 0x5A;
                }
                bool survived = true;
                try
                {
                    StringBank.Load(scrambled);
                }
                catch (InvalidDataException)
                {
                }
                catch (Exception)
                {
                    survived = false;
                }
                check(survived, "recorded notes: a scrambled file is refused or read, never a crash");
            }

            // ---- Playing: on the recorded strings, and on the modelled string that stands in
            // when the recordings cannot be read
            foreach (var (strings, bassStrings, how) in new[] { (guitarBank, bassBank, "recorded"), ((StringBank) null, (StringBank) null, "modelled") })
            {
                foreach (var tone in new[] { GuitarVoiceTone.Clean, GuitarVoiceTone.Rock, GuitarVoiceTone.Heavy })
                {
                    foreach (int midi in new[] { 40, 52, 64, 76 })
                    {
                        const float SONG_CENTS = 23f;
                        var one = new[] { new VoiceNote { Time = 0.1, Length = 0.4, LowFret = 1, FretCount = 1, Pitch = midi } };
                        var audio = GuitarVoiceSynth.Render(one, 2.5, tone, false, SONG_CENTS, 0.1f, strings);
                        double hz = 440.0 * Math.Pow(2, (midi - 69) / 12.0) * Math.Pow(2, SONG_CENTS / 1200.0);
                        double off = PitchOff(audio, 2, 0, (int) (0.16 * SR), (int) (0.3 * SR), hz, SR);
                        check(Math.Abs(off) <= 4, $"{how} {tone} guitar, note {midi}: in tune with the song ({off:+0;-0;0} cents)");

                        if (midi == 52)
                        {
                            check(audio.Length == (int) (2.5 * SR) * 2, $"{how} {tone}: stereo, full length");
                            bool finite = true;
                            foreach (float v in audio)
                            {
                                finite &= !float.IsNaN(v) && !float.IsInfinity(v);
                            }
                            check(finite, $"{how} {tone}: no broken samples");
                            check(Peak(audio, 0, (int) (0.095 * SR) * 2) < 1e-4f, $"{how} {tone}: silent before the note");
                            float playing = Peak(audio, (int) (0.1 * SR) * 2, (int) (0.5 * SR) * 2);
                            check(playing > 0.05f && playing < 0.99f, $"{how} {tone}: sounds during the note without clipping (peak {playing:0.00})");
                            float after = Peak(audio, (int) (2.0 * SR) * 2, audio.Length);
                            check(after < playing * 0.03f, $"{how} {tone}: stops after the note ends ({after / playing:0.000} of peak)");
                        }
                    }
                }

                // Bass
                {
                    var one = new[] { new VoiceNote { Time = 0.1, Length = 0.4, LowFret = 1, FretCount = 1, Pitch = 33 } };
                    var audio = GuitarVoiceSynth.Render(one, 2.0, GuitarVoiceTone.Rock, true, 0f, 0.1f, bassStrings);
                    double off = PitchOff(audio, 2, 0, (int) (0.16 * SR), (int) (0.3 * SR), 55.0, SR);
                    check(Math.Abs(off) <= 6, $"{how} bass, low A: in tune ({off:+0;-0;0} cents)");
                }

                // A chord holds root, fifth and octave
                {
                    var chord = new[] { new VoiceNote { Time = 0.1, Length = 0.4, LowFret = 1, FretCount = 3, Pitch = 45 } };
                    var audio = GuitarVoiceSynth.Render(chord, 1.5, GuitarVoiceTone.Clean, false, 0f, 0.1f, strings);
                    foreach (int midi in new[] { 45, 52, 57 })
                    {
                        double hz = 440.0 * Math.Pow(2, (midi - 69) / 12.0);
                        double off = CentsOff(audio, 2, 0, (int) (0.16 * SR), (int) (0.3 * SR), hz, SR);
                        // Wider margin than for single notes: through an amplifier the three strings
                        // produce faint extra tones a few cents away from each (as on a real guitar)
                        check(Math.Abs(off) <= 12, $"{how} chord contains note {midi} ({off:+0;-0;0} cents)");
                    }
                }

                // Freestyle: separate recordings of single notes and chords, ready to play at once
                {
                    var wanted = new[]
                    {
                        new VoiceNote { Pitch = 45, FretCount = 1 },
                        new VoiceNote { Pitch = 57, FretCount = 1 },
                        new VoiceNote { Pitch = 45, FretCount = 3 },
                    };
                    var bank = GuitarVoiceSynth.RenderBank(wanted, 3.0, GuitarVoiceTone.Rock, false, 0f, 0.1f, strings);
                    check(bank.Length == 3 && bank[0].Length == 3 * SR && bank[2].Length == 3 * SR,
                        $"{how} freestyle: one three-second recording per note");
                    bool startsAtOnce = true, endsQuiet = true;
                    var asFloat = new float[bank.Length][];
                    for (int i = 0; i < bank.Length; i++)
                    {
                        asFloat[i] = new float[bank[i].Length];
                        for (int k = 0; k < bank[i].Length; k++)
                        {
                            asFloat[i][k] = bank[i][k] / 32768f;
                        }
                        startsAtOnce &= Peak(asFloat[i], 0, (int) (0.01 * SR)) > 0.02f;
                        endsQuiet &= Peak(asFloat[i], bank[i].Length - (int) (0.005 * SR), bank[i].Length) < 0.01f;
                    }
                    check(startsAtOnce, $"{how} freestyle: every recording sounds from its first moment");
                    check(endsQuiet, $"{how} freestyle: every recording ends quietly (no click)");
                    double off = PitchOff(asFloat[1], 1, 0, (int) (0.06 * SR), (int) (0.22 * SR), 220.0, SR);
                    check(Math.Abs(off) <= 4, $"{how} freestyle: the A is an A ({off:+0;-0;0} cents)");
                    float single = Peak(asFloat[0], 0, SR), chord = Peak(asFloat[2], 0, SR);
                    check(chord < single * 2.5f && single < chord * 2.5f,
                        $"{how} freestyle: single notes and chords are balanced ({single:0.00} and {chord:0.00})");
                }

                // Loudness lands on the target, measured the way the ear weighs it, so the
                // three sounds come out equally loud to listen to
                foreach (var tone in new[] { GuitarVoiceTone.Clean, GuitarVoiceTone.Rock, GuitarVoiceTone.Heavy })
                {
                    var run = new VoiceNote[16];
                    for (int i = 0; i < run.Length; i++)
                    {
                        run[i] = new VoiceNote { Time = 0.2 + i * 0.25, LowFret = 1 + i % 5, FretCount = 1, Pitch = 52 + (i % 5) * 2 };
                    }
                    var audio = GuitarVoiceSynth.Render(run, 5.0, tone, false, 0f, 0.08f, strings);
                    double loudness = EarWeightedRms(audio, (int) (0.3 * SR), (int) (4.0 * SR), SR);
                    check(loudness > 0.065 && loudness < 0.095, $"{how} {tone}: loudness on target (asked 0.080, got {loudness:0.000})");
                }

                // The whole way: hear a song, play its chart, and check the result against the song
                {
                    GuitarVoicePitcher.Assign(firstHarmony, firstNotes, false);
                    var audio = GuitarVoiceSynth.Render(firstNotes, firstHarmony.Seconds, GuitarVoiceTone.Clean, false,
                        firstHarmony.Cents, 0.1f, strings);
                    var low = new float[audio.Length / 2 / 4];
                    for (int i = 0; i < low.Length; i++)
                    {
                        float sum = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            sum += audio[(i * 4 + k) * 2];
                        }
                        low[i] = sum / 4;
                    }
                    var heard = SongHarmony.Analyze(low);
                    check(Math.Abs(heard.Cents - firstHarmony.Cents) < 6,
                        $"{how} guitar is tuned like the song (song {firstHarmony.Cents:+0.0;-0.0}, guitar {heard.Cents:+0.0;-0.0} cents)");
                }

                // A note starts exactly when the chart says
                foreach (double time in new[] { 0.5, 1.2345, 2.00001 })
                {
                    var one = new[] { new VoiceNote { Time = time, LowFret = 1, FretCount = 1, Pitch = 57 } };
                    var audio = GuitarVoiceSynth.Render(one, 3.0, GuitarVoiceTone.Rock, false, 0f, 0.1f, strings);
                    int first = 0;
                    while (first < audio.Length && Math.Abs(audio[first]) < 1e-4f)
                    {
                        first++;
                    }
                    double startedAt = first / 2 / (double) SR;
                    check(startedAt >= time - 0.0005 && startedAt < time + 0.002,
                        $"{how} note at {time:0.000} s starts on time (first sound at {startedAt:0.0000} s)");
                }
            }

            // ---- What makes it a guitar and not a beep
            {
                // The same note twice is two different takes, like two picks of a real string
                var twice = new[]
                {
                    new VoiceNote { Time = 0.1, LowFret = 1, FretCount = 1, Pitch = 55 },
                    new VoiceNote { Time = 1.1, LowFret = 1, FretCount = 1, Pitch = 55 },
                };
                var bare = new float[2 * SR];
                StringPlayer.Play(bare, twice, guitarBank, false, 1.0, 0.6, 1.0);
                double same = 0, first = 0, second = 0;
                for (int i = (int) (0.1 * SR); i < (int) (0.5 * SR); i++)
                {
                    double x = bare[i], y = bare[i + SR];
                    same += x * y;
                    first += x * x;
                    second += y * y;
                }
                double alike = same / Math.Sqrt(first * second);
                check(first > 0 && second > 0 && alike < 0.98,
                    $"the same note twice in a row is two different takes (alike {alike:0.00}, 1.00 would be a copy)");

                // A hammer-on is softer than a picked note and has no pick click
                var picked = new float[SR];
                var hammered = new float[SR];
                StringPlayer.Play(picked, new[] { new VoiceNote { Time = 0.1, LowFret = 1, FretCount = 1, Pitch = 52 } },
                    guitarBank, false, 1.0, 0.6, 1.0);
                StringPlayer.Play(hammered, new[] { new VoiceNote { Time = 0.1, LowFret = 1, FretCount = 1, Pitch = 52, Soft = true } },
                    guitarBank, false, 1.0, 0.6, 1.0);
                float pickedPeak = Peak(picked, 0, SR), hammeredPeak = Peak(hammered, 0, SR);
                check(hammeredPeak > 0.05f && hammeredPeak < 0.85f * pickedPeak,
                    $"a hammer-on is softer than a picked note ({hammeredPeak:0.00} against {pickedPeak:0.00})");

                // A held low note is still ringing after five seconds (its own long take, not a loop)
                var held = new[] { new VoiceNote { Time = 0.1, Length = 6.0, LowFret = 1, FretCount = 1, Pitch = 41 } };
                var ring = GuitarVoiceSynth.Render(held, 7.0, GuitarVoiceTone.Rock, false, 0f, 0.1f, guitarBank);
                float early = Peak(ring, (int) (0.2 * SR) * 2, (int) (0.6 * SR) * 2);
                float late = Peak(ring, (int) (5.0 * SR) * 2, (int) (5.5 * SR) * 2);
                float gone = Peak(ring, (int) (6.6 * SR) * 2, ring.Length);
                check(late > 0.1f * early && gone < 0.03f * early,
                    $"a held low note still rings after five seconds, and stops when let go ({late / early:0.00} of its start)");

                // The amplifier: bite where a real guitar amp has it. Recordings of real distorted
                // amps playing the low A have 30 to 50% of their energy between 2 and 5 kHz (the
                // speaker's peak); the old generated sound had under 1% there, which is why it
                // sounded like a beep. Above 8 kHz a guitar speaker gives almost nothing.
                foreach (var tone in new[] { GuitarVoiceTone.Rock, GuitarVoiceTone.Heavy })
                {
                    double bite = 0, fizz = 0;
                    const int TAKES = 4;
                    var again = new VoiceNote[TAKES];
                    for (int i = 0; i < TAKES; i++)
                    {
                        again[i] = new VoiceNote { Time = 0.1 + i * 2.0, Length = 1.5, LowFret = 1, FretCount = 1, Pitch = 45 };
                    }
                    var audio = GuitarVoiceSynth.Render(again, TAKES * 2.0, tone, false, 0f, 0.1f, guitarBank);
                    for (int i = 0; i < TAKES; i++)
                    {
                        int from = (int) ((0.2 + i * 2.0) * SR), to = (int) ((1.4 + i * 2.0) * SR);
                        bite += BandShare(audio, from, to, 2000, 5000, SR) / TAKES;
                        fizz += BandShare(audio, from, to, 8000, 20000, SR) / TAKES;
                    }
                    check(bite > 0.15 && bite < 0.7, $"{tone}: bite of a real amp, {100 * bite:0}% of the sound between 2 and 5 kHz");
                    check(fizz < 0.01, $"{tone}: no fizz, {100 * fizz:0.00}% above 8 kHz");
                }

                // Overdrive holds a note up as it dies away; a clean amp lets it fall
                double Hold(GuitarVoiceTone tone)
                {
                    var one = new[] { new VoiceNote { Time = 0.1, Length = 2.6, LowFret = 1, FretCount = 1, Pitch = 64 } };
                    var audio = GuitarVoiceSynth.Render(one, 3.2, tone, false, 0f, 0.1f, guitarBank);
                    return Rms(audio, (int) (2.0 * SR) * 2, (int) (2.5 * SR) * 2) / Rms(audio, (int) (0.2 * SR) * 2, (int) (0.7 * SR) * 2);
                }
                double cleanHold = Hold(GuitarVoiceTone.Clean), rockHold = Hold(GuitarVoiceTone.Rock), heavyHold = Hold(GuitarVoiceTone.Heavy);
                check(cleanHold < rockHold && rockHold < heavyHold && cleanHold < 0.5 && heavyHold > 0.5,
                    $"more drive holds a note longer (level after two seconds: clean {cleanHold:0.00}, rock {rockHold:0.00}, heavy {heavyHold:0.00} of the start)");

                // A long song is amplified in pieces on several processor cores. The joins must
                // not show: the result has to be what amplifying it in one go gives.
                {
                    var many = new List<VoiceNote>();
                    for (int i = 0; i < 90; i++)
                    {
                        many.Add(new VoiceNote
                        {
                            Time = 0.2 + i * 0.31, LowFret = 1, FretCount = i % 7 == 3 ? 3 : 1, Pitch = 40 + (i * 7) % 29,
                            Soft = i % 5 == 4, Length = i % 9 == 0 ? 0.9 : 0,
                        });
                    }
                    var whole = new float[30 * SR];
                    StringPlayer.Play(whole, many.ToArray(), guitarBank, false, 1.0, 0.6, 1.0);
                    var pieces = (float[]) whole.Clone();
                    GuitarAmp.Run(whole, AmpSettings.Heavy, int.MaxValue);
                    GuitarAmp.Run(pieces, AmpSettings.Heavy);
                    float apart = 0;
                    for (int i = 0; i < whole.Length; i++)
                    {
                        apart = Math.Max(apart, Math.Abs(whole[i] - pieces[i]));
                    }
                    float loud = Peak(whole, 0, whole.Length);
                    check(loud > 0.1f && apart < 1e-4f * loud,
                        $"amplifying a song in pieces gives the same as in one go (apart by {apart / loud:0.0e0} of the peak)");
                }

                // Silence in, silence out (no hum, no hiss, nothing left ringing)
                var nothing = GuitarVoiceSynth.Render(new VoiceNote[0], 2.0, GuitarVoiceTone.Heavy, false, 0f, 0.1f, guitarBank);
                check(Peak(nothing, 0, nothing.Length) == 0f, "no notes, no sound");
            }

            // ---- WAV file
            {
                var wav = GuitarVoiceSynth.ToWav(new[] { 0f, 1f, -1f, 0.5f }, 44100, 2);
                check(wav.Length == 44 + 8 && wav[0] == 'R' && wav[8] == 'W' && wav[22] == 2
                    && BitConverter.ToInt32(wav, 40) == 8 && BitConverter.ToInt16(wav, 46) == 32767
                    && BitConverter.ToInt16(wav, 48) == -32767, "WAV file is well formed");
            }

            // ---- A demo to listen to: the same riff in each of the three tones, then on bass
            if (!string.IsNullOrEmpty(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                var riff = new List<VoiceNote>();
                void Add(double beat, double beats, int pitch, int frets = 1, bool soft = false)
                {
                    const double BEAT = 0.5;
                    riff.Add(new VoiceNote
                    {
                        Time = 0.3 + beat * BEAT, Length = beats >= 1 ? beats * BEAT * 0.95 : 0,
                        LowFret = 1, FretCount = frets, Soft = soft, Pitch = pitch,
                    });
                }
                // E minor: a picked run with hammer-ons, a held note, then power chords
                Add(0, 0.5, 52); Add(0.5, 0.5, 55); Add(1, 0.5, 57, 1, true); Add(1.5, 0.5, 59);
                Add(2, 0.5, 62); Add(2.5, 0.5, 59, 1, true); Add(3, 0.5, 57); Add(3.5, 0.5, 55, 1, true);
                Add(4, 3, 64);
                Add(8, 1.5, 40, 3); Add(9.5, 0.5, 40, 2); Add(10, 1.5, 43, 3); Add(11.5, 0.5, 43, 2);
                Add(12, 1.5, 45, 3); Add(13.5, 0.5, 45, 2); Add(14, 2, 47, 3);
                var notes = riff.ToArray();

                var all = new List<float>();
                foreach (var tone in new[] { GuitarVoiceTone.Clean, GuitarVoiceTone.Rock, GuitarVoiceTone.Heavy })
                {
                    all.AddRange(GuitarVoiceSynth.Render(notes, 9.5, tone, false, 0f, 0.12f, guitarBank));
                }
                var bassLine = new List<VoiceNote>();
                for (int i = 0; i < 16; i++)
                {
                    int[] roots = { 28, 28, 31, 33 };
                    bassLine.Add(new VoiceNote { Time = 0.3 + i * 0.5, LowFret = 1, FretCount = 1, Pitch = roots[i / 4] + (i % 4 == 3 ? 12 : 0) });
                }
                all.AddRange(GuitarVoiceSynth.Render(bassLine.ToArray(), 9.5, GuitarVoiceTone.Rock, true, 0f, 0.12f, bassBank));

                string path = Path.Combine(outputFolder, "guitar-sound-demo.wav");
                File.WriteAllBytes(path, GuitarVoiceSynth.ToWav(all.ToArray(), SR, 2));
                check(new FileInfo(path).Length > 1_000_000, "demo recording written: " + path);
            }
        }
    }
}
