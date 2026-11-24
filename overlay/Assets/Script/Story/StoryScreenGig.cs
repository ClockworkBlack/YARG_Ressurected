// YARG Resurrected - Story Mode screens: the gig card (the venue, the target, the set), the song
// picker (the whole library ranked for the gig, drawn with a small pool of rows so a library of
// thousands of songs costs no more than one of ten) and the results of a night.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Logging;

namespace YARG.Story
{
    public sealed partial class StoryScreen
    {
        private const float PICK_ROW = 116f;

        private StoryGig _gig;
        private StoryGig _setFor;
        private IReadOnlyList<StorySong> _setLibrary;
        private List<StorySong> _set = new List<StorySong>();
        private int _shuffles;
        private StoryGigReturn _return;

        // Song picker
        private int _pickSlot;
        private int _cursor;
        private List<StorySong> _ranked;
        private StoryGig _rankedFor;
        private IReadOnlyList<StorySong> _rankedLibrary;
        private ScrollRect _pickScroll;
        private float _pickView;
        private readonly List<RectTransform> _poolRows = new List<RectTransform>();
        private readonly List<TextMeshProUGUI[]> _poolTexts = new List<TextMeshProUGUI[]>();
        private readonly List<Image> _poolRings = new List<Image>();
        private int[] _poolIndex = new int[0];

        // ------------------------------------------------------------------------------------
        // Gig card
        // ------------------------------------------------------------------------------------

        private void ShowGig(StoryGig gig)
        {
            if (gig == null || Engine == null)
            {
                ShowTour(null, null);
                return;
            }

            if (_gig != gig || _setFor != gig || !SetStillFits(gig))
            {
                _gig = gig;
                _shuffles = 0;
                Suggest(gig, false);
            }

            Open(View.Gig, BuildGig);
        }

        private bool SetStillFits(StoryGig gig)
        {
            var library = Mode.Library;
            // A rescan makes a new library: suggest again from the songs that are there now
            return library != null && library.Count > 0 && ReferenceEquals(library, _setLibrary) &&
                _set.Count == Mathf.Max(1, gig.Songs);
        }

        /// <summary>A set for the gig from the player's own songs, different from the last one played.</summary>
        private void Suggest(StoryGig gig, bool shuffle)
        {
            _setFor = gig;
            var library = Mode.Library;
            _setLibrary = library;
            var progress = Engine.Progress;
            if (library == null || library.Count == 0)
            {
                _set = new List<StorySong>();
                return;
            }

            var record = progress.RecordOf(gig.Id);
            int plays = record != null ? record.Plays : 0;
            int seed = StoryRandom.SeedFrom(gig.Id) + plays * 7919 + _shuffles * 104729;
            var avoid = new List<string>(progress.LastSet);
            if (shuffle)
            {
                foreach (var song in _set)
                {
                    if (song != null && song.Id != null) avoid.Add(song.Id);
                }
            }

            _set = StorySetlist.Suggest(library, gig.Vibe, Mathf.Max(1, gig.Songs), seed, progress.SongPlays, avoid);
        }

        private void Shuffle()
        {
            if (_gig == null) return;
            _shuffles++;
            Suggest(_gig, true);
            _keepFocus = Top != null ? Top.Index : -1;
            Rebuild(true);
        }

        private void BuildGig()
        {
            var gig = _gig;
            var engine = Engine;
            var campaign = engine.Campaign;
            var venue = campaign.FindVenue(gig.VenueId);
            var record = engine.Progress.RecordOf(gig.Id);
            var library = Mode.Library;
            bool noSongs = library == null || library.Count == 0;
            bool noPlayers = StoryMode.HumanPlayers <= 0;

            Picture(gig.VenueId);
            string chapter = gig.Chapter != null ? ChapterName(gig.Chapter) : "";
            var body = Header(gig.Title, JoinDash("Gig " + gig.Number, chapter), BackToTour);

            RectTransform info, set;
            if (Wide)
            {
                info = StoryUi.Area(body, 0f, 0f, 0.47f, 1f, "Info");
                set = StoryUi.Area(body, 0.48f, 0f, 1f, 1f, "Set");
            }
            else
            {
                info = StoryUi.Area(body, 0f, 0.5f, 1f, 1f, "Info");
                set = StoryUi.Area(body, 0f, 0f, 1f, 0.49f, "Set");
            }

            // What, where, and what it takes
            StoryUi.Inset(info, M, 8f, Wide ? 8f : M, 0f);
            StoryUi.Rounded(info, StoryTheme.Scrim, 1.5f);
            var inner = StoryUi.Inset(StoryUi.Area(info, 0f, 0f, 1f, 1f, "Inner"), 36f, 20f, 36f, 20f);
            StoryUi.Text(inner, 0f, 0.86f, 1f, 1f, venue != null ? venue.Name : "", 46, true, StoryTheme.Cream, TextAlignmentOptions.Left, false);
            string place = venue == null ? "" : JoinDash(venue.City, string.IsNullOrEmpty(venue.InspiredBy) ? "" : "Inspired by " + venue.InspiredBy);
            StoryUi.Text(inner, 0f, 0.78f, 1f, 0.86f, place, 28, false, StoryTheme.Muted, TextAlignmentOptions.Left);
            StoryUi.Text(inner, 0f, 0.72f, 1f, 0.78f, gig.Stamp, 28, true, StoryUi.Alpha(StoryTheme.Gold, 0.9f), TextAlignmentOptions.Left, false);
            StoryUi.Text(inner, 0f, 0.43f, 1f, 0.71f, Fill(gig.Blurb), 34, false, StoryTheme.Cream, TextAlignmentOptions.TopLeft);
            if (!string.IsNullOrEmpty(gig.Vibe.Label))
            {
                StoryUi.Text(inner, 0f, 0.36f, 1f, 0.43f, "Tonight: " + gig.Vibe.Label, 30, false, StoryTheme.Muted, TextAlignmentOptions.Left, false);
            }

            StoryUi.Text(inner, 0f, 0.27f, 1f, 0.36f, "Win the crowd: " + gig.StarsToPass + " of " + gig.MaxStars + " stars", 36, true,
                StoryTheme.Gold, TextAlignmentOptions.Left, false);
            if (gig.Challenge != null)
            {
                string challenge = !string.IsNullOrEmpty(gig.Challenge.Text) ? Fill(gig.Challenge.Text) : StoryEngine.Describe(gig.Challenge);
                bool met = record != null && record.ChallengeMet;
                StoryUi.Text(inner, 0f, 0.11f, met ? 0.68f : 1f, 0.27f, "Challenge: " + challenge, 28, false, StoryTheme.Cream,
                    TextAlignmentOptions.TopLeft);
                if (met && !string.IsNullOrEmpty(gig.Challenge.Badge))
                {
                    StoryArt.Badge(StoryUi.Area(inner, 0.7f, 0.15f, 1f, 0.25f, "Badge"), gig.Challenge.Badge, StoryTheme.Gold, 26);
                }
            }

            string best = record == null || record.Plays == 0
                ? "Not played yet"
                : "Best so far: " + record.BestStars + " of " + gig.MaxStars + " stars" + (record.Won ? "  -  won" : "");
            StoryArt.Icon(inner, StoryArt.Star, 0f, 0.01f, 0.05f, 0.09f, record != null && record.Won ? StoryTheme.Gold : StoryTheme.Faint);
            StoryUi.Text(inner, 0.07f, 0f, 1f, 0.1f, best, 30, false, StoryTheme.Cream, TextAlignmentOptions.Left, false);

            // The set
            StoryUi.Inset(set, Wide ? 8f : M, 8f, M, 0f);
            StoryUi.Rounded(set, StoryTheme.Scrim, 1.5f);
            var buttons = StoryUi.Area(set, 0f, 0f, 1f, 0f, "Buttons");
            buttons.offsetMin = new Vector2(28f, 24f);
            buttons.offsetMax = new Vector2(-28f, 24f + 104f);
            var title = StoryUi.Area(set, 0f, 1f, 1f, 1f, "Set Title");
            title.offsetMin = new Vector2(36f, -84f);
            title.offsetMax = new Vector2(-36f, -14f);
            StoryUi.Text(title, noSongs ? "No songs yet" : "The set", 42, true, StoryTheme.Gold, TextAlignmentOptions.Left, false);

            Focusable play = null;
            if (noSongs)
            {
                var help = StoryUi.Area(set, 0f, 0f, 1f, 1f, "Help");
                StoryUi.Inset(help, 40f, 150f, 40f, 100f);
                StoryUi.Text(help,
                    "The story plays your own songs, and the game has none yet.\n\nPut song folders in Internal storage/YARG/songs, " +
                    "then go to Settings > Songs and press Scan. Come back here once the scan is done.",
                    34, false, StoryTheme.Cream, TextAlignmentOptions.TopLeft);
            }
            else
            {
                var scroll = StoryUi.Scroll(set, 0f, 0f, 1f, 1f, out var items);
                var scrollRect = (RectTransform) scroll.transform;
                scrollRect.offsetMin = new Vector2(0f, 140f);
                scrollRect.offsetMax = new Vector2(0f, -96f);
                var size = BodySize;
                float viewHeight = (Wide ? size.y : size.y * 0.49f) - 260f;
                for (int i = 0; i < _set.Count; i++)
                {
                    var song = _set[i];
                    var row = StoryUi.Row(items, i * 116f + 4f, 104f, 28f, 28f, "Song");
                    int slot = i;
                    var focus = AddButton(row, StoryButtonStyle.Card, null, 0, () => ShowPicker(slot));
                    InList(focus, scroll, row, viewHeight);
                    StoryUi.Text(row, 0.02f, 0.1f, 0.1f, 0.9f, (i + 1).ToString(), 40, true, StoryTheme.Gold, TextAlignmentOptions.Center, false);
                    StoryUi.Text(row, 0.12f, 0.48f, 0.78f, 0.92f, song.Name, 34, true, StoryTheme.Cream, TextAlignmentOptions.Left, false);
                    StoryUi.Text(row, 0.12f, 0.1f, 0.78f, 0.48f, song.Artist, 26, false, StoryTheme.Muted, TextAlignmentOptions.Left, false);
                    string fit = StorySetlist.FitLabel(song, gig.Vibe);
                    if (!string.IsNullOrEmpty(fit))
                    {
                        StoryArt.Badge(StoryUi.Area(row, 0.79f, 0.28f, 0.98f, 0.72f, "Fit"), fit, StoryTheme.Gold, 22);
                    }
                }

                StoryUi.SetHeight(items, _set.Count * 116f + 8f);

                Top.Yellow = Shuffle;
                Top.YellowLabel = "Shuffle";
                AddButton(StoryUi.Area(buttons, 0f, 0f, 0.36f, 1f, "Shuffle"), StoryButtonStyle.Normal, "Shuffle", 36, Shuffle);
                if (noPlayers)
                {
                    play = AddButton(StoryUi.Area(buttons, 0.4f, 0f, 1f, 1f, "Players"), StoryButtonStyle.Primary, "Set up players", 36,
                        () => Mode.OpenProfiles());
                    StoryUi.Text(StoryUi.Inset(StoryUi.Area(buttons, 0f, 1f, 1f, 1f, "Why"), 0f, -2f, 0f, -40f),
                        "Nobody is set up to play yet: add a player first.", 26, false, StoryTheme.Danger, TextAlignmentOptions.Left, false);
                }
                else
                {
                    play = AddButton(StoryUi.Area(buttons, 0.4f, 0f, 1f, 1f, "Play"), StoryButtonStyle.Primary, "Play the gig", 40, Play);
                }
            }

            Prefer(play);
        }

        private void Play()
        {
            var gig = _gig;
            var progress = Engine.Progress;
            string before = StoryProgress.GigBeforeScene(gig);
            if (gig.Before.Count > 0 && !progress.HasSeen(before))
            {
                PlayScenes(new List<Step> { new Step(gig.Before, before, gig.VenueId) }, StartGigNow);
                return;
            }

            StartGigNow();
        }

        private void StartGigNow()
        {
            var gig = _gig;
            if (gig == null || _set.Count == 0)
            {
                ShowGig(gig);
                return;
            }

            // Come back to this card if the player backs out, or if the gig cannot start
            Remember(View.Gig, BuildGig);
            string problem = null;
            bool started = false;
            try
            {
                started = Mode.StartGig(gig, _set, out problem);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode could not start a gig");
            }

            if (started)
            {
                // StoryMode has already hidden the screen; make sure of it
                if (_shown) Hide();
                return;
            }

            if (!_shown)
            {
                Reveal();
            }

            Open(View.Gig, BuildGig);
            Notice("Not tonight", string.IsNullOrEmpty(problem) ? "The gig could not start. Please try again." : problem);
        }

        // ------------------------------------------------------------------------------------
        // Song picker
        // ------------------------------------------------------------------------------------

        private void ShowPicker(int slot)
        {
            var library = Mode.Library;
            if (library == null || library.Count == 0) return;

            _pickSlot = slot;
            if (_ranked == null || _rankedFor != _gig || !ReferenceEquals(_rankedLibrary, library))
            {
                _ranked = StorySetlist.Rank(library, _gig.Vibe);
                _rankedFor = _gig;
                _rankedLibrary = library;
            }

            var current = slot < _set.Count ? _set[slot] : null;
            _cursor = Mathf.Max(0, current != null ? _ranked.IndexOf(current) : 0);
            Open(View.Picker, BuildPicker);
        }

        private void BuildPicker()
        {
            StoryArt.Box(_artRoot, 0f, 0f, 1f, 1f, StoryTheme.Night);
            var body = Header("Song " + (_pickSlot + 1) + " of " + _set.Count,
                JoinDash(_gig.Title, string.IsNullOrEmpty(_gig.Vibe.Label) ? "" : "Tonight: " + _gig.Vibe.Label), () => ShowGig(_gig));

            _pickScroll = StoryUi.Scroll(body, 0f, 0f, 1f, 1f, out var items);
            _pickView = BodySize.y;
            StoryUi.SetHeight(items, _ranked.Count * PICK_ROW + 16f);
            _poolRows.Clear();
            _poolTexts.Clear();
            _poolRings.Clear();
            int pool = Mathf.Min(_ranked.Count, Mathf.CeilToInt(_pickView / PICK_ROW) + 2);
            _poolIndex = new int[pool];
            for (int k = 0; k < pool; k++)
            {
                var row = StoryUi.Row(items, 0f, PICK_ROW - 12f, M, M, "Pick");
                int slot = k;
                var button = StoryUi.Button(row, StoryButtonStyle.Card, null, 0, out _, out _);
                button.onClick.AddListener(() => Safely(() => PickRow(slot)));
                var texts = new[]
                {
                    StoryUi.Text(row, 0.01f, 0.1f, 0.08f, 0.9f, "", 30, true, StoryTheme.Faint, TextAlignmentOptions.Center, false),
                    StoryUi.Text(row, 0.1f, 0.48f, 0.74f, 0.92f, "", 34, true, StoryTheme.Cream, TextAlignmentOptions.Left, false),
                    StoryUi.Text(row, 0.1f, 0.08f, 0.74f, 0.48f, "", 26, false, StoryTheme.Muted, TextAlignmentOptions.Left, false),
                    StoryUi.Text(row, 0.75f, 0.1f, 0.9f, 0.9f, "", 26, true, StoryTheme.Gold, TextAlignmentOptions.Right, false),
                    StoryUi.Text(row, 0.9f, 0.1f, 0.99f, 0.9f, "", 26, false, StoryTheme.Muted, TextAlignmentOptions.Right, false),
                };
                var ring = StoryUi.Ring(row, StoryTheme.Gold);
                _anim.AddPulse(ring, 0.55f, 1f, 1.1f, 0f);
                _poolRows.Add(row);
                _poolTexts.Add(texts);
                _poolRings.Add(ring);
                _poolIndex[k] = -1;
            }

            _pickScroll.onValueChanged.AddListener(_ => RefreshPool());
            Top.Move = PickerMove;
            Top.Confirm = () => Pick(_cursor);
            StoryUi.ScrollTo(_pickScroll, _cursor * PICK_ROW - _pickView * 0.4f, _pickView);
            RefreshPool();
        }

        /// <summary>Puts the pooled rows where the list is scrolled to, and fills the ones that moved.</summary>
        private void RefreshPool()
        {
            if (_pickScroll == null || _ranked == null) return;

            float position = _pickScroll.content.anchoredPosition.y;
            int first = Mathf.Clamp(Mathf.FloorToInt(position / PICK_ROW), 0, Mathf.Max(0, _ranked.Count - _poolRows.Count));
            for (int k = 0; k < _poolRows.Count; k++)
            {
                var row = _poolRows[k];
                if (row == null) continue;

                int index = first + k;
                bool used = index < _ranked.Count;
                if (row.gameObject.activeSelf != used) row.gameObject.SetActive(used);
                if (!used) continue;

                _poolRings[k].gameObject.SetActive(index == _cursor);
                if (_poolIndex[k] == index) continue;

                _poolIndex[k] = index;
                row.offsetMin = new Vector2(M, -index * PICK_ROW - (PICK_ROW - 12f) - 8f);
                row.offsetMax = new Vector2(-M, -index * PICK_ROW - 8f);
                var song = _ranked[index];
                var texts = _poolTexts[k];
                texts[0].text = (index + 1).ToString();
                texts[1].text = song.Name;
                texts[2].text = JoinDash(song.Artist, song.Year > 0 ? song.Year.ToString() : "");
                texts[3].text = StorySetlist.FitLabel(song, _gig.Vibe);
                texts[4].text = StoryUi.Duration(song.LengthSeconds);
            }
        }

        private bool PickerMove(int dx, int dy)
        {
            int page = Mathf.Max(1, Mathf.FloorToInt(_pickView / PICK_ROW) - 1);
            int step = dy != 0 ? -dy : dx * page;
            _cursor = Mathf.Clamp(_cursor + step, 0, _ranked.Count - 1);

            float top = _cursor * PICK_ROW;
            float position = _pickScroll.content.anchoredPosition.y;
            if (top < position + 8f) position = top - 8f;
            else if (top + PICK_ROW > position + _pickView - 8f) position = top + PICK_ROW - _pickView + 8f;
            StoryUi.ScrollTo(_pickScroll, position, _pickView);
            RefreshPool();
            return true;
        }

        private void PickRow(int poolSlot)
        {
            StoryUi.KeepAwake();
            if (poolSlot < _poolIndex.Length && _poolIndex[poolSlot] >= 0)
            {
                Pick(_poolIndex[poolSlot]);
            }
        }

        private void Pick(int index)
        {
            if (_ranked == null || index < 0 || index >= _ranked.Count) return;

            if (_pickSlot < _set.Count)
            {
                _set[_pickSlot] = _ranked[index];
            }

            int slot = _pickSlot;
            ShowGig(_gig);
            // The song rows come first in the card's focus list after the Back button
            var layer = Top;
            if (layer != null && slot + 1 < layer.Focus.Count)
            {
                layer.Index = slot + 1;
                ShowFocus(layer);
            }
        }

        // ------------------------------------------------------------------------------------
        // Results
        // ------------------------------------------------------------------------------------

        private void BuildResults()
        {
            var outcome = _return.Outcome;
            var gig = outcome.Gig ?? _return.Gig;
            var campaign = Campaign;
            Picture(gig.VenueId);
            StoryArt.Box(_artRoot, 0f, 0f, 1f, 1f, StoryUi.Alpha(StoryTheme.Ink, 0.55f));

            var verdict = StoryUi.Row(_viewRoot, 16f, 150f, M, M, "Verdict");
            StoryUi.Text(verdict, 0f, 0.3f, 1f, 1f, outcome.Won ? "The crowd is yours" : "Not this time", 84, true,
                outcome.Won ? StoryTheme.Gold : StoryTheme.Cream, TextAlignmentOptions.Center, false);
            var venue = campaign.FindVenue(gig.VenueId);
            StoryUi.Text(verdict, 0f, 0f, 1f, 0.3f, JoinDash(gig.Title, venue != null ? venue.Name : ""), 30, false, StoryTheme.Muted,
                TextAlignmentOptions.Center, false);

            var body = StoryUi.Area(_viewRoot, 0f, 0f, 1f, 1f, "Body");
            body.offsetMax = new Vector2(0f, -180f);
            // Never "Songs": the game's font scaler resizes every text under an object of that name
            RectTransform songs, numbers, buttons;
            if (Wide)
            {
                songs = StoryUi.Area(body, 0f, 0.16f, 0.5f, 1f, "Set Played");
                numbers = StoryUi.Area(body, 0.5f, 0.16f, 1f, 1f, "Numbers");
                buttons = StoryUi.Area(body, 0.2f, 0f, 0.8f, 0.13f, "Buttons");
            }
            else
            {
                songs = StoryUi.Area(body, 0f, 0.55f, 1f, 1f, "Set Played");
                numbers = StoryUi.Area(body, 0f, 0.13f, 1f, 0.54f, "Numbers");
                buttons = StoryUi.Area(body, 0f, 0f, 1f, 0.11f, "Buttons");
            }

            StoryUi.Inset(songs, M, 0f, M / 2f, 0f);
            StoryUi.Inset(numbers, M / 2f, 0f, M, 0f);
            StoryUi.Inset(buttons, M, 0f, M, 0f);

            // One row per song
            StoryUi.Rounded(songs, StoryTheme.Scrim, 1.5f);
            int count = Mathf.Max(1, outcome.Songs.Count);
            for (int i = 0; i < outcome.Songs.Count; i++)
            {
                var song = outcome.Songs[i];
                float top = 1f - i / (float) Mathf.Max(count, 3);
                var row = StoryUi.Area(songs, 0f, top - 1f / Mathf.Max(count, 3), 1f, top, "Song");
                StoryUi.Inset(row, 28f, 6f, 28f, 6f);
                StoryUi.Text(row, 0f, 0.5f, 0.6f, 0.95f, song.SongName, 34, true, StoryTheme.Cream, TextAlignmentOptions.Left, false);
                StoryUi.Text(row, 0f, 0.08f, 0.6f, 0.5f, song.SongArtist, 26, false, StoryTheme.Muted, TextAlignmentOptions.Left, false);
                StoryArt.Stars(StoryUi.Area(row, 0.62f, 0.25f, 1f, 0.75f, "Stars"), song.Stars, 5, song.Gold);
            }

            // Stars against the target, and what the night brought
            StoryUi.Rounded(numbers, StoryTheme.Scrim, 1.5f);
            var inner = StoryUi.Inset(StoryUi.Area(numbers, 0f, 0f, 1f, 1f, "Inner"), 32f, 16f, 32f, 16f);
            StoryUi.Text(inner, 0f, 0.8f, 1f, 1f, outcome.Stars + " stars  -  " + gig.StarsToPass + " needed", 48, true, StoryTheme.Gold,
                TextAlignmentOptions.Left, false);
            var track = StoryUi.Area(inner, 0f, 0.72f, 1f, 0.77f, "Total");
            StoryUi.Rounded(track, StoryUi.Alpha(StoryTheme.Cream, 0.15f), 0.05f);
            float share = Mathf.Clamp01(outcome.Stars / (float) Mathf.Max(1, gig.MaxStars));
            StoryUi.Rounded(StoryUi.Area(track, 0f, 0f, Mathf.Max(0.01f, share), 1f, "Fill"), outcome.Won ? StoryTheme.Gold : StoryTheme.Muted, 0.05f);
            float target = Mathf.Clamp01(gig.StarsToPass / (float) Mathf.Max(1, gig.MaxStars));
            StoryUi.Solid(track, target - 0.004f, -0.8f, target + 0.004f, 1.8f, StoryTheme.Cream);

            var lines = new List<string>();
            if (gig.Challenge != null)
            {
                lines.Add(outcome.ChallengeMet
                    ? "Challenge met" + (string.IsNullOrEmpty(gig.Challenge.Badge) ? "" : ": " + gig.Challenge.Badge)
                    : "Challenge: " + (string.IsNullOrEmpty(gig.Challenge.Text) ? StoryEngine.Describe(gig.Challenge) : Fill(gig.Challenge.Text)));
            }

            if (outcome.FansGained > 0) lines.Add("+" + StoryUi.Number(outcome.FansGained) + " fans");
            if (outcome.RankUp && outcome.RankAfter != null) lines.Add("New rank: " + outcome.RankAfter.Name);
            foreach (var note in outcome.NewLinerNotes) lines.Add("New liner note: " + note.Title);
            foreach (var keepsake in outcome.NewKeepsakes) lines.Add("New in " + campaign.KeepsakeName + ": " + keepsake.Name);
            if (!string.IsNullOrEmpty(_return.Note)) lines.Add(_return.Note);
            StoryUi.Text(inner, 0f, 0f, 1f, 0.68f, string.Join("\n", lines), 34, false, StoryTheme.Cream, TextAlignmentOptions.TopLeft);

            if (outcome.Won)
            {
                var go = AddButton(buttons, StoryButtonStyle.Primary, "Continue", 40, () => AfterResults(false));
                Top.Back = () => AfterResults(false);
                Prefer(go);
            }
            else
            {
                AddButton(StoryUi.Area(buttons, 0f, 0f, 0.48f, 1f, "Tour"), StoryButtonStyle.Normal, "Back to the tour", 36, () => AfterResults(false));
                var retry = AddButton(StoryUi.Area(buttons, 0.52f, 0f, 1f, 1f, "Retry"), StoryButtonStyle.Primary, "Run it back", 40,
                    () => AfterResults(true));
                Top.Back = () => AfterResults(false);
                Prefer(retry);
            }
        }

        /// <summary>The scenes a night earned, in story order, then the tour (or the same gig again).</summary>
        private void AfterResults(bool retry)
        {
            var outcome = _return.Outcome;
            var gig = outcome.Gig ?? _return.Gig;
            var engine = Engine;
            var progress = engine.Progress;
            var steps = new List<Step>();
            if (outcome.Won)
            {
                string win = StoryProgress.GigWinScene(gig);
                if (!progress.HasSeen(win)) AddStep(steps, gig.AfterWin, win, gig.VenueId);
            }
            else
            {
                AddStep(steps, gig.AfterLose, null, gig.VenueId);
            }

            string challenge = StoryProgress.GigChallengeScene(gig);
            if (outcome.ChallengeNew && !progress.HasSeen(challenge)) AddStep(steps, gig.AfterChallenge, challenge, gig.VenueId);

            var chapter = outcome.ChapterFinished;
            if (chapter != null && !progress.HasSeen(StoryProgress.ChapterOutroScene(chapter)))
            {
                AddStep(steps, chapter.Outro, StoryProgress.ChapterOutroScene(chapter), gig.VenueId);
            }

            bool ending = outcome.StoryFinished && !progress.HasSeen(StoryProgress.SCENE_ENDING);
            if (ending)
            {
                AddStep(steps, engine.Campaign.Ending, StoryProgress.SCENE_ENDING, "wave");
                progress.MarkSeen(StoryProgress.SCENE_ENDING);
                Save();
            }

            PlayScenes(steps, () =>
            {
                if (ending) ShowPage(Page.Credits, BackToTourAfterGig);
                else if (!outcome.Won && retry) ShowGig(gig);
                else BackToTourAfterGig();
            });
        }

        private void BackToTourAfterGig()
        {
            // The story moves on: show the gig it now waits on
            ShowTour(null, null);
        }

        private static void AddStep(List<Step> steps, List<StoryLine> lines, string key, string backdrop)
        {
            if (lines != null && lines.Count > 0) steps.Add(new Step(lines, key, backdrop));
        }

        private void BuildMessage()
        {
            var result = _return;
            Picture(result.Gig != null ? result.Gig.VenueId : "black");
            StoryArt.Box(_artRoot, 0f, 0f, 1f, 1f, StoryUi.Alpha(StoryTheme.Ink, 0.6f));
            string title, body;
            switch (result.Kind)
            {
                case StoryGigReturnKind.CutShort:
                    title = "The set was cut short";
                    body = "Only a whole set counts, so nothing was recorded. The gig is waiting whenever you are.";
                    break;
                case StoryGigReturnKind.LoadFailed:
                    title = "A song would not load";
                    body = "The game could not load a song of the set. Try picking another song for that spot.";
                    break;
                default:
                    title = "No show tonight";
                    body = "You left before the first song. The crowd will still be there.";
                    break;
            }

            if (!string.IsNullOrEmpty(result.Note)) body += "\n\n" + result.Note;
            StoryUi.Text(_viewRoot, 0.08f, 0.6f, 0.92f, 0.8f, title, 72, true, StoryTheme.Gold, TextAlignmentOptions.Center);
            StoryUi.Text(_viewRoot, 0.1f, 0.32f, 0.9f, 0.58f, body, 38, false, StoryTheme.Cream, TextAlignmentOptions.Center);
            Action next = () =>
            {
                if (result.Gig != null && Engine != null) ShowGig(result.Gig);
                else ShowTour(null, null);
            };
            var box = StoryUi.Fixed(_viewRoot, 0.5f, 0.16f, 420f, 104f, "Continue");
            AddButton(box, StoryButtonStyle.Primary, "Continue", 40, next);
            Top.Back = next;
        }
    }
}
