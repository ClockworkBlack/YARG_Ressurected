using System;
using System.Collections.Generic;
using YARG.Input;

namespace RemoteServerTests
{
    /// <summary>Automatic timing (Settings > General > Automatic Timing For Each Controller).</summary>
    public static class AutoTimingTests
    {
        /// <summary>
        /// A player whose controller is <paramref name="delay"/> ms late and whose hands are steady
        /// to <paramref name="spread"/> ms plays <paramref name="notes"/> notes, one every
        /// <paramref name="gap"/> ms, with notes counting <paramref name="window"/> ms either side.
        /// Returns the calibration at the end and how many of the last 50 notes were hit.
        /// </summary>
        private static (long Calibration, int LastHits, int Corrections) Play(double delay, double spread, int notes,
            double gap, double window, int seed, long startAt = 0, int sameFretsFor = 0)
        {
            var random = new Random(seed);
            var timing = new AutoTiming();
            long calibration = startAt;
            int lastHits = 0;

            var times = new double[notes];
            for (int i = 0; i < notes; i++) times[i] = 5 + i * gap / 1000.0;
            // Green, red, yellow in turn, after so many notes on green alone
            var frets = new int[notes];
            for (int i = 0; i < notes; i++) frets[i] = i < sameFretsFor ? 1 : 1 << (i % 3);
            var played = new bool[notes];

            for (int i = 0; i < notes; i++)
            {
                // (Roughly bell-shaped: the sum of three)
                double hand = (random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5) * spread * 1.4;
                // What the game makes of the strum: the player's moment, plus the delay, less the calibration
                double late = hand + delay - calibration;
                double strum = times[i] + late / 1000.0;

                // The game plays the nearest note the frets fit that the strum is in time for
                int hit = -1;
                for (int n = Math.Max(0, i - 2); n < Math.Min(notes, i + 3); n++)
                {
                    if (!played[n] && frets[n] == frets[i] && Math.Abs(strum - times[n]) * 1000.0 <= window &&
                        (hit < 0 || Math.Abs(strum - times[n]) < Math.Abs(strum - times[hit])))
                    {
                        hit = n;
                    }
                }

                if (hit >= 0)
                {
                    played[hit] = true;
                    timing.Add((strum - times[hit]) * 1000.0);
                    if (i >= notes - 50 && hit == i) lastHits++;
                }
                else if (AutoTiming.LoneFittingNote(notes, n => times[n], n => !played[n] && AutoTiming.FretsFit(frets[i], frets[n]),
                    strum, out _, out double off))
                {
                    // A strum with no note to play: measured against the one note it could have been for
                    timing.Add(off * 1000.0);
                }

                calibration += timing.TakeCorrection(calibration);
            }

            return (calibration, lastHits, timing.Corrections);
        }

        public static void Run(Action<bool, string> check)
        {
            // ---- A Bluetooth guitar 120 ms late, notes counting 100 ms either side (the Relaxed
            // engine): at first most strums miss; the timing is found from the near misses
            {
                var (calibration, hits, _) = Play(120, 25, 200, 500, 100, 1);
                check(Math.Abs(calibration - 120) <= 12, $"120 ms late: calibration found ({calibration} ms)");
                check(hits >= 49, $"120 ms late: the last 50 notes are hit ({hits})");
            }

            // ---- The same on the strict engine (70 ms either side) and with a shakier hand
            {
                var (calibration, hits, _) = Play(120, 40, 300, 500, 70, 2);
                check(Math.Abs(calibration - 120) <= 15, $"120 ms late, 70 ms window, shaky hand: found ({calibration} ms)");
                check(hits >= 44, $"...and the last 50 notes are mostly hit ({hits})");
            }

            // ---- Delays of every size, early ones too (a calibration set too high by hand)
            foreach (double delay in new[] { -90.0, -30.0, 15.0, 45.0, 80.0, 200.0, 280.0 })
            {
                var (calibration, hits, _) = Play(delay, 25, 300, 400, 100, 3 + (int) delay);
                check(Math.Abs(calibration - delay) <= 12 && hits >= 48,
                    $"{delay} ms: calibration {calibration} ms, {hits} of the last 50 hit");
            }

            // ---- Already right: left alone (no hunting back and forth)
            {
                var (calibration, _, corrections) = Play(60, 25, 600, 400, 100, 9, startAt: 60);
                check(Math.Abs(calibration - 60) <= 10 && corrections <= 3,
                    $"already right: stays ({calibration} ms, {corrections} corrections in 600 notes)");
            }

            // ---- Never beyond the limit, whatever comes in
            {
                var timing = new AutoTiming();
                long calibration = 0;
                for (int i = 0; i < 400; i++)
                {
                    timing.Add(340);
                    calibration += timing.TakeCorrection(calibration);
                }
                check(calibration == AutoTiming.LIMIT_MS, $"stops at the limit ({calibration} ms)");
            }

            // ---- The same fret over and over, evenly spaced, and a long delay: a strum 280 ms late
            // for one note is 120 ms early for the next, and the game plays the next. Nothing can
            // tell those apart, and the calibration may go the wrong way there. As soon as the
            // frets change it is put right.
            {
                var (calibration, hits, _) = Play(280, 20, 400, 400, 100, 11, sameFretsFor: 150);
                check(Math.Abs(calibration - 280) <= 12 && hits >= 48,
                    $"a long delay through a run of one fret, then other frets: found ({calibration} ms, {hits} of the last 50 hit)");
            }

            // ---- Which notes a strum could have been for
            {
                const int G = 1, R = 2, Y = 4, B = 8;
                check(AutoTiming.FretsFit(R, R) && !AutoTiming.FretsFit(G, R) && !AutoTiming.FretsFit(Y, R), "one fret fits its own note only");
                check(AutoTiming.FretsFit(G | R, R) && AutoTiming.FretsFit(G | Y, Y), "a lower fret may be held as well (anchoring)");
                check(!AutoTiming.FretsFit(R | Y, R), "a higher fret held as well does not fit");
                check(AutoTiming.FretsFit(R | B, R | B) && AutoTiming.FretsFit(G | R | B, R | B) && !AutoTiming.FretsFit(R, R | B), "a chord needs all its frets");
                check(AutoTiming.FretsFit(0, 0) && !AutoTiming.FretsFit(G, 0) && AutoTiming.FretsFit(64, 0), "an open note needs no fret held");

                var times = new List<double> { 1.0, 1.4, 1.8, 2.2, 6.0 };
                var frets = new List<int> { G, R, G, R, Y };
                bool Lone(double at, int held, out int index, out double late) =>
                    AutoTiming.LoneFittingNote(times.Count, i => times[i], i => AutoTiming.FretsFit(held, frets[i]), at, out index, out late);

                check(Lone(1.68, R, out int a, out double lateA) && a == 1 && Math.Abs(lateA - 0.28) < 1e-9,
                    "280 ms late for red, 120 ms early for green: the red one, since red is held");
                check(Lone(6.2, Y, out int b, out double lateB) && b == 4 && Math.Abs(lateB - 0.2) < 1e-9, "a late strum after a lone note is measured");
                check(Lone(5.8, Y, out int c, out double lateC) && c == 4 && Math.Abs(lateC + 0.2) < 1e-9, "an early strum before a lone note is measured");
                check(!Lone(1.4, G, out _, out _), "two notes the frets fit: anybody's guess");
                check(!Lone(4.0, Y, out _, out _), "a strum far from every note says nothing");
                check(!Lone(6.1, B, out _, out _), "the wrong fret: says nothing");
                check(!AutoTiming.LoneFittingNote(0, i => 0, i => true, 1.0, out _, out _), "no notes: nothing");
            }
        }
    }
}
