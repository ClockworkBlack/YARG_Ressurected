// YARG Resurrected - automatic timing for one player of a song, remembered for each controller.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine.InputSystem;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Helpers;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings;

namespace YARG.Input
{
    /// <summary>
    /// Runs <see cref="AutoTiming"/> for a player while a song plays, and writes what it finds to
    /// the profile's Input Calibration.
    ///
    /// A profile has one calibration, but a person may play it on a Bluetooth guitar one day and a
    /// gamepad the next. So the value is also kept for each controller the profile has used
    /// (auto-timing.txt, next to the settings): pick the guitar up again and its own value is back.
    /// </summary>
    public sealed class PlayerAutoTiming
    {
        private const string FILE_NAME = "auto-timing.txt";
        private const string LAST = "last";

        private readonly AutoTiming _timing = new();
        private readonly YargProfile _profile;
        private readonly string _device;
        private readonly long _startedAt;

        /// <summary>What it has done, for the end of the song and for tests.</summary>
        public AutoTiming Timing => _timing;
        public string Device => _device;

        private PlayerAutoTiming(YargProfile profile, string device)
        {
            _profile = profile;
            _device = device;
            _startedAt = profile.InputCalibrationMilliseconds;
        }

        /// <summary>
        /// Automatic timing for this player, or null when it doesn't apply (the setting is off, a
        /// bot, a replay, a phone joined over Wi-Fi, which has its own measured delay).
        /// </summary>
        public static PlayerAutoTiming For(YargPlayer player, float songSpeed)
        {
            try
            {
                if (player?.Profile == null || player.Profile.IsBot || player.IsReplay ||
                    !SettingsManager.Settings.AutoTiming.Value || Math.Abs(songSpeed - 1f) > 0.001f)
                {
                    return null;
                }

                string device = "Touch screen";
                if (player.Bindings != null)
                {
                    foreach (var input in player.Bindings.InputDevices)
                    {
                        if (YARG.Remote.RemotePlayManager.IsRemoteDevice(input))
                        {
                            return null;
                        }

                        if (input is Pointer or Sensor)
                        {
                            continue;
                        }

                        string product = input.description.product;
                        device = (string.IsNullOrEmpty(product) ? input.displayName : product) +
                            (string.IsNullOrEmpty(input.description.interfaceName) ? "" : " (" + input.description.interfaceName + ")");
                        break;
                    }
                }

                device = device.Replace('\t', ' ').Replace('\n', ' ').Trim();
                var timing = new PlayerAutoTiming(player.Profile, device);
                timing.TakeUpController();
                return timing;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't start automatic timing");
                return null;
            }
        }

        // A different controller than last time: put the old one's value away and bring this one's out
        private void TakeUpController()
        {
            var store = Load();
            string profile = _profile.Id.ToString();
            store.TryGetValue(profile + "\t" + LAST, out string last);
            if (last == _device)
            {
                return;
            }

            if (!string.IsNullOrEmpty(last))
            {
                store[profile + "\t" + last] = _profile.InputCalibrationMilliseconds.ToString(CultureInfo.InvariantCulture);
            }

            if (store.TryGetValue(profile + "\t" + _device, out string saved) &&
                long.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
            {
                YargLogger.LogFormatInfo("Automatic timing: {0} is on {1} again, its timing is {2} ms", _profile.Name, _device, ms);
                _profile.InputCalibrationMilliseconds = ms;
            }

            store[profile + "\t" + LAST] = _device;
            Save(store);
        }

        /// <summary>The player hit (or meant) a note this many seconds late; negative for early.</summary>
        public void Add(double lateSeconds)
        {
            _timing.Add(lateSeconds * 1000.0);
            int change = _timing.TakeCorrection(_profile.InputCalibrationMilliseconds);
            if (change == 0)
            {
                return;
            }

            _profile.InputCalibrationMilliseconds += change;
            YargLogger.LogFormatInfo("Automatic timing: {0} was {1} ms {2} on {3}; input calibration is now {4} ms",
                _profile.Name, Math.Abs(Math.Round(_timing.LastMiddle)), _timing.LastMiddle > 0 ? "late" : "early", _device,
                _profile.InputCalibrationMilliseconds);

            try
            {
                var store = Load();
                store[_profile.Id + "\t" + _device] = _profile.InputCalibrationMilliseconds.ToString(CultureInfo.InvariantCulture);
                store[_profile.Id + "\t" + LAST] = _device;
                Save(store);

                // (Small changes are made quietly)
                if (Math.Abs(change) >= 15)
                {
                    ToastManager.ToastInformation(_profile.Name + ": timing set for this controller (" +
                        (change > 0 ? "+" : "") + change.ToString(CultureInfo.InvariantCulture) + " ms, now " +
                        _profile.InputCalibrationMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms)");
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't save the automatic timing");
            }
        }

        /// <summary>One line for the log when the song is over.</summary>
        public void Finish()
        {
            YargLogger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "Automatic timing: {0} on {1}: {2} notes measured, {3} corrections, input calibration {4} -> {5} ms",
                _profile.Name, _device, _timing.Measured, _timing.Corrections, _startedAt, _profile.InputCalibrationMilliseconds));
        }

        private static string FilePath => Path.Combine(PathHelper.PersistentDataPath, FILE_NAME);

        // profile id, tab, controller (or "last"), tab, value
        private static Dictionary<string, string> Load()
        {
            var store = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int cut = line.LastIndexOf('\t');
                        if (cut > 0)
                        {
                            store[line.Substring(0, cut)] = line.Substring(cut + 1);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't read the saved controller timings");
            }
            return store;
        }

        private static void Save(Dictionary<string, string> store)
        {
            var lines = new List<string>(store.Count);
            foreach (var pair in store)
            {
                lines.Add(pair.Key + "\t" + pair.Value);
            }
            File.WriteAllLines(FilePath, lines);
        }
    }
}
