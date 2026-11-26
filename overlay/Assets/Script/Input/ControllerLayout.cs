using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using YARG.Core.Input;
using YARG.Settings;

namespace YARG.Input
{
    /// <summary>
    /// Which controller the menus are set up for. The PlayStation (PS5 DualSense) layout is the base:
    /// Cross confirms, Circle goes back, Options pauses. The others move confirm / back to where those
    /// controllers usually have them, and change the button names shown in the help bar.
    /// </summary>
    public enum ControllerLayoutType
    {
        PlayStation,      // Cross = confirm, Circle = back (PS5 / PS4, western)
        Xbox,             // A = confirm, B = back (same positions as PlayStation)
        Nintendo,         // A (right) = confirm, B (bottom) = back
        PlayStationJapan, // Circle (right) = confirm, Cross (bottom) = back
    }

    /// <summary>Where the five frets are when guitar or bass is played on an ordinary gamepad.</summary>
    public enum GamepadGuitarLayout
    {
        // Frets on the same buttons as the menu colors: Cross green, Circle red, Triangle yellow,
        // Square blue, L1 orange.
        FaceButtons,

        // The default. Guitar Hero 2 on a DualShock: L2 green, L1 red, R1 yellow, R2 blue, Cross orange.
        ShoulderButtons,
    }

    /// <summary>The physical buttons, by position (Unity names them the same way on every pad).</summary>
    public enum PadButton
    {
        South,
        East,
        North,
        West,
        LeftShoulder,
        RightShoulder,
        Start,
        Select,
    }

    public static class ControllerLayout
    {
        public static ControllerLayoutType Current =>
            SettingsManager.Settings?.ControllerLayout.Value ?? ControllerLayoutType.PlayStation;

        /// <summary>How an ordinary gamepad is set up for guitar: Settings > General > Gamepad Guitar Layout.</summary>
        public static GamepadBindingMode GuitarBindingMode =>
            SettingsManager.Settings != null &&
            SettingsManager.Settings.GamepadGuitarLayout.Value == GamepadGuitarLayout.FaceButtons
                ? GamepadBindingMode.Gamepad_FaceButtons
                : GamepadBindingMode.Gamepad;

        private static GamepadGuitarLayout? _appliedGuitarLayout;

        /// <summary>
        /// Called when the Gamepad Guitar Layout setting changes: gives PlayStation / Xbox pads that are
        /// already in a profile the new guitar buttons. (Not at startup, so buttons changed by hand in a
        /// profile's binding menu are only replaced when the layout is actually switched.)
        /// </summary>
        public static void OnGuitarLayoutChanged(GamepadGuitarLayout layout)
        {
            bool first = _appliedGuitarLayout == null;
            bool changed = _appliedGuitarLayout != layout;
            _appliedGuitarLayout = layout;
            if (first || !changed)
            {
                return;
            }

            RebindGuitarLayout();
        }

        /// <summary>Gives the PlayStation / Xbox pads of the connected profiles the current guitar layout.</summary>
        public static void RebindGuitarLayout()
        {
            var mode = GuitarBindingMode;
            foreach (var player in YARG.Player.PlayerContainer.Players)
            {
                if (player.Bindings == null) continue;

                foreach (var device in player.Bindings.InputDevices)
                {
                    if (device is Gamepad gamepad && IsStandardGamepad(gamepad))
                    {
                        var guitar = player.Bindings[YARG.Core.GameMode.FiveFretGuitar];
                        guitar.ClearBindingsForDevice(gamepad);
                        guitar.SetDefaultBindings(gamepad, mode);
                    }
                }
            }
        }

        /// <summary>
        /// Build 25 set gamepads up with the face-button layout by default. The default is the Guitar
        /// Hero 2 shoulder layout again, and the setting is moved back to it once when the game starts
        /// (see SettingsManager.LoadSettings); this re-does the buttons of the gamepads that are
        /// connected, the first time a song is chosen after that.
        /// </summary>
        public static void ApplyPendingGuitarLayout()
        {
            var settings = SettingsManager.Settings;
            if (settings == null || !settings.GuitarLayoutRebindPending)
            {
                return;
            }

            RebindGuitarLayout();
            settings.GuitarLayoutRebindPending = false;
            SettingsManager.SaveSettings();
        }

        /// <summary>The menu action a face / shoulder / center button does in the current layout.</summary>
        public static MenuAction ToMenuAction(PadButton button) => ToMenuAction(button, Current);

        /// <summary>
        /// The menu action for a button of this particular device. A phone joined through the remote
        /// has colored buttons, not a controller's: its OK (green) is sent as the bottom button and its
        /// Back (red) as the right one, so it keeps those colors whatever the layout. (With the Nintendo
        /// or Japanese PlayStation layout, a phone not yet in a profile had OK and Back swapped.)
        /// </summary>
        public static MenuAction ToMenuActionFor(InputDevice device, PadButton button) =>
            ToMenuAction(button, YARG.Remote.RemotePlayManager.IsRemoteDevice(device) ? ControllerLayoutType.Xbox : Current);

        public static MenuAction ToMenuAction(PadButton button, ControllerLayoutType layout)
        {
            bool confirmOnRight = layout is ControllerLayoutType.Nintendo or ControllerLayoutType.PlayStationJapan;

            return button switch
            {
                PadButton.South => confirmOnRight ? MenuAction.Red : MenuAction.Green,
                PadButton.East  => confirmOnRight ? MenuAction.Green : MenuAction.Red,
                // Nintendo has X on top and Y on the left: keep the letters' colors (Y yellow, X blue)
                PadButton.North => layout == ControllerLayoutType.Nintendo ? MenuAction.Blue : MenuAction.Yellow,
                PadButton.West  => layout == ControllerLayoutType.Nintendo ? MenuAction.Yellow : MenuAction.Blue,
                PadButton.LeftShoulder  => MenuAction.Orange,
                PadButton.RightShoulder => MenuAction.Orange,
                PadButton.Start  => MenuAction.Start,
                PadButton.Select => MenuAction.Select,
                _ => MenuAction.Green,
            };
        }

        public static ButtonControl GetControl(Gamepad gamepad, PadButton button) => button switch
        {
            PadButton.South         => gamepad.buttonSouth,
            PadButton.East          => gamepad.buttonEast,
            PadButton.North         => gamepad.buttonNorth,
            PadButton.West          => gamepad.buttonWest,
            PadButton.LeftShoulder  => gamepad.leftShoulder,
            PadButton.RightShoulder => gamepad.rightShoulder,
            PadButton.Start         => gamepad.startButton,
            PadButton.Select        => gamepad.selectButton,
            _                       => null,
        };

        /// <summary>What the button is called on the current controller ("Cross", "A", "Options"...).</summary>
        public static string ButtonName(PadButton button)
        {
            switch (Current)
            {
                case ControllerLayoutType.Xbox:
                    return button switch
                    {
                        PadButton.South => "A",
                        PadButton.East => "B",
                        PadButton.North => "Y",
                        PadButton.West => "X",
                        PadButton.LeftShoulder or PadButton.RightShoulder => "LB/RB",
                        PadButton.Start => "Menu",
                        PadButton.Select => "View",
                        _ => null,
                    };
                case ControllerLayoutType.Nintendo:
                    return button switch
                    {
                        PadButton.South => "B",
                        PadButton.East => "A",
                        PadButton.North => "X",
                        PadButton.West => "Y",
                        PadButton.LeftShoulder or PadButton.RightShoulder => "L/R",
                        PadButton.Start => "+",
                        PadButton.Select => "−",
                        _ => null,
                    };
                default:
                    return button switch
                    {
                        PadButton.South => "Cross",
                        PadButton.East => "Circle",
                        PadButton.North => "Triangle",
                        PadButton.West => "Square",
                        PadButton.LeftShoulder or PadButton.RightShoulder => "L1/R1",
                        PadButton.Start => "Options",
                        PadButton.Select => "Create",
                        _ => null,
                    };
            }
        }

        /// <summary>
        /// The controller button for a menu action in the current layout, or null for actions
        /// that aren't on a face / shoulder / center button (directions use the D-pad or stick).
        /// </summary>
        public static string ButtonNameFor(MenuAction action)
        {
            foreach (PadButton button in System.Enum.GetValues(typeof(PadButton)))
            {
                if (button == PadButton.RightShoulder) continue; // "L1/R1" already covers it
                if (ToMenuAction(button) == action)
                {
                    return ButtonName(button);
                }
            }
            return null;
        }

        /// <summary>
        /// Shows the controller's own buttons (symbols and names) next to menu actions while a real
        /// gamepad is connected. Phones joined through the remote don't count: their buttons are the colors.
        /// </summary>
        public static bool ShowButtonNames
        {
            get
            {
                if (SettingsManager.Settings == null || !SettingsManager.Settings.ShowControllerButtonNames.Value)
                {
                    return false;
                }

                foreach (var gamepad in Gamepad.all)
                {
                    if (!YARG.Remote.RemotePlayManager.IsRemoteDevice(gamepad))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// True for controllers that are certainly ordinary gamepads (PlayStation and Xbox pads), going by
        /// what the device calls itself. On Android guitars and drum kits also show up as "gamepads", so
        /// anything else is asked about when it's added to a profile.
        /// </summary>
        public static bool IsStandardGamepad(Gamepad gamepad)
        {
            if (gamepad == null || YARG.Remote.RemotePlayManager.IsRemoteDevice(gamepad))
            {
                return false;
            }

            if (gamepad is UnityEngine.InputSystem.DualShock.DualShockGamepad)
            {
                return true;
            }

            foreach (string name in new[] { gamepad.description.product, gamepad.displayName, gamepad.name })
            {
                if (string.IsNullOrEmpty(name)) continue;

                string lower = name.ToLowerInvariant();
                if (lower.Contains("guitar") || lower.Contains("drum"))
                {
                    return false;
                }

                // "DualSense Wireless Controller", "Wireless Controller" (PS4), "Xbox Wireless Controller"
                if (lower.Contains("dualsense") || lower.Contains("dualshock") ||
                    lower.Contains("wireless controller"))
                {
                    return true;
                }
            }

            return false;
        }

        // Gamepads the player said are ordinary gamepads when adding them (for pads IsStandardGamepad
        // doesn't recognise by name). Only remembered until the game is closed.
        private static readonly System.Collections.Generic.HashSet<int> _plainGamepads = new();

        public static void MarkAsPlainGamepad(Gamepad gamepad)
        {
            if (gamepad != null)
            {
                _plainGamepads.Add(gamepad.deviceId);
            }
        }

        private static bool PlaysOnPlainGamepad(YARG.Player.YargPlayer player)
        {
            if (player?.Bindings == null)
            {
                return false;
            }

            foreach (var device in player.Bindings.InputDevices)
            {
                if (device is Gamepad gamepad &&
                    (IsStandardGamepad(gamepad) || _plainGamepads.Contains(gamepad.deviceId)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// "No Strumming On Gamepads": makes every note of a gamepad guitar player a tap note, so pressing
        /// the fret button plays it, and uses the Casual engine preset, which doesn't punish a press that
        /// comes a little early. Both are ordinary per-profile options (the All Taps modifier and the
        /// engine preset), so they show in the menus and can be changed there; each profile is only
        /// switched over once.
        /// </summary>
        public static void EnsureNoStrum(YARG.Player.YargPlayer player)
        {
            var settings = SettingsManager.Settings;
            if (settings == null || !settings.GamepadNoStrum.Value || player == null)
            {
                return;
            }

            var profile = player.Profile;
            if (profile == null || profile.IsBot || player.IsReplay ||
                profile.GameMode != YARG.Core.GameMode.FiveFretGuitar || !PlaysOnPlainGamepad(player))
            {
                return;
            }

            string id = profile.Id.ToString();
            if (settings.NoStrumProfiles.Contains(id))
            {
                return;
            }

            profile.AddSingleModifier(YARG.Core.Game.Modifier.AllTaps);
            if (profile.EnginePreset == YARG.Core.Game.EnginePreset.Default.Id)
            {
                profile.EnginePreset = YARG.Core.Game.EnginePreset.Casual.Id;
                player.RefreshPresets();
            }

            settings.NoStrumProfiles.Add(id);
            SettingsManager.SaveSettings();
        }

        private static bool? _appliedNoStrum;

        /// <summary>
        /// Called when the No Strumming setting changes. Turning it off puts the profiles it switched
        /// back to normal notes; turning it on takes effect the next time a song is chosen.
        /// </summary>
        public static void OnNoStrumChanged(bool enabled)
        {
            bool first = _appliedNoStrum == null;
            bool changed = _appliedNoStrum != enabled;
            _appliedNoStrum = enabled;
            if (first || !changed || enabled)
            {
                return;
            }

            var settings = SettingsManager.Settings;
            if (settings == null)
            {
                return;
            }

            foreach (var profile in YARG.Player.PlayerContainer.Profiles)
            {
                if (!settings.NoStrumProfiles.Contains(profile.Id.ToString()))
                {
                    continue;
                }

                profile.RemoveModifiers(YARG.Core.Game.Modifier.AllTaps);
                if (profile.EnginePreset == YARG.Core.Game.EnginePreset.Casual.Id)
                {
                    profile.EnginePreset = YARG.Core.Game.EnginePreset.Default.Id;
                    YARG.Player.PlayerContainer.GetPlayerFromProfile(profile)?.RefreshPresets();
                }
            }

            settings.NoStrumProfiles.Clear();
        }

        /// <summary>The face / shoulder button for a menu action in the current layout, if it has one.</summary>
        public static PadButton? ButtonFor(MenuAction action)
        {
            foreach (PadButton button in System.Enum.GetValues(typeof(PadButton)))
            {
                if (button == PadButton.RightShoulder) continue;
                if (ToMenuAction(button) == action)
                {
                    return button;
                }
            }
            return null;
        }
    }
}
