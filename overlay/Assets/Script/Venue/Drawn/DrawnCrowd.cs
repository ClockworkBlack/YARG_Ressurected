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
    /// crowd, along with later ones (Shmoney, the Griddy, Hit Dem Folks, the Stanky Leg, the
    /// Renegade, the Tootsie Slide, the Shoot) and plain nodding, fist pumps and jumping.
    /// Neighbors dance the same move together and change every sixteen beats, each group on its
    /// own bar. Slow songs get swaying and phones held up as lights.
    ///
    /// Every part of the body follows its dance through a spring, so moves flow into each other:
    /// the body leads, the head and forearms follow a moment later and swing through.
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
            public Vector2 HeadHome;
            public float GroupOffset;   // in beats: groups change dance on different bars

            public PhoneLight Phone, LeftPhone;  // only some people hold one up

            public Dance Dance, PreviousDance;
            public float SwitchBeat = float.NegativeInfinity;
            public Pose Current;
            public readonly float[] Velocity = new float[Pose.COUNT];
            public bool Started;
        }

        private sealed class PhoneLight
        {
            public Image Screen, Glow;
            public Color ScreenColor, GlowColor;
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
                person.GroupOffset = Mathf.Floor((float) Hash(person.Group, -7) * 4f) * 4f;
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
            person.HeadHome = head.anchoredPosition;

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

            // About one in three holds a phone up as a light in slow songs and big moments
            if (_random.Next(3) == 0)
            {
                person.Phone = BuildPhone(person.RightForearm, brightness);
                person.LeftPhone = BuildPhone(person.LeftForearm, brightness);
            }

            return person;
        }

        private PhoneLight BuildPhone(RectTransform forearm, float brightness)
        {
            var screenColor = Color.Lerp(new Color(0.85f, 0.92f, 1f), new Color(1f, 0.95f, 0.8f), Rand(0f, 1f));
            var glowColor = Alpha(screenColor, 0.28f * Mathf.Lerp(0.6f, 1f, brightness));
            var phone = new PhoneLight
            {
                // Glow first, so the screen is on top of it
                Glow = Oval(forearm, -0.9f, -0.95f, 1.9f, 0.35f, glowColor),
                Screen = Box(forearm, 0.12f, -0.42f, 0.88f, 0.02f, screenColor),
                ScreenColor = screenColor,
                GlowColor = glowColor,
            };
            phone.Glow.raycastTarget = false;
            phone.Screen.raycastTarget = false;
            phone.Glow.enabled = false;
            phone.Screen.enabled = false;
            return phone;
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
            public float Twist;     // turning the shoulders, -1 to 1 (seen from behind it narrows the body)
            public float Nod;       // head dipping forward, 0 to 1
            public float Phone;     // a phone held up as a light, 0 or 1

            public const int COUNT = 11;

            public float this[int i]
            {
                readonly get => i switch
                {
                    0 => Lean, 1 => Sway, 2 => Bounce, 3 => Head, 4 => LeftArm, 5 => LeftElbow,
                    6 => RightArm, 7 => RightElbow, 8 => Twist, 9 => Nod, _ => Phone,
                };
                set
                {
                    switch (i)
                    {
                        case 0: Lean = value; break;
                        case 1: Sway = value; break;
                        case 2: Bounce = value; break;
                        case 3: Head = value; break;
                        case 4: LeftArm = value; break;
                        case 5: LeftElbow = value; break;
                        case 6: RightArm = value; break;
                        case 7: RightElbow = value; break;
                        case 8: Twist = value; break;
                        case 9: Nod = value; break;
                        default: Phone = value; break;
                    }
                }
            }

            public static Pose Lerp(in Pose a, in Pose b, float t)
            {
                var pose = new Pose();
                for (int i = 0; i < COUNT; i++) pose[i] = Mathf.Lerp(a[i], b[i], t);
                return pose;
            }
        }

        // How quickly each part of the body follows where the dance wants it (in Hz) and how much
        // it overshoots (damping under 1 swings past and settles). The body leads; the head and
        // forearms follow a moment later and swing through, which is what makes it look loose.
        private static readonly float[] Frequency = { 3.0f, 2.6f, 4.6f, 2.1f, 3.4f, 2.7f, 3.4f, 2.7f, 3.5f, 4.0f, 3.0f };
        private static readonly float[] Damping =   { 0.80f, 0.85f, 0.62f, 0.45f, 0.70f, 0.55f, 0.70f, 0.55f, 0.80f, 0.60f, 1.0f };

        private enum Dance
        {
            TwoStep, LeanWitIt, CrankThat, WalkItOut, Jerk, Dougie, CatDaddy, NaeNae, Whip, Dab,
            HitTheQuan, MillyRock, Woah, Bend, HandsUp, SwagSurf, Clap,
            Shmoney, Griddy, HitDemFolks, Wobble, StankyLeg, RunningMan, Shimmy, Shoot, TootsieSlide,
            Renegade, Bop, FistPump, Jump, Wave,
        }

        private static readonly Dance[] StreetDances =
        {
            Dance.TwoStep, Dance.LeanWitIt, Dance.CrankThat, Dance.WalkItOut, Dance.Jerk, Dance.Dougie,
            Dance.CatDaddy, Dance.NaeNae, Dance.Whip, Dance.Dab, Dance.HitTheQuan, Dance.MillyRock,
            Dance.Woah, Dance.Bend, Dance.HandsUp, Dance.Shmoney, Dance.Griddy, Dance.HitDemFolks,
            Dance.Wobble, Dance.StankyLeg, Dance.RunningMan, Dance.Shimmy, Dance.Shoot,
            Dance.TootsieSlide, Dance.Renegade, Dance.Bop, Dance.Bop, Dance.FistPump,
        };

        private static readonly Dance[] FormalDances =
        {
            Dance.TwoStep, Dance.TwoStep, Dance.Clap, Dance.Clap, Dance.LeanWitIt, Dance.Dougie,
            Dance.NaeNae, Dance.MillyRock, Dance.HandsUp, Dance.Bop, Dance.Bop, Dance.Shimmy, Dance.Wobble,
        };

        // Slow songs: swaying, nodding, lights in the air
        private static readonly Dance[] SlowDances =
        {
            Dance.Wave, Dance.Wave, Dance.TwoStep, Dance.Bop, Dance.Clap, Dance.Wobble,
        };

        private const float SECTION_BEATS = 16f;
        private const float BLEND_BEATS = 2f;

        private static float Sin(float turns) => Mathf.Sin(turns * Mathf.PI * 2f);

        private const float ATTACK = 0.08f;

        // 1 right on the beat, falling away to 0 before the next, rising smoothly into it
        private static float Hit(float beat)
        {
            float x = Mathf.Repeat(beat + ATTACK, 1f);
            if (x < ATTACK) return Mathf.SmoothStep(0f, 1f, x / ATTACK);
            float fall = (1f - x) / (1f - ATTACK);
            return fall * fall;
        }

        // A jump: on the ground on the beat, highest halfway to the next
        private static float Air(float beat)
        {
            float x = Mathf.Repeat(beat, 1f);
            return 4f * x * (1f - x);
        }

        // A smooth 0 to 1 step, for snapping from one position to the next
        private static float Snap(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 2.5f));

        // Arm positions for the Renegade, one every half beat: left arm, left elbow, right arm, right elbow
        private static readonly float[,] RenegadeMoves =
        {
            { 20f, 110f, 20f, 110f }, { 95f, 10f, 30f, 60f }, { 30f, 60f, 95f, 10f }, { 150f, 100f, 20f, 40f },
            { 20f, 40f, 150f, 100f }, { 60f, 120f, 60f, 120f }, { 160f, 20f, 160f, 20f }, { 40f, 90f, 10f, 20f },
        };

        private static Pose DancePose(Dance dance, float t, float seed)
        {
            // Which way round this person does a one-sided move
            float side = seed < 0.5f ? 1f : -1f;
            var pose = new Pose();
            // Most people nod along a little whatever they are doing
            pose.Nod = 0.35f * Hit(t);

            switch (dance)
            {
                case Dance.TwoStep:
                    pose.Sway = 0.1f * Sin(t / 2f);
                    pose.Bounce = 0.02f * Hit(t);
                    pose.Lean = 3f * Sin(t / 2f);
                    pose.Twist = 0.25f * Sin(t / 2f);
                    pose.LeftArm = 18f; pose.LeftElbow = 70f + 14f * Sin(t / 2f);
                    pose.RightArm = 18f; pose.RightElbow = 70f - 14f * Sin(t / 2f);
                    break;

                case Dance.Clap:
                {
                    pose.Sway = 0.06f * Sin(t / 2f);
                    pose.Bounce = 0.012f * Hit(t);
                    // Hands meet in front on two and four
                    float clap = Mathf.Repeat(t, 2f) > 1f ? Hit(t) : 0f;
                    pose.LeftArm = 12f; pose.LeftElbow = 95f + 25f * clap;
                    pose.RightArm = 12f; pose.RightElbow = 95f + 25f * clap;
                    break;
                }

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
                        pose.Nod = 0.8f * dive;
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
                    pose.Twist = 0.5f * Sin(t);
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
                    pose.Twist = 0.3f * Sin(t / 2f);
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
                    pose.Twist = (right ? 1f : -1f) * 0.4f * punch;
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
                    pose.Nod = 0.9f * dab;
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
                    pose.Twist = -0.45f * Sin(t / 2f);
                    pose.LeftArm = 30f - 28f * Sin(t / 2f); pose.LeftElbow = 25f;
                    pose.RightArm = 30f + 28f * Sin(t / 2f); pose.RightElbow = 25f;
                    break;

                case Dance.MillyRock:
                    // Arms sweeping out to one side then the other, body going with them
                    pose.Sway = 0.08f * Sin(t / 2f);
                    pose.Lean = 7f * Sin(t / 2f);
                    pose.Twist = 0.5f * Sin(t / 2f);
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
                    pose.Nod = 0.6f * Hit(t / 2f);
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
                    pose.Phone = seed > 0.75f ? 1f : 0f;
                    break;

                case Dance.SwagSurf:
                    // Arms over each other's shoulders, the whole room rocking as one
                    pose.Sway = 0.22f * Sin(t / 4f);
                    pose.Lean = 11f * Sin(t / 4f);
                    pose.Bounce = 0.015f * Hit(t);
                    pose.LeftArm = 88f; pose.LeftElbow = 8f;
                    pose.RightArm = 88f; pose.RightElbow = 8f;
                    break;

                case Dance.Shmoney:
                {
                    // Arms swinging low from side to side, hands flicking on the half beats
                    float swing = Sin(t / 2f);
                    pose.Lean = 5f * swing;
                    pose.Sway = 0.05f * swing;
                    pose.Twist = 0.35f * swing;
                    pose.Bounce = 0.025f * Hit(t);
                    float flick = 30f * Hit(t * 2f);
                    pose.LeftArm = 22f + 30f * Mathf.Max(0f, -swing); pose.LeftElbow = 70f + flick;
                    pose.RightArm = 22f + 30f * Mathf.Max(0f, swing); pose.RightElbow = 70f + flick;
                    break;
                }

                case Dance.Griddy:
                {
                    // Quick heel taps (a bounce every half beat), arms pumping in turn, hands near the eyes now and then
                    pose.Bounce = 0.03f * Hit(t * 2f);
                    pose.Sway = 0.03f * Sin(t);
                    pose.Twist = 0.3f * Sin(t);
                    bool goggles = Mathf.Repeat(t, 8f) >= 6f;
                    if (goggles)
                    {
                        pose.LeftArm = 40f; pose.LeftElbow = 150f;
                        pose.RightArm = 40f; pose.RightElbow = 150f;
                        pose.Nod = 0.3f;
                    }
                    else
                    {
                        pose.LeftArm = 30f + 22f * Sin(t); pose.LeftElbow = 90f;
                        pose.RightArm = 30f - 22f * Sin(t); pose.RightElbow = 90f;
                    }
                    break;
                }

                case Dance.HitDemFolks:
                {
                    // Step out and throw one arm down and out to the side, the body dipping with it, then the other
                    bool right = Mathf.Repeat(t, 2f) < 1f == (side > 0f);
                    float way = right ? 1f : -1f;
                    float hit = Hit(t);
                    pose.Lean = way * 10f * hit;
                    pose.Sway = way * 0.06f;
                    pose.Bounce = -0.035f * hit;
                    pose.LeftArm = right ? 25f : 25f + 45f * hit; pose.LeftElbow = right ? 80f : 80f * (1f - hit);
                    pose.RightArm = right ? 25f + 45f * hit : 25f; pose.RightElbow = right ? 80f * (1f - hit) : 80f;
                    break;
                }

                case Dance.Wobble:
                    // Big slow hip rolls, body going all the way over to each side
                    pose.Sway = 0.13f * Sin(t / 2f);
                    pose.Lean = 9f * Sin(t / 2f + 0.15f);
                    pose.Twist = 0.3f * Sin(t / 2f + 0.25f);
                    pose.Bounce = -0.03f * Mathf.Abs(Sin(t / 2f));
                    pose.LeftArm = 35f; pose.LeftElbow = 85f + 10f * Sin(t / 2f);
                    pose.RightArm = 35f; pose.RightElbow = 85f - 10f * Sin(t / 2f);
                    break;

                case Dance.StankyLeg:
                {
                    // Down to one side, rolling the knee round: the body circles, one arm out for balance
                    float roll = Sin(t / 2f);
                    pose.Bounce = -0.05f + 0.015f * Mathf.Cos(t * Mathf.PI);
                    pose.Lean = side * (12f + 5f * roll);
                    pose.Sway = side * 0.06f;
                    pose.Twist = 0.5f * roll;
                    pose.LeftArm = side > 0f ? 70f + 10f * roll : 25f; pose.LeftElbow = side > 0f ? 25f : 70f;
                    pose.RightArm = side > 0f ? 25f : 70f + 10f * roll; pose.RightElbow = side > 0f ? 70f : 25f;
                    break;
                }

                case Dance.RunningMan:
                    // Running on the spot: a bounce every half beat, arms pumping hard in turn
                    pose.Bounce = 0.045f * Hit(t * 2f);
                    pose.Nod = 0.5f * Hit(t * 2f);
                    pose.Twist = 0.25f * Sin(t);
                    pose.LeftArm = 25f + 25f * Sin(t); pose.LeftElbow = 95f;
                    pose.RightArm = 25f - 25f * Sin(t); pose.RightElbow = 95f;
                    break;

                case Dance.Shimmy:
                    // Shoulders shaking fast, the body dipping slowly underneath
                    pose.Twist = 0.8f * Sin(t * 2f);
                    pose.Lean = 4f * Sin(t * 2f) + 4f * Sin(t / 4f);
                    pose.Bounce = -0.03f * (0.5f + 0.5f * Sin(t / 4f));
                    pose.LeftArm = 40f + 10f * Sin(t * 2f); pose.LeftElbow = 80f;
                    pose.RightArm = 40f - 10f * Sin(t * 2f); pose.RightElbow = 80f;
                    break;

                case Dance.Shoot:
                {
                    // A kick on each beat with the hips, one arm pointing up and out, the other hand behind the head
                    bool right = Mathf.Repeat(t, 8f) < 4f == (side > 0f);
                    float way = right ? 1f : -1f;
                    float kick = Hit(t);
                    pose.Bounce = 0.03f * kick;
                    pose.Lean = -way * 6f * kick;
                    pose.Sway = way * 0.04f * kick;
                    float point = 140f + 10f * kick, back = 150f;
                    pose.LeftArm = right ? back : point; pose.LeftElbow = right ? 130f : 10f;
                    pose.RightArm = right ? point : back; pose.RightElbow = right ? 10f : 130f;
                    break;
                }

                case Dance.TootsieSlide:
                {
                    // Hands at the chest, slide right, slide left, take it back, then hands up for a beat
                    float count = Mathf.Repeat(t, 4f);
                    float way = count < 1f ? 1f : count < 2f ? -1f : count < 3f ? -1f : 0f;
                    float slide = Snap(Mathf.Repeat(t, 1f));
                    pose.Sway = way * 0.09f * slide;
                    pose.Lean = way * 6f * slide;
                    pose.Bounce = 0.02f * Hit(t);
                    if (count >= 3f)
                    {
                        pose.LeftArm = 150f; pose.LeftElbow = 30f;
                        pose.RightArm = 150f; pose.RightElbow = 30f;
                    }
                    else
                    {
                        pose.LeftArm = 15f; pose.LeftElbow = 125f;
                        pose.RightArm = 15f; pose.RightElbow = 125f;
                    }
                    break;
                }

                case Dance.Renegade:
                {
                    // A quick run of arm shapes, a new one every half beat
                    int move = (int) Mathf.Repeat(Mathf.Floor(t * 2f), 8f);
                    pose.LeftArm = RenegadeMoves[move, 0]; pose.LeftElbow = RenegadeMoves[move, 1];
                    pose.RightArm = RenegadeMoves[move, 2]; pose.RightElbow = RenegadeMoves[move, 3];
                    pose.Bounce = 0.02f * Hit(t * 2f);
                    pose.Lean = 5f * Sin(t / 2f);
                    pose.Twist = 0.3f * Sin(t);
                    pose.Head = 6f * Sin(t / 2f);
                    break;
                }

                case Dance.Bop:
                    // Nodding hard on the beat, a little bounce, hands loose; a fist up on the one
                    pose.Nod = Hit(t);
                    pose.Bounce = 0.015f * Hit(t);
                    pose.Sway = 0.03f * Sin(t / 4f);
                    pose.LeftArm = 15f; pose.LeftElbow = 40f;
                    pose.RightArm = 15f + (side > 0f ? 120f * Hit(t / 4f) : 0f);
                    pose.RightElbow = 40f + (side > 0f ? 30f * Hit(t / 4f) : 0f);
                    break;

                case Dance.FistPump:
                {
                    // Fist punched up on every beat, a little hop with it
                    float pump = Hit(t);
                    pose.Bounce = 0.04f * pump;
                    pose.Nod = 0.6f * pump;
                    pose.Lean = side * 3f * pump;
                    pose.LeftArm = side > 0f ? 20f : 140f + 25f * pump; pose.LeftElbow = side > 0f ? 50f : 70f * (1f - pump);
                    pose.RightArm = side > 0f ? 140f + 25f * pump : 20f; pose.RightElbow = side > 0f ? 70f * (1f - pump) : 50f;
                    break;
                }

                case Dance.Jump:
                    // Everyone jumping on the beat, hands up
                    pose.Bounce = 0.1f * Air(t);
                    pose.Nod = 0.5f * Hit(t);
                    pose.LeftArm = 150f + 15f * Air(t); pose.LeftElbow = 35f - 20f * Air(t);
                    pose.RightArm = 150f + 15f * Air(t); pose.RightElbow = 35f - 20f * Air(t);
                    break;

                case Dance.Wave:
                    // A slow song: swaying with one arm up, a phone held up as a light
                    pose.Sway = 0.08f * Sin(t / 4f + seed);
                    pose.Lean = 7f * Sin(t / 4f + seed);
                    pose.Head = -3f * Sin(t / 4f + seed);
                    pose.Nod = 0.2f * Hit(t);
                    pose.LeftArm = side > 0f ? 15f : 160f + 12f * Sin(t / 4f + seed); pose.LeftElbow = side > 0f ? 40f : 10f;
                    pose.RightArm = side > 0f ? 160f + 12f * Sin(t / 4f + seed) : 15f; pose.RightElbow = side > 0f ? 10f : 40f;
                    pose.Phone = 1f;
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

        // The dance a person should be doing at this beat. Neighbors share a dance; each group changes
        // on its own bar, so the room doesn't all switch at once. Now and then everyone does the same.
        private Dance DanceFor(Person person, float beat)
        {
            int section = Mathf.FloorToInt(beat / SECTION_BEATS);
            float together = Hash(-1, section);
            if (_slow)
            {
                if (together < 0.3f) return Dance.Wave;
                return SlowDances[(int) (Hash(person.Group, Mathf.FloorToInt((beat + person.GroupOffset) / SECTION_BEATS)) * SlowDances.Length) % SlowDances.Length];
            }

            if (together < 0.10f) return Dance.SwagSurf;
            if (together < 0.17f) return Dance.HandsUp;
            if (together < 0.23f && !_formal) return Dance.Jump;

            var dances = _formal ? FormalDances : StreetDances;
            int own = Mathf.FloorToInt((beat + person.GroupOffset) / SECTION_BEATS);
            return dances[(int) (Hash(person.Group, own) * dances.Length) % dances.Length];
        }

        // Which way a bent elbow takes the forearm. With the arm down by the side the hand comes in
        // across the front of the body (and is hidden by it, seen from behind); with the arm raised
        // the hand comes in over the head. In between it straightens out.
        private static float ElbowTurn(float arm, float elbow)
        {
            float raised = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((arm - 50f) / 50f));
            return elbow * Mathf.Lerp(-1f, 1f, raised);
        }

        // The song's speed, from how fast the beat count goes up, for telling slow songs apart
        private float _lastBeat = float.NaN;
        private float _beatsPerSecond = 2f;
        private bool _slow;
        private int _slowSection = int.MinValue;

        private void FollowTempo(float beat, float dt)
        {
            if (!float.IsNaN(_lastBeat) && dt > 0f)
            {
                float step = beat - _lastBeat;
                if (step >= 0f && step < 1f)
                {
                    float speed = step / dt;
                    _beatsPerSecond = Mathf.Lerp(_beatsPerSecond, speed, 1f - Mathf.Exp(-dt / 3f));
                }
            }
            _lastBeat = beat;

            // Only decided at the start of a section, so the crowd doesn't flip back and forth
            int section = Mathf.FloorToInt(beat / SECTION_BEATS);
            if (section != _slowSection)
            {
                _slowSection = section;
                _slow = _beatsPerSecond * 60f < 82f;
            }
        }

        /// <summary>Moves everyone. <paramref name="beat"/> counts beats of the song, with fractions.</summary>
        public void Animate(float beat)
        {
            if (_area == null) return;

            float screenHeight = _area.rect.height;
            if (screenHeight <= 0f) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            FollowTempo(beat, dt);
            float seconds = Time.unscaledTime;

            foreach (var person in _people)
            {
                if (person.Root == null) continue;

                // People pick up a new dance a moment apart, and blend into it over two beats
                var dance = DanceFor(person, beat - person.Seed * 0.75f);
                if (dance != person.Dance)
                {
                    person.PreviousDance = person.Dance;
                    person.Dance = dance;
                    person.SwitchBeat = beat;
                }
                if (beat < person.SwitchBeat) person.SwitchBeat = beat - BLEND_BEATS;

                // Everyone is in step for the group dances; otherwise people are a touch loose
                bool inStep = dance == Dance.SwagSurf || dance == Dance.Jump;
                float loose = inStep ? 0f : (person.Seed - 0.5f) * 0.12f;
                var target = DancePose(dance, beat + loose, person.Seed);
                float blend = (beat - person.SwitchBeat) / BLEND_BEATS;
                if (blend < 1f)
                {
                    var previous = DancePose(person.PreviousDance, beat + loose, person.Seed);
                    target = Pose.Lerp(previous, target, Mathf.SmoothStep(0f, 1f, blend));
                }

                // Breathing and shifting weight, so nobody is ever perfectly still
                target.Bounce += 0.004f * Mathf.Sin((seconds * 0.27f + person.Seed) * Mathf.PI * 2f);
                target.Lean += 0.8f * Mathf.Sin((seconds * 0.11f + person.Seed * 3.1f) * Mathf.PI * 2f);

                float energy = inStep ? 1f : person.Energy;
                target.Lean *= energy;
                target.Sway *= energy;
                target.Bounce *= energy;
                target.Twist *= energy;

                // The head swings the other way a little when the body leans quickly
                target.Head -= person.Velocity[0] * 0.035f;

                Follow(person, target, dt);
                var pose = person.Current;

                float bodyHeight = person.Root.rect.height;
                float bodyWidth = person.Root.rect.width;

                person.Root.anchoredPosition = new Vector2(pose.Sway * bodyWidth, pose.Bounce * bodyHeight);
                person.Root.localRotation = Quaternion.Euler(0f, 0f, -pose.Lean);
                // Turning narrows the shoulders; dropping down squashes the body a touch
                float twist = Mathf.Clamp(Mathf.Abs(pose.Twist), 0f, 1f);
                float squash = 1f + Mathf.Clamp(pose.Bounce, -0.1f, 0.1f) * 0.6f;
                person.Root.localScale = new Vector3(1f - 0.13f * twist, squash, 1f);

                float nod = Mathf.Clamp01(pose.Nod);
                person.Head.localRotation = Quaternion.Euler(0f, 0f, -pose.Head);
                person.Head.localScale = new Vector3(1f, 1f - 0.07f * nod, 1f);
                person.Head.anchoredPosition = person.HeadHome + new Vector2(0f, -0.025f * nod * bodyHeight);

                // The left arm swings out to the left (clockwise from hanging), the right arm to the right
                person.LeftArm.localRotation = Quaternion.Euler(0f, 0f, -pose.LeftArm);
                person.LeftForearm.localRotation = Quaternion.Euler(0f, 0f, -ElbowTurn(pose.LeftArm, pose.LeftElbow));
                person.RightArm.localRotation = Quaternion.Euler(0f, 0f, pose.RightArm);
                person.RightForearm.localRotation = Quaternion.Euler(0f, 0f, ElbowTurn(pose.RightArm, pose.RightElbow));

                if (person.Phone != null)
                {
                    // The phone goes in whichever hand is up
                    float light = Mathf.Clamp01(pose.Phone);
                    bool left = pose.LeftArm > pose.RightArm;
                    SetPhone(person.Phone, left ? 0f : light, seconds, person.Seed);
                    SetPhone(person.LeftPhone, left ? light : 0f, seconds, person.Seed);
                }
            }
        }

        private static void SetPhone(PhoneLight phone, float amount, float seconds, float seed)
        {
            if (phone == null) return;
            bool on = amount > 0.02f;
            if (phone.Screen.enabled != on)
            {
                phone.Screen.enabled = on;
                phone.Glow.enabled = on;
            }
            if (!on) return;

            float flicker = 0.85f + 0.15f * Mathf.Sin((seconds * 1.7f + seed * 5f) * Mathf.PI * 2f);
            phone.Screen.color = Alpha(phone.ScreenColor, amount);
            phone.Glow.color = Alpha(phone.GlowColor, phone.GlowColor.a * amount * flicker);
        }

        // Each part of the pose is a spring pulled toward where the dance wants it
        private static void Follow(Person person, in Pose target, float dt)
        {
            if (!person.Started || dt <= 0f)
            {
                person.Current = target;
                person.Started = true;
                return;
            }

            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f));
            float h = dt / steps;
            var current = person.Current;
            for (int i = 0; i < Pose.COUNT; i++)
            {
                float w = Frequency[i] * Mathf.PI * 2f;
                float zeta = Damping[i];
                float x = current[i];
                float v = person.Velocity[i];
                float goal = target[i];
                for (int s = 0; s < steps; s++)
                {
                    float a = w * w * (goal - x) - 2f * zeta * w * v;
                    v += a * h;
                    x += v * h;
                }
                current[i] = x;
                person.Velocity[i] = v;
            }
            person.Current = current;
        }
    }
}
