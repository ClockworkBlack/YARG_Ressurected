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

            // Auto: the buttons play for the first five-fret player. Show them only if that player
            // has nothing else to play with.
            foreach (var player in PlayerContainer.Players)
            {
                if (player == null || player.Profile.IsBot || player.SittingOut || player.IsReplay)
                    continue;
                if (player.Profile.GameMode != GameMode.FiveFretGuitar)
                    continue;

                return !HasController(player);
            }

            // Nobody the buttons could play for
            return false;
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
