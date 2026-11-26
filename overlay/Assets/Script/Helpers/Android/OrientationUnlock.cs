#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace YARG.Helpers
{
    /// <summary>
    /// YARG Resurrected: the game turns between the two landscape sides with the phone.
    ///
    /// (Portrait was allowed for a while, but YARG's screens are all laid out for a wide screen:
    /// in portrait they were stretched into a tall strip. Both of a Fold's screens are wide in
    /// landscape.) This turns auto-rotation on at startup and after every scene load, so nothing
    /// pins one side; with rotation lock on, Android keeps the side the phone is in.
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
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }
    }
}
#endif
