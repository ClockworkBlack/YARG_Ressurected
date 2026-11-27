// Real instrument tracks: which of a song's tracks a part plays.
//
// Guitar Hero songs have the guitar on a track of its own, which cuts out when you miss. The
// converter can save a song's separated instruments the same way (guitar, bass, drums, vocals,
// and keys with everything else that carries a tune). But its charts do not always follow the instrument they are named after:
// in a song carried by the voice the guitar chart follows the vocals and the beat's melody, and
// Deep Music Mode's bass plays the drum hits too. So the converter also writes, in the song's
// folder, a small file "yarg-tracks.txt" saying which tracks each part plays:
//
//     guitar=guitar,vocals,keys
//     bass=bass
//     coop=guitar,vocals,keys
//     rhythm=bass,drums
//
// (coop and rhythm are Deep Music Mode's guitar and bass.) When you miss, those tracks are the
// ones that cut out, and they come back on your next hit: what you play is what you hear.
// A song without the file behaves as always.

using System;
using System.Collections.Generic;
using System.IO;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Player;

namespace YARG.Gameplay
{
    public partial class GameManager
    {
        public const string TRACKS_FILE = "yarg-tracks.txt";

        private Dictionary<string, SongStem[]> _trackParts;
        private bool _trackPartsRead;

        /// <summary>
        /// The tracks this player's part plays, from the song's yarg-tracks.txt, or null when the
        /// song has none (or says nothing about the part): then the part's own track is used.
        /// </summary>
        public SongStem[] PlayedStems(YargPlayer player)
        {
            if (player?.Profile == null)
            {
                return null;
            }

            if (!_trackPartsRead)
            {
                _trackPartsRead = true;
                _trackParts = ReadTrackParts(Song);
            }

            if (_trackParts == null)
            {
                return null;
            }

            bool deep = (player.Profile.CurrentModifiers & Modifier.DeepMusicMode) != 0;
            string part = player.Profile.CurrentInstrument switch
            {
                Instrument.FiveFretGuitar => deep ? "coop" : "guitar",
                Instrument.FiveFretBass => deep ? "rhythm" : "bass",
                Instrument.FiveFretCoopGuitar => "coop",
                Instrument.FiveFretRhythm => "rhythm",
                _ => null,
            };

            return part != null && _trackParts.TryGetValue(part, out var stems) ? stems : null;
        }

        private static Dictionary<string, SongStem[]> ReadTrackParts(YARG.Core.Song.SongEntry song)
        {
            try
            {
                string folder = song?.ActualLocation;
                if (string.IsNullOrEmpty(folder))
                {
                    return null;
                }
                if (!Directory.Exists(folder))
                {
                    folder = Path.GetDirectoryName(folder);
                }

                string path = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, TRACKS_FILE);
                return path != null && File.Exists(path) ? ParseTrackParts(File.ReadAllLines(path)) : null;
            }
            catch (Exception e)
            {
                YargLogger.LogFormatWarning("Could not read yarg-tracks.txt: {0}", e.Message);
                return null;
            }
        }

        /// <summary>Reads "part=track,track" lines; unknown names and empty lists are skipped.</summary>
        public static Dictionary<string, SongStem[]> ParseTrackParts(IEnumerable<string> lines)
        {
            var parts = new Dictionary<string, SongStem[]>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int at = line.IndexOf('=');
                if (line.StartsWith("#") || at <= 0)
                {
                    continue;
                }

                var stems = new List<SongStem>();
                foreach (string name in line.Substring(at + 1).Split(','))
                {
                    SongStem? stem = name.Trim().ToLowerInvariant() switch
                    {
                        "guitar" => SongStem.Guitar,
                        "bass" => SongStem.Bass,
                        "rhythm" => SongStem.Rhythm,
                        "drums" => SongStem.Drums,
                        "vocals" => SongStem.Vocals,
                        "keys" => SongStem.Keys,
                        "song" => SongStem.Song,
                        _ => null,
                    };
                    if (stem != null && !stems.Contains(stem.Value))
                    {
                        stems.Add(stem.Value);
                    }
                }

                if (stems.Count > 0)
                {
                    parts[line.Substring(0, at).Trim().ToLowerInvariant()] = stems.ToArray();
                }
            }

            return parts.Count > 0 ? parts : null;
        }
    }
}
