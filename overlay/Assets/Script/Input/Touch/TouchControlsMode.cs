using UnityEngine.InputSystem;
using YARG.Core;
using YARG.Player;
using YARG.Settings;

namespace YARG.Input.Touch
{
    /// <summary>When the on-screen fret and strum buttons are shown during a song.</summary>
    public enum TouchControlsMode
    {
        Auto,   // only when the guitar player has no controller (so the screen is the controller)
        Show,   // always
        Hide,   // never
    }

    public static class TouchControlsVisibility
    {
        /// <summary>Whether the touch buttons should be on screen right now (ignoring pause).</summary>
        public static bool ShouldShow()
        {
            var settings = SettingsManager.Settings;
            var mode = settings != null ? settings.TouchControls.Value : TouchControlsMode.Auto;

            switch (mode)
            {
                case TouchControlsMode.Show:
                    return true;
                case TouchControlsMode.Hide:
                    return false;
            }

            // Auto: only when the screen is the controller, that is when nobody playing has a
            // controller of their own. With several players, one controller (a guitar, a gamepad,
            // a phone remote) among them is enough to keep the buttons off the screen: they used
            // to be judged by the first guitar player alone, and came up over a multiplayer song
            // when that player's profile had no controller in it.
            return !AnyoneHasController() && TouchPlayer() != null;
        }

        /// <summary>
        /// The player the touch buttons play for: the first five-fret player without a controller
        /// (or, when they all have one and the buttons are forced on, the first five-fret player).
        /// </summary>
        public static YargPlayer TouchPlayer()
        {
            YargPlayer first = null;
            foreach (var player in PlayerContainer.Players)
            {
                if (!IsPlaying(player) || player.Profile.GameMode != GameMode.FiveFretGuitar)
                    continue;

                if (!HasController(player))
                    return player;

                first ??= player;
            }

            return first;
        }

        private static bool AnyoneHasController()
        {
            foreach (var player in PlayerContainer.Players)
            {
                if (IsPlaying(player) && HasController(player))
                    return true;
            }

            return false;
        }

        private static bool IsPlaying(YargPlayer player)
        {
            return player != null && !player.Profile.IsBot && !player.SittingOut && !player.IsReplay;
        }

        private static bool HasController(YargPlayer player)
        {
            if (player.Bindings == null)
                return false;

            foreach (var device in player.Bindings.InputDevices)
            {
                // The phone's own touch screen, Android's built-in "Virtual" keyboard and sensors
                // aren't controllers; gamepads, guitars, drum kits and phone remotes are
                if (device is Pointer or Keyboard or Sensor)
                    continue;

                return true;
            }

            return false;
        }
    }
}
