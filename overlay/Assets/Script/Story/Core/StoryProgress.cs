// YARG Resurrected - Story Mode. Plain .NET, tested in tests/StoryTests.
//
// What a save file remembers: the band, which gigs are won and how well, which scenes have been
// seen, what has been unlocked, and a gig that is being played right now (so the story can pick
// up again if the app is closed between two songs of a set).
//
// Gigs, notes and scenes are remembered by id, so a save keeps working when the script is edited:
// ids the script no longer has are simply ignored, and new ones start out fresh.
using System;
using System.Collections.Generic;

namespace YARG.Story
{
    /// <summary>How one song of a set went.</summary>
    public sealed class StorySongResult
    {
        public string SongId = "";
        public string SongName = "";
        public string SongArtist = "";

        /// <summary>0 to 5.</summary>
        public int Stars;

        /// <summary>Gold stars (still counted as 5).</summary>
        public bool Gold;

        /// <summary>Percent of notes hit, 0 to 100, averaged over the people playing.</summary>
        public double Percent;

        /// <summary>The longest streak anyone reached.</summary>
        public int BestStreak;

        /// <summary>Nobody missed a note.</summary>
        public bool FullCombo;

        public long Score;

        /// <summary>How many people played (computer-controlled players are not counted).</summary>
        public int Players;

        /// <summary>The easiest difficulty anyone played on: 1 easy, 2 medium, 3 hard, 4 expert. 0 when unknown.</summary>
        public int LowestDifficulty;

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                ["songId"] = SongId ?? "",
                ["name"] = SongName ?? "",
                ["artist"] = SongArtist ?? "",
                ["stars"] = Stars,
                ["gold"] = Gold,
                ["percent"] = Math.Round(Percent, 2),
                ["streak"] = BestStreak,
                ["fullCombo"] = FullCombo,
                ["score"] = Score,
                ["players"] = Players,
                ["difficulty"] = LowestDifficulty,
            };
        }

        public static StorySongResult FromJson(Dictionary<string, object> json)
        {
            return new StorySongResult
            {
                SongId = StoryJson.Str(json, "songId"),
                SongName = StoryJson.Str(json, "name"),
                SongArtist = StoryJson.Str(json, "artist"),
                Stars = Clamp(StoryJson.Int(json, "stars"), 0, 5),
                Gold = StoryJson.Bool(json, "gold"),
                Percent = Math.Max(0, Math.Min(100, StoryJson.Num(json, "percent"))),
                BestStreak = Math.Max(0, StoryJson.Int(json, "streak")),
                FullCombo = StoryJson.Bool(json, "fullCombo"),
                Score = Math.Max(0, StoryJson.Long(json, "score")),
                Players = Math.Max(0, StoryJson.Int(json, "players")),
                LowestDifficulty = Clamp(StoryJson.Int(json, "difficulty"), 0, 4),
            };
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }

    /// <summary>The best the band has done at one gig.</summary>
    public sealed class StoryGigRecord
    {
        public string GigId = "";
        public bool Won;
        public int BestStars;
        public bool ChallengeMet;

        /// <summary>How many times the set was played to the end.</summary>
        public int Plays;

        /// <summary>The most fans one night here has brought in.</summary>
        public long BestFans;

        /// <summary>The set that earned <see cref="BestStars"/>, for the tour diary.</summary>
        public List<StorySongResult> BestSet = new List<StorySongResult>();

        /// <summary>When it was first won (UTC, ISO 8601), or empty.</summary>
        public string WonOn = "";

        public Dictionary<string, object> ToJson()
        {
            var set = new List<object>();
            foreach (var song in BestSet)
            {
                set.Add(song.ToJson());
            }

            return new Dictionary<string, object>
            {
                ["won"] = Won,
                ["stars"] = BestStars,
                ["challenge"] = ChallengeMet,
                ["plays"] = Plays,
                ["fans"] = BestFans,
                ["wonOn"] = WonOn ?? "",
                ["set"] = set,
            };
        }

        public static StoryGigRecord FromJson(string gigId, Dictionary<string, object> json)
        {
            var record = new StoryGigRecord
            {
                GigId = gigId,
                Won = StoryJson.Bool(json, "won"),
                BestStars = Math.Max(0, StoryJson.Int(json, "stars")),
                ChallengeMet = StoryJson.Bool(json, "challenge"),
                Plays = Math.Max(0, StoryJson.Int(json, "plays")),
                BestFans = Math.Max(0, StoryJson.Long(json, "fans")),
                WonOn = StoryJson.Str(json, "wonOn"),
            };

            foreach (var item in StoryJson.Objects(json, "set"))
            {
                record.BestSet.Add(StorySongResult.FromJson(item));
            }

            return record;
        }
    }

    /// <summary>A gig that has been started and not yet finished.</summary>
    public sealed class StoryPendingGig
    {
        public string GigId = "";

        /// <summary>The songs of the set, in order.</summary>
        public List<string> SongIds = new List<string>();

        /// <summary>Results of the songs played so far.</summary>
        public List<StorySongResult> Results = new List<StorySongResult>();

        public bool IsComplete => Results.Count >= SongIds.Count;

        public Dictionary<string, object> ToJson()
        {
            var results = new List<object>();
            foreach (var result in Results)
            {
                results.Add(result.ToJson());
            }

            return new Dictionary<string, object>
            {
                ["gig"] = GigId ?? "",
                ["songs"] = new List<object>(SongIds),
                ["results"] = results,
            };
        }

        public static StoryPendingGig FromJson(Dictionary<string, object> json)
        {
            if (json == null)
            {
                return null;
            }

            var pending = new StoryPendingGig
            {
                GigId = StoryJson.Str(json, "gig"),
                SongIds = StoryJson.Strings(json, "songs"),
            };

            foreach (var item in StoryJson.Objects(json, "results"))
            {
                pending.Results.Add(StorySongResult.FromJson(item));
            }

            return string.IsNullOrEmpty(pending.GigId) || pending.SongIds.Count == 0 ? null : pending;
        }
    }

    public sealed class StoryProgress
    {
        public const int FORMAT = 1;

        // Scene names used with HasSeen / MarkSeen
        public const string SCENE_OPENING = "opening";
        public const string SCENE_ENDING = "ending";

        public static string ChapterIntroScene(StoryChapter chapter) => "chapter." + chapter.Id + ".intro";
        public static string ChapterOutroScene(StoryChapter chapter) => "chapter." + chapter.Id + ".outro";
        public static string GigBeforeScene(StoryGig gig) => "gig." + gig.Id + ".before";
        public static string GigWinScene(StoryGig gig) => "gig." + gig.Id + ".win";
        public static string GigChallengeScene(StoryGig gig) => "gig." + gig.Id + ".challenge";

        public string CampaignId = "";
        public string BandName = "";
        public long Fans;

        /// <summary>When the story was started and last played (UTC, ISO 8601).</summary>
        public string StartedOn = "";
        public string PlayedOn = "";

        public readonly Dictionary<string, StoryGigRecord> Gigs = new Dictionary<string, StoryGigRecord>();
        public readonly HashSet<string> SeenScenes = new HashSet<string>();
        public readonly HashSet<string> LinerNotes = new HashSet<string>();
        public readonly HashSet<string> ReadLinerNotes = new HashSet<string>();
        public readonly HashSet<string> Keepsakes = new HashSet<string>();

        /// <summary>How many times the story has used each song.</summary>
        public readonly Dictionary<string, int> SongPlays = new Dictionary<string, int>();

        /// <summary>The songs of the last set played, to keep the next suggestions different.</summary>
        public readonly List<string> LastSet = new List<string>();

        public StoryPendingGig Pending;

        /// <summary>
        /// The player's own "Venue" setting, kept while a gig borrows it to pick the stage, so it
        /// can be put back even if the app was closed during the song. Null when nothing is borrowed.
        /// </summary>
        public string HeldVenueChoice;

        public bool HasSeen(string scene) => SeenScenes.Contains(scene);

        public void MarkSeen(string scene) => SeenScenes.Add(scene);

        public StoryGigRecord RecordOf(string gigId)
        {
            return gigId != null && Gigs.TryGetValue(gigId, out var record) ? record : null;
        }

        public bool IsWon(string gigId)
        {
            var record = RecordOf(gigId);
            return record != null && record.Won;
        }

        public string ToJsonText()
        {
            var gigs = new Dictionary<string, object>();
            foreach (var pair in Gigs)
            {
                gigs[pair.Key] = pair.Value.ToJson();
            }

            var plays = new Dictionary<string, object>();
            foreach (var pair in SongPlays)
            {
                plays[pair.Key] = pair.Value;
            }

            var root = new Dictionary<string, object>
            {
                ["format"] = FORMAT,
                ["campaign"] = CampaignId ?? "",
                ["band"] = BandName ?? "",
                ["fans"] = Fans,
                ["startedOn"] = StartedOn ?? "",
                ["playedOn"] = PlayedOn ?? "",
                ["gigs"] = gigs,
                ["seen"] = Sorted(SeenScenes),
                ["linerNotes"] = Sorted(LinerNotes),
                ["readLinerNotes"] = Sorted(ReadLinerNotes),
                ["keepsakes"] = Sorted(Keepsakes),
                ["songPlays"] = plays,
                ["lastSet"] = new List<object>(LastSet),
            };

            if (Pending != null)
            {
                root["pending"] = Pending.ToJson();
            }

            if (HeldVenueChoice != null)
            {
                root["heldVenueChoice"] = HeldVenueChoice;
            }

            return StoryJson.Write(root);
        }

        // Sorted so the file reads the same from one save to the next
        private static List<object> Sorted(HashSet<string> set)
        {
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return new List<object>(list);
        }

        /// <summary>Reads a save. Returns null when the text is not a story save at all.</summary>
        public static StoryProgress FromJsonText(string text)
        {
            var root = StoryJson.TryParseObject(text);
            if (root == null || !root.ContainsKey("format"))
            {
                return null;
            }

            var progress = new StoryProgress
            {
                CampaignId = StoryJson.Str(root, "campaign"),
                BandName = StoryJson.Str(root, "band"),
                Fans = Math.Max(0, StoryJson.Long(root, "fans")),
                StartedOn = StoryJson.Str(root, "startedOn"),
                PlayedOn = StoryJson.Str(root, "playedOn"),
                Pending = StoryPendingGig.FromJson(StoryJson.Obj(root, "pending")),
                HeldVenueChoice = root.TryGetValue("heldVenueChoice", out object held) ? held as string : null,
            };

            var gigs = StoryJson.Obj(root, "gigs");
            if (gigs != null)
            {
                foreach (var pair in gigs)
                {
                    if (pair.Value is Dictionary<string, object> record)
                    {
                        progress.Gigs[pair.Key] = StoryGigRecord.FromJson(pair.Key, record);
                    }
                }
            }

            foreach (string id in StoryJson.Strings(root, "seen")) progress.SeenScenes.Add(id);
            foreach (string id in StoryJson.Strings(root, "linerNotes")) progress.LinerNotes.Add(id);
            foreach (string id in StoryJson.Strings(root, "readLinerNotes")) progress.ReadLinerNotes.Add(id);
            foreach (string id in StoryJson.Strings(root, "keepsakes")) progress.Keepsakes.Add(id);
            progress.LastSet.AddRange(StoryJson.Strings(root, "lastSet"));

            var plays = StoryJson.Obj(root, "songPlays");
            if (plays != null)
            {
                foreach (var pair in plays)
                {
                    if (pair.Value is double count && count > 0)
                    {
                        progress.SongPlays[pair.Key] = (int) Math.Min(count, 100000);
                    }
                }
            }

            return progress;
        }
    }
}
