using System.Collections.Generic;
using YARG.Story;

namespace StoryTests
{
    /// <summary>A tiny made-up campaign for testing the rules, built from JSON text like the real one.</summary>
    internal static class Sample
    {
        private static string Scene(string speaker, string text)
        {
            return "[{\"s\":\"" + speaker + "\",\"t\":\"" + text + "\"}]";
        }

        private static string Gig(string id, string venue, int songs, int target, string extra = "")
        {
            return "{\"id\":\"" + id + "\",\"title\":\"Gig " + id + "\",\"venue\":\"" + venue + "\",\"blurb\":\"A night out.\"," +
                "\"vibe\":{\"label\":\"Soul\",\"genres\":[\"soul\",\"rnb\"],\"yearFrom\":1960,\"yearTo\":1975}," +
                "\"songs\":" + songs + ",\"starsToPass\":" + target + "," +
                "\"before\":" + Scene("mentor", "Go get them, {band}.") + "," +
                "\"afterWin\":" + Scene("you", "We did it.") + "," +
                "\"afterLose\":" + Scene("narrator", "Not tonight.") + extra + "}";
        }

        public static Dictionary<string, string> Files()
        {
            var files = new Dictionary<string, string>();
            files["campaign"] = @"{
              ""id"": ""sample"", ""version"": 3, ""title"": ""Sample Story"", ""tagline"": ""A test"", ""narratorName"": ""The Voice"",
              ""keepsakeName"": ""Tapes"",
              ""bandNames"": [""The Testers"", ""Unit & The Asserts""],
              ""characters"": [
                {""id"": ""mentor"", ""name"": ""Miss Test"", ""role"": ""Mentor"",
                 ""portrait"": {""skin"": ""#8D5524"", ""hair"": ""afro"", ""hairColor"": ""#111111"", ""outfit"": ""#224466"", ""accent"": ""#FFCC00"", ""accessory"": ""glasses""}}
              ],
              ""venues"": [
                {""id"": ""room-a"", ""name"": ""Room A"", ""city"": ""Testville"", ""style"": ""club"", ""top"": ""#101010"", ""bottom"": ""#000000"", ""accent"": ""#FF0000"", ""accent2"": ""#00FF00"", ""mapX"": 0.2, ""mapY"": 0.4},
                {""id"": ""room-b"", ""name"": ""Room B"", ""city"": ""Assert City"", ""style"": ""arena"", ""top"": ""#101010"", ""bottom"": ""#000000"", ""accent"": ""#FF0000"", ""accent2"": ""#00FF00"", ""mapX"": 0.7, ""mapY"": 0.6}
              ],
              ""keepsakes"": [{""id"": ""tape-1"", ""name"": ""Tape One"", ""description"": ""The first tape."", ""layer"": ""drums""}],
              ""ranks"": [{""name"": ""Legends"", ""fans"": 5000}, {""name"": ""Opening Act"", ""fans"": 0}, {""name"": ""Headliners"", ""fans"": 1000}],
              ""linerNotes"": [{""id"": ""note-1"", ""title"": ""A Note"", ""kicker"": ""Somewhere, 1960"", ""body"": ""It happened.""}],
              ""linerNoteFiles"": [""notes""],
              ""opening"": " + Scene("title", "Sample Story") + @",
              ""ending"": " + Scene("narrator", "The end.") + @",
              ""credits"": [""Thanks.""],
              ""chapters"": [""chapter01"", ""chapter02"", ""chapter03""]
            }";

            files["notes"] = @"{""linerNotes"": [{""id"": ""note-2"", ""title"": ""Another Note"", ""body"": ""It also happened.""}]}";

            files["chapter01"] = @"{
              ""id"": ""ch1"", ""title"": ""One"", ""subtitle"": ""Testville"", ""focus"": ""Soul"",
              ""intro"": " + Scene("narrator", "It begins.") + @",
              ""outro"": " + Scene("mentor", "On to the next town.") + @",
              ""gigs"": [
                " + Gig("g1", "room-a", 1, 3, ",\"rewards\":[\"note-1\",\"tape-1\"]") + @",
                " + Gig("g2", "room-a", 2, 6, ",\"challenge\":{\"type\":\"allFive\",\"text\":\"Five stars on both.\",\"badge\":\"Spotless\"},\"afterChallenge\":" + Scene("mentor", "Spotless.")) + @",
                " + Gig("side", "room-b", 1, 4, ",\"optional\":true,\"requires\":[\"g1\"],\"rewards\":[\"note-2\"]") + @"
              ]
            }";

            files["chapter02"] = @"{
              ""id"": ""ch2"", ""title"": ""Two"", ""subtitle"": ""Assert City"", ""focus"": ""Hip hop"",
              ""intro"": " + Scene("narrator", "A new town.") + @",
              ""gigs"": [
                " + Gig("g3", "room-b", 3, 9, ",\"challenge\":{\"type\":\"streak\",\"value\":50,\"text\":\"Fifty in a row.\",\"badge\":\"On a roll\"}") + @",
                " + Gig("g4a", "room-b", 1, 3, ",\"requires\":[\"g3\"]") + @",
                " + Gig("g4b", "room-a", 1, 3, ",\"requires\":[\"g3\"]") + @",
                " + Gig("g5", "room-b", 4, 14, ",\"requires\":[\"g4a\",\"g4b\"]") + @"
              ]
            }";

            files["chapter03"] = @"{
              ""id"": ""ch3"", ""title"": ""Hidden Track"", ""subtitle"": ""Later"", ""focus"": ""Yours"", ""side"": ""Side D"", ""bonus"": true,
              ""intro"": " + Scene("narrator", "Six months later.") + @",
              ""gigs"": [
                " + Gig("g6", "room-a", 1, 3, ",\"stamp\":\"87 BPM\"") + @"
              ]
            }";

            return files;
        }

        public static StoryCampaign Load()
        {
            var files = Files();
            return StoryCampaign.Load(name => files.TryGetValue(name, out string text) ? text : null);
        }

        public static StorySongResult Song(string id, int stars, bool gold = false, double percent = 90, int streak = 20,
            bool fullCombo = false, int players = 1, int difficulty = 2)
        {
            return new StorySongResult
            {
                SongId = id, SongName = "Song " + id, SongArtist = "Artist", Stars = stars, Gold = gold, Percent = percent,
                BestStreak = streak, FullCombo = fullCombo, Players = players, LowestDifficulty = difficulty, Score = stars * 10000,
            };
        }
    }
}
