using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static YARG.Venue.Drawn.VenueShapes;

namespace YARG.Venue.Drawn
{
    /// <summary>
    /// Draws a venue's stage out of simple shapes, in one of six styles, and keeps its lights moving
    /// with the music. Everything is placed in 0-1 coordinates of the screen (y up). The middle of the
    /// screen is where the note highway sits, so the detail is kept to the sides and the top.
    /// </summary>
    public class DrawnStage : MonoBehaviour
    {
        private struct Pulse
        {
            public Graphic Graphic;
            public Color Color;
            public float Low, High;  // alpha between beats and on the beat
            public float Offset;     // in beats, so not everything flashes together
        }

        private struct Sweep
        {
            public RectTransform Beam;
            public float Center, Range, Speed, Offset;
        }

        private readonly List<Pulse> _pulses = new();
        private readonly List<Sweep> _sweeps = new();
        private readonly List<(RectTransform rect, float amount)> _thumpers = new();

        private System.Random _random;
        private DrawnVenue _venue;
        private Transform _root;

        public void Build(DrawnVenue venue)
        {
            _venue = venue;
            _root = transform;
            _random = new System.Random(venue.Seed);

            // Sky or back wall
            var backdrop = new GameObject("Backdrop", typeof(RectTransform)).AddComponent<RawImage>();
            backdrop.transform.SetParent(_root, false);
            Place(backdrop.rectTransform, 0f, 0f, 1f, 1f);
            backdrop.texture = Gradient(venue.Top, venue.Bottom);
            backdrop.raycastTarget = false;

            switch (venue.Style)
            {
                case VenueStyle.Theater: BuildTheater(); break;
                case VenueStyle.Arena: BuildArena(); break;
                case VenueStyle.Club: BuildClub(); break;
                case VenueStyle.Street: BuildStreet(); break;
                case VenueStyle.Upscale: BuildUpscale(); break;
                case VenueStyle.Festival: BuildFestival(); break;
            }

            BuildLights();

            // The floor the crowd stands on
            var floor = new GameObject("Floor", typeof(RectTransform)).AddComponent<RawImage>();
            floor.transform.SetParent(_root, false);
            Place(floor.rectTransform, 0f, 0f, 1f, 0.2f);
            floor.texture = Gradient(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.75f));
            floor.raycastTarget = false;
        }

        private float Rand(float min, float max) => min + (float) _random.NextDouble() * (max - min);

        private void AddPulse(Graphic graphic, float low, float high, float offset = 0f)
        {
            _pulses.Add(new Pulse { Graphic = graphic, Color = graphic.color, Low = low, High = high, Offset = offset });
        }

        // ------------------------------------------------------------------ styles

        private void BuildTheater()
        {
            var gold = _venue.Accent;
            var drape = _venue.Accent2;

            // Curtains: folds of two shades, gathered at the sides
            for (int side = 0; side < 2; side++)
            {
                for (int fold = 0; fold < 7; fold++)
                {
                    float x0 = 0.07f + fold * 0.026f;
                    float x1 = x0 + 0.027f;
                    float bottom = 0.16f + fold * 0.035f;
                    var color = Shade(drape, fold % 2 == 0 ? 0.95f : 0.65f);
                    if (side == 1) (x0, x1) = (1f - x1, 1f - x0);
                    Box(_root, x0, bottom, x1, 0.88f, color);
                }
            }

            // Scalloped valance along the top
            Box(_root, 0.07f, 0.84f, 0.93f, 0.90f, Shade(drape, 0.8f));
            for (int i = 0; i < 12; i++)
            {
                float x = 0.07f + i * (0.86f / 12f);
                Oval(_root, x, 0.795f, x + 0.86f / 12f, 0.885f, Shade(drape, i % 2 == 0 ? 0.9f : 0.7f));
            }

            // Gold proscenium
            Box(_root, 0.045f, 0.12f, 0.07f, 0.93f, gold);
            Box(_root, 0.93f, 0.12f, 0.955f, 0.93f, gold);
            Box(_root, 0.045f, 0.90f, 0.955f, 0.935f, gold);
            Box(_root, 0.03f, 0.935f, 0.97f, 0.955f, Shade(gold, 0.7f));

            // Marquee bulbs along the arch, chasing
            for (int i = 0; i < 30; i++)
            {
                float x = 0.06f + i * (0.88f / 29f);
                var bulb = Oval(_root, x - 0.006f, 0.908f, x + 0.006f, 0.928f, new Color(1f, 0.95f, 0.75f));
                AddPulse(bulb, 0.35f, 1f, i * 0.25f);
            }

            // Footlights along the front of the stage
            for (int i = 0; i < 16; i++)
            {
                float x = 0.1f + i * (0.8f / 15f);
                var light = Oval(_root, x - 0.012f, 0.19f, x + 0.012f, 0.225f, Alpha(gold, 0.8f));
                AddPulse(light, 0.3f, 0.9f, i % 2 * 0.5f);
            }
        }

        private void BuildArena()
        {
            // Three tiers of seats, each a darker band with a rail of light
            for (int tier = 0; tier < 3; tier++)
            {
                float y0 = 0.36f + tier * 0.17f;
                Box(_root, 0f, y0, 1f, y0 + 0.15f, new Color(0f, 0f, 0f, 0.28f));
                Box(_root, 0f, y0 + 0.15f, 1f, y0 + 0.156f, Alpha(_venue.Accent, 0.35f));
            }

            // Phone lights and camera flashes in the stands
            for (int i = 0; i < 150; i++)
            {
                float x = Rand(0.01f, 0.99f);
                // Fewer in the middle, where the highway is
                if (x > 0.36f && x < 0.64f && _random.Next(3) != 0) continue;

                float y = Rand(0.37f, 0.86f);
                float size = Rand(0.0025f, 0.005f);
                var dot = Oval(_root, x - size, y - size * 1.8f, x + size, y + size * 1.8f,
                    Color.Lerp(Color.white, _venue.Accent, Rand(0f, 0.6f)));
                if (i % 3 == 0)
                {
                    AddPulse(dot, 0.15f, 1f, Rand(0f, 4f));
                }
                else
                {
                    dot.color = Alpha(dot.color, Rand(0.35f, 0.8f));
                }
            }

            // Lighting truss across the top
            Box(_root, 0f, 0.93f, 1f, 0.95f, new Color(0.08f, 0.08f, 0.1f));
            for (int i = 0; i < 24; i++)
            {
                float x = 0.02f + i * (0.96f / 23f);
                Box(_root, x - 0.002f, 0.90f, x + 0.002f, 0.93f, new Color(0.12f, 0.12f, 0.14f));
            }

            // Big screens either side
            for (int side = 0; side < 2; side++)
            {
                float x0 = side == 0 ? 0.05f : 0.75f;
                Box(_root, x0 - 0.006f, 0.59f, x0 + 0.206f, 0.83f, new Color(0.03f, 0.03f, 0.04f));
                var screen = Box(_root, x0, 0.6f, x0 + 0.2f, 0.82f, side == 0 ? _venue.Accent : _venue.Accent2);
                AddPulse(screen, 0.35f, 0.85f, side * 0.5f);
                // Bars on the screen, like an equalizer
                for (int bar = 0; bar < 8; bar++)
                {
                    float bx = x0 + 0.012f + bar * 0.023f;
                    var eq = Box(_root, bx, 0.615f, bx + 0.016f, 0.615f + Rand(0.05f, 0.18f), new Color(1f, 1f, 1f, 0.5f));
                    AddPulse(eq, 0.15f, 0.7f, bar * 0.5f);
                }
            }
        }

        private void BuildClub()
        {
            // Brick wall
            var brick = new Color(1f, 1f, 1f, 0.045f);
            for (int row = 0; row < 14; row++)
            {
                float y = 0.24f + row * 0.05f;
                float shift = row % 2 * 0.04f;
                for (int col = -1; col < 13; col++)
                {
                    float x = col * 0.08f + shift;
                    if (_random.Next(4) == 0) continue;
                    Box(_root, x + 0.003f, y + 0.004f, x + 0.077f, y + 0.046f, brick);
                }
            }

            // Low ceiling
            Box(_root, 0f, 0.94f, 1f, 1f, new Color(0f, 0f, 0f, 0.6f));

            // Neon sign either side: a tube frame with bars inside
            for (int side = 0; side < 2; side++)
            {
                float x0 = side == 0 ? 0.06f : 0.72f;
                var color = side == 0 ? _venue.Accent : _venue.Accent2;
                var parts = new List<Image>
                {
                    Pill(_root, x0, 0.80f, x0 + 0.22f, 0.815f, color),
                    Pill(_root, x0, 0.60f, x0 + 0.22f, 0.615f, color),
                    Pill(_root, x0, 0.60f, x0 + 0.009f, 0.815f, color),
                    Pill(_root, x0 + 0.211f, 0.60f, x0 + 0.22f, 0.815f, color),
                };
                for (int bar = 0; bar < 5; bar++)
                {
                    float bx = x0 + 0.03f + bar * 0.036f;
                    float height = Rand(0.05f, 0.14f);
                    parts.Add(Pill(_root, bx, 0.635f, bx + 0.012f, 0.635f + height,
                        bar % 2 == 0 ? _venue.Accent2 : _venue.Accent));
                }

                foreach (var part in parts)
                {
                    AddPulse(part, 0.55f, 1f, side * 0.5f);
                }

                // The glow on the wall behind it
                var glow = Oval(_root, x0 - 0.06f, 0.5f, x0 + 0.28f, 0.92f, Alpha(color, 0.12f));
                glow.transform.SetSiblingIndex(1);
                AddPulse(glow, 0.06f, 0.18f, side * 0.5f);
            }

            BuildSpeakers(0.0f, 0.2f);
            BuildSpeakers(0.9f, 0.2f);

            // String of bulbs under the ceiling
            for (int i = 0; i < 26; i++)
            {
                float x = 0.02f + i * (0.96f / 25f);
                float sag = Mathf.Sin(i / 25f * Mathf.PI * 3f) * 0.012f;
                var bulb = Oval(_root, x - 0.004f, 0.915f + sag, x + 0.004f, 0.93f + sag, new Color(1f, 0.9f, 0.6f));
                AddPulse(bulb, 0.4f, 1f, i * 0.5f);
            }
        }

        // A stack of two speaker cabinets whose cones thump on the beat
        private void BuildSpeakers(float x, float y)
        {
            for (int box = 0; box < 2; box++)
            {
                float y0 = y + box * 0.2f;
                Box(_root, x, y0, x + 0.1f, y0 + 0.195f, new Color(0.05f, 0.05f, 0.06f));
                Box(_root, x + 0.004f, y0 + 0.006f, x + 0.096f, y0 + 0.189f, new Color(0.1f, 0.1f, 0.12f));
                var cone = Oval(_root, x + 0.018f, y0 + 0.03f, x + 0.082f, y0 + 0.14f, new Color(0.02f, 0.02f, 0.03f));
                Oval(cone.transform, 0.3f, 0.3f, 0.7f, 0.7f, new Color(0.16f, 0.16f, 0.18f));
                Oval(_root, x + 0.04f, y0 + 0.15f, x + 0.06f, y0 + 0.182f, new Color(0.02f, 0.02f, 0.03f));
                _thumpers.Add((cone.rectTransform, 0.12f));
            }
        }

        private void BuildStreet()
        {
            // Sun or moon low in the sky
            Circle(_root, 0.81f, 0.71f, 0.05f, Alpha(_venue.Accent, 0.85f));

            // Skyline: far row then near row
            for (int row = 0; row < 2; row++)
            {
                var wall = row == 0 ? new Color(0.09f, 0.07f, 0.16f, 0.85f) : new Color(0.04f, 0.03f, 0.08f);
                float x = -0.02f;
                while (x < 1f)
                {
                    float width = Rand(0.05f, 0.11f);
                    float height = row == 0 ? Rand(0.3f, 0.52f) : Rand(0.14f, 0.34f);
                    float baseY = 0.2f;
                    Box(_root, x, baseY, x + width, baseY + height, wall);

                    // Lit windows
                    int columns = Mathf.Max(2, (int) (width / 0.018f));
                    int rows = Mathf.Max(2, (int) (height / 0.045f));
                    for (int c = 0; c < columns; c++)
                    {
                        for (int r = 1; r < rows; r++)
                        {
                            if (_random.Next(100) > (row == 0 ? 22 : 30)) continue;
                            float wx = x + (c + 0.25f) * (width / columns);
                            float wy = baseY + (r + 0.2f) * (height / rows);
                            Box(_root, wx, wy, wx + width / columns * 0.5f, wy + height / rows * 0.5f,
                                Alpha(_venue.Accent, row == 0 ? 0.45f : 0.8f));
                        }
                    }

                    x += width + Rand(0.002f, 0.012f);
                }
            }

            // String lights across the block
            for (int strand = 0; strand < 2; strand++)
            {
                for (int i = 0; i < 28; i++)
                {
                    float t = i / 27f;
                    float y = 0.86f - strand * 0.09f - Mathf.Sin(t * Mathf.PI) * 0.07f;
                    var color = (i + strand) % 3 == 0 ? _venue.Accent : (i + strand) % 3 == 1 ? _venue.Accent2 : Color.white;
                    var bulb = Oval(_root, t - 0.005f, y - 0.009f, t + 0.005f, y + 0.009f, color);
                    AddPulse(bulb, 0.45f, 1f, i * 0.5f);
                }
            }

            // The sound system: stacks and a table with two turntables
            BuildSpeakers(0.01f, 0.2f);
            BuildSpeakers(0.89f, 0.2f);
            Box(_root, 0.13f, 0.2f, 0.27f, 0.3f, new Color(0.07f, 0.06f, 0.09f));
            Box(_root, 0.125f, 0.3f, 0.275f, 0.315f, new Color(0.16f, 0.14f, 0.2f));
            for (int deck = 0; deck < 2; deck++)
            {
                float dx = 0.145f + deck * 0.065f;
                Oval(_root, dx, 0.315f, dx + 0.045f, 0.345f, new Color(0.02f, 0.02f, 0.03f));
                Oval(_root, dx + 0.016f, 0.325f, dx + 0.029f, 0.335f, _venue.Accent2);
            }
        }

        private void BuildUpscale()
        {
            var gold = _venue.Accent;

            // Marble columns
            foreach (float x in new[] { 0.04f, 0.2f, 0.76f, 0.92f })
            {
                Box(_root, x, 0.2f, x + 0.04f, 0.9f, new Color(0.93f, 0.9f, 0.84f, 0.92f));
                Box(_root, x + 0.027f, 0.2f, x + 0.04f, 0.9f, new Color(0f, 0f, 0f, 0.18f));
                Box(_root, x - 0.008f, 0.88f, x + 0.048f, 0.905f, gold);
                Box(_root, x - 0.008f, 0.2f, x + 0.048f, 0.225f, gold);
            }

            // Drapes between the columns
            for (int side = 0; side < 2; side++)
            {
                for (int fold = 0; fold < 6; fold++)
                {
                    float x0 = 0.085f + fold * 0.019f;
                    float x1 = x0 + 0.02f;
                    if (side == 1) (x0, x1) = (1f - x1, 1f - x0);
                    Box(_root, x0, 0.24f, x1, 0.88f, Shade(_venue.Accent2, fold % 2 == 0 ? 0.9f : 0.6f));
                }
            }

            // Cornice
            Box(_root, 0f, 0.905f, 1f, 0.93f, Shade(gold, 0.75f));
            Box(_root, 0f, 0.93f, 1f, 1f, new Color(0f, 0f, 0f, 0.35f));

            // Chandeliers
            foreach (float cx in new[] { 0.3f, 0.7f })
            {
                Box(_root, cx - 0.001f, 0.86f, cx + 0.001f, 0.93f, gold);
                var glow = Oval(_root, cx - 0.09f, 0.66f, cx + 0.09f, 0.94f, Alpha(gold, 0.13f));
                AddPulse(glow, 0.08f, 0.2f);
                for (int ring = 0; ring < 3; ring++)
                {
                    int count = 9 - ring * 2;
                    float radius = 0.055f - ring * 0.014f;
                    float y = 0.80f - ring * 0.035f;
                    for (int i = 0; i < count; i++)
                    {
                        float t = count == 1 ? 0.5f : i / (float) (count - 1);
                        float x = cx - radius + t * radius * 2f;
                        float drop = Mathf.Sin(t * Mathf.PI) * 0.018f;
                        var crystal = Oval(_root, x - 0.004f, y - drop - 0.011f, x + 0.004f, y - drop + 0.011f,
                            new Color(1f, 0.96f, 0.82f));
                        AddPulse(crystal, 0.5f, 1f, Rand(0f, 2f));
                    }
                }
            }

            // Polished floor catching the light
            Box(_root, 0f, 0.185f, 1f, 0.2f, Alpha(gold, 0.35f));
        }

        private void BuildFestival()
        {
            // Sun going down
            Circle(_root, 0.5f, 0.62f, 0.075f, Alpha(_venue.Accent, 0.9f));
            Circle(_root, 0.5f, 0.62f, 0.13f, Alpha(_venue.Accent, 0.18f));

            // Far hills or skyline
            for (int i = 0; i < 7; i++)
            {
                float x = -0.1f + i * 0.18f;
                Oval(_root, x, 0.06f, x + 0.3f, 0.36f + Rand(0f, 0.06f), new Color(0.05f, 0.04f, 0.1f, 0.75f));
            }

            // Palms either side
            BuildPalm(0.08f, 0.2f, 0.52f, -6f);
            BuildPalm(0.2f, 0.2f, 0.4f, 5f);
            BuildPalm(0.92f, 0.2f, 0.5f, 7f);
            BuildPalm(0.8f, 0.2f, 0.42f, -4f);

            // Stage roof: towers and a truss with banners
            var steel = new Color(0.06f, 0.06f, 0.08f);
            Box(_root, 0.0f, 0.2f, 0.018f, 0.95f, steel);
            Box(_root, 0.982f, 0.2f, 1f, 0.95f, steel);
            Box(_root, 0f, 0.92f, 1f, 0.95f, steel);
            for (int i = 0; i < 20; i++)
            {
                float x = 0.03f + i * (0.94f / 19f);
                var color = i % 2 == 0 ? _venue.Accent2 : _venue.Accent;
                var flag = Box(_root, x - 0.012f, 0.875f, x + 0.012f, 0.92f, Alpha(color, 0.85f));
                AddPulse(flag, 0.5f, 0.95f, i * 0.5f);
            }
        }

        private void BuildPalm(float x, float y, float height, float lean)
        {
            var dark = new Color(0.03f, 0.03f, 0.06f);
            var palm = Holder(_root, "Palm", x - 0.1f, y, x + 0.1f, y + height);
            palm.pivot = new Vector2(0.5f, 0f);
            Place(palm, x - 0.1f, y, x + 0.1f, y + height);
            palm.localRotation = Quaternion.Euler(0f, 0f, lean);

            Pill(palm, 0.47f, 0f, 0.53f, 0.82f, dark);
            for (int i = 0; i < 7; i++)
            {
                var leaf = Pill(palm, 0.47f, 0.78f, 0.53f, 1.18f, dark);
                leaf.rectTransform.pivot = new Vector2(0.5f, 0f);
                Place(leaf.rectTransform, 0.47f, 0.78f, 0.53f, 1.18f);
                leaf.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -105f + i * 35f);
            }
        }

        // ------------------------------------------------------------------ lights

        private void BuildLights()
        {
            bool outdoors = _venue.Style is VenueStyle.Street or VenueStyle.Festival;
            int count = _venue.Style == VenueStyle.Arena ? 8 : _venue.Style == VenueStyle.Upscale ? 4 : 6;
            float strength = outdoors ? 0.22f : _venue.Style == VenueStyle.Upscale ? 0.2f : 0.34f;

            for (int i = 0; i < count; i++)
            {
                float x = 0.06f + i * (0.88f / (count - 1));
                var color = i % 2 == 0 ? _venue.Accent : _venue.Accent2;
                var beam = LightBeam(_root, x, 0.97f, 0.85f, 0.2f, Alpha(color, strength));

                AddPulse(beam, strength * 0.45f, strength, i % 2 * 0.5f);
                _sweeps.Add(new Sweep
                {
                    Beam = beam.rectTransform,
                    Center = (0.5f - x) * 30f,
                    Range = _venue.Style == VenueStyle.Upscale ? 6f : 22f,
                    Speed = 0.125f + (i % 3) * 0.0625f,
                    Offset = i * 0.37f,
                });

                // The lamp itself
                var lamp = Oval(_root, x - 0.008f, 0.955f, x + 0.008f, 0.985f, Color.Lerp(color, Color.white, 0.5f));
                AddPulse(lamp, 0.6f, 1f, i % 2 * 0.5f);
            }
        }

        /// <summary>Moves the lights. <paramref name="beat"/> counts beats of the song, with fractions.</summary>
        public void Animate(float beat)
        {
            foreach (var pulse in _pulses)
            {
                if (pulse.Graphic == null) continue;

                // Brightest right on the beat, fading until the next one
                float phase = Mathf.Repeat(beat + pulse.Offset, 1f);
                float glow = Mathf.Pow(1f - phase, 2f);
                var color = pulse.Color;
                color.a = Mathf.Lerp(pulse.Low, pulse.High, glow);
                pulse.Graphic.color = color;
            }

            foreach (var sweep in _sweeps)
            {
                if (sweep.Beam == null) continue;

                float angle = sweep.Center + Mathf.Sin((beat * sweep.Speed + sweep.Offset) * Mathf.PI * 2f) * sweep.Range;
                sweep.Beam.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            foreach (var (rect, amount) in _thumpers)
            {
                if (rect == null) continue;

                float phase = Mathf.Repeat(beat, 1f);
                float scale = 1f + amount * Mathf.Pow(1f - phase, 3f);
                rect.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
