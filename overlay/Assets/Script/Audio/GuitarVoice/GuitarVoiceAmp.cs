// Guitar voice, part 2b: the amplifier, the speaker cabinet and the room.
//
// Plain .NET only (no Unity, no BASS) so the automated tests can run it.
//
// A guitar's own signal is thin and plain. What people know as "electric guitar sound" is made
// after it, so this follows the real signal path stage by stage:
//
//   - valve stages in a row, each with its own filters in front and behind. A valve does not clip
//     evenly (it runs into grid current one way and towards cut-off the other), which is where
//     the warm even overtones come from, and the bass is held back before the heavy stages so
//     chords stay tight instead of turning to mud;
//   - a power stage that clips evenly and sags under load, so a hard attack is squeezed and the
//     note then blooms;
//   - a speaker cabinet: a guitar speaker is very far from flat. It has a thump at the bottom,
//     a peak around 2.5 to 4 kHz (the "bite") and falls away quickly above 5 kHz. The curves
//     here were fitted to recordings of real amplified guitars, overtone by overtone;
//   - a little room, different left and right.
//
// The distorting stages run at four times the sample rate, because clipping makes overtones far
// above what the output can hold and they would otherwise fold back down as rough, tuneless grit.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YARG.Audio.GuitarVoice
{
    /// <summary>A two-pole filter (or a one-pole one, with the unused terms at zero).</summary>
    internal struct Filter
    {
        private double _b0, _b1, _b2, _a1, _a2, _z1, _z2;

        public static Filter LowPass(double hz, double q, double rate)
        {
            double w = 2 * Math.PI * hz / rate, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + alpha;
            return new Filter
            {
                _b0 = (1 - c) / 2 / a0, _b1 = (1 - c) / a0, _b2 = (1 - c) / 2 / a0,
                _a1 = -2 * c / a0, _a2 = (1 - alpha) / a0,
            };
        }

        public static Filter HighPass(double hz, double q, double rate)
        {
            double w = 2 * Math.PI * hz / rate, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + alpha;
            return new Filter
            {
                _b0 = (1 + c) / 2 / a0, _b1 = -(1 + c) / a0, _b2 = (1 + c) / 2 / a0,
                _a1 = -2 * c / a0, _a2 = (1 - alpha) / a0,
            };
        }

        public static Filter Peak(double hz, double q, double db, double rate)
        {
            double amp = Math.Pow(10, db / 40);
            double w = 2 * Math.PI * hz / rate, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + alpha / amp;
            return new Filter
            {
                _b0 = (1 + alpha * amp) / a0, _b1 = -2 * c / a0, _b2 = (1 - alpha * amp) / a0,
                _a1 = -2 * c / a0, _a2 = (1 - alpha / amp) / a0,
            };
        }

        public static Filter LowShelf(double hz, double q, double db, double rate)
        {
            double amp = Math.Pow(10, db / 40);
            double w = 2 * Math.PI * hz / rate, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w);
            double s = 2 * Math.Sqrt(amp) * alpha;
            double a0 = (amp + 1) + (amp - 1) * c + s;
            return new Filter
            {
                _b0 = amp * ((amp + 1) - (amp - 1) * c + s) / a0,
                _b1 = 2 * amp * ((amp - 1) - (amp + 1) * c) / a0,
                _b2 = amp * ((amp + 1) - (amp - 1) * c - s) / a0,
                _a1 = -2 * ((amp - 1) + (amp + 1) * c) / a0,
                _a2 = ((amp + 1) + (amp - 1) * c - s) / a0,
            };
        }

        public static Filter HighShelf(double hz, double q, double db, double rate)
        {
            double amp = Math.Pow(10, db / 40);
            double w = 2 * Math.PI * hz / rate, alpha = Math.Sin(w) / (2 * q), c = Math.Cos(w);
            double s = 2 * Math.Sqrt(amp) * alpha;
            double a0 = (amp + 1) - (amp - 1) * c + s;
            return new Filter
            {
                _b0 = amp * ((amp + 1) + (amp - 1) * c + s) / a0,
                _b1 = -2 * amp * ((amp - 1) + (amp + 1) * c) / a0,
                _b2 = amp * ((amp + 1) + (amp - 1) * c - s) / a0,
                _a1 = 2 * ((amp - 1) - (amp + 1) * c) / a0,
                _a2 = ((amp + 1) - (amp - 1) * c - s) / a0,
            };
        }

        /// <summary>One pole: a gentle 6 dB per octave slope above <paramref name="hz"/>.</summary>
        public static Filter LowPass1(double hz, double rate)
        {
            double k = 1 - Math.Exp(-2 * Math.PI * hz / rate);
            return new Filter { _b0 = k, _a1 = k - 1 };
        }

        /// <summary>One pole: a gentle 6 dB per octave slope below <paramref name="hz"/>.</summary>
        public static Filter HighPass1(double hz, double rate)
        {
            double p = Math.Exp(-2 * Math.PI * hz / rate);
            return new Filter { _b0 = (1 + p) / 2, _b1 = -(1 + p) / 2, _a1 = -p };
        }

        public double Run(double x)
        {
            double o = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * o + _z2;
            _z2 = _b2 * x - _a2 * o;
            return o;
        }

        public void Run(double[] buffer, int count)
        {
            double b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2, z1 = _z1, z2 = _z2;
            for (int i = 0; i < count; i++)
            {
                double x = buffer[i];
                double o = b0 * x + z1;
                z1 = b1 * x - a1 * o + z2;
                z2 = b2 * x - a2 * o;
                buffer[i] = o;
            }
            // A filter left with a vanishingly small number in it slows some processors to a crawl
            _z1 = Math.Abs(z1) < 1e-30 ? 0 : z1;
            _z2 = Math.Abs(z2) < 1e-30 ? 0 : z2;
        }
    }

    /// <summary>What one valve does to the wave, looked up from a table.</summary>
    internal sealed class ValveCurve
    {
        private const int SIZE = 8192;
        private const double RANGE = 24.0;

        private readonly double[] _table = new double[SIZE + 1];
        private readonly double _scale = SIZE / (2 * RANGE);

        /// <summary>
        /// A preamp valve. Upward swings run into grid current and stop firmly (sharpness
        /// <paramref name="up"/>); downward swings head for cut-off more gradually
        /// (<paramref name="down"/>). <paramref name="bias"/> moves the resting point, so the two
        /// halves of the wave are clipped unevenly. A valve stage also turns the wave upside down
        /// (<paramref name="invert"/>), so the next stage clips the other half harder.
        /// </summary>
        public static ValveCurve Triode(double bias, double up, double down, bool invert)
        {
            var curve = new ValveCurve();
            double rest = Shape(bias, up, down);
            double sign = invert ? -1.0 : 1.0;
            for (int i = 0; i <= SIZE; i++)
            {
                double x = -RANGE + i / curve._scale;
                curve._table[i] = sign * (Shape(x + bias, up, down) - rest);
            }
            return curve;
        }

        /// <summary>A power stage: two valves working against each other, so it clips evenly.</summary>
        public static ValveCurve PushPull(double sharpness)
        {
            return Triode(0, sharpness, sharpness, false);
        }

        private static double Shape(double u, double up, double down)
        {
            return u >= 0 ? Math.Tanh(up * u) / up : Math.Tanh(down * u) / down;
        }

        public void Run(double[] buffer, int count, double gain)
        {
            var table = _table;
            double scale = _scale;
            for (int i = 0; i < count; i++)
            {
                double at = (buffer[i] * gain + RANGE) * scale;
                if (at <= 0)
                {
                    buffer[i] = table[0];
                }
                else if (at >= SIZE)
                {
                    buffer[i] = table[SIZE];
                }
                else
                {
                    int k = (int) at;
                    double f = at - k;
                    double a = table[k];
                    buffer[i] = a + f * (table[k + 1] - a);
                }
            }
        }
    }

    /// <summary>One valve stage: filters, gain into the valve, filters.</summary>
    internal sealed class AmpStage
    {
        public Filter[] Before = Array.Empty<Filter>();
        public double Gain = 1;
        public double Bias, Up = 1, Down = 1;
        public bool Invert = true;
        public Filter[] After = Array.Empty<Filter>();
    }

    /// <summary>A whole amplifier: every setting needed to turn a guitar signal into a sound.</summary>
    internal sealed class AmpSettings
    {
        public const int RATE = GuitarVoiceSynth.SAMPLE_RATE;
        public const int HIGH_RATE = RATE * GuitarAmp.OVERSAMPLE;

        /// <summary>Filters on the way in, at the normal rate.</summary>
        public Func<Filter[]> Input = () => Array.Empty<Filter>();

        /// <summary>Quiet stretches are turned down further, like a noise gate pedal (0 = off).</summary>
        public double GateThreshold;

        /// <summary>The valve stages, in order (filters given at the high rate).</summary>
        public Func<AmpStage[]> Stages = () => Array.Empty<AmpStage>();

        /// <summary>Tone controls between the preamp and the power stage (high rate).</summary>
        public Func<Filter[]> Tone = () => Array.Empty<Filter>();

        public double PowerGain = 1, PowerSharpness = 1;

        /// <summary>How much the power supply gives way under a loud signal.</summary>
        public double Sag, SagAttack = 0.008, SagRelease = 0.12;

        /// <summary>Speaker and microphone (normal rate).</summary>
        public Func<Filter[]> Cabinet = () => Array.Empty<Filter>();

        /// <summary>How much room sound is mixed in, and how long it rings (seconds).</summary>
        public float RoomMix = 0.1f;
        public double RoomSeconds = 0.9;

        // ---- The amplifiers ------------------------------------------------------------------

        public static AmpSettings For(GuitarVoiceTone tone, bool bass)
        {
            if (bass)
            {
                return Bass;
            }
            return tone == GuitarVoiceTone.Clean ? Clean : tone == GuitarVoiceTone.Heavy ? Heavy : Rock;
        }

        // The tone-and-cabinet chains below were fitted, overtone by overtone, so that real
        // direct-recorded guitar notes played through each amplifier come out with the same
        // balance of low, middle and high as recordings of real amplified guitars playing the
        // same notes (see tools/string-bank/README.md).

        /// <summary>
        /// A clean amp turned up to where it just starts to round the hardest notes off:
        /// scooped middle, some sparkle around 4 kHz, the speaker rolling off above that.
        /// </summary>
        public static readonly AmpSettings Clean = new AmpSettings
        {
            Input = () => new[] { Filter.HighPass(60, 0.707, RATE) },
            Stages = () => new[]
            {
                new AmpStage
                {
                    Before = new[] { Filter.HighPass1(50, HIGH_RATE) },
                    Gain = 0.9, Bias = 0.10, Up = 0.9, Down = 0.6,
                    After = new[] { Filter.LowPass1(12000, HIGH_RATE) },
                },
            },
            Tone = () => new[] { Filter.HighPass1(40, HIGH_RATE) },
            PowerGain = 0.9, PowerSharpness = 0.8, Sag = 0.3,
            Cabinet = () => new[]
            {
                Filter.HighPass(60, 1.06, RATE),
                Filter.Peak(131, 2.04, 4.4, RATE),
                Filter.Peak(248, 1.76, -2.8, RATE),
                Filter.Peak(826, 0.57, -4.8, RATE),
                Filter.Peak(1201, 1.61, -4.2, RATE),
                Filter.Peak(2364, 1.11, 4.1, RATE),
                Filter.Peak(4268, 2.2, 6.0, RATE),
                Filter.LowPass(7500, 0.5, RATE),
                Filter.LowPass(9500, 0.5, RATE),
            },
            RoomMix = 0.10f, RoomSeconds = 1.2,
        };

        /// <summary>
        /// A stack pushed into overdrive: crunchy chords, singing single notes, and it cleans
        /// up as a note dies away. The bass is held back before the valves so chords stay clear.
        /// </summary>
        public static readonly AmpSettings Rock = new AmpSettings
        {
            Input = () => new[] { Filter.HighPass(70, 0.707, RATE) },
            Stages = () => new[]
            {
                new AmpStage
                {
                    Before = new[] { Filter.HighPass1(150, HIGH_RATE), Filter.LowShelf(600, 0.6, -8.0, HIGH_RATE) },
                    Gain = 5.0, Bias = 0.15, Up = 1.3, Down = 0.75,
                    After = new[] { Filter.LowPass1(10000, HIGH_RATE) },
                },
                new AmpStage
                {
                    Before = new[] { Filter.HighPass1(80, HIGH_RATE) },
                    Gain = 3.0, Bias = 0.10, Up = 1.2, Down = 0.8,
                    After = new[] { Filter.LowPass1(8000, HIGH_RATE) },
                },
            },
            Tone = () => new[] { Filter.HighPass1(40, HIGH_RATE) },
            PowerGain = 2.0, PowerSharpness = 1.0, Sag = 0.45,
            Cabinet = () => new[]
            {
                Filter.HighPass(85, 0.68, RATE),
                Filter.Peak(158, 2.2, -3.4, RATE),
                Filter.Peak(239, 1.34, -4.1, RATE),
                Filter.Peak(791, 0.7, -5.3, RATE),
                Filter.Peak(1402, 1.53, -3.7, RATE),
                Filter.Peak(2422, 1.72, 5.4, RATE),
                Filter.Peak(3466, 1.24, 4.0, RATE),
                Filter.LowPass(4791, 0.82, RATE),
                Filter.LowPass(9409, 0.9, RATE),
            },
            RoomMix = 0.11f, RoomSeconds = 1.0,
        };

        /// <summary>
        /// A high-gain amp with a boost in front: thick, tight and sustaining, with the
        /// scooped middle and hard-edged top of a modern metal sound.
        /// </summary>
        public static readonly AmpSettings Heavy = new AmpSettings
        {
            Input = () => new[] { Filter.HighPass(70, 0.707, RATE) },
            GateThreshold = 0.004,
            Stages = () => new[]
            {
                new AmpStage
                {
                    Before = new[] { Filter.HighPass1(120, HIGH_RATE), Filter.LowShelf(500, 0.6, -9.0, HIGH_RATE) },
                    Gain = 10.0, Bias = 0.12, Up = 1.4, Down = 0.8,
                    After = new[] { Filter.LowPass1(9000, HIGH_RATE) },
                },
                new AmpStage
                {
                    Before = new[] { Filter.HighPass1(80, HIGH_RATE) },
                    Gain = 5.0, Bias = 0.10, Up = 1.3, Down = 0.8,
                    After = new[] { Filter.LowPass1(7500, HIGH_RATE) },
                },
                new AmpStage
                {
                    Before = new[] { Filter.HighPass1(180, HIGH_RATE) },
                    Gain = 4.0, Bias = 0.08, Up = 1.2, Down = 0.9,
                    After = new[] { Filter.LowPass1(6500, HIGH_RATE) },
                },
            },
            Tone = () => new[] { Filter.HighPass1(40, HIGH_RATE) },
            PowerGain = 1.6, PowerSharpness = 1.0, Sag = 0.35,
            Cabinet = () => new[]
            {
                Filter.HighPass(60, 1.4, RATE),
                Filter.Peak(93, 1.05, 6.6, RATE),
                Filter.Peak(313, 1.33, -5.0, RATE),
                Filter.Peak(858, 1.48, -2.6, RATE),
                Filter.Peak(1567, 1.03, -9.7, RATE),
                Filter.Peak(2611, 2.2, 5.2, RATE),
                Filter.Peak(3358, 1.77, 5.7, RATE),
                Filter.LowPass(4343, 0.87, RATE),
                Filter.LowPass(9436, 0.9, RATE),
            },
            RoomMix = 0.08f, RoomSeconds = 0.8,
        };

        /// <summary>A bass amp: a little valve warmth and a big, slow speaker.</summary>
        public static readonly AmpSettings Bass = new AmpSettings
        {
            Input = () => new[] { Filter.HighPass(32, 0.707, RATE) },
            Stages = () => new[]
            {
                new AmpStage
                {
                    Before = new[] { Filter.HighPass1(20, HIGH_RATE) },
                    Gain = 1.6, Bias = 0.10, Up = 1.0, Down = 0.7,
                    After = new[] { Filter.LowPass1(9000, HIGH_RATE) },
                },
            },
            Tone = () => new[] { Filter.HighPass1(20, HIGH_RATE) },
            PowerGain = 1.2, PowerSharpness = 0.9, Sag = 0.4,
            Cabinet = () => new[]
            {
                Filter.HighPass(38, 0.8, RATE),
                Filter.Peak(90, 0.9, 3.0, RATE),
                Filter.Peak(400, 0.8, -3.0, RATE),
                Filter.Peak(1800, 1.0, 3.0, RATE),
                Filter.LowPass(3600, 0.7, RATE),
                Filter.LowPass(4500, 0.7, RATE),
            },
            RoomMix = 0f,
        };
    }

    internal static class GuitarAmp
    {
        public const int OVERSAMPLE = 4;

        // The filter used to go up to the high rate and back: a windowed sinc. 45 taps keeps the
        // delay a whole number of output samples (11), which is then taken back out.
        private const int TAPS = 45;
        private const int DELAY = (TAPS - 1) / OVERSAMPLE;
        private static readonly double[] Lowpass = MakeLowpass();

        // Work is done in pieces, on several processor cores at once. Each piece starts a little
        // early so that its filters have settled by the time its own part begins.
        private const int PIECE = 12 * AmpSettings.RATE;
        private const int LEAD_IN = AmpSettings.RATE / 2;
        private const int BLOCK = 512;

        // Below this the guitar counts as silent, and silence is not worth amplifying
        private const float SILENCE = 1e-7f;
        private const int TAIL = AmpSettings.RATE / 4;

        private static double[] MakeLowpass()
        {
            // Cut-off at the old half sample rate, Kaiser window (beta 8: about 80 dB)
            const double BETA = 8.0;
            var h = new double[TAPS];
            double sum = 0;
            for (int i = 0; i < TAPS; i++)
            {
                double k = i - (TAPS - 1) / 2.0;
                double a = Math.PI * k / OVERSAMPLE;
                double sinc = Math.Abs(a) < 1e-12 ? 1.0 : Math.Sin(a) / a;
                double r = 2.0 * i / (TAPS - 1) - 1.0;
                h[i] = sinc * BesselI0(BETA * Math.Sqrt(Math.Max(0, 1 - r * r))) / BesselI0(BETA);
                sum += h[i];
            }
            for (int i = 0; i < TAPS; i++)
            {
                h[i] /= sum;
            }
            return h;
        }

        private static double BesselI0(double x)
        {
            double sum = 1, term = 1, half = x / 2;
            for (int k = 1; k < 40; k++)
            {
                term *= half / k;
                sum += term * term;
            }
            return sum;
        }

        /// <summary>Plays <paramref name="signal"/> (one channel, in place) through the amplifier.</summary>
        public static void Run(float[] signal, AmpSettings settings)
        {
            Run(signal, settings, PIECE);
        }

        /// <param name="pieceLength">How long a piece to hand to each processor core, in samples.</param>
        internal static void Run(float[] signal, AmpSettings settings, int pieceLength)
        {
            var pieces = Pieces(signal, pieceLength);

            // Each piece is worked on in place, but starts from the bare signal a little before
            // its own part (and needs a few samples after it), which belong to its neighbours:
            // keep copies of those before anything is overwritten.
            int n = signal.Length;
            var before = new float[pieces.Count][];
            var after = new float[pieces.Count][];
            for (int p = 0; p < pieces.Count; p++)
            {
                var (leadFrom, from, to) = pieces[p];
                before[p] = new float[from - leadFrom];
                Array.Copy(signal, leadFrom, before[p], 0, before[p].Length);
                after[p] = new float[Math.Min(DELAY, n - to)];
                Array.Copy(signal, to, after[p], 0, after[p].Length);
            }

            Parallel.For(0, pieces.Count, p =>
            {
                var (leadFrom, from, to) = pieces[p];
                RunPiece(signal, before[p], after[p], leadFrom, from, to, settings);
            });

            // Nothing at all where the guitar is silent
            int quietFrom = 0;
            foreach (var (_, from, to) in pieces)
            {
                Array.Clear(signal, quietFrom, from - quietFrom);
                quietFrom = to;
            }
            Array.Clear(signal, quietFrom, n - quietFrom);
        }

        // The stretches where the guitar is sounding, cut into pieces of a few seconds
        private static List<(int LeadFrom, int From, int To)> Pieces(float[] signal, int pieceLength)
        {
            var pieces = new List<(int, int, int)>();
            int n = signal.Length;
            int i = 0;
            while (i < n)
            {
                while (i < n && Math.Abs(signal[i]) <= SILENCE)
                {
                    i++;
                }
                if (i >= n)
                {
                    break;
                }

                // A stretch runs until the guitar has been silent for a while
                int start = Math.Max(0, i - DELAY - 1);
                int lastSound = i;
                int j = i;
                while (j < n && j - lastSound <= TAIL)
                {
                    if (Math.Abs(signal[j]) > SILENCE)
                    {
                        lastSound = j;
                    }
                    j++;
                }
                int end = j;

                for (long from = start; from < end; from += pieceLength)
                {
                    int to = (int) Math.Min(end, from + pieceLength);
                    pieces.Add(((int) Math.Max(start, from - LEAD_IN), (int) from, to));
                }
                i = end;
            }
            return pieces;
        }

        // Amplifies signal[from .. to) in place. `before` is the bare signal just ahead of it
        // (from leadFrom), `after` the few samples that follow it.
        private static void RunPiece(float[] signal, float[] before, float[] after, int leadFrom, int from, int to,
            AmpSettings s)
        {
            var input = s.Input();
            var stages = s.Stages();
            var curves = new ValveCurve[stages.Length];
            for (int k = 0; k < stages.Length; k++)
            {
                curves[k] = ValveCurve.Triode(stages[k].Bias, stages[k].Up, stages[k].Down, stages[k].Invert);
            }
            var tone = s.Tone();
            var power = ValveCurve.PushPull(s.PowerSharpness);
            var cabinet = s.Cabinet();

            const int HIGH = BLOCK * OVERSAMPLE;
            var low = new double[BLOCK];
            var high = new double[HIGH];
            // The last TAPS inputs (going up) and outputs of the valves (coming down)
            int upTaps = (TAPS + OVERSAMPLE - 1) / OVERSAMPLE;
            var upHistory = new double[upTaps + BLOCK];
            var downHistory = new double[TAPS + HIGH];

            double gate = 0, sag = 0;
            double gateAttack = 1 - Math.Exp(-1.0 / (AmpSettings.RATE * 0.002));
            double gateRelease = 1 - Math.Exp(-1.0 / (AmpSettings.RATE * 0.060));
            double sagAttack = 1 - Math.Exp(-1.0 / (AmpSettings.HIGH_RATE * s.SagAttack));
            double sagRelease = 1 - Math.Exp(-1.0 / (AmpSettings.HIGH_RATE * s.SagRelease));

            // The up-and-down filters delay everything by DELAY samples: input sample k comes
            // out as output sample k (after taking the delay back out) only once input k + DELAY
            // has gone in. So this piece's outputs [from, to) need inputs up to to + DELAY.
            // Writing an output always happens after its own input was read, so the work can be
            // done in place.
            var lowpass = Lowpass;
            for (int at = leadFrom; at < to + DELAY; at += BLOCK)
            {
                int count = Math.Min(BLOCK, to + DELAY - at);
                for (int i = 0; i < count; i++)
                {
                    int src = at + i;
                    if (src < from)
                    {
                        low[i] = before[src - leadFrom];
                    }
                    else if (src < to)
                    {
                        low[i] = signal[src];
                    }
                    else
                    {
                        low[i] = src - to < after.Length ? after[src - to] : 0.0;
                    }
                }

                for (int f = 0; f < input.Length; f++)
                {
                    input[f].Run(low, count);
                }

                if (s.GateThreshold > 0)
                {
                    // Quieter than the threshold: turned down in proportion, twice over
                    double threshold = s.GateThreshold;
                    for (int i = 0; i < count; i++)
                    {
                        double loudness = Math.Abs(low[i]);
                        gate += (loudness > gate ? gateAttack : gateRelease) * (loudness - gate);
                        if (gate < threshold)
                        {
                            double ratio = gate / threshold;
                            low[i] *= ratio * ratio;
                        }
                    }
                }

                // Up to the high rate
                Array.Copy(low, 0, upHistory, upTaps, count);
                int highCount = count * OVERSAMPLE;
                for (int i = 0; i < count; i++)
                {
                    for (int phase = 0; phase < OVERSAMPLE; phase++)
                    {
                        // Output sample i * 4 + phase takes every fourth tap, newest input first
                        double acc = 0;
                        int tap = phase;
                        int idx = i + upTaps;
                        while (tap < TAPS)
                        {
                            acc += lowpass[tap] * upHistory[idx];
                            tap += OVERSAMPLE;
                            idx--;
                        }
                        high[i * OVERSAMPLE + phase] = acc * OVERSAMPLE;
                    }
                }
                Array.Copy(upHistory, count, upHistory, 0, upTaps);

                // The valves
                for (int k = 0; k < stages.Length; k++)
                {
                    var stage = stages[k];
                    for (int f = 0; f < stage.Before.Length; f++)
                    {
                        stage.Before[f].Run(high, highCount);
                    }
                    curves[k].Run(high, highCount, stage.Gain);
                    for (int f = 0; f < stage.After.Length; f++)
                    {
                        stage.After[f].Run(high, highCount);
                    }
                }
                for (int f = 0; f < tone.Length; f++)
                {
                    tone[f].Run(high, highCount);
                }

                // Power stage, with the supply giving way under load
                double powerGain = s.PowerGain;
                if (s.Sag > 0)
                {
                    double depth = s.Sag;
                    for (int i = 0; i < highCount; i++)
                    {
                        double v = high[i] * powerGain;
                        double load = Math.Abs(v);
                        sag += (load > sag ? sagAttack : sagRelease) * (load - sag);
                        high[i] = v / (1 + depth * sag);
                    }
                    power.Run(high, highCount, 1.0);
                }
                else
                {
                    power.Run(high, highCount, powerGain);
                }

                // Back down
                Array.Copy(high, 0, downHistory, TAPS, highCount);
                for (int i = 0; i < count; i++)
                {
                    double acc = 0;
                    int idx = i * OVERSAMPLE + TAPS;
                    for (int tap = 0; tap < TAPS; tap++)
                    {
                        acc += lowpass[tap] * downHistory[idx - tap];
                    }
                    low[i] = acc;
                }
                Array.Copy(downHistory, highCount, downHistory, 0, TAPS);

                for (int f = 0; f < cabinet.Length; f++)
                {
                    cabinet[f].Run(low, count);
                }

                // Output sample for input sample `at + i` belongs DELAY earlier
                int outAt = at - DELAY;
                for (int i = Math.Max(0, from - outAt); i < count; i++)
                {
                    signal[outAt + i] = (float) low[i];
                }
            }
        }

        // ---- Room ----------------------------------------------------------------------------

        /// <summary>
        /// Puts the amplified guitar in a room: the guitar stays in the middle and the room's
        /// answer comes from all around, different left and right. Eight echoing paths feed each
        /// other, so the tail thickens into a smooth wash instead of ringing on a few notes.
        /// </summary>
        public static void Room(float[] mono, float[] stereo, float mix, double seconds)
        {
            int n = mono.Length;
            if (mix <= 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    stereo[2 * i] = mono[i];
                    stereo[2 * i + 1] = mono[i];
                }
                return;
            }

            int[] lengths = { 1123, 1327, 1549, 1733, 1933, 2111, 2333, 2539 };
            const int LINES = 8;
            var lines = new float[LINES][];
            var positions = new int[LINES];
            var gains = new float[LINES];
            var damped = new float[LINES];
            for (int k = 0; k < LINES; k++)
            {
                lines[k] = new float[lengths[k]];
                // Each path loses just enough per trip for the whole to die away in `seconds`
                gains[k] = (float) Math.Pow(10, -3.0 * lengths[k] / (seconds * AmpSettings.RATE));
            }
            const float DAMPING = 0.35f; // highs die faster than lows, as in a real room

            // The first reflections arrive after a short gap
            int gap = (int) (0.012 * AmpSettings.RATE);
            var early = new float[gap + 1];
            int earlyAt = 0;

            // Two smearing stages in front
            int[] smear = { 142, 379 };
            var smearA = new float[smear[0]];
            var smearB = new float[smear[1]];
            int smearAtA = 0, smearAtB = 0;

            var outs = new float[LINES];
            float dry = 1f - mix;
            for (int i = 0; i < n; i++)
            {
                float x = early[earlyAt];
                early[earlyAt] = mono[i];
                if (++earlyAt > gap)
                {
                    earlyAt = 0;
                }

                float a = smearA[smearAtA];
                float xa = -0.6f * x + a;
                smearA[smearAtA] = x + 0.6f * xa;
                if (++smearAtA == smear[0])
                {
                    smearAtA = 0;
                }
                float b = smearB[smearAtB];
                float xb = -0.6f * xa + b;
                smearB[smearAtB] = xa + 0.6f * xb;
                if (++smearAtB == smear[1])
                {
                    smearAtB = 0;
                }

                float sum = 0;
                for (int k = 0; k < LINES; k++)
                {
                    float o = lines[k][positions[k]];
                    damped[k] = o + DAMPING * (damped[k] - o);
                    outs[k] = damped[k] * gains[k];
                    sum += outs[k];
                }

                // Every path feeds every other one (and itself, inverted)
                float feed = sum * (2f / LINES);
                float left = 0, right = 0;
                for (int k = 0; k < LINES; k++)
                {
                    lines[k][positions[k]] = xb + outs[k] - feed;
                    if (++positions[k] == lengths[k])
                    {
                        positions[k] = 0;
                    }
                    // Alternate paths to the left and right, with alternating signs
                    float o = outs[k];
                    if ((k & 1) == 0)
                    {
                        left += (k & 2) == 0 ? o : -o;
                    }
                    else
                    {
                        right += (k & 2) == 0 ? o : -o;
                    }
                }

                float d = mono[i] * dry;
                stereo[2 * i] = d + left * mix * 0.5f;
                stereo[2 * i + 1] = d + right * mix * 0.5f;
            }
        }
    }
}
