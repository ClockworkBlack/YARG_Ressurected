// YARG Resurrected - Story Mode. Plain .NET, tested in tests/StoryTests.
//
// Suggests songs for a gig from the player's own library.
//
// The game ships no songs, so the story cannot ask for particular ones. Instead every gig has a
// "vibe" (genre families, a span of years, a few words) and the songs in the library are ranked by
// how well they fit it:
//   - the song's genre text is sorted into families ("R&B/Soul/Funk" is R&B, soul and funk);
//   - a song from the right years fits better, and one from nearby years a little better;
//   - a song the story has already used a lot drops down, so sets stay fresh;
//   - songs with no genre or year (most home-made conversions) sit in the middle: never ruled out.
// Any song can still be picked by hand. Nothing here decides what may be played.
using System;
using System.Collections.Generic;

namespace YARG.Story
{
    /// <summary>What the story needs to know about one song in the library.</summary>
    public sealed class StorySong
    {
        /// <summary>Stays the same between runs of the game (the song's hash).</summary>
        public string Id = "";

        public string Name = "";
        public string Artist = "";
        public string Album = "";
        public string Genre = "";

        /// <summary>0 when the song does not say.</summary>
        public int Year;

        /// <summary>0 when unknown.</summary>
        public double LengthSeconds;

        /// <summary>The game's own object for the song.</summary>
        public object Handle;
    }

    /// <summary>The families of music the story sorts genre text into.</summary>
    public static class StoryGenres
    {
        public const string RNB = "rnb";
        public const string SOUL = "soul";
        public const string FUNK = "funk";
        public const string HIPHOP = "hiphop";
        public const string POP = "pop";
        public const string GOSPEL = "gospel";
        public const string BLUES = "blues";
        public const string JAZZ = "jazz";
        public const string DISCO = "disco";
        public const string DANCE = "dance";
        public const string REGGAE = "reggae";
        public const string AFRO = "afro";
        public const string LATIN = "latin";
        public const string ROCK = "rock";

        public static readonly string[] All =
        {
            RNB, SOUL, FUNK, HIPHOP, POP, GOSPEL, BLUES, JAZZ, DISCO, DANCE, REGGAE, AFRO, LATIN, ROCK,
        };

        /// <summary>The families the story is an homage to. A song from any of them is "in the tradition".</summary>
        public static readonly string[] Tradition =
        {
            RNB, SOUL, FUNK, HIPHOP, POP, GOSPEL, BLUES, JAZZ, DISCO, DANCE, REGGAE, AFRO,
        };

        // Words and phrases (whole words only) that put a genre text into a family. A phrase may
        // put a song into several families.
        private static readonly KeyValuePair<string, string[]>[] _phrases =
        {
            Entry("rhythm and blues", RNB),
            Entry("r and b", RNB),
            Entry("r b", RNB),
            Entry("rnb", RNB),
            Entry("randb", RNB),
            Entry("new jack swing", RNB, HIPHOP),
            Entry("new jack", RNB, HIPHOP),
            Entry("quiet storm", RNB, SOUL),
            Entry("slow jam", RNB),
            Entry("slow jams", RNB),
            Entry("neo soul", RNB, SOUL),
            Entry("neosoul", RNB, SOUL),
            Entry("urban", RNB, HIPHOP),
            Entry("contemporary r", RNB),
            Entry("soul", SOUL),
            Entry("motown", SOUL, RNB, POP),
            Entry("doo wop", SOUL, RNB),
            Entry("doowop", SOUL, RNB),
            Entry("northern soul", SOUL),
            Entry("philly", SOUL),
            Entry("funk", FUNK),
            Entry("p funk", FUNK),
            Entry("g funk", HIPHOP, FUNK),
            Entry("go go", FUNK),
            Entry("gogo", FUNK),
            Entry("boogie", FUNK, DISCO),
            Entry("hip hop", HIPHOP),
            Entry("hiphop", HIPHOP),
            Entry("rap", HIPHOP),
            Entry("trap", HIPHOP),
            Entry("drill", HIPHOP),
            Entry("crunk", HIPHOP),
            Entry("bounce", HIPHOP),
            Entry("boom bap", HIPHOP),
            Entry("hyphy", HIPHOP),
            Entry("grime", HIPHOP),
            Entry("horrorcore", HIPHOP),
            Entry("pop", POP),
            Entry("synthpop", POP, DANCE),
            Entry("electropop", POP, DANCE),
            Entry("gospel", GOSPEL),
            Entry("spiritual", GOSPEL),
            Entry("spirituals", GOSPEL),
            Entry("choir", GOSPEL),
            Entry("blues", BLUES),
            Entry("jazz", JAZZ),
            Entry("swing", JAZZ),
            Entry("bebop", JAZZ),
            Entry("big band", JAZZ),
            Entry("ragtime", JAZZ),
            Entry("fusion", JAZZ, FUNK),
            Entry("disco", DISCO, DANCE),
            Entry("dance", DANCE),
            Entry("house", DANCE),
            Entry("electronic", DANCE),
            Entry("electro", DANCE, HIPHOP),
            Entry("edm", DANCE),
            Entry("techno", DANCE),
            Entry("garage", DANCE),
            Entry("freestyle", DANCE, POP),
            Entry("miami bass", HIPHOP, DANCE),
            Entry("club", DANCE),
            Entry("reggae", REGGAE),
            Entry("dancehall", REGGAE),
            Entry("ska", REGGAE),
            Entry("dub", REGGAE),
            Entry("afrobeats", AFRO, POP),
            Entry("afrobeat", AFRO, FUNK),
            Entry("afropop", AFRO, POP),
            Entry("afro", AFRO),
            Entry("amapiano", AFRO, DANCE),
            Entry("highlife", AFRO),
            Entry("latin", LATIN),
            Entry("reggaeton", LATIN, HIPHOP),
            Entry("salsa", LATIN),
            // Genre codes from Rock Band song packs
            Entry("rbsoulfunk", RNB, SOUL, FUNK),
            Entry("hiphoprap", HIPHOP),
            Entry("popdanceelectronic", POP, DANCE),
            Entry("poprock", POP, ROCK),
            Entry("reggaeska", REGGAE),
            Entry("inspirational", GOSPEL),
            Entry("rock", ROCK),
            Entry("metal", ROCK),
            Entry("punk", ROCK),
            Entry("alternative", ROCK),
            Entry("indie", ROCK),
            Entry("grunge", ROCK),
            Entry("emo", ROCK),
            Entry("prog", ROCK),
            Entry("glam", ROCK),
            Entry("new wave", ROCK, POP),
        };

        private static KeyValuePair<string, string[]> Entry(string phrase, params string[] families)
        {
            return new KeyValuePair<string, string[]>(phrase, families);
        }

        /// <summary>
        /// Lower case, with "&amp;" spelled out and every other sign turned into a space, and a
        /// space at each end so whole words can be found with " word ".
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return " ";
            }

            var builder = new System.Text.StringBuilder(text.Length + 8);
            builder.Append(' ');
            bool lastSpace = true;
            foreach (char raw in text)
            {
                char c = char.ToLowerInvariant(raw);
                if (c == '&')
                {
                    if (!lastSpace)
                    {
                        builder.Append(' ');
                    }

                    builder.Append("and ");
                    lastSpace = true;
                }
                else if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                    lastSpace = false;
                }
                else if (!lastSpace)
                {
                    builder.Append(' ');
                    lastSpace = true;
                }
            }

            if (!lastSpace)
            {
                builder.Append(' ');
            }

            return builder.ToString();
        }

        /// <summary>The families a genre text belongs to. Empty when it says nothing we know.</summary>
        public static List<string> FamiliesOf(string genre)
        {
            var families = new List<string>();
            string text = Normalize(genre);
            if (text.Length <= 1)
            {
                return families;
            }

            // "Rhythm and blues" is not the blues: take those words out before looking for "blues"
            bool rhythmAndBlues = text.Contains(" rhythm and blues ");

            foreach (var entry in _phrases)
            {
                if (!text.Contains(" " + entry.Key + " "))
                {
                    continue;
                }

                if (entry.Key == "blues" && rhythmAndBlues &&
                    text.Replace(" rhythm and blues ", " ").IndexOf(" blues ", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                foreach (string family in entry.Value)
                {
                    if (!families.Contains(family))
                    {
                        families.Add(family);
                    }
                }
            }

            return families;
        }

        public static bool IsKnown(string family)
        {
            return Array.IndexOf(All, family) >= 0;
        }
    }

    /// <summary>
    /// A small random number generator that gives the same numbers on every device, so a
    /// suggestion can be repeated from its seed.
    /// </summary>
    public struct StoryRandom
    {
        private uint _state;

        public StoryRandom(int seed)
        {
            _state = (uint) seed * 747796405u + 2891336453u;
            if (_state == 0)
            {
                _state = 0x9E3779B9u;
            }

            Next();
        }

        public uint Next()
        {
            // xorshift32
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>A number from 0 up to, not including, 1.</summary>
        public double NextDouble()
        {
            return (Next() >> 8) / 16777216.0;
        }

        /// <summary>A whole number from 0 up to, not including, <paramref name="max"/>.</summary>
        public int Range(int max)
        {
            return max <= 0 ? 0 : (int) (NextDouble() * max);
        }

        /// <summary>The same seed for the same text on every device (string.GetHashCode is not).</summary>
        public static int SeedFrom(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (text != null)
                {
                    foreach (char c in text)
                    {
                        hash = (hash ^ c) * 16777619u;
                    }
                }

                return (int) hash;
            }
        }
    }

    public static class StorySetlist
    {
        // How much each part of the fit is worth. Genre matters most, then the years.
        private const double GENRE_POINTS = 60;
        private const double TRADITION_POINTS = 8;
        private const double UNKNOWN_GENRE_POINTS = 14;
        private const double YEAR_IN_RANGE_POINTS = 25;
        private const double YEAR_NEAR_POINTS = 15;
        private const double YEAR_AROUND_POINTS = 8;
        private const double UNKNOWN_YEAR_POINTS = 6;
        private const double KEYWORD_POINTS = 12;
        private const double MAX_KEYWORD_POINTS = 24;
        private const double REPEAT_PENALTY = 7;
        private const double MAX_REPEAT_PENALTY = 28;
        private const double ODD_LENGTH_PENALTY = 15;
        private const double SAME_ARTIST_PENALTY = 10;
        private const double JITTER = 9;

        /// <summary>
        /// How well a song fits a vibe. Higher is better; only the order matters. A song that fits
        /// nothing still scores zero or more, so it can still be suggested when there is nothing better.
        /// </summary>
        public static double Fit(StorySong song, StoryVibe vibe)
        {
            if (song == null)
            {
                return 0;
            }

            vibe ??= new StoryVibe();
            double score = 0;

            // Genre
            var families = StoryGenres.FamiliesOf(song.Genre);
            if (families.Count == 0)
            {
                score += vibe.Genres.Count > 0 ? UNKNOWN_GENRE_POINTS : 0;
            }
            else
            {
                double best = 0;
                int matches = 0;
                for (int i = 0; i < vibe.Genres.Count; i++)
                {
                    if (!families.Contains(vibe.Genres[i]))
                    {
                        continue;
                    }

                    matches++;
                    // The first genre a gig names is the one it wants most
                    double weight = Math.Max(0.4, 1.0 - 0.15 * i);
                    best = Math.Max(best, weight);
                }

                score += best * GENRE_POINTS + Math.Max(0, matches - 1) * 4;

                if (matches == 0)
                {
                    foreach (string family in StoryGenres.Tradition)
                    {
                        if (families.Contains(family))
                        {
                            score += TRADITION_POINTS;
                            break;
                        }
                    }
                }
            }

            // Years
            if (vibe.YearFrom > 0 || vibe.YearTo > 0)
            {
                int from = vibe.YearFrom > 0 ? vibe.YearFrom : int.MinValue;
                int to = vibe.YearTo > 0 ? vibe.YearTo : int.MaxValue;
                if (song.Year <= 0)
                {
                    score += UNKNOWN_YEAR_POINTS;
                }
                else if (song.Year >= from && song.Year <= to)
                {
                    score += YEAR_IN_RANGE_POINTS;
                }
                else
                {
                    long distance = song.Year < from ? (long) from - song.Year : (long) song.Year - to;
                    if (distance <= 5)
                    {
                        score += YEAR_NEAR_POINTS;
                    }
                    else if (distance <= 10)
                    {
                        score += YEAR_AROUND_POINTS;
                    }
                }
            }

            // Words
            if (vibe.Keywords.Count > 0)
            {
                string text = StoryGenres.Normalize(song.Name + " " + song.Artist + " " + song.Album + " " + song.Genre);
                double points = 0;
                foreach (string keyword in vibe.Keywords)
                {
                    string word = StoryGenres.Normalize(keyword);
                    if (word.Length > 1 && text.Contains(word))
                    {
                        points += KEYWORD_POINTS;
                    }
                }

                score += Math.Min(points, MAX_KEYWORD_POINTS);
            }

            // A ten-second skit or a twenty-minute jam is a strange thing to open a show with
            if (song.LengthSeconds > 0 && (song.LengthSeconds < 45 || song.LengthSeconds > 12 * 60))
            {
                score -= ODD_LENGTH_PENALTY;
            }

            return Math.Max(0, score);
        }

        /// <summary>
        /// Picks a set for a gig. Returns fewer songs than asked only when the library is empty;
        /// a library smaller than the set repeats songs.
        /// </summary>
        /// <param name="playCounts">How many times the story has used each song id so far (may be null).</param>
        /// <param name="avoid">Song ids to keep out when there are enough others (may be null): the last set, or the suggestions being replaced.</param>
        public static List<StorySong> Suggest(IReadOnlyList<StorySong> library, StoryVibe vibe, int count, int seed,
            IReadOnlyDictionary<string, int> playCounts = null, ICollection<string> avoid = null)
        {
            var picked = new List<StorySong>();
            if (library == null || library.Count == 0 || count <= 0)
            {
                return picked;
            }

            var random = new StoryRandom(seed);
            var ranked = new List<KeyValuePair<StorySong, double>>(library.Count);
            foreach (var song in library)
            {
                if (song == null)
                {
                    continue;
                }

                double score = Fit(song, vibe);

                if (playCounts != null && song.Id != null && playCounts.TryGetValue(song.Id, out int plays))
                {
                    score -= Math.Min(MAX_REPEAT_PENALTY, plays * REPEAT_PENALTY);
                }

                // A little luck, so two songs that fit equally well take turns
                score += random.NextDouble() * JITTER;
                ranked.Add(new KeyValuePair<StorySong, double>(song, score));
            }

            if (ranked.Count == 0)
            {
                return picked;
            }

            // Songs to avoid go to the back of the line rather than out of it
            int avoidable = 0;
            if (avoid != null && avoid.Count > 0)
            {
                foreach (var pair in ranked)
                {
                    if (pair.Key.Id != null && avoid.Contains(pair.Key.Id))
                    {
                        avoidable++;
                    }
                }
            }

            bool useAvoid = avoidable > 0 && ranked.Count - avoidable >= count;

            var used = new HashSet<StorySong>();
            while (picked.Count < count)
            {
                StorySong best = null;
                double bestScore = double.NegativeInfinity;
                foreach (var pair in ranked)
                {
                    if (used.Contains(pair.Key))
                    {
                        continue;
                    }

                    if (useAvoid && pair.Key.Id != null && avoid.Contains(pair.Key.Id))
                    {
                        continue;
                    }

                    double score = pair.Value;

                    // Spread the set over different artists when there is a choice
                    foreach (var already in picked)
                    {
                        if (!string.IsNullOrEmpty(already.Artist) &&
                            string.Equals(already.Artist, pair.Key.Artist, StringComparison.OrdinalIgnoreCase))
                        {
                            score -= SAME_ARTIST_PENALTY;
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = pair.Key;
                    }
                }

                if (best == null)
                {
                    if (used.Count == 0)
                    {
                        break;
                    }

                    // The library is smaller than the set: go around again
                    used.Clear();
                    useAvoid = false;
                    continue;
                }

                used.Add(best);
                picked.Add(best);
            }

            return picked;
        }

        /// <summary>
        /// The whole library, best fit first, for the "pick a song yourself" list. Songs that fit
        /// equally well are in alphabetical order.
        /// </summary>
        public static List<StorySong> Rank(IReadOnlyList<StorySong> library, StoryVibe vibe)
        {
            var ranked = new List<KeyValuePair<StorySong, double>>();
            if (library != null)
            {
                foreach (var song in library)
                {
                    if (song != null)
                    {
                        ranked.Add(new KeyValuePair<StorySong, double>(song, Fit(song, vibe)));
                    }
                }
            }

            ranked.Sort((a, b) =>
            {
                int byFit = b.Value.CompareTo(a.Value);
                if (byFit != 0)
                {
                    return byFit;
                }

                int byName = string.Compare(a.Key.Name, b.Key.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.Compare(a.Key.Artist, b.Key.Artist, StringComparison.OrdinalIgnoreCase);
            });

            var result = new List<StorySong>(ranked.Count);
            foreach (var pair in ranked)
            {
                result.Add(pair.Key);
            }

            return result;
        }

        /// <summary>A few words on how well a song fits, for the song list: "Great fit", "Good fit" or nothing.</summary>
        public static string FitLabel(StorySong song, StoryVibe vibe)
        {
            double fit = Fit(song, vibe);
            if (fit >= GENRE_POINTS * 0.8 + YEAR_AROUND_POINTS)
            {
                return "Great fit";
            }

            return fit >= GENRE_POINTS * 0.5 ? "Good fit" : "";
        }
    }
}
