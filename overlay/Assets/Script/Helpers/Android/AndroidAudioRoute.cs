// YARG Resurrected - where the game's sound is coming out (Android).
using System;
using UnityEngine;

namespace YARG.Helpers
{
    public static class AndroidAudioRoute
    {
        /// <summary>
        /// True when the only place sound can come out is the device's own loudspeaker (no
        /// headphones, no Bluetooth, no USB or HDMI audio). Call from the main thread.
        /// </summary>
        /// <remarks>
        /// Used to keep the microphone from being played back through a loudspeaker that sits
        /// right next to it. False when it cannot be told, and on anything but Android.
        /// </remarks>
        public static bool IsBuiltInSpeakerOnly()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
                {
                    if (audioManager == null)
                    {
                        return false;
                    }

                    // AudioManager.GET_DEVICES_OUTPUTS = 2 (Android 6 and later)
                    var devices = audioManager.Call<AndroidJavaObject[]>("getDevices", 2);
                    if (devices == null || devices.Length == 0)
                    {
                        return false;
                    }

                    bool external = false;
                    foreach (var device in devices)
                    {
                        if (device == null)
                        {
                            continue;
                        }

                        using (device)
                        {
                            if (!IsBuiltIn(device.Call<int>("getType")))
                            {
                                external = true;
                            }
                        }
                    }

                    return !external;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG] Could not read the audio output devices: " + e.Message);
                return false;
            }
#else
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // android.media.AudioDeviceInfo types that are part of the phone itself
        private static bool IsBuiltIn(int type)
        {
            switch (type)
            {
                case 1:  // TYPE_BUILTIN_EARPIECE
                case 2:  // TYPE_BUILTIN_SPEAKER
                case 18: // TYPE_TELEPHONY
                case 24: // TYPE_BUILTIN_SPEAKER_SAFE
                case 25: // TYPE_REMOTE_SUBMIX (screen recording, not a speaker)
                    return true;
                default:
                    return false;
            }
        }
#endif
    }
}
