// YARG Resurrected - checks that a song written by YARG Converter is read by the game as intended.
//
// The converter's own tests read notes.mid back with a reader written to follow the game's rules.
// This goes one better: it hands the song folder to YARG.Core itself, first to the song scanner
// (which decides whether the song shows up, and with which instruments) and then to the chart
// loader (which produces the notes, phrases and lyrics the game plays), and compares what comes
// out with the numbers the converter wrote next to the song in expect.txt.
using System;
using System.Collections.Generic;
using System.IO;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Song;

namespace GameReaderCheck
{
    internal static class Program
    {
        private static int _failures;
        private static Dictionary<string, int> _expect;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + what);
            if (!ok)
            {
                _failures++;
            }
        }

        private static void Same(string key, int actual, string what)
        {
            if (!_expect.TryGetValue(key, out int wanted))
            {
                Check(false, what + ": no expected value '" + key + "'");
                return;
            }

            Check(actual == wanted, what + ": " + actual + " (written: " + wanted + ")");
        }

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: GameReaderCheck <song folder>");
                return 2;
            }

            string dir = Path.GetFullPath(args[0]);
            _expect = new Dictionary<string, int>();
            foreach (string line in File.ReadAllLines(Path.Combine(dir, "expect.txt")))
            {
                int eq = line.IndexOf('=');
                if (eq > 0)
                {
                    _expect[line.Substring(0, eq).Trim()] = int.Parse(line.Substring(eq + 1).Trim());
                }
            }

            Console.WriteLine("The game's song scan");
            var scan = UnpackedIniEntry.ProcessNewEntry(dir, new FileInfo(Path.Combine(dir, "notes.mid")),
                YARG.Core.Song.ChartFormat.Mid, new FileInfo(Path.Combine(dir, "song.ini")), "Check");
            if (!scan.HasValue)
            {
                Console.WriteLine("  FAIL  the scan turned the song down: " + scan.Error);
                return 1;
            }

            var entry = scan.Value;
            Check(entry.HasInstrument(Instrument.FiveFretGuitar), "guitar is listed");
            Check(entry.HasInstrument(Instrument.FiveFretBass), "bass is listed");
            Check(entry.HasInstrument(Instrument.Vocals), "vocals are listed");
            bool two = _expect["parts"] == 2;
            Check(entry.HasInstrument(Instrument.Harmony) == two, "harmonies (pick an artist) listed only for a two-singer song");
            Same("parts", entry.VocalsCount, "number of vocal parts");

            Console.WriteLine("The game's chart loader");
            var chart = entry.LoadChart();
            if (chart == null)
            {
                Console.WriteLine("  FAIL  the chart did not load");
                return 1;
            }

            Instruments("guitar", chart.FiveFretGuitar);
            Instruments("bass", chart.FiveFretBass);

            Check(chart.Vocals.Parts.Count == 1, "one lead vocals part");
            Part("lead", chart.Vocals.Parts[0]);
            Same("lead_phrases", chart.Lyrics.Phrases.Count, "lines in the lyric bar other players see");
            if (two)
            {
                Check(chart.Harmony.Parts.Count == 3, "harmony track has its three slots");
                Part("harm1", chart.Harmony.Parts[0]);
                Part("harm2", chart.Harmony.Parts[1]);
                Check(chart.Harmony.Parts[2].NotePhrases.Count == 0, "no third singer");
            }
            else
            {
                Check(chart.Harmony.Parts.TrueForAll(p => p.NotePhrases.Count == 0), "no harmony parts for one singer");
            }

            // Timing survives the trip: the tempo map is written in microseconds per beat.
            var lead = chart.Vocals.Parts[0];
            if (lead.NotePhrases.Count > 0)
            {
                var first = lead.NotePhrases[0].PhraseParentNote.ChildNotes[0];
                var lastPhrase = lead.NotePhrases[lead.NotePhrases.Count - 1].PhraseParentNote;
                var last = lastPhrase.ChildNotes[lastPhrase.ChildNotes.Count - 1];
                int firstMs = (int) Math.Round(first.Time * 1000);
                int lastMs = (int) Math.Round(last.Time * 1000);
                Check(Math.Abs(firstMs - _expect["first_word_ms"]) <= 2, "first word at " + firstMs + " ms (written: " + _expect["first_word_ms"] + ")");
                Check(Math.Abs(lastMs - _expect["last_word_ms"]) <= 2, "last word at " + lastMs + " ms (written: " + _expect["last_word_ms"] + ")");

                Console.WriteLine("First lines as the game shows them:");
                for (int i = 0; i < lead.NotePhrases.Count && i < 4; i++)
                {
                    var phrase = lead.NotePhrases[i];
                    var words = new List<string>();
                    foreach (var lyric in phrase.Lyrics)
                    {
                        words.Add(lyric.Text);
                    }
                    var notes = new List<string>();
                    foreach (var note in phrase.PhraseParentNote.ChildNotes)
                    {
                        notes.Add(note.IsNonPitched ? "spoken" : ((int) note.Pitch).ToString());
                    }
                    Console.WriteLine("  " + phrase.Time.ToString("0.00") + " s  " + string.Join(" ", words) + "   [" + string.Join(" ", notes) + "]");
                }
            }

            Check(chart.Sections.Count > 0, "practice sections: " + chart.Sections.Count);

            Console.WriteLine(_failures == 0 ? "The game reads the converted song as written" : "FAILED: " + _failures + " check(s)");
            return _failures == 0 ? 0 : 1;
        }

        private static void Instruments(string key, InstrumentTrack<GuitarNote> track)
        {
            var levels = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert };
            for (int d = 0; d < levels.Length; d++)
            {
                if (!track.TryGetDifficulty(levels[d], out var level))
                {
                    Check(false, key + " " + levels[d] + " is missing");
                    continue;
                }

                int held = 0;
                int chords = 0;
                foreach (var note in level.Notes)
                {
                    if (note.IsSustain)
                    {
                        held++;
                    }
                    if (note.ChildNotes.Count > 0)
                    {
                        chords++;
                    }
                }

                Same(key + "_" + d, level.Notes.Count, key + " " + levels[d] + " notes");
                Same(key + "_" + d + "_held", held, key + " " + levels[d] + " held notes");
                Same(key + "_" + d + "_chords", chords, key + " " + levels[d] + " chords");
            }
        }

        private static void Part(string key, VocalsPart part)
        {
            int words = 0, spoken = 0, slides = 0, lyrics = 0, starPower = 0;
            foreach (var phrase in part.NotePhrases)
            {
                if (phrase.IsStarPower)
                {
                    starPower++;
                }

                lyrics += phrase.Lyrics.Count;
                foreach (var note in phrase.PhraseParentNote.ChildNotes)
                {
                    words++;
                    if (note.IsNonPitched)
                    {
                        spoken++;
                    }
                    slides += note.ChildNotes.Count;
                }
            }

            Same(key + "_phrases", part.NotePhrases.Count, key + " phrases");
            Same(key + "_words", words, key + " words to sing");
            Same(key + "_words", lyrics, key + " lyrics shown");
            Same(key + "_spoken", spoken, key + " spoken words (any sound counts)");
            Same(key + "_slides", slides, key + " joined-on notes of held words");
            Same(key + "_star", starPower, key + " star power phrases");
        }
    }
}
