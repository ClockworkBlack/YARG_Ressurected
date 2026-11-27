// YARG Resurrected - automatic timing: works out a controller's delay from how the player's notes land.
using System;
using System.Collections.Generic;

namespace YARG.Input
{
    /// <summary>
    /// Every controller is late by its own amount: a Bluetooth guitar more than a wired one, a TV
    /// more than a phone's own screen. The game has a number for it on each profile (Input
    /// Calibration), but finding the number by hand is guesswork.
    ///
    /// This finds it from play. Each note the player hits, and each strum that found no note but
    /// had one close by that the held frets fit, says how early or late the player was. One note
    /// says little (hands are not that steady), the middle one of a dozen says a lot. When that
    /// middle is more than a few thousandths of a second off, most of it is taken out of the
    /// calibration, and the next batch is measured against the new value.
    ///
    /// No Unity in here, so it can be tested on its own.
    /// </summary>
    public sealed class AutoTiming
    {
        /// <summary>Notes measured before the first correction, and before each one after.</summary>
        public const int FIRST_BATCH = 8;
        public const int BATCH = 16;

        /// <summary>A middle closer to zero than this (ms) is left alone.</summary>
        public const double DEADBAND_MS = 12.0;

        /// <summary>The share of the measured error taken out each time.</summary>
        public const double GAIN = 0.75;

        /// <summary>The most one correction changes (ms), and the furthest the calibration is taken.</summary>
        public const double MAX_STEP_MS = 150.0;
        public const long LIMIT_MS = 350;

        /// <summary>
        /// A note this far (ms) from a strum, or further, says nothing about it. Wider than the
        /// limit: a run of one fret can pull the calibration the wrong way (see LoneFittingNote),
        /// and the next notes on other frets have to be able to pull it back.
        /// </summary>
        public const double REACH_MS = 450.0;

        private readonly List<double> _batch = new();
        private readonly List<double> _sorted = new();

        /// <summary>How many notes have been measured.</summary>
        public int Measured { get; private set; }

        /// <summary>How many corrections have been made.</summary>
        public int Corrections { get; private set; }

        /// <summary>The middle of the last full batch, in ms (positive: late).</summary>
        public double LastMiddle { get; private set; }

        /// <summary>The player was this many ms late (negative: early) on a note.</summary>
        public void Add(double lateMs)
        {
            if (double.IsNaN(lateMs) || Math.Abs(lateMs) > REACH_MS)
            {
                return;
            }

            _batch.Add(lateMs);
            Measured++;
        }

        /// <summary>
        /// Once enough notes are in: how much to add to the calibration, in ms (0 for nothing).
        /// </summary>
        public int TakeCorrection(long calibrationMs)
        {
            if (_batch.Count < (Corrections == 0 ? FIRST_BATCH : BATCH))
            {
                return 0;
            }

            _sorted.Clear();
            _sorted.AddRange(_batch);
            _sorted.Sort();
            _batch.Clear();

            int half = _sorted.Count / 2;
            double middle = _sorted.Count % 2 == 1 ? _sorted[half] : (_sorted[half - 1] + _sorted[half]) / 2.0;
            LastMiddle = middle;
            if (Math.Abs(middle) < DEADBAND_MS)
            {
                return 0;
            }

            double step = Math.Max(-MAX_STEP_MS, Math.Min(MAX_STEP_MS, middle * GAIN));
            long target = Math.Max(-LIMIT_MS, Math.Min(LIMIT_MS, calibrationMs + (long) Math.Round(step)));
            int change = (int) (target - calibrationMs);
            if (change != 0)
            {
                Corrections++;
            }
            return change;
        }

        /// <summary>
        /// Whether the frets held are the ones a note asks for. A lower fret may be held as well
        /// (an anchored finger), as the game itself allows.
        /// </summary>
        /// <param name="held">The frets held, green in bit 0 to orange in bit 4.</param>
        /// <param name="note">The note's frets, the same way; none for an open note.</param>
        public static bool FretsFit(int held, int note)
        {
            held &= 31;
            note &= 31;
            if (note == 0)
            {
                return held == 0;
            }

            int lowest = note & -note;
            return (held & ~(lowest - 1)) == note;
        }

        /// <summary>
        /// A strum found no note. If exactly one note within reach is one the strum could have
        /// been for (the held frets fit it, and it has not been played), the strum was meant for
        /// it: says which note and how late the strum was, in seconds. False when there is no such
        /// note, or more than one (the same note repeated: it could be any of them).
        ///
        /// The frets matter. With notes evenly spaced, a strum 280 ms late for one note is also
        /// 120 ms early for the next; only the frets say which it was.
        /// </summary>
        /// <param name="count">How many notes the part has.</param>
        /// <param name="timeOf">The time of a note, in seconds; notes are in order.</param>
        /// <param name="couldBe">Whether the strum could have been for this note.</param>
        /// <param name="time">When the strum came.</param>
        public static bool LoneFittingNote(int count, Func<int, double> timeOf, Func<int, bool> couldBe, double time,
            out int index, out double late)
        {
            index = -1;
            late = 0;
            double reach = REACH_MS / 1000.0;

            // The first note within reach
            int low = 0, high = count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (timeOf(mid) < time - reach) low = mid + 1;
                else high = mid;
            }

            for (int i = low; i < count && timeOf(i) <= time + reach; i++)
            {
                if (!couldBe(i))
                {
                    continue;
                }

                if (index >= 0)
                {
                    index = -1;
                    return false;
                }
                index = i;
            }

            if (index < 0)
            {
                return false;
            }

            late = time - timeOf(index);
            return true;
        }
    }
}
