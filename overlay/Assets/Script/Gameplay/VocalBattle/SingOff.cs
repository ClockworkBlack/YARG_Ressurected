// YARG Resurrected - sing-off: two singers (or two teams) trade lines and are scored head to head.
using System;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Extensions;
using YARG.Core.Logging;
using YARG.Gameplay.Player;
using YARG.Player;
using YARG.Settings;

namespace YARG.Gameplay.VocalBattle
{
    /// <summary>How many lines a singer gets before the other takes over.</summary>
    public enum SingOffTurns
    {
        OneLine,
        TwoLines,
        FourLines,
        // Rap mode: the song split evenly by bars
        FourBars,
        EightBars,
        SixteenBars
    }

    /// <summary>
    /// A sing-off turns the vocals of a song into a battle between two sides.
    ///
    /// On the Vocals instrument the song's lines are dealt out in turns: the first singer gets a
    /// line (or two, or four: Settings > General > Sing-Off Turn Length), then the second, and so
    /// on to the end. That is done by building a two-part "harmony" version of the vocals in
    /// memory, one part a side, so the game's own two-singer display does the rest: each side's
    /// notes come in its own color and its words on its own line.
    ///
    /// On Harmonies (a song with a part for each artist) nothing is dealt: each player sings the
    /// artist they picked, and the players on the first artist's part face those on the second's.
    ///
    /// Either way every line is marked out of 100 for how much of it was sung right, a harder level
    /// earning a little more, and the sides are compared on their average line (so the side with
    /// more lines has no head start). With dealt lines, each exchange is also a round with a winner.
    /// With more than two singers, players alternate between the two sides and play as teams.
    /// </summary>
    public static class SingOff
    {
        public const int SIDES = 2;

        /// <summary>A margin, in points out of 100, under which a round or the battle is a tie.</summary>
        public const double TIE_MARGIN = 3.0;

        public sealed class Side
        {
            public readonly List<YargPlayer> Players = new();
            public string Name = string.Empty;
            public Color Color = Color.white;

            public double Points;
            public int Lines;
            public int RoundsWon;

            /// <summary>Average points per line, 0 to about 110.</summary>
            public double Average => Lines > 0 ? Points / Lines : 0.0;

            // When this side sings: the end time of each of its lines and the turn it belongs to
            internal readonly List<double> LineStart = new();
            internal readonly List<double> LineEnd = new();
            internal readonly List<int> LineTurn = new();
            internal int Reported;

            internal readonly Dictionary<int, double> TurnPoints = new();
            internal readonly Dictionary<int, int> TurnLines = new();
        }

        /// <summary>True while the current song is being played as a sing-off.</summary>
        public static bool Active { get; private set; }

        /// <summary>True when the lines were dealt out in turns; false when each side sings an artist's part.</summary>
        public static bool SplitLines { get; private set; }

        public static readonly Side[] Sides = { new Side(), new Side() };

        /// <summary>Raised when an exchange is over: the round's number from 1, and the winning side (-1 for a tie).</summary>
        public static event Action<int, int> RoundDecided;

        private static VocalsTrack _splitTrack;
        private static readonly Dictionary<YargPlayer, int> _sideOf = new();

        // Which part of the split vocals each singer has (a side's in a sing-off with turns, an
        // artist's when the song's verses were dealt out between its artists)
        private static readonly Dictionary<YargPlayer, int> _partOf = new();

        /// <summary>True when the song's lines were dealt out in memory, for turns or for artists.</summary>
        public static bool UsesSplitTrack => _splitTrack != null;

        /// <summary>
        /// True when the song has one vocal part for everybody and its verses were dealt out between
        /// its credited artists, because a singer chose to be one of them. A guess: see VerseDealer.
        /// </summary>
        public static bool GuessedVerses { get; private set; }

        /// <summary>With guessed verses: the artist each part belongs to.</summary>
        public static string[] GuessedNames { get; private set; } = Array.Empty<string>();

        // For each round: when its last line ends, and whether it has been called
        private static readonly List<double> _roundEnd = new();
        private static int _roundsCalled;

        /// <summary>The time the last line of the battle ends.</summary>
        public static double LastLineEnd { get; private set; }

        public static void Reset()
        {
            Active = false;
            SplitLines = false;
            _splitTrack = null;
            _sideOf.Clear();
            _partOf.Clear();
            GuessedVerses = false;
            GuessedNames = Array.Empty<string>();
            _roundEnd.Clear();
            _roundsCalled = 0;
            LastLineEnd = 0;
            RoundDecided = null;
            for (int i = 0; i < SIDES; i++)
            {
                Sides[i] = new Side();
            }
        }

        /// <summary>
        /// Decides, before the players of a song are created, whether it is a sing-off, and sets it up.
        /// </summary>
        public static void Setup(GameManager gameManager, SongChart chart, IReadOnlyList<YargPlayer> players)
        {
            Reset();
            try
            {
                if (gameManager == null || chart == null || players == null ||
                    gameManager.IsPractice || gameManager.ReplayInfo != null)
                {
                    return;
                }

                var singers = new List<YargPlayer>();
                foreach (var player in players)
                {
                    if (!player.SittingOut && player.Profile.GameMode == GameMode.Vocals)
                    {
                        singers.Add(player);
                    }
                }

                if (singers.Count == 0)
                {
                    return;
                }

                bool battle = SettingsManager.Settings.SingOff.Value && singers.Count >= 2;
                if (singers[0].Profile.CurrentInstrument == Instrument.Harmony)
                {
                    if (battle)
                    {
                        SetupArtists(chart, singers);
                    }
                }
                else if (!SetupVerses(gameManager.Song, chart, singers, battle) && battle)
                {
                    SetupTurns(chart, singers);
                }
            }
            catch (Exception e)
            {
                // A sing-off that can't be set up is just a normal song
                YargLogger.LogException(e, "Couldn't set up the sing-off");
                Reset();
            }
        }

        /// <summary>Each side sings the artist's part its players picked.</summary>
        private static void SetupArtists(SongChart chart, List<YargPlayer> singers)
        {
            foreach (var singer in singers)
            {
                int side = singer.Profile.HarmonyIndex == 0 ? 0 : 1;
                _sideOf[singer] = side;
                Sides[side].Players.Add(singer);
            }

            // Everyone on the same artist: nobody to sing against
            if (Sides[0].Players.Count == 0 || Sides[1].Players.Count == 0)
            {
                Reset();
                return;
            }

            var parts = chart.Harmony.Parts;
            for (int p = 0; p < parts.Count; p++)
            {
                AddSchedule(Sides[p == 0 ? 0 : 1], parts[p], null);
            }

            Finish(false);
        }

        /// <summary>
        /// A singer chose to be one artist of a song that has one vocal part for everybody: its
        /// verses are dealt out between its artists (VerseDealer), each singer gets the verses of
        /// theirs, and the rest scroll past without counting. False when nobody chose that.
        /// </summary>
        private static bool SetupVerses(YARG.Core.Song.SongEntry song, SongChart chart, List<YargPlayer> singers, bool battle)
        {
            var source = chart.Vocals;
            bool chosen = false;
            foreach (var singer in singers)
            {
                chosen |= VocalParts.GuessedArtist(singer.Profile, song) >= 0;
            }

            if (!chosen || source.Parts.Count != 1 || CountSungPhrases(source.Parts[0]) < 4)
            {
                return false;
            }

            var artists = VocalParts.Artists(song, 1);
            if (artists.Count < 2 || !artists[0].Guessed)
            {
                return false;
            }

            int count = artists.Count;
            var original = source.Parts[0];

            var bars = new List<double>();
            foreach (var line in chart.SyncTrack.Beatlines)
            {
                if (line.Type == BeatlineType.Measure) bars.Add(line.Time);
            }

            var starts = new List<double>();
            var ends = new List<double>();
            var sung = new List<bool>();
            foreach (var phrase in original.NotePhrases)
            {
                var notes = phrase.PhraseParentNote.ChildNotes;
                bool words = IsSung(phrase);
                sung.Add(words);
                starts.Add(words ? notes[0].Time : phrase.Time);
                ends.Add(words ? notes[notes.Count - 1].TotalTimeEnd : phrase.TimeEnd);
            }
            var verses = VerseDealer.Verses(starts, ends, sung, bars);

            var notePhrases = new List<VocalsPhrase>[count];
            var staticPhrases = new List<VocalsPhrase>[count];
            for (int p = 0; p < count; p++)
            {
                notePhrases[p] = new List<VocalsPhrase>();
                staticPhrases[p] = new List<VocalsPhrase>();
            }

            for (int i = 0; i < original.NotePhrases.Count; i++)
            {
                int part = verses[i] % count;
                var copy = Rebuild(original.NotePhrases[i], part);

                var list = notePhrases[part];
                if (list.Count > 0)
                {
                    var previous = list[list.Count - 1].PhraseParentNote;
                    copy.PhraseParentNote.PreviousNote = previous;
                    previous.NextNote = copy.PhraseParentNote;
                }

                list.Add(copy);
                staticPhrases[part].Add(copy.Clone());
            }

            var parts = new List<VocalsPart>(count);
            var names = new string[count];
            for (int p = 0; p < count; p++)
            {
                parts.Add(new VocalsPart(true, notePhrases[p], staticPhrases[p],
                    original.OtherPhrases.Duplicate(), original.TextEvents.Duplicate()));
                names[p] = artists[p].Name;
            }

            // Who is who: the artist they chose; a singer who chose none gets one nobody has yet
            var taken = new bool[count];
            foreach (var singer in singers)
            {
                int artist = VocalParts.GuessedArtist(singer.Profile, song);
                if (artist >= 0 && artist < count)
                {
                    _partOf[singer] = artist;
                    taken[artist] = true;
                }
            }
            foreach (var singer in singers)
            {
                if (_partOf.ContainsKey(singer))
                {
                    continue;
                }

                int free = Array.IndexOf(taken, false);
                if (free < 0) free = 0;
                _partOf[singer] = free;
                taken[free] = true;
            }

            _splitTrack = new VocalsTrack(Instrument.Harmony, parts, source.RangeShifts.Duplicate(), source.Animations);
            GuessedVerses = true;
            GuessedNames = names;
            YargLogger.LogFormatInfo("Vocal Challenge: the song's verses dealt out between {0} artists (a guess)", count);

            // Artist against artist: the first artist anyone has, against everybody else's
            if (battle)
            {
                int first = _partOf[singers[0]];
                foreach (var singer in singers)
                {
                    int side = _partOf[singer] == first ? 0 : 1;
                    _sideOf[singer] = side;
                    Sides[side].Players.Add(singer);
                }

                if (Sides[1].Players.Count == 0)
                {
                    // Everyone on the same artist: nobody to sing against
                    _sideOf.Clear();
                    Sides[0].Players.Clear();
                }
                else
                {
                    for (int p = 0; p < count; p++)
                    {
                        if (taken[p])
                        {
                            AddSchedule(Sides[p == first ? 0 : 1], parts[p], null);
                        }
                    }
                    Finish(false);

                    // Each side in its artist's color
                    Sides[0].Color = VocalTrack.Colors[first % VocalTrack.Colors.Length];
                    int other = _partOf[Sides[1].Players[0]];
                    Sides[1].Color = VocalTrack.Colors[other % VocalTrack.Colors.Length];
                }
            }

            return true;
        }

        /// <summary>The song's lines are dealt out between the two sides in turns.</summary>
        private static void SetupTurns(SongChart chart, List<YargPlayer> singers)
        {
            var source = chart.Vocals;
            if (source.Parts.Count < 1 || CountSungPhrases(source.Parts[0]) < 4)
            {
                return;
            }

            var turnSetting = SettingsManager.Settings.SingOffTurnLength.Value;
            int turnLength = turnSetting switch
            {
                SingOffTurns.OneLine => 1,
                SingOffTurns.FourLines => 4,
                _ => 2
            };

            // Rap mode: turns of so many bars, a line going to whoever's bars it starts in
            int barsPerTurn = turnSetting switch
            {
                SingOffTurns.FourBars => 4,
                SingOffTurns.EightBars => 8,
                SingOffTurns.SixteenBars => 16,
                _ => 0
            };
            var measureTimes = new List<double>();
            if (barsPerTurn > 0)
            {
                foreach (var line in chart.SyncTrack.Beatlines)
                {
                    if (line.Type == BeatlineType.Measure) measureTimes.Add(line.Time);
                }
                if (measureTimes.Count < barsPerTurn * 2) barsPerTurn = 0;
            }
            int firstBlock = -1;

            for (int i = 0; i < singers.Count; i++)
            {
                int side = i % SIDES;
                _sideOf[singers[i]] = side;
                _partOf[singers[i]] = side;
                Sides[side].Players.Add(singers[i]);
            }

            var original = source.Parts[0];
            var notePhrases = new[] { new List<VocalsPhrase>(), new List<VocalsPhrase>() };
            var staticPhrases = new[] { new List<VocalsPhrase>(), new List<VocalsPhrase>() };
            var turnOfPhrase = new[] { new List<int>(), new List<int>() };

            int turn = 0;
            int linesInTurn = 0;
            foreach (var phrase in original.NotePhrases)
            {
                if (barsPerTurn > 0)
                {
                    // Which block of bars the line starts in (counted from the first one with words)
                    int measure = measureTimes.BinarySearch(phrase.Time + 0.05);
                    if (measure < 0) measure = ~measure - 1;
                    int block = Math.Max(0, measure) / barsPerTurn;
                    if (firstBlock < 0 && IsSung(phrase)) firstBlock = block;
                    turn = Math.Max(0, block - Math.Max(0, firstBlock));
                }

                int side = turn % SIDES;
                var copy = Rebuild(phrase, side);

                var list = notePhrases[side];
                if (list.Count > 0)
                {
                    var previous = list[list.Count - 1].PhraseParentNote;
                    copy.PhraseParentNote.PreviousNote = previous;
                    previous.NextNote = copy.PhraseParentNote;
                }

                list.Add(copy);
                staticPhrases[side].Add(copy.Clone());
                turnOfPhrase[side].Add(turn);

                // Only lines with words to sing use up a turn (not tambourine breaks or empty phrases)
                if (barsPerTurn == 0 && IsSung(phrase) && ++linesInTurn >= turnLength)
                {
                    linesInTurn = 0;
                    turn++;
                }
            }

            var parts = new List<VocalsPart>(SIDES);
            for (int side = 0; side < SIDES; side++)
            {
                var part = new VocalsPart(true, notePhrases[side], staticPhrases[side],
                    original.OtherPhrases.Duplicate(), original.TextEvents.Duplicate());
                parts.Add(part);
                AddSchedule(Sides[side], part, turnOfPhrase[side]);
            }

            _splitTrack = new VocalsTrack(Instrument.Harmony, parts, source.RangeShifts.Duplicate(), source.Animations);

            // A round is one turn each: it is over when the second side's turn ends
            int rounds = 0;
            foreach (int t in Sides[1].LineTurn)
            {
                rounds = Math.Max(rounds, t / SIDES + 1);
            }
            for (int r = 0; r < rounds; r++)
            {
                double end = -1;
                for (int i = 0; i < Sides[1].LineTurn.Count; i++)
                {
                    if (Sides[1].LineTurn[i] == r * SIDES + 1)
                    {
                        end = Math.Max(end, Sides[1].LineEnd[i]);
                    }
                }
                _roundEnd.Add(end);
            }

            Finish(true);
        }

        private static void Finish(bool splitLines)
        {
            for (int i = 0; i < SIDES; i++)
            {
                var side = Sides[i];
                side.Color = VocalTrack.Colors[i];
                side.Name = side.Players.Count == 1
                    ? side.Players[0].Profile.Name
                    : side.Players[0].Profile.Name + " +" + (side.Players.Count - 1);
                foreach (double end in side.LineEnd)
                {
                    LastLineEnd = Math.Max(LastLineEnd, end);
                }
            }

            SplitLines = splitLines;
            Active = true;
        }

        /// <summary>
        /// Called once the players of the song exist: starts the scoreboard. A sing-off with turns
        /// is not a normal run of the song (each singer has half of it), so it leaves no high score.
        /// </summary>
        public static void Begin(GameManager gameManager)
        {
            // Half a song, or one artist's guessed share of it, is not a run of the song
            if (UsesSplitTrack)
            {
                foreach (var player in _partOf.Keys)
                {
                    player.IsScoreValid = false;
                }
            }

            if (!Active)
            {
                return;
            }

            SingOffHud.Create(gameManager);
        }

        /// <summary>Notes when a side sings: every phrase of the part that has words in it.</summary>
        private static void AddSchedule(Side side, VocalsPart part, List<int> turns)
        {
            for (int i = 0; i < part.NotePhrases.Count; i++)
            {
                var phrase = part.NotePhrases[i];
                if (!IsSung(phrase))
                {
                    continue;
                }

                var notes = phrase.PhraseParentNote.ChildNotes;
                side.LineStart.Add(notes[0].Time);
                side.LineEnd.Add(phrase.TimeEnd);
                side.LineTurn.Add(turns != null ? turns[i] : -1);
            }
        }

        private static bool IsSung(VocalsPhrase phrase)
        {
            foreach (var note in phrase.PhraseParentNote.ChildNotes)
            {
                if (!note.IsPercussion)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountSungPhrases(VocalsPart part)
        {
            int count = 0;
            foreach (var phrase in part.NotePhrases)
            {
                if (IsSung(phrase))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>The same phrase with its notes belonging to another part (which decides their color and lane).</summary>
        private static VocalsPhrase Rebuild(VocalsPhrase phrase, int part)
        {
            var oldParent = phrase.PhraseParentNote;
            var parent = new VocalNote(oldParent.Flags, oldParent.IsPercussionPhrase, oldParent.Time, oldParent.TimeLength,
                oldParent.Tick, oldParent.TickLength);

            foreach (var note in oldParent.ChildNotes)
            {
                var copy = new VocalNote(note.Pitch, part, note.Type, note.Time, note.TimeLength, note.Tick, note.TickLength);
                // The notes a held word moves on to
                foreach (var slide in note.ChildNotes)
                {
                    copy.AddChildNote(new VocalNote(slide.Pitch, part, slide.Type, slide.Time, slide.TimeLength,
                        slide.Tick, slide.TickLength));
                }
                parent.AddChildNote(copy);
            }

            return new VocalsPhrase(phrase.Time, phrase.TimeLength, phrase.Tick, phrase.TickLength, parent,
                phrase.Lyrics.Duplicate());
        }

        /// <summary>The vocals to show and sing: the dealt-out version in a sing-off with turns, else the song's own.</summary>
        public static VocalsTrack TrackOr(VocalsTrack normal)
        {
            return _splitTrack ?? normal;
        }

        /// <summary>Which part of the vocals a player sings.</summary>
        public static int PartIndex(YargPlayer player)
        {
            if (_splitTrack != null && player != null && _partOf.TryGetValue(player, out int part))
            {
                return part;
            }

            return player != null ? player.Profile.HarmonyIndex : 0;
        }

        /// <summary>The side a player is on, or -1 when they aren't in the battle.</summary>
        public static int SideOf(YargPlayer player)
        {
            return Active && player != null && _sideOf.TryGetValue(player, out int side) ? side : -1;
        }

        /// <summary>
        /// A player finished a line. <paramref name="accuracy"/> is the share of the line sung right
        /// (0 to 1); a harder level is worth a little more.
        /// </summary>
        public static void ReportLine(YargPlayer player, double accuracy, Difficulty difficulty, double songTime)
        {
            int index = SideOf(player);
            if (index < 0)
            {
                return;
            }

            var side = Sides[index];
            double bonus = difficulty switch
            {
                Difficulty.Beginner => 0.7,
                Difficulty.Easy => 0.8,
                Difficulty.Medium => 0.9,
                Difficulty.Hard => 1.0,
                _ => 1.1
            };
            double points = Math.Max(0.0, Math.Min(1.0, accuracy)) * 100.0 * bonus;
            side.Points += points;
            side.Lines++;

            // Which turn was that line in? The latest of this side's lines to have ended.
            int turn = -1;
            for (int i = 0; i < side.LineEnd.Count; i++)
            {
                if (side.LineEnd[i] <= songTime + 0.35)
                {
                    turn = side.LineTurn[i];
                }
                else
                {
                    break;
                }
            }

            if (turn >= 0)
            {
                side.TurnPoints.TryGetValue(turn, out double soFar);
                side.TurnPoints[turn] = soFar + points;
                side.TurnLines.TryGetValue(turn, out int lines);
                side.TurnLines[turn] = lines + 1;
            }
        }

        /// <summary>Called every frame by the battle display: calls each round once both turns are in.</summary>
        public static void Update(double songTime)
        {
            if (!Active || !SplitLines)
            {
                return;
            }

            while (_roundsCalled < _roundEnd.Count)
            {
                // A moment after the round's last line, so the last singer's marks have arrived
                double end = _roundEnd[_roundsCalled];
                if (end >= 0 && songTime <= end + 0.6)
                {
                    break;
                }

                int round = _roundsCalled++;
                if (end < 0)
                {
                    // A turn with nothing to sing in it: no round to call
                    continue;
                }

                double first = TurnAverage(Sides[0], round * SIDES);
                double second = TurnAverage(Sides[1], round * SIDES + 1);
                int winner = Math.Abs(first - second) < TIE_MARGIN ? -1 : first > second ? 0 : 1;
                if (winner >= 0)
                {
                    Sides[winner].RoundsWon++;
                }

                RoundDecided?.Invoke(round + 1, winner);
            }
        }

        private static double TurnAverage(Side side, int turn)
        {
            return side.TurnLines.TryGetValue(turn, out int lines) && lines > 0
                ? side.TurnPoints[turn] / lines
                : 0.0;
        }

        /// <summary>
        /// Whether a side is singing at <paramref name="songTime"/>, and if not, how long until it is
        /// (<see cref="double.MaxValue"/> when it has no lines left).
        /// </summary>
        public static bool IsSinging(int sideIndex, double songTime, out double wait)
        {
            var side = Sides[sideIndex];
            wait = double.MaxValue;
            for (int i = 0; i < side.LineEnd.Count; i++)
            {
                if (side.LineEnd[i] < songTime)
                {
                    continue;
                }

                if (side.LineStart[i] <= songTime)
                {
                    wait = 0;
                    return true;
                }

                wait = side.LineStart[i] - songTime;
                return false;
            }
            return false;
        }

        /// <summary>The side that is ahead, or -1 when it is too close to call.</summary>
        public static int Leader()
        {
            if (Sides[0].Lines == 0 && Sides[1].Lines == 0)
            {
                return -1;
            }

            double difference = Sides[0].Average - Sides[1].Average;
            return Math.Abs(difference) < TIE_MARGIN ? -1 : difference > 0 ? 0 : 1;
        }
    }
}
