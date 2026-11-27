// YARG Resurrected - cuts a song's lines into verses, to deal them out between its artists.
using System;
using System.Collections.Generic;

namespace YARG.Gameplay.VocalBattle
{
    /// <summary>
    /// A song with one vocal part for everybody doesn't say who raps what. To let a singer be one
    /// of its artists anyway, its lines are cut into verses and the verses dealt out in turn: the
    /// first to the first artist, the second to the next, and round again.
    ///
    /// A new verse starts after a pause in the words (two seconds or a bar, whichever is longer),
    /// and a stretch of 16 bars without one is cut there. This is a guess: it gives each artist a
    /// fair share of the song in sensible pieces, not the lines they have on the record.
    /// </summary>
    public static class VerseDealer
    {
        public const int LONGEST_VERSE_BARS = 16;
        public const double SHORTEST_PAUSE = 2.0;

        /// <summary>
        /// Which verse (from 0) each line belongs to.
        /// </summary>
        /// <param name="starts">When the words of each line start, in seconds, in order.</param>
        /// <param name="ends">When they end.</param>
        /// <param name="sung">False for a line with nothing to sing: it stays with the verse around it.</param>
        /// <param name="bars">When each bar of the song starts.</param>
        public static int[] Verses(IReadOnlyList<double> starts, IReadOnlyList<double> ends, IReadOnlyList<bool> sung,
            IReadOnlyList<double> bars)
        {
            var verses = new int[starts.Count];
            int verse = 0;
            int verseBar = -1;
            double lastEnd = double.NaN;

            for (int i = 0; i < starts.Count; i++)
            {
                if (sung[i])
                {
                    int bar = BarAt(bars, starts[i]);
                    if (verseBar < 0)
                    {
                        verseBar = bar;
                    }
                    else if (starts[i] - lastEnd >= Math.Max(SHORTEST_PAUSE, BarLength(bars, bar)) ||
                        bar - verseBar >= LONGEST_VERSE_BARS)
                    {
                        verse++;
                        verseBar = bar;
                    }

                    lastEnd = ends[i];
                }

                verses[i] = verse;
            }

            return verses;
        }

        private static int BarAt(IReadOnlyList<double> bars, double time)
        {
            int low = 0, high = bars.Count - 1, found = 0;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (bars[mid] <= time + 0.05)
                {
                    found = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return found;
        }

        private static double BarLength(IReadOnlyList<double> bars, int bar)
        {
            if (bars.Count < 2)
            {
                return SHORTEST_PAUSE;
            }

            int from = Math.Min(Math.Max(0, bar), bars.Count - 2);
            return bars[from + 1] - bars[from];
        }
    }
}
