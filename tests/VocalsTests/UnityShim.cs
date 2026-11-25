// Just enough of UnityEngine.Mathf for YARG's original pitch tracker to compile in the tests.
using System;

namespace UnityEngine
{
    public static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Round(float a) => (float) Math.Round(a);
        public static float Abs(float a) => Math.Abs(a);
        public static float Sqrt(float a) => (float) Math.Sqrt(a);
        public static float Log10(float a) => (float) Math.Log10(a);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
    }
}
