// YARG Resurrected - Story Mode: playing a gig on the gig's own stage.
//
// Some builds of the game draw a venue behind the highway, picked by the "Venue" setting
// (VenueChoice). The story's venue ids are the same as that feature's, so a gig at the Harlem
// theater can be played in front of the Harlem theater. Builds without the feature simply have no
// such setting; the story then plays on whatever background the game shows, and nothing here
// does anything.
//
// The setting is found by name at run time (never named in code), so this file compiles the same
// on every build. It is only borrowed for the moment the game reads it, right as a song starts:
//   - borrowed when the Gameplay scene loads during a gig;
//   - given back three frames after the song has started (the venue is picked by then), and on
//     every scene load that is not Gameplay, which also covers a song that fails to load;
//   - the player's own value is written into the story save while it is borrowed, so if the app is
//     killed during the song it is put back the next time the game starts;
//   - if the game wrote its settings file while the setting was borrowed (a few screens do), the
//     settings are written again once it is given back, so the story's venue never sticks;
//   - a player who chose "None" (no venue) keeps it: the story does not force a stage on them.
using System;
using System.IO;
using System.Reflection;
using YARG.Core.Logging;
using YARG.Helpers;
using YARG.Settings;
using YARG.Settings.Types;

namespace YARG.Story
{
    public static class StoryVenueBridge
    {
        private const string SETTING = "VenueChoice";
        private const string CHOICE_NONE = "None";

        private static bool     _held;
        private static string   _saved;
        private static DateTime _settingsStamp;

        /// <summary>True while the story has the setting borrowed.</summary>
        public static bool IsHeld => _held;

        private static string SettingsFile => Path.Combine(PathHelper.PersistentDataPath, "settings.json");

        // The venue setting of this build, or null when the build has no venues (or settings are not loaded)
        private static AbstractSetting<string> Find()
        {
            var settings = SettingsManager.Settings;
            if (settings == null)
            {
                return null;
            }

            var property = typeof(SettingsManager.SettingContainer).GetProperty(SETTING,
                BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(settings) as AbstractSetting<string>;
        }

        /// <summary>Whether this build can play a gig on a chosen stage at all.</summary>
        public static bool Available
        {
            get
            {
                try
                {
                    return Find() != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Borrows the setting so the song about to start is played at <paramref name="venueId"/>.
        /// The player's own value goes into <paramref name="progress"/> (then <paramref name="save"/>
        /// is called) so it can be put back even after a crash. Does nothing on builds without venues.
        /// </summary>
        public static void Hold(string venueId, StoryProgress progress, Action save)
        {
            if (string.IsNullOrEmpty(venueId))
            {
                return;
            }

            try
            {
                var setting = Find();
                if (setting == null)
                {
                    return;
                }

                if (!_held)
                {
                    string own = setting.Value;
                    if (own == CHOICE_NONE)
                    {
                        // The player turned venues off; respect that
                        return;
                    }

                    _saved = own;
                    _held = true;
                    _settingsStamp = SettingsStamp();

                    if (progress != null)
                    {
                        progress.HeldVenueChoice = own ?? "";
                        save?.Invoke();
                    }
                }

                setting.SetValueWithoutNotify(venueId);
                if (setting.Value != venueId)
                {
                    // The build does not know this venue (the setting turned it into "Random"): give it back
                    YargLogger.LogInfo("Story: this build has no venue \"" + venueId + "\", using the player's own choice");
                    Release(progress, save);
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not pick the gig's venue");
                Release(progress, save);
            }
        }

        /// <summary>Gives the setting back, if it is borrowed. Safe to call at any time.</summary>
        public static void Release(StoryProgress progress, Action save)
        {
            if (!_held)
            {
                return;
            }

            _held = false;
            try
            {
                var setting = Find();
                if (setting != null && _saved != null)
                {
                    setting.SetValueWithoutNotify(_saved);

                    // The game wrote its settings while the story's venue was in them: write them again
                    if (SettingsStamp() != _settingsStamp)
                    {
                        SettingsManager.SaveSettings();
                    }
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not give the venue setting back");
            }

            if (progress != null && progress.HeldVenueChoice != null)
            {
                progress.HeldVenueChoice = null;
                save?.Invoke();
            }
        }

        /// <summary>
        /// At start-up: puts back a venue setting that a gig was borrowing when the app was closed.
        /// Needs the settings to be loaded. Clears the note from every save that has one.
        /// </summary>
        public static void RecoverFromSaves()
        {
            bool settingsChanged = false;
            for (int slot = 1; slot <= StorySaves.SLOTS; slot++)
            {
                var progress = StorySaves.Load(slot);
                if (progress == null || progress.HeldVenueChoice == null)
                {
                    continue;
                }

                try
                {
                    var setting = Find();
                    string own = progress.HeldVenueChoice;
                    if (setting != null && own.Length > 0 && setting.Value != own)
                    {
                        setting.SetValueWithoutNotify(own);
                        settingsChanged = true;
                    }

                    YargLogger.LogInfo("Story: put the player's venue setting back after the game was closed during a gig");
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Story: could not put the venue setting back");
                }

                progress.HeldVenueChoice = null;
                StorySaves.Write(slot, progress);
            }

            if (settingsChanged)
            {
                try
                {
                    SettingsManager.SaveSettings();
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Story: could not save the settings");
                }
            }
        }

        private static DateTime SettingsStamp()
        {
            try
            {
                return File.GetLastWriteTimeUtc(SettingsFile);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
    }
}
