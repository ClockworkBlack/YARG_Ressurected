// Freestyle guitar: play your own notes over the song.
//
// With freestyle on (Song Setup screen), every strum sounds a real guitar note straight away,
// whether or not the chart has a note there. The notes come from the song's own scale, tuned to
// the recording, so anything you play fits:
//
//   - the five frets are the five notes of the song's pentatonic scale (green lowest), and a
//     strum with no fret held is the note just under green;
//   - strum down for the low octave, strum up for the same notes an octave higher;
//   - two frets make a two-string power chord on the lower one, three or more a full one;
//   - change frets while a note rings and it hammers on or pulls off without a new strum;
//     let go of every fret and the string is damped;
//   - the whammy bar bends the ringing note down, like a real one.
//
// The notes are recorded ahead of time (while the song loads) by the same modelled guitar that
// plays the generated guitar track, then played back the instant the input arrives.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ManagedBass;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Input;
using YARG.Audio.BASS;
using YARG.Core.Logging;
using YARG.Settings;

namespace YARG.Audio.GuitarVoice
{
    /// <summary>The recorded notes for one instrument (guitar or bass) in one song.</summary>
    public sealed class FreestyleBank : IDisposable
    {
        public const double SECONDS = 3.0;
        private const int SLOTS = 6;   // open, then the five frets
        private const int OCTAVES = 2; // strum down, strum up

        private readonly bool      _bass;
        private readonly short[][] _recordings;
        private int[]              _samples;

        public bool IsBass => _bass;

        // Index of a single note: octave * SLOTS + slot.
        // Index of a chord (guitar only): SLOTS * OCTAVES + (octave * 5 + fret) * 2 + (full ? 1 : 0).

        private FreestyleBank(bool bass, short[][] recordings)
        {
            _bass = bass;
            _recordings = recordings;
        }

        /// <summary>Records the notes. Slow (a second or two): call from a worker thread.</summary>
        public static FreestyleBank Build(SongHarmony harmony, GuitarVoiceTone tone, bool bass,
            float keyShiftSemitones, float targetRms)
        {
            // The pentatonic scale every song in this key shares: built on the minor root,
            // which sits three semitones under the major root.
            int rootName = (harmony.Key + 9) % 12;
            int lowest = bass ? 31 : 43;
            int root = lowest + ((rootName - lowest) % 12 + 12) % 12;
            int[] steps = { -2, 0, 3, 5, 7, 10 };

            var notes = new List<VoiceNote>();
            for (int octave = 0; octave < OCTAVES; octave++)
            {
                for (int slot = 0; slot < SLOTS; slot++)
                {
                    notes.Add(new VoiceNote { Pitch = root + steps[slot] + 12 * octave, FretCount = 1 });
                }
            }
            if (!bass)
            {
                for (int octave = 0; octave < OCTAVES; octave++)
                {
                    for (int fret = 0; fret < 5; fret++)
                    {
                        int pitch = root + steps[fret + 1] + 12 * octave;
                        notes.Add(new VoiceNote { Pitch = pitch, FretCount = 2 });
                        notes.Add(new VoiceNote { Pitch = pitch, FretCount = 3 });
                    }
                }
            }

            // In tune with the recording, and moved along with it if the song's key was changed
            float cents = harmony.Cents + 100f * keyShiftSemitones;
            var recordings = GuitarVoiceSynth.RenderBank(notes.ToArray(), SECONDS, tone, bass, cents, targetRms);
            return new FreestyleBank(bass, recordings);
        }

        /// <summary>Hands the recordings to the audio system. Call on the main thread.</summary>
        public void EnsureLoaded()
        {
            if (_samples != null)
            {
                return;
            }

            _samples = new int[_recordings.Length];
            for (int i = 0; i < _recordings.Length; i++)
            {
                var data = _recordings[i];
                int sample = Bass.CreateSample(data.Length * 2, GuitarVoiceSynth.SAMPLE_RATE, 1, 3,
                    BassFlags.SampleOverrideLongestPlaying);
                if (sample == 0)
                {
                    YargLogger.LogFormatError("Freestyle: could not create a sample: {0}", Bass.LastError);
                    continue;
                }

                var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    if (!Bass.SampleSetData(sample, pin.AddrOfPinnedObject()))
                    {
                        YargLogger.LogFormatError("Freestyle: could not fill a sample: {0}", Bass.LastError);
                    }
                }
                finally
                {
                    pin.Free();
                }
                _samples[i] = sample;
            }
        }

        /// <summary>
        /// The recording for a set of held frets (bit 0 = green ... bit 4 = orange; 0 = open).
        /// </summary>
        public int SampleFor(int fretMask, bool highOctave)
        {
            if (_samples == null)
            {
                return 0;
            }

            int octave = highOctave ? 1 : 0;
            int lowFret = -1, count = 0;
            for (int f = 0; f < 5; f++)
            {
                if ((fretMask & (1 << f)) != 0)
                {
                    count++;
                    if (lowFret < 0)
                    {
                        lowFret = f;
                    }
                }
            }

            int index;
            if (count == 0)
            {
                index = octave * SLOTS;
            }
            else if (count == 1 || _bass)
            {
                index = octave * SLOTS + 1 + lowFret;
            }
            else
            {
                index = SLOTS * OCTAVES + (octave * 5 + lowFret) * 2 + (count >= 3 ? 1 : 0);
            }
            return index < _samples.Length ? _samples[index] : 0;
        }

        public void Dispose()
        {
            if (_samples == null)
            {
                return;
            }
            foreach (int sample in _samples)
            {
                if (sample != 0)
                {
                    Bass.SampleFree(sample);
                }
            }
            _samples = null;
        }
    }

    /// <summary>One player's hands on the freestyle guitar.</summary>
    public sealed class FreestyleVoice
    {
        private readonly FreestyleBank _bank;
        private readonly SongStem      _volumeStem;

        private int    _fretMask;
        private int    _channel;       // the note ringing now (0 = none)
        private int    _ringingMask;
        private bool   _ringingHigh;
        private double _ringingSince;
        private bool   _hasStrummed;
        private float  _whammy;

        public FreestyleVoice(FreestyleBank bank, SongStem volumeStem)
        {
            _bank = bank;
            _volumeStem = volumeStem;
        }

        /// <param name="now">Any steadily increasing clock, in seconds.</param>
        public void Input(GuitarAction action, bool pressed, float axis, double now)
        {
            switch (action)
            {
                case GuitarAction.GreenFret:
                case GuitarAction.RedFret:
                case GuitarAction.YellowFret:
                case GuitarAction.BlueFret:
                case GuitarAction.OrangeFret:
                {
                    int bit = 1 << ((int) action - (int) GuitarAction.GreenFret);
                    int before = _fretMask;
                    _fretMask = pressed ? _fretMask | bit : _fretMask & ~bit;
                    if (_fretMask != before)
                    {
                        FretsChanged(pressed, now);
                    }
                    break;
                }
                case GuitarAction.StrumDown:
                case GuitarAction.StrumUp:
                    if (pressed)
                    {
                        _hasStrummed = true;
                        Play(_fretMask, action == GuitarAction.StrumUp, 1f, now);
                    }
                    break;
                case GuitarAction.Whammy:
                    _whammy = axis < 0f ? 0f : axis > 1f ? 1f : axis;
                    ApplyWhammy();
                    break;
            }
        }

        private void FretsChanged(bool pressed, double now)
        {
            bool ringing = _channel != 0 && now - _ringingSince < FreestyleBank.SECONDS
                && Bass.ChannelIsActive(_channel) == PlaybackState.Playing;

            if (_fretMask == 0)
            {
                // Hand off the neck: the string stops
                if (ringing && _ringingMask != 0)
                {
                    Damp(0.06);
                }
                return;
            }

            if (ringing)
            {
                // Hammer-on or pull-off: the new note sounds without a strum, a little softer
                if (_fretMask != _ringingMask)
                {
                    Play(_fretMask, _ringingHigh, 0.7f, now);
                }
            }
            else if (pressed && !_hasStrummed)
            {
                // Played without a strum bar (touch lanes, keyboards): a press is a note
                Play(_fretMask, false, 1f, now);
            }
        }

        private void Play(int mask, bool high, float velocity, double now)
        {
            int sample = _bank.SampleFor(mask, high);
            if (sample == 0)
            {
                return;
            }

            // One string at a time, like a lead line: the old note makes way for the new one
            Damp(0.025);

            int channel = Bass.SampleGetChannel(sample);
            if (channel == 0)
            {
                return;
            }

            // The slider itself, not the stem's current level: the game turns the stem down
            // when a chart note is missed, and that must not silence what you play yourself
            double setting = _volumeStem == SongStem.Bass
                ? SettingsManager.Settings.BassVolume.Value
                : SettingsManager.Settings.GuitarVolume.Value;
            double volume = BassAudioManager.ExponentialVolume(setting) * velocity;
            Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, volume);
            Bass.ChannelSetAttribute(channel, ChannelAttribute.Frequency, WhammyRate());
            if (!Bass.ChannelPlay(channel, true))
            {
                return;
            }

            _channel = channel;
            _ringingMask = mask;
            _ringingHigh = high;
            _ringingSince = now;
        }

        private void Damp(double seconds)
        {
            if (_channel == 0)
            {
                return;
            }
            // Sliding the volume to -1 fades the note out and then stops it
            Bass.ChannelSlideAttribute(_channel, ChannelAttribute.Volume, -1f, (int) (seconds * 1000));
            _channel = 0;
        }

        private float WhammyRate()
        {
            // Down by up to a semitone (times the game's whammy depth setting)
            double semitones = -_whammy * GlobalAudioHandler.WhammyPitchShiftAmount;
            return (float) (GuitarVoiceSynth.SAMPLE_RATE * Math.Pow(2, semitones / 12.0));
        }

        private void ApplyWhammy()
        {
            if (_channel != 0)
            {
                Bass.ChannelSetAttribute(_channel, ChannelAttribute.Frequency, WhammyRate());
            }
        }

        public void Silence()
        {
            Damp(0.05);
        }
    }

    /// <summary>The freestyle banks for the song being played.</summary>
    public static class GuitarFreestyle
    {
        private static FreestyleBank _guitar;
        private static FreestyleBank _bassBank;
        private static readonly List<FreestyleVoice> Voices = new();

        /// <summary>Is there anything to play with in this song?</summary>
        public static bool Active => _guitar != null || _bassBank != null;

        public static bool Wants(Instrument instrument)
        {
            return instrument is Instrument.FiveFretGuitar or Instrument.FiveFretBass
                or Instrument.FiveFretRhythm or Instrument.FiveFretCoopGuitar;
        }

        /// <summary>Records the notes for this song. Worker thread.</summary>
        public static void Prepare(SongHarmony harmony, GuitarVoiceTone tone, float keyShiftSemitones,
            bool guitar, bool bass)
        {
            Clear();
            float target = Math.Min(0.2f, Math.Max(0.04f, harmony.Rms * 0.6f));
            if (guitar)
            {
                _guitar = FreestyleBank.Build(harmony, tone, false, keyShiftSemitones, target);
            }
            if (bass)
            {
                _bassBank = FreestyleBank.Build(harmony, tone, true, keyShiftSemitones, target * 1.15f);
            }
        }

        /// <summary>A voice for one player, or null when freestyle is not on for them. Main thread.</summary>
        public static FreestyleVoice CreateVoice(Instrument instrument)
        {
            if (!Wants(instrument))
            {
                return null;
            }

            bool bass = instrument == Instrument.FiveFretBass;
            var bank = bass ? _bassBank : _guitar;
            if (bank == null)
            {
                return null;
            }

            bank.EnsureLoaded();
            var voice = new FreestyleVoice(bank, bass ? SongStem.Bass : SongStem.Guitar);
            Voices.Add(voice);
            return voice;
        }

        public static void SilenceAll()
        {
            foreach (var voice in Voices)
            {
                voice.Silence();
            }
        }

        public static void Clear()
        {
            SilenceAll();
            Voices.Clear();
            _guitar?.Dispose();
            _guitar = null;
            _bassBank?.Dispose();
            _bassBank = null;
        }
    }
}
