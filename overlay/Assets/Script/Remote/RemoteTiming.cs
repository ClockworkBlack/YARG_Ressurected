// YARG Resurrected - timing for phone controllers. Plain .NET, tested in tests/RemoteServerTests.
//
// Wi-Fi delivers each button press a few milliseconds late, and never by the same amount twice.
// To keep that from hurting accuracy:
//   1. the phone keeps its clock synchronised with the host and stamps every press with the
//      moment the finger touched the screen;
//   2. the host plays each press exactly "hold" milliseconds after that moment, where "hold" is
//      a little more than the connection's usual delay, so early and late packets land the same;
//   3. the player's input calibration is moved by the same "hold", so the press is judged at the
//      moment the finger touched.
using System;

namespace YARG.Remote
{
    /// <summary>
    /// Tracks recent one-way delays (phone to host) and suggests how long to hold inputs.
    /// </summary>
    public sealed class DelayEstimator
    {
        public const double DEFAULT_MS = 20;
        public const double MIN_MS = 6;
        public const double MAX_MS = 70;
        public const double MARGIN_MS = 3;
        private const int MIN_SAMPLES = 5;

        private readonly object _lock = new object();
        private readonly double[] _samples = new double[64];
        private readonly double[] _sorted = new double[64];
        private int _count;
        private int _next;

        public void AddSample(double delayMs)
        {
            if (double.IsNaN(delayMs) || delayMs < 0 || delayMs > 1000)
            {
                return;
            }

            lock (_lock)
            {
                _samples[_next] = delayMs;
                _next = (_next + 1) % _samples.Length;
                if (_count < _samples.Length)
                {
                    _count++;
                }
            }
        }

        /// <summary>
        /// A delay that about 90% of recent packets beat, plus a small margin.
        /// </summary>
        public double EstimateMs()
        {
            lock (_lock)
            {
                if (_count < MIN_SAMPLES)
                {
                    return DEFAULT_MS;
                }

                Array.Copy(_samples, _sorted, _count);
                Array.Sort(_sorted, 0, _count);
                double p90 = _sorted[Math.Min(_count - 1, (int) (_count * 0.9))];
                return Math.Max(MIN_MS, Math.Min(MAX_MS, p90 + MARGIN_MS));
            }
        }
    }

    public static class RemoteTiming
    {
        /// <summary>
        /// When (on the host clock, in ms) a press should be played.
        /// </summary>
        /// <param name="touchHostMs">When the finger touched, on the host clock, as reported by the
        /// phone. 0 or NaN if the phone hasn't synchronised its clock yet.</param>
        /// <param name="arrivalMs">When the packet arrived.</param>
        /// <param name="holdMs">The hold time in use for this phone.</param>
        /// <param name="delaySampleMs">The measured delay of this packet, or -1 if unknown.</param>
        public static double PlayTimeMs(double touchHostMs, double arrivalMs, double holdMs, out double delaySampleMs)
        {
            // A touch can't be in the future, and anything older than half a second means the
            // phone's clock is off, so fall back to "it happened one hold time before it arrived".
            if (double.IsNaN(touchHostMs) || touchHostMs <= 0 ||
                touchHostMs > arrivalMs + 2 || touchHostMs < arrivalMs - 500)
            {
                delaySampleMs = -1;
                return arrivalMs;
            }

            delaySampleMs = Math.Max(0, arrivalMs - touchHostMs);
            return touchHostMs + holdMs;
        }
    }
}
