using System;
using YARG.Input;

namespace RemoteServerTests
{
    /// <summary>The strum bar bounce filter (Settings > General > Strum Bar Bounce Filter).</summary>
    public static class StrumBounceTests
    {
        public static void Run(Action<bool, string> check)
        {
            const bool UP = true, DOWN = false, PRESS = true, RELEASE = false;

            // ---- Ordinary playing goes through untouched
            {
                var gate = new StrumBounceGate(0.020, 0.010);
                int left = 0;
                // Down strums, eighth notes at 120 beats a minute, each held for 60 ms
                for (int i = 0; i < 16; i++)
                {
                    double t = i * 0.25;
                    if (gate.IsBounce(DOWN, PRESS, t)) left++;
                    if (gate.IsBounce(DOWN, RELEASE, t + 0.06)) left++;
                }
                check(left == 0 && gate.Filtered == 0, "plain down strums all go through");

                // Fast alternate strumming: sixteenth notes at 200 beats a minute (75 ms apart),
                // the bar in the middle for 15 ms between strokes
                gate = new StrumBounceGate(0.020, 0.010);
                left = 0;
                for (int i = 0; i < 64; i++)
                {
                    double t = 10 + i * 0.075;
                    bool up = i % 2 == 1;
                    if (gate.IsBounce(up, PRESS, t)) left++;
                    if (gate.IsBounce(up, RELEASE, t + 0.060)) left++;
                }
                check(left == 0, $"fast alternate strumming all goes through, light filter ({left} left out of 128)");

                // ...and faster than anyone plays: 20 strokes a second, even with the strong filter
                gate = new StrumBounceGate(0.035, 0.020);
                left = 0;
                for (int i = 0; i < 64; i++)
                {
                    double t = 20 + i * 0.050;
                    bool up = i % 2 == 1;
                    if (gate.IsBounce(up, PRESS, t)) left++;
                    if (gate.IsBounce(up, RELEASE, t + 0.028)) left++;
                }
                check(left == 0, $"20 strokes a second all go through, strong filter ({left} left out of 128)");
            }

            // ---- The spring-back: let go of a down strum, the bar swings up for a moment
            {
                var gate = new StrumBounceGate(0.020, 0.010);
                bool a = gate.IsBounce(DOWN, PRESS, 1.000);
                bool b = gate.IsBounce(DOWN, RELEASE, 1.050);
                bool c = gate.IsBounce(UP, PRESS, 1.054);     // 4 ms after letting go: the swing
                bool d = gate.IsBounce(UP, RELEASE, 1.060);   // and its release goes with it
                bool e = gate.IsBounce(DOWN, PRESS, 1.250);   // the next real strum
                check(!a && !b && c && d && !e && gate.Filtered == 1,
                    "a swing past the middle after letting go is left out, with its release; the next strum counts");
            }

            // ---- Chatter: the same switch closing twice
            {
                var gate = new StrumBounceGate(0.020, 0.010);
                bool a = gate.IsBounce(DOWN, PRESS, 2.000);
                bool b = gate.IsBounce(DOWN, RELEASE, 2.004);
                bool c = gate.IsBounce(DOWN, PRESS, 2.008);   // 8 ms after the first press
                bool d = gate.IsBounce(DOWN, RELEASE, 2.050);
                check(!a && !b && c && d && gate.Filtered == 1, "a switch that closes twice within 20 ms is one strum");
            }

            // ---- A real up strum well after letting go of a down strum is a strum
            {
                var gate = new StrumBounceGate(0.020, 0.010);
                gate.IsBounce(DOWN, PRESS, 3.000);
                gate.IsBounce(DOWN, RELEASE, 3.040);
                check(!gate.IsBounce(UP, PRESS, 3.055), "an up strum 15 ms after letting go of a down strum counts");
            }

            // ---- Practice mode: the song jumps back, and the first strum after it counts
            {
                var gate = new StrumBounceGate(0.020, 0.010);
                gate.IsBounce(DOWN, PRESS, 50.000);
                gate.IsBounce(DOWN, RELEASE, 50.050);
                check(!gate.IsBounce(DOWN, PRESS, 12.000) && !gate.IsBounce(UP, PRESS, 12.200),
                    "strums after the song jumps back are not taken for bounces");
            }
        }
    }
}
