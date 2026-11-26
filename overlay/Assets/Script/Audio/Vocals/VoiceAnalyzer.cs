// YARG Resurrected - pitch and loudness of a singing voice.
//
// One analyzer per microphone. Audio goes in as it arrives (any block size); a result comes out
// every 20 ms: how loud the sound is, whether it is a sung/spoken tone, and its pitch.
//
// The pitch is found with the YIN method (de Cheveigne & Kawahara, 2002): the sound is compared
// with a delayed copy of itself, and the delay at which the two match best is one period of the
// voice. How well they match ("clarity") tells a voice from breath, consonants and noise, so
// noise never gets a pitch.
//
// No Unity types in here: the automated tests run this file as it is.
using System;
using System.Collections.Generic;

namespace YARG.Audio.Vocals
{
    /// <summary>One analysis result (20 ms apart).</summary>
    public struct VoiceFrame
    {
        /// <summary>
        /// Where the middle of the analysed sound is, counted in samples fed to the analyzer
        /// since the last <see cref="VoiceAnalyzer.Reset"/> (at the microphone's sample rate).
        /// </summary>
        public long CentreSample;

        /// <summary>Loudness on the scale of YARG's "Microphone Sensitivity" setting (dB).</summary>
        public float LevelDb;

        /// <summary>The sound is louder than the sensitivity setting.</summary>
        public bool Active;

        /// <summary>A sudden loud sound (a clap or a tap on the mic).</summary>
        public bool IsHit;

        /// <summary>A clear tone was found; <see cref="PitchHz"/> is valid.</summary>
        public bool Voiced;

        public float PitchHz;

        /// <summary>0 (noise) to 1 (a perfectly steady tone).</summary>
        public float Clarity;
    }

    public sealed class VoiceAnalyzer
    {
        public const int FRAMES_PER_SECOND = 50;

        /// <summary>Lowest and highest pitch looked for: C2 to just above C6.</summary>
        public const float MIN_PITCH_HZ = 65f;
        public const float MAX_PITCH_HZ = 1100f;

        /// <summary>A jump in loudness of this many dB within 40 ms counts as a hit (as in YARG).</summary>
        public const float HIT_JUMP_DB = 25f;

        /// <summary>Clarity needed for a sound to count as a tone.</summary>
        public const float CLARITY_NORMAL = 0.60f;
        public const float CLARITY_STRICT = 0.72f;

        // A delay whose mismatch is under this is taken at once (the YIN "absolute threshold");
        // it is what keeps the detector from answering an octave too low.
        private const float SURE_MISMATCH = 0.15f;

        private const double HIT_REFRACTORY_SECONDS = 0.12;

        private readonly int _sampleRate;
        private readonly int _decimation;
        private readonly double _rate;      // after decimation
        private readonly int _hop;          // decimated samples between results
        private readonly int _window;       // decimated samples compared
        private readonly int _minLag;
        private readonly int _maxLag;
        private readonly int _span;         // _window + _maxLag

        private readonly Biquad _highPass;
        private readonly Biquad _lowPassA;
        private readonly Biquad _lowPassB;

        private readonly float[] _buffer;   // the newest _span decimated samples, oldest first
        private readonly float[] _difference;
        private readonly float[] _mismatch;

        // Samples that arrived since the last result (at most one hop), waiting to be shifted in
        private readonly float[] _pending;
        private int _pendingCount;

        private int _decimationPhase;
        private int _filled;
        private int _sinceResult;
        private long _samplesIn;

        private float _hopEnergy;
        private float _levelOneHopAgo = float.NaN;
        private float _levelTwoHopsAgo = float.NaN;
        private double _sinceHit = 1.0;

        /// <summary>Use the stricter clarity threshold (Vocals Challenge Mode).</summary>
        public bool Strict;

        public int SampleRate => _sampleRate;

        /// <summary>
        /// The most a result can lag behind the audio it needed: seconds from the middle of
        /// the analysed sound to the newest sample (it is less for higher notes).
        /// </summary>
        public double MaxCentreToNewestSeconds => (_span * 0.5) / _rate;

        public VoiceAnalyzer(int sampleRate)
        {
            if (sampleRate < 8000)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            _sampleRate = sampleRate;

            // The voice's pitch is all below ~1.1 kHz, so the search runs at about 16 kHz
            _decimation = Math.Max(1, (int) Math.Round(sampleRate / 16000.0));
            _rate = (double) sampleRate / _decimation;

            _hop = (int) Math.Round(_rate / FRAMES_PER_SECOND);
            _maxLag = (int) Math.Ceiling(_rate / MIN_PITCH_HZ) + 1;
            _minLag = Math.Max(2, (int) Math.Floor(_rate / MAX_PITCH_HZ));
            _window = (int) Math.Round(_rate * 0.026);
            _span = _window + _maxLag;

            _buffer = new float[_span];
            _pending = new float[_hop + 1];
            _difference = new float[_maxLag + 1];
            _mismatch = new float[_maxLag + 1];

            // Rumble and handling noise out, hiss and sibilance out; done before decimating
            _highPass = Biquad.HighPass(sampleRate, 55.0, 0.7071);
            double lowPass = Math.Min(3000.0, _rate * 0.42);
            _lowPassA = Biquad.LowPass(sampleRate, lowPass, 0.5412);
            _lowPassB = Biquad.LowPass(sampleRate, lowPass, 1.3066);
        }

        public void Reset()
        {
            _highPass.Reset();
            _lowPassA.Reset();
            _lowPassB.Reset();
            Array.Clear(_buffer, 0, _buffer.Length);
            _decimationPhase = 0;
            _filled = 0;
            _pendingCount = 0;
            _sinceResult = 0;
            _samplesIn = 0;
            _hopEnergy = 0f;
            _levelOneHopAgo = float.NaN;
            _levelTwoHopsAgo = float.NaN;
            _sinceHit = 1.0;
        }

        /// <summary>
        /// Feeds audio (-1..1) and appends a <see cref="VoiceFrame"/> to <paramref name="results"/>
        /// for every 20 ms completed. <paramref name="sensitivityDb"/> is YARG's Microphone
        /// Sensitivity setting: quieter sound is treated as silence.
        /// </summary>
        public void Process(ReadOnlySpan<float> samples, float sensitivityDb, List<VoiceFrame> results)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                float x = samples[i];
                if (float.IsNaN(x) || float.IsInfinity(x))
                {
                    x = 0f;
                }

                x = _lowPassB.Step(_lowPassA.Step(_highPass.Step(x)));
                _samplesIn++;

                if (++_decimationPhase < _decimation)
                {
                    continue;
                }
                _decimationPhase = 0;

                // Slide the newest sample in. The buffer is short (about 700 samples), and a
                // plain shift every 20 ms is cheaper than wrapping every read in the search.
                if (_filled < _span)
                {
                    _buffer[_filled++] = x;
                }
                else
                {
                    _pending[_pendingCount++] = x;
                }
                _hopEnergy += x * x;

                if (++_sinceResult < _hop)
                {
                    continue;
                }
                _sinceResult = 0;

                FlushPending();
                results.Add(Analyse(sensitivityDb));
                _hopEnergy = 0f;
            }
        }

        private void FlushPending()
        {
            if (_pendingCount == 0)
            {
                return;
            }

            if (_pendingCount >= _span)
            {
                Array.Copy(_pending, _pendingCount - _span, _buffer, 0, _span);
            }
            else
            {
                Array.Copy(_buffer, _pendingCount, _buffer, 0, _span - _pendingCount);
                Array.Copy(_pending, 0, _buffer, _span - _pendingCount, _pendingCount);
            }
            _pendingCount = 0;
        }

        private VoiceFrame Analyse(float sensitivityDb)
        {
            var frame = new VoiceFrame();

            // The search compares the newest _window samples with the ones one period earlier,
            // so until a period is known the analysed sound is just that window
            frame.CentreSample = _samplesIn - (long) Math.Round(_window * 0.5 * _decimation);

            // Loudness of the whole analysed stretch (about 40 ms, as YARG's level always was)
            double energy = 0;
            for (int i = 0; i < _span; i++)
            {
                energy += _buffer[i] * _buffer[i];
            }
            frame.LevelDb = ToLevelDb(Math.Sqrt(energy / _span));
            frame.Active = _filled >= _span && frame.LevelDb >= sensitivityDb;

            // A hit: the last 20 ms are far louder than the 20 ms two steps back
            float hopLevel = ToLevelDb(Math.Sqrt(_hopEnergy / _hop));
            _sinceHit += 1.0 / FRAMES_PER_SECOND;
            if (!float.IsNaN(_levelTwoHopsAgo) && hopLevel >= sensitivityDb &&
                hopLevel - _levelTwoHopsAgo >= HIT_JUMP_DB && _sinceHit >= HIT_REFRACTORY_SECONDS)
            {
                frame.IsHit = true;
                _sinceHit = 0.0;
            }
            _levelTwoHopsAgo = _levelOneHopAgo;
            _levelOneHopAgo = hopLevel;

            if (!frame.Active)
            {
                return frame;
            }

            float lag = FindPeriod(out float clarity);
            frame.Clarity = clarity;
            if (lag > 0f && clarity >= (Strict ? CLARITY_STRICT : CLARITY_NORMAL))
            {
                frame.Voiced = true;
                frame.PitchHz = (float) (_rate / lag);
                frame.CentreSample = _samplesIn - (long) Math.Round((_window + lag) * 0.5 * _decimation);
            }

            return frame;
        }

        /// <summary>YARG's microphone level: 20 * log10(rms * 180), never below -160.</summary>
        public static float ToLevelDb(double rms)
        {
            if (rms <= 0.0)
            {
                return -160f;
            }

            float db = (float) (20.0 * Math.Log10(rms * 180.0));
            return db < -160f ? -160f : db;
        }

        // YIN steps 2 to 5. Returns the period in (decimated) samples, or 0.
        private float FindPeriod(out float clarity)
        {
            var x = _buffer;
            var d = _difference;
            var m = _mismatch;
            int window = _window;
            int maxLag = _maxLag;

            // Difference function over the newest samples, and its running-mean normalised form
            int start = _span - window;
            d[0] = 0f;
            m[0] = 1f;
            double running = 0;
            for (int lag = 1; lag <= maxLag; lag++)
            {
                float sum = 0f;
                int earlier = start - lag;
                for (int i = 0; i < window; i++)
                {
                    float delta = x[start + i] - x[earlier + i];
                    sum += delta * delta;
                }
                d[lag] = sum;
                running += sum;
                m[lag] = running > 0 ? (float) (sum * lag / running) : 1f;
            }

            // The first delay that matches well enough, followed down to the bottom of its dip
            int best = -1;
            for (int lag = _minLag; lag < maxLag; lag++)
            {
                if (m[lag] < SURE_MISMATCH)
                {
                    while (lag + 1 < maxLag && m[lag + 1] < m[lag])
                    {
                        lag++;
                    }
                    best = lag;
                    break;
                }
            }

            // Otherwise the best match anywhere (a breathy or shaky voice)
            if (best < 0)
            {
                float lowest = float.MaxValue;
                for (int lag = _minLag; lag < maxLag; lag++)
                {
                    // Only real dips: lower than both neighbours
                    if (m[lag] < lowest && m[lag] <= m[lag - 1] && m[lag] <= m[lag + 1])
                    {
                        lowest = m[lag];
                        best = lag;
                    }
                }
            }

            if (best < 0)
            {
                clarity = 0f;
                return 0f;
            }

            clarity = Math.Max(0f, Math.Min(1f, 1f - m[best]));

            // Parabola through the dip and its neighbours for a period finer than one sample
            float refined = best;
            if (best > 1 && best < maxLag)
            {
                float a = m[best - 1], b = m[best], c = m[best + 1];
                float curve = a - 2f * b + c;
                if (curve > 1e-12f)
                {
                    float shift = 0.5f * (a - c) / curve;
                    if (shift > -1f && shift < 1f)
                    {
                        refined = best + shift;
                    }
                }
            }

            return refined;
        }

        /// <summary>Hertz to a MIDI note number (A4 = 440 Hz = 69), fractions included.</summary>
        public static float HzToMidi(float hz)
        {
            return 12f * (float) Math.Log(hz / 440.0, 2.0) + 69f;
        }

        // Second-order filter section (RBJ cookbook)
        private sealed class Biquad
        {
            private readonly float _b0, _b1, _b2, _a1, _a2;
            private float _z1, _z2;

            private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
            {
                _b0 = (float) (b0 / a0);
                _b1 = (float) (b1 / a0);
                _b2 = (float) (b2 / a0);
                _a1 = (float) (a1 / a0);
                _a2 = (float) (a2 / a0);
            }

            public static Biquad LowPass(double sampleRate, double frequency, double q)
            {
                double w = 2.0 * Math.PI * frequency / sampleRate;
                double alpha = Math.Sin(w) / (2.0 * q);
                double cos = Math.Cos(w);
                return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
            }

            public static Biquad HighPass(double sampleRate, double frequency, double q)
            {
                double w = 2.0 * Math.PI * frequency / sampleRate;
                double alpha = Math.Sin(w) / (2.0 * q);
                double cos = Math.Cos(w);
                return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
            }

            public float Step(float x)
            {
                // Transposed direct form II
                float y = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * y + _z2;
                _z2 = _b2 * x - _a2 * y;
                return y;
            }

            public void Reset()
            {
                _z1 = 0f;
                _z2 = 0f;
            }
        }
    }
}
