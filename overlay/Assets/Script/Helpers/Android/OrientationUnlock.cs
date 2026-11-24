#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace YARG.Helpers
{
    /// <summary>
    /// YARG Resurrected: lets the game run in portrait or landscape.
    ///
    /// The build allows all four orientations, but YARG-DROID code or a scene could still pin
    /// Screen.orientation to landscape. This turns auto-rotation back on at startup and after
    /// every scene load, so turning the phone (with rotation lock off) switches orientation.
    /// With rotation lock on, Android keeps whatever orientation the phone is in.
    /// </summary>
    public static class OrientationUnlock
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            Apply();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => Apply();
        }

        private static void Apply()
        {
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }
    }
}
#endif
