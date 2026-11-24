// Guitar voice, part 1: listen to the song and decide which pitch each chart note should sound.
//
// Plain .NET only (no Unity, no BASS) so the automated tests can run it.
//
// How it works:
//   1. The song (mono, 11025 Hz) is cut into overlapping frames and each frame's spectrum is
//      reduced to "how strong is each note name" twice: once for the guitar/melody register
//      and once, with four times finer pitch resolution, for the bass register.
//   2. The recording's tuning (how far it sits from A = 440 Hz) and its scale are estimated,
//      so the generated guitar is in tune and in key with the song.
//   3. Every chart note gets a pitch: the note name the song is sounding at that moment, in an
//      octave chosen so the melody moves the way the frets move (higher fret, higher pitch;
//      same fret, same pitch).

using System;

namespace YARG.Audio.GuitarVoice
{
    /// <summary>One chart note (or chord) the guitar voice should play.</summary>
    public struct VoiceNote
    {
        /// <summary>Start, in seconds of song audio.</summary>
        public double Time;

        /// <summary>Sustain length in seconds (0 for a plain note).</summary>
        public double Length;

        /// <summary>Lowest fret of the note: 0 = open, 1 = green ... 5 = orange.</summary>
        public int LowFret;

        /// <summary>How many frets are held (1 = single note, 2+ = chord).</summary>
        public int FretCount;

        /// <summary>Hammer-on / pull-off / tap: a softer attack than a picked note.</summary>
        public bool Soft;

        /// <summary>MIDI note number chosen for this note (filled by <see cref="GuitarVoicePitcher"/>).</summary>
        public int Pitch;
    }

    /// <summary>What was learned about a song's harmony.</summary>
    public sealed class SongHarmony
    {
        public const int SAMPLE_RATE = 11025;

        private const int NFFT = 2048;
        private const int HOP = 512;
        private const int LOW_NFFT = 1024;
        private const int LOW_HOP = HOP / 4;
        private const int P_LO = 24;
        private const int P_HI = 108;
        private const int SEMIS = P_HI - P_LO + 2;

        // Harmonics 1..5 of a note land this many semitones above it.
        private static readonly int[]   HarmOffset = { 0, 12, 19, 24, 28 };
        private static readonly float[] HarmWeight = { 1.0f, 0.7f, 0.49f, 0.34f, 0.24f };
        private static readonly int[]   MajorScale = { 0, 2, 4, 5, 7, 9, 11 };

        /// <summary>How far the recording is from A = 440 Hz, in cents (-50..50).</summary>
        public float Cents { get; private set; }

        /// <summary>Root (0 = C) of the major scale whose seven notes fit the song best.</summary>
        public int Key { get; private set; }

        /// <summary>For each of the 12 note names: is it in the song's scale?</summary>
        public bool[] InScale { get; private set; } = new bool[12];

        /// <summary>Loudness (RMS) of the analysed audio.</summary>
        public float Rms { get; private set; }

        /// <summary>Length of the analysed audio in seconds.</summary>
        public double Seconds { get; private set; }

        public int FrameCount { get; private set; }

        private float[] _mid = Array.Empty<float>(); // [frame * 12 + noteName], guitar / melody register
        private float[] _low = Array.Empty<float>(); // [frame * 12 + noteName], bass register

        public static SongHarmony Analyze(float[] mono)
        {
            var h = new SongHarmony();
            int n = mono.Length;
            h.Seconds = n / (double) SAMPLE_RATE;

            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += (double) mono[i] * mono[i];
            }
            h.Rms = n > 0 ? (float) Math.Sqrt(sum / n) : 0f;

            float binHz = SAMPLE_RATE / (float) NFFT;
            var fft = new Fft(NFFT);
            int frames = n >= NFFT ? 1 + (n - NFFT) / HOP : 1;
            var mag = new float[NFFT / 2 + 1];
            var peakPos = new float[NFFT / 2];
            var peakMag = new float[NFFT / 2];

            // Tuning, from about 400 frames spread over the song
            {
                double sx = 0, sy = 0;
                int lo = (int) (150 / binHz), hi = (int) (2000 / binHz);
                int step = Math.Max(1, frames / 400);
                for (int f = 0; f < frames; f += step)
                {
                    fft.Magnitudes(mono, f * HOP, mag);
                    int count = FindPeaks(mag, lo, hi, 0.1f, peakPos, peakMag);
                    for (int i = 0; i < count; i++)
                    {
                        double midi = 69 + 12 * Math.Log(peakPos[i] * binHz / 440.0, 2);
                        double ang = 2 * Math.PI * (midi - Math.Round(midi));
                        sx += peakMag[i] * Math.Cos(ang);
                        sy += peakMag[i] * Math.Sin(ang);
                    }
                }
                h.Cents = sx == 0 && sy == 0 ? 0f : (float) (Math.Atan2(sy, sx) / (2 * Math.PI) * 100.0);
            }

            double a4 = 440.0 * Math.Pow(2, h.Cents / 1200.0);
            var semis = new float[SEMIS];

            // Guitar / melody register
            var mid = new float[frames * 12];
            for (int f = 0; f < frames; f++)
            {
                fft.Magnitudes(mono, f * HOP, mag);
                SemitoneSpectrum(mag, binHz, a4, 100f, semis, peakPos, peakMag);
                Chroma(semis, 45, 84, mid, f * 12);
            }

            // Bass register: a quarter of the sample rate gives four times finer pitch resolution
            // (an 808 or a bass guitar has little but its lowest tone), at the same frame times.
            var lowSignal = Decimate4(mono);
            float lowBinHz = SAMPLE_RATE / 4f / LOW_NFFT;
            var lowFft = new Fft(LOW_NFFT);
            int lowFrames = lowSignal.Length >= LOW_NFFT ? 1 + (lowSignal.Length - LOW_NFFT) / LOW_HOP : 1;
            var lowMag = new float[LOW_NFFT / 2 + 1];
            var low = new float[lowFrames * 12];
            for (int f = 0; f < lowFrames; f++)
            {
                lowFft.Magnitudes(lowSignal, f * LOW_HOP, lowMag);
                SemitoneSpectrum(lowMag, lowBinHz, a4, 30f, semis, peakPos, peakMag);
                Chroma(semis, 28, 55, low, f * 12);
            }

            h.FrameCount = Math.Min(frames, lowFrames);
            h._mid = mid;
            h._low = low;

            // Scale: the seven-note major scale (any of 12 roots) holding the most energy.
            // A minor key is the same seven notes as its relative major.
            var total = new double[12];
            for (int f = 0; f < h.FrameCount; f++)
            {
                for (int c = 0; c < 12; c++)
                {
                    total[c] += mid[f * 12 + c] + low[f * 12 + c];
                }
            }
            double best = -1;
            for (int k = 0; k < 12; k++)
            {
                double s = 0;
                foreach (int d in MajorScale)
                {
                    s += total[(k + d) % 12];
                }
                if (s > best)
                {
                    best = s;
                    h.Key = k;
                }
            }
            foreach (int d in MajorScale)
            {
                h.InScale[(h.Key + d) % 12] = true;
            }

            return h;
        }

        /// <summary>
        /// How strongly each note name sounds just after <paramref name="t0"/> (up to <paramref name="t1"/>),
        /// with extra weight on what was not already sounding before. Largest value is 1.
        /// </summary>
        public void Local(bool low, double t0, double t1, float[] result)
        {
            var c = low ? _low : _mid;
            int last = FrameCount - 1;
            int f0 = FrameOf(t0 + (low ? 0.16 : 0.07), low);
            int f1 = Math.Max(f0, FrameOf(t1 - 0.03, low));
            f1 = Math.Min(f1, f0 + 5);
            f0 = Clamp(f0, 0, last);
            f1 = Clamp(f1, 0, last);
            int fp = Clamp(FrameOf(t0 - (low ? 0.22 : 0.12), low), 0, last);

            float max = 0;
            for (int k = 0; k < 12; k++)
            {
                float post = 0;
                for (int f = f0; f <= f1; f++)
                {
                    post += c[f * 12 + k];
                }
                post /= f1 - f0 + 1;
                float pre = c[fp * 12 + k];
                float v = post + 0.5f * Math.Max(0f, post - pre);
                result[k] = v;
                if (v > max)
                {
                    max = v;
                }
            }
            if (max > 0)
            {
                for (int k = 0; k < 12; k++)
                {
                    result[k] /= max;
                }
            }
        }

        // The frame whose window is centred on time t.
        private static int FrameOf(double t, bool low)
        {
            double half = (low ? LOW_NFFT * 4 : NFFT) / 2.0;
            return (int) Math.Round((t * SAMPLE_RATE - half) / HOP);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        // Peaks of a spectrum, each position refined by a parabola through the log magnitudes.
        private static int FindPeaks(float[] m, int lo, int hi, float relative, float[] pos, float[] mags)
        {
            if (lo < 1)
            {
                lo = 1;
            }
            if (hi > m.Length - 1)
            {
                hi = m.Length - 1;
            }
            float max = 0;
            for (int k = lo; k < hi; k++)
            {
                if (m[k] > max)
                {
                    max = m[k];
                }
            }
            if (max <= 0)
            {
                return 0;
            }

            float threshold = relative * max;
            int count = 0;
            for (int k = lo; k < hi; k++)
            {
                float b = m[k];
                if (b > threshold && b > m[k - 1] && b >= m[k + 1])
                {
                    double la = Math.Log(m[k - 1] + 1e-9), lb = Math.Log(b + 1e-9), lc = Math.Log(m[k + 1] + 1e-9);
                    double d = la - 2 * lb + lc;
                    double off = d < 0 ? 0.5 * (la - lc) / d : 0.0;
                    if (off > 0.5)
                    {
                        off = 0.5;
                    }
                    else if (off < -0.5)
                    {
                        off = -0.5;
                    }
                    pos[count] = (float) (k + off);
                    mags[count] = b;
                    count++;
                }
            }
            return count;
        }

        // Folds the peaks of one spectrum into semitone bins.
        private static void SemitoneSpectrum(float[] mag, float binHz, double a4, float minHz, float[] semis,
            float[] peakPos, float[] peakMag)
        {
            Array.Clear(semis, 0, semis.Length);
            int lo = Math.Max(1, (int) (minHz / binHz));
            int count = FindPeaks(mag, lo, mag.Length - 1, 0.02f, peakPos, peakMag);
            for (int i = 0; i < count; i++)
            {
                double midi = 69 + 12 * Math.Log(peakPos[i] * binHz / a4, 2);
                int p0 = (int) Math.Floor(midi);
                float frac = (float) (midi - p0);
                int p = p0 - P_LO;
                if (p < 0 || p + 1 >= semis.Length)
                {
                    continue;
                }
                float v = (float) Math.Sqrt(peakMag[i]);
                semis[p] += v * (1 - frac);
                semis[p + 1] += v * frac;
            }
        }

        // For each pitch from pLo to pHi, adds up its first five harmonics, then folds octaves together.
        private static void Chroma(float[] semis, int pLo, int pHi, float[] chroma, int offset)
        {
            for (int p = pLo; p <= pHi; p++)
            {
                float s = 0;
                for (int hIdx = 0; hIdx < HarmOffset.Length; hIdx++)
                {
                    int idx = p + HarmOffset[hIdx] - P_LO;
                    if (idx < semis.Length)
                    {
                        s += HarmWeight[hIdx] * semis[idx];
                    }
                }
                chroma[offset + p % 12] += s;
            }
        }

        // Low-pass and keep every fourth sample.
        private static float[] Decimate4(float[] x)
        {
            const int TAPS = 48;
            var h = new double[TAPS];
            double hs = 0;
            for (int i = 0; i < TAPS; i++)
            {
                double k = i - (TAPS - 1) / 2.0;
                double a = Math.PI * k * 0.225;
                double sinc = Math.Abs(a) < 1e-12 ? 1.0 : Math.Sin(a) / a;
                h[i] = sinc * 0.225 * (0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (TAPS - 1)));
                hs += h[i];
            }
            for (int i = 0; i < TAPS; i++)
            {
                h[i] /= hs;
            }

            int n = x.Length / 4;
            var result = new float[n];
            for (int i = 0; i < n; i++)
            {
                double acc = 0;
                int start = i * 4 - TAPS / 2;
                for (int t = 0; t < TAPS; t++)
                {
                    int idx = start + t;
                    if (idx >= 0 && idx < x.Length)
                    {
                        acc += x[idx] * h[t];
                    }
                }
                result[i] = (float) acc;
            }
            return result;
        }

        /// <summary>Radix-2 FFT with a Hann window, returning magnitudes only.</summary>
        private sealed class Fft
        {
            private readonly int     _n;
            private readonly int     _bits;
            private readonly float[] _window;
            private readonly float[] _cos;
            private readonly float[] _sin;
            private readonly int[]   _reverse;
            private readonly float[] _re;
            private readonly float[] _im;

            public Fft(int n)
            {
                _n = n;
                _bits = 0;
                while (1 << _bits < n)
                {
                    _bits++;
                }
                _window = new float[n];
                _cos = new float[n / 2];
                _sin = new float[n / 2];
                _reverse = new int[n];
                _re = new float[n];
                _im = new float[n];
                for (int i = 0; i < n; i++)
                {
                    _window[i] = (float) (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
                    int r = 0;
                    for (int b = 0; b < _bits; b++)
                    {
                        if ((i & (1 << b)) != 0)
                        {
                            r |= 1 << (_bits - 1 - b);
                        }
                    }
                    _reverse[i] = r;
                }
                for (int i = 0; i < n / 2; i++)
                {
                    _cos[i] = (float) Math.Cos(-2 * Math.PI * i / n);
                    _sin[i] = (float) Math.Sin(-2 * Math.PI * i / n);
                }
            }

            public void Magnitudes(float[] x, int start, float[] mag)
            {
                int n = _n;
                for (int i = 0; i < n; i++)
                {
                    int idx = start + i;
                    _re[_reverse[i]] = idx < x.Length ? x[idx] * _window[i] : 0f;
                }
                Array.Clear(_im, 0, n);

                for (int size = 2; size <= n; size <<= 1)
                {
                    int half = size >> 1;
                    int step = n / size;
                    for (int i = 0; i < n; i += size)
                    {
                        for (int j = 0, k = 0; j < half; j++, k += step)
                        {
                            float wr = _cos[k], wi = _sin[k];
                            int a = i + j, b = a + half;
                            float tr = _re[b] * wr - _im[b] * wi;
                            float ti = _re[b] * wi + _im[b] * wr;
                            _re[b] = _re[a] - tr;
                            _im[b] = _im[a] - ti;
                            _re[a] += tr;
                            _im[a] += ti;
                        }
                    }
                }

                for (int i = 0; i <= n / 2; i++)
                {
                    mag[i] = (float) Math.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]);
                }
            }
        }
    }

    /// <summary>Chooses a pitch for every chart note.</summary>
    public static class GuitarVoicePitcher
    {
        /// <summary>
        /// Fills in <see cref="VoiceNote.Pitch"/>. Notes must be in time order.
        /// </summary>
        public static void Assign(SongHarmony harmony, VoiceNote[] notes, bool bass)
        {
            int n = notes.Length;
            if (n == 0)
            {
                return;
            }

            // Guitar: E2..E5. Bass: E1..E3.
            int pLo = bass ? 28 : 40;
            int pHi = bass ? 52 : 76;
            double baseline = bass ? 31.0 : 50.0;
            int count = pHi - pLo + 1;

            var local = new float[12];
            var localLow = new float[12];
            var back = new byte[n * count];
            var cost = new double[count];
            var next = new double[count];

            for (int i = 0; i < n; i++)
            {
                double t = notes[i].Time;
                double tNext = i + 1 < n ? notes[i + 1].Time : t + 1.0;
                double t1 = Math.Min(t + 0.35, Math.Max(t + 0.10, tNext));
                harmony.Local(bass, t, t1, local);
                if (notes[i].FretCount >= 2 && !bass)
                {
                    // Chords sit on the harmony: lean on the bass register as well
                    harmony.Local(true, t, t1, localLow);
                    for (int k = 0; k < 12; k++)
                    {
                        local[k] = 0.5f * local[k] + 0.5f * localLow[k];
                    }
                }

                double target = baseline + 2.4 * notes[i].LowFret;
                int df = i > 0 ? notes[i].LowFret - notes[i - 1].LowFret : 0;
                double gap = i > 0 ? t - (notes[i - 1].Time + notes[i - 1].Length) : 0;

                for (int j = 0; j < count; j++)
                {
                    int p = pLo + j;
                    // How well this pitch fits the music here, staying near the fret's register
                    // and inside the song's scale unless the music clearly leaves it.
                    double emit = 3.0 * local[p % 12] - 0.03 * Math.Abs(p - target)
                        - (harmony.InScale[p % 12] ? 0.0 : 0.6);

                    if (i == 0)
                    {
                        cost[j] = emit;
                        continue;
                    }

                    double bestValue = double.NegativeInfinity;
                    int bestFrom = 0;
                    for (int k = 0; k < count; k++)
                    {
                        int dp = j - k;
                        double move;
                        if (gap > 2.0)
                        {
                            move = 0;
                        }
                        else if (df == 0)
                        {
                            // Same fret: same pitch, unless the music moved
                            move = dp == 0 ? 0 : -(1.0 + 0.1 * Math.Abs(dp));
                        }
                        else if (dp * df > 0)
                        {
                            // Moving the way the frets move, about a tone and a bit per fret
                            move = -0.08 * Math.Abs(Math.Abs(dp) - 2.4 * Math.Abs(df));
                        }
                        else if (dp == 0)
                        {
                            move = -1.5;
                        }
                        else
                        {
                            move = -2.5 - 0.1 * Math.Abs(dp);
                        }

                        double v = cost[k] + move;
                        if (v > bestValue)
                        {
                            bestValue = v;
                            bestFrom = k;
                        }
                    }
                    next[j] = bestValue + emit;
                    back[i * count + j] = (byte) bestFrom;
                }

                if (i > 0)
                {
                    var swap = cost;
                    cost = next;
                    next = swap;
                }
            }

            int at = 0;
            for (int j = 1; j < count; j++)
            {
                if (cost[j] > cost[at])
                {
                    at = j;
                }
            }
            for (int i = n - 1; i >= 0; i--)
            {
                notes[i].Pitch = pLo + at;
                at = back[i * count + at];
            }
        }
    }
}
