// YARG Resurrected - checks the play-test song (tests/smoke-song) with YARG.Core's own song scanner
// and chart loader, the code the game uses, so the desktop play tests start from a song the game
// is known to accept: listed with guitar, and 81 Expert guitar notes over about 40 seconds.
using System;
using System.IO;
using YARG.Core;
using YARG.Core.Song;

namespace SmokeSongCheck
{
    internal static class Program
    {
        private static int _failures;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + what);
            if (!ok)
            {
                _failures++;
            }
        }

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: SmokeSongCheck <song folder>");
                return 2;
            }

            string dir = Path.GetFullPath(args[0]);
            Console.WriteLine("The game's song scan");
            var scan = UnpackedIniEntry.ProcessNewEntry(dir, new FileInfo(Path.Combine(dir, "notes.chart")),
                ChartFormat.Chart, new FileInfo(Path.Combine(dir, "song.ini")), "Check");
            if (!scan.HasValue)
            {
                Console.WriteLine("  FAIL  the scan turned the song down: " + scan.Error);
                return 1;
            }

            var entry = scan.Value;
            Check(entry.Name.Original == "Smoke Test", "name is 'Smoke Test' (" + entry.Name.Original + ")");
            Check(entry.HasInstrument(Instrument.FiveFretGuitar), "guitar is listed");

            Console.WriteLine("The game's chart loader");
            var chart = entry.LoadChart();
            if (chart == null)
            {
                Console.WriteLine("  FAIL  the chart did not load");
                return 1;
            }

            var expert = chart.FiveFretGuitar.GetDifficulty(Difficulty.Expert);
            int notes = expert.Notes.Count;
            Check(notes == 81, "81 Expert guitar notes (" + notes + ")");
            if (notes > 0)
            {
                double first = expert.Notes[0].Time, last = expert.Notes[notes - 1].Time;
                Check(Math.Abs(first - 2.0) < 0.01, "first note at 2.0 s (" + first.ToString("0.000") + ")");
                Check(Math.Abs(last - 42.0) < 0.01, "last note at 42.0 s (" + last.ToString("0.000") + ")");
            }

            Console.WriteLine(_failures == 0 ? "All checks passed" : _failures + " check(s) failed");
            return _failures == 0 ? 0 : 1;
        }
    }
}
