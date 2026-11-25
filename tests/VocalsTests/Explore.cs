// Tuning tools (not tests): tables of how made-up singers of different skill do under different
// scoring rules and microphone conditions. This is how the Vocals Challenge Mode numbers in
// YARG.Core (VocalsPreset.GetChallengeLevel) were chosen. Run one with, for example:
//   dotnet run -c Release -p:YargProjectDir=/path/to/patched/YARG-DROID -- table
//   ... -- readme     the table in the README (phrases cleared by each kind of singer)
//   ... -- grid       sets of challenge rules against each singer
//   ... -- robust     challenge mode with a late mic, Wi-Fi, a noisy room, low frame rates
//   ... -- why Fast good Expert      why phrases fail (too little sung, or which notes)
using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core;

namespace VocalsTests
{
    public static class Explore
    {
        // Scratch: print how each singer does in each mode, to choose the numbers
        public static void Table(string[] args)
        {
            var styles = new[] { SongStyle.Pop, SongStyle.Fast, SongStyle.Ballad };
            var singers = new List<SingerSkill>
            {
                SingerSkill.Star, SingerSkill.Good, SingerSkill.Karaoke, SingerSkill.Rough,
            };
            var octave = SingerSkill.Star; octave.Name = "star -8ve"; octave.Transpose = -12; singers.Add(octave);
            var lazy = SingerSkill.Star; lazy.Name = "star 62%"; lazy.PhraseShare = 0.62; singers.Add(lazy);
            var skipper = SingerSkill.Star; skipper.Name = "skip high"; skipper.SkipHighest = true; singers.Add(skipper);
            var hum = SingerSkill.Star; hum.Name = "one note"; hum.MonotonePitch = 60; singers.Add(hum);
            var noise = SingerSkill.Star; noise.Name = "noise"; noise.NoiseOnly = true; singers.Add(noise);

            var difficulties = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert };
            var mic = MicSetup.Phone;
            int runs = 2;

            foreach (var style in styles)
            {
                Console.WriteLine($"=== {style} ===  phrases hit % (mean fill %):   classic | new normal | CHALLENGE");
                foreach (var skill in singers)
                {
                    var sums = new double[difficulties.Length, 6];
                    for (int run = 0; run < runs; run++)
                    {
                        var song = SimSong.Create(style, 20, 100 + run);
                        var audio = SimSinger.Sing(song, skill, 500 + run);
                        var classicIn = SimMic.Classic(audio, SimSinger.RATE, mic, 0, run);
                        var normalIn = SimMic.Improved(audio, SimSinger.RATE, mic, false, 0.03, mic.Latency, run);
                        var strictIn = SimMic.Improved(audio, SimSinger.RATE, mic, true, 0.03, mic.Latency, run);
                        for (int d = 0; d < difficulties.Length; d++)
                        {
                            var classic = SimGame.Play(song, classicIn, SimGame.Parameters(difficulties[d], false));
                            var normal = SimGame.Play(song, normalIn, SimGame.Parameters(difficulties[d], false));
                            var hard = SimGame.Play(song, strictIn, SimGame.Parameters(difficulties[d], true));
                            sums[d, 0] += classic.HitShare; sums[d, 1] += classic.MeanFill;
                            sums[d, 2] += normal.HitShare; sums[d, 3] += normal.MeanFill;
                            sums[d, 4] += hard.HitShare; sums[d, 5] += hard.MeanFill;
                        }
                    }

                    var line = new System.Text.StringBuilder($"{skill.Name,-10}");
                    for (int d = 0; d < difficulties.Length; d++)
                    {
                        line.Append($" | {difficulties[d].ToString()[0]}: {100 * sums[d, 0] / runs,3:F0}({100 * sums[d, 1] / runs,2:F0}) {100 * sums[d, 2] / runs,3:F0}({100 * sums[d, 3] / runs,2:F0}) {100 * sums[d, 4] / runs,3:F0}({100 * sums[d, 5] / runs,2:F0})");
                    }
                    Console.WriteLine(line);
                }
            }
        }

        // Try sets of challenge numbers: phrases hit % for each singer and style
        public static void Grid(string[] args)
        {
            var styles = new[] { SongStyle.Pop, SongStyle.Fast, SongStyle.Ballad };
            var skills = new[] { SingerSkill.Star, SingerSkill.Good, SingerSkill.Karaoke, SingerSkill.Rough };
            var lazy = SingerSkill.Star; lazy.Name = "lazy70"; lazy.PhraseShare = 0.70;
            var skipper = SingerSkill.Star; skipper.Name = "skiphigh"; skipper.SkipHighest = true;
            skills = skills.Concat(new[] { lazy, skipper }).ToArray();
            int runs = 3;
            var mic = MicSetup.Phone;
            var cache = new Dictionary<string, (SimSong, List<SimInput>)>();
            foreach (var style in styles)
                foreach (var skill in skills)
                    for (int run = 0; run < runs; run++)
                    {
                        var song = SimSong.Create(style, 20, 100 + run);
                        var audio = SimSinger.Sing(song, skill, 500 + run);
                        cache[$"{style}/{skill.Name}/{run}"] = (song, SimMic.Improved(audio, SimSinger.RATE, mic, true, 0.03, mic.Latency, run));
                    }

            // window, perfect, phrase, note, misses, grace
            var sets = new (string Name, float W, float P, double Ph, double N, int M, double G)[]
            {
                ("X 0.80/0.48 85 50% 0", 0.80f, 0.48f, 0.85, 0.50, 0, 0.10),
                ("X 0.80/0.48 85 35% 0", 0.80f, 0.48f, 0.85, 0.35, 0, 0.10),
                ("X 0.70/0.42 85 35% 0", 0.70f, 0.42f, 0.85, 0.35, 0, 0.10),
                ("X 0.70/0.40 82 35% 0", 0.70f, 0.40f, 0.82, 0.35, 0, 0.10),
                ("X 0.70/0.40 80 30% 0", 0.70f, 0.40f, 0.80, 0.30, 0, 0.10),
                ("X 0.60/0.36 80 30% 0", 0.60f, 0.36f, 0.80, 0.30, 0, 0.10),
                ("H 1.00/0.60 78 50% 1", 1.00f, 0.60f, 0.78, 0.50, 1, 0.10),
                ("H 0.90/0.54 75 30% 1", 0.90f, 0.54f, 0.75, 0.30, 1, 0.10),
                ("H 0.90/0.54 72 30% 1", 0.90f, 0.54f, 0.72, 0.30, 1, 0.10),
                ("M 1.20/0.72 70 40% 1", 1.20f, 0.72f, 0.70, 0.40, 1, 0.12),
                ("M 1.20/0.72 65 30% 2", 1.20f, 0.72f, 0.65, 0.30, 2, 0.12),
                ("M 1.10/0.66 62 25% 2", 1.10f, 0.66f, 0.62, 0.25, 2, 0.12),
                ("E 1.50/0.90 60 30% 2", 1.50f, 0.90f, 0.60, 0.30, 2, 0.12),
                ("E 1.50/0.90 55 25% 3", 1.50f, 0.90f, 0.55, 0.25, 3, 0.12),
                ("E 1.40/0.84 50 25% 3", 1.40f, 0.84f, 0.50, 0.25, 3, 0.12),
            };
            Console.WriteLine($"{"set",-24}" + string.Join(" ", skills.Select(k => $"{k.Name,-12}")) + "   (Pop/Fast/Ballad phrases hit %)");
            foreach (var set in sets)
            {
                var line = new System.Text.StringBuilder($"{set.Name,-24}");
                foreach (var skill in skills)
                {
                    var cells = new List<string>();
                    foreach (var style in styles)
                    {
                        double hit = 0;
                        for (int run = 0; run < runs; run++)
                        {
                            var (song, inputs) = cache[$"{style}/{skill.Name}/{run}"];
                            hit += SimGame.Play(song, inputs, SimGame.Custom(Difficulty.Expert, set.W, set.P, set.Ph, set.N, set.M, set.G)).HitShare;
                        }
                        cells.Add($"{100 * hit / runs,3:F0}");
                    }
                    line.Append($"{string.Join("/", cells),-12} ");
                }
                Console.WriteLine(line);
            }
        }

        // How much do less-than-ideal conditions cost a singer in challenge mode?
        public static void Robust(string[] args)
        {
            var styles = new[] { SongStyle.Pop, SongStyle.Fast, SongStyle.Ballad };
            var cases = new List<(string Name, Func<SingerSkill, SingerSkill> Skill, MicSetup Mic, double Hold, double Assumed, double Fps)>
            {
                ("phone mic, as assumed", k => k, MicSetup.Phone, 0.03, 0.030, 60),
                ("mic 30 ms later than assumed", k => k, new MicSetup { Latency = 0.060 }, 0.03, 0.030, 60),
                ("mic 30 ms sooner than assumed", k => k, new MicSetup { Latency = 0.000 }, 0.03, 0.030, 60),
                ("mic 60 ms later than assumed", k => k, new MicSetup { Latency = 0.090 }, 0.03, 0.030, 60),
                ("Wi-Fi mic, as assumed", k => k, MicSetup.WiFi, 0.05, 0.070, 60),
                ("Wi-Fi mic, hold too short", k => k, MicSetup.WiFi, 0.01, 0.070, 60),
                ("Wi-Fi mic 40 ms later", k => k, new MicSetup { Latency = 0.110, Jitter = 0.030, DropChance = 0.01 }, 0.05, 0.070, 60),
                ("Wi-Fi, 5% packets lost", k => k, new MicSetup { Latency = 0.070, Jitter = 0.040, DropChance = 0.05 }, 0.05, 0.070, 60),
                ("bursty audio (40 ms blocks)", k => k, new MicSetup { Block = 1920, Jitter = 0.010 }, 0.03, 0.030, 60),
                ("noisy room (voice 20 dB over)", k => { k.RoomNoise = 0.006; return k; }, MicSetup.Phone, 0.03, 0.030, 60),
                ("loud room (voice 12 dB over)", k => { k.RoomNoise = 0.015; return k; }, new MicSetup { SensitivityDb = 12f }, 0.03, 0.030, 60),
                ("quiet singer", k => { k.Level = 0.05; return k; }, MicSetup.Phone, 0.03, 0.030, 60),
                ("game at 30 fps", k => k, MicSetup.Phone, 0.03, 0.030, 30),
                ("game at 120 fps", k => k, MicSetup.Phone, 0.03, 0.030, 120),
            };
            var skills = new Func<SingerSkill>[] { () => SingerSkill.Star, () => SingerSkill.Good, () => SingerSkill.Karaoke };
            var difficulties = new[] { Difficulty.Expert, Difficulty.Hard, Difficulty.Medium, Difficulty.Easy };
            int runs = 2;
            Console.WriteLine($"{"case",-32}" + string.Join("  ", skills.Select(k => $"{k().Name + " X/H/M/E",-22}")) + "  (phrases hit %, Pop+Fast+Ballad)");
            foreach (var c in cases)
            {
                var line = new System.Text.StringBuilder($"{c.Name,-32}");
                foreach (var make in skills)
                {
                    var hit = new double[difficulties.Length];
                    int n = 0;
                    foreach (var style in styles)
                        for (int run = 0; run < runs; run++)
                        {
                            var skill = c.Skill(make());
                            var song = SimSong.Create(style, 20, 100 + run);
                            var audio = SimSinger.Sing(song, skill, 500 + run);
                            var inputs = SimMic.Improved(audio, SimSinger.RATE, c.Mic, true, c.Hold, c.Assumed, run);
                            for (int d = 0; d < difficulties.Length; d++)
                                hit[d] += SimGame.Play(song, inputs, SimGame.Parameters(difficulties[d], true), c.Fps).HitShare;
                            n++;
                        }
                    line.Append($"{string.Join("/", hit.Select(h => $"{100 * h / n,3:F0}")),-22}  ");
                }
                Console.WriteLine(line);
            }
        }

        // Why do phrases fail in challenge mode? fill below the bar, or notes skipped (and which)
        // The table in the README: phrases cleared by each kind of singer
        public static void Readme(string[] args)
        {
            var lazy = SingerSkill.Star; lazy.Name = "sings 70% of each phrase"; lazy.PhraseShare = 0.7;
            var skipper = SingerSkill.Star; skipper.Name = "leaves out the highest note"; skipper.SkipHighest = true;
            var hum = SingerSkill.Star; hum.Name = "hums one note"; hum.MonotonePitch = 60;
            var noise = SingerSkill.Star; noise.Name = "makes noise"; noise.NoiseOnly = true;
            var skills = new[] { SingerSkill.Star, SingerSkill.Good, SingerSkill.Karaoke, lazy, skipper, hum, noise };
            var modes = new (string Name, Difficulty Difficulty, bool Challenge)[]
            {
                ("Expert", Difficulty.Expert, false), ("Easy Ch.", Difficulty.Easy, true), ("Medium Ch.", Difficulty.Medium, true),
                ("Hard Ch.", Difficulty.Hard, true), ("Expert Ch.", Difficulty.Expert, true),
            };
            var mic = MicSetup.Phone;
            Console.WriteLine($"{"singer",-30}" + string.Join(" ", modes.Select(m => $"{m.Name,-11}")) + " (phrases cleared: pop + fast + slow song)");
            foreach (var skill in skills)
            {
                var hit = new int[modes.Length];
                int phrases = 0, seed = 0;
                foreach (var style in new[] { SongStyle.Pop, SongStyle.Fast, SongStyle.Ballad })
                {
                    var song = SimSong.Create(style, style == SongStyle.Ballad ? 14 : 18, 100 + seed);
                    var audio = SimSinger.Sing(song, skill, 500 + seed);
                    var normal = SimMic.Improved(audio, SimSinger.RATE, mic, false, 0.03, mic.Latency, seed);
                    var strict = SimMic.Improved(audio, SimSinger.RATE, mic, true, 0.03, mic.Latency, seed);
                    for (int m = 0; m < modes.Length; m++)
                    {
                        hit[m] += SimGame.Play(song, modes[m].Challenge ? strict : normal,
                            SimGame.Parameters(modes[m].Difficulty, modes[m].Challenge)).PhrasesHit;
                    }
                    phrases += song.Phrases.Count;
                    seed++;
                }
                Console.WriteLine($"{skill.Name,-30}" + string.Join(" ", hit.Select(h => $"{100.0 * h / phrases,-11:F0}")));
            }
        }

        public static void Why(string[] args)
        {
            var style = (SongStyle) Enum.Parse(typeof(SongStyle), args[1]);
            var skill = args[2] == "star" ? SingerSkill.Star : args[2] == "good" ? SingerSkill.Good : SingerSkill.Karaoke;
            var difficulty = (Difficulty) Enum.Parse(typeof(Difficulty), args[3]);
            var mic = MicSetup.Phone;
            int fillFails = 0, noteFails = 0, phrases = 0;
            var missedLengths = new List<double>(); var missedSung = new List<double>();
            var allSungShort = new List<double>();
            for (int run = 0; run < 3; run++)
            {
                var song = SimSong.Create(style, 20, 100 + run);
                var audio = SimSinger.Sing(song, skill, 500 + run);
                var strictIn = SimMic.Improved(audio, SimSinger.RATE, mic, true, 0.03, mic.Latency, run);
                var prm = SimGame.Parameters(difficulty, true);
                var r = SimGame.Play(song, strictIn, prm);
                for (int i = 0; i < r.Phrases; i++)
                {
                    phrases++;
                    if (r.Hits[i]) continue;
                    if (r.Fills[i] < prm.PhraseHitPercent) fillFails++; else noteFails++;
                }
                foreach (var n in r.Notes)
                {
                    if (n.Length < 0.2) allSungShort.Add(n.Sung);
                    if (!n.Counted) { missedLengths.Add(n.Length); missedSung.Add(n.Sung); }
                }
            }
            Console.WriteLine($"{style} {skill.Name} {difficulty}: {phrases} phrases, failed on fill {fillFails}, failed on skipped notes only {noteFails}");
            Console.WriteLine("missed notes (length ms: sung %): " + string.Join("  ", missedLengths.Zip(missedSung, (l, sg) => $"{l * 1000:F0}:{sg * 100:F0}").Take(60)));
            if (allSungShort.Count > 0)
            {
                allSungShort.Sort();
                Console.WriteLine($"short notes (<200 ms) sung%: p5 {allSungShort[(int)(allSungShort.Count*0.05)]*100:F0} p25 {allSungShort[(int)(allSungShort.Count*0.25)]*100:F0} median {allSungShort[allSungShort.Count/2]*100:F0}");
            }
        }
    }
}
