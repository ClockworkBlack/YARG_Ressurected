// Guitar voice: hearing a sound before choosing it.
//
// Settings > Sound > Guitar Sound On Notes had three names (Clean, Rock, Heavy) and no way to
// know what they sound like short of starting a song. Now picking one plays a short riff with
// it: a few single notes, a power chord and a held note, on the same recorded guitar, through
// the same amplifier, as in a song.

using System;
using Cysharp.Threading.Tasks;
using ManagedBass;
using YARG.Core.Logging;
using YARG.Gameplay;
using YARG.Settings;

namespace YARG.Audio.GuitarVoice
{
    public static class GuitarVoicePreview
    {
        private const double SECONDS = 4.6;
        // BASS_STREAMPROC_END: no more data after this, so the stream ends (and frees itself)
        private const int END = unchecked((int) 0x80000000);

        private static GuitarVoiceMode? _last;
        private static int _handle;
        private static int _generation;

        /// <summary>The setting's callback. Its first call is the saved value being loaded: silent.</summary>
        public static void OnSettingChanged(GuitarVoiceMode mode)
        {
            bool first = _last == null;
            bool changed = _last != mode;
            _last = mode;
            if (first || !changed)
            {
                return;
            }

            if (mode == GuitarVoiceMode.Off)
            {
                Stop();
                return;
            }

            Play(mode);
        }

        public static void Stop()
        {
            _generation++;
            if (_handle != 0)
            {
                Bass.ChannelStop(_handle);
                Bass.StreamFree(_handle);
                _handle = 0;
            }
        }

        /// <summary>Plays the riff with the given sound. Does nothing during a song.</summary>
        public static void Play(GuitarVoiceMode mode)
        {
            if (mode == GuitarVoiceMode.Off || UnityEngine.Object.FindAnyObjectByType<GameManager>() != null)
            {
                return;
            }

            Stop();
            int generation = _generation;
            PlayAsync(mode, generation).Forget();
        }

        private static async UniTaskVoid PlayAsync(GuitarVoiceMode mode, int generation)
        {
            try
            {
                var tone = mode == GuitarVoiceMode.Clean ? GuitarVoiceTone.Clean
                    : mode == GuitarVoiceMode.Heavy ? GuitarVoiceTone.Heavy
                    : GuitarVoiceTone.Rock;
                float volume = 0.75f;
                try
                {
                    volume = SettingsManager.Settings.MasterMusicVolume.Value;
                }
                catch (Exception)
                {
                }

                // Making the sound takes a moment; not on the frame that draws the menu
                short[] pcm = await UniTask.RunOnThreadPool(() => Render(tone));
                await UniTask.SwitchToMainThread();
                if (generation != _generation || pcm == null || pcm.Length == 0)
                {
                    return;
                }

                int handle = Bass.CreateStream(GuitarVoiceSynth.SAMPLE_RATE, 2, BassFlags.AutoFree, StreamProcedureType.Push);
                if (handle == 0)
                {
                    YargLogger.LogFormatWarning("Guitar sound preview: no stream ({0})", Bass.LastError);
                    return;
                }

                Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, Math.Max(0.05f, Math.Min(1f, volume)));
                if (Bass.StreamPutData(handle, pcm, (pcm.Length * 2) | END) == -1 || !Bass.ChannelPlay(handle))
                {
                    YargLogger.LogFormatWarning("Guitar sound preview: could not play ({0})", Bass.LastError);
                    Bass.StreamFree(handle);
                    return;
                }

                _handle = handle;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Guitar sound preview failed");
            }
        }

        /// <summary>The riff as 16-bit stereo. Public for the automated tests.</summary>
        public static short[] Render(GuitarVoiceTone tone)
        {
            // E minor: four picked notes going up, a power chord, two more, and a held note
            var notes = new[]
            {
                Note(0.00, 0, 52, 1, 1, false),
                Note(0.25, 0, 55, 2, 1, false),
                Note(0.50, 0, 57, 3, 1, true),
                Note(0.75, 0, 59, 4, 1, false),
                Note(1.00, 0.85, 40, 1, 2, false),
                Note(2.00, 0, 59, 3, 1, false),
                Note(2.25, 0, 62, 4, 1, true),
                Note(2.50, 1.4, 64, 5, 1, false),
            };

            float[] stereo = GuitarVoiceSynth.Render(notes, SECONDS, tone, false, 0f, 0.11f, GuitarVoiceLoader.Strings(false));
            var pcm = new short[stereo.Length];
            // A short fade at the very end, so the last note's ring does not stop with a click
            int fade = Math.Min(stereo.Length, GuitarVoiceSynth.SAMPLE_RATE / 4 * 2);
            for (int i = 0; i < stereo.Length; i++)
            {
                float v = stereo[i];
                int left = stereo.Length - i;
                if (left < fade)
                {
                    v *= (float) left / fade;
                }
                pcm[i] = (short) Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v * 32767f)));
            }
            return pcm;
        }

        private static VoiceNote Note(double time, double length, int pitch, int fret, int frets, bool soft)
        {
            return new VoiceNote
            {
                Time = time, Length = length, Pitch = pitch, LowFret = fret, FretCount = frets, Soft = soft,
            };
        }
    }
}
