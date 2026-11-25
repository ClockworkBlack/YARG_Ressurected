// A stand-in for a singer: a voice-like tone (glottal buzz through vowel formants, with the
// small pitch and loudness wobble real voices have), breath noise and consonants.
// It lets the tests "sing" a chart with known pitch, timing and mistakes.
using System;

namespace VocalsTests
{
    public sealed class SyntheticVoice
    {
        public static readonly double[][] Vowels =
        {
            new[] { 730.0, 1090.0, 2440.0 }, // ah
            new[] { 270.0, 2290.0, 3010.0 }, // ee
            new[] { 300.0, 870.0, 2240.0 },  // oo
            new[] { 530.0, 1840.0, 2480.0 }, // eh
            new[] { 570.0, 840.0, 2410.0 },  // aw
        };

        private readonly int _rate;
        private readonly Random _random;
        private readonly Resonator[] _formants = new Resonator[3];
        private double _phase;
        private double _jitter;
        private double _shimmer = 1.0;
        private double _noiseLow;

        public SyntheticVoice(int sampleRate, int seed)
        {
            _rate = sampleRate;
            _random = new Random(seed);
            SetVowel(0);
        }

        public void SetVowel(int index)
        {
            var vowel = Vowels[index % Vowels.Length];
            double[] bandwidths = { 90.0, 110.0, 170.0 };
            for (int i = 0; i < 3; i++)
            {
                double f = Math.Min(vowel[i], _rate * 0.45);
                if (_formants[i] == null)
                {
                    _formants[i] = new Resonator(_rate, f, bandwidths[i]);
                }
                else
                {
                    _formants[i].Retune(_rate, f, bandwidths[i]);
                }
            }
        }

        /// <summary>One sample of voiced sound at <paramref name="hz"/>. Peak is roughly <paramref name="level"/>.</summary>
        public float Voiced(double hz, double level, double breathiness = 0.03)
        {
            // Slow random wobble of pitch (jitter, about 0.4%) and loudness (shimmer)
            _jitter += (Gaussian() * 0.004 - _jitter) * 0.002;
            _shimmer += (1.0 + Gaussian() * 0.05 - _shimmer) * 0.002;

            _phase += hz * (1.0 + _jitter) / _rate;
            if (_phase >= 1.0)
            {
                _phase -= 1.0;
            }

            // Glottal pulse: open for 60% of the period, closing sharply
            double open = 0.6;
            double source;
            if (_phase < open)
            {
                double t = _phase / open;
                source = 0.5 * (1.0 - Math.Cos(Math.PI * t)) * (t < 0.75 ? 1.0 : Math.Cos((t - 0.75) / 0.25 * Math.PI * 0.5));
            }
            else
            {
                source = 0.0;
            }
            source = source * 2.0 - 0.6;
            source += Gaussian() * breathiness;

            double shaped = 0.0;
            double[] gains = { 1.0, 0.5, 0.25 };
            for (int i = 0; i < 3; i++)
            {
                shaped += _formants[i].Step(source) * gains[i];
            }

            return (float) (shaped * level * _shimmer * 0.5);
        }

        /// <summary>Breath or an "s"/"sh"/"f" sound.</summary>
        public float Noise(double level, bool hiss)
        {
            double white = Gaussian();
            if (hiss)
            {
                // Crude high-pass: white minus its own low part
                _noiseLow += (white - _noiseLow) * 0.35;
                return (float) ((white - _noiseLow) * level * 0.6);
            }

            _noiseLow += (white - _noiseLow) * 0.12;
            return (float) (_noiseLow * level * 1.6);
        }

        public double Gaussian()
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = _random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        public double Uniform() => _random.NextDouble();

        private sealed class Resonator
        {
            private double _a1, _a2, _gain;
            private double _y1, _y2;

            public Resonator(int rate, double frequency, double bandwidth)
            {
                Retune(rate, frequency, bandwidth);
            }

            public void Retune(int rate, double frequency, double bandwidth)
            {
                double r = Math.Exp(-Math.PI * bandwidth / rate);
                _a1 = 2.0 * r * Math.Cos(2.0 * Math.PI * frequency / rate);
                _a2 = -r * r;
                _gain = 1.0 - r;
            }

            public double Step(double x)
            {
                double y = _gain * x + _a1 * _y1 + _a2 * _y2;
                _y2 = _y1;
                _y1 = y;
                return y;
            }
        }
    }
}
