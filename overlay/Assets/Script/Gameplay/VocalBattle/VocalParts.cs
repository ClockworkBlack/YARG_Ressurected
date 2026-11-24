// YARG Resurrected - names for a song's two vocal parts, and what each vocal level asks for.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Core.Song;

namespace YARG.Gameplay.VocalBattle
{
    /// <summary>
    /// A song with two artists has a vocal part for each (the "harmony" parts of a Rock Band chart).
    /// YARG Converter writes the artists' names next to the song as vocal_parts.txt, one a line,
    /// so the menu before a song can say "Don Toliver" and "Travis Scott" instead of "1" and "2".
    /// </summary>
    public static class VocalParts
    {
        public const string FILE_NAME = "vocal_parts.txt";

        private static string _cachedFor;
        private static string[] _cachedNames = Array.Empty<string>();

        /// <summary>The names of the song's vocal parts, in order. Empty when the song doesn't say.</summary>
        public static string[] NamesFor(SongEntry song)
        {
            if (song == null)
            {
                return Array.Empty<string>();
            }

            string location = song.ActualLocation;
            if (location == _cachedFor)
            {
                return _cachedNames;
            }

            _cachedFor = location;
            _cachedNames = Array.Empty<string>();
            try
            {
                // Only songs kept as a plain folder can carry the file
                if (!string.IsNullOrEmpty(location) && Directory.Exists(location))
                {
                    string path = Path.Combine(location, FILE_NAME);
                    if (File.Exists(path))
                    {
                        var names = new List<string>();
                        foreach (string line in File.ReadAllLines(path))
                        {
                            string name = line.Trim();
                            if (name.Length > 0 && names.Count < 3)
                            {
                                names.Add(name.Length > 40 ? name.Substring(0, 40) : name);
                            }
                        }
                        _cachedNames = names.ToArray();
                    }
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't read the vocal part names");
            }

            return _cachedNames;
        }

        public static bool HasNames(SongEntry song)
        {
            return NamesFor(song).Length >= 2;
        }

        /// <summary>What to call part <paramref name="index"/> (0 = first): the artist's name, or its number.</summary>
        public static string Label(SongEntry song, int index)
        {
            var names = NamesFor(song);
            return index >= 0 && index < names.Length
                ? names[index]
                : (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// One line on what a vocal level asks of the singer, from the numbers the engine really uses:
        /// how close to the note counts, and how much of each line has to be sung.
        /// </summary>
        public static string LevelHint(EnginePreset preset, Difficulty difficulty)
        {
            var vocals = (preset ?? EnginePreset.Default).Vocals;
            float window;
            float share;
            switch (difficulty)
            {
                case Difficulty.Beginner:
                    window = vocals.PitchWindowB;
                    share = vocals.HitPercentB;
                    break;
                case Difficulty.Easy:
                    window = vocals.PitchWindowE;
                    share = vocals.HitPercentE;
                    break;
                case Difficulty.Medium:
                    window = vocals.PitchWindowM;
                    share = vocals.HitPercentM;
                    break;
                case Difficulty.Hard:
                    window = vocals.PitchWindowH;
                    share = vocals.HitPercentH;
                    break;
                default:
                    window = vocals.PitchWindowX;
                    share = vocals.HitPercentX;
                    break;
            }

            // (22.5% is shown as 23%, not rounded down to the even number below)
            int percent = (int) Math.Floor(share * 100f + 0.5001f);
            // Six semitones either way is every note there is: the pitch doesn't matter at all
            string notes = window >= 5.9f
                ? "Any note counts"
                : "Within " + window.ToString("0.#", CultureInfo.InvariantCulture) + " semitones";
            return notes + ", sing " + percent.ToString(CultureInfo.InvariantCulture) + "% of the line";
        }
    }
}
