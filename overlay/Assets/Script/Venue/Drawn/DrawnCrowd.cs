using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static YARG.Venue.Drawn.VenueShapes;

namespace YARG.Venue.Drawn
{
    /// <summary>
    /// A crowd seen from behind, three rows deep, dancing on the beat.
    ///
    /// Each person is a handful of shapes: a body, a neck, the back of a head with one of several
    /// hairstyles, and two arms that bend at the elbow. Skin tones, hair and clothes are mixed, with
    /// Black concert-goers the largest group, as in the rooms these venues are a tribute to.
    ///
    /// The dances are simplified versions of party dances from the mid-2000s on (Lean wit It, Crank
    /// That, Walk It Out, the Jerk, the Dougie, Cat Daddy, the Nae Nae, the Whip, the Dab, Hit the
    /// Quan, the Milly Rock, the Woah, Swag Surfin' and more). They are drawn as upper-body moves:
    /// lean, bounce, dip and where the arms go, which is what you see of a dance from behind in a
    /// crowd. Neighbors dance the same move together and change every sixteen beats.
    /// </summary>
    public class DrawnCrowd : MonoBehaviour
    {
        // ------------------------------------------------------------------ people

        // Deep to light. The weights make brown skin tones the majority without leaving anyone out.
        private static readonly (Color tone, int weight)[] SkinTones =
        {
            (new Color(0.24f, 0.15f, 0.10f), 16),
            (new Color(0.33f, 0.21f, 0.14f), 20),
            (new Color(0.44f, 0.29f, 0.19f), 20),
            (new Color(0.55f, 0.38f, 0.26f), 14),
            (new Color(0.68f, 0.49f, 0.35f), 10),
            (new Color(0.80f, 0.62f, 0.47f), 8),
            (new Color(0.89f, 0.72f, 0.58f), 7),
            (new Color(0.96f, 0.83f, 0.72f), 5),
        };

        private static readonly Color[] HairColors =
        {
            new(0.05f, 0.04f, 0.04f), new(0.05f, 0.04f, 0.04f), new(0.05f, 0.04f, 0.04f),
            new(0.14f, 0.09f, 0.06f), new(0.25f, 0.15f, 0.08f), new(0.62f, 0.45f, 0.2f),
            new(0.45f, 0.12f, 0.08f), new(0.6f, 0.6f, 0.62f), new(0.75f, 0.2f, 0.45f),
            new(0.2f, 0.4f, 0.75f),
        };

        private static readonly Color[] StreetClothes =
        {
            new(0.86f, 0.15f, 0.18f), new(0.13f, 0.32f, 0.78f), new(0.96f, 0.8f, 0.16f),
            new(0.95f, 0.95f, 0.93f), new(0.09f, 0.09f, 0.1f), new(0.15f, 0.6f, 0.32f),
            new(0.96f, 0.5f, 0.12f), new(0.92f, 0.4f, 0.66f), new(0.45f, 0.25f, 0.7f),
            new(0.5f, 0.5f, 0.52f), new(0.2f, 0.7f, 0.75f), new(0.55f, 0.1f, 0.14f),
        };

        private static readonly Color[] FormalClothes =
        {
            new(0.06f, 0.06f, 0.07f), new(0.06f, 0.06f, 0.07f), new(0.94f, 0.93f, 0.9f),
            new(0.1f, 0.14f, 0.3f), new(0.45f, 0.07f, 0.13f), new(0.06f, 0.35f, 0.27f),
            new(0.78f, 0.62f, 0.25f), new(0.7f, 0.7f, 0.74f), new(0.3f, 0.12f, 0.38f),
        };

        private enum Hair { Fade, Afro, Locs, Braids, Puff, Bald, Cap, Wrap, Long, Bun }

        private sealed class Person
        {
            public RectTransform Root, Body, Head, LeftArm, LeftForearm, RightArm, RightForearm;
            public float Seed;      // 0-1, so people in a group aren't perfect copies
            public int Group;       // neighbors share a dance
            public float Energy;    // how big this person dances
            public Vector2 Home;    // anchored position at rest, in screen heights
        }

        private readonly List<Person> _people = new();
        private RectTransform _area;
        private bool _formal;
        private System.Random _random;

        public void Build(DrawnVenue venue)
        {
            _formal = venue != null && venue.Upscale;
            _random = new System.Random(venue != null ? venue.Seed + 7 : 7);

            _area = Holder(transform, "People", 0f, 0f, 1f, 1f);

            float aspect = Screen.height > 0 ? Screen.width / (float) Screen.height : 16f / 9f;

            // Back row first so the front row is drawn over it
            BuildRow(0, 0.150f, 0.185f, 0.50f, aspect);
            BuildRow(1, 0.055f, 0.235f, 0.74f, aspect);
            BuildRow(2, -0.060f, 0.300f, 1.00f, aspect);
        }

        private void BuildRow(int row, float baseY, float height, float brightness, float aspect)
        {
            // A person's box is 0.62 as wide as it is tall, in real pixels
            float width = height * 0.62f / aspect;
            float spacing = width * 0.78f;
            int count = Mathf.CeilToInt(1f / spacing) + 1;
            float start = -spacing * 0.5f + (row % 2) * spacing * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float x = start + i * spacing + Rand(-0.12f, 0.12f) * spacing;
                float y = baseY + Rand(-0.012f, 0.012f);
                float scale = Rand(0.92f, 1.08f);

                var person = BuildPerson(x, y, width * scale, height * scale, brightness);
                // Groups of four or five neighbors, different in each row
                person.Group = row * 100 + i / (4 + row % 2);
                _people.Add(person);
            }
        }

        private float Rand(float min, float max) => min + (float) _random.NextDouble() * (max - min);

        private Color Pick(Color[] colors) => colors[_random.Next(colors.Length)];

        private Color PickSkin()
        {
            int total = 0;
            foreach (var (_, weight) in SkinTones) total += weight;

            int roll = _random.Next(total);
            foreach (var (tone, weight) in SkinTones)
            {
                if (roll < weight) return tone;
                roll -= weight;
            }
            return SkinTones[0].tone;
        }

        private Person BuildPerson(float x, float y, float width, float height, float brightness)
        {
            // People further back are dimmer and a little cooler, like a real room
            Color Lit(Color color)
            {
                var shaded = Shade(color, brightness);
                return brightness < 1f ? Color.Lerp(shaded, new Color(0.05f, 0.05f, 0.12f), (1f - brightness) * 0.35f) : shaded;
            }

            var skin = Lit(PickSkin());
            var clothes = Lit(Pick(_formal ? FormalClothes : StreetClothes));
            var hairColor = Lit(Pick(HairColors));
            bool shortSleeves = !_formal && _random.Next(3) != 0;
            var sleeve = shortSleeves ? skin : clothes;

            var root = Holder(_area, "Person", x - width / 2f, y, x + width / 2f, y + height);
            root.pivot = new Vector2(0.5f, 0.1f);
            Place(root, x - width / 2f, y, x + width / 2f, y + height);

            var person = new Person
            {
                Root = root,
                Seed = Rand(0f, 1f),
                Energy = Rand(0.75f, 1.15f),
            };

            // Arms go on first so the body covers the shoulders
            person.LeftArm = BuildArm(root, 0.27f, clothes, sleeve, skin, out person.LeftForearm);
            person.RightArm = BuildArm(root, 0.73f, clothes, sleeve, skin, out person.RightForearm);

            // Body, with shoulders a little wider than the waist
            person.Body = Pill(root, 0.25f, -0.1f, 0.75f, 0.62f, clothes).rectTransform;
            Pill(root, 0.17f, 0.47f, 0.83f, 0.655f, clothes);
            if (_formal && _random.Next(2) == 0)
            {
                // A collar showing above a jacket
                Box(root, 0.42f, 0.6f, 0.58f, 0.645f, Lit(new Color(0.94f, 0.93f, 0.9f)));
            }

            // Neck
            Box(root, 0.43f, 0.6f, 0.57f, 0.7f, skin);

            // Head, from behind: mostly hair
            var head = Holder(root, "Head", 0.3f, 0.66f, 0.7f, 0.92f);
            head.pivot = new Vector2(0.5f, 0.1f);
            Place(head, 0.3f, 0.66f, 0.7f, 0.92f);
            person.Head = head;

            // Ears
            Oval(head, -0.07f, 0.3f, 0.1f, 0.6f, skin);
            Oval(head, 0.9f, 0.3f, 1.07f, 0.6f, skin);

            var hair = (Hair) _random.Next(10);
            if (_formal && hair == Hair.Cap) hair = Hair.Fade;

            switch (hair)
            {
                case Hair.Bald:
                    Oval(head, 0f, 0f, 1f, 1f, skin);
                    break;
                case Hair.Fade:
                    Oval(head, 0f, 0f, 1f, 1f, skin);
                    Oval(head, 0.02f, 0.3f, 0.98f, 1.02f, hairColor);
                    break;
                case Hair.Afro:
                    Oval(head, -0.2f, 0.05f, 1.2f, 1.28f, hairColor);
                    break;
                case Hair.Puff:
                    Oval(head, 0f, 0f, 1f, 1f, hairColor);
                    Oval(head, 0.14f, 0.72f, 0.86f, 1.42f, hairColor);
                    break;
                case Hair.Bun:
                    Oval(head, 0f, 0f, 1f, 1f, hairColor);
                    Oval(head, 0.3f, 0.86f, 0.7f, 1.22f, hairColor);
                    break;
                case Hair.Locs:
                case Hair.Braids:
                    Oval(head, -0.04f, 0f, 1.04f, 1.04f, hairColor);
                    int strands = hair == Hair.Locs ? 6 : 8;
                    for (int i = 0; i < strands; i++)
                    {
                        float sx = 0.02f + i * (0.96f / strands);
                        float length = hair == Hair.Locs ? Rand(0.55f, 0.95f) : Rand(0.8f, 1.2f);
                        Pill(head, sx, 0.35f - length, sx + 0.96f / strands * 0.8f, 0.5f, hairColor);
                    }
                    break;
                case Hair.Long:
                    Oval(head, -0.04f, 0f, 1.04f, 1.04f, hairColor);
                    Pill(head, -0.02f, -0.75f, 1.02f, 0.55f, hairColor);
                    break;
                case Hair.Cap:
                    var cap = Lit(Pick(StreetClothes));
                    Oval(head, 0f, 0f, 1f, 1f, skin);
                    Oval(head, -0.02f, 0.28f, 1.02f, 1.05f, cap);
                    // Worn backwards, so the brim points at us
                    Pill(head, 0.2f, 0.22f, 0.8f, 0.42f, Shade(cap, 0.75f));
                    break;
                case Hair.Wrap:
                    var cloth = Lit(Pick(_formal ? FormalClothes : StreetClothes));
                    Oval(head, -0.05f, 0f, 1.05f, 1.12f, cloth);
                    Oval(head, 0.25f, 0.8f, 0.75f, 1.3f, Shade(cloth, 0.85f));
                    break;
            }

            return person;
        }

        // An arm hangs down from the shoulder; the forearm hangs from the elbow. Both turn about their tops.
        private RectTransform BuildArm(RectTransform root, float shoulderX, Color clothes, Color sleeve, Color skin,
            out RectTransform forearm)
        {
            const float shoulderY = 0.6f, length = 0.27f, thickness = 0.085f;

            var upper = Pill(root, shoulderX - thickness, shoulderY - length, shoulderX + thickness, shoulderY + 0.03f, sleeve)
                .rectTransform;
            upper.pivot = new Vector2(0.5f, 0.92f);
            Place(upper, shoulderX - thickness, shoulderY - length, shoulderX + thickness, shoulderY + 0.03f);

            // A short sleeve over the top of a bare arm
            if (sleeve != clothes)
            {
                Pill(upper, -0.08f, 0.5f, 1.08f, 1f, clothes);
            }

            forearm = Pill(upper, 0.06f, -0.88f, 0.94f, 0.14f, sleeve).rectTransform;
            forearm.pivot = new Vector2(0.5f, 0.93f);
            Place(forearm, 0.06f, -0.88f, 0.94f, 0.14f);

            // Hand
            Oval(forearm, -0.05f, -0.08f, 1.05f, 0.24f, skin);

            return upper;
        }

        // ------------------------------------------------------------------ dancing

        /// <summary>
        /// Where a person's body is at one moment. Arm angles are in degrees: 0 hangs straight down,
        /// 90 points out to the side, 180 straight up. The elbow bends the forearm further the same way.
        /// </summary>
        private struct Pose
        {
            public float Lean;      // degrees, positive to the right
            public float Sway;      // sideways, in body widths
            public float Bounce;    // up and down, in body heights
            public float Head;      // head tilt, degrees
            public float LeftArm, LeftElbow, RightArm, RightElbow;

            public static Pose Lerp(in Pose a, in Pose b, float t)
            {
                return new Pose
                {
                    Lean = Mathf.Lerp(a.Lean, b.Lean, t),
                    Sway = Mathf.Lerp(a.Sway, b.Sway, t),
                    Bounce = Mathf.Lerp(a.Bounce, b.Bounce, t),
                    Head = Mathf.Lerp(a.Head, b.Head, t),
                    LeftArm = Mathf.Lerp(a.LeftArm, b.LeftArm, t),
                    LeftElbow = Mathf.Lerp(a.LeftElbow, b.LeftElbow, t),
                    RightArm = Mathf.Lerp(a.RightArm, b.RightArm, t),
                    RightElbow = Mathf.Lerp(a.RightElbow, b.RightElbow, t),
                };
            }
        }

        private enum Dance
        {
            TwoStep, LeanWitIt, CrankThat, WalkItOut, Jerk, Dougie, CatDaddy, NaeNae, Whip, Dab,
            HitTheQuan, MillyRock, Woah, Bend, HandsUp, SwagSurf, Clap,
        }

        private static readonly Dance[] StreetDances =
        {
            Dance.TwoStep, Dance.LeanWitIt, Dance.CrankThat, Dance.WalkItOut, Dance.Jerk, Dance.Dougie,
            Dance.CatDaddy, Dance.NaeNae, Dance.Whip, Dance.Dab, Dance.HitTheQuan, Dance.MillyRock,
            Dance.Woah, Dance.Bend, Dance.HandsUp,
        };

        private static readonly Dance[] FormalDances =
        {
            Dance.TwoStep, Dance.TwoStep, Dance.Clap, Dance.Clap, Dance.LeanWitIt, Dance.Dougie,
            Dance.NaeNae, Dance.MillyRock, Dance.HandsUp,
        };

        private const float SECTION_BEATS = 16f;

        private static float Sin(float turns) => Mathf.Sin(turns * Mathf.PI * 2f);

        // 1 right on the beat, falling to 0 before the next
        private static float Hit(float beat) => Mathf.Pow(1f - Mathf.Repeat(beat, 1f), 2f);

        // A smooth 0 to 1 step, for snapping from one position to the next
        private static float Snap(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 2.5f));

        private static Pose DancePose(Dance dance, float t, float seed)
        {
            // Which way round this person does a one-sided move
            float side = seed < 0.5f ? 1f : -1f;
            var pose = new Pose();

            switch (dance)
            {
                case Dance.TwoStep:
                    pose.Sway = 0.1f * Sin(t / 2f);
                    pose.Bounce = 0.02f * Hit(t);
                    pose.Lean = 3f * Sin(t / 2f);
                    pose.LeftArm = 18f; pose.LeftElbow = 70f + 14f * Sin(t / 2f);
                    pose.RightArm = 18f; pose.RightElbow = 70f - 14f * Sin(t / 2f);
                    break;

                case Dance.Clap:
                    pose.Sway = 0.06f * Sin(t / 2f);
                    pose.Bounce = 0.012f * Hit(t);
                    // Hands meet in front on two and four
                    float clap = Mathf.Repeat(t, 2f) > 1f ? Hit(t) : 0f;
                    pose.LeftArm = 12f; pose.LeftElbow = 95f + 25f * clap;
                    pose.RightArm = 12f; pose.RightElbow = 95f + 25f * clap;
                    break;

                case Dance.LeanWitIt:
                {
                    // Lean one way and snap, rock back the other way
                    float way = Mathf.Repeat(t, 2f) < 1f ? 1f : -1f;
                    float into = Snap(Mathf.Repeat(t, 1f));
                    pose.Lean = way * 15f * into;
                    pose.Sway = way * 0.1f * into;
                    pose.Bounce = -0.02f * into;
                    pose.LeftArm = 30f + (way < 0f ? 35f * into : 0f); pose.LeftElbow = 40f;
                    pose.RightArm = 30f + (way > 0f ? 35f * into : 0f); pose.RightElbow = 40f;
                    break;
                }

                case Dance.CrankThat:
                {
                    float count = Mathf.Repeat(t, 8f);
                    if (count < 4f)
                    {
                        // Hop and cross the feet, wrists crossing low in front
                        pose.Bounce = 0.05f * Hit(t);
                        pose.Sway = 0.06f * Sin(t / 2f);
                        pose.LeftArm = 22f; pose.LeftElbow = 85f + 20f * Sin(t);
                        pose.RightArm = 22f; pose.RightElbow = 85f - 20f * Sin(t);
                    }
                    else if (count < 6f)
                    {
                        // "Superman": dive forward with both arms out
                        float dive = Snap(count - 4f);
                        pose.Bounce = -0.07f * dive + 0.02f * Hit(t);
                        pose.LeftArm = 95f * dive; pose.LeftElbow = 5f;
                        pose.RightArm = 95f * dive; pose.RightElbow = 5f;
                    }
                    else
                    {
                        // Crank the wrists like a throttle, leaning to the side
                        pose.Lean = side * 10f;
                        pose.Bounce = 0.03f * Hit(t * 2f);
                        pose.LeftArm = 55f; pose.LeftElbow = 80f + 30f * Sin(t * 2f);
                        pose.RightArm = 55f; pose.RightElbow = 80f - 30f * Sin(t * 2f);
                    }
                    break;
                }

                case Dance.WalkItOut:
                    // Twisting on the balls of the feet: quick small swivels, shoulders following
                    pose.Sway = 0.05f * Sin(t);
                    pose.Lean = 6f * Sin(t);
                    pose.Bounce = 0.015f * Hit(t * 2f);
                    pose.LeftArm = 28f + 12f * Sin(t); pose.LeftElbow = 75f;
                    pose.RightArm = 28f - 12f * Sin(t); pose.RightElbow = 75f;
                    break;

                case Dance.Jerk:
                    // Knees in and out, dropping low on every half beat
                    pose.Bounce = -0.06f * Mathf.Abs(Sin(t)) + 0.01f;
                    pose.Sway = 0.04f * Sin(t / 2f);
                    pose.LeftArm = 20f; pose.LeftElbow = 55f + 35f * Sin(t);
                    pose.RightArm = 20f; pose.RightElbow = 55f - 35f * Sin(t);
                    break;

                case Dance.Dougie:
                {
                    // Loose side-to-side lean, one hand smoothing over the head, swapping hands
                    pose.Lean = 9f * Sin(t / 2f);
                    pose.Sway = 0.09f * Sin(t / 2f);
                    pose.Bounce = 0.02f * Hit(t);
                    bool rightHand = (Mathf.Repeat(t, 8f) < 4f) == (side > 0f);
                    float brush = 150f + 14f * Sin(t);
                    float bend = 105f + 25f * Sin(t);
                    pose.LeftArm = rightHand ? 22f : brush; pose.LeftElbow = rightHand ? 45f : bend;
                    pose.RightArm = rightHand ? brush : 22f; pose.RightElbow = rightHand ? bend : 45f;
                    pose.Head = -4f * Sin(t / 2f);
                    break;
                }

                case Dance.CatDaddy:
                {
                    // Low in the knees, one arm wheeling round like a wheelchair wheel
                    pose.Bounce = -0.045f + 0.02f * Hit(t);
                    pose.Lean = side * 6f;
                    float wheel = 60f + 55f * Sin(t / 2f);
                    float wheelBend = 60f + 50f * Mathf.Cos(t * Mathf.PI);
                    pose.LeftArm = side > 0f ? 25f : wheel; pose.LeftElbow = side > 0f ? 60f : wheelBend;
                    pose.RightArm = side > 0f ? wheel : 25f; pose.RightElbow = side > 0f ? wheelBend : 60f;
                    break;
                }

                case Dance.NaeNae:
                {
                    // One hand up, waving, hips swaying the other way
                    pose.Sway = -side * 0.08f * Sin(t / 2f);
                    pose.Lean = -side * 5f * Sin(t / 2f);
                    pose.Bounce = 0.025f * Hit(t);
                    float wave = 160f + 16f * Sin(t / 2f);
                    pose.LeftArm = side > 0f ? 20f : wave; pose.LeftElbow = side > 0f ? 50f : 18f;
                    pose.RightArm = side > 0f ? wave : 20f; pose.RightElbow = side > 0f ? 18f : 50f;
                    break;
                }

                case Dance.Whip:
                {
                    // Punch one arm out like grabbing a steering wheel, hold, then the other
                    bool right = Mathf.Repeat(t, 4f) < 2f == (side > 0f);
                    float punch = Snap(Mathf.Repeat(t, 2f));
                    pose.Lean = (right ? 1f : -1f) * 8f * punch;
                    pose.Bounce = -0.03f * punch + 0.015f * Hit(t);
                    pose.LeftArm = right ? 30f : 30f + 68f * punch; pose.LeftElbow = right ? 85f : 85f * (1f - punch);
                    pose.RightArm = right ? 30f + 68f * punch : 30f; pose.RightElbow = right ? 85f * (1f - punch) : 85f;
                    break;
                }

                case Dance.Dab:
                {
                    // Head into one elbow, the other arm thrown out along the same line; then bounce
                    float count = Mathf.Repeat(t, 4f);
                    bool right = Mathf.Repeat(t, 8f) < 4f == (side > 0f);
                    float dab = count < 2f ? Snap(count) : 1f - Snap(count - 2f);
                    float way = right ? 1f : -1f;
                    pose.Lean = way * 12f * dab;
                    pose.Head = way * -22f * dab;
                    pose.Bounce = 0.02f * Hit(t) * (1f - dab);
                    float straight = 25f + 110f * dab, fold = 25f + 85f * dab;
                    pose.LeftArm = right ? fold : straight; pose.LeftElbow = right ? 60f + 80f * dab : 60f * (1f - dab);
                    pose.RightArm = right ? straight : fold; pose.RightElbow = right ? 60f * (1f - dab) : 60f + 80f * dab;
                    break;
                }

                case Dance.HitTheQuan:
                    // Down low, both arms swinging across the body together
                    pose.Bounce = -0.06f + 0.02f * Hit(t);
                    pose.Lean = -8f * Sin(t / 2f);
                    pose.Sway = 0.07f * Sin(t / 2f);
                    pose.LeftArm = 30f - 28f * Sin(t / 2f); pose.LeftElbow = 25f;
                    pose.RightArm = 30f + 28f * Sin(t / 2f); pose.RightElbow = 25f;
                    break;

                case Dance.MillyRock:
                    // Arms sweeping out to one side then the other, body going with them
                    pose.Sway = 0.08f * Sin(t / 2f);
                    pose.Lean = 7f * Sin(t / 2f);
                    pose.Bounce = 0.02f * Hit(t);
                    pose.LeftArm = 60f - 42f * Sin(t / 2f); pose.LeftElbow = 30f;
                    pose.RightArm = 60f + 42f * Sin(t / 2f); pose.RightElbow = 30f;
                    break;

                case Dance.Woah:
                {
                    // Wind the arms up, then lock them on the beat with a little jolt
                    float count = Mathf.Repeat(t, 2f);
                    float locked = count < 1f ? 0f : Snap(count - 1f);
                    pose.Lean = side * (4f * Sin(t) * (1f - locked) + 9f * locked);
                    pose.Bounce = -0.025f * locked + 0.02f * Hit(t) * (1f - locked);
                    pose.LeftArm = 55f + 20f * Sin(t) * (1f - locked) + 15f * locked;
                    pose.LeftElbow = 70f + 30f * Mathf.Cos(t * Mathf.PI * 2f) * (1f - locked) + 30f * locked;
                    pose.RightArm = 55f - 20f * Sin(t) * (1f - locked) + 25f * locked;
                    pose.RightElbow = 70f - 30f * Mathf.Cos(t * Mathf.PI * 2f) * (1f - locked) + 10f * locked;
                    break;
                }

                case Dance.Bend:
                    // Drop down on the beat and come back up slowly
                    pose.Bounce = -0.09f * Hit(t / 2f);
                    pose.Sway = 0.04f * Sin(t / 4f);
                    pose.LeftArm = 22f + 20f * Hit(t / 2f); pose.LeftElbow = 20f;
                    pose.RightArm = 22f + 20f * Hit(t / 2f); pose.RightElbow = 20f;
                    break;

                case Dance.HandsUp:
                    // Hands in the air, waving side to side
                    pose.Sway = 0.06f * Sin(t / 2f);
                    pose.Bounce = 0.035f * Hit(t);
                    pose.LeftArm = 165f + 14f * Sin(t / 2f); pose.LeftElbow = 12f;
                    pose.RightArm = 165f - 14f * Sin(t / 2f); pose.RightElbow = 12f;
                    break;

                case Dance.SwagSurf:
                    // Arms over each other's shoulders, the whole room rocking as one
                    pose.Sway = 0.22f * Sin(t / 4f);
                    pose.Lean = 11f * Sin(t / 4f);
                    pose.Bounce = 0.015f * Hit(t);
                    pose.LeftArm = 88f; pose.LeftElbow = 8f;
                    pose.RightArm = 88f; pose.RightElbow = 8f;
                    break;
            }

            return pose;
        }

        // A repeatable "random" number from 0 to 1 for a pair of whole numbers
        private static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint) a * 374761393u + (uint) b * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float) 0x1000000;
            }
        }

        private Dance DanceFor(int group, int section)
        {
            // Now and then the whole room does the same thing
            float together = Hash(-1, section);
            if (together < 0.12f) return Dance.SwagSurf;
            if (together < 0.2f) return Dance.HandsUp;

            var dances = _formal ? FormalDances : StreetDances;
            return dances[(int) (Hash(group, section) * dances.Length) % dances.Length];
        }

        // Which way a bent elbow takes the forearm. With the arm down by the side the hand comes in
        // across the front of the body (and is hidden by it, seen from behind); with the arm raised
        // the hand comes in over the head. In between it straightens out.
        private static float ElbowTurn(float arm, float elbow)
        {
            float raised = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((arm - 50f) / 50f));
            return elbow * Mathf.Lerp(-1f, 1f, raised);
        }

        /// <summary>Moves everyone. <paramref name="beat"/> counts beats of the song, with fractions.</summary>
        public void Animate(float beat)
        {
            if (_area == null) return;

            float screenHeight = _area.rect.height;
            if (screenHeight <= 0f) return;

            int section = Mathf.FloorToInt(beat / SECTION_BEATS);
            float intoSection = beat - section * SECTION_BEATS;

            foreach (var person in _people)
            {
                if (person.Root == null) continue;

                var dance = DanceFor(person.Group, section);
                // Everyone is in step for the group dances; otherwise people are a touch loose
                float loose = dance == Dance.SwagSurf ? 0f : (person.Seed - 0.5f) * 0.12f;
                var pose = DancePose(dance, beat + loose, person.Seed);

                // Ease out of the last dance over the first beat of a new one
                if (intoSection < 1f && section > 0)
                {
                    var previous = DancePose(DanceFor(person.Group, section - 1), beat + loose, person.Seed);
                    pose = Pose.Lerp(previous, pose, Mathf.SmoothStep(0f, 1f, intoSection));
                }

                float energy = dance == Dance.SwagSurf ? 1f : person.Energy;
                float bodyHeight = person.Root.rect.height;
                float bodyWidth = person.Root.rect.width;

                person.Root.anchoredPosition = new Vector2(pose.Sway * energy * bodyWidth, pose.Bounce * energy * bodyHeight);
                person.Root.localRotation = Quaternion.Euler(0f, 0f, -pose.Lean * energy);
                person.Head.localRotation = Quaternion.Euler(0f, 0f, -pose.Head);

                // The left arm swings out to the left (clockwise from hanging), the right arm to the right
                person.LeftArm.localRotation = Quaternion.Euler(0f, 0f, -pose.LeftArm);
                person.LeftForearm.localRotation = Quaternion.Euler(0f, 0f, -ElbowTurn(pose.LeftArm, pose.LeftElbow));
                person.RightArm.localRotation = Quaternion.Euler(0f, 0f, pose.RightArm);
                person.RightForearm.localRotation = Quaternion.Euler(0f, 0f, ElbowTurn(pose.RightArm, pose.RightElbow));
            }
        }
    }
}
