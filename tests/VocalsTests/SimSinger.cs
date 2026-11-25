// A made-up singer for the tests. Give it a song and how good it is (how far off pitch, how
// late, how often it gets a note wrong or leaves one out) and it "sings" the song as audio,
// with the consonants, scoops and vibrato a real voice has.
using System;
using System.Collections.Generic;

namespace VocalsTests
{
    public sealed class SingerSkill
    {
        public string Name;

        /// <summary>Whole-song transposition in semitones (e.g. -12 = an octave down).</summary>
        public double Transpose;

        /// <summary>Constant sharp (+) or flat (-) tendency, in cents.</summary>
        public double BiasCents;

        /// <summary>How far each note lands from its target (standard deviation, cents).</summary>
        public double NoteErrorCents;

        /// <summary>How far below the note the voice starts, and how long it takes to arrive.</summary>
        public double ScoopCents;
        public double ScoopSeconds;

        /// <summary>Average lateness of each note and its spread (seconds).</summary>
        public double LateSeconds;
        public double TimingSpreadSeconds;

        /// <summary>Chance of singing a neighbouring wrong note, and of leaving a note out.</summary>
        public double WrongNoteChance;
        public double SkipNoteChance;

        /// <summary>Vibrato depth on long notes (cents).</summary>
        public double VibratoCents;

        /// <summary>Chance that a note starts with an unvoiced consonant, and its longest length.</summary>
        public double ConsonantChance = 0.6;
        public double ConsonantSeconds = 0.07;

        /// <summary>How much of the consonant is placed before the beat (1 = all of it, as trained singers do).</summary>
        public double ConsonantLead = 0.5;

        /// <summary>Stop singing each phrase after this share of it (1 = sing all of it).</summary>
        public double PhraseShare = 1.0;

        /// <summary>Leave out the highest note of every phrase.</summary>
        public bool SkipHighest;

        /// <summary>Hold one pitch (this MIDI note) through every phrase instead of the melody.</summary>
        public double MonotonePitch = double.NaN;

        /// <summary>Make noise (breath, "shhh") through every phrase instead of singing.</summary>
        public bool NoiseOnly;

        /// <summary>Loudness of the voice at the mic (peak, 0 to 1).</summary>
        public double Level = 0.25;

        /// <summary>Steady noise in the room (RMS, 0 to 1): a fan, a crowd, a TV.</summary>
        public double RoomNoise = 0.0005;

        public SingerSkill Clone() => (SingerSkill) MemberwiseClone();

        // The singers the tests use. "Star" is a trained singer; "Good" is a capable amateur;
        // "Karaoke" is an average person who knows the song; "Rough" is having a go.
        public static SingerSkill Star => new SingerSkill
        {
            Name = "star", NoteErrorCents = 8, ScoopCents = 40, ScoopSeconds = 0.04,
            LateSeconds = 0.0, TimingSpreadSeconds = 0.015, VibratoCents = 35, ConsonantLead = 0.9,
        };

        public static SingerSkill Good => new SingerSkill
        {
            Name = "good", BiasCents = 8, NoteErrorCents = 22, ScoopCents = 90, ScoopSeconds = 0.07,
            LateSeconds = 0.01, TimingSpreadSeconds = 0.03, WrongNoteChance = 0.02, VibratoCents = 45,
            ConsonantLead = 0.6,
        };

        public static SingerSkill Karaoke => new SingerSkill
        {
            Name = "karaoke", BiasCents = -18, NoteErrorCents = 42, ScoopCents = 140, ScoopSeconds = 0.11,
            LateSeconds = 0.03, TimingSpreadSeconds = 0.055, WrongNoteChance = 0.07, SkipNoteChance = 0.04,
            VibratoCents = 30, ConsonantLead = 0.3,
        };

        public static SingerSkill Rough => new SingerSkill
        {
            Name = "rough", BiasCents = 30, NoteErrorCents = 80, ScoopCents = 200, ScoopSeconds = 0.15,
            LateSeconds = 0.06, TimingSpreadSeconds = 0.10, WrongNoteChance = 0.20, SkipNoteChance = 0.12,
            VibratoCents = 20, ConsonantLead = 0.2,
        };
    }

    public static class SimSinger
    {
        public const int RATE = 48000;

        /// <summary>Renders the singer's performance of the song (mono, 48 kHz).</summary>
        public static float[] Sing(SimSong song, SingerSkill skill, int seed)
        {
            var random = new Random(seed);
            var voice = new SyntheticVoice(RATE, seed * 31 + 7);
            var audio = new float[(int) (song.Length * RATE)];

            foreach (var phrase in song.Phrases)
            {
                double phraseStop = phrase.Start + (phrase.End - phrase.Start) * skill.PhraseShare;

                if (skill.NoiseOnly)
                {
                    AddNoise(audio, voice, phrase.Notes[0].Start, phrase.Notes[phrase.Notes.Count - 1].End, skill.Level * 0.5, true);
                    continue;
                }

                if (!double.IsNaN(skill.MonotonePitch))
                {
                    AddTone(audio, voice, phrase.Notes[0].Start, phrase.Notes[phrase.Notes.Count - 1].End,
                        skill.MonotonePitch, skill.MonotonePitch, 0, 0, 0, skill.Level);
                    continue;
                }

                float highest = float.MinValue;
                foreach (var note in phrase.Notes)
                {
                    highest = Math.Max(highest, note.Pitch);
                }

                double previousEnd = double.NegativeInfinity;
                double previousPitch = double.NaN;
                bool skippedHighest = false;

                for (int n = 0; n < phrase.Notes.Count; n++)
                {
                    var note = phrase.Notes[n];
                    voice.SetVowel(random.Next(SyntheticVoice.Vowels.Length));

                    if (note.Start >= phraseStop)
                    {
                        break;
                    }

                    if (random.NextDouble() < skill.SkipNoteChance)
                    {
                        previousPitch = double.NaN;
                        continue;
                    }

                    if (skill.SkipHighest && !skippedHighest && note.Pitch == highest && phrase.Notes.Count > 1)
                    {
                        skippedHighest = true;
                        previousPitch = double.NaN;
                        continue;
                    }

                    // When this note is sung
                    double shift = skill.LateSeconds + Gaussian(random) * skill.TimingSpreadSeconds;
                    double start = Math.Max(note.Start + shift, previousEnd);
                    double end = Math.Min(note.End + shift * 0.5, phraseStop + 0.05);
                    if (n + 1 < phrase.Notes.Count)
                    {
                        // Never run over the next note's (unshifted) start by more than a little
                        end = Math.Min(end, phrase.Notes[n + 1].Start + 0.04);
                    }
                    if (end - start < 0.04)
                    {
                        previousPitch = double.NaN;
                        continue;
                    }

                    // The consonant, partly before the beat
                    double consonant = 0;
                    int consonantKind = 0; // 0 none (or a voiced one), 1 hiss, 2 stop (a short silence)
                    if (random.NextDouble() < skill.ConsonantChance)
                    {
                        consonant = (0.3 + 0.7 * random.NextDouble()) * skill.ConsonantSeconds;
                        consonantKind = random.NextDouble() < 0.55 ? 1 : 2;
                    }

                    double vowelStart = start + consonant * (1.0 - skill.ConsonantLead);
                    double consonantStart = vowelStart - consonant;
                    if (consonantStart < previousEnd)
                    {
                        // The consonant eats the end of the previous note
                        consonantStart = Math.Max(consonantStart, previousEnd - consonant);
                        MuteRange(audio, consonantStart, previousEnd);
                    }

                    if (consonantKind == 1)
                    {
                        AddNoise(audio, voice, consonantStart, vowelStart, skill.Level * 0.35, true);
                    }

                    if (end - vowelStart < 0.03)
                    {
                        previousPitch = double.NaN;
                        previousEnd = end;
                        continue;
                    }

                    if (note.Spoken)
                    {
                        // Speech: a falling tone around a comfortable pitch
                        double spoken = 50 + random.NextDouble() * 6;
                        AddTone(audio, voice, vowelStart, end, spoken + 1.5, spoken - 1.5, 0, 0, 0, skill.Level);
                    }
                    else
                    {
                        double target = note.Pitch + skill.Transpose +
                            (skill.BiasCents + Gaussian(random) * skill.NoteErrorCents) / 100.0;
                        if (random.NextDouble() < skill.WrongNoteChance)
                        {
                            target += random.NextDouble() < 0.5 ? -1 - random.Next(2) : 1 + random.Next(2);
                        }

                        // Glide in from the previous note if the two are joined, else scoop up
                        bool joined = !double.IsNaN(previousPitch) && vowelStart - previousEnd < 0.03 && consonantKind == 0;
                        double from = joined ? previousPitch : target - skill.ScoopCents / 100.0;
                        double glide = joined ? Math.Min(0.07, skill.ScoopSeconds + 0.02) : skill.ScoopSeconds;
                        double vibrato = end - vowelStart > 0.5 ? skill.VibratoCents : 0;

                        AddTone(audio, voice, vowelStart, end, from, target, glide, vibrato, 0.25, skill.Level);
                        previousPitch = target;
                    }

                    previousEnd = end;
                }
            }

            // The room: noise everywhere (a mix of hiss and rumble)
            var room = new Random(seed + 1);
            double low = 0;
            double scale = skill.RoomNoise * Math.Sqrt(12.0);
            for (int i = 0; i < audio.Length; i++)
            {
                double white = room.NextDouble() - 0.5;
                low += (white - low) * 0.05;
                audio[i] += (float) ((white * 0.6 + low * 3.0) * scale);
            }

            return audio;
        }

        /// <summary>Adds a voiced stretch whose pitch goes from one note to another and then holds.</summary>
        private static void AddTone(float[] audio, SyntheticVoice voice, double start, double end,
            double fromMidi, double toMidi, double glideSeconds, double vibratoCents, double vibratoDelay, double level)
        {
            int first = Math.Max(0, (int) (start * RATE));
            int last = Math.Min(audio.Length, (int) (end * RATE));
            for (int i = first; i < last; i++)
            {
                double t = (double) (i - first) / RATE;
                double remaining = (double) (last - i) / RATE;

                double midi = toMidi;
                if (glideSeconds > 0)
                {
                    midi = toMidi + (fromMidi - toMidi) * Math.Exp(-t / (glideSeconds / 3.0));
                }
                else if (fromMidi != toMidi)
                {
                    midi = fromMidi + (toMidi - fromMidi) * t / Math.Max(1e-6, end - start);
                }

                if (vibratoCents > 0 && t > vibratoDelay)
                {
                    double depth = Math.Min(1.0, (t - vibratoDelay) / 0.2) * vibratoCents / 100.0;
                    midi += depth * Math.Sin(2 * Math.PI * 5.5 * (t - vibratoDelay));
                }

                double envelope = Math.Min(1.0, t / 0.015) * Math.Min(1.0, remaining / 0.03);
                double hz = 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);
                audio[i] += voice.Voiced(hz, level * envelope);
            }
        }

        private static void AddNoise(float[] audio, SyntheticVoice voice, double start, double end, double level, bool hiss)
        {
            int first = Math.Max(0, (int) (start * RATE));
            int last = Math.Min(audio.Length, (int) (end * RATE));
            for (int i = first; i < last; i++)
            {
                double t = (double) (i - first) / RATE;
                double remaining = (double) (last - i) / RATE;
                double envelope = Math.Min(1.0, t / 0.005) * Math.Min(1.0, remaining / 0.005);
                audio[i] += voice.Noise(level * envelope, hiss);
            }
        }

        private static void MuteRange(float[] audio, double start, double end)
        {
            int first = Math.Max(0, (int) (start * RATE));
            int last = Math.Min(audio.Length, (int) (end * RATE));
            for (int i = first; i < last; i++)
            {
                audio[i] = 0f;
            }
        }

        private static double Gaussian(Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
