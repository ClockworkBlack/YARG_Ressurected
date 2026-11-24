// YARG Resurrected - Story Mode. Plain .NET, tested in tests/StoryTests.
//
// The rules of the story:
//   - A gig is a set of songs. Each song gives up to five stars; the set is won when the stars
//     add up to the gig's target. Losing costs nothing: the gig can be played again right away.
//   - Winning a gig opens the next one. A chapter opens when every gig the story needs in the
//     chapters before it is won. Side gigs ("optional") never hold the story up.
//   - Winning earns fans (more for more stars, and more later in the story), which raise the
//     band's rank, and unlocks the gig's liner notes and keepsakes.
//   - Each gig may have a challenge. Meeting it earns a badge and extra fans, nothing more.
using System;
using System.Collections.Generic;

namespace YARG.Story
{
    public enum StoryGigState
    {
        /// <summary>Earlier gigs have to be won first.</summary>
        Locked,

        /// <summary>Can be played, not won yet.</summary>
        Open,

        /// <summary>Won. Can be played again for a better result.</summary>
        Won,
    }

    public enum StoryChapterState
    {
        Locked,
        Open,

        /// <summary>Every gig the story needs here is won.</summary>
        Done,
    }

    /// <summary>What came of one night at a gig.</summary>
    public sealed class StoryGigOutcome
    {
        public StoryGig Gig;
        public List<StorySongResult> Songs = new List<StorySongResult>();

        public int Stars;
        public bool Won;
        public bool ChallengeMet;

        // Filled in by StoryEngine.Apply
        public bool FirstWin;
        public bool NewBest;
        public bool ChallengeNew;
        public long FansGained;
        public StoryRank RankBefore;
        public StoryRank RankAfter;
        public List<StoryLinerNote> NewLinerNotes = new List<StoryLinerNote>();
        public List<StoryKeepsake> NewKeepsakes = new List<StoryKeepsake>();
        public List<StoryGig> NewGigs = new List<StoryGig>();

        /// <summary>The gig's chapter, when this win finished it.</summary>
        public StoryChapter ChapterFinished;

        /// <summary>This win finished the whole story.</summary>
        public bool StoryFinished;

        public bool RankUp => RankAfter != null && RankAfter != RankBefore;
    }

    public sealed class StoryEngine
    {
        // Fans for one night: stars and gold stars count, a win counts, and it all counts for more
        // the further into the story the gig is.
        public const int FANS_PER_STAR = 100;
        public const int FANS_PER_GOLD = 50;
        public const int FANS_FOR_WIN = 250;
        public const int FANS_FOR_CHALLENGE = 500;

        /// <summary>Playing a gig again without beating the best night still brings a few new faces.</summary>
        public const double REPLAY_SHARE = 0.05;

        public StoryCampaign Campaign { get; }
        public StoryProgress Progress { get; }

        public StoryEngine(StoryCampaign campaign, StoryProgress progress)
        {
            Campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        /// <summary>A fresh save for a campaign.</summary>
        public static StoryProgress NewProgress(StoryCampaign campaign, string bandName, DateTime nowUtc)
        {
            string name = (bandName ?? "").Trim();
            if (name.Length == 0)
            {
                name = campaign.BandNames.Count > 0 ? campaign.BandNames[0] : "The Band";
            }

            string now = Stamp(nowUtc);
            return new StoryProgress
            {
                CampaignId = campaign.Id,
                BandName = name,
                StartedOn = now,
                PlayedOn = now,
            };
        }

        public static string Stamp(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------------------------
        // What is open
        // ------------------------------------------------------------------------------------

        public StoryChapterState StateOf(StoryChapter chapter)
        {
            foreach (var earlier in Campaign.Chapters)
            {
                if (earlier == chapter)
                {
                    break;
                }

                if (!AllNeededGigsWon(earlier))
                {
                    return StoryChapterState.Locked;
                }
            }

            return AllNeededGigsWon(chapter) ? StoryChapterState.Done : StoryChapterState.Open;
        }

        private bool AllNeededGigsWon(StoryChapter chapter)
        {
            foreach (var gig in chapter.Gigs)
            {
                if (!gig.Optional && !Progress.IsWon(gig.Id))
                {
                    return false;
                }
            }

            return true;
        }

        public StoryGigState StateOf(StoryGig gig)
        {
            if (Progress.IsWon(gig.Id))
            {
                return StoryGigState.Won;
            }

            if (gig.Chapter == null || StateOf(gig.Chapter) == StoryChapterState.Locked)
            {
                return StoryGigState.Locked;
            }

            if (gig.Requires.Count > 0)
            {
                foreach (string id in gig.Requires)
                {
                    // A requirement the script no longer has cannot hold anything up
                    if (Campaign.FindGig(id) != null && !Progress.IsWon(id))
                    {
                        return StoryGigState.Locked;
                    }
                }

                return StoryGigState.Open;
            }

            // No list of its own: every needed gig before it in the chapter comes first
            for (int i = 0; i < gig.IndexInChapter; i++)
            {
                var earlier = gig.Chapter.Gigs[i];
                if (!earlier.Optional && !Progress.IsWon(earlier.Id))
                {
                    return StoryGigState.Locked;
                }
            }

            return StoryGigState.Open;
        }

        /// <summary>What has to be won before this gig opens, for the "locked" message.</summary>
        public List<StoryGig> MissingFor(StoryGig gig)
        {
            var missing = new List<StoryGig>();
            if (gig.Chapter == null)
            {
                return missing;
            }

            foreach (var chapter in Campaign.Chapters)
            {
                if (chapter == gig.Chapter)
                {
                    break;
                }

                foreach (var earlier in chapter.Gigs)
                {
                    if (!earlier.Optional && !Progress.IsWon(earlier.Id))
                    {
                        missing.Add(earlier);
                    }
                }
            }

            if (gig.Requires.Count > 0)
            {
                foreach (string id in gig.Requires)
                {
                    var needed = Campaign.FindGig(id);
                    if (needed != null && !Progress.IsWon(id) && !missing.Contains(needed))
                    {
                        missing.Add(needed);
                    }
                }
            }
            else
            {
                for (int i = 0; i < gig.IndexInChapter; i++)
                {
                    var earlier = gig.Chapter.Gigs[i];
                    if (!earlier.Optional && !Progress.IsWon(earlier.Id))
                    {
                        missing.Add(earlier);
                    }
                }
            }

            return missing;
        }

        /// <summary>
        /// The gig the story is waiting on: the first open gig it needs, or failing that the first
        /// open side gig. Null when everything is won.
        /// </summary>
        public StoryGig NextGig()
        {
            StoryGig side = null;
            foreach (var gig in Campaign.AllGigs)
            {
                if (StateOf(gig) != StoryGigState.Open)
                {
                    continue;
                }

                if (!gig.Optional)
                {
                    return gig;
                }

                side ??= gig;
            }

            return side;
        }

        /// <summary>The chapter the story is in: the one with the next gig, or the last one.</summary>
        public StoryChapter CurrentChapter()
        {
            var next = NextGig();
            if (next != null)
            {
                return next.Chapter;
            }

            return Campaign.Chapters.Count > 0 ? Campaign.Chapters[Campaign.Chapters.Count - 1] : null;
        }

        /// <summary>True once every gig the story needs is won.</summary>
        public bool IsStoryFinished()
        {
            if (Campaign.Chapters.Count == 0)
            {
                return false;
            }

            foreach (var chapter in Campaign.Chapters)
            {
                // A bonus chapter comes after the ending
                if (!chapter.Bonus && !AllNeededGigsWon(chapter))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------------------------
        // Numbers for the screens
        // ------------------------------------------------------------------------------------

        public int GigsWon()
        {
            int won = 0;
            foreach (var gig in Campaign.AllGigs)
            {
                if (Progress.IsWon(gig.Id))
                {
                    won++;
                }
            }

            return won;
        }

        public int StarsEarned()
        {
            int stars = 0;
            foreach (var gig in Campaign.AllGigs)
            {
                var record = Progress.RecordOf(gig.Id);
                if (record != null)
                {
                    stars += Math.Min(record.BestStars, gig.MaxStars);
                }
            }

            return stars;
        }

        public int StarsPossible()
        {
            int stars = 0;
            foreach (var gig in Campaign.AllGigs)
            {
                stars += gig.MaxStars;
            }

            return stars;
        }

        public int StarsEarned(StoryChapter chapter)
        {
            int stars = 0;
            foreach (var gig in chapter.Gigs)
            {
                var record = Progress.RecordOf(gig.Id);
                if (record != null)
                {
                    stars += Math.Min(record.BestStars, gig.MaxStars);
                }
            }

            return stars;
        }

        public int StarsPossible(StoryChapter chapter)
        {
            int stars = 0;
            foreach (var gig in chapter.Gigs)
            {
                stars += gig.MaxStars;
            }

            return stars;
        }

        public int ChallengesMet()
        {
            int met = 0;
            foreach (var gig in Campaign.AllGigs)
            {
                var record = Progress.RecordOf(gig.Id);
                if (gig.Challenge != null && record != null && record.ChallengeMet)
                {
                    met++;
                }
            }

            return met;
        }

        public int ChallengesPossible()
        {
            int count = 0;
            foreach (var gig in Campaign.AllGigs)
            {
                if (gig.Challenge != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>How much of everything there is to do is done, 0 to 100.</summary>
        public int PercentComplete()
        {
            int total = Campaign.AllGigs.Count * 2 + ChallengesPossible() + Campaign.LinerNotes.Count;
            if (total == 0)
            {
                return 0;
            }

            int done = GigsWon() * 2 + ChallengesMet();
            foreach (var note in Campaign.LinerNotes)
            {
                if (Progress.LinerNotes.Contains(note.Id))
                {
                    done++;
                }
            }

            return (int) Math.Floor(100.0 * done / total);
        }

        public StoryRank RankFor(long fans)
        {
            StoryRank best = null;
            foreach (var rank in Campaign.Ranks)
            {
                if (fans >= rank.Fans && (best == null || rank.Fans >= best.Fans))
                {
                    best = rank;
                }
            }

            return best;
        }

        public StoryRank CurrentRank() => RankFor(Progress.Fans);

        /// <summary>The rank after the current one, or null at the top.</summary>
        public StoryRank NextRank()
        {
            StoryRank next = null;
            foreach (var rank in Campaign.Ranks)
            {
                if (rank.Fans > Progress.Fans && (next == null || rank.Fans < next.Fans))
                {
                    next = rank;
                }
            }

            return next;
        }

        // ------------------------------------------------------------------------------------
        // Playing a gig
        // ------------------------------------------------------------------------------------

        /// <summary>Marks a gig as under way with the chosen set. Any earlier unfinished gig is dropped.</summary>
        public StoryPendingGig Begin(StoryGig gig, IReadOnlyList<StorySong> set)
        {
            var pending = new StoryPendingGig { GigId = gig.Id };
            foreach (var song in set)
            {
                pending.SongIds.Add(song.Id ?? "");
            }

            Progress.Pending = pending;
            return pending;
        }

        /// <summary>Stars for a set, and whether it wins the gig and meets its challenge. Changes nothing.</summary>
        public StoryGigOutcome Evaluate(StoryGig gig, IReadOnlyList<StorySongResult> songs)
        {
            var outcome = new StoryGigOutcome { Gig = gig };
            if (songs != null)
            {
                outcome.Songs.AddRange(songs);
            }

            foreach (var song in outcome.Songs)
            {
                outcome.Stars += Math.Max(0, Math.Min(5, song.Stars));
            }

            // A set cut short cannot win, however good the songs that were played
            bool wholeSet = outcome.Songs.Count >= gig.Songs;
            outcome.Won = wholeSet && outcome.Stars >= gig.StarsToPass;
            outcome.ChallengeMet = wholeSet && outcome.Won && MeetsChallenge(gig.Challenge, outcome);
            return outcome;
        }

        private static bool MeetsChallenge(StoryChallenge challenge, StoryGigOutcome outcome)
        {
            if (challenge == null || outcome.Songs.Count == 0)
            {
                return false;
            }

            var songs = outcome.Songs;
            switch (challenge.Type)
            {
                case StoryChallenge.STARS:
                    return outcome.Stars >= challenge.Value;

                case StoryChallenge.ALL_FIVE:
                    foreach (var song in songs)
                    {
                        if (song.Stars < 5) return false;
                    }
                    return true;

                case StoryChallenge.GOLD:
                {
                    int gold = 0;
                    foreach (var song in songs)
                    {
                        if (song.Gold) gold++;
                    }
                    return gold >= Math.Max(1, challenge.Value);
                }

                case StoryChallenge.FULL_COMBO:
                {
                    int combos = 0;
                    foreach (var song in songs)
                    {
                        if (song.FullCombo) combos++;
                    }
                    return combos >= Math.Max(1, challenge.Value);
                }

                case StoryChallenge.ACCURACY:
                    foreach (var song in songs)
                    {
                        if (song.Percent < challenge.Value) return false;
                    }
                    return true;

                case StoryChallenge.STREAK:
                    foreach (var song in songs)
                    {
                        if (song.BestStreak >= challenge.Value) return true;
                    }
                    return false;

                case StoryChallenge.BAND:
                    foreach (var song in songs)
                    {
                        if (song.Players < challenge.Value) return false;
                    }
                    return true;

                case StoryChallenge.DIFFICULTY:
                    foreach (var song in songs)
                    {
                        if (song.LowestDifficulty < challenge.Value) return false;
                    }
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>A plain description of a challenge, for when the script gives none.</summary>
        public static string Describe(StoryChallenge challenge)
        {
            if (challenge == null)
            {
                return "";
            }

            switch (challenge.Type)
            {
                case StoryChallenge.STARS: return $"Earn {challenge.Value} stars over the set.";
                case StoryChallenge.ALL_FIVE: return "Earn five stars on every song.";
                case StoryChallenge.GOLD:
                    return challenge.Value > 1 ? $"Earn gold stars on {challenge.Value} songs." : "Earn gold stars on a song.";
                case StoryChallenge.FULL_COMBO:
                    return challenge.Value > 1
                        ? $"Play {challenge.Value} songs without missing a note."
                        : "Play a song without missing a note.";
                case StoryChallenge.ACCURACY: return $"Hit {challenge.Value}% of the notes on every song.";
                case StoryChallenge.STREAK: return $"Reach a streak of {challenge.Value} notes.";
                case StoryChallenge.BAND: return $"Play the set with {challenge.Value} or more people.";
                case StoryChallenge.DIFFICULTY:
                    return $"Everyone plays on {DifficultyName(challenge.Value)} or harder.";
                default: return "";
            }
        }

        public static string DifficultyName(int level)
        {
            switch (level)
            {
                case 1: return "Easy";
                case 2: return "Medium";
                case 3: return "Hard";
                case 4: return "Expert";
                default: return "any difficulty";
            }
        }

        /// <summary>Fans one night brings in, before comparing it with earlier nights at the same gig.</summary>
        public static long FansFor(StoryGigOutcome outcome)
        {
            int chapter = outcome.Gig.Chapter != null ? Math.Max(1, outcome.Gig.Chapter.Number) : 1;
            long fans = (long) outcome.Stars * FANS_PER_STAR;
            foreach (var song in outcome.Songs)
            {
                if (song.Gold)
                {
                    fans += FANS_PER_GOLD;
                }
            }

            if (outcome.Won)
            {
                fans += FANS_FOR_WIN;
            }

            return fans * chapter;
        }

        /// <summary>
        /// Writes a night's result into the save and works out everything it unlocked. Call once
        /// per finished set, with the outcome from <see cref="Evaluate"/>.
        /// </summary>
        public StoryGigOutcome Apply(StoryGigOutcome outcome, DateTime nowUtc)
        {
            var gig = outcome.Gig;
            var chapterBefore = gig.Chapter != null ? StateOf(gig.Chapter) : StoryChapterState.Open;
            bool finishedBefore = IsStoryFinished();

            var openBefore = new HashSet<StoryGig>();
            foreach (var other in Campaign.AllGigs)
            {
                if (StateOf(other) != StoryGigState.Locked)
                {
                    openBefore.Add(other);
                }
            }

            if (!Progress.Gigs.TryGetValue(gig.Id, out var record))
            {
                record = new StoryGigRecord { GigId = gig.Id };
                Progress.Gigs[gig.Id] = record;
            }

            outcome.RankBefore = CurrentRank();
            record.Plays++;

            // Fans: the best night counts in full, any other night a little
            long night = FansFor(outcome);
            long gained = night > record.BestFans ? night - record.BestFans : 0;
            gained += (long) Math.Floor(night * REPLAY_SHARE);
            record.BestFans = Math.Max(record.BestFans, night);

            outcome.NewBest = outcome.Stars > record.BestStars;
            if (outcome.NewBest || record.BestSet.Count == 0)
            {
                record.BestStars = Math.Max(record.BestStars, outcome.Stars);
                record.BestSet = new List<StorySongResult>(outcome.Songs);
            }

            if (outcome.Won && !record.Won)
            {
                outcome.FirstWin = true;
                record.Won = true;
                record.WonOn = Stamp(nowUtc);

                foreach (string id in gig.Rewards)
                {
                    var note = Campaign.FindLinerNote(id);
                    if (note != null && Progress.LinerNotes.Add(id))
                    {
                        outcome.NewLinerNotes.Add(note);
                    }

                    var keepsake = Campaign.FindKeepsake(id);
                    if (keepsake != null && Progress.Keepsakes.Add(id))
                    {
                        outcome.NewKeepsakes.Add(keepsake);
                    }
                }
            }

            if (outcome.ChallengeMet && !record.ChallengeMet)
            {
                outcome.ChallengeNew = true;
                record.ChallengeMet = true;
                int chapter = gig.Chapter != null ? Math.Max(1, gig.Chapter.Number) : 1;
                gained += (long) FANS_FOR_CHALLENGE * chapter;
            }

            outcome.FansGained = gained;
            Progress.Fans += gained;
            outcome.RankAfter = CurrentRank();

            // Remember the songs, so the next suggestions differ
            Progress.LastSet.Clear();
            foreach (var song in outcome.Songs)
            {
                if (string.IsNullOrEmpty(song.SongId))
                {
                    continue;
                }

                Progress.LastSet.Add(song.SongId);
                Progress.SongPlays.TryGetValue(song.SongId, out int plays);
                Progress.SongPlays[song.SongId] = plays + 1;
            }

            if (Progress.Pending != null && Progress.Pending.GigId == gig.Id)
            {
                Progress.Pending = null;
            }

            Progress.PlayedOn = Stamp(nowUtc);

            foreach (var other in Campaign.AllGigs)
            {
                if (!openBefore.Contains(other) && StateOf(other) != StoryGigState.Locked)
                {
                    outcome.NewGigs.Add(other);
                }
            }

            if (gig.Chapter != null && chapterBefore != StoryChapterState.Done &&
                StateOf(gig.Chapter) == StoryChapterState.Done)
            {
                outcome.ChapterFinished = gig.Chapter;
            }

            outcome.StoryFinished = !finishedBefore && IsStoryFinished();
            return outcome;
        }

        // ------------------------------------------------------------------------------------
        // Text
        // ------------------------------------------------------------------------------------

        /// <summary>Puts the band's name into a line of the script: "{band}" becomes the name.</summary>
        public string Fill(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
            {
                return text ?? "";
            }

            return text.Replace("{band}", Progress.BandName ?? "");
        }
    }
}
