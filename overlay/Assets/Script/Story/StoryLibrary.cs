// YARG Resurrected - Story Mode: the player's song library, as the story sees it.
//
// The story never keeps the game's own song objects for long: a rescan of the songs folder makes
// new ones for every song. So it takes a plain snapshot of the library (name, artist, genre text,
// year, length, and the song's hash as its id) and takes a new one whenever the game's song list
// is replaced. Right before a gig starts, each song of the set is looked up again by its hash.
using System;
using System.Collections.Generic;
using System.Text;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Core.Utility;
using YARG.Song;

namespace YARG.Story
{
    public static class StoryLibrary
    {
        private static List<StorySong> _songs = new List<StorySong>();
        private static SongEntry[] _seen;

        /// <summary>
        /// The last snapshot. Empty until the game has scanned the songs. A rescan makes a new list,
        /// so a list taken earlier never changes under whoever holds it.
        /// </summary>
        public static IReadOnlyList<StorySong> Songs => _songs;

        /// <summary>
        /// Takes a new snapshot if the game's song list changed since the last one (first scan or a
        /// rescan). Main thread only. Returns true when the snapshot changed.
        /// </summary>
        public static bool Refresh()
        {
            var entries = SongContainer.Songs;
            if (entries == null || ReferenceEquals(entries, _seen))
            {
                return false;
            }

            _seen = entries;
            var songs = new List<StorySong>(entries.Length);

            // The list holds every copy of a song that is in the library more than once; the story
            // only wants each song once
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                try
                {
                    if (entry == null || entry.IsDuplicate)
                    {
                        continue;
                    }

                    string id = entry.Hash.ToString();
                    if (!ids.Add(id))
                    {
                        continue;
                    }

                    songs.Add(Snapshot(entry, id));
                }
                catch (Exception e)
                {
                    // One odd song must not leave the story with no library at all
                    YargLogger.LogException(e, "Story: skipped a song that could not be read");
                }
            }

            _songs = songs;
            YargLogger.LogInfo("Story: library snapshot has " + songs.Count + " songs");
            return true;
        }

        private static StorySong Snapshot(SongEntry entry, string id)
        {
            int year = entry.YearAsNumber;
            long length = entry.SongLengthMilliseconds;
            return new StorySong
            {
                Id = id,
                Name = Plain(entry.Name.Original),
                Artist = Plain(entry.Artist.Original),
                Album = Plain(entry.Album.Original),
                Genre = GenreText(entry),
                Year = year == int.MaxValue || year < 0 ? 0 : year,
                LengthSeconds = length > 0 ? length / 1000.0 : 0,
                Handle = entry,
            };
        }

        // The raw genre texts first: the game may rewrite Genre and Subgenre into its own names
        // (or "Unknown Genre"), while the raw ones are what the chart actually says. Both help.
        private static string GenreText(SongEntry entry)
        {
            var text = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddGenre(text, seen, entry.RawGenre.Original);
            AddGenre(text, seen, entry.RawSubgenre.Original);
            AddGenre(text, seen, entry.Genre.Original);
            AddGenre(text, seen, entry.Subgenre.Original);
            return text.ToString();
        }

        private static void AddGenre(StringBuilder text, HashSet<string> seen, string genre)
        {
            string plain = Plain(genre);
            if (plain.Length == 0 || !seen.Add(plain))
            {
                return;
            }

            if (text.Length > 0)
            {
                text.Append(" | ");
            }

            text.Append(plain);
        }

        /// <summary>Text as the player should see it: rich-text tags removed, trimmed, never null.</summary>
        internal static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return (RichTextUtils.StripRichTextTags(text) ?? "").Trim();
        }

        /// <summary>
        /// The game's song for an id from <see cref="StorySong.Id"/>, as it is in the library right
        /// now, or null when the song is no longer there.
        /// </summary>
        public static SongEntry Resolve(string id)
        {
            // The game's hash reader logs an error and returns rubbish for bad text, so check it first
            if (!IsHash(id))
            {
                return null;
            }

            var hash = HashWrapper.FromString(id);
            if (SongContainer.SongsByHash.TryGetValue(hash, out var list) && list != null && list.Count > 0)
            {
                return list[0];
            }

            return null;
        }

        private static bool IsHash(string id)
        {
            if (id == null || id.Length != 40)
            {
                return false;
            }

            foreach (char c in id)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
