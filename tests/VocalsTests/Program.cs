// Tests for singing in YARG Resurrected:
//   1. the pitch detector (overlay/Assets/Script/Audio/Vocals/VoiceAnalyzer.cs),
//   2. the microphone timing (VoiceInput.cs),
//   3. the scoring engine's rules, including Vocals Challenge Mode (YARG.Core + patches-core),
//   4. whole songs "sung" by made-up singers of different skill, through all of the above.
//
// Usage: dotnet run -c Release -p:YargProjectDir=<patched game source> [-- table|readme|grid|robust|why ...]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YARG.Audio.Vocals;
using YARG.Core;
using YARG.Core.Engine.Vocals;
using YARG.Core.Game;
using YARG.Core.IO;

namespace VocalsTests
{
    public static class Program
    {
        private static int _checks;
        private static int _failed;

        public static void Check(bool condition, string what)
        {
            _checks++;
            if (condition)
            {
                Console.WriteLine("ok   " + what);
            }
            else
            {
                _failed++;
                Console.WriteLine("FAIL " + what);
            }
        }

        public static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "table": Explore.Table(args); return 0;
                    case "readme": Explore.Readme(args); return 0;
                    case "grid": Explore.Grid(args); return 0;
                    case "robust": Explore.Robust(args); return 0;
                    case "why": Explore.Why(args); return 0;
                }
            }

            try
            {
                Console.WriteLine("-- Pitch detector --");
                DetectorTests.Run();
                Console.WriteLine("-- Microphone timing --");
                TimingTests.Run();
                Console.WriteLine("-- Scoring rules --");
                EngineTests.Run();
                Console.WriteLine("-- Whole songs --");
                SingerTests.Run();
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL an exception stopped the tests: " + e);
                return 1;
            }

            if (_failed == 0)
            {
                Console.WriteLine($"ALL {_checks} CHECKS PASSED");
                return 0;
            }

            Console.WriteLine($"{_failed} OF {_checks} CHECKS FAILED");
            return 1;
        }
    }

    // ------------------------------------------------------------------------------------------

    internal static class DetectorTests
    {
        private const int RATE = 48000;

        private static double Cents(double hz, double reference) => 1200.0 * Math.Log(hz / reference, 2.0);

        private static double Median(List<double> values)
        {
            var sorted = values.Select(Math.Abs).OrderBy(v => v).ToList();
            return sorted.Count == 0 ? double.NaN : sorted[sorted.Count / 2];
        }

        private static double Percentile95(List<double> values)
        {
            var sorted = values.Select(Math.Abs).OrderBy(v => v).ToList();
            return sorted.Count == 0 ? double.NaN : sorted[(int) (sorted.Count * 0.95)];
        }

        internal static float[] Vowel(double hz, int vowel, double seconds, double level, int seed, int rate = RATE,
            double vibratoCents = 0, double noise = 0)
        {
            var voice = new SyntheticVoice(rate, seed);
            voice.SetVowel(vowel);
            var hiss = new SyntheticVoice(rate, seed + 99);
            var audio = new float[(int) (seconds * rate)];
            for (int i = 0; i < audio.Length; i++)
            {
                double f = hz * Math.Pow(2, vibratoCents / 1200.0 * Math.Sin(2 * Math.PI * 6.0 * i / rate));
                audio[i] = voice.Voiced(f, level) + (noise > 0 ? (float) (hiss.Gaussian() * noise) : 0f);
            }
            return audio;
        }

        internal static List<VoiceFrame> Analyse(float[] audio, int rate, bool strict = false, float gate = 2f, int block = 480)
        {
            var analyzer = new VoiceAnalyzer(rate) { Strict = strict };
            var frames = new List<VoiceFrame>();
            for (int i = 0; i < audio.Length; i += block)
            {
                analyzer.Process(new ReadOnlySpan<float>(audio, i, Math.Min(block, audio.Length - i)), gate, frames);
            }
            return frames;
        }

        private static double Rms(float[] audio)
        {
            double sum = 0;
            foreach (float x in audio)
            {
                sum += x * x;
            }
            return Math.Sqrt(sum / audio.Length);
        }

        public static void Run()
        {
            // Every note from C2 to C6 on five vowels: a pitch for every result, and the right one
            var errors = new List<double>();
            var classicErrors = new List<double>();
            int frames = 0, voiced = 0, wrong = 0;
            foreach (int midi in new[] { 36, 40, 43, 45, 48, 52, 55, 57, 60, 62, 64, 65, 67, 69, 72, 76, 79, 81, 84 })
            {
                double hz = 440.0 * Math.Pow(2, (midi - 69) / 12.0);
                for (int vowel = 0; vowel < 5; vowel++)
                {
                    var audio = Vowel(hz, vowel, 1.0, 0.3, 1000 + vowel + midi);
                    foreach (var frame in Analyse(audio, RATE).Skip(5))
                    {
                        frames++;
                        if (!frame.Voiced)
                        {
                            continue;
                        }
                        voiced++;
                        double cents = Cents(frame.PitchHz, hz);
                        if (Math.Abs(cents) > 100)
                        {
                            wrong++;
                        }
                        else
                        {
                            errors.Add(cents);
                        }
                    }

                    // YARG's original tracker on the same sound, 40 ms at a time
                    var tracker = new YARG.Audio.PitchDetection.PitchTracker(RATE);
                    int chunk = RATE * 40 / 1000;
                    for (int i = chunk * 3; i + chunk <= audio.Length; i += chunk)
                    {
                        var pitch = tracker.ProcessBuffer(new ReadOnlySpan<float>(audio, i, chunk));
                        if (pitch != null && Math.Abs(Cents(pitch.Value, hz)) <= 100)
                        {
                            classicErrors.Add(Cents(pitch.Value, hz));
                        }
                    }
                }
            }

            Program.Check(voiced >= frames * 0.995, $"steady vowels C2-C6 always get a pitch ({voiced}/{frames})");
            Program.Check(wrong == 0, $"never a wrong note or octave ({wrong} wrong)");
            Program.Check(Median(errors) < 2.0 && Percentile95(errors) < 10.0,
                $"pitch within a few cents (median {Median(errors):F1}, 95% under {Percentile95(errors):F1} cents)");
            Program.Check(Median(errors) < Median(classicErrors),
                $"closer than the original tracker (its median error: {Median(classicErrors):F1} cents)");

            // Other sample rates
            foreach (int rate in new[] { 16000, 44100, 96000 })
            {
                var audio = Vowel(220.0, 0, 1.0, 0.3, 7, rate);
                var result = Analyse(audio, rate, block: rate / 100).Skip(5).ToList();
                var cents = result.Where(f => f.Voiced).Select(f => Cents(f.PitchHz, 220.0)).ToList();
                Program.Check(cents.Count >= result.Count * 0.98 && Percentile95(cents) < 8.0,
                    $"works at {rate} Hz ({cents.Count}/{result.Count} pitched, 95% under {Percentile95(cents):F1} cents)");
            }

            // Noise is never given a pitch
            foreach (var (name, hissy, level) in new[] { ("breath", false, 0.2), ("\"shhh\"", true, 0.3) })
            {
                var voice = new SyntheticVoice(RATE, 7);
                var audio = new float[RATE * 3];
                for (int i = 0; i < audio.Length; i++)
                {
                    audio[i] = voice.Noise(level, hissy);
                }
                var result = Analyse(audio, RATE);
                Program.Check(result.Count(f => f.Voiced) == 0 && result.Count(f => f.Active) > result.Count * 0.9,
                    $"{name} is heard as sound but never as a note (clearest: {result.Max(f => f.Clarity):F2})");
            }

            // A voice in a noisy room
            {
                var clean = Vowel(220.0, 0, 2.0, 0.3, 5);
                var audio = Vowel(220.0, 0, 2.0, 0.3, 5, noise: Rms(clean) / Math.Pow(10, 10.0 / 20.0));
                var result = Analyse(audio, RATE).Skip(5).ToList();
                var cents = result.Where(f => f.Voiced).Select(f => Cents(f.PitchHz, 220.0)).ToList();
                Program.Check(cents.Count >= result.Count * 0.98 && Percentile95(cents) < 10.0,
                    $"a voice 10 dB above room noise is still tracked ({cents.Count}/{result.Count}, 95% under {Percentile95(cents):F1} cents)");

                var strict = Analyse(audio, RATE, strict: true).Skip(5).ToList();
                Program.Check(strict.Count(f => f.Voiced) >= strict.Count * 0.95,
                    $"also with the stricter Challenge Mode threshold ({strict.Count(f => f.Voiced)}/{strict.Count})");
            }

            // Vibrato: the pitch that is reported belongs to the moment that is reported
            {
                const double hz = 261.63;
                var audio = Vowel(hz, 0, 3.0, 0.3, 11, vibratoCents: 60);
                var result = Analyse(audio, RATE).Skip(5).Where(f => f.Voiced).ToList();
                double bestOffset = 0, bestError = double.MaxValue;
                for (double offset = -0.03; offset <= 0.03; offset += 0.001)
                {
                    double error = result.Average(f => Math.Abs(Cents(f.PitchHz, hz) -
                        60 * Math.Sin(2 * Math.PI * 6.0 * ((double) f.CentreSample / RATE - offset))));
                    if (error < bestError)
                    {
                        bestError = error;
                        bestOffset = offset;
                    }
                }
                Program.Check(Math.Abs(bestOffset) <= 0.004 && bestError < 5.0,
                    $"vibrato is followed, and each pitch is timed to within {Math.Abs(bestOffset) * 1000:F0} ms (error {bestError:F1} cents)");
            }

            // A note starting: found quickly
            {
                var voice = new SyntheticVoice(RATE, 3);
                var audio = new float[RATE * 2];
                for (int i = RATE / 2; i < audio.Length; i++)
                {
                    audio[i] = voice.Voiced(220, 0.3 * Math.Min(1.0, (i - RATE / 2) / (0.01 * RATE)));
                }
                var first = Analyse(audio, RATE).First(f => f.Voiced);
                double centre = (double) first.CentreSample / RATE;
                Program.Check(centre >= 0.49 && centre <= 0.53,
                    $"a note that starts at 0.500 s is picked up at {centre:F3} s");
            }

            // A clap is one hit; singing on is not another
            {
                var voice = new SyntheticVoice(RATE, 9);
                var audio = new float[RATE * 2];
                for (int i = 0; i < audio.Length; i++)
                {
                    audio[i] = (float) (voice.Gaussian() * 0.0005);
                }
                for (int i = RATE / 2; i < RATE / 2 + RATE / 20; i++)
                {
                    audio[i] += (float) (voice.Gaussian() * 0.4 * Math.Exp(-(i - RATE / 2) / (0.01 * RATE)));
                }
                for (int i = RATE; i < audio.Length; i++)
                {
                    audio[i] += voice.Voiced(220, 0.3 * Math.Min(1.0, (i - RATE) / (0.3 * RATE)));
                }
                var result = Analyse(audio, RATE);
                int claps = result.Count(f => f.IsHit && f.CentreSample < RATE * 0.7);
                int later = result.Count(f => f.IsHit && f.CentreSample > RATE * 1.4);
                Program.Check(claps == 1 && later == 0, $"a clap is one hit ({claps}), a held note is none ({later})");

                var quiet = Analyse(audio, RATE, gate: 60f);
                Program.Check(quiet.All(f => !f.Active && !f.IsHit && !f.Voiced),
                    "nothing is reported for sound below the sensitivity setting");
            }
        }
    }

    // ------------------------------------------------------------------------------------------

    internal static class TimingTests
    {
        private const int RATE = 48000;

        // A tone from 1.000 s on, delivered late and unevenly
        private static List<VoiceInputFrame> Deliver(double latency, double jitter, int block, double hold,
            double dropFrom = -1, double dropTo = -1, bool tellAboutDrops = true, bool strict = false, int seed = 1,
            float[] audio = null)
        {
            if (audio == null)
            {
                var voice = new SyntheticVoice(RATE, 5);
                audio = new float[RATE * 3];
                for (int i = RATE; i < audio.Length; i++)
                {
                    audio[i] = voice.Voiced(220, 0.3 * Math.Min(1.0, (i - RATE) / (0.005 * RATE)));
                }
            }

            var random = new Random(seed);
            var input = new VoiceInput(RATE) { HoldSeconds = hold, Strict = strict };
            var frames = new List<VoiceInputFrame>();
            double now = 0;
            long missing = 0;
            for (int i = 0; i < audio.Length; i += block)
            {
                int count = Math.Min(block, audio.Length - i);
                double time = (double) i / RATE;
                if (time >= dropFrom && time < dropTo)
                {
                    missing += count;
                    continue;
                }

                now = Math.Max(now, (double) (i + count) / RATE + latency + random.NextDouble() * jitter);
                input.Feed(new ReadOnlySpan<float>(audio, i, count), now, 2f, frames, tellAboutDrops ? missing : 0);
                missing = 0;
            }
            return frames;
        }

        private static double FirstPitchTime(List<VoiceInputFrame> frames) => frames.First(f => f.PitchHz > 0).Time;

        private static double WorstSpacingError(List<VoiceInputFrame> frames)
        {
            var pitched = frames.Where(f => f.PitchHz > 0).ToList();
            double worst = 0;
            for (int i = 1; i < pitched.Count; i++)
            {
                worst = Math.Max(worst, Math.Abs(pitched[i].Time - pitched[i - 1].Time - 0.020));
            }
            return worst;
        }

        public static void Run()
        {
            const double hold = 0.030;

            // Prompt, steady delivery: the reference
            var steady = Deliver(0.030, 0.0, 480, hold);
            double expected = 1.000 + 0.030 + hold;
            Program.Check(Math.Abs(FirstPitchTime(steady) - expected) < 0.020,
                $"a note sung at 1.000 s is stamped {FirstPitchTime(steady):F3} (sound + mic delay + hold = {expected:F3})");
            Program.Check(WorstSpacingError(steady) < 0.0005, "results are stamped exactly 20 ms apart");

            // Jitter and bursts do not move the stamps
            var jittery = Deliver(0.030, 0.030, 480, hold, seed: 3);
            Program.Check(WorstSpacingError(jittery) < 0.003 && Math.Abs(FirstPitchTime(jittery) - FirstPitchTime(steady)) < 0.004,
                $"30 ms of delivery jitter moves a stamp by {Math.Abs(FirstPitchTime(jittery) - FirstPitchTime(steady)) * 1000:F1} ms at most");

            var bursty = Deliver(0.030, 0.0, 1920, hold);
            Program.Check(WorstSpacingError(bursty) < 0.0005 && Math.Abs(FirstPitchTime(bursty) - FirstPitchTime(steady)) < 0.002,
                "audio that arrives 40 ms at a time is stamped the same");

            // Lost packets: with the phone's sample count the timing holds; without it, it would not
            var dropped = Deliver(0.030, 0.0, 480, hold, dropFrom: 0.4, dropTo: 0.6);
            Program.Check(Math.Abs(FirstPitchTime(dropped) - FirstPitchTime(steady)) < 0.002,
                "200 ms of lost packets (counted by the phone) leave the timing as it was");

            var uncounted = Deliver(0.030, 0.0, 480, hold, dropFrom: 0.4, dropTo: 0.6, tellAboutDrops: false);
            Program.Check(FirstPitchTime(steady) - FirstPitchTime(uncounted) > 0.1,
                $"(without the count, the same loss would put every later note {(FirstPitchTime(steady) - FirstPitchTime(uncounted)) * 1000:F0} ms early)");

            Program.Check(VoiceMicTiming.MissingSamples(-1, 480) == 0 && VoiceMicTiming.MissingSamples(480, 480) == 0 &&
                VoiceMicTiming.MissingSamples(480, 1440) == 960 && VoiceMicTiming.MissingSamples(1440, 480) == -1 &&
                VoiceMicTiming.MissingSamples(4294967200L, 384) == 480 && VoiceMicTiming.MissingSamples(100, -1) == 0,
                "lost samples are counted right, across the counter wrapping round and a mic restart");

            // A long interruption starts the clock again instead of bridging it
            {
                var voice = new SyntheticVoice(RATE, 5);
                var input = new VoiceInput(RATE) { HoldSeconds = hold };
                var frames = new List<VoiceInputFrame>();
                var block = new float[480];
                double now = 0;
                for (int i = 0; i < 100; i++)
                {
                    for (int k = 0; k < block.Length; k++)
                    {
                        block[k] = voice.Voiced(220, 0.3);
                    }
                    now = (i + 1) * 0.01 + 0.03;
                    input.Feed(block, now, 2f, frames);
                }
                frames.Clear();
                now += 5.0; // the app was paused for five seconds
                for (int i = 0; i < 100; i++)
                {
                    for (int k = 0; k < block.Length; k++)
                    {
                        block[k] = voice.Voiced(220, 0.3);
                    }
                    now += 0.01;
                    input.Feed(block, now, 2f, frames);
                }
                Program.Check(frames.Count > 0 && frames.All(f => f.Time > now - 1.5 && f.Time < now + 0.2),
                    "after a pause, results are timed from when the audio came back");
            }

            // A note that turns into noise: the pitch stops at once, and what follows is "sound without a tone"
            {
                var voice = new SyntheticVoice(RATE, 5);
                var audio = new float[RATE * 2];
                for (int i = 0; i < audio.Length; i++)
                {
                    double t = (double) i / RATE;
                    audio[i] = t < 1.0 ? voice.Voiced(220, 0.3) : voice.Noise(0.3, true);
                }

                foreach (bool strict in new[] { false, true })
                {
                    var after = Deliver(0.030, 0.0, 480, hold, strict: strict, audio: audio)
                        .Where(f => f.Time > 1.0 + 0.06 + 0.03).ToList();
                    Program.Check(after.Count(f => f.PitchHz > 0) == 0 && after.Count(f => f.PitchHz <= 0 && !f.IsHit) > 30,
                        (strict ? "in Challenge Mode too" : "when a note turns into noise the pitch stops at once: noise cannot hold a note"));
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------

    internal static class EngineTests
    {
        // Three phrases; the middle note of each is long enough to be judged on its own
        private static SimSong SmallSong()
        {
            var song = new SimSong();
            double time = 2.0;
            float[][] melodies =
            {
                new[] { 60f, 62f, 64f, 65f, 67f },
                new[] { 67f, 65f, 64f, 62f, 60f },
                new[] { 60f, 64f, 67f, 64f, 60f },
            };
            foreach (var melody in melodies)
            {
                var phrase = new SimPhrase { Start = time - 0.1 };
                foreach (float pitch in melody)
                {
                    phrase.Notes.Add(new SimNote { Start = time, Length = 0.4, Pitch = pitch });
                    time += 0.5;
                }
                phrase.End = time;
                song.Phrases.Add(phrase);
                time += 1.5;
            }
            song.Length = time + 1.0;
            return song;
        }

        /// <summary>
        /// Inputs for a song sung to order: every <paramref name="step"/> seconds while a note is
        /// on, shifted and trimmed as asked. <paramref name="keep"/> picks the notes to sing
        /// (phrase index, note index).
        /// </summary>
        private static List<SimInput> Sing(SimSong song, double step = 0.02, float offset = 0f,
            double lateStart = 0, double earlyEnd = 0, Func<int, int, bool> keep = null, double share = 1.0,
            bool toneless = false)
        {
            var inputs = new List<SimInput>();
            for (double time = 0; time < song.Length; time += step)
            {
                for (int p = 0; p < song.Phrases.Count; p++)
                {
                    var phrase = song.Phrases[p];
                    double stop = phrase.Notes[0].Start + (phrase.Notes[phrase.Notes.Count - 1].End - phrase.Notes[0].Start) * share;
                    for (int n = 0; n < phrase.Notes.Count; n++)
                    {
                        var note = phrase.Notes[n];
                        if (time < note.Start + lateStart || time >= note.TotalEnd - earlyEnd || time >= stop)
                        {
                            continue;
                        }
                        if (keep != null && !keep(p, n))
                        {
                            continue;
                        }
                        inputs.Add(new SimInput { Time = time, Midi = toneless ? -1f : note.PitchAt(time) + offset });
                    }
                }
            }
            return inputs;
        }

        /// <summary>
        /// Inputs chosen one by one: <paramref name="offsetAt"/> is given the phrase, the note,
        /// and how far into the note the input is (seconds), and answers how far off the note
        /// the voice is (semitones), or null for no sound at all.
        /// </summary>
        private static List<SimInput> SingBy(SimSong song, Func<int, int, double, float?> offsetAt, double step = 0.02)
        {
            var inputs = new List<SimInput>();
            int steps = (int) (song.Length / step);
            for (int s = 0; s < steps; s++)
            {
                double time = s * step;
                for (int p = 0; p < song.Phrases.Count; p++)
                {
                    var phrase = song.Phrases[p];
                    for (int n = 0; n < phrase.Notes.Count; n++)
                    {
                        var note = phrase.Notes[n];
                        if (time < note.Start || time >= note.TotalEnd)
                        {
                            continue;
                        }

                        float? offset = offsetAt(p, n, time - note.Start);
                        if (offset.HasValue)
                        {
                            inputs.Add(new SimInput { Time = time, Midi = note.PitchAt(time) + offset.Value });
                        }
                    }
                }
            }
            return inputs;
        }

        public static void Run()
        {
            var song = SmallSong();
            var expertNormal = SimGame.Parameters(Difficulty.Expert, false);
            var expert = SimGame.Parameters(Difficulty.Expert, true);
            var hard = SimGame.Parameters(Difficulty.Hard, true);

            Program.Check(!expertNormal.IsChallenge && !expertNormal.UsesNoteTracking && expertNormal.NoteHitPercent == 0 &&
                expertNormal.EdgeGrace == 0 && !expertNormal.VoicedOnly,
                "without the modifier the engine gets no Challenge rules at all");
            Program.Check(expert.IsChallenge && expert.UsesNoteTracking && expert.VoicedOnly && expert.MaxNoteMisses == 0,
                "with it, Expert allows no skipped note");

            // The rules get stricter with every difficulty
            {
                var levels = new[] { Difficulty.Beginner, Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert }
                    .Select(EnginePreset.VocalsPreset.GetChallengeLevel).ToList();
                bool ordered = true;
                for (int i = 1; i < levels.Count; i++)
                {
                    ordered &= levels[i].PitchWindow < levels[i - 1].PitchWindow &&
                        levels[i].PitchWindowPerfect < levels[i - 1].PitchWindowPerfect &&
                        levels[i].PhraseHitPercent > levels[i - 1].PhraseHitPercent &&
                        levels[i].MaxNoteMisses <= levels[i - 1].MaxNoteMisses;
                }
                Program.Check(ordered, "each Challenge difficulty asks for more than the one below");

                bool harder = true;
                foreach (var difficulty in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert })
                {
                    var normal = SimGame.Parameters(difficulty, false);
                    var challenge = SimGame.Parameters(difficulty, true);
                    harder &= challenge.PitchWindow <= normal.PitchWindow && challenge.PhraseHitPercent > normal.PhraseHitPercent &&
                        challenge.PointsPerPhrase == normal.PointsPerPhrase;
                }
                Program.Check(harder, "and more than the same difficulty without it, for the same points");
            }

            // Perfect singing
            var perfect = Sing(song);
            var result = SimGame.Play(song, perfect, expert);
            Program.Check(result.PhrasesHit == 3 && result.Fills.Min() >= 0.97,
                $"perfect singing clears Expert Challenge (least of a phrase credited: {result.Fills.Min():P0})");
            Program.Check(SimGame.Play(song, Sing(song, offset: -12f), expert).PhrasesHit == 3 &&
                SimGame.Play(song, Sing(song, offset: 12f), expert).PhrasesHit == 3,
                "an octave down or up is as good (low and high voices are equal)");

            // In tune or not
            Program.Check(SimGame.Play(song, Sing(song, offset: 0.35f), expert).PhrasesHit == 3,
                "35 cents off is still in tune on Expert Challenge");
            var sharp = SimGame.Play(song, Sing(song, offset: 0.55f), expert);
            Program.Check(sharp.PhrasesHit == 0 && sharp.MeanFill > 0.3 && sharp.MeanFill < 0.7,
                $"55 cents off earns part of the credit and not the phrase ({sharp.MeanFill:P0})");
            Program.Check(SimGame.Play(song, Sing(song, offset: 0.55f), expertNormal).PhrasesHit == 3,
                "(the same singing passes Expert without the modifier)");
            Program.Check(SimGame.Play(song, Sing(song, offset: 0.75f), expert).MeanFill < 0.02 &&
                SimGame.Play(song, Sing(song, offset: 1.0f), expert).PhrasesHit == 0,
                "three quarters of a semitone off earns nothing");

            // Singing only part of a phrase
            var most = SimGame.Play(song, Sing(song, share: 0.7), expert);
            Program.Check(most.PhrasesHit == 0, "70% of each phrase is not enough on Expert Challenge");
            Program.Check(SimGame.Play(song, Sing(song, share: 0.7), expertNormal).PhrasesHit == 3,
                "(it is plenty without the modifier, where 57.5% is)");

            // Skipping a note
            var skipOne = Sing(song, keep: (p, n) => n != 2);
            var skipped = SimGame.Play(song, skipOne, SimGame.Custom(Difficulty.Expert, 0.7f, 0.4f, 0.50, 0.30, 0, 0.10));
            Program.Check(skipped.PhrasesHit == 0 && skipped.Notes.Count(n => !n.Counted) == 3,
                "a skipped note loses the phrase on Expert, even with enough of the phrase sung");
            Program.Check(SimGame.Play(song, skipOne, SimGame.Custom(Difficulty.Hard, 0.7f, 0.4f, 0.50, 0.30, 1, 0.10)).PhrasesHit == 3,
                "one skipped note is allowed on Hard");
            Program.Check(SimGame.Play(song, Sing(song, keep: (p, n) => n != 1 && n != 3),
                    SimGame.Custom(Difficulty.Hard, 0.7f, 0.4f, 0.50, 0.30, 1, 0.10)).PhrasesHit == 0,
                "two are not");
            Program.Check(SimGame.Play(song, skipOne, expertNormal).PhrasesHit == 3,
                "(without the modifier a skipped note costs nothing)");

            // The ends of a note are forgiven
            var edges = SimGame.Play(song, Sing(song, lateStart: 0.08, earlyEnd: 0.06), expert);
            Program.Check(edges.PhrasesHit == 3 && edges.Fills.Min() >= 0.95,
                $"finding each note 80 ms late and leaving it 60 ms early still counts in full ({edges.Fills.Min():P0})");
            var cutShort = SimGame.Play(song, Sing(song, earlyEnd: 0.2), expert);
            Program.Check(cutShort.MeanFill < 0.6, $"leaving each note 200 ms early does not ({cutShort.MeanFill:P0} credited)");
            var veryLate = SimGame.Play(song, Sing(song, lateStart: 0.2), expert);
            Program.Check(veryLate.MeanFill < 0.6, $"200 ms late does not ({veryLate.MeanFill:P0} credited)");

            // An input that is only just on the note must never earn less than one that misses.
            // (Notes are 0.4 s; on Expert Challenge 0.69 semitones off is just on, 0.75 is off.)
            {
                // Sliding up into each note: far off, then nearly there, then on it
                Func<float, List<SimInput>> scoop = nearly => SingBy(song, (p, n, into) =>
                    into < 0.03 ? 3f : into < 0.05 ? nearly : 0f);
                var scoopOn = SimGame.Play(song, scoop(0.69f), expert);
                var scoopOff = SimGame.Play(song, scoop(0.75f), expert);
                Program.Check(scoopOn.MeanFill >= scoopOff.MeanFill - 0.001 && scoopOn.MeanFill >= 0.97,
                    $"sliding up into a note costs nothing, however close the slide passes ({scoopOn.MeanFill:P0} and {scoopOff.MeanFill:P0})");

                // Letting each note sag as it is left 60 ms early
                Func<float, List<SimInput>> sag = last => SingBy(song, (p, n, into) =>
                    into < 0.33 ? 0f : into < 0.35 ? last : (float?) null);
                var sagOn = SimGame.Play(song, sag(0.69f), expert);
                var sagOff = SimGame.Play(song, sag(0.75f), expert);
                Program.Check(sagOn.MeanFill >= sagOff.MeanFill - 0.001 && sagOn.MeanFill >= 0.97,
                    $"nor does a note that sags as it is let go ({sagOn.MeanFill:P0} and {sagOff.MeanFill:P0})");
            }

            // What is forgiven is limited by what was sung: one lucky reading is not a whole note
            {
                var quick = SmallSong();
                foreach (var phrase in quick.Phrases)
                {
                    foreach (var note in phrase.Notes)
                    {
                        note.Length = 0.18;
                    }
                }

                // Off the note the whole time, but for one reading in the middle of each
                var lucky = SimGame.Play(quick, SingBy(quick, (p, n, into) => Math.Abs(into - 0.08) < 0.005 ? 0f : 3f), expert);
                Program.Check(lucky.PhrasesHit == 0 && lucky.MeanFill < 0.4,
                    $"one reading on pitch does not stand for a whole short note ({lucky.MeanFill:P0} credited)");

                // Really sung, a little late and let go a little early
                var sung = SimGame.Play(quick, SingBy(quick, (p, n, into) => into >= 0.05 && into < 0.15 ? 0f : (float?) null), expert);
                Program.Check(sung.PhrasesHit == 3 && sung.Fills.Min() >= 0.95,
                    $"short notes that are sung still count in full ({sung.Fills.Min():P0})");
            }

            // A computer singer is not at the mercy of the frame rate
            {
                var bot = SimGame.Play(song, new List<SimInput>(), expert, 12, 0.003, bot: true);
                Program.Check(bot.PhrasesHit == 3 && bot.Fills.Min() >= 0.999,
                    $"a computer singer clears Expert Challenge at 12 frames a second ({bot.Fills.Min():P0})");
            }

            // Sound without a tone
            Program.Check(SimGame.Play(song, Sing(song, toneless: true), expertNormal).MeanFill == 0 &&
                SimGame.Play(song, Sing(song, toneless: true), expert).MeanFill == 0 &&
                SimGame.Play(song, Sing(song, toneless: true), SimGame.Parameters(Difficulty.Beginner, false)).MeanFill == 0,
                "breath and noise never count for a sung note, on any difficulty");
            {
                var rap = SmallSong();
                foreach (var phrase in rap.Phrases)
                {
                    foreach (var note in phrase.Notes)
                    {
                        note.Pitch = -1f;
                    }
                }
                var easyNormal = SimGame.Parameters(Difficulty.Easy, false);
                Program.Check(SimGame.Play(rap, Sing(rap, toneless: true), easyNormal).PhrasesHit == 3,
                    "for spoken notes any sound counts on Easy, as it always has");

                // From Medium up a spoken line has to be spoken: syllables are counted from the voice
                Program.Check(SimGame.Play(rap, Sing(rap, offset: 50f), expertNormal).PhrasesHit == 3,
                    "speaking a spoken line, word by word, clears it on Expert");
                Program.Check(SimGame.Play(rap, Sing(rap, toneless: true), expertNormal).PhrasesHit == 0,
                    "noise does not (no voice, no syllables)");

                var drone = new List<SimInput>();
                foreach (var phrase in rap.Phrases)
                {
                    for (double time = phrase.Notes[0].Start; time < phrase.Notes[phrase.Notes.Count - 1].End; time += 0.02)
                    {
                        drone.Add(new SimInput { Time = time, Midi = 50f });
                    }
                }
                Program.Check(SimGame.Play(rap, drone, expertNormal).PhrasesHit == 0 &&
                    SimGame.Play(rap, drone, easyNormal).PhrasesHit == 3,
                    "nor does one long \"aaah\" through the line (it does on Easy)");

                Program.Check(SimGame.Play(rap, Sing(rap, toneless: true), expert).PhrasesHit == 0 &&
                    SimGame.Play(rap, drone, expert).PhrasesHit == 0 &&
                    SimGame.Play(rap, Sing(rap, offset: 50f), expert).PhrasesHit == 3,
                    "Challenge Mode asks the same of spoken lines: a voice, on every word");
            }

            // The result does not depend on the frame rate (it used to, at phrase ends)
            {
                var a = SimGame.Play(song, perfect, expert, 30);
                var b = SimGame.Play(song, perfect, expert, 60, 0.004);
                var c = SimGame.Play(song, perfect, expert, 144, 0.001);
                var late = Sing(song, lateStart: 0.05, earlyEnd: 0.03);
                var d = SimGame.Play(song, late, hard, 30);
                var e = SimGame.Play(song, late, hard, 120, 0.003);
                Program.Check(a.Score == b.Score && b.Score == c.Score && a.TicksHit == b.TicksHit && b.TicksHit == c.TicksHit &&
                    d.Score == e.Score && d.TicksHit == e.TicksHit,
                    $"the same singing scores the same at 30, 60 and 144 frames a second ({a.Score})");
            }

            // Faster songs: a perfect singer could only ever be credited 1/speed of a phrase
            {
                var slowInputs = Sing(song, step: 0.04 * 1.5); // 25 results a second, song 1.5 times faster
                var before = SimGame.Play(song, slowInputs, SimGame.Parameters(Difficulty.Expert, false, null, 1.0));
                var after = SimGame.Play(song, slowInputs, SimGame.Parameters(Difficulty.Expert, false, null, 1.5));
                Program.Check(before.MeanFill < 0.75 && after.MeanFill > 0.95,
                    $"at 150% speed perfect singing is credited in full ({after.MeanFill:P0}; it was {before.MeanFill:P0})");
            }

            // An honest percentage
            {
                var ninety = Sing(song, share: 0.9);
                var normalStats = SimGame.Play(song, ninety, expertNormal);
                var challengeStats = SimGame.Play(song, ninety, SimGame.Custom(Difficulty.Expert, 0.7f, 0.4f, 0.5, 0, 0, 0.10));
                Program.Check(normalStats.Percent > 0.999 && challengeStats.Percent > 0.82 && challengeStats.Percent < 0.95,
                    $"Challenge Mode's percentage is what was sung ({challengeStats.Percent:P0}), not 100% for every passed phrase");
            }

            // A note that runs past the end of its phrase, and starting the song over
            {
                var carry = SmallSong();
                carry.Phrases[0].End = carry.Phrases[0].Notes[4].Start + 0.2; // the last note runs 0.2 s past it
                var inputs = Sing(carry);
                var first = SimGame.PlayTwice(carry, inputs, expertNormal, out var second);
                Program.Check(first.Score == second.Score && first.TicksHit == second.TicksHit && first.PhrasesHit == second.PhrasesHit,
                    "restarting a song (practice, replays) gives the same result the second time");
            }

            // Slides (one syllable gliding to another pitch) and notes that run past their phrase
            {
                var slides = SmallSong();
                foreach (var phrase in slides.Phrases)
                {
                    var note = phrase.Notes[1];
                    note.Length = 0.15;
                    note.SlideTo = note.Pitch + 3f;
                    note.SlideGap = 0.1;
                    note.SlideLength = 0.15;
                }
                var followed = SimGame.Play(slides, Sing(slides), expert);
                Program.Check(followed.PhrasesHit == 3 && followed.Fills.Min() >= 0.95,
                    $"a slide followed with the voice counts in full in Challenge Mode ({followed.Fills.Min():P0})");

                var flat = SmallSong();
                foreach (var phrase in flat.Phrases)
                {
                    phrase.Notes[1].Length = 0.15;
                }
                var notFollowed = SimGame.Play(slides, Sing(flat), expert); // the first pitch, then nothing
                Program.Check(notFollowed.Fills.Max() < 0.90 && notFollowed.Fills.Min() > 0.84,
                    $"a slide that is not followed earns only the part that was sung ({notFollowed.MeanFill:P0})");

                var carry = SmallSong();
                carry.Phrases[0].End = carry.Phrases[0].Notes[4].Start + 0.2; // the last note runs 0.2 s past it
                carry.Phrases[1].Start = carry.Phrases[0].End + 0.05;         // and into the next phrase
                var carried = SimGame.Play(carry, Sing(carry), expert);
                Program.Check(carried.PhrasesHit == 3 && carried.Fills.Min() >= 0.95,
                    $"a note that runs on into the next phrase is counted in both, without trouble ({carried.Fills.Min():P0})");

                // Let go where the first phrase ends: the rest of the note, in the next phrase,
                // is not a note of its own that was skipped
                double letGo = carry.Phrases[0].End - carry.Phrases[0].Notes[4].Start;
                var heldPart = SimGame.Play(carry, SingBy(carry, (p, n, into) =>
                    p == 0 && n == 4 && into >= letGo ? (float?) null : 0f), expert);
                Program.Check(heldPart.PhrasesHit == 3,
                    $"and letting it go at the phrase's end does not lose the next phrase ({heldPart.PhrasesHit} of 3)");
            }

            // Replays
            {
                using var memory = new MemoryStream();
                using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
                {
                    expert.Serialize(writer);
                }
                memory.Position = 0;
                using var data = FixedArray.ReadRemainder(memory);
                var stream = new FixedArrayStream(data);
                var loaded = new VocalsEngineParameters(ref stream, YARG.Core.Replays.ReplayIO.REPLAY_VERSIONS.CURRENT);
                Program.Check(loaded.IsChallenge && loaded.NoteHitPercent == expert.NoteHitPercent &&
                    loaded.MaxNoteMisses == expert.MaxNoteMisses && loaded.EdgeGrace == expert.EdgeGrace && loaded.VoicedOnly &&
                    loaded.PitchWindow == expert.PitchWindow && loaded.PhraseHitPercent == expert.PhraseHitPercent &&
                    stream.Position == data.Length,
                    "a replay keeps the Challenge rules it was played with");

                // A replay from before: the same bytes without the new ones at the end
                memory.Position = 0;
                var oldBytes = memory.ToArray().Take((int) memory.Length - (1 + 8 + 4 + 8 + 1)).ToArray();
                using var oldMemory = new MemoryStream(oldBytes);
                using var oldData = FixedArray.ReadRemainder(oldMemory);
                var oldStream = new FixedArrayStream(oldData);
                var old = new VocalsEngineParameters(ref oldStream, 15);
                Program.Check(!old.IsChallenge && !old.UsesNoteTracking && !old.VoicedOnly &&
                    old.PitchWindow == expert.PitchWindow && oldStream.Position == oldData.Length,
                    "replays from before this change still load, with the rules they had");
            }

            // The checkbox
            {
                var (possible, _) = GameMode.Vocals.PossibleModifiers(Instrument.Vocals);
                var (guitar, _) = GameMode.FiveFretGuitar.PossibleModifiers(Instrument.FiveFretGuitar);
                Program.Check((possible & Modifier.VocalsChallenge) != 0 && (guitar & Modifier.VocalsChallenge) == 0,
                    "Challenge Mode is offered as a modifier for vocals (and only for vocals)");
            }
        }
    }

    // ------------------------------------------------------------------------------------------

    internal static class SingerTests
    {
        private sealed class Performance
        {
            public readonly List<(SimSong Song, List<SimInput> Normal, List<SimInput> Strict, List<SimInput> Classic)> Songs =
                new List<(SimSong, List<SimInput>, List<SimInput>, List<SimInput>)>();

            /// <summary>Share of phrases hit over all the songs.</summary>
            public double Hit(Difficulty difficulty, bool challenge, out double fill, bool classic = false)
            {
                int phrases = 0, hit = 0;
                fill = 0;
                foreach (var (song, normal, strict, old) in Songs)
                {
                    var inputs = classic ? old : challenge ? strict : normal;
                    var result = SimGame.Play(song, inputs, SimGame.Parameters(difficulty, challenge));
                    phrases += result.Phrases;
                    hit += result.PhrasesHit;
                    fill += result.MeanFill * result.Phrases;
                }
                fill /= Math.Max(1, phrases);
                return (double) hit / Math.Max(1, phrases);
            }

            public double Hit(Difficulty difficulty, bool challenge) => Hit(difficulty, challenge, out _);
        }

        private static Performance Perform(SingerSkill skill, MicSetup mic = null, double assumedLatency = double.NaN,
            double hold = 0.03, bool withClassic = false, SongStyle[] styles = null)
        {
            mic ??= MicSetup.Phone;
            if (double.IsNaN(assumedLatency))
            {
                assumedLatency = mic.Latency;
            }

            var performance = new Performance();
            int seed = 0;
            foreach (var style in styles ?? new[] { SongStyle.Pop, SongStyle.Fast, SongStyle.Ballad })
            {
                var song = SimSong.Create(style, style == SongStyle.Ballad ? 14 : 18, 100 + seed);
                var audio = SimSinger.Sing(song, skill, 500 + seed);
                performance.Songs.Add((song,
                    SimMic.Improved(audio, SimSinger.RATE, mic, false, hold, assumedLatency, seed),
                    SimMic.Improved(audio, SimSinger.RATE, mic, true, hold, assumedLatency, seed),
                    withClassic ? SimMic.Classic(audio, SimSinger.RATE, mic, 0, seed) : null));
                seed++;
            }
            return performance;
        }

        public static void Run()
        {
            const Difficulty X = Difficulty.Expert, H = Difficulty.Hard, M = Difficulty.Medium, E = Difficulty.Easy;

            // A trained singer
            var star = Perform(SingerSkill.Star, withClassic: true);
            double starChallenge = star.Hit(X, true, out double starFill);
            Program.Check(starChallenge >= 0.95, $"a trained singer clears Expert Challenge ({starChallenge:P0} of phrases, {starFill:P0} of each)");
            star.Hit(X, false, out double newFill);
            double classicHit = star.Hit(X, false, out double classicFill, classic: true);
            Program.Check(newFill >= classicFill + 0.08 && newFill >= 0.95,
                $"the game now credits that singer {newFill:P0} of a phrase; the original microphone timing credited {classicFill:P0} (and {classicHit:P0} of phrases)");

            var low = SingerSkill.Star;
            low.Transpose = -12;
            Program.Check(Perform(low).Hit(X, true) >= 0.95, "the same an octave lower");

            // A good amateur
            var good = Perform(SingerSkill.Good);
            double goodHard = good.Hit(H, true), goodExpert = good.Hit(X, true);
            Program.Check(goodHard >= 0.9, $"a good amateur clears Hard Challenge ({goodHard:P0})");
            Program.Check(goodExpert >= 0.5 && goodExpert <= 0.95 && goodExpert < starChallenge,
                $"and finds Expert Challenge a real challenge ({goodExpert:P0} of phrases)");
            Program.Check(good.Hit(X, false) >= 0.95, "(without the modifier, Expert is no trouble)");

            // An average karaoke singer
            var karaoke = Perform(SingerSkill.Karaoke);
            double karaokeEasy = karaoke.Hit(E, true), karaokeMedium = karaoke.Hit(M, true),
                karaokeHard = karaoke.Hit(H, true), karaokeExpert = karaoke.Hit(X, true);
            Program.Check(karaokeEasy >= 0.6, $"an average karaoke singer gets through Easy Challenge ({karaokeEasy:P0})");
            Program.Check(karaokeExpert <= 0.25 && karaokeEasy > karaokeMedium && karaokeMedium > karaokeHard && karaokeHard > karaokeExpert,
                $"and each step up is harder (Medium {karaokeMedium:P0}, Hard {karaokeHard:P0}, Expert {karaokeExpert:P0})");
            Program.Check(karaoke.Hit(E, false) >= 0.9, "(Easy without the modifier stays easy)");

            // Not really singing it
            var lazy = SingerSkill.Star;
            lazy.PhraseShare = 0.7;
            var lazyResult = Perform(lazy);
            double lazyNormal = lazyResult.Hit(X, false), lazyChallenge = lazyResult.Hit(X, true), lazyHard = lazyResult.Hit(H, true);
            Program.Check(lazyNormal >= 0.9 && lazyChallenge <= 0.05 && lazyHard <= 0.2,
                $"singing 70% of each phrase: {lazyNormal:P0} on Expert, {lazyChallenge:P0} on Expert Challenge, {lazyHard:P0} on Hard Challenge");

            var skipper = SingerSkill.Star;
            skipper.SkipHighest = true;
            var skipResult = Perform(skipper);
            double skipNormal = skipResult.Hit(X, false), skipChallenge = skipResult.Hit(X, true);
            Program.Check(skipNormal >= 0.95 && skipChallenge <= 0.3,
                $"leaving out the highest note of each phrase: {skipNormal:P0} on Expert, {skipChallenge:P0} on Expert Challenge");

            var hum = SingerSkill.Star;
            hum.MonotonePitch = 60;
            var humResult = Perform(hum);
            Program.Check(humResult.Hit(X, false) <= 0.05 && humResult.Hit(X, true) == 0 && humResult.Hit(E, true) <= 0.1,
                "humming one note gets nowhere");

            var noise = SingerSkill.Star;
            noise.NoiseOnly = true;
            var noiseResult = Perform(noise, styles: new[] { SongStyle.Pop });
            Program.Check(noiseResult.Hit(Difficulty.Beginner, false) == 0 && noiseResult.Hit(X, true) == 0,
                "making noise scores nothing, even on Beginner");

            // Spoken songs
            var rap = Perform(SingerSkill.Good, styles: new[] { SongStyle.Rap });
            double rapNormal = rap.Hit(X, false), rapChallenge = rap.Hit(H, true);
            Program.Check(rapNormal >= 0.9 && rapChallenge >= 0.7,
                $"speaking the words of a spoken song works ({rapNormal:P0} on Expert, {rapChallenge:P0} on Hard Challenge)");
            var rapNoise = Perform(noise, styles: new[] { SongStyle.Rap });
            Program.Check(rapNoise.Hit(X, true) == 0, "but noise does not in Challenge Mode");

            // Microphones that are not ideal
            double wifi = Perform(SingerSkill.Star, MicSetup.WiFi, hold: 0.05).Hit(X, true);
            Program.Check(wifi >= 0.93, $"over Wi-Fi with jitter and lost packets: {wifi:P0} on Expert Challenge");
            double later = Perform(SingerSkill.Star, new MicSetup { Latency = 0.060 }, assumedLatency: 0.030).Hit(X, true);
            double sooner = Perform(SingerSkill.Star, new MicSetup { Latency = 0.0 }, assumedLatency: 0.030).Hit(X, true);
            Program.Check(later >= 0.9 && sooner >= 0.9,
                $"a microphone 30 ms later or sooner than the game assumes: {later:P0} and {sooner:P0}");
        }
    }
}
