// YARG Resurrected - two small helps for singers. A new file on its own: nothing else is changed.
//
// 1. No squeal. The game plays a singer's voice back to them ("Vocal Monitoring Volume", 70% by
//    default). Through headphones that is pleasant; through the phone's own speaker the microphone
//    hears it again and the two howl at each other. So the voice is only played back while
//    headphones (wired, USB or Bluetooth) are connected, and silenced otherwise. The setting
//    itself is not touched: plug headphones in and it is back at the volume you chose.
//    A USB microphone counts as "USB audio" too, so the voice is also silenced whenever the
//    game plays out loud on the phone speaker or a TV (see AndroidOutput), whatever is plugged in.
//
// 2. A reason. On a song with no vocals part, a singer's instrument list is simply empty (only
//    Sit Out and Disconnect). When that screen opens for such a song, a message says why and what
//    to do about it.
using System;
using UnityEngine;
using YARG.Core;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Menu.DifficultySelect;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings;

namespace YARG.Audio
{
    public sealed class SingerHelp : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            try
            {
                var host = new GameObject("YARG Singer Help");
                DontDestroyOnLoad(host);
                host.AddComponent<SingerHelp>();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Singer help could not start");
            }
        }

        private float _nextMonitorCheck;
        private float _nextHeadphoneCheck;
        private bool _headphones = true;
        private bool _wasMuted;
        private bool _menuWasOpen;
        private bool _failed;

        private void Update()
        {
            if (_failed)
            {
                return;
            }

            try
            {
                if (SettingsManager.Settings == null)
                {
                    return;
                }

                float now = Time.unscaledTime;
                if (now >= _nextHeadphoneCheck)
                {
                    _nextHeadphoneCheck = now + 1f;
                    _headphones = HeadphonesConnected();
                }

                if (now >= _nextMonitorCheck)
                {
                    _nextMonitorCheck = now + 0.25f;
                    KeepMonitorQuiet();
                }

                bool menuOpen = DifficultySelectMenu.Current != null;
                if (menuOpen && !_menuWasOpen)
                {
                    ExplainMissingVocals();
                }

                _menuWasOpen = menuOpen;
            }
            catch (Exception e)
            {
                // Never worth breaking the game over: stop helping and say so in the log
                _failed = true;
                YargLogger.LogException(e, "Singer help stopped after an error");
            }
        }

        // Every quarter of a second, because a microphone can be added to a profile at any time and
        // starts out at the volume in the settings
        private void KeepMonitorQuiet()
        {
            // The mic test's "Hear myself" is a deliberate choice: leave it alone while it is on
            if (MicTest.HearMyself)
            {
                _wasMuted = true;
                return;
            }

            // Sent every time, not only when headphones come and go: a microphone added since
            // starts out silent until told (setting the same level again costs nothing). Silent
            // while the game plays out loud (phone speaker or TV), where the mic would hear it.
            // Karaoke (Settings > Sound) plays them out loud as well, at the user's own risk of squeal
            bool karaoke = SettingsManager.Settings.Karaoke.Value;
            bool mute = !karaoke && (!_headphones || AndroidOutput.PlaysOutLoud);
            float volume = mute ? 0f : SettingsManager.Settings.VocalMonitoring.Value;
            foreach (var player in PlayerContainer.Players)
            {
                player.Bindings?.Microphone?.SetMonitoringLevel(volume);
            }

            _wasMuted = mute;
        }

        /// <summary>Karaoke was switched (Settings > Sound): say what it does and how to keep it from squealing.</summary>
        public static void OnKaraokeChanged(bool on)
        {
            // (Also called when the settings are loaded at startup: only said when switched on later)
            if (!on || SettingsManager.Settings == null || Time.unscaledTime < 10f)
            {
                return;
            }

            try
            {
                ToastManager.ToastInformation("Karaoke on: the singers are played through the speaker or TV too. Keep the mics " +
                    "away from the speaker, and start with Vocal Monitoring Volume low. If it howls, the game turns the voices down.");
            }
            catch (Exception)
            {
                // Settings loading at startup: nothing to show it on yet
            }
        }

        private static float _lastSqueal = -100f;

        /// <summary>
        /// A microphone's playback howled (the speaker heard by the mic): its player has already
        /// halved it; the setting is turned down too, so it stays down, and the singers are told.
        /// </summary>
        public static void OnSquealed()
        {
            var settings = SettingsManager.Settings;
            if (settings == null)
            {
                return;
            }

            // Several mics can report the same howl: one step down for it
            if (Time.unscaledTime - _lastSqueal < 1.5f)
            {
                return;
            }
            _lastSqueal = Time.unscaledTime;

            float before = settings.VocalMonitoring.Value;
            float after = Mathf.Max(0f, before * 0.7f - 0.02f);
            settings.VocalMonitoring.Value = after;
            YargLogger.LogFormatInfo("Vocal monitoring howled: turned down from {0} to {1}", before, after);
            ToastManager.ToastWarning("The microphones were howling (they heard the speaker), so Vocal Monitoring Volume is " +
                "turned down to " + Mathf.RoundToInt(after * 100f) + "%. Move the mics away from the speaker, or turn the music " +
                "down, then raise it again in Settings > Sound.");
        }

        private static bool HeadphonesConnected()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var audio = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                // AudioManager.GET_DEVICES_OUTPUTS
                var devices = audio.Call<AndroidJavaObject[]>("getDevices", 2);
                bool found = false;
                foreach (var device in devices)
                {
                    int type = device.Call<int>("getType");
                    // Wired headset, wired headphones, Bluetooth (call and music), USB audio and
                    // headset, hearing aid, Bluetooth LE headset: anything that is not the phone's
                    // own speaker
                    if (type == 3 || type == 4 || type == 7 || type == 8 || type == 11 || type == 22 || type == 23 ||
                        type == 26)
                    {
                        found = true;
                    }

                    device.Dispose();
                }

                return found;
            }
            catch (Exception)
            {
                // Cannot tell: silence is the safe guess on a phone
                return false;
            }
#else
            // On a computer the game's own behaviour is left as it is
            return true;
#endif
        }

        private static void ExplainMissingVocals()
        {
            bool singer = false;
            foreach (var player in PlayerContainer.Players)
            {
                if (!player.Profile.IsBot && player.Profile.GameMode == GameMode.Vocals)
                {
                    singer = true;
                }
            }

            if (!singer)
            {
                return;
            }

            var state = GlobalVariables.State;
            SongEntry missing = null;
            if (state.PlayingAShow && state.ShowSongs != null)
            {
                foreach (var song in state.ShowSongs)
                {
                    if (song != null && !song.HasInstrument(Instrument.Vocals))
                    {
                        missing = song;
                        break;
                    }
                }
            }
            else if (state.CurrentSong != null && !state.CurrentSong.HasInstrument(Instrument.Vocals))
            {
                missing = state.CurrentSong;
            }

            if (missing == null)
            {
                return;
            }

            string name = YARG.Core.Utility.RichTextUtils.StripRichTextTags(missing.Name);
            ToastManager.ToastWarning("\"" + name + "\" has no vocals part, so it cannot be sung. " +
                "A converted song gets one when it is converted with lyrics: convert it again with lyrics, " +
                "then scan songs.");
        }
    }
}
