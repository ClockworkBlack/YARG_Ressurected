namespace YARG.Input
{
    /// <summary>
    /// Tells a strum bar's spring-back from a strum.
    ///
    /// Let go of a strum bar and it springs back; on some guitars it swings past the middle and
    /// closes the other switch for a moment, or the same switch chatters. The game sees a second
    /// strum with no note to play, which is an overstrum: the streak is gone and the guitar track
    /// cuts out, for a strum the player never made. A second strum that comes sooner after the
    /// first than a hand can strum is left out, and so is its release.
    ///
    /// Two windows, both far shorter than fast alternate strumming (sixteenth notes at 200 beats a
    /// minute are 75 ms apart): after any strum is pressed, and after a strum is let go when the
    /// next press is the other direction (the swing past the middle).
    /// </summary>
    public sealed class StrumBounceGate
    {
        private readonly double _pressWindow, _reboundWindow;
        private double _lastPress = double.NegativeInfinity, _lastRelease = double.NegativeInfinity;
        private bool _lastWasUp;
        private bool _droppedUp, _droppedDown;

        /// <summary>How many strums have been left out.</summary>
        public int Filtered { get; private set; }

        /// <param name="pressWindow">Seconds after a strum in which another strum is a bounce.</param>
        /// <param name="reboundWindow">Seconds after letting go in which a strum the other way is a bounce.</param>
        public StrumBounceGate(double pressWindow, double reboundWindow)
        {
            _pressWindow = pressWindow;
            _reboundWindow = reboundWindow;
        }

        /// <summary>True when this press or release of the strum bar should be left out.</summary>
        public bool IsBounce(bool up, bool pressed, double time)
        {
            if (!pressed)
            {
                // The release of a strum that was left out goes with it
                if (up ? _droppedUp : _droppedDown)
                {
                    if (up) _droppedUp = false;
                    else _droppedDown = false;
                    return true;
                }

                if (up == _lastWasUp)
                {
                    _lastRelease = time;
                }
                return false;
            }

            // (Time can jump back in practice mode; only a strum just after the last one is a bounce)
            double sincePress = time - _lastPress;
            double sinceRelease = time - _lastRelease;
            bool chatter = sincePress >= 0 && sincePress < _pressWindow;
            bool rebound = up != _lastWasUp && sinceRelease >= 0 && sinceRelease < _reboundWindow;
            if (chatter || rebound)
            {
                if (up) _droppedUp = true;
                else _droppedDown = true;
                Filtered++;
                return true;
            }

            _lastPress = time;
            _lastWasUp = up;
            return false;
        }
    }
}
