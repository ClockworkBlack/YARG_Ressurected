// Plays a song in the real vocals engine with a list of microphone inputs, the way the game
// does: inputs are queued as they come and the engine is updated every video frame.
using System;
using System.Collections.Generic;
using YARG.Core;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

namespace VocalsTests
{
    public sealed class GameResult
    {
        public int Phrases;
        public int PhrasesHit;
        public double MeanFill;      // average part of each phrase that was sung (0..1)
        public int Score;
        public int BaseScore;
        public float Stars;
        public int MaxCombo;
        public uint TicksHit;
        public float Percent;        // the engine's own "percent" statistic
        public readonly List<double> Fills = new List<double>();
        public readonly List<bool> Hits = new List<bool>();

        /// <summary>Challenge mode: (note start, note length, part sung, counted) for every note.</summary>
        public readonly List<(double Start, double Length, double Sung, bool Counted)> Notes =
            new List<(double, double, double, bool)>();

        public double HitShare => Phrases == 0 ? 0 : (double) PhrasesHit / Phrases;
        public double ScoreShare => BaseScore == 0 ? 0 : (double) Score / BaseScore;
    }

    public static class SimGame
    {
        // The same numbers the game uses (VocalsPlayer / BasePlayer)
        public static readonly float[] StarThresholds = { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f };
        public static readonly float[] SoloThresholds = { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f };

        /// <summary>The number the game passes as the microphone's update rate.</summary>
        public const float MIC_UPDATES_PER_SECOND = 25f;

        public static VocalsEngineParameters Parameters(Difficulty difficulty, bool challenge,
            EnginePreset preset = null, double songSpeed = 1.0)
        {
            preset ??= EnginePreset.Default;
            float updates = (float) (MIC_UPDATES_PER_SECOND / Math.Max(1.0, songSpeed));
            return preset.Vocals.Create(StarThresholds, SoloThresholds, difficulty, updates, false, challenge);
        }

        /// <summary>Challenge parameters with explicit numbers (for choosing them).</summary>
        public static VocalsEngineParameters Custom(Difficulty difficulty, float window, float perfect,
            double phrasePercent, double notePercent, int maxMisses, double grace)
        {
            var normal = Parameters(difficulty, false);
            return new VocalsEngineParameters(normal.HitWindow, normal.MaxMultiplier, StarThresholds, SoloThresholds,
                window, perfect, phrasePercent, MIC_UPDATES_PER_SECOND, false, normal.PointsPerPhrase,
                isChallenge: true, noteHitPercent: notePercent, maxNoteMisses: maxMisses, edgeGrace: grace, voicedOnly: true);
        }

        public static GameResult Play(SimSong song, List<SimInput> inputs, VocalsEngineParameters parameters,
            double framesPerSecond = 60.0, double frameOffset = 0.0)
        {
            var engine = new YargVocalsEngine(song.BuildChart(), song.BuildSyncTrack(), parameters, false);
            engine.SetSpeed(1.0);
            return Run(engine, song, inputs, parameters, framesPerSecond, frameOffset);
        }

        /// <summary>
        /// Plays the song, starts the same engine over (as a practice restart or a replay seek
        /// does) and plays it again.
        /// </summary>
        public static GameResult PlayTwice(SimSong song, List<SimInput> inputs, VocalsEngineParameters parameters,
            out GameResult second)
        {
            var engine = new YargVocalsEngine(song.BuildChart(), song.BuildSyncTrack(), parameters, false);
            engine.SetSpeed(1.0);
            var first = Run(engine, song, inputs, parameters, 60.0, 0.0);
            engine.Reset();
            second = Run(engine, song, inputs, parameters, 60.0, 0.0);
            return first;
        }

        private static GameResult Run(YargVocalsEngine engine, SimSong song, List<SimInput> inputs,
            VocalsEngineParameters parameters, double framesPerSecond, double frameOffset)
        {
            var result = new GameResult();
            double threshold = parameters.PhraseHitPercent;

            VocalsEngine.PhraseHitEvent onPhrase = (percent, full, last) =>
            {
                result.Phrases++;
                if (full)
                {
                    result.PhrasesHit++;
                }
                result.Fills.Add(engine.LastPhrasePercent);
                result.Hits.Add(full);
            };
            VocalsEngine.NoteJudgedEvent onNote = (note, sung, counted) =>
            {
                result.Notes.Add((note.Time, note.TotalTimeLength, sung, counted));
            };
            engine.OnPhraseHit += onPhrase;
            engine.OnNoteJudged += onNote;

            int next = 0;
            double step = 1.0 / framesPerSecond;
            for (double time = frameOffset; time < song.Length; time += step)
            {
                while (next < inputs.Count && inputs[next].Time <= time)
                {
                    var input = inputs[next++];
                    if (input.Time < 0)
                    {
                        continue;
                    }

                    var gameInput = input.IsHit
                        ? GameInput.Create(input.Time, VocalsAction.Hit, true)
                        : GameInput.Create(input.Time, VocalsAction.Pitch, input.Midi);
                    engine.QueueInput(ref gameInput);
                }

                engine.Update(time);
            }

            engine.OnPhraseHit -= onPhrase;
            engine.OnNoteJudged -= onNote;

            double fill = 0;
            foreach (double f in result.Fills)
            {
                fill += f;
            }
            result.MeanFill = result.Fills.Count == 0 ? 0 : fill / result.Fills.Count;
            result.Score = engine.EngineStats.TotalScore;
            result.BaseScore = engine.BaseScore;
            result.Stars = engine.EngineStats.Stars;
            result.MaxCombo = engine.EngineStats.MaxCombo;
            result.TicksHit = engine.EngineStats.TicksHit;
            result.Percent = engine.EngineStats.Percent;
            return result;
        }
    }
}
