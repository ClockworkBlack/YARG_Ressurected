// What was chosen on the Song Setup screen, for the game to pick up when the song starts.

using YARG.Core.Song;

namespace YARG.Menu.SongSetup
{
    public static class SongSetupState
    {
        /// <summary>The song the key change and the section below belong to.</summary>
        public static SongEntry Song { get; private set; }

        /// <summary>Key change in semitones (0 = as recorded).</summary>
        public static float KeyShift { get; private set; }

        public static bool   HasSpan   { get; private set; }
        public static double SpanStart { get; private set; }
        public static double SpanEnd   { get; private set; }

        /// <summary>Freestyle guitar. Stays as set until the game is closed.</summary>
        public static bool Freestyle;

        /// <summary>Starts (or continues) setting up this song. A different song starts fresh.</summary>
        public static void Use(SongEntry song)
        {
            if (!ReferenceEquals(Song, song))
            {
                Song = song;
                KeyShift = 0f;
                HasSpan = false;
                SpanStart = 0;
                SpanEnd = 0;
            }
        }

        public static void SetKeyShift(float semitones)
        {
            KeyShift = semitones < -12f ? -12f : semitones > 12f ? 12f : semitones;
        }

        public static void SetSpan(double start, double end)
        {
            HasSpan = end - start >= 1.0;
            SpanStart = start;
            SpanEnd = end;
        }

        public static void ClearSpan()
        {
            HasSpan = false;
        }

        public static float KeyShiftFor(SongEntry song)
        {
            return song != null && ReferenceEquals(song, Song) ? KeyShift : 0f;
        }

        public static bool TryGetSpan(SongEntry song, out double start, out double end)
        {
            start = SpanStart;
            end = SpanEnd;
            return song != null && ReferenceEquals(song, Song) && HasSpan;
        }
    }
}
