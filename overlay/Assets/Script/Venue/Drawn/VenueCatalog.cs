using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YARG.Core.IO;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Core.Venue;
using YARG.Helpers;
using YARG.Settings;

namespace YARG.Venue.Drawn
{
    /// <summary>The kind of room, which decides how the stage is drawn.</summary>
    public enum VenueStyle
    {
        Theater,   // curtains, gold proscenium, footlights
        Arena,     // dark bowl full of phone lights, big screens
        Club,      // brick wall, neon, speaker stacks
        Street,    // block party under a skyline
        Upscale,   // columns, drapes and chandeliers
        Festival,  // open-air stage at dusk
    }

    /// <summary>
    /// One venue of the catalog. These are stages drawn in code in the spirit of places that matter to
    /// Hip Hop and R&amp;B history, not reproductions of them: there are no logos or likenesses, and each
    /// entry says which real place it tips its hat to.
    /// </summary>
    public sealed class DrawnVenue
    {
        public string Id;
        public string Name;
        public string InspiredBy;
        public string Region;
        public VenueStyle Style;
        public bool Upscale;
        public Color Top, Bottom, Accent, Accent2;
        public int Seed;

        public string Label => $"{Region}: {Name}";
    }

    public static class VenueCatalog
    {
        public const string CHOICE_RANDOM = "Random";
        public const string CHOICE_RANDOM_STREET = "RandomStreet";
        public const string CHOICE_RANDOM_UPSCALE = "RandomUpscale";
        public const string CHOICE_NONE = "None";

        private static Color C(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex, out var color) ? color : Color.magenta;
        }

        private static DrawnVenue V(string id, string region, string name, string inspiredBy, VenueStyle style,
            bool upscale, string top, string bottom, string accent, string accent2)
        {
            return new DrawnVenue
            {
                Id = id, Region = region, Name = name, InspiredBy = inspiredBy, Style = style, Upscale = upscale,
                Top = C(top), Bottom = C(bottom), Accent = C(accent), Accent2 = C(accent2),
                Seed = id.GetHashCode() & 0x7FFFFFF,
            };
        }

        public static readonly IReadOnlyList<DrawnVenue> All = new List<DrawnVenue>
        {
            // East Coast
            V("harlem-theater", "East", "Harlem Amateur Night Theater", "the Apollo Theater, Harlem, New York",
                VenueStyle.Theater, false, "3A0A12", "140408", "E8B84A", "C2262E"),
            V("bronx-block-party", "East", "Bronx Park Jam", "the 1970s block parties of the Bronx, where Hip Hop began",
                VenueStyle.Street, false, "F2994A", "3B1D5E", "FFD166", "2EC4B6"),
            V("midtown-garden", "East", "Midtown Arena", "Madison Square Garden, New York",
                VenueStyle.Arena, false, "0B1026", "05060F", "3FA9F5", "F25C54"),
            V("dc-u-street", "East", "U Street Theater", "the Howard Theatre and Washington D.C.'s \"Black Broadway\"",
                VenueStyle.Theater, true, "10233F", "060D1A", "D9B36C", "1F4E8C"),
            V("philly-uptown", "East", "North Philly Soul Hall", "the Uptown Theater, Philadelphia",
                VenueStyle.Club, false, "2B1B3D", "0E0816", "F28FAD", "8CE0F5"),

            // South
            V("atlanta-block", "South", "Atlanta Summer Block Party", "Atlanta's Freaknik years and the city's car and block culture",
                VenueStyle.Street, false, "FF7E5F", "2A1B4A", "FDCB58", "FF4D8D"),
            V("new-orleans-blues", "South", "Crescent City Blues House", "the House of Blues, New Orleans",
                VenueStyle.Club, false, "3B1F12", "120805", "F2A541", "4FC3C8"),
            V("new-orleans-festival", "South", "Crescent City Festival Night", "the Essence Festival, New Orleans",
                VenueStyle.Arena, true, "1A0B2E", "07030F", "C792EA", "F2C14E"),
            V("houston-slab", "South", "Houston Slab Sunday", "Houston's slab line and car show scene",
                VenueStyle.Street, false, "6A3093", "12051F", "E0457B", "FDE74C"),
            V("memphis-beale", "South", "Beale Street Juke", "the clubs of Beale Street, Memphis",
                VenueStyle.Club, false, "12233B", "060B14", "5EC8F2", "F25F5C"),
            V("miami-beach", "South", "Miami Beach Stage", "Miami bass and the city's open-air shows",
                VenueStyle.Festival, false, "1FB5C9", "F2709C", "FFE66D", "FFFFFF"),

            // Midwest
            V("pontiac-ballroom", "Midwest", "Pontiac Ballroom", "the Crofoot Ballroom, Pontiac, Michigan",
                VenueStyle.Club, false, "1F2A44", "0A0E18", "F2B134", "E84855"),
            V("detroit-fox", "Midwest", "Motor City Grand Theater", "the Fox Theatre, Detroit, and the Motown revues",
                VenueStyle.Theater, true, "3D1F0B", "140A04", "F0C75E", "8C2F39"),
            V("chicago-regal", "Midwest", "South Side Regal", "the Regal Theater, Chicago",
                VenueStyle.Theater, false, "1B2A1F", "08100A", "E6C229", "2E8B57"),
            V("minneapolis-club", "Midwest", "Minneapolis Star Wall Club", "First Avenue, Minneapolis",
                VenueStyle.Club, false, "1A1A1A", "050505", "B388EB", "F7F7F7"),

            // West
            V("la-forum", "West", "Inglewood Arena", "the Forum, Inglewood, Los Angeles",
                VenueStyle.Arena, false, "20104A", "080418", "F5B700", "A45EE5"),
            V("compton-swap-meet", "West", "Compton Lot Party", "the swap meets and parking-lot shows of Compton and South Los Angeles",
                VenueStyle.Street, false, "FFB347", "5E2B6E", "7BD389", "FFF275"),
            V("oakland-fox", "West", "Oakland Deco Theater", "the Fox Theater, Oakland",
                VenueStyle.Theater, false, "123B3A", "051514", "F28C28", "E3D26F"),
            V("vegas-residency", "West", "Las Vegas Residency Room", "the Las Vegas showrooms",
                VenueStyle.Upscale, true, "2A0E3A", "0C0412", "F7D774", "E0457B"),

            // High-class rooms
            V("concert-hall", "East", "Uptown Concert Hall", "Carnegie Hall, New York",
                VenueStyle.Upscale, true, "3B2A1A", "120C06", "F2DFA7", "8C1C13"),
            V("dc-arts-center", "East", "Capital Arts Center", "the Kennedy Center, Washington D.C.",
                VenueStyle.Upscale, true, "5A0F1B", "1A0408", "F5E6B8", "C9A227"),

            // International
            V("london-stadium", "International", "London Stadium Night", "Michael Jackson's 1988 Wembley Stadium shows",
                VenueStyle.Arena, false, "0A1A3A", "030812", "FFFFFF", "E63946"),
            V("tokyo-dome", "International", "Tokyo Dome Night", "the Tokyo Dome dates of the late 80s and 90s world tours",
                VenueStyle.Arena, true, "14213D", "05080F", "FCA311", "E5E5E5"),
            V("bucharest-open-air", "International", "Bucharest Open Air", "Michael Jackson's 1992 Bucharest concert",
                VenueStyle.Festival, false, "243B6B", "E0764A", "FFE8A3", "FFFFFF"),
            V("montreux-lakeside", "International", "Lakeside Jazz Festival", "the Montreux Jazz Festival, Switzerland",
                VenueStyle.Upscale, true, "0F2F4A", "05121C", "E9C46A", "2A9D8F"),
            V("johannesburg-festival", "International", "Johannesburg Festival Field", "the big outdoor shows of Johannesburg",
                VenueStyle.Festival, false, "F6AE2D", "86283C", "F26419", "FFF3B0"),
        };

        public static DrawnVenue Find(string id)
        {
            foreach (var venue in All)
            {
                if (venue.Id == id) return venue;
            }
            return null;
        }

        public static string Describe(string choice)
        {
            switch (choice)
            {
                case CHOICE_RANDOM: return "Random venue";
                case CHOICE_RANDOM_STREET: return "Random: street and club";
                case CHOICE_RANDOM_UPSCALE: return "Random: high class";
                case CHOICE_NONE: return "None (the game's own background)";
            }

            var venue = Find(choice);
            return venue != null ? venue.Label : "Random venue";
        }

        private static readonly System.Random _random = new();
        private static string _lastId;

        /// <summary>The venue for the next song, or null to leave the game's own background alone.</summary>
        public static DrawnVenue Choose()
        {
            string choice = SettingsManager.Settings?.VenueChoice.Value ?? CHOICE_RANDOM;
            if (choice == CHOICE_NONE)
            {
                return null;
            }

            var fixedVenue = Find(choice);
            if (fixedVenue != null)
            {
                return fixedVenue;
            }

            var pool = new List<DrawnVenue>();
            foreach (var venue in All)
            {
                if (choice == CHOICE_RANDOM_UPSCALE && !venue.Upscale) continue;
                if (choice == CHOICE_RANDOM_STREET && venue.Upscale) continue;
                // Not the same room twice in a row
                if (venue.Id == _lastId) continue;
                pool.Add(venue);
            }

            if (pool.Count == 0)
            {
                return All[0];
            }

            var picked = pool[_random.Next(pool.Count)];
            _lastId = picked.Id;
            return picked;
        }

        /// <summary>
        /// Where your own pictures or videos for the venues go: one folder per venue, named by its id.
        /// On Android this is Internal storage/YARG/venues, next to the songs folder.
        /// </summary>
        public static string MediaRoot
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Path.Combine(SongFolderHelper.InternalStorageRoot, "YARG", "venues");
#else
                return Path.Combine(PathHelper.PersistentDataPath, "venues");
#endif
            }
        }

        private static bool _foldersMade;

        /// <summary>Makes the per-venue folders once, so there is somewhere obvious to drop files.</summary>
        public static void EnsureFolders()
        {
            if (_foldersMade) return;
            _foldersMade = true;

            try
            {
                foreach (var venue in All)
                {
                    Directory.CreateDirectory(Path.Combine(MediaRoot, venue.Id));
                }
            }
            catch (Exception e)
            {
                // No all-files access yet, or read-only storage: the drawn stages still work
                YargLogger.LogFormatWarning("Could not make the venue folders: {0}", e.Message);
            }
        }

        private static readonly string[] _imageTypes = { ".png", ".jpg", ".jpeg" };
        private static readonly string[] _videoTypes = { ".mp4", ".webm", ".mov" };

        /// <summary>
        /// A picture or video you put in the venue's folder, picked at random if there are several,
        /// or null when the folder is empty and the drawn stage should be used.
        /// </summary>
        public static BackgroundResult LoadMedia(DrawnVenue venue)
        {
            try
            {
                string folder = Path.Combine(MediaRoot, venue.Id);
                if (!Directory.Exists(folder))
                {
                    return null;
                }

                var files = new List<string>();
                foreach (string file in Directory.GetFiles(folder))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(_imageTypes, extension) >= 0 || Array.IndexOf(_videoTypes, extension) >= 0)
                    {
                        files.Add(file);
                    }
                }

                while (files.Count > 0)
                {
                    int index = _random.Next(files.Count);
                    string file = files[index];
                    files.RemoveAt(index);

                    if (Array.IndexOf(_videoTypes, Path.GetExtension(file).ToLowerInvariant()) >= 0)
                    {
                        return new BackgroundResult(BackgroundType.Video, File.OpenRead(file));
                    }

                    var image = YARGImage.Load(file);
                    if (image != null)
                    {
                        return new BackgroundResult(image);
                    }
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Could not load the venue's own picture or video");
            }

            return null;
        }
    }
}
