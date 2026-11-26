// YARG Resurrected - what a microphone tells a vocals player about its timing.
//
// No Unity types in here: the automated tests run this file as it is.
namespace YARG.Audio.Vocals
{
    /// <summary>
    /// A microphone that knows how late its results are. A vocals player asks once, when its
    /// song starts, and runs that much behind the song, so that every sung sound is judged
    /// against the note that was playing when it was sung.
    /// </summary>
    public interface IVoiceMic
    {
        /// <summary>
        /// Gets the microphone ready for a song.
        /// </summary>
        /// <param name="strict">
        /// Vocals Challenge Mode: only a clear tone counts as a pitch.
        /// </param>
        /// <returns>
        /// The seconds between a sound being made and the game being handed the input for it.
        /// It stays the same for the whole song.
        /// </returns>
        double BeginSong(bool strict);
    }

    public static class VoiceMicTiming
    {
        /// <summary>
        /// The time a sound takes from this device's microphone to the game's audio input
        /// (Android's recording path), in seconds. A typical value: it cannot be measured from
        /// inside the game, and the "Microphone Delay" setting is added to it.
        /// </summary>
        public const double DEVICE_CAPTURE_SECONDS = 0.035;

        /// <summary>
        /// The same for another phone's browser, up to the moment the audio is sent.
        /// </summary>
        public const double BROWSER_CAPTURE_SECONDS = 0.045;

        /// <summary>
        /// Slack for the audio thread being a little late (seconds).
        /// </summary>
        public const double DELIVERY_MARGIN_SECONDS = 0.010;

        /// <summary>
        /// How many samples went missing between two packets of a phone's microphone audio.
        /// Each packet carries the phone's running count of its first sample (32 bits, so it
        /// wraps around after about a day).
        /// </summary>
        /// <param name="expected">The count the next packet should start at; below 0 if not known yet.</param>
        /// <param name="first">The count the packet does start at; below 0 if the phone sent none.</param>
        /// <returns>
        /// The number of samples lost (0 if none), or -1 if the count went backwards, which
        /// means the phone started its microphone again.
        /// </returns>
        public static long MissingSamples(long expected, long first)
        {
            if (expected < 0 || first < 0)
            {
                return 0;
            }

            long missing = (first - expected) & 0xFFFFFFFFL;
            return missing > int.MaxValue ? -1 : missing;
        }

        /// <summary>
        /// The hold for a microphone on this device: the analysis itself, one delivery of
        /// audio (<paramref name="recordPeriodMs"/>), and a margin.
        /// </summary>
        public static double DeviceHoldSeconds(double analysisSeconds, int recordPeriodMs)
        {
            if (recordPeriodMs < 5)
            {
                recordPeriodMs = 5;
            }
            else if (recordPeriodMs > 60)
            {
                recordPeriodMs = 60;
            }

            return analysisSeconds + recordPeriodMs / 1000.0 + DELIVERY_MARGIN_SECONDS;
        }

        /// <summary>
        /// The hold for a phone joined over Wi-Fi: the analysis, the network's usual delay as
        /// measured for that phone (<paramref name="networkHoldMs"/>), one 10 ms packet, and a margin.
        /// </summary>
        public static double RemoteHoldSeconds(double analysisSeconds, double networkHoldMs)
        {
            if (networkHoldMs < 0)
            {
                networkHoldMs = 0;
            }
            else if (networkHoldMs > 150)
            {
                networkHoldMs = 150;
            }

            return analysisSeconds + networkHoldMs / 1000.0 + 0.010 + DELIVERY_MARGIN_SECONDS;
        }
    }
}
