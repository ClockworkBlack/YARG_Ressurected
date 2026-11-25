using UnityEngine;
using UnityEngine.UI;

namespace YARG.Venue.Drawn
{
    /// <summary>
    /// The few shapes everything in a drawn venue is built from, and helpers that place them with
    /// coordinates from 0 to 1 inside their parent (x to the right, y up), so a venue looks the same
    /// at any screen size.
    /// </summary>
    internal static class VenueShapes
    {
        private static Sprite _disc;
        private static Sprite _beam;

        /// <summary>A soft-edged white disc. Drawn "sliced" it becomes a pill or rounded bar of any size.</summary>
        public static Sprite Disc
        {
            get
            {
                if (_disc != null) return _disc;

                const int size = 64;
                var pixels = new Color32[size * size];
                float center = (size - 1) / 2f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                        float alpha = Mathf.Clamp01(31f - distance);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte) Mathf.RoundToInt(alpha * 255f));
                    }
                }

                var texture = NewTexture(size, size, pixels, "VenueDisc");
                _disc = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(31, 31, 31, 31));
                _disc.hideFlags = HideFlags.HideAndDontSave;
                return _disc;
            }
        }

        /// <summary>A stage light's beam: a cone that is narrow and bright at the top and fades as it widens.</summary>
        public static Sprite Beam
        {
            get
            {
                if (_beam != null) return _beam;

                const int width = 64, height = 128;
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    // 0 at the lamp (top row), 1 at the far end
                    float along = 1f - y / (float) (height - 1);
                    float halfWidth = Mathf.Lerp(0.05f, 0.5f, along);
                    for (int x = 0; x < width; x++)
                    {
                        float across = Mathf.Abs(x / (float) (width - 1) - 0.5f);
                        float edge = Mathf.Clamp01(1f - across / halfWidth);
                        float alpha = edge * edge * (1f - along) * (1f - along * 0.4f);
                        pixels[y * width + x] = new Color32(255, 255, 255, (byte) Mathf.RoundToInt(alpha * 255f));
                    }
                }

                var texture = NewTexture(width, height, pixels, "VenueBeam");
                _beam = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 1f), 100f);
                _beam.hideFlags = HideFlags.HideAndDontSave;
                return _beam;
            }
        }

        /// <summary>A top-to-bottom blend of two colors, for skies and walls.</summary>
        public static Texture2D Gradient(Color top, Color bottom)
        {
            const int height = 64;
            var pixels = new Color32[height];
            for (int y = 0; y < height; y++)
            {
                pixels[y] = Color.Lerp(bottom, top, y / (float) (height - 1));
            }

            return NewTexture(1, height, pixels, "VenueGradient");
        }

        private static Texture2D NewTexture(int width, int height, Color32[] pixels, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>An empty holder covering the given part of its parent.</summary>
        public static RectTransform Holder(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform) holder.transform;
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

        /// <summary>A plain rectangle.</summary>
        public static Image Box(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            var image = NewImage(parent, "Box", x0, y0, x1, y1, color);
            return image;
        }

        /// <summary>A circle or oval filling the given box.</summary>
        public static Image Oval(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            var image = NewImage(parent, "Oval", x0, y0, x1, y1, color);
            image.sprite = Disc;
            return image;
        }

        /// <summary>
        /// A true circle on a full-screen parent: the radius is a share of the screen's width, and the
        /// height is worked out from the screen's shape so it isn't squashed.
        /// </summary>
        public static Image Circle(Transform parent, float x, float y, float radius, Color color)
        {
            float aspect = Screen.height > 0 ? Screen.width / (float) Screen.height : 16f / 9f;
            return Oval(parent, x - radius, y - radius * aspect, x + radius, y + radius * aspect, color);
        }

        /// <summary>A bar with fully rounded ends (an arm, a column, a neon tube).</summary>
        public static Image Pill(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            var image = NewImage(parent, "Pill", x0, y0, x1, y1, color);
            image.sprite = Disc;
            image.type = Image.Type.Sliced;
            // Corners as big as they can be: Unity shrinks them to half the short side
            image.pixelsPerUnitMultiplier = 0.02f;
            return image;
        }

        /// <summary>A light beam hanging from (x, y), pointing down, as long and wide as given.</summary>
        public static Image LightBeam(Transform parent, float x, float y, float length, float width, Color color)
        {
            var image = NewImage(parent, "Beam", x - width / 2f, y - length, x + width / 2f, y, color);
            image.sprite = Beam;
            image.rectTransform.pivot = new Vector2(0.5f, 1f);
            Place(image.rectTransform, x - width / 2f, y - length, x + width / 2f, y);
            return image;
        }

        private static Image NewImage(Transform parent, string name, float x0, float y0, float x1, float y1, Color color)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform) holder.transform;
            rect.SetParent(parent, false);
            Place(rect, x0, y0, x1, y1);

            var image = holder.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Color Shade(Color color, float brightness)
        {
            return new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
        }

        public static Color Alpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }
}
