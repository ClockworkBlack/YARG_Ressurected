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

        /// <summary>The menu action a face / shoulder / center button does in the current layout.</summary>
        public static MenuAction ToMenuAction(PadButton button) => ToMenuAction(button, Current);

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

        /// <summary>Shows controller button names in the help bar while a gamepad is connected.</summary>
        public static bool ShowButtonNames =>
            SettingsManager.Settings != null &&
            SettingsManager.Settings.ShowControllerButtonNames.Value &&
            Gamepad.all.Count > 0;
    }
}
