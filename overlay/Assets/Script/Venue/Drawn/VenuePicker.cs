// YARG Resurrected - choose the venue and the crowd from the song screen.
//
// The venue and crowd settings live in Settings > General, which is a long way from where a song
// is picked. On the instrument and difficulty screen (Quickplay and the rest) a VENUE bar now sits
// just above the SONG SETUP bar, saying where the next song will be played and whether the crowd
// is on. Tapping it opens a list: random (any, street and club, high class), every venue by
// region, or none; and the crowd on or off. The choice is the same setting as in Settings, saved
// straight away. With a controller, the Venue button in the help bar opens the same choices as a
// list. A new file on its own: nothing else is changed.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Core.Logging;
using YARG.Menu.DifficultySelect;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Settings;
using YARG.Story;

namespace YARG.Venue.Drawn
{
    public sealed class VenuePicker : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            try
            {
                var host = new GameObject("YARG Venue Picker");
                DontDestroyOnLoad(host);
                host.AddComponent<VenuePicker>();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "The venue picker could not start");
            }
        }

        private GameObject _bar;
        private TextMeshProUGUI _barText;
        private GameObject _panel;
        private RectTransform _items;
        private ScrollRect _scroll;
        private NavigationScheme _scheme;
        private bool _schemeOn;
        private bool _failed;
        private string _shownChoice;
        private bool _shownCrowd;

        private static VenuePicker _instance;

        /// <summary>True while the touch list of venues is open (other bars keep clear of it).</summary>
        public static bool PanelOpen => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

        /// <summary>True when a venue can be picked now (the instrument screen, not a story gig).</summary>
        public static bool CanOpen => SettingsManager.Settings != null && DifficultySelectMenu.Current != null &&
            !(StoryMode.Instance != null && StoryMode.Instance.GigUnderWay);

        /// <summary>The choices as a list the controller can work (the Venue button on the instrument screen).</summary>
        public static void OpenList()
        {
            if (!CanOpen || DialogManager.Instance == null)
            {
                return;
            }

            try
            {
                var dialog = DialogManager.Instance.ShowList("Where to play");
                FillList(dialog, 0);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "The venue list could not open");
            }
        }

        private static void FillList(YARG.Menu.Dialogs.ListDialog dialog, int select)
        {
            var settings = SettingsManager.Settings;
            string current = settings.VenueChoice.Value;
            dialog.ClearList();

            dialog.AddListButton("Dancing crowd: " + (settings.VenueCrowd.Value ? "ON" : "off"), () =>
            {
                settings.VenueCrowd.SetValueWithoutNotify(!settings.VenueCrowd.Value);
                SettingsManager.SaveSettings();
                FillList(dialog, 0);
            }, false);

            void Add(string text, string choice)
            {
                dialog.AddListButton((choice == current ? "> " : "") + text, () =>
                {
                    settings.VenueChoice.SetValueWithoutNotify(choice);
                    SettingsManager.SaveSettings();
                });
            }

            foreach (string choice in new[] { VenueCatalog.CHOICE_RANDOM, VenueCatalog.CHOICE_RANDOM_STREET, VenueCatalog.CHOICE_RANDOM_UPSCALE })
            {
                Add(VenueCatalog.Describe(choice), choice);
            }

            foreach (var venue in VenueCatalog.All)
            {
                Add(venue.Name + "   <alpha=#88>" + venue.Region + ", inspired by " + venue.InspiredBy, venue.Id);
            }

            Add(VenueCatalog.Describe(VenueCatalog.CHOICE_NONE), VenueCatalog.CHOICE_NONE);
            dialog.SelectListIndex(select);
        }

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            if (_failed)
            {
                return;
            }

            try
            {
                var settings = SettingsManager.Settings;
                // A story gig chooses its own stage: no bar then
                bool show = settings != null && DifficultySelectMenu.Current != null &&
                    !(StoryMode.Instance != null && StoryMode.Instance.GigUnderWay);
                if (show && _bar == null)
                {
                    Build();
                }

                if (_bar == null)
                {
                    return;
                }

                if (!show && _panel.activeSelf)
                {
                    Close();
                }

                _bar.SetActive(show && !_panel.activeSelf);
                if (show && (_shownChoice != settings.VenueChoice.Value || _shownCrowd != settings.VenueCrowd.Value))
                {
                    _shownChoice = settings.VenueChoice.Value;
                    _shownCrowd = settings.VenueCrowd.Value;
                    _barText.text = "VENUE   " + VenueCatalog.Describe(_shownChoice) + "   |   crowd " + (_shownCrowd ? "on" : "off");
                }
            }
            catch (Exception e)
            {
                _failed = true;
                YargLogger.LogException(e, "The venue picker stopped after an error");
                if (_bar != null) _bar.SetActive(false);
            }
        }

        private void Build()
        {
            var holder = new GameObject("Venue Picker Canvas");
            holder.transform.SetParent(transform, false);
            var canvas = holder.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Next to the Song Setup bar (95), under the game's dialogs
            canvas.sortingOrder = 94;
            var scaler = holder.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            holder.AddComponent<GraphicRaycaster>();
            var root = (RectTransform) holder.transform;

            // The bar: the same width and look as the SONG SETUP bar, just above it
            var bar = new GameObject("Venue bar", typeof(RectTransform));
            var rect = (RectTransform) bar.transform;
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0.04f, 0f);
            rect.anchorMax = new Vector2(0.96f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 236f);
            rect.sizeDelta = new Vector2(0f, 56f);
            var image = bar.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.78f);
            var button = bar.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Open);
            _barText = StoryUi.Text(StoryUi.Area(rect, 0.01f, 0.05f, 0.99f, 0.95f, "Label"), "VENUE", 28f, true, Color.white,
                TextAlignmentOptions.Center, false);
            _bar = bar;
            _bar.SetActive(false);

            var panel = StoryUi.Area(root, 0f, 0f, 1f, 1f, "Venue Panel");
            var scrim = panel.gameObject.AddComponent<Image>();
            scrim.color = new Color(0.02f, 0.02f, 0.03f, 0.96f);
            scrim.raycastTarget = true;
            StoryUi.Text(panel, 0.05f, 0.88f, 0.7f, 0.97f, "WHERE TO PLAY", 56f, true, StoryTheme.Gold, TextAlignmentOptions.Left, false);
            var done = StoryUi.Area(panel, 0.74f, 0.885f, 0.95f, 0.965f, "Done");
            StoryUi.Button(done, StoryButtonStyle.Primary, "Done", 34f, out _, out _).onClick.AddListener(Close);
            _scroll = StoryUi.Scroll(panel, 0.05f, 0.04f, 0.95f, 0.86f, out _items);
            _panel = panel.gameObject;
            _panel.SetActive(false);
        }

        private void Open()
        {
            try
            {
                Fill();
                _panel.SetActive(true);
                _bar.SetActive(false);
                if (Navigator.Instance != null && !_schemeOn)
                {
                    _scheme = new NavigationScheme(new List<NavigationScheme.Entry>
                    {
                        new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Close),
                    }, null, () => _schemeOn = false);
                    Navigator.Instance.PushScheme(_scheme);
                    _schemeOn = true;
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "The venue list could not open");
                Close();
            }
        }

        // The list: the crowd switch, the random choices, every venue by region, and none
        private void Fill()
        {
            foreach (Transform child in _items)
            {
                Destroy(child.gameObject);
            }

            var settings = SettingsManager.Settings;
            string current = settings.VenueChoice.Value;
            float y = 0f;
            const float rowHeight = 92f;

            AddRow(ref y, rowHeight, "Dancing crowd: " + (settings.VenueCrowd.Value ? "ON" : "off") + "  (tap to switch)", false, () =>
            {
                settings.VenueCrowd.SetValueWithoutNotify(!settings.VenueCrowd.Value);
                SettingsManager.SaveSettings();
                Fill();
            });
            y += 18f;

            foreach (string choice in new[] { VenueCatalog.CHOICE_RANDOM, VenueCatalog.CHOICE_RANDOM_STREET, VenueCatalog.CHOICE_RANDOM_UPSCALE })
            {
                string pick = choice;
                AddRow(ref y, rowHeight, VenueCatalog.Describe(pick), pick == current, () => Choose(pick));
            }

            string region = null;
            foreach (var venue in VenueCatalog.All)
            {
                if (venue.Region != region)
                {
                    region = venue.Region;
                    var heading = StoryUi.Row(_items, y + 10f, 56f, 8f, 8f, "Region");
                    StoryUi.Text(heading, region == "International" ? "INTERNATIONAL" : region.ToUpperInvariant(), 30f, true,
                        StoryTheme.Gold, TextAlignmentOptions.BottomLeft, false);
                    y += 70f;
                }

                var v = venue;
                AddRow(ref y, rowHeight, v.Name + "  -  " + v.InspiredBy, v.Id == current, () => Choose(v.Id));
            }

            y += 18f;
            AddRow(ref y, rowHeight, VenueCatalog.Describe(VenueCatalog.CHOICE_NONE), current == VenueCatalog.CHOICE_NONE,
                () => Choose(VenueCatalog.CHOICE_NONE));
            StoryUi.SetHeight(_items, y + 20f);
        }

        private void AddRow(ref float y, float height, string text, bool chosen, Action onTap)
        {
            var row = StoryUi.Row(_items, y, height - 10f, 0f, 0f, "Choice");
            var button = StoryUi.Button(row, chosen ? StoryButtonStyle.Primary : StoryButtonStyle.Normal,
                (chosen ? "> " : "") + text, 30f, out _, out var label);
            if (label != null) label.alignment = TextAlignmentOptions.Left;
            button.onClick.AddListener(() =>
            {
                try
                {
                    onTap();
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "The venue choice did not take");
                }
            });
            y += height;
        }

        private void Choose(string choice)
        {
            var setting = SettingsManager.Settings.VenueChoice;
            // Not through Value: that also pokes the settings menu, which is not open here
            setting.SetValueWithoutNotify(choice);
            SettingsManager.SaveSettings();
            Close();
        }

        private void Close()
        {
            if (_panel != null)
            {
                _panel.SetActive(false);
            }

            if (_schemeOn && Navigator.Instance != null)
            {
                _schemeOn = false;
                Navigator.Instance.PopScheme();
            }

            _shownChoice = null;
        }
    }
}
