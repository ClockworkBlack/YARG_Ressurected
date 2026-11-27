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
            var transform = new Fourier(N);
            var re = new double[N];
            var im = new double[N];
            double inBand = 0, total = 0;
            for (int start = from; start + N <= to; start += N)
            {
                for (int i = 0; i < N; i++)
                {
                    re[i] = stereo[(start + i) * 2] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N));
                    im[i] = 0;
                }
                transform.Run(re, im, false);
                for (int k = 1; k < N / 2; k++)
                {
                    double power = re[k] * re[k] + im[k] * im[k];
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

        // The left channel of stereo[start .. start + length) (frames), faded in and out
        private static double[] Windowed(float[] stereo, int start, int length)
        {
            var windowed = new double[length];
            for (int i = 0; i < length; i++)
            {
                windowed[i] = stereo[(start + i) * 2] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / length));
            }
            return windowed;
        }

        // Power of the tone at `hz` in a stretch from Windowed. A string's overtones sit a few
        // cents off exact multiples, so a little either side counts.
        private static double TonePower(double[] windowed, double hz, int sampleRate)
        {
            double power = 0;
            for (int c = -30; c <= 30; c += 15)
            {
                double w = 2 * Math.PI * hz * Math.Pow(2, c / 1200.0) / sampleRate;
                double stepRe = Math.Cos(w), stepIm = -Math.Sin(w), re = 1, im = 0, sumRe = 0, sumIm = 0;
                for (int i = 0; i < windowed.Length; i++)
                {
                    sumRe += windowed[i] * re;
                    sumIm += windowed[i] * im;
                    double next = re * stepRe - im * stepIm;
                    im = re * stepIm + im * stepRe;
                    re = next;
                }
                power += sumRe * sumRe + sumIm * sumIm;
            }
            return power;
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

                // Every guitar take is balanced before the amplifier (StringVoicing)
                {
                    bool strings = true, sane = true;
                    int filled = 0, tamed = 0;
                    float mostFilled = 0, mostTamed = 0;
                    foreach (var take in guitarBank.Recordings)
                    {
                        strings &= take.String >= 1 && take.String <= 6;
                        sane &= take.Full >= 0f && take.Full <= 12f && take.Top <= 0f && take.Top >= -10f
                            && take.Trim > 0.2f && take.Trim < 5f;
                        filled += take.Full > 0f ? 1 : 0;
                        tamed += take.Top < 0f ? 1 : 0;
                        mostFilled = Math.Max(mostFilled, take.Full);
                        mostTamed = Math.Min(mostTamed, take.Top);
                    }
                    check(strings, "recorded guitar: every take knows which string it was played on");
                    check(sane && filled > 20 && tamed > 0,
                        $"recorded guitar: takes balanced ({filled} given more fundamental, up to {mostFilled:0.0} dB; {tamed} with the top turned down, by up to {-mostTamed:0.0} dB)");
                    bool untouched = true;
                    foreach (var take in bassBank.Recordings)
                    {
                        untouched &= take.Full == 0f && take.Top == 0f && take.Trim == 1f;
                    }
                    check(untouched, "recorded bass: left as recorded");
                }

                // The measured speaker cabinets
                foreach (var (response, name) in new[] { (Cabinets.Greenback, "Greenback"), (Cabinets.Vintage30, "Vintage 30") })
                {
                    double energy = 0, late = 0;
                    bool finite = response.Length == Cabinets.LENGTH;
                    for (int i = 0; i < response.Length; i++)
                    {
                        finite &= !float.IsNaN(response[i]) && !float.IsInfinity(response[i]);
                        energy += (double) response[i] * response[i];
                        late += i >= response.Length - 64 ? Math.Abs(response[i]) : 0;
                    }
                    check(finite && Math.Abs(energy - 1) < 1e-3 && late < 1e-3,
                        $"{name} cabinet: {response.Length} samples, unit energy, ends at rest");

                    // What a guitar speaker does: a thump around 100 to 200 Hz, less in the middle
                    // (how much less differs from speaker to speaker), bite at 2 to 4 kHz, and next
                    // to nothing above 9 kHz or below 45 Hz
                    double Gain(double lo, double hi)
                    {
                        // (how much gets through, averaged over lo .. hi)
                        const int POINTS = 24;
                        double sum = 0;
                        for (int p = 0; p < POINTS; p++)
                        {
                            double w = 2 * Math.PI * lo * Math.Pow(hi / lo, (p + 0.5) / POINTS) / SR;
                            double re = 0, im = 0;
                            for (int i = 0; i < response.Length; i++)
                            {
                                re += response[i] * Math.Cos(w * i);
                                im -= response[i] * Math.Sin(w * i);
                            }
                            sum += re * re + im * im;
                        }
                        return sum / POINTS;
                    }
                    double thump = Gain(110, 180), middle = Gain(450, 900), bite = Gain(2200, 4200);
                    double top = Gain(9000, 18000), rumble = Gain(20, 45);
                    check(thump > 1.5 * middle && bite > middle && top < 0.003 * bite && rumble < 0.05 * thump,
                        $"{name} cabinet: a guitar speaker's shape (thump {10 * Math.Log10(thump / middle):+0.0} dB and bite {10 * Math.Log10(bite / middle):+0.0} dB over the middle, {10 * Math.Log10(top / bite):0} dB above 9 kHz)");
                }

                // Playing through a cabinet the quick way gives what the slow way gives
                {
                    _seed = 4242;
                    var input = new float[5000];
                    for (int i = 0; i < input.Length; i++)
                    {
                        input[i] = (float) (Rand() - 0.5);
                    }
                    var speaker = new Speaker(Cabinets.Vintage30);
                    var quick = new float[input.Length];
                    speaker.Run(input, input.Length, 0, quick, 0);
                    const int FIRST = 3111;
                    var fromLater = new float[input.Length - FIRST];
                    speaker.Run(input, input.Length, FIRST, fromLater, 0);
                    double worst = 0, worstLater = 0;
                    for (int i = 0; i < input.Length; i++)
                    {
                        double slow = 0;
                        for (int k = 0; k <= i && k < Cabinets.LENGTH; k++)
                        {
                            slow += (double) Cabinets.Vintage30[k] * input[i - k];
                        }
                        worst = Math.Max(worst, Math.Abs(slow - quick[i]));
                        if (i >= FIRST)
                        {
                            worstLater = Math.Max(worstLater, Math.Abs(slow - fromLater[i - FIRST]));
                        }
                    }
                    check(worst < 1e-5 && worstLater < 1e-5,
                        $"speaker: the quick way matches sample-by-sample (off by {worst:0.0e0}; started part-way {worstLater:0.0e0})");
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

                // A picked note stops the one before it a moment early (the pick lands on the
                // string before it lets go); a hammer-on runs straight on from it
                {
                    float[] Two(bool hammer)
                    {
                        var pair = new[]
                        {
                            new VoiceNote { Time = 0.1, LowFret = 1, FretCount = 1, Pitch = 52 },
                            new VoiceNote { Time = 0.5, LowFret = 2, FretCount = 1, Pitch = 55, Soft = hammer },
                        };
                        var signal = new float[SR];
                        StringPlayer.Play(signal, pair, guitarBank, false, 1.0, 0.6, 1.0);
                        return signal;
                    }
                    var pickedPair = Two(false);
                    var hammeredPair = Two(true);
                    float ringing = Peak(pickedPair, (int) (0.44 * SR), (int) (0.48 * SR));
                    float stopped = Peak(pickedPair, (int) (0.495 * SR), (int) (0.4995 * SR));
                    float carried = Peak(hammeredPair, (int) (0.495 * SR), (int) (0.4995 * SR));
                    check(ringing > 0.02f && stopped < 0.02f * ringing,
                        $"a picked note stops the one before it just ahead of the pick ({stopped / ringing:0.000} of it left)");
                    check(carried > 0.2f * ringing, $"a hammer-on runs on from the note before ({carried / ringing:0.00} of it still sounding)");
                }

                // A held note stays on its pitch: no added wobble
                {
                    var one = new[] { new VoiceNote { Time = 0.1, Length = 2.6, LowFret = 1, FretCount = 1, Pitch = 62 } };
                    var audio = GuitarVoiceSynth.Render(one, 3.2, GuitarVoiceTone.Rock, false, 0f, 0.1f, guitarBank);
                    double hz = 440.0 * Math.Pow(2, (62 - 69) / 12.0);
                    double lowest = 100, highest = -100;
                    for (double t = 0.4; t < 2.3; t += 0.4)
                    {
                        double off = PitchOff(audio, 2, 0, (int) (t * SR), (int) (0.2 * SR), hz, SR);
                        lowest = Math.Min(lowest, off);
                        highest = Math.Max(highest, off);
                    }
                    check(highest - lowest <= 6 && Math.Abs(highest) <= 8 && Math.Abs(lowest) <= 8,
                        $"a held note stays on its pitch ({lowest:+0;-0;0} to {highest:+0;-0;0} cents over two seconds)");
                }

                // The amplifier, note by note up the neck. What made the earlier sounds beep was a
                // weak fundamental under a pile of overtones: a real amp gives a note its body
                // first (the fundamental among its strongest parts) and bite second (the
                // speaker's 2 to 5 kHz, a tenth to a third of the sound). Above 8 kHz a guitar
                // speaker gives almost nothing.
                foreach (var tone in new[] { GuitarVoiceTone.Rock, GuitarVoiceTone.Heavy })
                {
                    int[] neck = { 48, 50, 52, 55, 57, 59, 62, 64, 67, 69, 72, 74, 76 };
                    const int TAKES = 4;
                    double thinnest = 0, brightest = 0, fizziest = 0, biteAll = 0;
                    int thinnestNote = 0, brightestNote = 0;
                    foreach (int midi in neck)
                    {
                        var again = new VoiceNote[TAKES];
                        for (int i = 0; i < TAKES; i++)
                        {
                            again[i] = new VoiceNote { Time = 0.1 + i * 2.0, Length = 1.5, LowFret = 1, FretCount = 1, Pitch = midi };
                        }
                        var audio = GuitarVoiceSynth.Render(again, TAKES * 2.0, tone, false, 0f, 0.1f, guitarBank);
                        double hz = 440.0 * Math.Pow(2, (midi - 69) / 12.0);
                        double body = 0, bite = 0, fizz = 0;
                        for (int i = 0; i < TAKES; i++)
                        {
                            int from = (int) ((0.2 + i * 2.0) * SR), to = (int) ((1.4 + i * 2.0) * SR);
                            bite += BandShare(audio, from, to, 2000, 5000, SR) / TAKES;
                            fizz += BandShare(audio, from, to, 8000, 20000, SR) / TAKES;
                            // The fundamental against the strongest of the next five overtones
                            var stretch = Windowed(audio, from, (int) (0.5 * SR));
                            double fundamental = TonePower(stretch, hz, SR), strongest = 0;
                            for (int h = 2; h <= 6; h++)
                            {
                                strongest = Math.Max(strongest, TonePower(stretch, hz * h, SR));
                            }
                            body += 10 * Math.Log10(fundamental / strongest) / TAKES;
                        }
                        if (body < thinnest)
                        {
                            thinnest = body;
                            thinnestNote = midi;
                        }
                        if (bite > brightest)
                        {
                            brightest = bite;
                            brightestNote = midi;
                        }
                        fizziest = Math.Max(fizziest, fizz);
                        biteAll += bite / neck.Length;
                    }
                    check(thinnest > -8,
                        $"{tone}: every note has body (weakest fundamental {thinnest:+0.0;-0.0} dB against its strongest overtone, at note {thinnestNote}; the beeping sound was at -9 to -15)");
                    check(brightest < 0.55 && biteAll > 0.08 && biteAll < 0.40,
                        $"{tone}: bite without the beep ({100 * biteAll:0}% of the sound between 2 and 5 kHz on average, {100 * brightest:0}% at most, at note {brightestNote})");
                    check(fizziest < 0.01, $"{tone}: no fizz, {100 * fizziest:0.00}% above 8 kHz at most");
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

            // ---- The guitar rises and falls with the song
            {
                const int SONG_RATE = SongHarmony.SAMPLE_RATE;
                // A song that is quiet for its first third (15.6 dB down), then at full level
                var quietLoud = new float[SONG_RATE * 60];
                var steady = new float[SONG_RATE * 60];
                for (int i = 0; i < quietLoud.Length; i++)
                {
                    float noise = (float) (Rand() * 2 - 1);
                    quietLoud[i] = noise * (i < SONG_RATE * 20 ? 0.05f : 0.3f);
                    steady[i] = noise * 0.3f;
                }

                float[] Tone()
                {
                    var y = new float[SR * 60 * 2];
                    for (int i = 0; i < y.Length / 2; i++)
                    {
                        y[2 * i] = y[2 * i + 1] = (float) (0.1 * Math.Sin(2 * Math.PI * 220 * i / SR));
                    }
                    return y;
                }

                var followed = Tone();
                GuitarVoiceSynth.FollowSong(followed, quietLoud, SONG_RATE);
                double quietDb = 20 * Math.Log10(Rms(followed, SR * 2 * 5, SR * 2 * 15));
                double loudDb = 20 * Math.Log10(Rms(followed, SR * 2 * 30, SR * 2 * 55));
                double plainDb = 20 * Math.Log10(Rms(Tone(), SR * 2 * 30, SR * 2 * 55));
                check(loudDb - quietDb > 7.5 && loudDb - quietDb < 10.5,
                    $"guitar is quieter where the song is quiet ({loudDb - quietDb:0.0} dB for a song 15.6 dB quieter)");
                check(Math.Abs(loudDb - plainDb) < 0.7, $"guitar keeps its level where the song is at its usual level ({loudDb - plainDb:+0.0;-0.0} dB)");

                // The change from quiet to loud is a swell, not a step: under 1 dB in any 50 ms
                double biggestStep = 0;
                int block = SR / 20;
                double before = 0;
                for (int b = SR * 15 / block; b < SR * 26 / block; b++)
                {
                    double now = 20 * Math.Log10(Rms(followed, b * block * 2, (b + 1) * block * 2) + 1e-9);
                    if (before != 0)
                    {
                        biggestStep = Math.Max(biggestStep, Math.Abs(now - before));
                    }
                    before = now;
                }
                check(biggestStep < 1.0, $"the level moves smoothly ({biggestStep:0.00} dB at most in 50 ms)");

                var same = Tone();
                GuitarVoiceSynth.FollowSong(same, steady, SONG_RATE);
                double sameDb = 20 * Math.Log10(Rms(same, SR * 2 * 5, SR * 2 * 55));
                check(Math.Abs(sameDb - plainDb) < 0.5, $"a song at one level leaves the guitar alone ({Math.Abs(sameDb - plainDb):0.00} dB)");
            }

            // ---- WAV file
            {
                var wav = GuitarVoiceSynth.ToWav(new[] { 0f, 1f, -1f, 0.5f }, 44100, 2);
                check(wav.Length == 44 + 8 && wav[0] == 'R' && wav[8] == 'W' && wav[22] == 2
                    && BitConverter.ToInt32(wav, 40) == 8 && BitConverter.ToInt16(wav, 46) == 32767
                    && BitConverter.ToInt16(wav, 48) == -32767, "WAV file is well formed");
            }

            // ---- A demo to listen to: the same four parts in each of the three sounds, then a bass
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
                // 1. A riff of single notes on the low strings, every one picked
                int[] low = { 45, 45, 48, 45, 50, 45, 48, 47, 45, 45, 48, 45, 52, 50, 48, 47 };
                for (int i = 0; i < low.Length; i++)
                {
                    Add(i * 0.5, 0.5, low[i]);
                }
                // 2. A tune in the middle of the neck, with hammer-ons and pull-offs, ending on a held note
                Add(8, 0.5, 57); Add(8.5, 0.5, 59, 1, true); Add(9, 0.5, 60); Add(9.5, 0.5, 62, 1, true);
                Add(10, 1, 64); Add(11, 0.5, 62); Add(11.5, 0.5, 60, 1, true);
                Add(12, 0.5, 59); Add(12.5, 0.5, 57, 1, true); Add(13, 1, 55); Add(14, 2, 57);
                // 3. Higher up: a run, the same note four times (no two picks alike), a long held note
                Add(16, 0.5, 64); Add(16.5, 0.5, 67); Add(17, 0.5, 69); Add(17.5, 0.5, 71); Add(18, 1, 72);
                for (int i = 0; i < 4; i++)
                {
                    Add(19 + i * 0.25, 0.25, 69);
                }
                Add(20, 0.5, 67); Add(20.5, 0.5, 64); Add(21, 3, 69);
                // 4. Power chords: held, then strummed quickly down and up
                Add(24, 1.5, 40, 3); Add(25.5, 0.5, 40, 2); Add(26, 1.5, 43, 3); Add(27.5, 0.5, 43, 2);
                for (int i = 0; i < 6; i++)
                {
                    Add(28 + i * 0.25, 0.25, 45, 3);
                }
                Add(29.5, 0.5, 45, 2); Add(30, 2.5, 47, 3);
                var notes = riff.ToArray();
                const double SECONDS = 18.0;

                var all = new List<float>();
                foreach (var tone in new[] { GuitarVoiceTone.Clean, GuitarVoiceTone.Rock, GuitarVoiceTone.Heavy })
                {
                    all.AddRange(GuitarVoiceSynth.Render(notes, SECONDS, tone, false, 0f, 0.12f, guitarBank));
                }
                // bass: a steady line, an octave jump at the end of each bar, hammer-ons on the way up
                var bassLine = new List<VoiceNote>();
                int[] roots = { 28, 28, 31, 33 };
                for (int i = 0; i < 16; i++)
                {
                    bassLine.Add(new VoiceNote
                    {
                        Time = 0.3 + i * 0.5, LowFret = 1, FretCount = 1,
                        Pitch = roots[i / 4] + (i % 4 == 3 ? 12 : i % 4 == 2 ? 7 : 0), Soft = i % 4 == 2,
                    });
                }
                bassLine.Add(new VoiceNote { Time = 0.3 + 16 * 0.5, Length = 2.0, LowFret = 1, FretCount = 1, Pitch = 28 });
                all.AddRange(GuitarVoiceSynth.Render(bassLine.ToArray(), 11.5, GuitarVoiceTone.Rock, true, 0f, 0.12f, bassBank));

                string path = Path.Combine(outputFolder, "guitar-sound-demo.wav");
                File.WriteAllBytes(path, GuitarVoiceSynth.ToWav(all.ToArray(), SR, 2));
                check(new FileInfo(path).Length > 1_000_000, "demo recording written: " + path);
            }
        }
    }
}
