// YARG Resurrected - turns microphone audio into the timed pitch inputs the vocals engine scores.
//
// Shared by the phone's own microphone and the microphones of phones joined over Wi-Fi, so both
// are judged the same way. Two things here matter for fair scoring:
//
//  1. WHEN a sound was sung. Audio reaches the game in bursts, a little late, and over Wi-Fi
//     with jitter. Counting samples gives an exact, steady clock for the audio; this file ties
//     that clock to the game's input clock, so every result is stamped with the moment its
//     sound was in the air (plus a fixed, known delay) instead of the moment it was analysed.
//
//  2. WHAT counts as singing. A clear tone is a pitch. Sound without a tone (breath, "s",
//     background noise) is reported as just that, so it can count for spoken notes but can
//     never hold a sung note.
//
// No Unity types in here: the automated tests run this file as it is.
using System;
using System.Collections.Generic;

namespace YARG.Audio.Vocals
{
    /// <summary>One input for the vocals engine.</summary>
    public struct VoiceInputFrame
    {
        /// <summary>
        /// When to hand this to the game, on the game's input clock: the moment the sound
        /// reached the game's audio input, plus <see cref="VoiceInput.HoldSeconds"/>.
        /// </summary>
        public double Time;

        /// <summary>A clap or a tap on the mic (percussion notes, Star Power).</summary>
        public bool IsHit;

        /// <summary>The sung pitch, or 0 for sound without a tone.</summary>
        public float PitchHz;

        /// <summary>Loudness on the scale of the Microphone Sensitivity setting.</summary>
        public float LevelDb;
    }

    public sealed class VoiceInput
    {
        // The audio clock may run a little faster or slower than the game's clock. The link
        // between the two follows the promptest deliveries and creeps up by this much at most.
        private const double CLOCK_DRIFT_PER_SECOND = 0.002;

        // A gap this long means the stream was interrupted (the app was paused, a phone
        // reconnected): start over instead of bridging it.
        private const double MAX_GAP_SECONDS = 0.5;

        private readonly VoiceAnalyzer _analyzer;
        private readonly List<VoiceFrame> _results = new List<VoiceFrame>();
        private readonly float[] _silence = new float[512];

        private bool _clockSet;
        private double _clockOffset;     // input time of sample 0
        private double _lastFeedTime;
        private long _samplesFed;
        private double _lastFrameTime = double.NegativeInfinity;

        public int SampleRate => _analyzer.SampleRate;

        /// <summary>
        /// Vocals Challenge Mode: a tone has to be clearer to count as a pitch.
        /// </summary>
        public bool Strict
        {
            get => _analyzer.Strict;
            set => _analyzer.Strict = value;
        }

        /// <summary>
        /// Every result is stamped this long after its sound arrived, so that it is never
        /// stamped in the past however late the audio was delivered. Set it before a song.
        /// </summary>
        public double HoldSeconds;

        /// <summary>
        /// The longest a result takes to come out after its sound went in (seconds).
        /// <see cref="HoldSeconds"/> should be at least this, plus the usual delivery delay.
        /// </summary>
        public double AnalysisSeconds => _analyzer.MaxCentreToNewestSeconds;

        public VoiceInput(int sampleRate)
        {
            _analyzer = new VoiceAnalyzer(sampleRate);
        }

        public void Reset()
        {
            _analyzer.Reset();
            _clockSet = false;
            _samplesFed = 0;
            _lastFrameTime = double.NegativeInfinity;
        }

        /// <summary>
        /// Feeds audio that just arrived.
        /// </summary>
        /// <param name="samples">Mono audio, -1 to 1.</param>
        /// <param name="now">The game's input clock right now (seconds).</param>
        /// <param name="sensitivityDb">The Microphone Sensitivity setting.</param>
        /// <param name="output">Receives the inputs, in time order.</param>
        /// <param name="missingBefore">
        /// How many samples were lost between the previous audio and this (a dropped packet),
        /// if the source can tell. They are counted as silence so the timing stays right.
        /// </param>
        public void Feed(ReadOnlySpan<float> samples, double now, float sensitivityDb,
            List<VoiceInputFrame> output, long missingBefore = 0)
        {
            int rate = _analyzer.SampleRate;

            // An interruption: forget the past
            if (_clockSet && (now - _lastFeedTime > MAX_GAP_SECONDS || now < _lastFeedTime - MAX_GAP_SECONDS ||
                missingBefore > rate * MAX_GAP_SECONDS || missingBefore < 0))
            {
                Reset();
                missingBefore = 0;
            }

            // Lost audio is silence
            while (missingBefore > 0)
            {
                int count = (int) Math.Min(missingBefore, _silence.Length);
                FeedAnalyzer(new ReadOnlySpan<float>(_silence, 0, count), sensitivityDb, output);
                missingBefore -= count;
            }

            // Tie the audio clock to the game's clock. The newest sample arrived "now" at the
            // latest; deliveries that were held up only ever make that look later, so the
            // earliest ones are the truth.
            long newest = _samplesFed + samples.Length;
            double observed = now - (double) newest / rate;
            if (!_clockSet)
            {
                _clockOffset = observed;
                _clockSet = true;
            }
            else if (observed < _clockOffset)
            {
                _clockOffset = observed;
            }
            else
            {
                double creep = Math.Max(0.0, now - _lastFeedTime) * CLOCK_DRIFT_PER_SECOND;
                _clockOffset = Math.Min(observed, _clockOffset + creep);
            }
            _lastFeedTime = now;

            FeedAnalyzer(samples, sensitivityDb, output);
        }

        private void FeedAnalyzer(ReadOnlySpan<float> samples, float sensitivityDb, List<VoiceInputFrame> output)
        {
            _results.Clear();
            _analyzer.Process(samples, sensitivityDb, _results);
            _samplesFed += samples.Length;

            if (!_clockSet)
            {
                // Silence fed for lost audio right after a reset: nothing to time it against
                return;
            }

            int rate = _analyzer.SampleRate;
            for (int i = 0; i < _results.Count; i++)
            {
                var result = _results[i];

                double time = _clockOffset + (double) result.CentreSample / rate + HoldSeconds;

                // The engine needs inputs in order; results are 20 ms apart, so this only
                // ever nudges by a hair when the clock link moved
                if (time <= _lastFrameTime)
                {
                    time = _lastFrameTime + 0.0005;
                }

                bool emitted = false;

                if (result.IsHit)
                {
                    output.Add(new VoiceInputFrame { Time = time, IsHit = true, PitchHz = 0f, LevelDb = result.LevelDb });
                    emitted = true;
                }

                // Loud enough to be heard: a pitch if it is a tone, otherwise "sound without
                // a tone". (The original microphone code repeated the last pitch for as long
                // as there was any sound, which let noise hold a note; here the scoring engine
                // bridges the odd missing reading instead.)
                if (result.Active)
                {
                    output.Add(new VoiceInputFrame
                    {
                        Time = time,
                        PitchHz = result.Voiced ? result.PitchHz : 0f,
                        LevelDb = result.LevelDb
                    });
                    emitted = true;
                }

                if (emitted)
                {
                    _lastFrameTime = time;
                }
            }
        }
    }
}
