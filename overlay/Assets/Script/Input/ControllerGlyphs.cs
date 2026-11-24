using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Input;

namespace YARG.Input
{
    /// <summary>
    /// Button icons drawn in code for the controller chosen in Settings > General > Controller Layout,
    /// so every prompt in the game shows the real button: Cross, Circle, Triangle, Square and L1 on a
    /// PlayStation pad; A / B / X / Y and LB (or L) on Xbox and Nintendo pads.
    ///
    /// Each icon is a white disc with the symbol cut out of it. The game tints icons with the action's
    /// color, so Confirm is a green disc with a cross-shaped hole, Back a red disc with a circle, etc.
    /// </summary>
    public static class ControllerGlyphs
    {
        private const int SIZE = 96;
        private const float DISC_RADIUS = 46f;

        private static readonly Dictionary<(ControllerLayoutType, PadButton), Sprite> _cache = new();

        // 5x7 letters, top row first
        private static readonly Dictionary<char, string[]> _font = new()
        {
            ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            ['X'] = new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
            ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
            ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
        };

        /// <summary>
        /// The icon for a menu action on the current controller, or null when the game's own icon
        /// should be used (no gamepad connected, the setting is off, or the action is a direction,
        /// Start or Select).
        /// </summary>
        public static Sprite GetSprite(MenuAction action)
        {
            if (!ControllerLayout.ShowButtonNames)
            {
                return null;
            }

            if (ControllerLayout.ButtonFor(action) is not { } button ||
                button is PadButton.Start or PadButton.Select)
            {
                return null;
            }

            var key = (ControllerLayout.Current, button);
            if (_cache.TryGetValue(key, out var sprite) && sprite != null)
            {
                return sprite;
            }

            sprite = Build(key.Item1, button);
            _cache[key] = sprite;
            return sprite;
        }

        public static bool HasSprite(MenuAction action) => GetSprite(action) != null;

        private static Sprite Build(ControllerLayoutType layout, PadButton button)
        {
            bool playStation = layout is ControllerLayoutType.PlayStation or ControllerLayoutType.PlayStationJapan;

            // What to cut out of the disc: a PlayStation shape, or letters
            string text = null;
            if (!playStation || button == PadButton.LeftShoulder)
            {
                text = layout switch
                {
                    ControllerLayoutType.Xbox => button switch
                    {
                        PadButton.South => "A", PadButton.East => "B", PadButton.North => "Y", PadButton.West => "X",
                        _ => "LB",
                    },
                    ControllerLayoutType.Nintendo => button switch
                    {
                        PadButton.South => "B", PadButton.East => "A", PadButton.North => "X", PadButton.West => "Y",
                        _ => "L",
                    },
                    _ => "L1",
                };
            }

            var pixels = new Color32[SIZE * SIZE];
            float center = (SIZE - 1) / 2f;
            for (int py = 0; py < SIZE; py++)
            {
                for (int px = 0; px < SIZE; px++)
                {
                    // x right, y up, origin in the middle
                    float x = px - center;
                    float y = py - center;

                    float disc = Coverage(Mathf.Sqrt(x * x + y * y) - DISC_RADIUS);
                    float cut = text != null ? TextCoverage(text, x, y) : ShapeCoverage(button, x, y);
                    byte alpha = (byte) Mathf.RoundToInt(255f * disc * (1f - cut));
                    pixels[py * SIZE + px] = new Color32(255, 255, 255, alpha);
                }
            }

            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
            {
                name = $"ControllerGlyph_{layout}_{button}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // 1 inside (distance below 0), 0 outside, one pixel of smoothing on the edge
        private static float Coverage(float signedDistance) => Mathf.Clamp01(0.5f - signedDistance);

        private static float ShapeCoverage(PadButton button, float x, float y)
        {
            const float HALF_STROKE = 5f;

            float distance;
            switch (button)
            {
                case PadButton.South: // Cross
                    distance = Mathf.Min(
                        SegmentDistance(x, y, -19f, -19f, 19f, 19f),
                        SegmentDistance(x, y, -19f, 19f, 19f, -19f));
                    break;
                case PadButton.East: // Circle
                    distance = Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 22f);
                    break;
                case PadButton.North: // Triangle, point up
                    distance = Mathf.Min(
                        SegmentDistance(x, y, 0f, 25f, -24f, -16f),
                        Mathf.Min(
                            SegmentDistance(x, y, -24f, -16f, 24f, -16f),
                            SegmentDistance(x, y, 24f, -16f, 0f, 25f)));
                    break;
                default: // Square
                    distance = Mathf.Min(
                        Mathf.Min(
                            SegmentDistance(x, y, -20f, -20f, 20f, -20f),
                            SegmentDistance(x, y, 20f, -20f, 20f, 20f)),
                        Mathf.Min(
                            SegmentDistance(x, y, 20f, 20f, -20f, 20f),
                            SegmentDistance(x, y, -20f, 20f, -20f, -20f)));
                    break;
            }

            return Coverage(distance - HALF_STROKE);
        }

        private static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            float cx = ax + t * dx - x, cy = ay + t * dy - y;
            return Mathf.Sqrt(cx * cx + cy * cy);
        }

        private static float TextCoverage(string text, float x, float y)
        {
            // One letter is drawn big, two letters smaller with a one-cell gap
            float cell = text.Length == 1 ? 8f : 6f;
            float width = (text.Length * 5 + (text.Length - 1)) * cell;
            float height = 7 * cell;

            float left = x + width / 2f;   // 0 at the left edge of the text
            float top = height / 2f - y;   // 0 at the top edge of the text
            if (left < 0f || left >= width || top < 0f || top >= height)
            {
                return 0f;
            }

            int column = (int) (left / cell);
            int row = (int) (top / cell);
            int letter = column / 6;
            int letterColumn = column % 6;
            if (letter >= text.Length || letterColumn >= 5)
            {
                return 0f;
            }

            return _font.TryGetValue(text[letter], out var rows) && rows[row][letterColumn] == '1' ? 1f : 0f;
        }
    }
}
