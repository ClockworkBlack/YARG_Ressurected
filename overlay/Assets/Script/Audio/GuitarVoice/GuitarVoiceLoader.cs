// Guitar voice, part 3: plugging it into the game.
//
// Guitar Hero and Rock Band songs ship with the guitar on its own audio track: the game lets it
// play while you hit notes and silences it when you miss. Most songs people add themselves (and
// everything made by the converter) are a single recording, so there is nothing to silence.
//
// When the setting is on and a song has no separate track for the instrument a player picked,
// this builds one while the song loads: it listens to the song (GuitarVoiceAnalysis), plays the
// player's chart in tune with it on a recorded guitar or bass through a modelled amplifier
// (GuitarVoiceSynth), and adds the result to the song as that instrument's track. From there
// the game treats it like any real guitar track: it sounds as the notes go by, drops out on a
// miss, bends with the whammy bar, follows the Guitar / Bass volume sliders, practice speed
// and seeking.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ManagedBass;
using ManagedBass.Mix;
using YARG.Audio.BASS;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Helpers;
using YARG.Menu.SongSetup;
using YARG.Player;
using YARG.Settings;

namespace YARG.Audio.GuitarVoice
{
    public enum GuitarVoiceMode
    {
        Off,
        Clean,
        Rock,
        Heavy,
    }

    public static class GuitarVoiceLoader
    {
        // Songs longer than this are left alone (analysis memory and loading time)
        private const double MAX_SECONDS = 20 * 60;

        public static bool Enabled =>
            SettingsManager.Settings.GuitarVoice.Value != GuitarVoiceMode.Off || SongSetupState.Freestyle;

        /// <summary>How many guitar or bass tracks were generated for the song loaded last
        /// (0 when the setting is off or the song has its own). Read by the automated play test.</summary>
        public static volatile int LastTracksAdded;

        // The recorded notes ship with the game as two files in StreamingAssets
        private const string STRINGS_FOLDER = "guitar-voice";
        private const string GUITAR_STRINGS = "guitar.bank";
        private const string BASS_STRINGS = "bass.bank";

        private static readonly object StringsLock = new();
        private static StringBank _guitarStrings, _bassStrings;
        private static bool _guitarStringsRead, _bassStringsRead;

        /// <summary>
        /// The recorded notes of the guitar (or the bass), read from the game's files the first
        /// time they are needed and kept. Null if the file is missing or damaged: the modelled
        /// string then stands in, so there is still a guitar. Safe to call from a worker thread.
        /// </summary>
        public static StringBank Strings(bool bass)
        {
            lock (StringsLock)
            {
                if (bass ? _bassStringsRead : _guitarStringsRead)
                {
                    return bass ? _bassStrings : _guitarStrings;
                }

                StringBank bank = null;
                string path = Path.Combine(PathHelper.StreamingAssetsPath ?? "", STRINGS_FOLDER,
                    bass ? BASS_STRINGS : GUITAR_STRINGS);
                try
                {
                    var watch = Stopwatch.StartNew();
                    bank = StringBank.Load(path);
                    // (the path goes first: a text argument at the end of this call would be
                    // taken for the logger's own "which file is calling" argument)
                    YargLogger.LogFormatInfo("Guitar voice: read {0}: {1} recorded notes in {2} ms",
                        path, bank.Recordings.Length, watch.ElapsedMilliseconds);
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Guitar voice: could not read the recorded notes at " + path
                        + "; using the modelled string instead");
                }

                if (bass)
                {
                    _bassStrings = bank;
                    _bassStringsRead = true;
                }
                else
                {
                    _guitarStrings = bank;
                    _guitarStringsRead = true;
                }
                return bank;
            }
        }

        /// <summary>
        /// Adds a generated track for every guitar-family instrument in play that the song has no
        /// separate audio for. Returns the stems that were added. Safe to call from a worker thread.
        /// </summary>
        public static List<SongStem> AddStems(SongEntry song, StemMixer mixer, SongChart chart,
            IReadOnlyList<YargPlayer> players)
        {
            var added = new List<SongStem>();
            LastTracksAdded = 0;
            var mode = SettingsManager.Settings.GuitarVoice.Value;
            bool freestyle = SongSetupState.Freestyle;
            GuitarFreestyle.Clear();
            if ((mode == GuitarVoiceMode.Off && !freestyle) || mixer is not BassStemMixer bassMixer || chart == null)
            {
                return added;
            }

            bool freestyleGuitar = false, freestyleBass = false;

            // Which tracks are wanted, and from which chart (the hardest one in play)
            // (With the modifiers too: Deep Music Mode plays a different part of the chart)
            var wanted = new Dictionary<SongStem, (Instrument Instrument, Difficulty Difficulty, Modifier Modifiers)>();
            foreach (var player in players)
            {
                if (player == null || player.SittingOut)
                {
                    continue;
                }

                var instrument = player.Profile.CurrentInstrument;
                SongStem stem;
                switch (instrument)
                {
                    case Instrument.FiveFretGuitar:
                        stem = SongStem.Guitar;
                        break;
                    case Instrument.FiveFretBass:
                        stem = SongStem.Bass;
                        break;
                    case Instrument.FiveFretRhythm:
                    case Instrument.FiveFretCoopGuitar:
                        stem = SongStem.Rhythm;
                        break;
                    default:
                        continue;
                }

                // Freestyle: this player makes their own sound, so no generated track for them
                if (freestyle && !player.Profile.IsBot && !player.IsReplay)
                {
                    if (stem == SongStem.Bass)
                    {
                        freestyleBass = true;
                    }
                    else
                    {
                        freestyleGuitar = true;
                    }
                    continue;
                }

                // Turned off, or the song already has the real thing
                if (mode == GuitarVoiceMode.Off || mixer[stem] != null
                    || (stem == SongStem.Bass && mixer[SongStem.Rhythm] != null))
                {
                    continue;
                }

                var difficulty = player.Profile.CurrentDifficulty;
                if (!wanted.TryGetValue(stem, out var current) || difficulty > current.Difficulty)
                {
                    wanted[stem] = (instrument, difficulty, player.Profile.CurrentModifiers);
                }
            }

            var copies = bassMixer.SourceCopies;
            if ((wanted.Count == 0 && !freestyleGuitar && !freestyleBass) || copies == null || copies.Count == 0)
            {
                return added;
            }

            var watch = Stopwatch.StartNew();
            var audio = DecodeMono(copies);
            if (audio == null || audio.Length < SongHarmony.SAMPLE_RATE * 5)
            {
                YargLogger.LogWarning("Guitar voice: could not read the song audio; skipping.");
                return added;
            }
            long decodeMs = watch.ElapsedMilliseconds;

            var harmony = SongHarmony.Analyze(audio);
            long analyzeMs = watch.ElapsedMilliseconds - decodeMs;

            // Chart time 0 is `offset` seconds into the audio
            double offset = song.SongOffsetSeconds;

            var tone = mode == GuitarVoiceMode.Clean ? GuitarVoiceTone.Clean
                : mode == GuitarVoiceMode.Heavy ? GuitarVoiceTone.Heavy
                : GuitarVoiceTone.Rock;

            foreach (var (stem, (instrument, difficulty, modifiers)) in wanted)
            {
                try
                {
                    if (!chart.GetFiveFretTrack(instrument, modifiers, difficulty).TryGetDifficulty(difficulty, out var track)
                        || track.Notes.Count == 0)
                    {
                        continue;
                    }

                    bool bass = stem == SongStem.Bass;
                    var notes = CollectNotes(track.Notes, offset, harmony.Seconds);
                    if (notes.Length == 0)
                    {
                        continue;
                    }

                    GuitarVoicePitcher.Assign(harmony, notes, bass);

                    var last = notes[notes.Length - 1];
                    double seconds = Math.Min(harmony.Seconds, last.Time + Math.Max(last.Length, 0.2) + 3.0);

                    // A little under the song's own loudness, so it sits in the mix like a band member
                    float target = Math.Min(0.2f, Math.Max(0.04f, harmony.Rms * (bass ? 0.7f : 0.6f)));
                    var rendered = GuitarVoiceSynth.Render(notes, seconds, tone, bass, harmony.Cents, target,
                        Strings(bass));
                    // ...and rising and falling with it, like one
                    GuitarVoiceSynth.FollowSong(rendered, audio, SongHarmony.SAMPLE_RATE);
                    var wav = GuitarVoiceSynth.ToWav(rendered, GuitarVoiceSynth.SAMPLE_RATE, 2);

                    // The mixer owns the stream from here on
                    if (mixer.AddChannel(new MemoryStream(wav, false), stem))
                    {
                        added.Add(stem);
                    }
                    else
                    {
                        YargLogger.LogFormatWarning("Guitar voice: the mixer refused the {0} track.", stem);
                    }
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Guitar voice: failed to build a track");
                }
            }

            if (freestyleGuitar || freestyleBass)
            {
                try
                {
                    GuitarFreestyle.Prepare(harmony, tone, SongSetupState.KeyShiftFor(song),
                        freestyleGuitar, freestyleBass,
                        freestyleGuitar ? Strings(false) : null, freestyleBass ? Strings(true) : null);
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Freestyle: failed to record the notes");
                }
            }

            LastTracksAdded = added.Count;
            YargLogger.LogFormatInfo(
                "Guitar voice: added {0} track(s) in {1} ms (read {2} ms, listen {3} ms); tuning {4} cents, scale root {5}",
                added.Count, watch.ElapsedMilliseconds, decodeMs, analyzeMs,
                harmony.Cents, harmony.Key);
            return added;
        }

        private static VoiceNote[] CollectNotes(List<GuitarNote> chartNotes, double offset, double audioSeconds)
        {
            var notes = new List<VoiceNote>(chartNotes.Count);
            foreach (var parent in chartNotes)
            {
                int low = int.MaxValue;
                int count = 0;
                double length = 0;
                foreach (var note in parent.AllNotes)
                {
                    int fret;
                    if (note.Fret == (int) FiveFretGuitarFret.Open)
                    {
                        fret = 0;
                    }
                    else if (note.Fret >= (int) FiveFretGuitarFret.Green && note.Fret <= (int) FiveFretGuitarFret.Orange)
                    {
                        fret = note.Fret;
                    }
                    else
                    {
                        continue;
                    }

                    count++;
                    low = Math.Min(low, fret);
                    length = Math.Max(length, note.TimeLength);
                }

                double time = parent.Time + offset;
                if (count == 0 || time < 0 || time >= audioSeconds)
                {
                    continue;
                }

                notes.Add(new VoiceNote
                {
                    Time = time,
                    Length = length,
                    LowFret = low,
                    FretCount = count,
                    Soft = parent.IsHopo || parent.IsTap,
                });
            }

            notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            return notes.ToArray();
        }

        // Mixes the song's sources (leaving out drums, crowd and, when there is anything else,
        // vocals) down to mono at the analysis sample rate.
        private static float[] DecodeMono(IReadOnlyList<(MemoryStream Data, StemMixer.StemInfo[] Stems)> copies)
        {
            static bool IsPitched(SongStem stem)
            {
                return stem is SongStem.Song or SongStem.Guitar or SongStem.Bass or SongStem.Rhythm
                    or SongStem.Keys;
            }

            static bool IsUsable(SongStem stem)
            {
                return IsPitched(stem) || stem == SongStem.Vocals;
            }

            bool anyPitched = copies.Any(c => c.Stems.Any(s => IsPitched(s.Stem)));
            Func<SongStem, bool> include = anyPitched ? (Func<SongStem, bool>) IsPitched : IsUsable;

            const int MIX_RATE = 44100;
            var handles = new List<int>();
            int mixer = 0;
            try
            {
                mixer = BassMix.CreateMixerStream(MIX_RATE, 2, BassFlags.Decode | BassFlags.Float | BassFlags.MixerEnd);
                if (mixer == 0)
                {
                    YargLogger.LogFormatError("Guitar voice: failed to create the listening mixer: {0}", Bass.LastError);
                    return null;
                }

                double longest = 0;
                foreach (var (data, stems) in copies)
                {
                    var picked = stems.Where(s => include(s.Stem)).ToArray();
                    if (picked.Length == 0)
                    {
                        continue;
                    }

                    // Each source gets its own reader over the copied bytes
                    var view = new MemoryStream(data.GetBuffer(), 0, (int) data.Length, false);
                    if (!BassAudioManager.CreateSourceStream(view, out int source))
                    {
                        view.Dispose();
                        continue;
                    }
                    handles.Add(source);
                    longest = Math.Max(longest,
                        Bass.ChannelBytes2Seconds(source, Bass.ChannelGetLength(source)));

                    bool wholeFile = picked.Any(s => s.Indices == null);
                    if (wholeFile)
                    {
                        if (!BassMix.MixerAddChannel(mixer, source, BassFlags.MixerChanDownMix))
                        {
                            YargLogger.LogFormatWarning("Guitar voice: could not add a source: {0}", Bass.LastError);
                        }
                        continue;
                    }

                    // One file holding several instruments: take just the channels of the wanted ones
                    foreach (var info in picked)
                    {
                        int[] map = info.Indices.Append(-1).ToArray();
                        int split = BassMix.CreateSplitStream(source, BassFlags.Decode, map);
                        if (split == 0)
                        {
                            continue;
                        }
                        handles.Add(split);
                        BassMix.MixerAddChannel(mixer, split, BassFlags.MixerChanDownMix);
                    }
                }

                if (handles.Count == 0 || longest <= 0 || longest > MAX_SECONDS)
                {
                    return null;
                }

                return ReadAndDecimate(mixer, MIX_RATE, longest);
            }
            finally
            {
                if (mixer != 0)
                {
                    Bass.StreamFree(mixer);
                }
                // Splits before their sources
                for (int i = handles.Count - 1; i >= 0; i--)
                {
                    Bass.StreamFree(handles[i]);
                }
            }
        }

        // Reads the whole mixer as stereo floats and brings it down to mono at a quarter of the rate
        private static float[] ReadAndDecimate(int mixer, int mixRate, double seconds)
        {
            const int FACTOR = 4; // 44100 -> 11025
            const int TAPS = 48;

            var filter = new float[TAPS];
            {
                double total = 0;
                var h = new double[TAPS];
                for (int i = 0; i < TAPS; i++)
                {
                    double k = i - (TAPS - 1) / 2.0;
                    double a = Math.PI * k * 0.225;
                    double sinc = Math.Abs(a) < 1e-12 ? 1.0 : Math.Sin(a) / a;
                    h[i] = sinc * 0.225 * (0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (TAPS - 1)));
                    total += h[i];
                }
                for (int i = 0; i < TAPS; i++)
                {
                    filter[i] = (float) (h[i] / total);
                }
            }

            long expected = (long) (seconds * mixRate) + mixRate;
            var output = new List<float>((int) (expected / FACTOR) + 16);

            const int CHUNK_FRAMES = 32768;
            var chunk = new float[CHUNK_FRAMES * 2];
            // The last TAPS mono samples of the previous chunk, followed by the current chunk
            var mono = new float[TAPS + CHUNK_FRAMES];
            long totalFrames = 0;
            int phase = 0;

            while (totalFrames < expected)
            {
                int bytes = Bass.ChannelGetData(mixer, chunk, (chunk.Length * 4) | (int) DataFlags.Float);
                if (bytes <= 0)
                {
                    break;
                }

                int frames = bytes / 8;
                for (int i = 0; i < frames; i++)
                {
                    mono[TAPS + i] = 0.5f * (chunk[2 * i] + chunk[2 * i + 1]);
                }

                // Output sample for every FACTOR-th input frame, filtered over the TAPS frames before it
                int at = phase;
                for (; at < frames; at += FACTOR)
                {
                    float acc = 0;
                    int start = at + 1; // window is mono[start .. start + TAPS - 1], ending at this frame
                    for (int t = 0; t < TAPS; t++)
                    {
                        acc += mono[start + t] * filter[t];
                    }
                    output.Add(acc);
                }
                phase = at - frames;

                // Keep the tail for the next chunk
                Array.Copy(mono, frames, mono, 0, TAPS);
                totalFrames += frames;
            }

            return output.ToArray();
        }
    }
}
