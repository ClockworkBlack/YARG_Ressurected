using System;
using System.Collections.Generic;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Playlists;
using YARG.Settings;
using YARG.Song;

namespace YARG.Menu.Persistent
{
    /// <summary>
    /// Chooses what the menu's music player plays: an optional song first on startup, then songs from
    /// the chosen source (whole library, Favorites, or a playlist), shuffled or in playlist order.
    /// </summary>
    public static class MenuMusic
    {
        public const string SOURCE_ALL = "All";
        public const string SOURCE_FAVORITES = "Favorites";
        public const string PLAYLIST_PREFIX = "Playlist:";

        // The startup song plays once per launch, not every time the menu comes back
        private static bool _startupSongDone;

        private static readonly Random _random = new();

        /// <summary>
        /// Picks the next song to play after <paramref name="current"/> (null at startup).
        /// <paramref name="poolSize"/> is how many songs it was picked from, so the caller can
        /// allow a one-song playlist to repeat.
        /// </summary>
        public static SongEntry PickNext(SongEntry current, out int poolSize)
        {
            var settings = SettingsManager.Settings;

            if (!_startupSongDone)
            {
                _startupSongDone = true;
                var startup = FindSong(settings.MenuMusicStartupSong.Value);
                if (startup != null)
                {
                    poolSize = 1;
                    return startup;
                }
            }

            var pool = GetPool(settings.MenuMusicSource.Value);
            if (pool == null || pool.Count == 0)
            {
                // Whole library
                poolSize = SongContainer.Count;
                return poolSize > 0 ? SongContainer.GetRandomSong() : null;
            }

            poolSize = pool.Count;
            if (pool.Count == 1)
            {
                return pool[0];
            }

            if (settings.MenuMusicShuffle.Value)
            {
                SongEntry pick;
                do
                {
                    pick = pool[_random.Next(pool.Count)];
                } while (pick == current);
                return pick;
            }

            // In playlist order, wrapping around at the end
            int index = current != null ? pool.IndexOf(current) : -1;
            return pool[(index + 1) % pool.Count];
        }

        /// <summary>
        /// The songs of the chosen favorites / playlist that are in the library,
        /// or null for the whole library (also when the playlist is gone).
        /// </summary>
        private static List<SongEntry> GetPool(string source)
        {
            var playlist = GetPlaylist(source);
            return playlist?.ToList();
        }

        public static Playlist GetPlaylist(string source)
        {
            if (string.IsNullOrEmpty(source) || source == SOURCE_ALL)
            {
                return null;
            }

            if (source == SOURCE_FAVORITES)
            {
                return PlaylistContainer.FavoritesPlaylist;
            }

            if (source.StartsWith(PLAYLIST_PREFIX) &&
                Guid.TryParse(source[PLAYLIST_PREFIX.Length..], out var id) &&
                PlaylistContainer.Playlists != null)
            {
                foreach (var playlist in PlaylistContainer.Playlists)
                {
                    if (playlist.Id == id)
                    {
                        return playlist;
                    }
                }
            }

            return null;
        }

        public static string SourceFor(Playlist playlist)
        {
            if (playlist == null)
            {
                return SOURCE_ALL;
            }

            return playlist == PlaylistContainer.FavoritesPlaylist
                ? SOURCE_FAVORITES
                : PLAYLIST_PREFIX + playlist.Id;
        }

        public static bool IsMenuMusicSource(Playlist playlist)
        {
            return playlist != null && !playlist.Ephemeral &&
                SettingsManager.Settings.MenuMusicSource.Value == SourceFor(playlist);
        }

        public static void UseAsMenuMusic(Playlist playlist)
        {
            SettingsManager.Settings.MenuMusicSource.Value = SourceFor(playlist);
            SettingsManager.SaveSettings();
        }

        public static bool IsStartupSong(SongEntry song)
        {
            return song != null &&
                SettingsManager.Settings.MenuMusicStartupSong.Value == song.Hash.ToString();
        }

        public static void SetStartupSong(SongEntry song)
        {
            SettingsManager.Settings.MenuMusicStartupSong.Value = song != null ? song.Hash.ToString() : string.Empty;
            SettingsManager.SaveSettings();
        }

        public static SongEntry FindSong(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return null;
            }

            try
            {
                var wrapper = HashWrapper.FromString(hash);
                if (SongContainer.SongsByHash.TryGetValue(wrapper, out var songs) && songs.Count > 0)
                {
                    return songs[0];
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Bad startup song hash in settings");
            }

            return null;
        }

        public static string DescribeSource(string source)
        {
            if (string.IsNullOrEmpty(source) || source == SOURCE_ALL)
            {
                return "Whole library";
            }

            if (source == SOURCE_FAVORITES)
            {
                return "Favorites";
            }

            var playlist = GetPlaylist(source);
            return playlist != null
                ? "Playlist: " + playlist.Name
                : "Deleted playlist (plays whole library)";
        }

        public static string DescribeStartupSong(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "None";
            }

            var song = FindSong(hash);
            if (song == null)
            {
                // Before the library has loaded, or the song was removed
                return SongContainer.Count > 0 ? "Song not in library" : "Saved song";
            }

            return $"{song.Artist} - {song.Name}";
        }

        /// <summary>
        /// Called when the source or the shuffle setting changes: switch the music right away.
        /// </summary>
        public static void OnSettingChanged()
        {
            if (!SettingsManager.SettingContainer.IsInitialized)
            {
                return;
            }

            var helpBar = HelpBar.Instance;
            // Nothing playing yet: the player will pick from the new settings when it starts
            if (helpBar == null || helpBar.MusicPlayer == null || !helpBar.MusicPlayer.isActiveAndEnabled ||
                MusicPlayer.NowPlaying == null)
            {
                return;
            }

            helpBar.MusicPlayer.NextSong();
        }
    }
}
