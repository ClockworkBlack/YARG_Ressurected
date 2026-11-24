using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using YARG.Core.Input;

namespace YARG.Input
{
    /// <summary>
    /// Lets any gamepad drive the menus, even before it has been added to a profile.
    /// YARG normally only listens to devices that belong to a profile (plus the keyboard), so a fresh
    /// controller could do nothing until you had already made a profile with the touch screen.
    /// A gamepad that belongs to a profile is skipped here: its profile's own menu bindings are used.
    /// </summary>
    public sealed class DefaultGamepadMenuBindings : IDisposable
    {
        // Same colors as the guitar layout (South = Green, East = Red, ...), so the help bar's
        // colored hints match the face buttons on a pad and on the phone remote.
        private static readonly (MenuAction action, string path)[] DefaultBindings =
        {
            (MenuAction.Green,  "<Gamepad>/buttonSouth"),     // A / Cross: select
            (MenuAction.Red,    "<Gamepad>/buttonEast"),      // B / Circle: back
            (MenuAction.Yellow, "<Gamepad>/buttonNorth"),     // Y / Triangle
            (MenuAction.Blue,   "<Gamepad>/buttonWest"),      // X / Square
            (MenuAction.Orange, "<Gamepad>/leftShoulder"),    // LB / L1: more options
            (MenuAction.Orange, "<Gamepad>/rightShoulder"),   // RB / R1: more options
            (MenuAction.Start,  "<Gamepad>/start"),
            (MenuAction.Select, "<Gamepad>/select"),
            (MenuAction.Up,     "<Gamepad>/dpad/up"),
            (MenuAction.Down,   "<Gamepad>/dpad/down"),
            (MenuAction.Left,   "<Gamepad>/dpad/left"),
            (MenuAction.Right,  "<Gamepad>/dpad/right"),
            (MenuAction.Up,     "<Gamepad>/leftStick/up"),
            (MenuAction.Down,   "<Gamepad>/leftStick/down"),
            (MenuAction.Left,   "<Gamepad>/leftStick/left"),
            (MenuAction.Right,  "<Gamepad>/leftStick/right"),
        };

        private readonly InputAction[] _inputActions = new InputAction[DefaultBindings.Length];

        // Presses we sent, so the release always follows even if the pad joins a profile in between
        // (otherwise a held direction would keep scrolling forever).
        private readonly HashSet<int> _pressed = new();

        private readonly Func<InputDevice, bool> _isOwnedByProfile;

        public DefaultGamepadMenuBindings(Func<InputDevice, bool> isOwnedByProfile)
        {
            _isOwnedByProfile = isOwnedByProfile;
            SetupBinds();
            Enable();
        }

        private void SetupBinds()
        {
            for (int i = 0; i < DefaultBindings.Length; i++)
            {
                int index = i;
                var (menuAction, path) = DefaultBindings[i];

                var action = new InputAction(
                    name: $"GamepadMenu_{menuAction}_{i}",
                    type: InputActionType.Button,
                    binding: path
                );

                action.performed += ctx =>
                {
                    var device = ctx.control?.device;
                    if (device == null || _isOwnedByProfile(device)) return;
                    if (_pressed.Add(index))
                    {
                        InputManager.OnMenuAction(menuAction, true);
                    }
                };
                action.canceled += _ =>
                {
                    if (_pressed.Remove(index))
                    {
                        InputManager.OnMenuAction(menuAction, false);
                    }
                };
                _inputActions[i] = action;
            }
        }

        public void Dispose()
        {
            foreach (var action in _inputActions)
            {
                action.Disable();
                action.Dispose();
            }
            _pressed.Clear();
        }

        public void Enable()
        {
            foreach (var action in _inputActions)
            {
                action.Enable();
            }
        }
    }
}
