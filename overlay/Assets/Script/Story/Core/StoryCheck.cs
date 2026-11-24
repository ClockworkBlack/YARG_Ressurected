// YARG Resurrected - Story Mode. Plain .NET, tested in tests/StoryTests.
//
// Looks a campaign over for mistakes in the script: a line spoken by nobody, a gig at a venue
// that does not exist, a reward that is never defined, a gig that can never open, and so on.
// The automated tests run this on the real story files, so a slip in the writing is caught
// before it reaches a phone. The game runs it too and writes what it finds to the log.
using System;
using System.Collections.Generic;

namespace YARG.Story
{
    public static class StoryCheck
    {
        /// <summary>Every problem found, in plain words. Empty means the campaign is sound.</summary>
        public static List<string> Problems(StoryCampaign campaign)
        {
            var problems = new List<string>();
            if (campaign == null)
            {
                problems.Add("There is no campaign");
                return problems;
            }

            if (string.IsNullOrWhiteSpace(campaign.Id)) problems.Add("The campaign has no id");
            if (string.IsNullOrWhiteSpace(campaign.Title)) problems.Add("The campaign has no title");
            if (campaign.BandNames.Count == 0) problems.Add("There are no band names to choose from");
            if (campaign.Chapters.Count == 0) problems.Add("There are no chapters");
            if (campaign.Ranks.Count == 0) problems.Add("There are no ranks");

            CheckIds("character", campaign.Characters.ConvertAll(c => c.Id), problems);
            CheckIds("venue", campaign.Venues.ConvertAll(v => v.Id), problems);
            CheckIds("chapter", campaign.Chapters.ConvertAll(c => c.Id), problems);
            CheckIds("liner note", campaign.LinerNotes.ConvertAll(n => n.Id), problems);
            CheckIds("keepsake", campaign.Keepsakes.ConvertAll(k => k.Id), problems);
            CheckIds("gig", new List<StoryGig>(campaign.AllGigs).ConvertAll(g => g.Id), problems);

            foreach (string reserved in new[] { StoryLine.NARRATOR, StoryLine.YOU, StoryLine.TITLE })
            {
                if (campaign.FindCharacter(reserved) != null)
                {
                    problems.Add($"A character may not use the id \"{reserved}\"");
                }
            }

            // A reward id must mean exactly one thing
            foreach (var note in campaign.LinerNotes)
            {
                if (campaign.FindKeepsake(note.Id) != null)
                {
                    problems.Add($"\"{note.Id}\" is both a liner note and a keepsake");
                }
            }

            foreach (var character in campaign.Characters)
            {
                string who = $"Character \"{character.Id}\"";
                if (string.IsNullOrWhiteSpace(character.Name)) problems.Add($"{who} has no name");
                CheckOneOf(character.Portrait.Hair, StoryPortrait.Hairs, $"{who}: hair", problems);
                CheckOneOf(character.Portrait.Accessory, StoryPortrait.Accessories, $"{who}: accessory", problems);
                CheckColor(character.Portrait.Skin, $"{who}: skin", problems);
                CheckColor(character.Portrait.HairColor, $"{who}: hairColor", problems);
                CheckColor(character.Portrait.Outfit, $"{who}: outfit", problems);
                CheckColor(character.Portrait.Accent, $"{who}: accent", problems);
            }

            foreach (var venue in campaign.Venues)
            {
                string where = $"Venue \"{venue.Id}\"";
                if (string.IsNullOrWhiteSpace(venue.Name)) problems.Add($"{where} has no name");
                if (string.IsNullOrWhiteSpace(venue.City)) problems.Add($"{where} has no city");
                CheckOneOf(venue.Style, StoryVenue.Styles, $"{where}: style", problems);
                CheckColor(venue.Top, $"{where}: top", problems);
                CheckColor(venue.Bottom, $"{where}: bottom", problems);
                CheckColor(venue.Accent, $"{where}: accent", problems);
                CheckColor(venue.Accent2, $"{where}: accent2", problems);
                if (venue.MapX < 0 || venue.MapX > 1 || venue.MapY < 0 || venue.MapY > 1)
                {
                    problems.Add($"{where}: the map position must be between 0 and 1");
                }
            }

            long lastFans = -1;
            for (int i = 0; i < campaign.Ranks.Count; i++)
            {
                var rank = campaign.Ranks[i];
                if (string.IsNullOrWhiteSpace(rank.Name)) problems.Add($"Rank {i + 1} has no name");
                if (rank.Fans == lastFans) problems.Add($"Two ranks start at {rank.Fans} fans");
                lastFans = rank.Fans;
            }

            if (campaign.Ranks.Count > 0 && campaign.Ranks[0].Fans != 0)
            {
                problems.Add("The first rank must start at 0 fans");
            }

            CheckScene(campaign, campaign.Opening, "The opening", problems, required: true);
            CheckScene(campaign, campaign.Ending, "The ending", problems, required: true);

            var rewarded = new HashSet<string>();
            foreach (var chapter in campaign.Chapters)
            {
                string chapterName = $"Chapter \"{chapter.Id}\"";
                if (string.IsNullOrWhiteSpace(chapter.Title)) problems.Add($"{chapterName} has no title");
                if (chapter.Gigs.Count == 0) problems.Add($"{chapterName} has no gigs");

                bool anyNeeded = false;
                foreach (var gig in chapter.Gigs)
                {
                    anyNeeded |= !gig.Optional;
                }

                if (chapter.Gigs.Count > 0 && !anyNeeded)
                {
                    problems.Add($"{chapterName} has only side gigs, so it would be finished before it starts");
                }

                CheckScene(campaign, chapter.Intro, $"{chapterName}: intro", problems, required: true);
                CheckScene(campaign, chapter.Outro, $"{chapterName}: outro", problems, required: false);

                foreach (var gig in chapter.Gigs)
                {
                    string gigName = $"Gig \"{gig.Id}\"";
                    if (string.IsNullOrWhiteSpace(gig.Title)) problems.Add($"{gigName} has no title");
                    if (string.IsNullOrWhiteSpace(gig.Blurb)) problems.Add($"{gigName} has no blurb");
                    if (campaign.FindVenue(gig.VenueId) == null)
                    {
                        problems.Add($"{gigName} is at an unknown venue \"{gig.VenueId}\"");
                    }

                    if (gig.Songs < 1 || gig.Songs > StoryGig.MAX_SONGS)
                    {
                        problems.Add($"{gigName}: a set has 1 to {StoryGig.MAX_SONGS} songs, not {gig.Songs}");
                    }
                    else if (gig.StarsToPass < 1 || gig.StarsToPass > gig.MaxStars)
                    {
                        problems.Add($"{gigName}: the star target {gig.StarsToPass} must be between 1 and {gig.MaxStars}");
                    }

                    if (string.IsNullOrWhiteSpace(gig.Vibe.Label)) problems.Add($"{gigName}: the vibe has no label");
                    if (gig.Vibe.Genres.Count == 0) problems.Add($"{gigName}: the vibe names no genres");
                    foreach (string genre in gig.Vibe.Genres)
                    {
                        if (!StoryGenres.IsKnown(genre))
                        {
                            problems.Add($"{gigName}: unknown genre family \"{genre}\"");
                        }
                    }

                    if (gig.Vibe.YearFrom > 0 && gig.Vibe.YearTo > 0 && gig.Vibe.YearFrom > gig.Vibe.YearTo)
                    {
                        problems.Add($"{gigName}: the vibe's years run backwards");
                    }

                    CheckChallenge(gig, gigName, problems);

                    foreach (string id in gig.Requires)
                    {
                        var needed = campaign.FindGig(id);
                        if (needed == null)
                        {
                            problems.Add($"{gigName} requires an unknown gig \"{id}\"");
                        }
                        else if (needed.Number >= gig.Number)
                        {
                            problems.Add($"{gigName} requires \"{id}\", which comes after it");
                        }
                    }

                    foreach (string id in gig.Rewards)
                    {
                        if (campaign.FindLinerNote(id) == null && campaign.FindKeepsake(id) == null)
                        {
                            problems.Add($"{gigName} rewards \"{id}\", which is neither a liner note nor a keepsake");
                        }

                        if (!rewarded.Add(id))
                        {
                            problems.Add($"\"{id}\" is the reward of more than one gig");
                        }
                    }

                    CheckScene(campaign, gig.Before, $"{gigName}: before", problems, required: true);
                    CheckScene(campaign, gig.AfterWin, $"{gigName}: afterWin", problems, required: true);
                    CheckScene(campaign, gig.AfterLose, $"{gigName}: afterLose", problems, required: true);
                    CheckScene(campaign, gig.AfterChallenge, $"{gigName}: afterChallenge", problems, required: false);
                }
            }

            foreach (var note in campaign.LinerNotes)
            {
                if (string.IsNullOrWhiteSpace(note.Title)) problems.Add($"Liner note \"{note.Id}\" has no title");
                if (string.IsNullOrWhiteSpace(note.Body)) problems.Add($"Liner note \"{note.Id}\" has no text");
                if (!rewarded.Contains(note.Id)) problems.Add($"Liner note \"{note.Id}\" is never unlocked by a gig");
            }

            foreach (var keepsake in campaign.Keepsakes)
            {
                if (string.IsNullOrWhiteSpace(keepsake.Name)) problems.Add($"Keepsake \"{keepsake.Id}\" has no name");
                if (!rewarded.Contains(keepsake.Id)) problems.Add($"Keepsake \"{keepsake.Id}\" is never earned at a gig");
            }

            CheckReachable(campaign, problems);
            return problems;
        }

        private static void CheckIds(string kind, List<string> ids, List<string> problems)
        {
            var seen = new HashSet<string>();
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    problems.Add($"A {kind} has no id");
                }
                else if (!seen.Add(id))
                {
                    problems.Add($"The {kind} id \"{id}\" is used twice");
                }
            }
        }

        private static void CheckOneOf(string value, string[] allowed, string what, List<string> problems)
        {
            if (Array.IndexOf(allowed, value) < 0)
            {
                problems.Add($"{what} \"{value}\" is not one of: {string.Join(", ", allowed)}");
            }
        }

        public static bool IsColor(string text)
        {
            if (text == null || text.Length != 7 || text[0] != '#')
            {
                return false;
            }

            for (int i = 1; i < 7; i++)
            {
                char c = text[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static void CheckColor(string value, string what, List<string> problems)
        {
            if (!IsColor(value))
            {
                problems.Add($"{what} \"{value}\" is not a color like #A1B2C3");
            }
        }

        private static void CheckChallenge(StoryGig gig, string gigName, List<string> problems)
        {
            var challenge = gig.Challenge;
            if (challenge == null)
            {
                if (gig.AfterChallenge.Count > 0)
                {
                    problems.Add($"{gigName} has a challenge scene but no challenge");
                }

                return;
            }

            if (Array.IndexOf(StoryChallenge.Types, challenge.Type) < 0)
            {
                problems.Add($"{gigName}: unknown challenge type \"{challenge.Type}\"");
                return;
            }

            if (string.IsNullOrWhiteSpace(challenge.Text)) problems.Add($"{gigName}: the challenge has no text");
            if (string.IsNullOrWhiteSpace(challenge.Badge)) problems.Add($"{gigName}: the challenge has no badge name");

            int songs = Math.Max(1, gig.Songs);
            switch (challenge.Type)
            {
                case StoryChallenge.STARS:
                    if (challenge.Value <= gig.StarsToPass || challenge.Value > songs * 5)
                    {
                        problems.Add($"{gigName}: a stars challenge must ask for more than the target ({gig.StarsToPass}) and at most {songs * 5}");
                    }
                    break;
                case StoryChallenge.GOLD:
                case StoryChallenge.FULL_COMBO:
                    if (challenge.Value < 1 || challenge.Value > songs)
                    {
                        problems.Add($"{gigName}: the challenge must ask for 1 to {songs} songs");
                    }
                    break;
                case StoryChallenge.ACCURACY:
                    if (challenge.Value < 50 || challenge.Value > 100)
                    {
                        problems.Add($"{gigName}: an accuracy challenge must be 50 to 100 percent");
                    }
                    break;
                case StoryChallenge.STREAK:
                    if (challenge.Value < 10 || challenge.Value > 2000)
                    {
                        problems.Add($"{gigName}: a streak challenge must be 10 to 2000 notes");
                    }
                    break;
                case StoryChallenge.BAND:
                    if (challenge.Value < 2 || challenge.Value > 4)
                    {
                        problems.Add($"{gigName}: a band challenge must ask for 2 to 4 people");
                    }
                    break;
                case StoryChallenge.DIFFICULTY:
                    if (challenge.Value < 2 || challenge.Value > 4)
                    {
                        problems.Add($"{gigName}: a difficulty challenge must be 2 (medium) to 4 (expert)");
                    }
                    break;
            }
        }

        private static void CheckScene(StoryCampaign campaign, List<StoryLine> lines, string where,
            List<string> problems, bool required)
        {
            if (lines.Count == 0)
            {
                if (required)
                {
                    problems.Add($"{where} has no lines");
                }

                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                string at = $"{where}, line {i + 1}";

                if (string.IsNullOrWhiteSpace(line.Text))
                {
                    problems.Add($"{at} has no text");
                }
                else
                {
                    CheckText(line.Text, at, problems);
                }

                if (line.Speaker != StoryLine.NARRATOR && line.Speaker != StoryLine.YOU &&
                    line.Speaker != StoryLine.TITLE && campaign.FindCharacter(line.Speaker) == null)
                {
                    problems.Add($"{at} is spoken by an unknown character \"{line.Speaker}\"");
                }

                if (line.Sub.Length > 0)
                {
                    if (line.Speaker != StoryLine.TITLE)
                    {
                        problems.Add($"{at} has a subtitle but is not a title card");
                    }

                    CheckText(line.Sub, at, problems);
                }

                if (line.Mood.Length > 0 && Array.IndexOf(StoryMoods.All, line.Mood) < 0)
                {
                    problems.Add($"{at} has an unknown mood \"{line.Mood}\"");
                }

                if (line.Backdrop.Length > 0 && Array.IndexOf(StoryBackdrops.Names, line.Backdrop) < 0 &&
                    campaign.FindVenue(line.Backdrop) == null)
                {
                    problems.Add($"{at} has an unknown backdrop \"{line.Backdrop}\"");
                }
            }
        }

        // The text is shown with rich text switched on, so a stray tag would swallow words, and
        // the only thing in braces the game fills in is {band}
        private static void CheckText(string text, string at, List<string> problems)
        {
            if (text.IndexOf('<') >= 0 || text.IndexOf('>') >= 0)
            {
                problems.Add($"{at} contains '<' or '>', which the text display would read as formatting");
            }

            int open = text.IndexOf('{');
            while (open >= 0)
            {
                int close = text.IndexOf('}', open);
                string token = close > open ? text.Substring(open, close - open + 1) : text.Substring(open);
                if (token != "{band}")
                {
                    problems.Add($"{at} contains \"{token}\"; the only fill-in is {{band}}");
                }

                open = close > open ? text.IndexOf('{', close) : -1;
            }

            if (text.Length > 420)
            {
                problems.Add($"{at} is {text.Length} characters long; split it so it fits the dialogue box (420 at most)");
            }
        }

        // Plays the story the way a player would, winning every open gig it needs, and checks that
        // this reaches the end and that every side gig opens along the way.
        private static void CheckReachable(StoryCampaign campaign, List<string> problems)
        {
            if (campaign.AllGigs.Count == 0)
            {
                return;
            }

            var progress = new StoryProgress { CampaignId = campaign.Id };
            var engine = new StoryEngine(campaign, progress);
            var opened = new HashSet<StoryGig>();

            int guard = campaign.AllGigs.Count + 2;
            while (guard-- > 0)
            {
                foreach (var gig in campaign.AllGigs)
                {
                    if (engine.StateOf(gig) != StoryGigState.Locked)
                    {
                        opened.Add(gig);
                    }
                }

                StoryGig next = null;
                foreach (var gig in campaign.AllGigs)
                {
                    if (!gig.Optional && engine.StateOf(gig) == StoryGigState.Open)
                    {
                        next = gig;
                        break;
                    }
                }

                if (next == null)
                {
                    break;
                }

                progress.Gigs[next.Id] = new StoryGigRecord { GigId = next.Id, Won = true };
            }

            foreach (var gig in campaign.AllGigs)
            {
                if (engine.StateOf(gig) != StoryGigState.Locked)
                {
                    opened.Add(gig);
                }
            }

            if (!engine.IsStoryFinished())
            {
                problems.Add("The story cannot be finished: winning every open gig does not reach the end");
            }

            foreach (var gig in campaign.AllGigs)
            {
                if (!opened.Contains(gig))
                {
                    problems.Add($"Gig \"{gig.Id}\" can never be opened");
                }
            }
        }
    }
}
