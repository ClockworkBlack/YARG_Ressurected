// YARG Resurrected - the fastest Bluetooth setting for controllers, and how often a controller really reports.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using YARG.Core.Logging;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings;

namespace YARG.Input
{
    /// <summary>
    /// Two things for controllers on Bluetooth.
    ///
    /// On Android, asks the phone for its fast Bluetooth pace for the controllers in use
    /// (YargBluetoothBoost.java). That can only work for a Bluetooth Low Energy controller; for a
    /// classic Bluetooth one the phone has no say, and the status says so.
    ///
    /// Everywhere, measures how often each controller really reports: the usual shortest gap
    /// between two of its reports while something on it is moving (a whammy bar, a tilt sensor, a
    /// stick). That is the pace of the connection as the game sees it, whatever the box says.
    /// </summary>
    public static class BluetoothBoost
    {
        private const string PERMISSION = "android.permission.BLUETOOTH_CONNECT";
        private const string ASKED_KEY = "yarg.bluetooth-boost.asked";

        /// <summary>What was found and done, in words. Empty when there is nothing to say.</summary>
        public static string Status { get; private set; } = string.Empty;

        private static string _toasted;

        /// <summary>True when a player has a controller that is not the keyboard, mouse or touch screen.</summary>
        private static bool AnyController()
        {
            foreach (var player in PlayerContainer.Players)
            {
                if (player.Bindings == null || player.Profile.IsBot)
                {
                    continue;
                }

                foreach (var device in player.Bindings.InputDevices)
                {
                    if (device is Keyboard or Pointer or Sensor || YARG.Remote.RemotePlayManager.IsRemoteDevice(device))
                    {
                        continue;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Called on the screen before a song. Asks for the fast pace; the first time, asks for the
        /// permission it needs (a system question, so not during a song).
        /// </summary>
        /// <param name="mayAsk">Whether the permission question may be shown now.</param>
        public static void Apply(bool mayAsk)
        {
            StartMeasuring();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!SettingsManager.Settings.FasterBluetooth.Value || !AnyController())
                {
                    return;
                }

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(PERMISSION) && AndroidVersion() >= 31)
                {
                    if (!mayAsk || PlayerPrefs.GetInt(ASKED_KEY, 0) != 0)
                    {
                        Status = "Bluetooth controllers: the game is not allowed to see them. Allow \"Nearby devices\" for YARG in the phone's settings to let it ask for the fast connection.";
                        return;
                    }

                    PlayerPrefs.SetInt(ASKED_KEY, 1);
                    PlayerPrefs.Save();
                    ToastManager.ToastInformation("To ask the phone for its fastest Bluetooth setting for your controller, the game needs \"Nearby devices\".");
                    var callbacks = new UnityEngine.Android.PermissionCallbacks();
                    callbacks.PermissionGranted += _ => Apply(false);
                    UnityEngine.Android.Permission.RequestUserPermission(PERMISSION, callbacks);
                    return;
                }

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var boost = new AndroidJavaClass("in.yarg.resurrected.YargBluetoothBoost");
                Status = boost.CallStatic<string>("apply", activity) ?? string.Empty;
                YargLogger.LogInfo(Status);
            }
            catch (Exception e)
            {
                YargLogger.LogFormatWarning("Bluetooth controllers could not be looked at ({0})", e.Message);
            }
#endif
        }

        /// <summary>The status again, a moment after asking (the phone answers in its own time), shown once when it changes.</summary>
        public static void Refresh(bool toast)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var boost = new AndroidJavaClass("in.yarg.resurrected.YargBluetoothBoost");
                string status = boost.CallStatic<string>("status");
                if (!string.IsNullOrEmpty(status) && !status.Contains("not looked at"))
                {
                    Status = status;
                }
            }
            catch (Exception)
            {
            }
#endif
            if (toast && !string.IsNullOrEmpty(Status) && Status != _toasted && Status.Contains(": ") &&
                (Status.Contains("Low Energy") || Status.Contains("classic")))
            {
                _toasted = Status;
                ToastManager.ToastInformation(Status);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static int AndroidVersion()
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
        }
#endif

        // ---------------------------------------------------------------- how often a controller reports

        private sealed class Meter
        {
            public string Name;
            public double Last = double.NaN;
            public int Reports;
            // Gaps of 0 to 63 ms, a count for each
            public readonly int[] Gaps = new int[64];
        }

        private static readonly Dictionary<int, Meter> _meters = new();
        private static readonly Dictionary<string, string> _paces = new();
        private static bool _measuring;

        private static void StartMeasuring()
        {
            if (_measuring)
            {
                return;
            }

            _measuring = true;
            InputSystem.onEvent += OnEvent;
        }

        private static void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device is Keyboard or Pointer or Sensor || !(eventPtr.IsA<StateEvent>() || eventPtr.IsA<DeltaStateEvent>()))
            {
                return;
            }

            if (!_meters.TryGetValue(device.deviceId, out var meter))
            {
                string product = device.description.product;
                meter = new Meter { Name = string.IsNullOrEmpty(product) ? device.displayName : product };
                _meters[device.deviceId] = meter;
            }

            Count(meter, eventPtr.time);
        }

        private static void Count(Meter meter, double time)
        {
            if (!double.IsNaN(meter.Last))
            {
                double gap = (time - meter.Last) * 1000.0;
                if (gap > 0.2 && gap < 63.5)
                {
                    meter.Gaps[(int) Math.Round(gap)]++;
                }
            }
            meter.Last = time;
            meter.Reports++;
        }

        /// <summary>
        /// The pace of a run of reports: the gap that a fifth of the short gaps are no longer than.
        /// 0 when there are too few to say. (For tests.)
        /// </summary>
        public static int PaceOf(IReadOnlyList<double> times)
        {
            var meter = new Meter();
            foreach (double time in times)
            {
                Count(meter, time);
            }
            return Pace(meter);
        }

        private static int Pace(Meter meter)
        {
            int total = 0;
            foreach (int count in meter.Gaps)
            {
                total += count;
            }
            if (total < 40)
            {
                return 0;
            }

            int upTo = 0;
            for (int gap = 0; gap < meter.Gaps.Length; gap++)
            {
                upTo += meter.Gaps[gap];
                if (upTo * 5 >= total)
                {
                    return Math.Max(1, gap);
                }
            }
            return 0;
        }

        /// <summary>Called when a song is over: writes each controller's pace to the log and keeps it for the menus.</summary>
        public static void SongEnded()
        {
            foreach (var meter in _meters.Values)
            {
                int pace = Pace(meter);
                if (pace > 0)
                {
                    string text = string.Format(CultureInfo.InvariantCulture, "reports about every {0} ms ({1} a second)", pace, 1000 / pace);
                    _paces[meter.Name] = text;
                    YargLogger.LogInfo("Controller " + meter.Name + ": " + text + ", from " + meter.Reports.ToString(CultureInfo.InvariantCulture) + " reports");
                }

                Array.Clear(meter.Gaps, 0, meter.Gaps.Length);
                meter.Reports = 0;
                meter.Last = double.NaN;
            }
            Refresh(false);
        }

        /// <summary>What is known about a player's controllers, for the profile's list. Empty when nothing is.</summary>
        public static string Describe(YargPlayer player)
        {
            var parts = new List<string>();
            if (player?.Bindings != null)
            {
                foreach (var device in player.Bindings.InputDevices)
                {
                    string product = device.description.product;
                    string name = string.IsNullOrEmpty(product) ? device.displayName : product;
                    if (_paces.TryGetValue(name, out string pace))
                    {
                        parts.Add(name + " " + pace);
                    }
                }
            }

            Refresh(false);
            if (!string.IsNullOrEmpty(Status))
            {
                parts.Add(Status);
            }
            return string.Join(". ", parts);
        }
    }
}
