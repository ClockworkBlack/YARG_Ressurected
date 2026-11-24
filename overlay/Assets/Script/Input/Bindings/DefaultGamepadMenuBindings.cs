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
    /// Which button confirms / goes back follows Settings > General > Controller Layout
    /// (PlayStation by default: Cross confirms, Circle goes back), read at each press.
    /// </summary>
    public sealed class DefaultGamepadMenuBindings : IDisposable
    {
        private readonly struct Bind
        {
            public readonly string Path;
            public readonly PadButton? Button;   // face / shoulder / center button: action depends on layout
            public readonly MenuAction Direction; // otherwise a fixed direction

            public Bind(string path, PadButton button)
            {
                Path = path;
                Button = button;
                Direction = default;
            }

            public Bind(string path, MenuAction direction)
            {
                Path = path;
                Button = null;
                Direction = direction;
            }
        }

        private static readonly Bind[] DefaultBindings =
        {
            new("<Gamepad>/buttonSouth",   PadButton.South),          // Cross / A
            new("<Gamepad>/buttonEast",    PadButton.East),           // Circle / B
            new("<Gamepad>/buttonNorth",   PadButton.North),          // Triangle / Y
            new("<Gamepad>/buttonWest",    PadButton.West),           // Square / X
            new("<Gamepad>/leftShoulder",  PadButton.LeftShoulder),   // L1 / LB
            new("<Gamepad>/rightShoulder", PadButton.RightShoulder),  // R1 / RB
            new("<Gamepad>/start",         PadButton.Start),          // Options / Menu
            new("<Gamepad>/select",        PadButton.Select),         // Create / View
            new("<Gamepad>/dpad/up",        MenuAction.Up),
            new("<Gamepad>/dpad/down",      MenuAction.Down),
            new("<Gamepad>/dpad/left",      MenuAction.Left),
            new("<Gamepad>/dpad/right",     MenuAction.Right),
            new("<Gamepad>/leftStick/up",   MenuAction.Up),
            new("<Gamepad>/leftStick/down", MenuAction.Down),
            new("<Gamepad>/leftStick/left", MenuAction.Left),
            new("<Gamepad>/leftStick/right", MenuAction.Right),
        };

        private readonly InputAction[] _inputActions = new InputAction[DefaultBindings.Length];

        // Presses we sent and the action each one was sent as, so the release always follows with the
        // same action, even if the pad joins a profile or the layout changes while it's held
        // (otherwise a held direction would keep scrolling forever).
        private readonly Dictionary<int, MenuAction> _pressed = new();

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
                var bind = DefaultBindings[i];

                var action = new InputAction(
                    name: $"GamepadMenu_{i}",
                    type: InputActionType.Button,
                    binding: bind.Path
                );

                action.performed += ctx =>
                {
                    var device = ctx.control?.device;
                    if (device == null || _isOwnedByProfile(device) || _pressed.ContainsKey(index)) return;

                    var menuAction = bind.Button is { } button
                        ? ControllerLayout.ToMenuAction(button)
                        : bind.Direction;
                    _pressed[index] = menuAction;
                    InputManager.OnMenuAction(menuAction, true);
                };
                action.canceled += _ =>
                {
                    if (_pressed.Remove(index, out var menuAction))
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
