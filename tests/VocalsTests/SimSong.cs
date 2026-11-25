// A made-up song for the tests: phrases of notes with the lengths, gaps and melodic movement
// of real vocal charts, built into the same objects the game's chart loader produces.
using System;
using System.Collections.Generic;
using YARG.Core;
using YARG.Core.Chart;

namespace VocalsTests
{
    public sealed class SimNote
    {
        public double Start;
        public double Length;
        public float Pitch;       // MIDI note; below 0 for a spoken note
        public double End => Start + Length;
        public bool Spoken => Pitch < 0f;

        // A slide: after the note the voice glides to another pitch and holds it
        public float? SlideTo;
        public double SlideGap;
        public double SlideLength;

        /// <summary>The end of the note including its slide.</summary>
        public double TotalEnd => SlideTo.HasValue ? End + SlideGap + SlideLength : End;

        /// <summary>The pitch the chart wants at a moment during the note (and its slide).</summary>
        public float PitchAt(double time)
        {
            if (!SlideTo.HasValue || time < End)
            {
                return Pitch;
            }

            double slideStart = End + SlideGap;
            if (time >= slideStart)
            {
                return SlideTo.Value;
            }

            return (float) (Pitch + (SlideTo.Value - Pitch) * (time - End) / SlideGap);
        }
    }

    public sealed class SimPhrase
    {
        public double Start;
        public double End;
        public readonly List<SimNote> Notes = new List<SimNote>();
    }

    public enum SongStyle
    {
        Pop,     // mixed short and medium notes
        Fast,    // mostly short notes
        Ballad,  // long held notes
        Rap,     // spoken notes only
    }

    public sealed class SimSong
    {
        public const uint RESOLUTION = 480;
        public const double BPM = 120.0;

        public readonly List<SimPhrase> Phrases = new List<SimPhrase>();
        public double Length;
        public SongStyle Style;

        private static readonly int[] MajorScale = { 0, 2, 4, 5, 7, 9, 11 };

        public static SimSong Create(SongStyle style, int phraseCount, int seed)
        {
            var random = new Random(seed);
            var song = new SimSong { Style = style };

            double time = 2.0;
            int degree = 7 + random.Next(5); // position on the scale; 7 = the key note, an octave up
            int key = 48 + random.Next(7);   // C3..F#3 as the low key note

            for (int p = 0; p < phraseCount; p++)
            {
                var phrase = new SimPhrase();
                int count = style switch
                {
                    SongStyle.Fast => 8 + random.Next(7),
                    SongStyle.Ballad => 3 + random.Next(4),
                    SongStyle.Rap => 8 + random.Next(8),
                    _ => 5 + random.Next(5),
                };

                phrase.Start = time - 0.10;
                for (int n = 0; n < count; n++)
                {
                    bool last = n == count - 1;
                    double length = style switch
                    {
                        SongStyle.Fast => Pick(random, 0.10, 0.125, 0.125, 0.125, 0.15, 0.1875, 0.25),
                        SongStyle.Ballad => Pick(random, 0.4, 0.5, 0.6, 0.75, 1.0, 1.25, 1.6),
                        SongStyle.Rap => Pick(random, 0.11, 0.125, 0.15, 0.1875, 0.25),
                        _ => Pick(random, 0.125, 0.1875, 0.25, 0.25, 0.25, 0.375, 0.5, 0.75),
                    };
                    if (last && style != SongStyle.Rap)
                    {
                        length = Math.Max(length, Pick(random, 0.4, 0.5, 0.75, 1.0));
                    }

                    // Mostly steps, sometimes a leap; stay within about an octave and a half
                    int step = Pick(random, -2, -1, -1, 0, 0, 1, 1, 2);
                    if (random.NextDouble() < 0.12)
                    {
                        step = Pick(random, -4, -3, 3, 4, 5);
                    }
                    degree = Math.Max(3, Math.Min(15, degree + step));
                    float pitch = key + 12 * (degree / 7) + MajorScale[degree % 7];

                    phrase.Notes.Add(new SimNote
                    {
                        Start = time,
                        Length = length,
                        Pitch = style == SongStyle.Rap ? -1f : pitch,
                    });

                    double gap = Pick(random, 0.0, 0.0, 0.0, 0.0, 0.03, 0.06, 0.06, 0.125);
                    time += length + gap;
                }

                phrase.End = phrase.Notes[phrase.Notes.Count - 1].End + 0.12;
                song.Phrases.Add(phrase);

                // Room to breathe before the next phrase
                time = phrase.End + 0.2 + Pick(random, 0.5, 0.75, 1.0, 1.5, 2.0);
            }

            song.Length = time + 1.0;
            return song;
        }

        private static T Pick<T>(Random random, params T[] options) => options[random.Next(options.Length)];

        public static uint Tick(double time) => (uint) Math.Round(time * BPM / 60.0 * RESOLUTION);

        public SyncTrack BuildSyncTrack()
        {
            return new SyncTrack(RESOLUTION,
                new List<TempoChange> { new TempoChange(BPM, 0, 0) },
                new List<TimeSignatureChange> { new TimeSignatureChange(4, 4, 0, 0, 0, 0, 0, 0) },
                new List<Beatline>());
        }

        /// <summary>A fresh chart (the engine marks notes as hit/missed, so one per run).</summary>
        public InstrumentDifficulty<VocalNote> BuildChart()
        {
            var phrases = new List<VocalNote>();
            foreach (var phrase in Phrases)
            {
                uint start = Tick(phrase.Start);
                uint end = Tick(phrase.End);
                var parent = new VocalNote(NoteFlags.None, false, phrase.Start, phrase.End - phrase.Start, start, end - start);
                foreach (var note in phrase.Notes)
                {
                    uint noteStart = Tick(note.Start);
                    uint noteEnd = Tick(note.End);
                    var child = new VocalNote(note.Pitch, 0, VocalNoteType.Lyric,
                        note.Start, note.Length, noteStart, noteEnd - noteStart);
                    if (note.SlideTo.HasValue)
                    {
                        // The way the game's chart loader builds a slide: a note inside the note
                        double slideTime = note.End + note.SlideGap;
                        uint slideStart = Tick(slideTime);
                        uint slideEnd = Tick(slideTime + note.SlideLength);
                        child.AddChildNote(new VocalNote(note.SlideTo.Value, 0, VocalNoteType.Lyric,
                            slideTime, note.SlideLength, slideStart, slideEnd - slideStart));
                    }
                    parent.AddChildNote(child);
                }
                phrases.Add(parent);
            }

            return new InstrumentDifficulty<VocalNote>(Instrument.Vocals, Difficulty.Expert,
                phrases, new List<Phrase>(), new List<TextEvent>());
        }
    }
}
