using UnityEngine;
using YARG.Core.Logging;

namespace YARG.Input
{
    /// <summary>
    /// Writes to the game's log what happened to controller input during a song, so a log from a
    /// phone shows whether strums are being saved up by Android (see YargActivity.java) and how
    /// many strum bar bounces were left out.
    /// </summary>
    public static class ControllerEventLog
    {
        private static int _lastBatches, _lastRecovered;

        public static void SongEnded()
        {
            BluetoothBoost.SongEnded();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var activity = new AndroidJavaClass("in.yarg.resurrected.YargActivity");
                int batches = activity.CallStatic<int>("batches");
                int recovered = activity.CallStatic<int>("recovered");
                bool unbuffered = activity.CallStatic<bool>("unbuffered");
                YargLogger.LogFormatInfo(
                    "Controller input this song: {0} motion events arrived saved up by Android, with {1} earlier positions that were passed on one by one; events delivered as they happen: {2}",
                    batches - _lastBatches, recovered - _lastRecovered, unbuffered);
                _lastBatches = batches;
                _lastRecovered = recovered;
            }
            catch (System.Exception e)
            {
                YargLogger.LogFormatWarning("Controller input: the activity could not be asked ({0})", e.Message);
            }
#endif
        }

        public static void StrumBounces(string player, int filtered)
        {
            if (filtered > 0)
            {
                YargLogger.LogFormatInfo("Strum bar bounce filter: for {0}, strums left out: {1}", player ?? "a player", filtered);
            }
        }
    }
}
