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
    /// Chooses what the menu's music player plays: first the "Startup Songs" playlist, in its order,
    /// once per launch (it starts out holding "No Pole" by Don Toliver), then songs from the chosen
    /// source (whole library, Favorites, or a playlist), shuffled or in playlist order.
    /// </summary>
    public static class MenuMusic
    {
        public const string SOURCE_ALL = "All";
        public const string SOURCE_FAVORITES = "Favorites";
        public const string PLAYLIST_PREFIX = "Playlist:";

        /// <summary>The fixed id of the Startup Songs playlist (a normal playlist the game made).</summary>
        public static readonly Guid StartupPlaylistId = new("5a7e0b1e-0000-4000-8000-00000000057a");
        public const string STARTUP_PLAYLIST_NAME = "Startup Songs";

        // The song the Startup Songs playlist starts with, matched by title and artist
        private const string DEFAULT_STARTUP_TITLE = "No Pole";
        private const string DEFAULT_STARTUP_ARTIST = "Don Toliver";

        // The startup songs play once per launch, not every time the menu comes back
        private static List<SongEntry> _startupQueue;
        private static int _startupIndex;
        private static bool _startupDone;

        private static readonly Random _random = new();

        /// <summary>
        /// Picks the next song to play after <paramref name="current"/> (null at startup).
        /// <paramref name="poolSize"/> is how many songs it was picked from, so the caller can
        /// allow a one-song playlist to repeat.
        /// </summary>
        public static SongEntry PickNext(SongEntry current, out int poolSize)
        {
            var settings = SettingsManager.Settings;

            if (!_startupDone)
            {
                if (_startupQueue == null)
                {
                    _startupQueue = settings.MenuMusicPlayStartupSongs.Value
                        ? StartupPlaylist().ToList()
                        : new List<SongEntry>();
                    _startupIndex = 0;
                }

                if (_startupIndex < _startupQueue.Count)
                {
                    poolSize = _startupQueue.Count;
                    var next = _startupQueue[_startupIndex++];
                    // A one-song startup list still moves on to the menu music after it
                    poolSize = Math.Max(poolSize, 2);
                    return next;
                }

                _startupDone = true;
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

        /// <summary>
        /// The Startup Songs playlist, made on first use. The first time "No Pole" by Don Toliver is in
        /// the library it's put at the top; a startup song chosen in builds 19-20 is moved in too.
        /// After that it's an ordinary playlist: add, remove and reorder as you like.
        /// </summary>
        public static Playlist StartupPlaylist()
        {
            var playlist = PlaylistContainer.GetOrCreatePlaylist(StartupPlaylistId, STARTUP_PLAYLIST_NAME);
            var settings = SettingsManager.Settings;
            bool changed = false;

            // Move the old single startup song in
            var oldSong = FindSong(settings.MenuMusicStartupSong.Value);
            if (oldSong != null)
            {
                if (!playlist.ContainsSong(oldSong))
                {
                    playlist.SongHashes.Add(oldSong.Hash);
                }
                settings.MenuMusicStartupSong.SetValueWithoutNotify(string.Empty);
                changed = true;
            }

            // Put the default opener at the top, once (so removing it sticks)
            if (!settings.StartupSongsSeeded)
            {
                var opener = FindDefaultStartupSong();
                if (opener != null)
                {
                    if (!playlist.ContainsSong(opener))
                    {
                        playlist.SongHashes.Insert(0, opener.Hash);
                    }
                    settings.StartupSongsSeeded = true;
                    changed = true;
                }
            }

            if (changed)
            {
                PlaylistContainer.SavePlaylist(playlist);
                SettingsManager.SaveSettings();
            }

            return playlist;
        }

        /// <summary>"No Pole" by Don Toliver, if it's in the library (any version: converted, charted...).</summary>
        private static SongEntry FindDefaultStartupSong()
        {
            foreach (var song in SongContainer.Songs)
            {
                string title = song.Name;
                string artist = song.Artist;
                if (title != null && artist != null &&
                    string.Equals(title.Trim(), DEFAULT_STARTUP_TITLE, StringComparison.OrdinalIgnoreCase) &&
                    artist.IndexOf(DEFAULT_STARTUP_ARTIST, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return song;
                }
            }
            return null;
        }

        public static bool IsStartupSong(SongEntry song)
        {
            return song != null && StartupPlaylist().ContainsSong(song);
        }

        /// <summary>Adds a song to the end of Startup Songs.</summary>
        public static void SetStartupSong(SongEntry song)
        {
            if (song == null) return;
            StartupPlaylist().AddSong(song);
        }

        public static void RemoveStartupSong(SongEntry song)
        {
            if (song == null) return;
            StartupPlaylist().RemoveSong(song);
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

            // A new choice of menu music plays now, not after the rest of the startup songs
            _startupDone = true;

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
