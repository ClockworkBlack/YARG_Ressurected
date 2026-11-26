// YARG Resurrected - a pop-out on the right of the menus: a light blue box explaining the option
// under the cursor.
//
// (It also used to show an artist card, a picture and a short bio looked up on Wikipedia. That
// was taken out; ShowArtist and HideArtist are kept, doing nothing, so the menus that call them
// stay as they are.)
using System;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Game;
using YARG.Core.Logging;

namespace YARG.Menu.Persistent
{
    public sealed class MenuInfoPanel : MonoBehaviour
    {
        private static MenuInfoPanel _instance;

        // The explanation box: light blue, white writing
        private static readonly Color HintBlue = new(0.16f, 0.55f, 0.90f, 0.96f);

        private RectTransform _hintBox;
        private TextMeshProUGUI _hintTitle;
        private TextMeshProUGUI _hintText;

        private static MenuInfoPanel Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Menu Info Panel");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<MenuInfoPanel>();
                    _instance.Build();
                }
                return _instance;
            }
        }

        // ------------------------------------------------------------------ the explanation box

        /// <summary>Shows a light blue box on the right explaining what is under the cursor.</summary>
        public static void ShowHint(string title, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                HideHint();
                return;
            }

            var panel = Instance;
            panel._hintTitle.text = title ?? string.Empty;
            panel._hintTitle.gameObject.SetActive(!string.IsNullOrEmpty(title));
            panel._hintText.text = text;
            panel._hintBox.gameObject.SetActive(true);
        }

        public static void HideHint()
        {
            if (_instance != null) _instance._hintBox.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ artist cards (taken out)

        /// <summary>Does nothing: artist cards were taken out.</summary>
        public static void ShowArtist(string artist)
        {
        }

        /// <summary>Does nothing: artist cards were taken out.</summary>
        public static void HideArtist()
        {
        }

        public static void HideAll()
        {
            HideHint();
        }

        // The pictures and bios the artist cards kept on the phone are no longer used
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ForgetArtistCards()
        {
            string folder;
            try
            {
                folder = Path.Combine(Application.persistentDataPath, "artist_cards");
            }
            catch (Exception)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                }
                catch (Exception)
                {
                    // Tried again next time
                }
            });
        }

        /// <summary>The first name in "A feat. B", "A & B", "A, B", "A x B".</summary>
        public static string FirstArtist(string artists)
        {
            if (string.IsNullOrWhiteSpace(artists)) return null;
            string s = artists;
            foreach (string cut in new[] { " feat. ", " feat ", " ft. ", " ft ", " featuring ", " & ", ", ", " x ", " X ", " and ", " with ", " vs. ", " vs " })
            {
                int at = s.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
                if (at > 0) s = s.Substring(0, at);
            }
            return s.Trim();
        }

        // ------------------------------------------------------------------ building it

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over the menus; dialogs and the help bar stay usable underneath since nothing here takes touches
            canvas.sortingOrder = 40;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            // The explanation box: right side
            _hintBox = Area(transform, 0.60f, 0.14f, 0.985f, 0.49f, "Explanation");
            Fill(_hintBox, HintBlue);
            _hintTitle = Text(_hintBox, 0.05f, 0.74f, 0.95f, 0.95f, 38, TextAlignmentOptions.BottomLeft);
            _hintTitle.fontStyle = FontStyles.Bold;
            _hintTitle.color = Color.white;
            _hintTitle.textWrappingMode = TextWrappingModes.NoWrap;
            _hintTitle.overflowMode = TextOverflowModes.Ellipsis;
            _hintText = Text(_hintBox, 0.05f, 0.06f, 0.95f, 0.72f, 30, TextAlignmentOptions.TopLeft);
            _hintText.color = Color.white;
            _hintText.enableAutoSizing = true;
            _hintText.fontSizeMin = 20;
            _hintText.fontSizeMax = 32;
            _hintText.overflowMode = TextOverflowModes.Ellipsis;
            _hintBox.gameObject.SetActive(false);
        }

        private static RectTransform Area(Transform parent, float x0, float y0, float x1, float y1, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static void Fill(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static TextMeshProUGUI Text(Transform parent, float x0, float y0, float x1, float y1, float size,
            TextAlignmentOptions alignment)
        {
            var rect = Area(parent, x0, y0, x1, y1, "Text");
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        // ------------------------------------------------------------------ what the options mean

        /// <summary>What a song modifier does, in a sentence or two.</summary>
        public static string Explain(Modifier modifier) => modifier switch
        {
            Modifier.AllStrums => "Every note has to be strummed: no hammer-ons, pull-offs or taps.",
            Modifier.AllHopos => "Every note can be played as a hammer-on or pull-off: press the fret without strumming once you're on a run.",
            Modifier.AllTaps => "Every note is a tap: just press the fret (or the chord) at the right time. No strumming at all. Great on a gamepad or for learning a song.",
            Modifier.HoposToTaps => "Hammer-ons and pull-offs become taps, so they can be played without strumming even after a miss.",
            Modifier.TapsToHopos => "Tap notes become hammer-ons and pull-offs: they need a strummed note before them.",
            Modifier.NoteShuffle => "The notes are moved to random frets. The rhythm stays the same.",
            Modifier.NoKicks => "Drums: the bass drum notes are taken out.",
            Modifier.UnpitchedOnly => "Vocals: only the timing matters, not the note you sing. Talk, rap or sing at any pitch.",
            Modifier.NoDynamics => "Drums: accent and ghost notes count as normal hits.",
            Modifier.NoVocalPercussion => "Vocals: the tambourine and cowbell parts between lines are taken out.",
            Modifier.RangeCompress => "Pro keys: the highway doesn't shift range during the song.",
            Modifier.OpensToGreens => "Open notes become green notes.",
            Modifier.VocalsChallenge => "Vocals: you have to sing the song. Tighter on pitch, more of each line, and every note counts; breath and noise never do. Stricter from Easy to Expert.",
            _ => string.Empty,
        };

        /// <summary>What an engine preset (Profile > Engine) changes.</summary>
        public static string ExplainEngine(string presetName) => presetName switch
        {
            "Casual" => "Casual: bigger timing windows, so notes count even when you're a little early or late, and strumming is more forgiving. Best for gamepads, phones and new players.",
            "Precision" => "Precision: tight timing windows for players who want every hit to be exact.",
            "Solo Taps" => "Solo Taps: guitar solos can be played without strumming.",
            "Default" => "Default: the normal timing windows.",
            _ => presetName + ": a custom engine preset (timing windows and strumming rules).",
        };
    }
}
