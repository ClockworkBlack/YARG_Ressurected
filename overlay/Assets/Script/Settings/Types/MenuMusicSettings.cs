using System;
using YARG.Menu.Persistent;
using YARG.Playlists;

namespace YARG.Settings.Types
{
    /// <summary>
    /// Where the menu's background music comes from: the whole library, Favorites, or one of your playlists.
    /// Stored as <see cref="MenuMusic.SOURCE_ALL"/>, <see cref="MenuMusic.SOURCE_FAVORITES"/>
    /// or <see cref="MenuMusic.PLAYLIST_PREFIX"/> + the playlist's id, so renaming a playlist keeps it.
    /// </summary>
    public class MenuMusicSourceSetting : DropdownSetting<string>
    {
        public MenuMusicSourceSetting(string value, Action<string> onChange = null)
            : base(value, onChange, localizable: false)
        {
        }

        protected override void SetValue(string value)
        {
            base.SetValue(string.IsNullOrEmpty(value) ? MenuMusic.SOURCE_ALL : value);
            UpdateValues();
        }

        public override void UpdateValues()
        {
            _possibleValues.Clear();
            _possibleValues.Add(MenuMusic.SOURCE_ALL);
            _possibleValues.Add(MenuMusic.SOURCE_FAVORITES);

            var playlists = PlaylistContainer.Playlists;
            if (playlists != null)
            {
                foreach (var playlist in playlists)
                {
                    _possibleValues.Add(MenuMusic.PLAYLIST_PREFIX + playlist.Id);
                }
            }

            // Keep a deleted playlist selectable so the dropdown still shows what is saved
            // (the music player falls back to the whole library for it)
            if (_value != null && !_possibleValues.Contains(_value))
            {
                _possibleValues.Add(_value);
            }
        }

        public override string ValueToString(string value) => MenuMusic.DescribeSource(value);
    }

    /// <summary>
    /// The song that plays first every time the game starts ("" = none).
    /// Chosen from the music library: highlight a song, open More Options, "Play On Startup".
    /// The dropdown in Settings only offers the chosen song or None, to turn it off.
    /// </summary>
    public class StartupSongSetting : DropdownSetting<string>
    {
        public StartupSongSetting(string value, Action<string> onChange = null)
            : base(value, onChange, localizable: false)
        {
        }

        protected override void SetValue(string value)
        {
            base.SetValue(value ?? string.Empty);
            UpdateValues();
        }

        public override void UpdateValues()
        {
            _possibleValues.Clear();
            _possibleValues.Add(string.Empty);
            if (!string.IsNullOrEmpty(_value))
            {
                _possibleValues.Add(_value);
            }
        }

        public override string ValueToString(string value) => MenuMusic.DescribeStartupSong(value);
    }
}
