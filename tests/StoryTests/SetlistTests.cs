using System;
using System.Collections.Generic;
using YARG.Story;

namespace StoryTests
{
    internal static class SetlistTests
    {
        private static StorySong Song(string id, string genre, int year, string artist = null, string name = null, double length = 200)
        {
            return new StorySong { Id = id, Name = name ?? "Song " + id, Artist = artist ?? "Artist " + id, Genre = genre, Year = year, LengthSeconds = length };
        }

        private static bool Has(string genre, params string[] families)
        {
            var found = StoryGenres.FamiliesOf(genre);
            foreach (string family in families)
            {
                if (!found.Contains(family)) return false;
            }

            return true;
        }

        public static void Run()
        {
            Console.WriteLine("\n== Sorting genre text into families ==");

            Program.Check(Has("R&B/Soul/Funk", StoryGenres.RNB, StoryGenres.SOUL, StoryGenres.FUNK), "\"R&B/Soul/Funk\" is R&B, soul and funk");
            Program.Check(Has("Hip-Hop/Rap", StoryGenres.HIPHOP) && Has("hip hop", StoryGenres.HIPHOP) && Has("Rap", StoryGenres.HIPHOP) && Has("HipHop", StoryGenres.HIPHOP), "the spellings of hip hop");
            Program.Check(Has("Pop/Dance/Electronic", StoryGenres.POP, StoryGenres.DANCE), "\"Pop/Dance/Electronic\" is pop and dance");
            Program.Check(Has("Rhythm & Blues", StoryGenres.RNB) && !Has("Rhythm & Blues", StoryGenres.BLUES), "\"Rhythm & Blues\" is R&B, not the blues");
            Program.Check(Has("Blues", StoryGenres.BLUES) && Has("Rhythm and Blues / Blues", StoryGenres.RNB, StoryGenres.BLUES), "the blues is still the blues");
            Program.Check(Has("RnB", StoryGenres.RNB) && Has("r&b", StoryGenres.RNB) && Has("Contemporary R&B", StoryGenres.RNB) && Has("R & B", StoryGenres.RNB), "the spellings of R&B");
            Program.Check(Has("Neo-Soul", StoryGenres.RNB, StoryGenres.SOUL) && Has("New Jack Swing", StoryGenres.RNB, StoryGenres.HIPHOP) && Has("Motown", StoryGenres.SOUL), "neo-soul, new jack swing, Motown");
            Program.Check(Has("Trap", StoryGenres.HIPHOP) && Has("G-Funk", StoryGenres.HIPHOP, StoryGenres.FUNK) && Has("Go-Go", StoryGenres.FUNK) && Has("Gospel", StoryGenres.GOSPEL) && Has("Disco", StoryGenres.DISCO) && Has("Jazz Fusion", StoryGenres.JAZZ), "trap, G-funk, go-go, gospel, disco, jazz");
            Program.Check(Has("Afrobeats", StoryGenres.AFRO) && Has("Reggae/Ska", StoryGenres.REGGAE) && Has("House", StoryGenres.DANCE), "Afrobeats, reggae, house");
            Program.Check(!Has("Trap", StoryGenres.POP) && !Has("Wrap", StoryGenres.HIPHOP) && !Has("Therapy", StoryGenres.HIPHOP) && !Has("Popular", StoryGenres.POP) && !Has("Skate", StoryGenres.REGGAE), "only whole words count (\"Therapy\" is not rap)");
            Program.Check(Has("Pop-Rock", StoryGenres.POP, StoryGenres.ROCK) && Has("Metal", StoryGenres.ROCK), "rock is told apart");
            Program.Check(StoryGenres.FamiliesOf("").Count == 0 && StoryGenres.FamiliesOf(null).Count == 0 && StoryGenres.FamiliesOf("Unknown Genre").Count == 0 && StoryGenres.FamiliesOf("!!!").Count == 0, "empty or unknown genre text gives no family");

            Console.WriteLine("\n== How well a song fits a gig ==");

            var soul = new StoryVibe { Label = "Soul", Genres = { StoryGenres.SOUL, StoryGenres.RNB }, YearFrom = 1960, YearTo = 1975 };
            double perfect = StorySetlist.Fit(Song("1", "Soul", 1968), soul);
            double rightGenre = StorySetlist.Fit(Song("2", "Soul", 1999), soul);
            double nearYear = StorySetlist.Fit(Song("3", "Soul", 1978), soul);
            double second = StorySetlist.Fit(Song("4", "R&B", 1968), soul);
            double cousin = StorySetlist.Fit(Song("5", "Hip-Hop/Rap", 1999), soul);
            double unknown = StorySetlist.Fit(Song("6", "", 0), soul);
            double metal = StorySetlist.Fit(Song("7", "Metal", 1999), soul);
            Program.Check(perfect > nearYear && nearYear > rightGenre, "the right years beat nearby years, which beat far-off years");
            Program.Check(perfect > second && second > cousin, "the gig's first genre beats its second, which beats a cousin genre");
            Program.Check(rightGenre > unknown && unknown > cousin && cousin > metal, "a song with no tags sits between a match and a mismatch");
            Program.Check(metal >= 0 && StorySetlist.Fit(null, soul) == 0 && StorySetlist.Fit(Song("8", "Soul", 1968), null) >= 0, "nothing scores below zero; missing things do not crash");
            Program.Check(StorySetlist.Fit(Song("9", "Soul", 1968, length: 20), soul) < perfect && StorySetlist.Fit(Song("9", "Soul", 1968, length: 1500), soul) < perfect, "a very short or very long song fits a little worse");

            var keyword = new StoryVibe { Label = "Motor City", Genres = { StoryGenres.SOUL }, Keywords = { "Detroit", "love" } };
            Program.Check(StorySetlist.Fit(Song("1", "Soul", 0, name: "Love Train"), keyword) > StorySetlist.Fit(Song("2", "Soul", 0, name: "Glove Train"), keyword), "a keyword counts as a whole word");
            Program.Check(StorySetlist.FitLabel(Song("1", "Soul", 1968), soul) == "Great fit" && StorySetlist.FitLabel(Song("7", "Metal", 1999), soul) == "", "a great fit is labelled, a poor one is not");

            Console.WriteLine("\n== Suggesting a set ==");

            var library = new List<StorySong>();
            for (int i = 0; i < 12; i++) library.Add(Song("soul" + i, "Soul", 1962 + i, artist: "Soul Artist " + (i % 6)));
            for (int i = 0; i < 12; i++) library.Add(Song("rap" + i, "Hip-Hop/Rap", 1990 + i));
            for (int i = 0; i < 12; i++) library.Add(Song("none" + i, "", 0));
            for (int i = 0; i < 12; i++) library.Add(Song("metal" + i, "Metal", 1985 + i));

            var set = StorySetlist.Suggest(library, soul, 4, seed: 1);
            Program.Check(set.Count == 4 && set.TrueForAll(s => s.Id.StartsWith("soul")), "a soul gig suggests soul songs");
            Program.Check(new HashSet<StorySong>(set).Count == 4, "no song twice in a set");
            var artists = new HashSet<string>();
            foreach (var song in set) artists.Add(song.Artist);
            Program.Check(artists.Count == 4, "different artists when there is a choice");

            var same = StorySetlist.Suggest(library, soul, 4, seed: 1);
            bool identical = true;
            for (int i = 0; i < 4; i++) identical &= same[i] == set[i];
            Program.Check(identical, "the same seed gives the same set");

            var seen = new HashSet<string>();
            for (int seed = 0; seed < 30; seed++)
            {
                foreach (var song in StorySetlist.Suggest(library, soul, 4, seed)) seen.Add(song.Id);
            }

            Program.Check(seen.Count >= 8, "different seeds bring out different songs (" + seen.Count + " of 12 soul songs seen)");

            var avoid = new HashSet<string>();
            foreach (var song in set) avoid.Add(song.Id);
            var reshuffled = StorySetlist.Suggest(library, soul, 4, seed: 1, avoid: avoid);
            Program.Check(reshuffled.TrueForAll(s => !avoid.Contains(s.Id)) && reshuffled.TrueForAll(s => s.Id.StartsWith("soul")), "a reshuffle avoids the songs it replaces and still fits");

            var plays = new Dictionary<string, int>();
            foreach (var song in library)
            {
                if (song.Id.StartsWith("soul") && song.Id != "soul3" && song.Id != "soul9") plays[song.Id] = 4;
            }

            var fresh = StorySetlist.Suggest(library, soul, 2, seed: 5, playCounts: plays);
            Program.Check(fresh.Exists(s => s.Id == "soul3") && fresh.Exists(s => s.Id == "soul9"), "songs the story has not used yet come first");

            var tiny = new List<StorySong> { Song("only", "", 0), Song("two", "", 0) };
            var repeated = StorySetlist.Suggest(tiny, soul, 5, seed: 3);
            Program.Check(repeated.Count == 5 && repeated[0] != repeated[1], "a library smaller than the set repeats songs, using each before repeating");
            Program.Check(StorySetlist.Suggest(tiny, soul, 2, seed: 3, avoid: new HashSet<string> { "only", "two" }).Count == 2, "avoiding everything still gives a set");
            Program.Check(StorySetlist.Suggest(new List<StorySong>(), soul, 3, 1).Count == 0 && StorySetlist.Suggest(null, soul, 3, 1).Count == 0 && StorySetlist.Suggest(library, soul, 0, 1).Count == 0, "an empty library or a set of no songs gives nothing");
            Program.Check(StorySetlist.Suggest(new List<StorySong> { null, Song("a", null, 0, artist: null, name: null) }, null, 1, 1).Count == 1, "songs with missing details do not crash it");

            var untagged = new List<StorySong>();
            for (int i = 0; i < 40; i++) untagged.Add(Song("u" + i, "", 0));
            var spread = new HashSet<string>();
            for (int seed = 0; seed < 40; seed++) spread.Add(StorySetlist.Suggest(untagged, soul, 1, seed)[0].Id);
            Program.Check(spread.Count >= 15, "a library with no tags at all still gets varied suggestions (" + spread.Count + " different songs in 40 tries)");

            var ranked = StorySetlist.Rank(library, soul);
            Program.Check(ranked.Count == library.Count && ranked[0].Id.StartsWith("soul") && ranked[ranked.Count - 1].Id.StartsWith("metal"), "the full list is ranked best fit first");

            Program.Check(StoryRandom.SeedFrom("harlem") == StoryRandom.SeedFrom("harlem") && StoryRandom.SeedFrom("harlem") != StoryRandom.SeedFrom("bronx") && StoryRandom.SeedFrom(null) == StoryRandom.SeedFrom(""), "text gives a steady seed");
            var random = new StoryRandom(0);
            bool inRange = true;
            for (int i = 0; i < 1000; i++)
            {
                double d = random.NextDouble();
                int r = random.Range(7);
                inRange &= d >= 0 && d < 1 && r >= 0 && r < 7;
            }

            Program.Check(inRange && new StoryRandom(1).Range(0) == 0, "random numbers stay in range");
        }
    }
}
