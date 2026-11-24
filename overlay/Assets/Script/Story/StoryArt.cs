// YARG Resurrected - Story Mode screens.
//
// Everything the story shows is drawn in code from a handful of generated sprites (a soft disc,
// a rounded outline, a hoop, a star, a glow, a light beam) and plain colored Images: the rooms
// behind the scenes, the people talking, the narrator's waveform, stars, badges and the tour map.
// No art files, so nothing can go missing from the build.
//
// Shapes are placed in fractions of their holder. Holders that must stay round (portraits, reels,
// map dots) are square and sized in canvas units. Small animations (lights breathing, the
// waveform, blinking) are driven by one StoryAnim per screen, ticked from the screen's Update.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Story
{
    /// <summary>The moving parts of one screen. Filled while building, ticked every frame, dropped with the screen.</summary>
    internal sealed class StoryAnim
    {
        private struct Pulse
        {
            public Graphic Graphic;
            public float Low, High, Speed, Offset;
        }

        private struct Turn
        {
            public RectTransform Rect;
            public float Center, Range, Speed, Offset;
        }

        private struct Bar
        {
            public RectTransform Rect;
            public float Phase, Weight;
        }

        private struct Dot
        {
            public RectTransform Rect;
            public float X, Phase, Amplitude, Speed;
        }

        private struct Face
        {
            public RectTransform EyeLeft, EyeRight, Mouth;
            public float Offset;
        }

        private readonly List<Pulse> _pulses = new List<Pulse>();
        private readonly List<Turn> _turns = new List<Turn>();
        private readonly List<Bar> _bars = new List<Bar>();
        private readonly List<Dot> _dots = new List<Dot>();
        private readonly List<Face> _faces = new List<Face>();

        /// <summary>0 = quiet, 1 = someone is speaking: drives the waveform and the mouths.</summary>
        public float Energy = 0.3f;

        private float _energy = 0.3f;

        public void AddPulse(Graphic graphic, float low, float high, float speed, float offset)
        {
            // The pulse goes through the renderer's alpha, not the color: a new color makes Unity
            // rebuild the shape's mesh, and there are dozens of these pulsing every frame
            var color = graphic.color;
            color.a = 1f;
            graphic.color = color;
            graphic.canvasRenderer.SetAlpha(Mathf.Lerp(low, high, 0.5f));
            _pulses.Add(new Pulse { Graphic = graphic, Low = low, High = high, Speed = speed, Offset = offset });
        }

        /// <summary>Swings (or with a zero range and a speed, spins) a rect about its pivot.</summary>
        public void AddTurn(RectTransform rect, float center, float range, float speed, float offset)
        {
            _turns.Add(new Turn { Rect = rect, Center = center, Range = range, Speed = speed, Offset = offset });
        }

        public void AddBar(RectTransform rect, float phase, float weight)
        {
            _bars.Add(new Bar { Rect = rect, Phase = phase, Weight = weight });
        }

        public void AddDot(RectTransform rect, float x, float phase, float amplitude, float speed)
        {
            _dots.Add(new Dot { Rect = rect, X = x, Phase = phase, Amplitude = amplitude, Speed = speed });
        }

        public void AddFace(RectTransform eyeLeft, RectTransform eyeRight, RectTransform mouth, float offset)
        {
            _faces.Add(new Face { EyeLeft = eyeLeft, EyeRight = eyeRight, Mouth = mouth, Offset = offset });
        }

        public void Tick(float time, float delta)
        {
            _energy = Mathf.MoveTowards(_energy, Energy, delta * 2.5f);

            for (int i = 0; i < _pulses.Count; i++)
            {
                var pulse = _pulses[i];
                if (pulse.Graphic == null) continue;

                float wave = 0.5f + 0.5f * Mathf.Sin((time * pulse.Speed + pulse.Offset) * Mathf.PI * 2f);
                pulse.Graphic.canvasRenderer.SetAlpha(Mathf.Lerp(pulse.Low, pulse.High, wave));
            }

            for (int i = 0; i < _turns.Count; i++)
            {
                var turn = _turns[i];
                if (turn.Rect == null) continue;

                float angle = turn.Range > 0f
                    ? turn.Center + Mathf.Sin((time * turn.Speed + turn.Offset) * Mathf.PI * 2f) * turn.Range
                    : turn.Center + time * turn.Speed * 360f;
                turn.Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            // The narrator is a drum break: the bars kick on the beat and ring out between beats
            float beat = Mathf.Repeat(time * 1.75f, 1f);
            float kick = Mathf.Pow(1f - beat, 3f);
            for (int i = 0; i < _bars.Count; i++)
            {
                var bar = _bars[i];
                if (bar.Rect == null) continue;

                float noise = Mathf.Abs(Mathf.Sin(time * 3.1f + bar.Phase) * 0.6f + Mathf.Sin(time * 7.7f + bar.Phase * 2.3f) * 0.4f);
                float height = bar.Weight * (0.08f + _energy * (0.35f + 0.65f * kick) * (0.25f + 0.75f * noise));
                bar.Rect.localScale = new Vector3(1f, Mathf.Max(0.04f, height), 1f);
            }

            for (int i = 0; i < _dots.Count; i++)
            {
                var dot = _dots[i];
                if (dot.Rect == null) continue;

                float envelope = Mathf.Sin(dot.X * Mathf.PI);
                float y = dot.Amplitude * envelope * (0.3f + 0.7f * _energy) *
                    Mathf.Sin(dot.X * 9f + time * dot.Speed + dot.Phase);
                dot.Rect.anchoredPosition = new Vector2(0f, y);
            }

            for (int i = 0; i < _faces.Count; i++)
            {
                var face = _faces[i];
                if (face.EyeLeft == null) continue;

                // A blink every few seconds, and a moving mouth while the line is being typed
                bool blink = Mathf.Repeat(time / 3.7f + face.Offset, 1f) < 0.035f;
                var eyes = new Vector3(1f, blink ? 0.12f : 1f, 1f);
                face.EyeLeft.localScale = eyes;
                face.EyeRight.localScale = eyes;
                float talk = _energy > 0.6f ? 1f + 0.8f * Mathf.Abs(Mathf.Sin(time * 13f)) : 1f;
                face.Mouth.localScale = new Vector3(1f, talk, 1f);
            }
        }
    }

    internal static class StoryArt
    {
        // ------------------------------------------------------------------------------------
        // Generated sprites. Kept for the life of the app (Unity unloads unused assets at every
        // scene change, so they are marked to survive) and rebuilt if Unity destroyed them anyway.
        // ------------------------------------------------------------------------------------

        private static Sprite _disc, _ring, _hoop, _star, _glow, _beam, _lock, _check;

        /// <summary>A soft-edged white disc. Drawn "sliced" it becomes a rounded panel or a pill of any size.</summary>
        public static Sprite Disc => _disc != null ? _disc : _disc = Make("StoryDisc", 64, 64, (x, y) =>
            Mathf.Clamp01(31f - Mathf.Sqrt(x * x + y * y)), new Vector4(31, 31, 31, 31));

        /// <summary>A rounded-rectangle outline, for focus rings and neon frames (draw sliced).</summary>
        public static Sprite RingSprite => _ring != null ? _ring : _ring = Make("StoryRing", 64, 64, (x, y) =>
        {
            float d = RoundedBox(x, y, 31f, 26f);
            return Coverage(d) * (1f - Coverage(d + 7f));
        }, new Vector4(31, 31, 31, 31));

        /// <summary>A thin circle outline.</summary>
        public static Sprite Hoop => _hoop != null ? _hoop : _hoop = Make("StoryHoop", 96, 96, (x, y) =>
            Coverage(Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 43f) - 3f), Vector4.zero);

        /// <summary>A soft glow, bright in the middle and fading out.</summary>
        public static Sprite Glow => _glow != null ? _glow : _glow = Make("StoryGlow", 64, 64, (x, y) =>
        {
            float t = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) / 32f);
            return t * t;
        }, Vector4.zero);

        /// <summary>A five-point star.</summary>
        public static Sprite Star => _star != null ? _star : _star = Make("StoryStar", 64, 64, (x, y) =>
        {
            float px = x, py = y + 2f;
            bool inside = false;
            float distance = float.MaxValue;
            for (int i = 0, j = 9; i < 10; j = i++)
            {
                StarPoint(i, out float ax, out float ay);
                StarPoint(j, out float bx, out float by);
                if ((ay > py) != (by > py) && px < (bx - ax) * (py - ay) / (by - ay) + ax)
                {
                    inside = !inside;
                }

                distance = Mathf.Min(distance, Segment(px, py, ax, ay, bx, by));
            }

            return Mathf.Clamp01(0.5f + (inside ? distance : -distance));
        }, Vector4.zero);

        /// <summary>A padlock.</summary>
        public static Sprite Lock => _lock != null ? _lock : _lock = Make("StoryLock", 64, 64, (x, y) =>
        {
            float body = RoundedBox(x, y + 9f, 20f, 5f);
            float shackle = Mathf.Abs(Mathf.Sqrt(x * x + (y - 6f) * (y - 6f)) - 12f) - 3.5f;
            if (y < 4f) shackle = Mathf.Max(shackle, 1f);
            return Mathf.Max(Coverage(body), Coverage(shackle));
        }, Vector4.zero);

        /// <summary>A tick.</summary>
        public static Sprite Check => _check != null ? _check : _check = Make("StoryCheck", 64, 64, (x, y) =>
        {
            float d = Mathf.Min(Segment(x, y, -18f, 0f, -6f, -13f), Segment(x, y, -6f, -13f, 20f, 15f));
            return Coverage(d - 5f);
        }, Vector4.zero);

        /// <summary>A stage light's beam: narrow and bright at the lamp (top), fading as it widens.</summary>
        public static Sprite Beam
        {
            get
            {
                if (_beam != null) return _beam;

                const int width = 64, height = 128;
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    float along = 1f - y / (float) (height - 1);
                    float half = Mathf.Lerp(0.05f, 0.5f, along);
                    for (int x = 0; x < width; x++)
                    {
                        float across = Mathf.Abs(x / (float) (width - 1) - 0.5f);
                        float edge = Mathf.Clamp01(1f - across / half);
                        float alpha = edge * edge * (1f - along) * (1f - along * 0.4f);
                        pixels[y * width + x] = new Color32(255, 255, 255, (byte) Mathf.RoundToInt(alpha * 255f));
                    }
                }

                _beam = Sprite.Create(NewTexture(width, height, pixels, "StoryBeam"), new Rect(0, 0, width, height),
                    new Vector2(0.5f, 1f), 100f);
                _beam.hideFlags = HideFlags.HideAndDontSave;
                return _beam;
            }
        }

        private static void StarPoint(int i, out float x, out float y)
        {
            float angle = Mathf.PI / 2f + i * Mathf.PI / 5f;
            float radius = i % 2 == 0 ? 30f : 12.5f;
            x = Mathf.Cos(angle) * radius;
            y = Mathf.Sin(angle) * radius;
        }

        // 1 inside (distance below 0), 0 outside, one pixel of smoothing on the edge
        private static float Coverage(float distance) => Mathf.Clamp01(0.5f - distance);

        private static float RoundedBox(float x, float y, float half, float radius)
        {
            float qx = Mathf.Abs(x) - (half - radius);
            float qy = Mathf.Abs(y) - (half - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static float Segment(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            float cx = ax + t * dx - x, cy = ay + t * dy - y;
            return Mathf.Sqrt(cx * cx + cy * cy);
        }

        /// <summary>Makes a white sprite whose alpha is given per pixel, with (0, 0) at the centre.</summary>
        private static Sprite Make(string name, int width, int height, Func<float, float, float> alpha, Vector4 border)
        {
            var pixels = new Color32[width * height];
            float cx = (width - 1) / 2f, cy = (height - 1) / 2f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float a = Mathf.Clamp01(alpha(x - cx, y - cy));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte) Mathf.RoundToInt(a * 255f));
                }
            }

            var sprite = Sprite.Create(NewTexture(width, height, pixels, name), new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
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

        // Gradients are cached by their two colors: the same rooms are drawn again and again
        private static readonly Dictionary<ulong, Texture2D> _gradients = new Dictionary<ulong, Texture2D>();

        private static Texture2D GradientTexture(Color top, Color bottom)
        {
            Color32 a = top, b = bottom;
            ulong key = ((ulong) a.r << 56) | ((ulong) a.g << 48) | ((ulong) a.b << 40) | ((ulong) a.a << 32) |
                ((ulong) b.r << 24) | ((ulong) b.g << 16) | ((ulong) b.b << 8) | b.a;
            if (_gradients.TryGetValue(key, out var texture) && texture != null)
            {
                return texture;
            }

            const int height = 64;
            var pixels = new Color32[height];
            for (int y = 0; y < height; y++)
            {
                pixels[y] = Color.Lerp(bottom, top, y / (float) (height - 1));
            }

            texture = NewTexture(1, height, pixels, "StoryGradient");
            _gradients[key] = texture;
            return texture;
        }

        // ------------------------------------------------------------------------------------
        // Shapes (fractions of the parent)
        // ------------------------------------------------------------------------------------

        private static Image NewImage(Transform parent, string name, float x0, float y0, float x1, float y1, Color color)
        {
            var image = StoryUi.Area(parent, x0, y0, x1, y1, name).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Image Box(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            return NewImage(parent, "Box", x0, y0, x1, y1, color);
        }

        public static Image Oval(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            var image = NewImage(parent, "Oval", x0, y0, x1, y1, color);
            image.sprite = Disc;
            return image;
        }

        public static Image Pill(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            var image = NewImage(parent, "Pill", x0, y0, x1, y1, color);
            image.sprite = Disc;
            image.type = Image.Type.Sliced;
            // Corners as big as they can be: Unity shrinks them to half the short side
            image.pixelsPerUnitMultiplier = 0.02f;
            return image;
        }

        public static Image Frame(Transform parent, float x0, float y0, float x1, float y1, Color color)
        {
            var image = NewImage(parent, "Frame", x0, y0, x1, y1, color);
            image.sprite = RingSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 0.8f;
            return image;
        }

        public static Image Icon(Transform parent, Sprite sprite, float x0, float y0, float x1, float y1, Color color)
        {
            var image = NewImage(parent, sprite != null ? sprite.name : "Icon", x0, y0, x1, y1, color);
            image.sprite = sprite;
            image.preserveAspect = true;
            return image;
        }

        /// <summary>A round thing on a parent of the given aspect (width / height): r is a share of the height.</summary>
        public static Image Round(Transform parent, Sprite sprite, float x, float y, float r, float aspect, Color color)
        {
            float rx = r / Mathf.Max(0.1f, aspect);
            var image = NewImage(parent, "Round", x - rx, y - r, x + rx, y + r, color);
            image.sprite = sprite;
            return image;
        }

        public static RawImage Gradient(Transform parent, float x0, float y0, float x1, float y1, Color top, Color bottom)
        {
            var image = StoryUi.Area(parent, x0, y0, x1, y1, "Gradient").gameObject.AddComponent<RawImage>();
            image.texture = GradientTexture(top, bottom);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A light beam hanging from (x, y), pointing down, turned by <paramref name="angle"/> degrees.</summary>
        public static Image LightBeam(Transform parent, float x, float y, float length, float width, Color color,
            float angle)
        {
            var image = NewImage(parent, "Beam", x - width / 2f, y - length, x + width / 2f, y, color);
            image.sprite = Beam;
            var rect = image.rectTransform;
            rect.pivot = new Vector2(0.5f, 1f);
            StoryUi.Place(rect, x - width / 2f, y - length, x + width / 2f, y);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            return image;
        }

        /// <summary>A straight line between two points given in the parent's units (origin bottom-left).</summary>
        public static Image Line(Transform parent, Vector2 a, Vector2 b, float thickness, Color color)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            var rect = (RectTransform) go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = a;
            rect.sizeDelta = new Vector2((b - a).magnitude, thickness);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static float Next(ref StoryRandom random) => (float) random.NextDouble();

        // ------------------------------------------------------------------------------------
        // Backdrops
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Fills <paramref name="parent"/> with the picture for a venue id or one of
        /// <see cref="StoryBackdrops.Names"/>. Anything unknown is the black room.
        /// </summary>
        public static void Backdrop(RectTransform parent, StoryCampaign campaign, string key, Vector2 size,
            StoryAnim anim)
        {
            float aspect = size.y > 0f ? size.x / size.y : 16f / 9f;
            var venue = campaign != null ? campaign.FindVenue(key) : null;
            var random = new StoryRandom(StoryRandom.SeedFrom(key ?? ""));

            if (venue != null)
            {
                var top = StoryUi.Hex(venue.Top, new Color(0.12f, 0.06f, 0.29f));
                var bottom = StoryUi.Hex(venue.Bottom, new Color(0.03f, 0.02f, 0.09f));
                var accent = StoryUi.Hex(venue.Accent, StoryTheme.Gold);
                var accent2 = StoryUi.Hex(venue.Accent2, new Color(0.64f, 0.37f, 0.9f));
                Venue(parent, venue.Style, top, bottom, accent, accent2, aspect, ref random, anim);
            }
            else
            {
                Named(parent, key ?? "", size, aspect, ref random, anim);
            }

            // Darker edges top and bottom, so text laid over the picture always reads
            Gradient(parent, 0f, 0.8f, 1f, 1f, new Color(0f, 0f, 0f, 0.55f), new Color(0f, 0f, 0f, 0f));
            Gradient(parent, 0f, 0f, 1f, 0.3f, new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.6f));
        }

        private static void Venue(RectTransform p, string style, Color top, Color bottom, Color a, Color b,
            float aspect, ref StoryRandom random, StoryAnim anim)
        {
            Gradient(p, 0f, 0f, 1f, 1f, top, bottom);
            switch (style)
            {
                case "theater":
                {
                    anim.AddPulse(Round(p, Glow, 0.5f, 0.35f, 0.45f, aspect, StoryUi.Alpha(a, 0.3f)), 0.18f, 0.32f, 0.25f, 0f);
                    anim.AddPulse(LightBeam(p, 0.5f, 1.02f, 0.9f, 0.34f, StoryUi.Alpha(StoryTheme.Cream, 0.22f), 0f), 0.14f, 0.24f, 0.2f, 0f);
                    for (int i = 0; i < 6; i++)
                    {
                        var fold = StoryUi.Shade(b, 0.45f + 0.1f * (i % 2));
                        Pill(p, i * 0.032f - 0.02f, 0.1f, i * 0.032f + 0.04f, 1.05f, fold);
                        Pill(p, 1f - i * 0.032f - 0.04f, 0.1f, 1f - i * 0.032f + 0.02f, 1.05f, fold);
                    }

                    for (int i = 0; i < 12; i++)
                    {
                        Oval(p, i / 11f - 0.06f, 0.84f, i / 11f + 0.06f, 1.0f, StoryUi.Shade(b, 0.6f));
                    }

                    Box(p, 0f, 0.865f, 1f, 0.875f, StoryUi.Shade(a, 0.8f));
                    for (int i = 0; i < 14; i++)
                    {
                        anim.AddPulse(Round(p, Disc, 0.04f + i * 0.07f, 0.935f, 0.009f, aspect, a), 0.35f, 1f, 1.2f, i * 0.25f);
                    }

                    Box(p, 0f, 0.15f, 1f, 0.16f, StoryUi.Shade(a, 0.55f));
                    Gradient(p, 0f, 0f, 1f, 0.15f, StoryUi.Shade(bottom, 1.4f), StoryUi.Shade(bottom, 0.6f));
                    break;
                }

                case "arena":
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var beam = LightBeam(p, 0.2f + i * 0.2f, 0.9f, 0.9f, 0.18f, StoryUi.Alpha(i % 2 == 0 ? a : b, 0.35f), 0f);
                        anim.AddTurn(beam.rectTransform, 0f, 18f, 0.11f + i * 0.02f, i * 0.3f);
                    }

                    Box(p, 0.04f, 0.885f, 0.96f, 0.905f, new Color(0.25f, 0.25f, 0.28f));
                    for (int side = 0; side < 2; side++)
                    {
                        float x = side == 0 ? 0.07f : 0.77f;
                        Box(p, x, 0.48f, x + 0.16f, 0.74f, StoryUi.Shade(b, 0.35f));
                        for (int i = 0; i < 6; i++)
                        {
                            var bar = Box(p, x + 0.015f + i * 0.023f, 0.5f, x + 0.03f + i * 0.023f, 0.5f + 0.04f + 0.03f * (i % 3), StoryUi.Alpha(a, 0.9f));
                            anim.AddPulse(bar, 0.3f, 1f, 1.6f, i * 0.17f + side * 0.5f);
                        }
                    }

                    Box(p, 0f, 0f, 1f, 0.3f, StoryUi.Shade(bottom, 0.55f));
                    Box(p, 0f, 0.3f, 1f, 0.307f, StoryUi.Alpha(a, 0.45f));
                    Box(p, 0f, 0.17f, 1f, 0.175f, StoryUi.Alpha(a, 0.25f));
                    for (int i = 0; i < 70; i++)
                    {
                        var phone = Round(p, Disc, Next(ref random), 0.02f + Next(ref random) * 0.26f, 0.004f, aspect,
                            StoryUi.Alpha(StoryTheme.Cream, 0.7f));
                        anim.AddPulse(phone, 0.15f, 0.85f, 0.3f + Next(ref random) * 0.5f, Next(ref random));
                    }

                    break;
                }

                case "street":
                    Street(p, top, a, b, aspect, ref random, anim);
                    break;

                case "upscale":
                {
                    var stone = Color.Lerp(top, StoryTheme.Cream, 0.22f);
                    foreach (float x in new[] { 0.06f, 0.26f, 0.7f, 0.9f })
                    {
                        Pill(p, x - 0.025f, 0.12f, x + 0.025f, 0.86f, stone);
                        Box(p, x - 0.04f, 0.84f, x + 0.04f, 0.87f, StoryUi.Shade(stone, 1.1f));
                    }

                    for (int i = 0; i < 9; i++)
                    {
                        Oval(p, i / 8f - 0.08f, 0.88f, i / 8f + 0.08f, 1.04f, StoryUi.Shade(b, 0.55f));
                    }

                    foreach (float x in new[] { 0.5f, 0.17f, 0.83f })
                    {
                        float scale = x == 0.5f ? 1f : 0.6f;
                        anim.AddPulse(Round(p, Glow, x, 0.74f, 0.16f * scale, aspect, StoryUi.Alpha(a, 0.45f)), 0.3f, 0.5f, 0.2f, x);
                        for (int i = 0; i < 7; i++)
                        {
                            float angle = Mathf.PI * (1.15f + i * 0.7f / 6f * Mathf.PI / 2.2f);
                            Round(p, Disc, x + Mathf.Cos(angle) * 0.04f * scale, 0.76f + Mathf.Sin(angle) * 0.05f * scale,
                                0.012f * scale, aspect, StoryUi.Alpha(StoryTheme.Cream, 0.9f));
                        }
                    }

                    Gradient(p, 0f, 0f, 1f, 0.14f, StoryUi.Shade(stone, 0.7f), StoryUi.Shade(bottom, 0.8f));
                    break;
                }

                case "festival":
                {
                    anim.AddPulse(Round(p, Glow, 0.5f, 0.38f, 0.42f, aspect, StoryUi.Alpha(a, 0.5f)), 0.35f, 0.55f, 0.15f, 0f);
                    Round(p, Disc, 0.5f, 0.36f, 0.2f, aspect, StoryUi.Alpha(a, 0.95f));
                    Oval(p, -0.2f, -0.2f, 0.6f, 0.38f, StoryUi.Shade(b, 0.35f));
                    Oval(p, 0.4f, -0.25f, 1.2f, 0.34f, StoryUi.Shade(b, 0.28f));
                    Box(p, 0.05f, 0.82f, 0.95f, 0.835f, new Color(0.2f, 0.2f, 0.22f));
                    Box(p, 0.05f, 0.2f, 0.065f, 0.835f, new Color(0.2f, 0.2f, 0.22f));
                    Box(p, 0.935f, 0.2f, 0.95f, 0.835f, new Color(0.2f, 0.2f, 0.22f));
                    for (int i = 0; i < 16; i++)
                    {
                        float x = 0.08f + i * 0.055f;
                        var flag = Box(p, x, 0.77f, x + 0.03f, 0.81f, i % 2 == 0 ? a : b);
                        anim.AddTurn(flag.rectTransform, 0f, 6f, 0.4f, i * 0.13f);
                    }

                    for (int i = 0; i < 2; i++)
                    {
                        var beam = LightBeam(p, 0.3f + i * 0.4f, 0.82f, 0.7f, 0.16f, StoryUi.Alpha(StoryTheme.Cream, 0.25f), 0f);
                        anim.AddTurn(beam.rectTransform, 0f, 20f, 0.09f, i * 0.5f);
                    }

                    for (int i = 0; i < 34; i++)
                    {
                        float x = i / 33f;
                        Round(p, Disc, x, 0.05f + Next(ref random) * 0.04f, 0.035f, aspect, new Color(0.03f, 0.02f, 0.03f));
                    }

                    break;
                }

                default:
                    Club(p, top, bottom, a, b, aspect, anim);
                    break;
            }
        }

        private static void Club(RectTransform p, Color top, Color bottom, Color a, Color b, float aspect, StoryAnim anim)
        {
            for (int row = 0; row < 12; row++)
            {
                float y = 0.15f + row * 0.07f;
                Box(p, 0f, y, 1f, y + 0.004f, new Color(0f, 0f, 0f, 0.3f));
            }

            anim.AddPulse(Round(p, Glow, 0.5f, 0.6f, 0.4f, aspect, StoryUi.Alpha(a, 0.4f)), 0.25f, 0.45f, 0.5f, 0f);
            anim.AddPulse(Frame(p, 0.32f, 0.42f, 0.68f, 0.82f, a), 0.75f, 1f, 0.5f, 0f);
            anim.AddPulse(Frame(p, 0.36f, 0.47f, 0.64f, 0.77f, b), 0.6f, 1f, 0.5f, 0.5f);
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? 0.04f : 0.84f;
                Box(p, x, 0.08f, x + 0.12f, 0.52f, new Color(0.05f, 0.05f, 0.06f));
                Round(p, Disc, x + 0.06f, 0.4f, 0.05f, aspect, new Color(0.16f, 0.16f, 0.18f));
                Round(p, Disc, x + 0.06f, 0.2f, 0.07f, aspect, new Color(0.16f, 0.16f, 0.18f));
            }

            for (int i = 0; i < 16; i++)
            {
                float x = i / 15f;
                float y = 0.96f - 0.06f * Mathf.Sin(Mathf.PI * x);
                anim.AddPulse(Round(p, Disc, x, y, 0.01f, aspect, StoryUi.Alpha(a, 1f)), 0.45f, 1f, 0.8f, i * 0.31f);
            }

            Gradient(p, 0f, 0f, 1f, 0.15f, StoryUi.Shade(bottom, 1.3f), StoryUi.Shade(bottom, 0.5f));
        }

        private static void Street(RectTransform p, Color top, Color a, Color b, float aspect, ref StoryRandom random,
            StoryAnim anim)
        {
            anim.AddPulse(Round(p, Glow, 0.72f, 0.5f, 0.32f, aspect, StoryUi.Alpha(a, 0.5f)), 0.35f, 0.5f, 0.12f, 0f);
            Round(p, Disc, 0.72f, 0.48f, 0.12f, aspect, StoryUi.Alpha(a, 0.9f));
            float x = 0f;
            while (x < 1f)
            {
                float width = 0.04f + Next(ref random) * 0.06f;
                Box(p, x, 0.1f, x + width, 0.35f + Next(ref random) * 0.3f, StoryUi.Shade(top, 0.55f));
                x += width;
            }

            x = -0.02f;
            while (x < 1f)
            {
                float width = 0.06f + Next(ref random) * 0.08f;
                float height = 0.22f + Next(ref random) * 0.2f;
                Box(p, x, 0.1f, x + width - 0.006f, height, new Color(0.04f, 0.035f, 0.05f));
                for (int i = 0; i < 4; i++)
                {
                    float wx = x + 0.01f + Next(ref random) * (width - 0.03f);
                    float wy = 0.14f + Next(ref random) * (height - 0.18f);
                    var light = Box(p, wx, wy, wx + 0.012f, wy + 0.02f, StoryUi.Alpha(a, 0.8f));
                    if (i == 0) anim.AddPulse(light, 0.2f, 0.9f, 0.1f + Next(ref random) * 0.2f, Next(ref random));
                }

                x += width;
            }

            for (int i = 0; i < 18; i++)
            {
                float lx = i / 17f;
                float ly = 0.72f - 0.07f * Mathf.Sin(Mathf.PI * lx);
                anim.AddPulse(Round(p, Disc, lx, ly, 0.008f, aspect, i % 2 == 0 ? a : b), 0.5f, 1f, 0.6f, i * 0.2f);
            }

            Box(p, 0f, 0f, 1f, 0.1f, new Color(0.06f, 0.055f, 0.06f));
        }

        private static void Named(RectTransform p, string name, Vector2 size, float aspect, ref StoryRandom random,
            StoryAnim anim)
        {
            var ink = StoryTheme.Ink;
            switch (name)
            {
                case "wave":
                {
                    Box(p, 0f, 0f, 1f, 1f, ink);
                    var holder = StoryUi.Area(p, 0.05f, 0.3f, 0.95f, 0.7f, "Wave");
                    Waveform(holder, new Vector2(size.x * 0.9f, size.y * 0.4f), null, anim, 0.35f);
                    break;
                }

                case "tape":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.16f, 0.1f, 0.06f), new Color(0.05f, 0.03f, 0.02f));
                    float reel = Mathf.Min(size.y * 0.42f, size.x * 0.3f);
                    for (int i = 0; i < 2; i++)
                    {
                        var holder = StoryUi.Fixed(p, i == 0 ? 0.34f : 0.66f, 0.56f, reel, reel, "Reel");
                        Oval(holder, 0f, 0f, 1f, 1f, new Color(0.1f, 0.07f, 0.05f));
                        Icon(holder, Hoop, 0f, 0f, 1f, 1f, StoryUi.Alpha(StoryTheme.Gold, 0.8f));
                        Oval(holder, 0.18f, 0.18f, 0.82f, 0.82f, new Color(0.2f, 0.13f, 0.08f));
                        var spokes = StoryUi.Area(holder, 0f, 0f, 1f, 1f, "Spokes");
                        for (int s = 0; s < 3; s++)
                        {
                            var spoke = Box(spokes, 0.47f, 0.2f, 0.53f, 0.8f, StoryUi.Alpha(StoryTheme.Gold, 0.7f));
                            spoke.rectTransform.localRotation = Quaternion.Euler(0f, 0f, s * 60f);
                        }

                        Oval(holder, 0.4f, 0.4f, 0.6f, 0.6f, StoryTheme.Gold);
                        anim.AddTurn(spokes, 0f, 0f, -0.25f, 0f);
                    }

                    Box(p, 0.3f, 0.3f, 0.7f, 0.31f, StoryUi.Alpha(StoryTheme.Gold, 0.5f));
                    break;
                }

                case "van":
                {
                    Box(p, 0f, 0f, 1f, 1f, new Color(0.05f, 0.04f, 0.04f));
                    var glass = StoryUi.Area(p, 0.08f, 0.42f, 0.92f, 0.92f, "Windshield");
                    Gradient(glass, 0f, 0f, 1f, 1f, new Color(0.3f, 0.16f, 0.35f), new Color(0.95f, 0.5f, 0.25f));
                    Box(glass, 0f, 0f, 1f, 0.3f, new Color(0.08f, 0.06f, 0.08f));
                    Box(glass, 0.49f, 0f, 0.51f, 0.3f, StoryUi.Alpha(StoryTheme.Gold, 0.6f));
                    Box(p, 0f, 0f, 1f, 0.42f, new Color(0.09f, 0.07f, 0.06f));
                    Pill(p, 0.12f, 0.05f, 0.38f, 0.5f, new Color(0.14f, 0.1f, 0.08f));
                    Pill(p, 0.62f, 0.05f, 0.88f, 0.5f, new Color(0.14f, 0.1f, 0.08f));
                    var charm = Box(p, 0.495f, 0.78f, 0.505f, 0.92f, StoryTheme.Muted);
                    charm.rectTransform.pivot = new Vector2(0.5f, 1f);
                    StoryUi.Place(charm.rectTransform, 0.495f, 0.78f, 0.505f, 0.92f);
                    anim.AddTurn(charm.rectTransform, 0f, 8f, 0.5f, 0f);
                    break;
                }

                case "road":
                {
                    Gradient(p, 0f, 0.4f, 1f, 1f, new Color(0.18f, 0.12f, 0.3f), new Color(0.98f, 0.55f, 0.3f));
                    Round(p, Disc, 0.5f, 0.42f, 0.09f, aspect, new Color(1f, 0.75f, 0.4f));
                    Box(p, 0f, 0f, 1f, 0.42f, new Color(0.08f, 0.07f, 0.08f));
                    for (int i = 0; i < 10; i++)
                    {
                        float y0 = i * 0.042f, half = 0.42f - i * 0.04f;
                        Box(p, 0.5f - half, y0, 0.5f + half, y0 + 0.042f, new Color(0.15f, 0.14f, 0.15f));
                    }

                    for (int i = 0; i < 5; i++)
                    {
                        float y0 = 0.02f + i * 0.08f, w = 0.012f - i * 0.002f;
                        Box(p, 0.5f - w, y0, 0.5f + w, y0 + 0.04f - i * 0.005f, StoryTheme.Gold);
                    }

                    break;
                }

                case "backstage":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.22f, 0.13f, 0.1f), new Color(0.08f, 0.05f, 0.04f));
                    var mirror = StoryUi.Area(p, 0.25f, 0.34f, 0.75f, 0.86f, "Mirror");
                    StoryUi.Rounded(mirror, new Color(0.14f, 0.13f, 0.15f), 3f);
                    Gradient(mirror, 0.05f, 0.05f, 0.95f, 0.95f, new Color(0.3f, 0.3f, 0.34f, 0.5f), new Color(0.1f, 0.1f, 0.12f, 0.5f));
                    for (int i = 0; i < 7; i++)
                    {
                        float x = 0.25f + i * (0.5f / 6f);
                        anim.AddPulse(Round(p, Disc, x, 0.88f, 0.018f, aspect, new Color(1f, 0.9f, 0.7f)), 0.7f, 1f, 0.3f, i * 0.4f);
                    }

                    for (int i = 0; i < 5; i++)
                    {
                        float y = 0.38f + i * 0.11f;
                        Round(p, Disc, 0.23f, y, 0.018f, aspect, new Color(1f, 0.9f, 0.7f));
                        Round(p, Disc, 0.77f, y, 0.018f, aspect, new Color(1f, 0.9f, 0.7f));
                    }

                    Box(p, 0.15f, 0.22f, 0.85f, 0.3f, new Color(0.18f, 0.11f, 0.08f));
                    break;
                }

                case "studio":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.08f, 0.08f, 0.11f), new Color(0.02f, 0.02f, 0.03f));
                    var booth = StoryUi.Area(p, 0.55f, 0.44f, 0.94f, 0.88f, "Booth");
                    StoryUi.Rounded(booth, new Color(0.13f, 0.1f, 0.08f), 3f);
                    anim.AddPulse(Oval(booth, 0.15f, 0.1f, 0.85f, 0.9f, StoryUi.Alpha(StoryTheme.Gold, 0.25f)), 0.15f, 0.3f, 0.2f, 0f);
                    Oval(booth, 0.42f, 0.5f, 0.58f, 0.75f, new Color(0.05f, 0.05f, 0.05f));
                    Box(booth, 0.49f, 0.1f, 0.51f, 0.5f, new Color(0.05f, 0.05f, 0.05f));
                    Box(p, 0.03f, 0.04f, 0.97f, 0.33f, new Color(0.12f, 0.12f, 0.14f));
                    for (int i = 0; i < 18; i++)
                    {
                        float x = 0.06f + i * 0.05f;
                        Box(p, x, 0.08f, x + 0.008f, 0.24f, new Color(0.05f, 0.05f, 0.06f));
                        Box(p, x - 0.01f, 0.1f + Next(ref random) * 0.12f, x + 0.018f, 0.0f + 0.13f + Next(ref random) * 0.1f, StoryTheme.Muted);
                        anim.AddPulse(Round(p, Disc, x + 0.004f, 0.29f, 0.006f, aspect, i % 4 == 0 ? StoryTheme.Danger : StoryTheme.Won),
                            0.2f, 1f, 1.5f, Next(ref random));
                    }

                    break;
                }

                case "radio":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.07f, 0.05f, 0.08f), new Color(0.02f, 0.01f, 0.02f));
                    anim.AddPulse(Round(p, Glow, 0.3f, 0.55f, 0.35f, aspect, StoryUi.Alpha(StoryTheme.Gold, 0.3f)), 0.2f, 0.3f, 0.2f, 0f);
                    var sign = StoryUi.Area(p, 0.62f, 0.74f, 0.9f, 0.86f, "OnAir");
                    var signImage = StoryUi.Rounded(sign, StoryTheme.Danger, 3f);
                    anim.AddPulse(signImage, 0.55f, 1f, 0.5f, 0f);
                    StoryUi.Text(sign, 0f, 0f, 1f, 1f, "ON AIR", 44, true, StoryTheme.Cream, TextAlignmentOptions.Center, false);
                    Oval(p, 0.26f, 0.45f, 0.34f, 0.7f, new Color(0.25f, 0.25f, 0.27f));
                    Box(p, 0.295f, 0.12f, 0.305f, 0.46f, new Color(0.2f, 0.2f, 0.22f));
                    Box(p, 0.05f, 0f, 0.95f, 0.12f, new Color(0.1f, 0.08f, 0.09f));
                    break;
                }

                case "church":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.17f, 0.12f, 0.2f), new Color(0.05f, 0.03f, 0.05f));
                    Color[] glass = { StoryTheme.Gold, new Color(0.3f, 0.5f, 0.9f), new Color(0.85f, 0.3f, 0.3f), new Color(0.3f, 0.75f, 0.5f) };
                    for (int w = 0; w < 3; w++)
                    {
                        float x = 0.25f + w * 0.25f;
                        var window = StoryUi.Area(p, x - 0.06f, 0.45f, x + 0.06f, 0.92f, "Window");
                        StoryUi.Rounded(window, new Color(0.08f, 0.06f, 0.08f), 0.4f);
                        for (int i = 0; i < 4; i++)
                        {
                            var pane = Box(window, 0.12f + i * 0.19f, 0.06f, 0.12f + i * 0.19f + 0.17f, 0.8f, StoryUi.Alpha(glass[(i + w) % 4], 0.75f));
                            anim.AddPulse(pane, 0.55f, 0.85f, 0.1f, i * 0.2f + w * 0.3f);
                        }

                        LightBeam(p, x, 0.6f, 0.6f, 0.2f, StoryUi.Alpha(StoryTheme.Gold, 0.18f), -12f);
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        float y = 0.05f + i * 0.08f;
                        Box(p, 0.08f, y, 0.44f, y + 0.035f, new Color(0.2f, 0.11f, 0.06f));
                        Box(p, 0.56f, y, 0.92f, y + 0.035f, new Color(0.2f, 0.11f, 0.06f));
                    }

                    break;
                }

                case "street":
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.08f, 0.08f, 0.2f), new Color(0.02f, 0.02f, 0.05f));
                    Street(p, new Color(0.12f, 0.12f, 0.28f), StoryTheme.Gold, new Color(0.9f, 0.4f, 0.45f), aspect, ref random, anim);
                    break;

                case "rooftop":
                {
                    Gradient(p, 0f, 0.15f, 1f, 1f, new Color(0.15f, 0.2f, 0.45f), new Color(1f, 0.62f, 0.35f));
                    anim.AddPulse(Round(p, Glow, 0.3f, 0.25f, 0.35f, aspect, StoryUi.Alpha(new Color(1f, 0.8f, 0.45f), 0.6f)), 0.45f, 0.6f, 0.1f, 0f);
                    float x = 0f;
                    while (x < 1f)
                    {
                        float width = 0.03f + Next(ref random) * 0.07f;
                        Box(p, x, 0.15f, x + width, 0.25f + Next(ref random) * 0.25f, new Color(0.12f, 0.1f, 0.16f));
                        x += width;
                    }

                    Box(p, 0f, 0f, 1f, 0.17f, new Color(0.06f, 0.05f, 0.07f));
                    Box(p, 0.72f, 0.17f, 0.74f, 0.3f, new Color(0.06f, 0.05f, 0.07f));
                    Box(p, 0.8f, 0.17f, 0.82f, 0.3f, new Color(0.06f, 0.05f, 0.07f));
                    Pill(p, 0.7f, 0.3f, 0.84f, 0.45f, new Color(0.06f, 0.05f, 0.07f));
                    break;
                }

                case "office":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.1f, 0.14f, 0.22f), new Color(0.03f, 0.04f, 0.07f));
                    for (int i = 0; i < 60; i++)
                    {
                        var light = Round(p, Disc, Next(ref random), 0.2f + Next(ref random) * 0.5f, 0.004f, aspect, StoryUi.Alpha(StoryTheme.Gold, 0.7f));
                        if (i % 5 == 0) anim.AddPulse(light, 0.2f, 0.8f, 0.2f, Next(ref random));
                    }

                    for (int i = 0; i <= 6; i++)
                    {
                        Box(p, i / 6f - 0.004f, 0f, i / 6f + 0.004f, 1f, new Color(0.18f, 0.2f, 0.25f));
                    }

                    Box(p, 0f, 0.62f, 1f, 0.63f, new Color(0.18f, 0.2f, 0.25f));
                    Box(p, 0.1f, 0f, 0.9f, 0.14f, new Color(0.08f, 0.08f, 0.1f));
                    break;
                }

                case "kitchen":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.2f, 0.14f, 0.09f), new Color(0.07f, 0.05f, 0.03f));
                    Box(p, 0.497f, 0.82f, 0.503f, 1f, new Color(0.1f, 0.08f, 0.06f));
                    anim.AddPulse(LightBeam(p, 0.5f, 0.8f, 0.6f, 0.5f, StoryUi.Alpha(new Color(1f, 0.85f, 0.55f), 0.35f), 0f), 0.28f, 0.36f, 0.15f, 0f);
                    Pill(p, 0.42f, 0.76f, 0.58f, 0.84f, new Color(0.35f, 0.45f, 0.3f));
                    Round(p, Glow, 0.5f, 0.74f, 0.06f, aspect, new Color(1f, 0.9f, 0.6f, 0.9f));
                    Box(p, 0.2f, 0.18f, 0.8f, 0.24f, new Color(0.35f, 0.22f, 0.12f));
                    Box(p, 0.24f, 0f, 0.27f, 0.18f, new Color(0.25f, 0.16f, 0.09f));
                    Box(p, 0.73f, 0f, 0.76f, 0.18f, new Color(0.25f, 0.16f, 0.09f));
                    break;
                }

                case "diner":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.04f, 0.13f, 0.14f), new Color(0.01f, 0.04f, 0.05f));
                    var neon = new Color(1f, 0.35f, 0.6f);
                    anim.AddPulse(Round(p, Glow, 0.5f, 0.72f, 0.25f, aspect, StoryUi.Alpha(neon, 0.4f)), 0.25f, 0.45f, 0.6f, 0f);
                    anim.AddPulse(Frame(p, 0.36f, 0.64f, 0.64f, 0.8f, neon), 0.6f, 1f, 0.6f, 0f);
                    var sign = StoryUi.Area(p, 0.38f, 0.66f, 0.62f, 0.78f, "Sign");
                    StoryUi.Text(sign, 0f, 0f, 1f, 1f, "OPEN ALL NIGHT", 40, true, neon, TextAlignmentOptions.Center, false);
                    Box(p, 0.05f, 0.15f, 0.3f, 0.45f, new Color(0.55f, 0.1f, 0.12f));
                    Box(p, 0.7f, 0.15f, 0.95f, 0.45f, new Color(0.55f, 0.1f, 0.12f));
                    for (int i = 0; i < 20; i++)
                    {
                        Box(p, i / 20f, 0f, i / 20f + 0.05f, 0.08f, i % 2 == 0 ? StoryTheme.Cream : StoryTheme.Ink);
                    }

                    break;
                }

                case "airport":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.25f, 0.2f, 0.4f), new Color(0.9f, 0.5f, 0.35f));
                    var plane = StoryUi.Area(p, 0.55f, 0.6f, 0.85f, 0.72f, "Plane");
                    Pill(plane, 0f, 0.35f, 1f, 0.65f, new Color(0.1f, 0.08f, 0.12f));
                    var wing = Box(plane, 0.35f, 0.1f, 0.6f, 0.9f, new Color(0.1f, 0.08f, 0.12f));
                    wing.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -30f);
                    anim.AddTurn(plane, 6f, 2f, 0.2f, 0f);
                    for (int i = 0; i <= 4; i++)
                    {
                        Box(p, i / 4f - 0.005f, 0.3f, i / 4f + 0.005f, 1f, new Color(0.05f, 0.05f, 0.07f));
                    }

                    var board = StoryUi.Area(p, 0.06f, 0.05f, 0.5f, 0.3f, "Board");
                    StoryUi.Rounded(board, new Color(0.04f, 0.04f, 0.05f), 3f);
                    for (int i = 0; i < 5; i++)
                    {
                        float y = 0.12f + i * 0.16f;
                        var row = Box(board, 0.06f, y, 0.06f + 0.4f + Next(ref random) * 0.45f, y + 0.08f, StoryUi.Alpha(new Color(1f, 0.75f, 0.2f), 0.9f));
                        anim.AddPulse(row, 0.6f, 1f, 0.15f, i * 0.37f);
                    }

                    Box(p, 0f, 0f, 1f, 0.05f, new Color(0.05f, 0.05f, 0.07f));
                    break;
                }

                case "porch":
                {
                    Gradient(p, 0f, 0f, 1f, 1f, new Color(0.14f, 0.1f, 0.3f), new Color(0.45f, 0.25f, 0.3f));
                    for (int i = 0; i < 14; i++)
                    {
                        var fly = Round(p, Glow, Next(ref random), 0.35f + Next(ref random) * 0.4f, 0.012f, aspect, new Color(0.95f, 0.95f, 0.5f));
                        anim.AddPulse(fly, 0f, 0.9f, 0.3f + Next(ref random) * 0.4f, Next(ref random));
                    }

                    Box(p, 0f, 0.86f, 1f, 0.9f, new Color(0.12f, 0.08f, 0.07f));
                    Box(p, 0.08f, 0f, 0.11f, 0.86f, new Color(0.2f, 0.13f, 0.1f));
                    Box(p, 0.89f, 0f, 0.92f, 0.86f, new Color(0.2f, 0.13f, 0.1f));
                    Box(p, 0f, 0.3f, 1f, 0.32f, new Color(0.2f, 0.13f, 0.1f));
                    for (int i = 0; i < 24; i++)
                    {
                        Box(p, i / 24f, 0.12f, i / 24f + 0.008f, 0.3f, new Color(0.2f, 0.13f, 0.1f));
                    }

                    anim.AddPulse(Round(p, Glow, 0.5f, 0.78f, 0.12f, aspect, new Color(1f, 0.85f, 0.5f, 0.8f)), 0.6f, 0.85f, 0.25f, 0f);
                    Box(p, 0f, 0f, 1f, 0.12f, new Color(0.15f, 0.1f, 0.08f));
                    break;
                }

                default:
                    Box(p, 0f, 0f, 1f, 1f, ink);
                    break;
            }
        }

        // ------------------------------------------------------------------------------------
        // People
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// A head-and-shoulders portrait filling a square holder. The mood moves the brows and
        /// the mouth, so the same face can be happy, worried or sly.
        /// </summary>
        public static void Portrait(RectTransform holder, StoryPortrait look, string mood, StoryAnim anim)
        {
            look ??= new StoryPortrait();
            var skin = StoryUi.Hex(look.Skin, new Color(0.55f, 0.33f, 0.14f));
            var hair = StoryUi.Hex(look.HairColor, new Color(0.1f, 0.08f, 0.06f));
            var outfit = StoryUi.Hex(look.Outfit, new Color(0.23f, 0.43f, 0.65f));
            var accent = StoryUi.Hex(look.Accent, StoryTheme.Gold);
            var dark = new Color(0.08f, 0.05f, 0.04f);

            Oval(holder, 0.02f, 0.08f, 0.98f, 1.04f, StoryUi.Alpha(accent, 0.22f));
            Oval(holder, 0.12f, 0.18f, 0.88f, 0.94f, StoryUi.Alpha(accent, 0.12f));

            // Behind the head
            switch (look.Hair)
            {
                case "afro":
                    Oval(holder, 0.17f, 0.42f, 0.83f, 1.0f, hair);
                    break;
                case "locs":
                    Oval(holder, 0.27f, 0.55f, 0.73f, 0.88f, hair);
                    for (int i = 0; i < 4; i++)
                    {
                        Pill(holder, 0.26f + i * 0.025f, 0.22f + i * 0.04f, 0.3f + i * 0.025f, 0.72f, StoryUi.Shade(hair, 1f + 0.12f * (i % 2)));
                        Pill(holder, 0.7f - i * 0.025f, 0.22f + i * 0.04f, 0.74f - i * 0.025f, 0.72f, StoryUi.Shade(hair, 1f + 0.12f * (i % 2)));
                    }

                    break;
                case "braids":
                    Pill(holder, 0.27f, 0.12f, 0.35f, 0.74f, hair);
                    Pill(holder, 0.65f, 0.12f, 0.73f, 0.74f, hair);
                    break;
                case "bob":
                    Pill(holder, 0.27f, 0.38f, 0.73f, 0.86f, hair);
                    break;
                case "puffs":
                    Oval(holder, 0.2f, 0.72f, 0.42f, 0.94f, hair);
                    Oval(holder, 0.58f, 0.72f, 0.8f, 0.94f, hair);
                    break;
            }

            if (look.Accessory == "headphones")
            {
                Icon(holder, Hoop, 0.25f, 0.42f, 0.75f, 0.94f, new Color(0.12f, 0.12f, 0.13f));
            }

            // Shoulders, neck, ears, head
            Pill(holder, 0.1f, -0.3f, 0.9f, 0.3f, outfit);
            Pill(holder, 0.37f, 0.2f, 0.63f, 0.3f, StoryUi.Shade(outfit, 0.75f));
            Box(holder, 0.43f, 0.24f, 0.57f, 0.4f, StoryUi.Shade(skin, 0.85f));
            Oval(holder, 0.295f, 0.5f, 0.355f, 0.62f, StoryUi.Shade(skin, 0.92f));
            Oval(holder, 0.645f, 0.5f, 0.705f, 0.62f, StoryUi.Shade(skin, 0.92f));
            Oval(holder, 0.33f, 0.36f, 0.67f, 0.8f, skin);

            // In front of the head
            switch (look.Hair)
            {
                case "afro":
                    Oval(holder, 0.3f, 0.69f, 0.7f, 0.86f, hair);
                    break;
                case "locs":
                case "braids":
                    Oval(holder, 0.31f, 0.67f, 0.69f, 0.84f, hair);
                    if (look.Hair == "braids") Box(holder, 0.495f, 0.74f, 0.505f, 0.84f, StoryUi.Shade(skin, 0.8f));
                    break;
                case "fade":
                    Oval(holder, 0.33f, 0.69f, 0.67f, 0.82f, hair);
                    break;
                case "bald":
                    Oval(holder, 0.42f, 0.72f, 0.52f, 0.77f, new Color(1f, 1f, 1f, 0.16f));
                    break;
                case "curls":
                    for (int i = 0; i < 7; i++)
                    {
                        float angle = Mathf.PI * (0.12f + i * 0.76f / 6f);
                        float x = 0.5f + Mathf.Cos(angle) * 0.17f, y = 0.68f + Mathf.Sin(angle) * 0.13f;
                        Oval(holder, x - 0.065f, y - 0.065f, x + 0.065f, y + 0.065f, i % 2 == 0 ? hair : StoryUi.Shade(hair, 1.25f));
                    }

                    break;
                case "wrap":
                    Pill(holder, 0.3f, 0.68f, 0.7f, 0.98f, accent);
                    Box(holder, 0.31f, 0.75f, 0.69f, 0.77f, StoryUi.Shade(accent, 0.75f));
                    Oval(holder, 0.43f, 0.9f, 0.57f, 1.01f, StoryUi.Shade(accent, 0.85f));
                    break;
                case "waves":
                    Oval(holder, 0.32f, 0.68f, 0.68f, 0.83f, hair);
                    for (int i = 0; i < 3; i++)
                    {
                        Pill(holder, 0.38f, 0.73f + i * 0.025f, 0.62f, 0.738f + i * 0.025f, StoryUi.Shade(hair, 1.8f));
                    }

                    break;
                case "puffs":
                    Oval(holder, 0.32f, 0.68f, 0.68f, 0.83f, hair);
                    break;
                case "bob":
                    Pill(holder, 0.31f, 0.7f, 0.69f, 0.84f, hair);
                    break;
                case "hightop":
                {
                    var top = StoryUi.Area(holder, 0.34f, 0.68f, 0.66f, 0.99f, "Hightop");
                    StoryUi.Rounded(top, hair, 3f);
                    break;
                }

                case "twists":
                    Oval(holder, 0.32f, 0.68f, 0.68f, 0.83f, hair);
                    for (int i = 0; i < 7; i++)
                    {
                        float x = 0.36f + i * 0.047f;
                        var twist = Pill(holder, x - 0.018f, 0.76f, x + 0.018f, 0.9f, StoryUi.Shade(hair, 1f + 0.15f * (i % 2)));
                        twist.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (3 - i) * 7f);
                    }

                    break;
            }

            // The face, set by the mood
            float browAngle = 0f, browLift = 0f, mouthWidth = 0.12f, mouthHeight = 0.024f, mouthDrop = 0f, mouthShift = 0f;
            bool oneBrow = false;
            switch (mood)
            {
                case "warm": browAngle = -4f; mouthWidth = 0.14f; mouthHeight = 0.034f; break;
                case "laugh": browAngle = -8f; browLift = 0.012f; mouthWidth = 0.14f; mouthHeight = 0.06f; break;
                case "stern": browAngle = 14f; browLift = -0.006f; mouthWidth = 0.1f; mouthHeight = 0.018f; break;
                case "sad": browAngle = -16f; mouthWidth = 0.08f; mouthHeight = 0.02f; mouthDrop = 0.012f; break;
                case "shock": browLift = 0.025f; mouthWidth = 0.06f; mouthHeight = 0.06f; break;
                case "hype": browLift = 0.02f; mouthWidth = 0.16f; mouthHeight = 0.07f; break;
                case "sly": oneBrow = true; mouthWidth = 0.1f; mouthShift = 0.025f; break;
                case "proud": browAngle = 4f; mouthWidth = 0.13f; mouthHeight = 0.03f; break;
                case "worried": browAngle = -12f; browLift = 0.008f; mouthWidth = 0.09f; mouthHeight = 0.02f; mouthDrop = 0.006f; break;
            }

            var eyeLeft = Oval(holder, 0.405f, 0.565f, 0.455f, 0.62f, dark).rectTransform;
            var eyeRight = Oval(holder, 0.545f, 0.565f, 0.595f, 0.62f, dark).rectTransform;
            var browLeft = Pill(holder, 0.385f, 0.645f + browLift, 0.47f, 0.668f + browLift, hair);
            var browRight = Pill(holder, 0.53f, 0.645f + browLift + (oneBrow ? 0.02f : 0f), 0.615f, 0.668f + browLift + (oneBrow ? 0.02f : 0f), hair);
            browLeft.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -browAngle);
            browRight.rectTransform.localRotation = Quaternion.Euler(0f, 0f, browAngle + (oneBrow ? 10f : 0f));
            Oval(holder, 0.478f, 0.5f, 0.522f, 0.55f, StoryUi.Shade(skin, 0.78f));
            float mx = 0.5f + mouthShift, my = 0.445f - mouthDrop;
            var mouth = Pill(holder, mx - mouthWidth / 2f, my - mouthHeight / 2f, mx + mouthWidth / 2f, my + mouthHeight / 2f,
                new Color(0.32f, 0.08f, 0.08f)).rectTransform;
            anim.AddFace(eyeLeft, eyeRight, mouth, (look.Skin ?? "").Length * 0.13f + (look.Hair ?? "").Length * 0.07f);

            switch (look.Accessory)
            {
                case "shades":
                    Pill(holder, 0.385f, 0.56f, 0.475f, 0.63f, new Color(0.04f, 0.04f, 0.05f));
                    Pill(holder, 0.525f, 0.56f, 0.615f, 0.63f, new Color(0.04f, 0.04f, 0.05f));
                    Box(holder, 0.47f, 0.6f, 0.53f, 0.61f, new Color(0.04f, 0.04f, 0.05f));
                    Box(holder, 0.4f, 0.605f, 0.43f, 0.615f, new Color(1f, 1f, 1f, 0.4f));
                    break;
                case "glasses":
                    Icon(holder, Hoop, 0.38f, 0.54f, 0.48f, 0.645f, accent);
                    Icon(holder, Hoop, 0.52f, 0.54f, 0.62f, 0.645f, accent);
                    Box(holder, 0.478f, 0.6f, 0.522f, 0.607f, accent);
                    break;
                case "headphones":
                    Pill(holder, 0.25f, 0.47f, 0.33f, 0.65f, accent);
                    Pill(holder, 0.67f, 0.47f, 0.75f, 0.65f, accent);
                    break;
                case "hat":
                    Pill(holder, 0.2f, 0.76f, 0.8f, 0.82f, StoryUi.Shade(accent, 0.5f));
                    var crown = StoryUi.Area(holder, 0.32f, 0.79f, 0.68f, 0.98f, "Crown");
                    StoryUi.Rounded(crown, StoryUi.Shade(accent, 0.5f), 4f);
                    Box(holder, 0.32f, 0.8f, 0.68f, 0.84f, accent);
                    break;
                case "cap":
                    Oval(holder, 0.31f, 0.7f, 0.69f, 0.93f, accent);
                    Pill(holder, 0.45f, 0.72f, 0.84f, 0.78f, StoryUi.Shade(accent, 0.75f));
                    break;
                case "earrings":
                    Oval(holder, 0.3f, 0.43f, 0.34f, 0.47f, accent);
                    Oval(holder, 0.66f, 0.43f, 0.7f, 0.47f, accent);
                    break;
                case "scarf":
                    Pill(holder, 0.3f, 0.2f, 0.7f, 0.34f, accent);
                    Pill(holder, 0.55f, -0.02f, 0.63f, 0.28f, StoryUi.Shade(accent, 0.85f));
                    break;
                case "chain":
                    Pill(holder, 0.4f, 0.13f, 0.6f, 0.155f, StoryTheme.Gold);
                    Oval(holder, 0.475f, 0.05f, 0.525f, 0.13f, StoryTheme.Gold);
                    break;
                case "bowtie":
                    var tieLeft = Box(holder, 0.43f, 0.215f, 0.49f, 0.265f, accent);
                    tieLeft.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                    var tieRight = Box(holder, 0.51f, 0.215f, 0.57f, 0.265f, accent);
                    tieRight.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -20f);
                    Oval(holder, 0.48f, 0.22f, 0.52f, 0.26f, StoryUi.Shade(accent, 0.7f));
                    break;
                case "flower":
                    for (int i = 0; i < 5; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 5f;
                        float x = 0.67f + Mathf.Cos(angle) * 0.035f, y = 0.79f + Mathf.Sin(angle) * 0.035f;
                        Oval(holder, x - 0.03f, y - 0.03f, x + 0.03f, y + 0.03f, accent);
                    }

                    Oval(holder, 0.65f, 0.77f, 0.69f, 0.81f, StoryTheme.Gold);
                    break;
                case "beanie":
                    Oval(holder, 0.3f, 0.68f, 0.7f, 0.96f, accent);
                    Pill(holder, 0.29f, 0.68f, 0.71f, 0.76f, StoryUi.Shade(accent, 0.75f));
                    break;
            }
        }

        /// <summary>The player's band: a figure in a spotlight, never a face, so it can be anyone.</summary>
        public static void Silhouette(RectTransform holder, StoryAnim anim)
        {
            anim.AddPulse(LightBeam(holder, 0.5f, 1.1f, 1.1f, 0.9f, StoryUi.Alpha(StoryTheme.Gold, 0.4f), 0f), 0.3f, 0.45f, 0.3f, 0f);
            Oval(holder, 0.15f, -0.02f, 0.85f, 0.1f, StoryUi.Alpha(StoryTheme.Gold, 0.35f));
            var rim = StoryUi.Alpha(StoryTheme.Gold, 0.55f);
            Pill(holder, 0.285f, 0.02f, 0.715f, 0.58f, rim);
            Oval(holder, 0.405f, 0.53f, 0.595f, 0.77f, rim);
            Pill(holder, 0.3f, 0.02f, 0.7f, 0.56f, StoryTheme.Ink);
            Oval(holder, 0.42f, 0.545f, 0.58f, 0.755f, StoryTheme.Ink);
            Box(holder, 0.62f, 0.3f, 0.635f, 0.62f, new Color(0.25f, 0.25f, 0.27f));
            Oval(holder, 0.605f, 0.6f, 0.65f, 0.66f, new Color(0.3f, 0.3f, 0.32f));
        }

        /// <summary>
        /// The narrator: a drum break seen as a waveform, gold bars on black, with one thin
        /// colored line woven through for every part of the band found again.
        /// </summary>
        public static void Waveform(RectTransform holder, Vector2 size, List<string> layers, StoryAnim anim,
            float energy)
        {
            const int bars = 44;
            anim.Energy = energy;
            Box(holder, 0f, 0.495f, 1f, 0.505f, StoryUi.Alpha(StoryTheme.Gold, 0.25f));
            float barWidth = 0.55f / bars;
            for (int i = 0; i < bars; i++)
            {
                float x = (i + 0.5f) / bars;
                float envelope = 0.3f + 0.7f * Mathf.Sin(x * Mathf.PI);
                var bar = Pill(holder, x - barWidth / 2f, 0.04f, x + barWidth / 2f, 0.96f,
                    Color.Lerp(StoryTheme.GoldDeep, StoryTheme.Gold, envelope));
                anim.AddBar(bar.rectTransform, i * 0.61f, envelope);
            }

            if (layers == null)
            {
                return;
            }

            float dot = Mathf.Clamp(size.y * 0.045f, 6f, 16f);
            for (int l = 0; l < layers.Count; l++)
            {
                var color = StoryTheme.LayerColor(layers[l]);
                for (int i = 0; i < 36; i++)
                {
                    float x = (i + 0.5f) / 36f;
                    var holderDot = StoryUi.Fixed(holder, x, 0.5f, dot, dot, "Layer");
                    var image = holderDot.gameObject.AddComponent<Image>();
                    image.sprite = Disc;
                    image.color = color;
                    image.raycastTarget = false;
                    anim.AddDot(holderDot, x, l * 1.3f, size.y * (0.18f + 0.05f * l), 1.6f + l * 0.35f);
                }
            }
        }

        // ------------------------------------------------------------------------------------
        // Stars, badges, the map
        // ------------------------------------------------------------------------------------

        /// <summary>A row of stars filling the rect: <paramref name="filled"/> bright ones out of <paramref name="total"/>.</summary>
        public static void Stars(RectTransform rect, int filled, int total, bool gold)
        {
            total = Mathf.Max(1, total);
            for (int i = 0; i < total; i++)
            {
                var color = i < filled ? (gold ? new Color(1f, 0.86f, 0.35f) : StoryTheme.Gold) : StoryUi.Alpha(StoryTheme.Faint, 0.6f);
                var star = Icon(rect, Star, i / (float) total, 0f, (i + 1) / (float) total, 1f, color);
                if (gold && i < filled)
                {
                    // Gold stars get a halo, like the game's own gold stars
                    var halo = Icon(star.rectTransform, Glow, -0.3f, -0.3f, 1.3f, 1.3f, new Color(1f, 0.85f, 0.4f, 0.5f));
                    halo.transform.SetAsFirstSibling();
                }
            }
        }

        /// <summary>A small gold-rimmed tag, for ranks and challenge badges.</summary>
        public static void Badge(RectTransform rect, string text, Color color, float size)
        {
            StoryUi.Rounded(rect, StoryUi.Alpha(color, 0.16f), 0.05f);
            var frame = Frame(rect, 0f, 0f, 1f, 1f, color);
            frame.pixelsPerUnitMultiplier = 0.6f;
            var label = StoryUi.Area(rect, 0f, 0f, 1f, 1f, "Badge");
            StoryUi.Inset(label, 18f, 4f, 18f, 4f);
            StoryUi.Text(label, text, size, true, color, TextAlignmentOptions.Center, false);
        }

        /// <summary>
        /// The tour so far: a dot for every gig at its venue's place on the map, the route drawn in
        /// the order of the story, the stop the story waits on pulsing.
        /// </summary>
        public static void TourMap(RectTransform rect, Vector2 size, StoryEngine engine, StoryChapter selected,
            StoryAnim anim)
        {
            StoryUi.Rounded(rect, StoryTheme.Night, 2f);
            for (int i = 1; i < 6; i++)
            {
                Box(rect, i / 6f, 0.04f, i / 6f + 0.002f, 0.96f, StoryUi.Alpha(StoryTheme.Gold, 0.06f));
            }

            for (int i = 1; i < 4; i++)
            {
                Box(rect, 0.03f, i / 4f, 0.97f, i / 4f + 0.003f, StoryUi.Alpha(StoryTheme.Gold, 0.06f));
            }

            var campaign = engine.Campaign;
            var next = engine.NextGig();
            const float margin = 0.09f;
            Vector2 previous = Vector2.zero;
            bool havePrevious = false, previousWon = false;
            var points = new List<Vector2>();
            foreach (var gig in campaign.AllGigs)
            {
                var venue = campaign.FindVenue(gig.VenueId);
                float fx = margin + Mathf.Clamp01(venue != null ? venue.MapX : 0.5f) * (1f - 2f * margin);
                float fy = margin + Mathf.Clamp01(venue != null ? venue.MapY : 0.5f) * (1f - 2f * margin);
                var point = new Vector2(fx, fy);
                points.Add(point);
                bool won = engine.Progress.IsWon(gig.Id);
                if (havePrevious && (point - previous).sqrMagnitude > 0.00001f)
                {
                    var a = new Vector2(previous.x * size.x, previous.y * size.y);
                    var b = new Vector2(point.x * size.x, point.y * size.y);
                    Line(rect, a, b, 4f, won && previousWon ? StoryUi.Alpha(StoryTheme.Gold, 0.85f) : StoryUi.Alpha(StoryTheme.Muted, 0.25f));
                }

                previous = point;
                previousWon = won;
                havePrevious = true;
            }

            var all = campaign.AllGigs;
            for (int i = 0; i < all.Count; i++)
            {
                var gig = all[i];
                var state = engine.StateOf(gig);
                bool mine = gig.Chapter == selected;
                float dot = mine ? 24f : 16f;
                var color = state == StoryGigState.Won ? StoryTheme.Gold
                    : state == StoryGigState.Open ? StoryTheme.Cream : StoryUi.Alpha(StoryTheme.Faint, 0.8f);
                var holder = StoryUi.Fixed(rect, points[i].x, points[i].y, dot, dot, "Stop");
                Oval(holder, 0f, 0f, 1f, 1f, color);
                if (gig == next)
                {
                    var ring = StoryUi.Fixed(rect, points[i].x, points[i].y, 56f, 56f, "Here");
                    anim.AddPulse(Icon(ring, Hoop, 0f, 0f, 1f, 1f, StoryTheme.Gold), 0.2f, 1f, 0.8f, 0f);
                    var venue = campaign.FindVenue(gig.VenueId);
                    if (venue != null && !string.IsNullOrEmpty(venue.City))
                    {
                        float labelY = points[i].y > 0.75f ? points[i].y - 0.13f : points[i].y + 0.13f;
                        var label = StoryUi.Fixed(rect, Mathf.Clamp(points[i].x, 0.2f, 0.8f), labelY, size.x * 0.4f, 44f, "City");
                        StoryUi.Text(label, venue.City, 30, true, StoryTheme.Gold, TextAlignmentOptions.Center, false);
                    }
                }
            }
        }
    }
}
