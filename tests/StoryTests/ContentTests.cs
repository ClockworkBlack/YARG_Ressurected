using System;
using System.Collections.Generic;
using System.IO;
using YARG.Story;

namespace StoryTests
{
    /// <summary>
    /// Checks the real story files, the ones the game ships: that they load, that the script has no
    /// loose ends, that it is as long as promised, and that it can be played from start to finish.
    /// </summary>
    internal static class ContentTests
    {
        // The stages the game's venue catalog draws. The story visits every one of them.
        private static readonly string[] _catalogVenues =
        {
            "harlem-theater", "bronx-block-party", "midtown-garden", "dc-u-street", "philly-uptown", "atlanta-block",
            "new-orleans-blues", "new-orleans-festival", "houston-slab", "memphis-beale", "miami-beach",
            "pontiac-ballroom", "detroit-fox", "chicago-regal", "minneapolis-club", "la-forum", "compton-swap-meet",
            "oakland-fox", "vegas-residency", "concert-hall", "dc-arts-center", "london-stadium", "tokyo-dome",
            "bucharest-open-air", "montreux-lakeside", "johannesburg-festival",
        };

        // Characters the game's font is sure to have. Anything else (curly quotes, long dashes,
        // accented letters) might show up as an empty box on the phone.
        private static bool IsSafe(char c)
        {
            return c == '\n' || (c >= ' ' && c <= '~');
        }

        private static int Words(string text)
        {
            int words = 0;
            bool inWord = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    inWord = false;
                }
                else if (!inWord)
                {
                    inWord = true;
                    words++;
                }
            }

            return words;
        }

        private static int Words(List<StoryLine> lines)
        {
            int words = 0;
            foreach (var line in lines)
            {
                words += Words(line.Text) + Words(line.Sub);
            }

            return words;
        }

        public static void Run(string folder)
        {
            Console.WriteLine("\n== The story files in " + folder + " ==");

            Program.Check(Directory.Exists(folder), "the story folder exists");

            var used = new HashSet<string>();
            var campaign = StoryCampaign.Load(name =>
            {
                string path = Path.Combine(folder, name + ".json");
                if (!File.Exists(path))
                {
                    return null;
                }

                used.Add(Path.GetFileName(path));
                return File.ReadAllText(path);
            });

            Program.Check(campaign.Title.Length > 0 && campaign.Id.Length > 0, $"the campaign loads: \"{campaign.Title}\"");

            var problems = StoryCheck.Problems(campaign);
            foreach (string problem in problems)
            {
                Console.WriteLine("     problem: " + problem);
            }

            Program.Check(problems.Count == 0, "the script checker finds no problems");

            foreach (string path in Directory.GetFiles(folder))
            {
                string file = Path.GetFileName(path);
                Program.Check(file.EndsWith(".json") && used.Contains(file), $"{file} is part of the campaign");

                // Unity reads these as text assets; a byte order mark or a stray character would not
                // stop it loading, but an unusual one might not be in the game's font
                string text = File.ReadAllText(path);
                int bad = -1;
                for (int i = 0; i < text.Length; i++)
                {
                    if (!IsSafe(text[i]) && text[i] != '\r' && text[i] != '\t')
                    {
                        bad = i;
                        break;
                    }
                }

                Program.Check(bad < 0, bad < 0
                    ? $"{file} uses only plain characters"
                    : $"{file} uses only plain characters (found '{text[bad]}' U+{(int) text[bad]:X4} near \"{text.Substring(Math.Max(0, bad - 30), Math.Min(60, text.Length - Math.Max(0, bad - 30))).Replace("\n", " ")}\")");
            }

            Console.WriteLine("\n== Length and shape ==");

            int gigs = campaign.AllGigs.Count;
            int needed = 0;
            int songs = 0;
            int words = Words(campaign.Opening) + Words(campaign.Ending);
            int lines = campaign.Opening.Count + campaign.Ending.Count;
            foreach (var chapter in campaign.Chapters)
            {
                words += Words(chapter.Intro) + Words(chapter.Outro);
                lines += chapter.Intro.Count + chapter.Outro.Count;
                // A bonus chapter after the ending may be a single gig
                Program.Check(chapter.Gigs.Count >= (chapter.Bonus ? 1 : 2) && chapter.Intro.Count >= 6,
                    $"chapter {chapter.Number} \"{chapter.Title}\": {chapter.Gigs.Count} gigs, an intro of {chapter.Intro.Count} lines");
                foreach (var gig in chapter.Gigs)
                {
                    if (!gig.Optional)
                    {
                        needed++;
                        songs += gig.Songs;
                    }

                    words += Words(gig.Before) + Words(gig.AfterWin) + Words(gig.AfterLose) + Words(gig.AfterChallenge);
                    lines += gig.Before.Count + gig.AfterWin.Count + gig.AfterLose.Count + gig.AfterChallenge.Count;
                    Program.Check(gig.Before.Count >= 6 && gig.AfterWin.Count >= 5 && gig.AfterLose.Count >= 2,
                        $"gig {gig.Number} \"{gig.Title}\": scenes of {gig.Before.Count} / {gig.AfterWin.Count} / {gig.AfterLose.Count} lines");
                    Program.Check(gig.Challenge != null, $"gig {gig.Number} has a challenge");
                }
            }

            Program.Check(campaign.Chapters.Count >= 9, $"{campaign.Chapters.Count} chapters (at least 9)");
            Program.Check(gigs >= 28, $"{gigs} gigs (at least 28), {needed} of them needed by the story");
            Program.Check(songs >= 60, $"{songs} songs to play through the story (at least 60)");
            Program.Check(words >= 15000, $"{words} words of story in {lines} lines (at least 15,000 words)");
            Program.Check(campaign.Characters.Count >= 8, $"{campaign.Characters.Count} characters");
            Program.Check(campaign.BandNames.Count >= 8, $"{campaign.BandNames.Count} band names to choose from");
            Program.Check(campaign.Credits.Count > 0 && campaign.About.Length > 0, "there are credits and an About text");

            // The finale is the last gig of the story proper, before any bonus chapter
            StoryGig last = null;
            foreach (var gig in campaign.AllGigs)
            {
                if (!gig.Chapter.Bonus && !gig.Optional)
                {
                    last = gig;
                }
            }

            Program.Check(!last.Optional && last.Songs >= 4, $"the finale \"{last.Title}\" is a set of {last.Songs} songs");

            int firstTarget = campaign.AllGigs[0].StarsToPass;
            Program.Check(campaign.AllGigs[0].Songs == 1 && firstTarget <= 3, "the first gig is one song with a gentle target");
            foreach (var gig in campaign.AllGigs)
            {
                double average = (double) gig.StarsToPass / gig.Songs;
                Program.Check(average >= 2.0 && average <= 4.5,
                    $"gig {gig.Number} asks for {gig.StarsToPass} of {gig.MaxStars} stars ({average:0.0} a song)");
            }

            Console.WriteLine("\n== Venues ==");

            var visited = new HashSet<string>();
            foreach (var gig in campaign.AllGigs)
            {
                visited.Add(gig.VenueId);
            }

            foreach (string id in _catalogVenues)
            {
                Program.Check(campaign.FindVenue(id) != null && visited.Contains(id), $"the story plays at \"{id}\"");
            }

            foreach (var venue in campaign.Venues)
            {
                Program.Check(Array.IndexOf(_catalogVenues, venue.Id) >= 0 && visited.Contains(venue.Id),
                    $"venue \"{venue.Id}\" is one of the game's stages and is used");
            }

            Console.WriteLine("\n== Characters ==");

            var speakers = new Dictionary<string, int>();
            void Count(List<StoryLine> scene)
            {
                foreach (var line in scene)
                {
                    speakers.TryGetValue(line.Speaker, out int count);
                    speakers[line.Speaker] = count + 1;
                }
            }

            Count(campaign.Opening);
            Count(campaign.Ending);
            foreach (var chapter in campaign.Chapters)
            {
                Count(chapter.Intro);
                Count(chapter.Outro);
                foreach (var gig in chapter.Gigs)
                {
                    Count(gig.Before);
                    Count(gig.AfterWin);
                    Count(gig.AfterLose);
                    Count(gig.AfterChallenge);
                }
            }

            foreach (var character in campaign.Characters)
            {
                speakers.TryGetValue(character.Id, out int count);
                Program.Check(count >= 3, $"{character.Name} speaks {count} lines");
            }

            speakers.TryGetValue(StoryLine.YOU, out int yours);
            Program.Check(yours >= 40, $"the band speaks {yours} lines of its own");

            Console.WriteLine("\n== Liner notes and keepsakes ==");

            Program.Check(campaign.LinerNotes.Count >= 26, $"{campaign.LinerNotes.Count} liner notes (at least 26)");
            foreach (var note in campaign.LinerNotes)
            {
                int length = Words(note.Body);
                Program.Check(length >= 50 && length <= 190, $"liner note \"{note.Title}\" is {length} words (50 to 190)");
            }

            Program.Check(campaign.Keepsakes.Count >= 5, $"{campaign.Keepsakes.Count} keepsakes ({campaign.KeepsakeName})");

            Console.WriteLine("\n== Playing it through ==");

            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // The least a player can do: win each gig with exactly its target, never a challenge
            var progress = StoryEngine.NewProgress(campaign, campaign.BandNames[0], now);
            var engine = new StoryEngine(campaign, progress);
            int nights = 0;
            bool endingSeen = false;
            while (true)
            {
                var gig = engine.NextGig();
                if (gig == null || nights > gigs * 2)
                {
                    break;
                }

                var set = new List<StorySongResult>();
                int left = gig.StarsToPass;
                for (int i = 0; i < gig.Songs; i++)
                {
                    int stars = Math.Min(5, (int) Math.Ceiling((double) left / (gig.Songs - i)));
                    left -= stars;
                    set.Add(new StorySongResult { SongId = "song" + i, Stars = stars, Percent = 60, Players = 1, LowestDifficulty = 1 });
                }

                var outcome = engine.Apply(engine.Evaluate(gig, set), now);
                if (!outcome.Won)
                {
                    throw new Exception($"FAILED: the target of gig \"{gig.Id}\" cannot be met");
                }

                endingSeen |= outcome.StoryFinished;
                nights++;
            }

            Program.Check(nights == gigs && endingSeen && engine.IsStoryFinished(), $"winning each gig in turn reaches the ending ({nights} nights)");
            Program.Check(progress.LinerNotes.Count == campaign.LinerNotes.Count && progress.Keepsakes.Count == campaign.Keepsakes.Count,
                "by then every liner note and keepsake is unlocked");

            var topRank = campaign.Ranks[campaign.Ranks.Count - 1];
            var rankAtBareMinimum = engine.CurrentRank();
            Program.Check(rankAtBareMinimum != campaign.Ranks[0] && campaign.Ranks.Count >= 6,
                $"scraping through still ends above the first rank: \"{rankAtBareMinimum.Name}\" with {progress.Fans:N0} fans");
            Program.Check(progress.Fans < topRank.Fans, $"...but below the top rank \"{topRank.Name}\" ({topRank.Fans:N0} fans)");

            // The most a player can do: five gold stars on everything, every challenge met
            var best = StoryEngine.NewProgress(campaign, campaign.BandNames[0], now);
            var bestEngine = new StoryEngine(campaign, best);
            foreach (var gig in campaign.AllGigs)
            {
                var set = new List<StorySongResult>();
                for (int i = 0; i < gig.Songs; i++)
                {
                    set.Add(new StorySongResult { SongId = "song" + i, Stars = 5, Gold = true, Percent = 100, FullCombo = true, BestStreak = 5000, Players = 4, LowestDifficulty = 4 });
                }

                var outcome = bestEngine.Apply(bestEngine.Evaluate(gig, set), now);
                if (!outcome.ChallengeMet)
                {
                    throw new Exception($"FAILED: the challenge of gig \"{gig.Id}\" cannot be met by a perfect night");
                }
            }

            Program.Check(bestEngine.PercentComplete() == 100 && bestEngine.ChallengesMet() == bestEngine.ChallengesPossible(),
                "a perfect run meets every challenge and reaches 100%");
            Program.Check(best.Fans >= topRank.Fans, $"a perfect run reaches the top rank ({best.Fans:N0} fans)");

            // A solid player (four stars a song, no challenges) should land near the top, not at it
            var solid = StoryEngine.NewProgress(campaign, campaign.BandNames[0], now);
            var solidEngine = new StoryEngine(campaign, solid);
            foreach (var gig in campaign.AllGigs)
            {
                var set = new List<StorySongResult>();
                for (int i = 0; i < gig.Songs; i++)
                {
                    set.Add(new StorySongResult { SongId = "song" + i, Stars = 4, Percent = 85, Players = 1, LowestDifficulty = 2 });
                }

                solidEngine.Apply(solidEngine.Evaluate(gig, set), now);
            }

            int solidRank = campaign.Ranks.IndexOf(solidEngine.CurrentRank());
            Program.Check(solidRank >= campaign.Ranks.Count - 3,
                $"a solid four-star run ends at rank {solidRank + 1} of {campaign.Ranks.Count}: \"{solidEngine.CurrentRank().Name}\"");

            Console.WriteLine($"\nThe story: {campaign.Chapters.Count} chapters, {gigs} gigs, {songs} songs, {words:N0} words, {campaign.LinerNotes.Count} liner notes.");
        }
    }
}
