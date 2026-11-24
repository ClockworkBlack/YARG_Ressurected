using System;
using System.Collections.Generic;
using YARG.Story;

namespace StoryTests
{
    internal static class EngineTests
    {
        private static readonly DateTime _now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        private static StoryGigOutcome Play(StoryEngine engine, string gigId, params StorySongResult[] songs)
        {
            var gig = engine.Campaign.FindGig(gigId);
            return engine.Apply(engine.Evaluate(gig, songs), _now);
        }

        public static void Run()
        {
            Console.WriteLine("\n== Reading a campaign ==");

            var campaign = Sample.Load();
            Program.Check(campaign.Id == "sample" && campaign.Version == 3 && campaign.Title == "Sample Story", "reads the campaign's id, version and title");
            Program.Check(campaign.Chapters.Count == 3 && campaign.AllGigs.Count == 8, "reads three chapters with eight gigs");
            Program.Check(campaign.Chapters[0].Number == 1 && campaign.Chapters[1].Number == 2, "numbers chapters that give no number");
            Program.Check(campaign.FindGig("g3").Number == 4 && campaign.FindGig("g3").Chapter.Id == "ch2", "numbers gigs through the whole story");
            Program.Check(campaign.LinerNotes.Count == 2 && campaign.FindLinerNote("note-2") != null, "reads liner notes from the campaign file and from their own file");
            Program.Check(campaign.Ranks[0].Fans == 0 && campaign.Ranks[2].Name == "Legends", "sorts ranks by fans");
            Program.Check(campaign.FindCharacter("mentor").Portrait.Accessory == "glasses", "reads a character's portrait");
            Program.Check(campaign.FindVenue("room-b").Style == "arena" && Math.Abs(campaign.FindVenue("room-b").MapX - 0.7f) < 0.001f, "reads a venue");
            Program.Check(campaign.FindGig("g2").Challenge.Type == StoryChallenge.ALL_FIVE && campaign.FindGig("g1").Challenge == null, "reads a gig's challenge");
            Program.Check(campaign.FindKeepsake("tape-1").Layer == "drums", "reads a keepsake and the part of the band it stands for");
            Program.Check(campaign.FindGig("nope") == null && campaign.FindVenue(null) == null, "an unknown id finds nothing");

            var problems = StoryCheck.Problems(campaign);
            Program.Check(problems.Count == 0, "the sample campaign has no problems" + (problems.Count > 0 ? ": " + string.Join(" | ", problems) : ""));

            bool missingThrew = false;
            try
            {
                StoryCampaign.Load(name => name == "campaign" ? Sample.Files()["campaign"] : null);
            }
            catch (StoryJsonException e)
            {
                missingThrew = e.Message.Contains("notes") || e.Message.Contains("chapter01");
            }

            Program.Check(missingThrew, "a missing story file is reported by name");

            Console.WriteLine("\n== Opening gigs and chapters ==");

            var progress = StoryEngine.NewProgress(campaign, "  ", _now);
            var engine = new StoryEngine(campaign, progress);
            Program.Check(progress.BandName == "The Testers", "an empty band name becomes the first on the list");
            Program.Check(engine.StateOf(campaign.FindGig("g1")) == StoryGigState.Open, "the first gig starts open");
            Program.Check(engine.StateOf(campaign.FindGig("g2")) == StoryGigState.Locked, "the second gig starts locked");
            Program.Check(engine.StateOf(campaign.FindGig("side")) == StoryGigState.Locked, "a side gig waits for the gig it requires");
            Program.Check(engine.StateOf(campaign.Chapters[0]) == StoryChapterState.Open && engine.StateOf(campaign.Chapters[1]) == StoryChapterState.Locked, "chapter one is open, chapter two locked");
            Program.Check(engine.StateOf(campaign.FindGig("g3")) == StoryGigState.Locked, "a gig in a locked chapter is locked");
            Program.Check(engine.NextGig().Id == "g1" && engine.CurrentChapter().Id == "ch1", "the next gig is the first one");
            Program.Check(engine.MissingFor(campaign.FindGig("g3")).Count == 2, "a locked gig says which gigs come first");
            Program.Check(engine.Fill("Hello {band}!") == "Hello The Testers!", "fills the band name into a line");

            Console.WriteLine("\n== Losing and winning ==");

            var lost = Play(engine, "g1", Sample.Song("a", 2));
            Program.Check(!lost.Won && !lost.FirstWin && lost.Stars == 2, "two stars does not win a three-star gig");
            Program.Check(lost.FansGained == 200 + 10, "a lost night still earns fans for its stars");
            Program.Check(engine.StateOf(campaign.FindGig("g1")) == StoryGigState.Open && progress.RecordOf("g1").Plays == 1, "a lost gig stays open and counts as played");
            Program.Check(lost.NewLinerNotes.Count == 0 && progress.LinerNotes.Count == 0, "a lost gig unlocks nothing");

            var won = Play(engine, "g1", Sample.Song("a", 4));
            Program.Check(won.Won && won.FirstWin && won.NewBest, "four stars wins it");
            Program.Check(won.NewLinerNotes.Count == 1 && won.NewKeepsakes.Count == 1 && progress.LinerNotes.Contains("note-1") && progress.Keepsakes.Contains("tape-1"), "the first win unlocks the gig's liner note and keepsake");
            Program.Check(won.NewGigs.Count == 2 && won.NewGigs.Contains(campaign.FindGig("g2")) && won.NewGigs.Contains(campaign.FindGig("side")), "the win opens the next gig and the side gig");
            Program.Check(won.FansGained == (650 - 200) + 32, "fans: the improvement over the best night, plus a little");
            Program.Check(won.ChapterFinished == null && !won.StoryFinished, "the chapter is not finished yet");
            Program.Check(progress.SongPlays["a"] == 2 && progress.LastSet.Count == 1, "remembers which songs were used");

            var again = Play(engine, "g1", Sample.Song("b", 3));
            Program.Check(again.Won && !again.FirstWin && !again.NewBest && again.NewLinerNotes.Count == 0, "winning again unlocks nothing new");
            Program.Check(again.FansGained == 27 && progress.RecordOf("g1").BestStars == 4, "a worse night keeps the best result and earns a few fans");
            Program.Check(progress.RecordOf("g1").BestSet[0].SongId == "a", "the best set is kept for the diary");

            Console.WriteLine("\n== Challenges ==");

            var short1 = engine.Evaluate(campaign.FindGig("g2"), new[] { Sample.Song("a", 5) });
            Program.Check(!short1.Won && short1.Stars == 5, "a set cut short does not win");

            var noChallenge = Play(engine, "g2", Sample.Song("a", 5), Sample.Song("b", 4));
            Program.Check(noChallenge.Won && !noChallenge.ChallengeMet && noChallenge.ChapterFinished == campaign.Chapters[0], "winning without the challenge still finishes the chapter");
            Program.Check(engine.StateOf(campaign.Chapters[0]) == StoryChapterState.Done && engine.StateOf(campaign.Chapters[1]) == StoryChapterState.Open, "chapter two opens with the side gig unplayed");
            Program.Check(noChallenge.NewGigs.Count == 1 && noChallenge.NewGigs[0].Id == "g3", "the first gig of the next chapter opens");

            long fansBefore = progress.Fans;
            var challenge = Play(engine, "g2", Sample.Song("a", 5, gold: true), Sample.Song("b", 5));
            Program.Check(challenge.ChallengeMet && challenge.ChallengeNew && progress.RecordOf("g2").ChallengeMet, "five stars on both songs meets the challenge");
            Program.Check(progress.Fans - fansBefore == (1300 - 1150) + 65 + 500, "the challenge adds its bonus once, gold stars add a little");
            var challengeAgain = Play(engine, "g2", Sample.Song("a", 5), Sample.Song("b", 5));
            Program.Check(challengeAgain.ChallengeMet && !challengeAgain.ChallengeNew, "meeting a challenge again is not new");

            var streakMiss = engine.Evaluate(campaign.FindGig("g3"), new[] { Sample.Song("a", 3, streak: 49), Sample.Song("b", 3), Sample.Song("c", 3) });
            var streakHit = engine.Evaluate(campaign.FindGig("g3"), new[] { Sample.Song("a", 3), Sample.Song("b", 3, streak: 50), Sample.Song("c", 3) });
            Program.Check(streakMiss.Won && !streakMiss.ChallengeMet && streakHit.ChallengeMet, "a streak challenge needs the streak in any one song");
            var lostButStreak = engine.Evaluate(campaign.FindGig("g3"), new[] { Sample.Song("a", 1, streak: 500), Sample.Song("b", 1), Sample.Song("c", 1) });
            Program.Check(!lostButStreak.Won && !lostButStreak.ChallengeMet, "a challenge only counts on a winning night");

            foreach (var test in new (string type, int value, StorySongResult[] songs, bool met)[]
            {
                (StoryChallenge.STARS, 9, new[] { Sample.Song("a", 5), Sample.Song("b", 4) }, true),
                (StoryChallenge.STARS, 10, new[] { Sample.Song("a", 5), Sample.Song("b", 4) }, false),
                (StoryChallenge.GOLD, 1, new[] { Sample.Song("a", 5, gold: true), Sample.Song("b", 4) }, true),
                (StoryChallenge.GOLD, 2, new[] { Sample.Song("a", 5, gold: true), Sample.Song("b", 4) }, false),
                (StoryChallenge.FULL_COMBO, 1, new[] { Sample.Song("a", 5), Sample.Song("b", 4, fullCombo: true) }, true),
                (StoryChallenge.FULL_COMBO, 1, new[] { Sample.Song("a", 5), Sample.Song("b", 4) }, false),
                (StoryChallenge.ACCURACY, 90, new[] { Sample.Song("a", 5, percent: 90), Sample.Song("b", 4, percent: 99) }, true),
                (StoryChallenge.ACCURACY, 95, new[] { Sample.Song("a", 5, percent: 94.9), Sample.Song("b", 4, percent: 99) }, false),
                (StoryChallenge.BAND, 2, new[] { Sample.Song("a", 5, players: 2), Sample.Song("b", 4, players: 3) }, true),
                (StoryChallenge.BAND, 2, new[] { Sample.Song("a", 5, players: 2), Sample.Song("b", 4, players: 1) }, false),
                (StoryChallenge.DIFFICULTY, 3, new[] { Sample.Song("a", 5, difficulty: 3), Sample.Song("b", 4, difficulty: 4) }, true),
                (StoryChallenge.DIFFICULTY, 3, new[] { Sample.Song("a", 5, difficulty: 2), Sample.Song("b", 4, difficulty: 4) }, false),
                ("nonsense", 1, new[] { Sample.Song("a", 5), Sample.Song("b", 5) }, false),
            })
            {
                var gig = new StoryGig
                {
                    Id = "t", Songs = 2, StarsToPass = 6, Chapter = campaign.Chapters[0],
                    Challenge = new StoryChallenge { Type = test.type, Value = test.value },
                };
                Program.Check(engine.Evaluate(gig, test.songs).ChallengeMet == test.met,
                    $"challenge \"{test.type}\" {test.value}: {(test.met ? "met" : "not met")}");
                Program.Check(test.type == "nonsense" || StoryEngine.Describe(gig.Challenge).Length > 0, $"challenge \"{test.type}\" can be described");
            }

            Console.WriteLine("\n== Branches, the ending, ranks ==");

            Play(engine, "g3", Sample.Song("a", 3), Sample.Song("b", 3), Sample.Song("c", 3));
            Program.Check(engine.StateOf(campaign.FindGig("g4a")) == StoryGigState.Open && engine.StateOf(campaign.FindGig("g4b")) == StoryGigState.Open, "two gigs that require the same gig open together");
            Program.Check(engine.StateOf(campaign.FindGig("g5")) == StoryGigState.Locked, "the gig after the branch is locked");
            Play(engine, "g4b", Sample.Song("a", 3));
            Program.Check(engine.StateOf(campaign.FindGig("g5")) == StoryGigState.Locked && engine.NextGig().Id == "g4a", "one branch is not enough");
            Play(engine, "g4a", Sample.Song("a", 3));
            Program.Check(engine.StateOf(campaign.FindGig("g5")) == StoryGigState.Open, "both branches open the finale");
            Program.Check(!engine.IsStoryFinished() && engine.StateOf(campaign.Chapters[2]) == StoryChapterState.Locked && engine.StateOf(campaign.FindGig("g6")) == StoryGigState.Locked, "the story is not finished before the finale, and the bonus chapter is locked");
            Program.Check(campaign.Chapters[2].Bonus && campaign.Chapters[2].Side == "Side D" && campaign.FindGig("g6").Stamp == "87 BPM", "reads a bonus chapter, its side and a gig's stamp");

            var finale = Play(engine, "g5", Sample.Song("a", 4), Sample.Song("b", 4), Sample.Song("c", 3), Sample.Song("d", 3));
            Program.Check(finale.Won && finale.StoryFinished && finale.ChapterFinished == campaign.Chapters[1] && engine.IsStoryFinished(), "winning the finale finishes the story");
            Program.Check(engine.NextGig().Id == "g6" && engine.StateOf(campaign.Chapters[2]) == StoryChapterState.Open, "with the story done, the bonus chapter opens");
            var finaleAgain = Play(engine, "g5", Sample.Song("a", 5), Sample.Song("b", 5), Sample.Song("c", 5), Sample.Song("d", 5));
            Program.Check(!finaleAgain.StoryFinished && finaleAgain.ChapterFinished == null, "the story only finishes once");

            Program.Check(engine.CurrentRank() != null && engine.RankFor(0).Name == "Opening Act" && engine.RankFor(999).Name == "Opening Act" && engine.RankFor(1000).Name == "Headliners" && engine.RankFor(999999).Name == "Legends", "ranks follow the fans");
            Program.Check(progress.Fans >= 5000 && engine.NextRank() == null, "at the top there is no next rank");
            Program.Check(engine.GigsWon() == 6 && engine.StarsPossible() == 70 && engine.StarsEarned() == 4 + 10 + 9 + 3 + 3 + 20, "counts gigs won and stars");
            Program.Check(engine.ChallengesPossible() == 2 && engine.ChallengesMet() == 1, "counts challenges");
            var bonus = Play(engine, "g6", Sample.Song("a", 5));
            Program.Check(bonus.Won && !bonus.StoryFinished && bonus.ChapterFinished == campaign.Chapters[2] && engine.NextGig().Id == "side", "the bonus gig does not finish the story a second time; the side gig is what is left");
            Play(engine, "side", Sample.Song("a", 5));
            Program.Check(engine.NextGig() == null && progress.LinerNotes.Contains("note-2"), "after the side gig nothing is left");
            int percent = engine.PercentComplete();
            Program.Check(percent > 80 && percent < 100, "not quite 100% with one challenge unmet (" + percent + "%)");

            Console.WriteLine("\n== The save file ==");

            progress.MarkSeen(StoryProgress.SCENE_OPENING);
            progress.MarkSeen(StoryProgress.GigBeforeScene(campaign.FindGig("g1")));
            progress.ReadLinerNotes.Add("note-1");
            progress.HeldVenueChoice = "Random";
            engine.Begin(campaign.FindGig("g3"), new[] { new StorySong { Id = "x" }, new StorySong { Id = "y" }, new StorySong { Id = "z" } });
            progress.Pending.Results.Add(Sample.Song("x", 4, gold: true, percent: 97.25, streak: 123, fullCombo: true, players: 2, difficulty: 4));

            string saved = progress.ToJsonText();
            var loaded = StoryProgress.FromJsonText(saved);
            Program.Check(loaded != null && loaded.BandName == progress.BandName && loaded.Fans == progress.Fans && loaded.CampaignId == "sample", "a save reads back the band and its fans");
            Program.Check(loaded.Gigs.Count == progress.Gigs.Count && loaded.IsWon("g5") && loaded.RecordOf("g2").ChallengeMet && loaded.RecordOf("g1").BestStars == 4 && loaded.RecordOf("g1").Plays == 3, "... and every gig's record");
            Program.Check(loaded.RecordOf("g5").BestSet.Count == 4 && loaded.RecordOf("g5").BestSet[0].SongName == "Song a" && loaded.RecordOf("g1").WonOn == "2026-11-24T12:00:00Z", "... with the best set and the date");
            Program.Check(loaded.HasSeen(StoryProgress.SCENE_OPENING) && loaded.HasSeen("gig.g1.before") && !loaded.HasSeen(StoryProgress.SCENE_ENDING), "... and the scenes seen");
            Program.Check(loaded.LinerNotes.SetEquals(progress.LinerNotes) && loaded.ReadLinerNotes.Contains("note-1") && loaded.Keepsakes.Contains("tape-1"), "... and what is unlocked");
            Program.Check(loaded.SongPlays["a"] == progress.SongPlays["a"] && loaded.LastSet.Count == progress.LastSet.Count, "... and the songs used");
            Program.Check(loaded.HeldVenueChoice == "Random", "... and the borrowed venue setting");
            var pending = loaded.Pending;
            Program.Check(pending != null && pending.GigId == "g3" && pending.SongIds.Count == 3 && pending.Results.Count == 1 && !pending.IsComplete, "... and the gig under way");
            var result = pending.Results[0];
            Program.Check(result.Stars == 4 && result.Gold && result.Percent == 97.25 && result.BestStreak == 123 && result.FullCombo && result.Players == 2 && result.LowestDifficulty == 4, "... with the result of each song played");
            Program.Check(loaded.ToJsonText() == saved, "saving what was loaded gives the same file");

            Program.Check(StoryProgress.FromJsonText("") == null && StoryProgress.FromJsonText("not json") == null && StoryProgress.FromJsonText("{}") == null && StoryProgress.FromJsonText("[]") == null, "text that is not a save reads as no save");
            var odd = StoryProgress.FromJsonText("{\"format\":1,\"fans\":-5,\"gigs\":{\"g1\":{\"won\":\"yes\",\"stars\":99,\"set\":[{\"stars\":42,\"percent\":900}]},\"g2\":7},\"seen\":[1,\"x\",null],\"pending\":{\"gig\":\"g1\",\"songs\":[]},\"songPlays\":{\"a\":-3,\"b\":\"many\",\"c\":2}}");
            Program.Check(odd != null && odd.Fans == 0 && !odd.IsWon("g1") && odd.RecordOf("g2") == null && odd.SeenScenes.Count == 1 && odd.Pending == null && odd.SongPlays.Count == 1 && odd.HeldVenueChoice == null, "a damaged save loads what makes sense and drops the rest");
            Program.Check(odd.RecordOf("g1").BestSet[0].Stars == 5 && odd.RecordOf("g1").BestSet[0].Percent == 100, "... and keeps numbers within their limits");

            // A save from an older script: gigs the story no longer has are ignored, stars are capped
            var old = StoryProgress.FromJsonText("{\"format\":1,\"gigs\":{\"gone\":{\"won\":true,\"stars\":5},\"g1\":{\"won\":true,\"stars\":50}}}");
            var oldEngine = new StoryEngine(campaign, old);
            Program.Check(oldEngine.GigsWon() == 1 && oldEngine.StarsEarned() == 5 && oldEngine.StateOf(campaign.FindGig("g2")) == StoryGigState.Open, "a save from an older script still works");
        }
    }
}
