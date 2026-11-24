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

        private static float Peak(float[] x, int from, int to)
        {
            float peak = 0;
            for (int i = Math.Max(0, from); i < Math.Min(x.Length, to); i++)
            {
                peak = Math.Max(peak, Math.Abs(x[i]));
            }
            return peak;
        }

        public static void Run(Action<bool, string> check, string outputFolder)
        {
            const int SR = GuitarVoiceSynth.SAMPLE_RATE;

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

            // ---- Playing the guitar
            foreach (var tone in new[] { GuitarVoiceTone.Clean, GuitarVoiceTone.Rock, GuitarVoiceTone.Heavy })
            {
                foreach (int midi in new[] { 40, 52, 64, 76 })
                {
                    const float SONG_CENTS = 23f;
                    var one = new[] { new VoiceNote { Time = 0.1, Length = 0.4, LowFret = 1, FretCount = 1, Pitch = midi } };
                    var audio = GuitarVoiceSynth.Render(one, 2.5, tone, false, SONG_CENTS, 0.1f);
                    double hz = 440.0 * Math.Pow(2, (midi - 69) / 12.0) * Math.Pow(2, SONG_CENTS / 1200.0);
                    double off = CentsOff(audio, 2, 0, (int) (0.16 * SR), (int) (0.3 * SR), hz, SR);
                    check(Math.Abs(off) <= 4, $"{tone} guitar, note {midi}: in tune with the song ({off:+0;-0;0} cents)");

                    if (midi == 52)
                    {
                        check(audio.Length == (int) (2.5 * SR) * 2, $"{tone}: stereo, full length");
                        bool finite = true;
                        foreach (float v in audio)
                        {
                            finite &= !float.IsNaN(v) && !float.IsInfinity(v);
                        }
                        check(finite, $"{tone}: no broken samples");
                        check(Peak(audio, 0, (int) (0.095 * SR) * 2) < 1e-4f, $"{tone}: silent before the note");
                        float playing = Peak(audio, (int) (0.1 * SR) * 2, (int) (0.5 * SR) * 2);
                        check(playing > 0.05f && playing < 0.99f, $"{tone}: sounds during the note without clipping (peak {playing:0.00})");
                        float after = Peak(audio, (int) (2.0 * SR) * 2, audio.Length);
                        check(after < playing * 0.03f, $"{tone}: stops after the note ends ({after / playing:0.000} of peak)");
                    }
                }
            }

            // Bass
            {
                var one = new[] { new VoiceNote { Time = 0.1, Length = 0.4, LowFret = 1, FretCount = 1, Pitch = 33 } };
                var audio = GuitarVoiceSynth.Render(one, 2.0, GuitarVoiceTone.Rock, true, 0f, 0.1f);
                double off = CentsOff(audio, 2, 0, (int) (0.16 * SR), (int) (0.3 * SR), 55.0, SR);
                check(Math.Abs(off) <= 6, $"bass, low A: in tune ({off:+0;-0;0} cents)");
            }

            // A chord holds root, fifth and octave
            {
                var chord = new[] { new VoiceNote { Time = 0.1, Length = 0.4, LowFret = 1, FretCount = 3, Pitch = 45 } };
                var audio = GuitarVoiceSynth.Render(chord, 1.5, GuitarVoiceTone.Clean, false, 0f, 0.1f);
                foreach (int midi in new[] { 45, 52, 57 })
                {
                    double hz = 440.0 * Math.Pow(2, (midi - 69) / 12.0);
                    double off = CentsOff(audio, 2, 0, (int) (0.16 * SR), (int) (0.3 * SR), hz, SR);
                    // Wider margin than for single notes: through an amplifier the three strings
                    // produce faint extra tones a few cents away from each (as on a real guitar)
                    check(Math.Abs(off) <= 12, $"chord contains note {midi} ({off:+0;-0;0} cents)");
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
                var bank = GuitarVoiceSynth.RenderBank(wanted, 3.0, GuitarVoiceTone.Rock, false, 0f, 0.1f);
                check(bank.Length == 3 && bank[0].Length == 3 * SR && bank[2].Length == 3 * SR,
                    "freestyle: one three-second recording per note");
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
                check(startsAtOnce, "freestyle: every recording sounds from its first moment");
                check(endsQuiet, "freestyle: every recording ends quietly (no click)");
                double off = CentsOff(asFloat[1], 1, 0, (int) (0.06 * SR), (int) (0.22 * SR), 220.0, SR);
                check(Math.Abs(off) <= 4, $"freestyle: the A is an A ({off:+0;-0;0} cents)");
                float single = Peak(asFloat[0], 0, SR), chord = Peak(asFloat[2], 0, SR);
                check(chord < single * 2.5f && single < chord * 2.5f,
                    $"freestyle: single notes and chords are balanced ({single:0.00} and {chord:0.00})");
            }

            // Loudness lands on the target
            {
                var run = new VoiceNote[16];
                for (int i = 0; i < run.Length; i++)
                {
                    run[i] = new VoiceNote { Time = 0.2 + i * 0.25, LowFret = 1 + i % 5, FretCount = 1, Pitch = 52 + (i % 5) * 2 };
                }
                var audio = GuitarVoiceSynth.Render(run, 5.0, GuitarVoiceTone.Rock, false, 0f, 0.08f);
                double power = 0;
                int count = 0;
                for (int i = (int) (0.3 * SR) * 2; i < (int) (4.0 * SR) * 2; i++)
                {
                    power += audio[i] * audio[i];
                    count++;
                }
                double rms = Math.Sqrt(power / count);
                check(rms > 0.05 && rms < 0.11, $"loudness on target (asked 0.080, got {rms:0.000})");
            }

            // ---- The whole way: hear a song, play its chart, and check the result against the song
            {
                GuitarVoicePitcher.Assign(firstHarmony, firstNotes, false);
                var audio = GuitarVoiceSynth.Render(firstNotes, firstHarmony.Seconds, GuitarVoiceTone.Clean, false,
                    firstHarmony.Cents, 0.1f);
                var low = new float[audio.Length / 2 / 4];
                for (int i = 0; i < low.Length; i++)
                {
                    float s = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        s += audio[(i * 4 + k) * 2];
                    }
                    low[i] = s / 4;
                }
                var heard = SongHarmony.Analyze(low);
                check(Math.Abs(heard.Cents - firstHarmony.Cents) < 6,
                    $"played guitar is tuned like the song (song {firstHarmony.Cents:+0.0;-0.0}, guitar {heard.Cents:+0.0;-0.0} cents)");
            }

            // A note starts exactly when the chart says
            foreach (double time in new[] { 0.5, 1.2345, 2.00001 })
            {
                var one = new[] { new VoiceNote { Time = time, LowFret = 1, FretCount = 1, Pitch = 57 } };
                var audio = GuitarVoiceSynth.Render(one, 3.0, GuitarVoiceTone.Rock, false, 0f, 0.1f);
                int first = 0;
                while (first < audio.Length && Math.Abs(audio[first]) < 1e-4f)
                {
                    first++;
                }
                double startedAt = first / 2 / (double) SR;
                check(startedAt >= time - 0.0005 && startedAt < time + 0.002,
                    $"note at {time:0.000} s starts on time (first sound at {startedAt:0.0000} s)");
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
                    all.AddRange(GuitarVoiceSynth.Render(notes, 9.5, tone, false, 0f, 0.12f));
                }
                var bassLine = new List<VoiceNote>();
                for (int i = 0; i < 16; i++)
                {
                    int[] roots = { 28, 28, 31, 33 };
                    bassLine.Add(new VoiceNote { Time = 0.3 + i * 0.5, LowFret = 1, FretCount = 1, Pitch = roots[i / 4] + (i % 4 == 3 ? 12 : 0) });
                }
                all.AddRange(GuitarVoiceSynth.Render(bassLine.ToArray(), 9.5, GuitarVoiceTone.Rock, true, 0f, 0.12f));

                string path = Path.Combine(outputFolder, "guitar-sound-demo.wav");
                File.WriteAllBytes(path, GuitarVoiceSynth.ToWav(all.ToArray(), SR, 2));
                check(new FileInfo(path).Length > 1_000_000, "demo recording written: " + path);
            }
        }
    }
}
