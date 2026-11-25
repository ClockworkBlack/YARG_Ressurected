// YARG Resurrected - Story Mode. Plain .NET, tested in tests/StoryTests.
//
// What a campaign is made of. The story itself (chapters, gigs, every line of dialogue, the
// liner notes) lives in JSON files under Assets/Resources/YargStory, so the writing can change
// without touching code. These classes are what those files turn into once read.
//
//   Campaign
//     characters   who speaks, and how their portrait is drawn
//     venues       where gigs happen (ids match the game's venue catalog when it is present)
//     chapters     in order; each has an opening scene, gigs, and a closing scene
//       gigs       a venue, a setlist size, a star target, an optional challenge, and scenes
//     liner notes  short history cards unlocked along the way
//     keepsakes    things collected through the story
//     ranks        names for how far the band has come
using System.Collections.Generic;

namespace YARG.Story
{
    /// <summary>One line of a scene: who says it and what they say.</summary>
    public sealed class StoryLine
    {
        /// <summary>The narrator: text with no portrait.</summary>
        public const string NARRATOR = "narrator";

        /// <summary>The player's band.</summary>
        public const string YOU = "you";

        /// <summary>A full-screen title card. Text is the big line, Sub the small one.</summary>
        public const string TITLE = "title";

        public string Speaker = NARRATOR;
        public string Text = "";

        /// <summary>The small line of a title card.</summary>
        public string Sub = "";

        /// <summary>How the speaker feels, for the portrait: see <see cref="StoryMoods"/>.</summary>
        public string Mood = "";

        /// <summary>
        /// Changes the picture behind the scene from this line on. Either a venue id or one of
        /// <see cref="StoryBackdrops.Names"/>. Empty keeps the current one.
        /// </summary>
        public string Backdrop = "";

        public static StoryLine FromJson(Dictionary<string, object> json)
        {
            return new StoryLine
            {
                Speaker = StoryJson.Str(json, "s", NARRATOR),
                Text = StoryJson.Str(json, "t"),
                Sub = StoryJson.Str(json, "sub"),
                Mood = StoryJson.Str(json, "mood"),
                Backdrop = StoryJson.Str(json, "bg"),
            };
        }

        public static List<StoryLine> ListFromJson(Dictionary<string, object> json, string key)
        {
            var lines = new List<StoryLine>();
            foreach (var item in StoryJson.Objects(json, key))
            {
                lines.Add(FromJson(item));
            }

            return lines;
        }
    }

    public static class StoryMoods
    {
        public const string NEUTRAL = "neutral";

        public static readonly string[] All =
        {
            NEUTRAL, "warm", "laugh", "stern", "sad", "shock", "hype", "sly", "proud", "worried",
        };
    }

    /// <summary>Pictures a scene can sit in front of, besides the venues.</summary>
    public static class StoryBackdrops
    {
        public const string BLACK = "black";

        public static readonly string[] Names =
        {
            BLACK,
            "wave",       // the narrator's own space: a waveform on black
            "tape",       // a turning tape reel: the past, as recorded
            "van",        // on the road, inside the van
            "road",       // highway at dusk, seen from outside
            "backstage",  // dressing room mirror lights
            "studio",     // mixing desk and a vocal booth
            "radio",      // a late-night radio booth
            "church",     // sanctuary with stained glass
            "street",     // a city block at night
            "rooftop",    // skyline at sunrise
            "office",     // a glass tower office
            "kitchen",    // a kitchen table under a hanging lamp
            "diner",      // a booth and a neon sign
            "airport",    // departures board and a wide window
            "porch",      // a porch in the evening
        };
    }

    /// <summary>How a character's portrait is drawn: simple shapes and a few colors.</summary>
    public sealed class StoryPortrait
    {
        public static readonly string[] Hairs =
        {
            "afro", "locs", "fade", "braids", "bald", "curls", "wrap", "waves", "puffs", "bob", "hightop", "twists",
        };

        public static readonly string[] Accessories =
        {
            "none", "shades", "glasses", "headphones", "hat", "cap", "earrings", "scarf", "chain", "bowtie", "flower",
            "beanie",
        };

        public string Skin = "#8D5524";
        public string Hair = "afro";
        public string HairColor = "#1B1410";
        public string Outfit = "#3A6EA5";
        public string Accent = "#F2C14E";
        public string Accessory = "none";

        public static StoryPortrait FromJson(Dictionary<string, object> json)
        {
            var portrait = new StoryPortrait();
            if (json == null)
            {
                return portrait;
            }

            portrait.Skin = StoryJson.Str(json, "skin", portrait.Skin);
            portrait.Hair = StoryJson.Str(json, "hair", portrait.Hair);
            portrait.HairColor = StoryJson.Str(json, "hairColor", portrait.HairColor);
            portrait.Outfit = StoryJson.Str(json, "outfit", portrait.Outfit);
            portrait.Accent = StoryJson.Str(json, "accent", portrait.Accent);
            portrait.Accessory = StoryJson.Str(json, "accessory", portrait.Accessory);
            return portrait;
        }
    }

    public sealed class StoryCharacter
    {
        public string Id = "";
        public string Name = "";

        /// <summary>A few words under the name the first time they appear, e.g. "Bus driver. Bass player. Legend."</summary>
        public string Role = "";

        public StoryPortrait Portrait = new StoryPortrait();

        public static StoryCharacter FromJson(Dictionary<string, object> json)
        {
            return new StoryCharacter
            {
                Id = StoryJson.Str(json, "id"),
                Name = StoryJson.Str(json, "name"),
                Role = StoryJson.Str(json, "role"),
                Portrait = StoryPortrait.FromJson(StoryJson.Obj(json, "portrait")),
            };
        }
    }

    public sealed class StoryVenue
    {
        public static readonly string[] Styles = { "theater", "arena", "club", "street", "upscale", "festival" };

        /// <summary>
        /// The same id the game's venue catalog uses, so the gig is played on that stage when the
        /// venue feature is installed.
        /// </summary>
        public string Id = "";

        public string Name = "";
        public string City = "";

        /// <summary>The real place this stage tips its hat to.</summary>
        public string InspiredBy = "";

        public string Style = "club";
        public string Top = "#20104A";
        public string Bottom = "#080418";
        public string Accent = "#F5B700";
        public string Accent2 = "#A45EE5";

        /// <summary>Where the stop sits on the tour map, 0 to 1 across and 0 to 1 up.</summary>
        public float MapX = 0.5f;
        public float MapY = 0.5f;

        public static StoryVenue FromJson(Dictionary<string, object> json)
        {
            var venue = new StoryVenue();
            venue.Id = StoryJson.Str(json, "id");
            venue.Name = StoryJson.Str(json, "name");
            venue.City = StoryJson.Str(json, "city");
            venue.InspiredBy = StoryJson.Str(json, "inspiredBy");
            venue.Style = StoryJson.Str(json, "style", venue.Style);
            venue.Top = StoryJson.Str(json, "top", venue.Top);
            venue.Bottom = StoryJson.Str(json, "bottom", venue.Bottom);
            venue.Accent = StoryJson.Str(json, "accent", venue.Accent);
            venue.Accent2 = StoryJson.Str(json, "accent2", venue.Accent2);
            venue.MapX = (float) StoryJson.Num(json, "mapX", venue.MapX);
            venue.MapY = (float) StoryJson.Num(json, "mapY", venue.MapY);
            return venue;
        }
    }

    /// <summary>
    /// The kind of music a gig leans toward. It is only used to suggest songs from the player's
    /// own library; any song can be played at any gig.
    /// </summary>
    public sealed class StoryVibe
    {
        /// <summary>Shown to the player, e.g. "Soul and classic R&amp;B".</summary>
        public string Label = "";

        /// <summary>Genre families, best first: see <see cref="StoryGenres"/>.</summary>
        public List<string> Genres = new List<string>();

        /// <summary>The years the gig leans toward. 0 means no preference.</summary>
        public int YearFrom;
        public int YearTo;

        /// <summary>Words that make a song fit better when they appear in its title, artist, album or genre.</summary>
        public List<string> Keywords = new List<string>();

        public static StoryVibe FromJson(Dictionary<string, object> json)
        {
            var vibe = new StoryVibe();
            if (json == null)
            {
                return vibe;
            }

            vibe.Label = StoryJson.Str(json, "label");
            vibe.Genres = StoryJson.Strings(json, "genres");
            vibe.YearFrom = StoryJson.Int(json, "yearFrom");
            vibe.YearTo = StoryJson.Int(json, "yearTo");
            vibe.Keywords = StoryJson.Strings(json, "keywords");
            return vibe;
        }
    }

    /// <summary>An extra goal for a gig. Meeting it is never needed to move on.</summary>
    public sealed class StoryChallenge
    {
        /// <summary>Earn at least Value stars in total over the set.</summary>
        public const string STARS = "stars";

        /// <summary>Five stars on every song of the set.</summary>
        public const string ALL_FIVE = "allFive";

        /// <summary>Gold stars on at least Value songs.</summary>
        public const string GOLD = "gold";

        /// <summary>A full combo (no missed notes) on at least Value songs.</summary>
        public const string FULL_COMBO = "fullCombo";

        /// <summary>Hit at least Value percent of the notes on every song.</summary>
        public const string ACCURACY = "accuracy";

        /// <summary>Reach a streak of at least Value notes in any one song.</summary>
        public const string STREAK = "streak";

        /// <summary>Play the set with at least Value people.</summary>
        public const string BAND = "band";

        /// <summary>Everyone plays on at least this difficulty: 1 easy, 2 medium, 3 hard, 4 expert.</summary>
        public const string DIFFICULTY = "difficulty";

        public static readonly string[] Types =
        {
            STARS, ALL_FIVE, GOLD, FULL_COMBO, ACCURACY, STREAK, BAND, DIFFICULTY,
        };

        public string Type = "";
        public int Value;

        /// <summary>The challenge in the story's words, e.g. "Miss Odessa never dropped a beat. Hit 95% of every song."</summary>
        public string Text = "";

        /// <summary>The name of the badge earned.</summary>
        public string Badge = "";

        public static StoryChallenge FromJson(Dictionary<string, object> json)
        {
            if (json == null)
            {
                return null;
            }

            return new StoryChallenge
            {
                Type = StoryJson.Str(json, "type"),
                Value = StoryJson.Int(json, "value"),
                Text = StoryJson.Str(json, "text"),
                Badge = StoryJson.Str(json, "badge"),
            };
        }
    }

    public sealed class StoryGig
    {
        public const int MAX_SONGS = 6;

        public string Id = "";
        public string Title = "";
        public string VenueId = "";

        /// <summary>One or two sentences on the gig's card.</summary>
        public string Blurb = "";

        /// <summary>A short stamp for the tour map, e.g. "87 BPM x 2 copies". May be empty.</summary>
        public string Stamp = "";

        public StoryVibe Vibe = new StoryVibe();

        /// <summary>How many songs are in the set.</summary>
        public int Songs = 1;

        /// <summary>Stars needed over the whole set to win the crowd. Each song gives up to five.</summary>
        public int StarsToPass = 3;

        public StoryChallenge Challenge;

        /// <summary>A side gig: it never blocks the story.</summary>
        public bool Optional;

        /// <summary>
        /// Gigs that must be won first. Empty means "the gig before this one" (or nothing, for the
        /// first gig of a chapter).
        /// </summary>
        public List<string> Requires = new List<string>();

        public List<StoryLine> Before = new List<StoryLine>();
        public List<StoryLine> AfterWin = new List<StoryLine>();
        public List<StoryLine> AfterLose = new List<StoryLine>();

        /// <summary>A short extra scene the first time the challenge is met.</summary>
        public List<StoryLine> AfterChallenge = new List<StoryLine>();

        /// <summary>Ids of liner notes and keepsakes earned by winning.</summary>
        public List<string> Rewards = new List<string>();

        // Filled in when the campaign is put together
        public StoryChapter Chapter;
        public int IndexInChapter;

        /// <summary>The gig's place in the whole story, counting from 1.</summary>
        public int Number;

        public int MaxStars => Songs * 5;

        public static StoryGig FromJson(Dictionary<string, object> json)
        {
            var gig = new StoryGig();
            gig.Id = StoryJson.Str(json, "id");
            gig.Title = StoryJson.Str(json, "title");
            gig.VenueId = StoryJson.Str(json, "venue");
            gig.Blurb = StoryJson.Str(json, "blurb");
            gig.Stamp = StoryJson.Str(json, "stamp");
            gig.Vibe = StoryVibe.FromJson(StoryJson.Obj(json, "vibe"));
            gig.Songs = StoryJson.Int(json, "songs", 1);
            gig.StarsToPass = StoryJson.Int(json, "starsToPass", gig.Songs * 3);
            gig.Challenge = StoryChallenge.FromJson(StoryJson.Obj(json, "challenge"));
            gig.Optional = StoryJson.Bool(json, "optional");
            gig.Requires = StoryJson.Strings(json, "requires");
            gig.Before = StoryLine.ListFromJson(json, "before");
            gig.AfterWin = StoryLine.ListFromJson(json, "afterWin");
            gig.AfterLose = StoryLine.ListFromJson(json, "afterLose");
            gig.AfterChallenge = StoryLine.ListFromJson(json, "afterChallenge");
            gig.Rewards = StoryJson.Strings(json, "rewards");
            return gig;
        }
    }

    public sealed class StoryChapter
    {
        public string Id = "";
        public int Number;
        public string Title = "";

        /// <summary>A line under the title, e.g. "Harlem, New York".</summary>
        public string Subtitle = "";

        /// <summary>The music the chapter honors, e.g. "Gospel roots, doo-wop and the first R&amp;B".</summary>
        public string Focus = "";

        /// <summary>A heading that groups chapters, e.g. "Side A". May be empty.</summary>
        public string Side = "";

        /// <summary>
        /// An extra chapter after the ending. It opens once the story is finished, and its gigs
        /// are not needed to finish the story.
        /// </summary>
        public bool Bonus;

        /// <summary>Played the first time the chapter is opened.</summary>
        public List<StoryLine> Intro = new List<StoryLine>();

        /// <summary>Played once every gig that the story needs is won.</summary>
        public List<StoryLine> Outro = new List<StoryLine>();

        public List<StoryGig> Gigs = new List<StoryGig>();

        public static StoryChapter FromJson(Dictionary<string, object> json)
        {
            var chapter = new StoryChapter();
            chapter.Id = StoryJson.Str(json, "id");
            chapter.Number = StoryJson.Int(json, "number");
            chapter.Title = StoryJson.Str(json, "title");
            chapter.Subtitle = StoryJson.Str(json, "subtitle");
            chapter.Focus = StoryJson.Str(json, "focus");
            chapter.Side = StoryJson.Str(json, "side");
            chapter.Bonus = StoryJson.Bool(json, "bonus");
            chapter.Intro = StoryLine.ListFromJson(json, "intro");
            chapter.Outro = StoryLine.ListFromJson(json, "outro");
            foreach (var item in StoryJson.Objects(json, "gigs"))
            {
                chapter.Gigs.Add(StoryGig.FromJson(item));
            }

            return chapter;
        }
    }

    /// <summary>A short card of real history, unlocked by winning a gig.</summary>
    public sealed class StoryLinerNote
    {
        public string Id = "";
        public string Title = "";

        /// <summary>A line under the title, e.g. "Harlem, 1934".</summary>
        public string Kicker = "";

        public string Body = "";

        public static StoryLinerNote FromJson(Dictionary<string, object> json)
        {
            return new StoryLinerNote
            {
                Id = StoryJson.Str(json, "id"),
                Title = StoryJson.Str(json, "title"),
                Kicker = StoryJson.Str(json, "kicker"),
                Body = StoryJson.Str(json, "body"),
            };
        }
    }

    /// <summary>Something collected through the story (what exactly is up to the script).</summary>
    public sealed class StoryKeepsake
    {
        /// <summary>The parts of a band. A keepsake may stand for one of them.</summary>
        public static readonly string[] Layers = { "voice", "bass", "guitar", "drums", "keys" };

        public string Id = "";
        public string Name = "";
        public string Description = "";

        /// <summary>
        /// One of <see cref="Layers"/>, or empty. The narrator's waveform gains that part once the
        /// keepsake is found.
        /// </summary>
        public string Layer = "";

        public static StoryKeepsake FromJson(Dictionary<string, object> json)
        {
            return new StoryKeepsake
            {
                Id = StoryJson.Str(json, "id"),
                Name = StoryJson.Str(json, "name"),
                Description = StoryJson.Str(json, "description"),
                Layer = StoryJson.Str(json, "layer"),
            };
        }
    }

    /// <summary>A name for how far the band has come, reached at a number of fans.</summary>
    public sealed class StoryRank
    {
        public string Name = "";
        public long Fans;

        public static StoryRank FromJson(Dictionary<string, object> json)
        {
            return new StoryRank
            {
                Name = StoryJson.Str(json, "name"),
                Fans = StoryJson.Long(json, "fans"),
            };
        }
    }

    public sealed class StoryCampaign
    {
        public string Id = "";

        /// <summary>
        /// Goes up when the script changes in a way old saves need to know about. Saves keep
        /// working across versions: gigs are remembered by id.
        /// </summary>
        public int Version = 1;

        public string Title = "";
        public string Tagline = "";

        /// <summary>The name shown over the narrator's lines, e.g. "The Break". May be empty.</summary>
        public string NarratorName = "";

        /// <summary>A few paragraphs for the "About" page.</summary>
        public string About = "";

        /// <summary>What the collected keepsakes are called together, e.g. "Verses".</summary>
        public string KeepsakeName = "Keepsakes";

        public List<string> BandNames = new List<string>();
        public List<StoryCharacter> Characters = new List<StoryCharacter>();
        public List<StoryVenue> Venues = new List<StoryVenue>();
        public List<StoryChapter> Chapters = new List<StoryChapter>();
        public List<StoryLinerNote> LinerNotes = new List<StoryLinerNote>();
        public List<StoryKeepsake> Keepsakes = new List<StoryKeepsake>();
        public List<StoryRank> Ranks = new List<StoryRank>();

        /// <summary>Played once, when a new story is started.</summary>
        public List<StoryLine> Opening = new List<StoryLine>();

        /// <summary>Played once every gig the story needs is won.</summary>
        public List<StoryLine> Ending = new List<StoryLine>();

        /// <summary>Thanks and dedications, shown after the ending and from the menu.</summary>
        public List<string> Credits = new List<string>();

        private readonly Dictionary<string, StoryCharacter> _characters = new Dictionary<string, StoryCharacter>();
        private readonly Dictionary<string, StoryVenue> _venues = new Dictionary<string, StoryVenue>();
        private readonly Dictionary<string, StoryGig> _gigs = new Dictionary<string, StoryGig>();
        private readonly Dictionary<string, StoryLinerNote> _notes = new Dictionary<string, StoryLinerNote>();
        private readonly Dictionary<string, StoryKeepsake> _keepsakes = new Dictionary<string, StoryKeepsake>();
        private readonly List<StoryGig> _allGigs = new List<StoryGig>();

        /// <summary>Every gig of the story, in order.</summary>
        public IReadOnlyList<StoryGig> AllGigs => _allGigs;

        public StoryCharacter FindCharacter(string id)
        {
            return id != null && _characters.TryGetValue(id, out var found) ? found : null;
        }

        public StoryVenue FindVenue(string id)
        {
            return id != null && _venues.TryGetValue(id, out var found) ? found : null;
        }

        public StoryGig FindGig(string id)
        {
            return id != null && _gigs.TryGetValue(id, out var found) ? found : null;
        }

        public StoryLinerNote FindLinerNote(string id)
        {
            return id != null && _notes.TryGetValue(id, out var found) ? found : null;
        }

        public StoryKeepsake FindKeepsake(string id)
        {
            return id != null && _keepsakes.TryGetValue(id, out var found) ? found : null;
        }

        /// <summary>
        /// Call after the lists are filled: builds the lookups and numbers the gigs. The first
        /// entry wins if an id is used twice (the checker reports it).
        /// </summary>
        public void Link()
        {
            _characters.Clear();
            _venues.Clear();
            _gigs.Clear();
            _notes.Clear();
            _keepsakes.Clear();
            _allGigs.Clear();

            foreach (var character in Characters)
            {
                if (!_characters.ContainsKey(character.Id))
                {
                    _characters[character.Id] = character;
                }
            }

            foreach (var venue in Venues)
            {
                if (!_venues.ContainsKey(venue.Id))
                {
                    _venues[venue.Id] = venue;
                }
            }

            foreach (var note in LinerNotes)
            {
                if (!_notes.ContainsKey(note.Id))
                {
                    _notes[note.Id] = note;
                }
            }

            foreach (var keepsake in Keepsakes)
            {
                if (!_keepsakes.ContainsKey(keepsake.Id))
                {
                    _keepsakes[keepsake.Id] = keepsake;
                }
            }

            int number = 0;
            for (int c = 0; c < Chapters.Count; c++)
            {
                var chapter = Chapters[c];
                if (chapter.Number <= 0)
                {
                    chapter.Number = c + 1;
                }

                for (int g = 0; g < chapter.Gigs.Count; g++)
                {
                    var gig = chapter.Gigs[g];
                    gig.Chapter = chapter;
                    gig.IndexInChapter = g;
                    gig.Number = ++number;
                    _allGigs.Add(gig);
                    if (!_gigs.ContainsKey(gig.Id))
                    {
                        _gigs[gig.Id] = gig;
                    }
                }
            }

            Ranks.Sort((a, b) => a.Fans.CompareTo(b.Fans));
        }

        /// <summary>
        /// Reads a campaign. <paramref name="readFile"/> is given a file name without its
        /// extension ("campaign", "chapter01", ...) and returns that file's text, or null when
        /// there is no such file.
        /// </summary>
        public static StoryCampaign Load(System.Func<string, string> readFile)
        {
            string indexText = readFile("campaign");
            if (indexText == null)
            {
                throw new StoryJsonException("The story's campaign file is missing");
            }

            if (!(StoryJson.Parse(indexText) is Dictionary<string, object> index))
            {
                throw new StoryJsonException("The story's campaign file is not a JSON object");
            }

            var campaign = new StoryCampaign();
            campaign.Id = StoryJson.Str(index, "id");
            campaign.Version = StoryJson.Int(index, "version", 1);
            campaign.Title = StoryJson.Str(index, "title");
            campaign.Tagline = StoryJson.Str(index, "tagline");
            campaign.NarratorName = StoryJson.Str(index, "narratorName");
            campaign.About = StoryJson.Str(index, "about");
            campaign.KeepsakeName = StoryJson.Str(index, "keepsakeName", campaign.KeepsakeName);
            campaign.BandNames = StoryJson.Strings(index, "bandNames");
            campaign.Opening = StoryLine.ListFromJson(index, "opening");
            campaign.Ending = StoryLine.ListFromJson(index, "ending");
            campaign.Credits = StoryJson.Strings(index, "credits");

            foreach (var item in StoryJson.Objects(index, "characters"))
            {
                campaign.Characters.Add(StoryCharacter.FromJson(item));
            }

            foreach (var item in StoryJson.Objects(index, "venues"))
            {
                campaign.Venues.Add(StoryVenue.FromJson(item));
            }

            foreach (var item in StoryJson.Objects(index, "keepsakes"))
            {
                campaign.Keepsakes.Add(StoryKeepsake.FromJson(item));
            }

            foreach (var item in StoryJson.Objects(index, "ranks"))
            {
                campaign.Ranks.Add(StoryRank.FromJson(item));
            }

            // Liner notes may sit in the campaign file or in files of their own
            foreach (var item in StoryJson.Objects(index, "linerNotes"))
            {
                campaign.LinerNotes.Add(StoryLinerNote.FromJson(item));
            }

            foreach (string file in StoryJson.Strings(index, "linerNoteFiles"))
            {
                string text = readFile(file);
                if (text == null)
                {
                    throw new StoryJsonException($"The story file \"{file}\" is missing");
                }

                if (!(StoryJson.Parse(text) is Dictionary<string, object> notes))
                {
                    throw new StoryJsonException($"The story file \"{file}\" is not a JSON object");
                }

                foreach (var item in StoryJson.Objects(notes, "linerNotes"))
                {
                    campaign.LinerNotes.Add(StoryLinerNote.FromJson(item));
                }
            }

            foreach (string file in StoryJson.Strings(index, "chapters"))
            {
                string text = readFile(file);
                if (text == null)
                {
                    throw new StoryJsonException($"The story file \"{file}\" is missing");
                }

                Dictionary<string, object> chapter;
                try
                {
                    chapter = StoryJson.Parse(text) as Dictionary<string, object>;
                }
                catch (StoryJsonException e)
                {
                    throw new StoryJsonException($"In the story file \"{file}\": {e.Message}");
                }

                if (chapter == null)
                {
                    throw new StoryJsonException($"The story file \"{file}\" is not a JSON object");
                }

                campaign.Chapters.Add(StoryChapter.FromJson(chapter));
            }

            campaign.Link();
            return campaign;
        }
    }
}
