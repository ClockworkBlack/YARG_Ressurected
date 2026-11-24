// YARG Resurrected - Story Mode screens.
//
// The small toolkit every story screen is built from: the colors, the two fonts, and plain
// factories for areas, text, rounded panels, buttons and scroll lists. Everything is laid out
// either in fractions of the parent (so it follows any screen shape) or in canvas units from the
// top of a list (so rows keep a finger-friendly height on every phone).
//
// Script text is plain: every text made here has rich text switched off, so a "<" in a line of
// dialogue or a band name is shown as it is and can never turn into markup.

using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Logging;
using YARG.Localization;
using YARG.Menu.Persistent;

namespace YARG.Story
{
    /// <summary>The look of a record sleeve: near-black, warm gold, cream type.</summary>
    internal static class StoryTheme
    {
        public static readonly Color Ink = new Color(0.035f, 0.03f, 0.03f, 1f);
        public static readonly Color Night = new Color(0.07f, 0.06f, 0.055f, 1f);
        public static readonly Color Panel = new Color(0.11f, 0.095f, 0.085f, 0.94f);
        public static readonly Color PanelLight = new Color(0.17f, 0.145f, 0.12f, 0.96f);
        public static readonly Color Scrim = new Color(0.02f, 0.015f, 0.01f, 0.78f);
        public static readonly Color Gold = new Color(0.96f, 0.76f, 0.31f, 1f);
        public static readonly Color GoldDeep = new Color(0.72f, 0.52f, 0.16f, 1f);
        public static readonly Color Cream = new Color(0.98f, 0.94f, 0.86f, 1f);
        public static readonly Color Muted = new Color(0.72f, 0.66f, 0.58f, 1f);
        public static readonly Color Faint = new Color(0.45f, 0.41f, 0.37f, 1f);
        public static readonly Color Won = new Color(0.55f, 0.85f, 0.55f, 1f);
        public static readonly Color Locked = new Color(0.38f, 0.35f, 0.33f, 1f);
        public static readonly Color Danger = new Color(0.85f, 0.32f, 0.27f, 1f);

        /// <summary>The narrator's extra lines, one per part of the band found again.</summary>
        public static Color LayerColor(string layer)
        {
            switch (layer)
            {
                case "voice": return new Color(0.95f, 0.42f, 0.40f, 1f);
                case "bass": return new Color(0.55f, 0.45f, 1.00f, 1f);
                case "guitar": return new Color(1.00f, 0.62f, 0.15f, 1f);
                case "drums": return new Color(0.20f, 0.80f, 0.72f, 1f);
                case "keys": return new Color(0.35f, 0.68f, 0.95f, 1f);
                default: return Cream;
            }
        }
    }

    internal enum StoryButtonStyle
    {
        /// <summary>Gold with dark type: the one main thing to do on a screen.</summary>
        Primary,

        /// <summary>A dark rounded panel with light type.</summary>
        Normal,

        /// <summary>A dark panel the caller fills with its own texts (cards, list rows).</summary>
        Card,
    }

    internal static class StoryUi
    {
        /// <summary>Rows and buttons are never shorter than this, so a thumb can hit them (1080p units).</summary>
        public const float TOUCH = 96f;

        // ------------------------------------------------------------------------------------
        // Fonts
        // ------------------------------------------------------------------------------------

        // The game's own headline (RedHat Display Bold) and body (Barlow) fonts live on the
        // loading screen, which is in the persistent scene for the whole run. Null means TMP's
        // default font, which is fine too.
        public static TMP_FontAsset HeadFont
        {
            get
            {
                var loading = LoadingScreen.Instance;
                return loading != null && loading.LoadingPhrase != null ? loading.LoadingPhrase.font : null;
            }
        }

        public static TMP_FontAsset BodyFont
        {
            get
            {
                var loading = LoadingScreen.Instance;
                return loading != null && loading.SubPhrase != null ? loading.SubPhrase.font : null;
            }
        }

        // ------------------------------------------------------------------------------------
        // Placing things
        // ------------------------------------------------------------------------------------

        /// <summary>An empty holder over a part of its parent, in fractions (x right, y up).</summary>
        public static RectTransform Area(Transform parent, float x0, float y0, float x1, float y1, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform) go.transform;
            rect.SetParent(parent, false);
            Place(rect, x0, y0, x1, y1);
            return rect;
        }

        public static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            rect.anchorMin = new Vector2(Mathf.Min(x0, x1), Mathf.Min(y0, y1));
            rect.anchorMax = new Vector2(Mathf.Max(x0, x1), Mathf.Max(y0, y1));
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Pulls the edges of a rect in by the given canvas units.</summary>
        public static RectTransform Inset(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>
        /// A row of a list: full width minus the side insets, <paramref name="top"/> units below the
        /// parent's top edge and <paramref name="height"/> units tall.
        /// </summary>
        public static RectTransform Row(Transform parent, float top, float height, float left, float right,
            string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform) go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>A fixed-size holder centred on a point of its parent (fractions), sized in units.</summary>
        public static RectTransform Fixed(Transform parent, float x, float y, float width, float height, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform) go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(x, y);
            rect.anchorMax = new Vector2(x, y);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        // ------------------------------------------------------------------------------------
        // Text
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// A text filling its rect. The size is set once here and never changed afterwards (the
        /// game's font scaler writes the first size it sees back on every scene load); long text
        /// shrinks to fit instead.
        /// </summary>
        public static TextMeshProUGUI Text(RectTransform rect, string text, float size, bool head, Color color,
            TextAlignmentOptions alignment, bool wrap = true)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var font = head ? HeadFont : BodyFont;
            if (font != null)
            {
                label.font = font;
            }

            label.richText = false;
            label.enableAutoSizing = true;
            label.fontSizeMax = size;
            label.fontSizeMin = Mathf.Min(size, Mathf.Max(22f, size * 0.5f));
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.text = text ?? "";
            return label;
        }

        public static TextMeshProUGUI Text(Transform parent, float x0, float y0, float x1, float y1, string text,
            float size, bool head, Color color, TextAlignmentOptions alignment, bool wrap = true)
        {
            return Text(Area(parent, x0, y0, x1, y1, "Text"), text, size, head, color, alignment, wrap);
        }

        /// <summary>A text that keeps its size and grows downwards: for long reading in a scroll list.</summary>
        public static TextMeshProUGUI Reading(RectTransform rect, string text, float size, Color color)
        {
            var label = Text(rect, text, size, false, color, TextAlignmentOptions.TopLeft);
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        /// <summary>The height a reading text needs at a given width, in units.</summary>
        public static float HeightOf(TextMeshProUGUI label, float width)
        {
            if (label == null || string.IsNullOrEmpty(label.text))
            {
                return 0f;
            }

            return label.GetPreferredValues(label.text, Mathf.Max(10f, width), 0f).y;
        }

        // ------------------------------------------------------------------------------------
        // Shapes, panels and buttons
        // ------------------------------------------------------------------------------------

        public static Image Solid(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            var image = Area(parent, x0, y0, x1, y1, "Box").gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A rounded panel filling the rect. Bigger <paramref name="sharpness"/> = smaller corners.</summary>
        public static Image Rounded(RectTransform rect, Color color, float sharpness = 1.6f)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = StoryArt.Disc;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = sharpness;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A rounded outline just outside the rect: the "this one has the focus" ring.</summary>
        public static Image Ring(RectTransform owner, Color color)
        {
            var rect = Area(owner, 0f, 0f, 1f, 1f, "Focus");
            Inset(rect, -7f, -7f, -7f, -7f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = StoryArt.RingSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.4f;
            image.color = color;
            image.raycastTarget = false;
            rect.gameObject.SetActive(false);
            return image;
        }

        /// <summary>
        /// A touchable panel. Returns the Button; the label (null for cards) comes back through
        /// <paramref name="label"/>.
        /// </summary>
        public static Button Button(RectTransform rect, StoryButtonStyle style, string text, float size,
            out Image background, out TextMeshProUGUI label)
        {
            Color fill = style == StoryButtonStyle.Primary ? StoryTheme.Gold
                : style == StoryButtonStyle.Card ? StoryTheme.Panel : StoryTheme.PanelLight;
            background = Rounded(rect, fill, style == StoryButtonStyle.Card ? 2.2f : 1.6f);
            background.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            button.colors = colors;

            // Pads never press UGUI buttons in this game; the story's own focus list does that.
            // Keep Unity's own selection out of the way so it does not tint a button forever.
            var navigation = button.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            button.navigation = navigation;

            label = null;
            if (style != StoryButtonStyle.Card && text != null)
            {
                var area = Area(rect, 0f, 0f, 1f, 1f, "Label");
                Inset(area, 16f, 6f, 16f, 6f);
                label = Text(area, text, size, true,
                    style == StoryButtonStyle.Primary ? StoryTheme.Ink : StoryTheme.Cream,
                    TextAlignmentOptions.Center, false);
            }

            return button;
        }

        /// <summary>A vertical scroll list. Rows go into <paramref name="items"/>; set its height with SetHeight.</summary>
        public static ScrollRect Scroll(Transform parent, float x0, float y0, float x1, float y1,
            out RectTransform items)
        {
            var area = Area(parent, x0, y0, x1, y1, "Scroll");
            // Something under the finger must be hit for a drag to reach the ScrollRect
            var catcher = area.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0.001f);
            var scroll = area.gameObject.AddComponent<ScrollRect>();

            var viewport = Area(area, 0f, 0f, 1f, 1f, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();

            // Never "Content": that name opts a subtree into the game's font scaler
            var go = new GameObject("Items", typeof(RectTransform));
            items = (RectTransform) go.transform;
            items.SetParent(viewport, false);
            items.anchorMin = new Vector2(0f, 1f);
            items.anchorMax = new Vector2(1f, 1f);
            items.pivot = new Vector2(0.5f, 1f);
            items.anchoredPosition = Vector2.zero;
            items.sizeDelta = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = items;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 60f;
            return scroll;
        }

        public static void SetHeight(RectTransform items, float height)
        {
            items.sizeDelta = new Vector2(0f, Mathf.Max(0f, height));
        }

        /// <summary>
        /// Scrolls so that a row (a direct child of the list's items, made with <see cref="Row"/>) is
        /// fully in view, with a little air around it.
        /// </summary>
        public static void Reveal(ScrollRect scroll, RectTransform row, float viewHeight)
        {
            if (scroll == null || row == null)
            {
                return;
            }

            var items = scroll.content;
            float top = -row.offsetMax.y;
            float bottom = -row.offsetMin.y;
            float position = items.anchoredPosition.y;
            const float air = 24f;
            if (top - air < position)
            {
                position = top - air;
            }
            else if (bottom + air > position + viewHeight)
            {
                position = bottom + air - viewHeight;
            }

            ScrollTo(scroll, position, viewHeight);
        }

        public static void ScrollTo(ScrollRect scroll, float position, float viewHeight)
        {
            var items = scroll.content;
            float most = Mathf.Max(0f, items.sizeDelta.y - viewHeight);
            scroll.StopMovement();
            items.anchoredPosition = new Vector2(items.anchoredPosition.x, Mathf.Clamp(position, 0f, most));
        }

        // ------------------------------------------------------------------------------------
        // Small helpers
        // ------------------------------------------------------------------------------------

        public static Color Hex(string text, Color fallback)
        {
            return !string.IsNullOrEmpty(text) && ColorUtility.TryParseHtmlString(text, out var color)
                ? color
                : fallback;
        }

        public static Color Shade(Color color, float brightness)
        {
            return new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
        }

        public static Color Alpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>"12,400" with the thousands marked, whatever the phone's language.</summary>
        public static string Number(long value)
        {
            return value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Duration(double seconds)
        {
            if (seconds <= 0)
            {
                return "";
            }

            int total = (int) Math.Round(seconds);
            return (total / 60) + ":" + (total % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------------------------
        // Help bar labels
        // ------------------------------------------------------------------------------------

        private static FieldInfo _mapField;
        private static bool _mapLooked;
        private static readonly Dictionary<string, string> _keys = new Dictionary<string, string>();

        /// <summary>
        /// A localization key that shows <paramref name="english"/> on the help bar. The game's
        /// labels only come from its language files, which story mode must not edit, so the key is
        /// added to the loaded table at run time. If that ever fails, the English text itself is
        /// used as the key: it still reads right, at the cost of a warning in the log.
        /// </summary>
        public static string Key(string english)
        {
            if (_keys.TryGetValue(english, out string known))
            {
                return known;
            }

            string key = "Menu.Story." + english.Replace(" ", "");
            try
            {
                if (LocalizationManager.TryGetLocalizedKey(key, out _))
                {
                    _keys[english] = key;
                    return key;
                }

                if (!_mapLooked)
                {
                    _mapLooked = true;
                    _mapField = typeof(LocalizationManager).GetField("_localizationMap",
                        BindingFlags.NonPublic | BindingFlags.Static);
                }

                if (_mapField != null && _mapField.GetValue(null) is Dictionary<string, string> map)
                {
                    map[key] = english;
                    _keys[english] = key;
                    return key;
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode could not add a help bar label");
            }

            _keys[english] = english;
            return english;
        }

        // ------------------------------------------------------------------------------------
        // Idle dimmer
        // ------------------------------------------------------------------------------------

        private static MethodInfo _resetIdle;
        private static bool _idleLooked;

        /// <summary>
        /// The game dims the screen after three minutes without pad or keyboard input, and taps do
        /// not count. Someone reading a scene by touch is not idle, so every tap tells it so.
        /// </summary>
        public static void KeepAwake()
        {
            try
            {
                var dimmer = IdleDimmer.Instance;
                if (dimmer == null)
                {
                    return;
                }

                if (!_idleLooked)
                {
                    _idleLooked = true;
                    _resetIdle = typeof(IdleDimmer).GetMethod("ResetTimer", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                _resetIdle?.Invoke(dimmer, null);
            }
            catch (Exception)
            {
                // Nothing to do: the dimmer is only a convenience
                _resetIdle = null;
            }
        }
    }
}
