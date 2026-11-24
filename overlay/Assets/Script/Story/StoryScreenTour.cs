// YARG Resurrected - Story Mode screens: the title (save slots, band name), the tour (chapters as
// the tracks of a double album, gig cards, the map) and the liner notes / evidence / about pages.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu.Persistent;

namespace YARG.Story
{
    public sealed partial class StoryScreen
    {
        private const int SLOTS = 3;

        private int _newSlot;
        private string _typedName;
        private StoryChapter _chapter;
        private StoryGig _tourFocus;

        private enum Page
        {
            LinerNotes,
            Evidence,
            About,
            Credits,
        }

        private Page _page;
        private Action _pageBack;
        private int _noteIndex;

        // ------------------------------------------------------------------------------------
        // Title
        // ------------------------------------------------------------------------------------

        private void ShowTitle()
        {
            Open(View.Title, BuildTitle);
        }

        private void Leave()
        {
            _view = View.None;
            _build = null;
            Hide();
            Mode?.Close();
        }

        private void BuildTitle()
        {
            Picture("wave");
            var campaign = Campaign;
            var mode = Mode;
            if (campaign == null || mode == null)
            {
                StoryUi.Text(_viewRoot, 0.08f, 0.55f, 0.92f, 0.75f, "Story could not be loaded", 72, true, StoryTheme.Gold,
                    TextAlignmentOptions.Center);
                StoryUi.Text(_viewRoot, 0.1f, 0.35f, 0.9f, 0.55f,
                    "The story files are missing or damaged. Your saved stories are safe. Updating the game should bring the story back.",
                    36, false, StoryTheme.Cream, TextAlignmentOptions.Center);
                var back = StoryUi.Fixed(_viewRoot, 0.5f, 0.2f, 360f, 104f, "Back");
                AddButton(back, StoryButtonStyle.Primary, "Back", 40, Leave);
                Top.Back = Leave;
                return;
            }

            Top.Back = Leave;
            Top.Yellow = ShowAboutFromTitle;
            Top.YellowLabel = "About";

            RectTransform words, slots, buttons;
            if (Wide)
            {
                words = StoryUi.Area(_viewRoot, 0f, 0.25f, 0.48f, 1f, "Words");
                slots = StoryUi.Area(_viewRoot, 0.5f, 0.04f, 1f, 0.96f, "Slots");
                buttons = StoryUi.Area(_viewRoot, 0f, 0.04f, 0.48f, 0.2f, "Buttons");
            }
            else
            {
                words = StoryUi.Area(_viewRoot, 0f, 0.7f, 1f, 0.98f, "Words");
                slots = StoryUi.Area(_viewRoot, 0f, 0.16f, 1f, 0.69f, "Slots");
                buttons = StoryUi.Area(_viewRoot, 0f, 0.02f, 1f, 0.14f, "Buttons");
            }

            StoryUi.Inset(words, M, 0f, M, 0f);
            StoryUi.Inset(slots, M, 0f, M, 0f);
            StoryUi.Inset(buttons, M, 0f, M, 0f);
            StoryUi.Text(words, 0f, 0.42f, 1f, 0.95f, campaign.Title, 132, true, StoryTheme.Gold, TextAlignmentOptions.BottomLeft);
            StoryUi.Solid(words, 0f, 0.38f, 0.25f, 0.39f, StoryTheme.Gold);
            StoryUi.Text(words, 0f, 0.05f, 1f, 0.35f, Fill(campaign.Tagline), 40, false, StoryTheme.Cream, TextAlignmentOptions.TopLeft);

            Focusable first = null;
            for (int slot = 1; slot <= SLOTS; slot++)
            {
                float top = 1f - (slot - 1) / (float) SLOTS;
                var card = StoryUi.Area(slots, 0f, top - 1f / SLOTS + 0.02f, 1f, top - 0.02f, "Slot");
                var focus = SlotCard(card, slot, mode.PeekSlot(slot));
                if (slot == mode.Slot || first == null) first = focus;
            }

            Prefer(first);
            AddButton(StoryUi.Area(buttons, 0f, 0f, 0.48f, 1f, "About"), StoryButtonStyle.Normal, "About", 38, ShowAboutFromTitle);
            AddButton(StoryUi.Area(buttons, 0.52f, 0f, 1f, 1f, "Leave"), StoryButtonStyle.Normal, "Back", 38, Leave);
        }

        private Focusable SlotCard(RectTransform card, int slot, StoryProgress progress)
        {
            if (progress == null)
            {
                var empty = AddButton(card, StoryButtonStyle.Card, null, 0, () => ShowBandNames(slot));
                StoryUi.Text(card, 0.05f, 0.55f, 0.95f, 0.9f, "New story", 56, true, StoryTheme.Gold, TextAlignmentOptions.Left, false);
                StoryUi.Text(card, 0.05f, 0.12f, 0.95f, 0.5f, "Slot " + slot + "  -  start the tour from the very first night", 30,
                    false, StoryTheme.Muted, TextAlignmentOptions.Left);
                return empty;
            }

            var focus = AddButton(card, StoryButtonStyle.Card, null, 0, () => ContinueSlot(slot));
            string line = "Slot " + slot;
            try
            {
                var engine = new StoryEngine(Campaign, progress);
                var rank = engine.CurrentRank();
                line = (rank != null ? rank.Name + "  -  " : "") + engine.GigsWon() + " gigs won  -  " + engine.StarsEarned() +
                    " stars  -  " + engine.PercentComplete() + "% complete";
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode could not read a save for the title screen");
            }

            StoryUi.Text(card, 0.05f, 0.5f, 0.72f, 0.9f, progress.BandName, 52, true, StoryTheme.Cream, TextAlignmentOptions.Left, false);
            StoryUi.Text(card, 0.05f, 0.1f, 0.72f, 0.48f, line, 28, false, StoryTheme.Muted, TextAlignmentOptions.Left);
            var delete = StoryUi.Area(card, 0.75f, 0.22f, 0.97f, 0.78f, "Delete");
            AddButton(delete, StoryButtonStyle.Normal, "Delete", 32, () => Ask("Delete this story?",
                "\"" + progress.BandName + "\" and everything the band has done will be gone for good.", "Delete",
                () =>
                {
                    Mode.DeleteSlot(slot);
                    ShowTitle();
                }, "Keep it", null));
            return focus;
        }

        private void ContinueSlot(int slot)
        {
            if (Mode.Continue(slot) && Engine != null)
            {
                ShowTour(null, null);
            }
            else
            {
                Notice("This story would not open", "Its save could not be read. Your other stories are not affected.");
            }
        }

        private void ShowAboutFromTitle()
        {
            ShowPage(Page.About, ShowTitle);
        }

        // ------------------------------------------------------------------------------------
        // Band name
        // ------------------------------------------------------------------------------------

        private void ShowBandNames(int slot)
        {
            _newSlot = slot;
            Open(View.BandName, BuildBandNames);
        }

        private void BuildBandNames()
        {
            Picture("backstage");
            var body = Header("Name your band", "Pick a name, or type your own. It is how the story will call you.", ShowTitle);
            var names = new List<string>(Campaign.BandNames);
            if (names.Count == 0) names.Add("The Band");

            int columns = Wide ? 3 : 2;
            const float rowHeight = 112f, gap = 20f;
            var size = BodySize;
            var scroll = StoryUi.Scroll(body, 0f, 0f, 1f, 1f, out var items);
            float viewHeight = size.y;
            float cell = (size.x - 2f * M - (columns - 1) * gap) / columns;
            int count = names.Count + 1;
            for (int i = 0; i < count; i++)
            {
                int row = i / columns, column = i % columns;
                var rect = StoryUi.Row(items, 10f + row * (rowHeight + gap), rowHeight, M + column * (cell + gap),
                    M + (columns - 1 - column) * (cell + gap), "Name");
                string name = i < names.Count ? names[i] : null;
                var focus = name != null
                    ? AddButton(rect, StoryButtonStyle.Normal, name, 38, () => BeginStory(name))
                    : AddButton(rect, StoryButtonStyle.Primary, "Type my own", 38, TypeBandName);
                InList(focus, scroll, rect, viewHeight);
            }

            StoryUi.SetHeight(items, 20f + ((count + columns - 1) / columns) * (rowHeight + gap));
        }

        private void TypeBandName()
        {
            var dialogs = DialogManager.Instance;
            if (dialogs == null || dialogs.IsDialogShowing) return;

            _typedName = null;
            var dialog = dialogs.ShowRenameDialog("Name your band", value => _typedName = value ?? "");
            var input = dialog != null ? dialog.GetComponentInChildren<TMP_InputField>(true) : null;
            if (input != null)
            {
                input.characterLimit = 28;
            }
        }

        // The rename dialog calls back while it is still open: act on the name once it has gone
        private void UpdateTypedName()
        {
            if (_typedName == null || (DialogManager.Instance != null && DialogManager.Instance.IsDialogShowing)) return;

            string name = _typedName.Replace("<", "").Replace(">", "").Replace("\n", " ").Trim();
            _typedName = null;
            if (name.Length > 28) name = name.Substring(0, 28).Trim();
            if (name.Length > 0 && _view == View.BandName)
            {
                BeginStory(name);
            }
        }

        private void BeginStory(string name)
        {
            Mode.StartNew(_newSlot, name);
            var engine = Engine;
            if (engine == null)
            {
                Notice("The story could not start", "Something went wrong while saving. Please try again.", ShowTitle);
                return;
            }

            var steps = new List<Step>();
            if (!engine.Progress.HasSeen(StoryProgress.SCENE_OPENING))
            {
                steps.Add(new Step(engine.Campaign.Opening, StoryProgress.SCENE_OPENING, "wave"));
            }

            PlayScenes(steps, () => ShowTour(null, null));
        }

        // ------------------------------------------------------------------------------------
        // Tour
        // ------------------------------------------------------------------------------------

        private void ShowTour(StoryChapter chapter, StoryGig focus)
        {
            var engine = Engine;
            if (engine == null || engine.Campaign.Chapters.Count == 0)
            {
                ShowTitle();
                return;
            }

            var next = engine.NextGig();
            chapter ??= focus != null ? focus.Chapter : engine.CurrentChapter();
            chapter ??= engine.Campaign.Chapters[0];
            if (focus == null && next != null && next.Chapter == chapter) focus = next;
            _chapter = chapter;
            _tourFocus = focus;

            // The first time a chapter is opened, its intro plays
            string intro = StoryProgress.ChapterIntroScene(chapter);
            if (engine.StateOf(chapter) != StoryChapterState.Locked && chapter.Intro.Count > 0 && !engine.Progress.HasSeen(intro))
            {
                var first = chapter.Gigs.Count > 0 ? chapter.Gigs[0].VenueId : "wave";
                PlayScenes(new List<Step> { new Step(chapter.Intro, intro, first) }, () => ShowTour(chapter, focus));
                return;
            }

            Open(View.Tour, BuildTour);
        }

        private void BuildTour()
        {
            var engine = Engine;
            var campaign = engine.Campaign;
            var progress = engine.Progress;
            Sleeve();

            var rank = engine.CurrentRank();
            var nextRank = engine.NextRank();
            string line = (rank != null ? rank.Name + "  -  " : "") + StoryUi.Number(progress.Fans) + " fans  -  " +
                engine.StarsEarned() + " of " + engine.StarsPossible() + " stars  -  " + engine.PercentComplete() + "% complete";
            var body = Header(progress.BandName, line, ShowTitle);
            Top.Yellow = () => ShowPage(Page.LinerNotes, BackToTour);
            Top.YellowLabel = "Liner Notes";
            Top.Blue = () => ShowPage(Page.Evidence, BackToTour);
            Top.BlueLabel = campaign.KeepsakeName;

            // Progress to the next rank, under the header
            if (nextRank != null)
            {
                long from = rank != null ? rank.Fans : 0;
                float share = nextRank.Fans > from ? Mathf.Clamp01((progress.Fans - from) / (float) (nextRank.Fans - from)) : 1f;
                var track = StoryUi.Row(_viewRoot, HEADER + 2f, 8f, M, M, "Rank");
                StoryUi.Rounded(track, StoryUi.Alpha(StoryTheme.Gold, 0.18f), 0.05f);
                var fill = StoryUi.Area(track, 0f, 0f, Mathf.Max(0.01f, share), 1f, "Fill");
                StoryUi.Rounded(fill, StoryTheme.Gold, 0.05f);
            }

            var size = BodySize;
            RectTransform chapters, mapArea, gigs, buttons;
            Vector2 chapterSize, gigSize, mapSize;
            if (Wide)
            {
                chapters = StoryUi.Area(body, 0f, 0.36f, 0.36f, 1f, "Chapters");
                mapArea = StoryUi.Area(body, 0f, 0f, 0.36f, 0.34f, "Map");
                gigs = StoryUi.Area(body, 0.37f, 0.14f, 1f, 1f, "Gigs");
                buttons = StoryUi.Area(body, 0.37f, 0f, 1f, 0.12f, "Buttons");
                chapterSize = new Vector2(size.x * 0.36f, size.y * 0.64f);
                mapSize = new Vector2(size.x * 0.36f - 2f * M, size.y * 0.34f - 16f);
                gigSize = new Vector2(size.x * 0.63f, size.y * 0.86f);
            }
            else
            {
                mapArea = StoryUi.Area(body, 0f, 0.78f, 1f, 1f, "Map");
                chapters = StoryUi.Area(body, 0f, 0.52f, 1f, 0.77f, "Chapters");
                gigs = StoryUi.Area(body, 0f, 0.09f, 1f, 0.51f, "Gigs");
                buttons = StoryUi.Area(body, 0f, 0f, 1f, 0.08f, "Buttons");
                mapSize = new Vector2(size.x - 2f * M, size.y * 0.22f - 16f);
                chapterSize = new Vector2(size.x, size.y * 0.25f);
                gigSize = new Vector2(size.x, size.y * 0.42f);
            }

            var map = StoryUi.Fixed(mapArea, 0.5f, 0.5f, mapSize.x, mapSize.y, "Tour Map");
            StoryArt.TourMap(map, mapSize, engine, _chapter, _anim);

            // Chapters, grouped under their side of the record
            var chapterScroll = StoryUi.Scroll(chapters, 0f, 0f, 1f, 1f, out var chapterItems);
            float y = 0f;
            string side = null;
            Focusable chosen = null;
            foreach (var chapter in campaign.Chapters)
            {
                if (!string.IsNullOrEmpty(chapter.Side) && chapter.Side != side)
                {
                    side = chapter.Side;
                    var heading = StoryUi.Row(chapterItems, y + 8f, 52f, M, M, "Side");
                    StoryUi.Text(heading, side.ToUpperInvariant(), 34, true, StoryTheme.Gold, TextAlignmentOptions.BottomLeft, false);
                    y += 64f;
                }

                var row = StoryUi.Row(chapterItems, y, 112f, M, M, "Chapter");
                var focus = ChapterRow(row, chapter, engine);
                InList(focus, chapterScroll, row, chapterSize.y);
                if (chapter == _chapter) chosen = focus;
                y += 124f;
            }

            StoryUi.SetHeight(chapterItems, y + 12f);

            // The gigs of the chosen chapter
            var heading2 = StoryUi.Area(gigs, 0f, 1f, 1f, 1f, "Chapter Title");
            heading2.offsetMin = new Vector2(M, -118f);
            heading2.offsetMax = new Vector2(-M, -4f);
            StoryUi.Text(heading2, 0f, 0.45f, 1f, 1f, ChapterName(_chapter), 46, true, StoryTheme.Cream, TextAlignmentOptions.BottomLeft, false);
            StoryUi.Text(heading2, 0f, 0f, 1f, 0.45f, JoinDash(_chapter.Subtitle, _chapter.Focus), 28, false, StoryTheme.Muted,
                TextAlignmentOptions.TopLeft);
            var gigScroll = StoryUi.Scroll(gigs, 0f, 0f, 1f, 1f, out var gigItems);
            gigScroll.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -124f);
            y = 0f;
            Focusable gigChosen = null;
            foreach (var gig in _chapter.Gigs)
            {
                var row = StoryUi.Row(gigItems, y + 6f, 178f, M, M, "Gig");
                var focus = GigCard(row, gig, engine);
                InList(focus, gigScroll, row, gigSize.y - 124f);
                if (gig == _tourFocus || (gigChosen == null && _tourFocus == null && engine.StateOf(gig) == StoryGigState.Open))
                {
                    gigChosen = focus;
                }

                y += 194f;
            }

            StoryUi.SetHeight(gigItems, y + 12f);

            StoryUi.Inset(buttons, M, 0f, M, 0f);
            AddButton(StoryUi.Area(buttons, 0f, 0f, 0.32f, 1f, "Notes"), StoryButtonStyle.Normal, "Liner Notes", 34, Top.Yellow);
            AddButton(StoryUi.Area(buttons, 0.34f, 0f, 0.66f, 1f, "Evidence"), StoryButtonStyle.Normal, campaign.KeepsakeName, 34, Top.Blue);
            AddButton(StoryUi.Area(buttons, 0.68f, 0f, 1f, 1f, "About"), StoryButtonStyle.Normal, "About", 34,
                () => ShowPage(Page.About, BackToTour));

            Prefer(gigChosen ?? chosen);
        }

        /// <summary>The tour's backdrop: a dark sleeve with the grooves of a record peeking out.</summary>
        private void Sleeve()
        {
            StoryArt.Box(_artRoot, 0f, 0f, 1f, 1f, StoryTheme.Night);
            float side = Mathf.Max(_full.x, _full.y) * 0.9f;
            var record = StoryUi.Fixed(_artRoot, 1f, 0f, side, side, "Record");
            StoryArt.Oval(record, 0f, 0f, 1f, 1f, new Color(0.02f, 0.02f, 0.02f, 1f));
            for (int i = 1; i < 9; i++)
            {
                float inset = i * 0.045f;
                StoryArt.Icon(record, StoryArt.Hoop, inset, inset, 1f - inset, 1f - inset, StoryUi.Alpha(StoryTheme.Gold, 0.05f));
            }

            StoryArt.Oval(record, 0.38f, 0.38f, 0.62f, 0.62f, StoryUi.Alpha(StoryTheme.GoldDeep, 0.35f));
        }

        private void BackToTour()
        {
            ShowTour(_chapter, _tourFocus);
        }

        private static string ChapterName(StoryChapter chapter)
        {
            return chapter.Number.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "  " + chapter.Title;
        }

        private static string JoinDash(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            return a + "  -  " + b;
        }

        private Focusable ChapterRow(RectTransform row, StoryChapter chapter, StoryEngine engine)
        {
            var state = engine.StateOf(chapter);
            var focus = AddButton(row, StoryButtonStyle.Card, null, 0, () =>
            {
                if (engine.StateOf(chapter) == StoryChapterState.Locked)
                {
                    Notice("Not yet", "This part of the record opens once the story before it is played through.");
                }
                else
                {
                    ShowTour(chapter, null);
                }
            });

            if (chapter == _chapter)
            {
                StoryUi.Solid(row, 0f, 0.12f, 0.012f, 0.88f, StoryTheme.Gold);
            }

            var titleColor = state == StoryChapterState.Locked ? StoryTheme.Faint : StoryTheme.Cream;
            StoryUi.Text(row, 0.05f, 0.45f, 0.74f, 0.92f, ChapterName(chapter), 36, true, titleColor, TextAlignmentOptions.Left, false);
            StoryUi.Text(row, 0.05f, 0.08f, 0.74f, 0.45f, chapter.Subtitle, 26, false, StoryTheme.Muted, TextAlignmentOptions.Left, false);
            var icon = state == StoryChapterState.Locked ? StoryArt.Lock : state == StoryChapterState.Done ? StoryArt.Check : StoryArt.Star;
            var iconColor = state == StoryChapterState.Locked ? StoryTheme.Faint : StoryTheme.Gold;
            StoryArt.Icon(row, icon, 0.76f, 0.3f, 0.84f, 0.7f, iconColor);
            if (state != StoryChapterState.Locked)
            {
                StoryUi.Text(row, 0.84f, 0.1f, 0.98f, 0.9f, engine.StarsEarned(chapter) + "/" + engine.StarsPossible(chapter), 28,
                    false, StoryTheme.Muted, TextAlignmentOptions.Right, false);
            }

            return focus;
        }

        private Focusable GigCard(RectTransform row, StoryGig gig, StoryEngine engine)
        {
            var state = engine.StateOf(gig);
            var record = engine.Progress.RecordOf(gig.Id);
            var venue = engine.Campaign.FindVenue(gig.VenueId);
            var focus = AddButton(row, StoryButtonStyle.Card, null, 0, () => OpenGigFromTour(gig));

            bool locked = state == StoryGigState.Locked;
            StoryUi.Text(row, 0.02f, 0.1f, 0.15f, 0.9f, gig.Number.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                72, true, locked ? StoryTheme.Faint : StoryTheme.Gold, TextAlignmentOptions.Center, false);
            StoryUi.Text(row, 0.17f, 0.6f, 0.74f, 0.92f, gig.Title, 38, true, locked ? StoryTheme.Faint : StoryTheme.Cream,
                TextAlignmentOptions.Left, false);
            string where = venue != null ? JoinDash(venue.Name, venue.City) : "";
            StoryUi.Text(row, 0.17f, 0.34f, 0.74f, 0.6f, where, 28, false, StoryTheme.Muted, TextAlignmentOptions.Left, false);
            string stamp = gig.Optional ? JoinDash("Side gig", gig.Stamp) : gig.Stamp;
            StoryUi.Text(row, 0.17f, 0.08f, 0.74f, 0.34f, stamp, 26, false, StoryUi.Alpha(StoryTheme.Gold, 0.85f),
                TextAlignmentOptions.Left, false);

            string status = locked ? "Locked" : state == StoryGigState.Won ? "Won" : "Open";
            var statusColor = locked ? StoryTheme.Faint : state == StoryGigState.Won ? StoryTheme.Won : StoryTheme.Gold;
            if (locked)
            {
                StoryArt.Icon(row, StoryArt.Lock, 0.8f, 0.55f, 0.86f, 0.85f, StoryTheme.Faint);
            }

            StoryUi.Text(row, 0.86f, 0.55f, 0.98f, 0.85f, status, 28, true, statusColor, TextAlignmentOptions.Right, false);
            if (record != null && record.BestStars > 0)
            {
                StoryArt.Icon(row, StoryArt.Star, 0.78f, 0.2f, 0.83f, 0.46f, StoryTheme.Gold);
                StoryUi.Text(row, 0.83f, 0.16f, 0.98f, 0.5f, record.BestStars + "/" + gig.MaxStars, 28, false, StoryTheme.Cream,
                    TextAlignmentOptions.Right, false);
            }

            if (record != null && record.ChallengeMet && gig.Challenge != null && !string.IsNullOrEmpty(gig.Challenge.Badge))
            {
                var badge = StoryUi.Area(row, 0.5f, 0.06f, 0.76f, 0.32f, "Badge");
                StoryArt.Badge(badge, gig.Challenge.Badge, StoryTheme.Gold, 24);
            }

            return focus;
        }

        private void OpenGigFromTour(StoryGig gig)
        {
            var engine = Engine;
            if (engine.StateOf(gig) != StoryGigState.Locked)
            {
                _tourFocus = gig;
                ShowGig(gig);
                return;
            }

            var missing = engine.MissingFor(gig);
            var names = new List<string>();
            for (int i = 0; i < missing.Count && i < 4; i++)
            {
                names.Add("\"" + missing[i].Title + "\"");
            }

            string body = names.Count == 0
                ? "This gig opens later in the story."
                : "Win " + string.Join(", ", names) + (missing.Count > 4 ? " and " + (missing.Count - 4) + " more" : "") + " first.";
            Notice("Not yet", body);
        }

        // ------------------------------------------------------------------------------------
        // Liner notes, evidence, about, credits
        // ------------------------------------------------------------------------------------

        private void ShowPage(Page page, Action back)
        {
            _page = page;
            _pageBack = back;
            if (page != Page.LinerNotes) _noteIndex = 0;
            Open(View.Notes, BuildPage);
        }

        private void BuildPage()
        {
            var campaign = Campaign;
            var progress = Engine != null ? Engine.Progress : null;
            StoryArt.Box(_artRoot, 0f, 0f, 1f, 1f, StoryTheme.Night);
            switch (_page)
            {
                case Page.LinerNotes:
                    BuildLinerNotes(campaign, progress);
                    break;
                case Page.Evidence:
                    BuildEvidence(campaign, progress);
                    break;
                default:
                {
                    bool credits = _page == Page.Credits;
                    var body = Header(credits ? "Credits" : "About", credits ? "" : campaign.Title, _pageBack);
                    string text = credits ? string.Join("\n\n", campaign.Credits) : Fill(campaign.About);
                    var reader = Reader(body, 0f, credits ? 0.13f : 0f, 1f, 1f, text, credits);
                    Top.Move = (dx, dy) => ScrollReader(reader, dx, dy, true);
                    if (credits)
                    {
                        var box = StoryUi.Fixed(body, 0.5f, 0.06f, 420f, 100f, "Continue");
                        AddButton(box, StoryButtonStyle.Primary, "Continue", 40, _pageBack);
                    }

                    break;
                }
            }
        }

        private void BuildLinerNotes(StoryCampaign campaign, StoryProgress progress)
        {
            var body = Header("Liner Notes", "Short histories, unlocked along the tour", _pageBack);
            var notes = campaign.LinerNotes;
            if (notes.Count == 0)
            {
                StoryUi.Text(body, 0.1f, 0.4f, 0.9f, 0.6f, "No liner notes in this story.", 40, false, StoryTheme.Muted, TextAlignmentOptions.Center);
                return;
            }

            _noteIndex = Mathf.Clamp(_noteIndex, 0, notes.Count - 1);
            var size = BodySize;
            RectTransform list, reading;
            float listHeight;
            if (Wide)
            {
                list = StoryUi.Area(body, 0f, 0f, 0.4f, 1f, "List");
                reading = StoryUi.Area(body, 0.41f, 0f, 1f, 1f, "Reading");
                listHeight = size.y;
            }
            else
            {
                list = StoryUi.Area(body, 0f, 0.56f, 1f, 1f, "List");
                reading = StoryUi.Area(body, 0f, 0f, 1f, 0.55f, "Reading");
                listHeight = size.y * 0.44f;
            }

            var scroll = StoryUi.Scroll(list, 0f, 0f, 1f, 1f, out var items);
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                bool have = progress != null && progress.LinerNotes.Contains(note.Id);
                bool unread = have && !progress.ReadLinerNotes.Contains(note.Id);
                var row = StoryUi.Row(items, i * 118f + 6f, 106f, M, M, "Note");
                int index = i;
                var focus = AddButton(row, StoryButtonStyle.Card, null, 0, () =>
                {
                    _noteIndex = index;
                    _keepFocus = index;
                    Rebuild(true);
                });
                InList(focus, scroll, row, listHeight);
                if (i == _noteIndex) StoryUi.Solid(row, 0f, 0.12f, 0.012f, 0.88f, StoryTheme.Gold);
                StoryUi.Text(row, 0.05f, 0.45f, 0.86f, 0.92f, have ? note.Title : "???", 34, true, have ? StoryTheme.Cream : StoryTheme.Faint,
                    TextAlignmentOptions.Left, false);
                StoryUi.Text(row, 0.05f, 0.08f, 0.95f, 0.45f, have ? note.Kicker : UnlockedBy(campaign, note.Id), 24, false,
                    StoryTheme.Muted, TextAlignmentOptions.Left, false);
                if (unread)
                {
                    var tag = StoryUi.Area(row, 0.86f, 0.5f, 0.98f, 0.88f, "New");
                    StoryArt.Badge(tag, "New", StoryTheme.Gold, 22);
                }
            }

            StoryUi.SetHeight(items, notes.Count * 118f + 12f);
            if (_layers[0].Index < 0) _layers[0].Index = _noteIndex;

            var chosen = notes[_noteIndex];
            bool readable = progress != null && progress.LinerNotes.Contains(chosen.Id);
            string text = readable
                ? chosen.Title + "\n" + chosen.Kicker + "\n\n" + Fill(chosen.Body)
                : "This note is still sealed.\n\n" + UnlockedBy(campaign, chosen.Id);
            var reader = Reader(reading, 0f, 0f, 1f, 1f, text, false);
            Top.Move = (dx, dy) => dx != 0 && ScrollReader(reader, 0, -dx, false);

            if (readable && progress.ReadLinerNotes.Add(chosen.Id))
            {
                Save();
            }
        }

        private static string UnlockedBy(StoryCampaign campaign, string id)
        {
            foreach (var gig in campaign.AllGigs)
            {
                if (gig.Rewards.Contains(id))
                {
                    return "Win \"" + gig.Title + "\" to unlock";
                }
            }

            return "Found somewhere along the tour";
        }

        private void BuildEvidence(StoryCampaign campaign, StoryProgress progress)
        {
            var body = Header(campaign.KeepsakeName, "What the tour has turned up so far", _pageBack);
            var keepsakes = campaign.Keepsakes;
            if (keepsakes.Count == 0)
            {
                StoryUi.Text(body, 0.1f, 0.4f, 0.9f, 0.6f, "Nothing to collect in this story.", 40, false, StoryTheme.Muted, TextAlignmentOptions.Center);
                return;
            }

            var size = BodySize;
            var scroll = StoryUi.Scroll(body, 0f, 0f, 1f, 1f, out var items);
            for (int i = 0; i < keepsakes.Count; i++)
            {
                var keepsake = keepsakes[i];
                bool have = progress != null && progress.Keepsakes.Contains(keepsake.Id);
                var row = StoryUi.Row(items, i * 164f + 6f, 150f, M, M, "Keepsake");
                var focus = AddButton(row, StoryButtonStyle.Card, null, 0, null);
                InList(focus, scroll, row, size.y);
                StoryUi.Text(row, 0.04f, 0.58f, 0.8f, 0.92f, have ? keepsake.Name : "???", 38, true, have ? StoryTheme.Cream : StoryTheme.Faint,
                    TextAlignmentOptions.Left, false);
                StoryUi.Text(row, 0.04f, 0.08f, 0.96f, 0.58f, have ? Fill(keepsake.Description) : "Still out there.", 28, false,
                    StoryTheme.Muted, TextAlignmentOptions.TopLeft);
                if (have && !string.IsNullOrEmpty(keepsake.Layer))
                {
                    var tag = StoryUi.Area(row, 0.8f, 0.6f, 0.97f, 0.9f, "Layer");
                    StoryArt.Badge(tag, keepsake.Layer, StoryTheme.LayerColor(keepsake.Layer), 24);
                }
            }

            StoryUi.SetHeight(items, keepsakes.Count * 164f + 12f);
        }

        /// <summary>A long text in its own scroll list, on a dark panel.</summary>
        private ScrollRect Reader(RectTransform parent, float x0, float y0, float x1, float y1, string text, bool centred)
        {
            var panel = StoryUi.Area(parent, x0, y0, x1, y1, "Reader");
            StoryUi.Inset(panel, M, 8f, M, 8f);
            StoryUi.Rounded(panel, StoryTheme.Panel, 2f);
            var scroll = StoryUi.Scroll(panel, 0f, 0f, 1f, 1f, out var items);
            var area = StoryUi.Row(items, 28f, 10f, 36f, 36f, "Text");
            var label = StoryUi.Reading(area, text, 34, StoryTheme.Cream);
            if (centred) label.alignment = TextAlignmentOptions.Top;

            // Width of the text in units: the panel's share of the body, less the insets
            var body = BodySize;
            float width = body.x * Mathf.Abs(x1 - x0) * (Wide || _page != Page.LinerNotes ? 1f : 1f) - 2f * M - 72f;
            if (_view == View.Notes && _page == Page.LinerNotes && Wide) width = body.x * 0.59f - 2f * M - 72f;
            float height = StoryUi.HeightOf(label, width) + 20f;
            area.offsetMin = new Vector2(36f, -28f - height);
            StoryUi.SetHeight(items, height + 60f);
            return scroll;
        }

        private bool ScrollReader(ScrollRect reader, int dx, int dy, bool alsoSideways)
        {
            if (reader == null) return false;
            int step = dy != 0 ? -dy : alsoSideways ? dx : 0;
            if (step == 0) return false;

            float view = ((RectTransform) reader.transform).rect.height;
            StoryUi.ScrollTo(reader, reader.content.anchoredPosition.y + step * view * 0.6f, view);
            return true;
        }
    }
}
