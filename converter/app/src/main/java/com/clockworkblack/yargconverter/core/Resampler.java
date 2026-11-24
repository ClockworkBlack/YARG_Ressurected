package com.clockworkblack.yargconverter.core;

/**
 * Streaming stereo sample-rate converter (straight-line interpolation between samples). Good
 * enough for feeding the analysis and the separation model; not meant for mastering.
 */
public final class Resampler {
    private final double step;
    private final boolean same;
    private double pos;
    private float lastL, lastR;
    private boolean primed;

    public Resampler(int fromRate, int toRate) {
        same = fromRate == toRate;
        step = (double) fromRate / toRate;
    }

    /** Largest number of frames {@link #process} can return for {@code n} input frames. */
    public int capacityFor(int n) {
        return same ? n : (int) Math.ceil(n / step) + 2;
    }

    /** @return number of output frames written */
    public int process(float[] inL, float[] inR, int n, float[] outL, float[] outR) {
        if (n <= 0) return 0;
        if (same) {
            System.arraycopy(inL, 0, outL, 0, n);
            System.arraycopy(inR, 0, outR, 0, n);
            return n;
        }
        if (!primed) {
            lastL = inL[0];
            lastR = inR[0];
            primed = true;
        }
        int count = 0;
        // Position -1 is the last sample of the previous block.
        while (true) {
            int idx = (int) Math.floor(pos);
            if (idx + 1 >= n) break;
            float frac = (float) (pos - idx);
            float aL = idx < 0 ? lastL : inL[idx];
            float aR = idx < 0 ? lastR : inR[idx];
            outL[count] = aL + (inL[idx + 1] - aL) * frac;
            outR[count] = aR + (inR[idx + 1] - aR) * frac;
            count++;
            pos += step;
        }
        pos -= n;
        lastL = inL[n - 1];
        lastR = inR[n - 1];
        return count;
    }
}
