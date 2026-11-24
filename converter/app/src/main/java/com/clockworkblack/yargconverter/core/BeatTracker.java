package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.List;

/** Finds the tempo and the beat positions of a song (dynamic-programming beat tracker). */
public final class BeatTracker {
    private BeatTracker() {}

    public static final class Result {
        /** Beat times in seconds; the first is always 0 and the last is at or past the end of the song. */
        public final double[] beats;
        /** Index (0-3) of the first beat that starts a measure. */
        public final int firstDownbeat;
        /** Overall tempo in beats per minute. */
        public final double bpm;

        Result(double[] beats, int firstDownbeat, double bpm) {
            this.beats = beats;
            this.firstDownbeat = firstDownbeat;
            this.bpm = bpm;
        }
    }

    public static Result track(Features f) {
        int n = f.frames();
        double fps = f.fps;
        if (n < fps * 2) {
            return fallback(f.duration);
        }
        // Bass counts extra: kick drum and bass guitar are the most reliable guide to the beat.
        float[] mixed = new float[n];
        for (int i = 0; i < n; i++) mixed[i] = f.flux[i] + 1.5f * f.lowFlux[i];
        float[] env = normalize(mixed, (int) Math.round(fps * 0.75));

        double period = estimatePeriod(env, fps);
        if (period <= 0) return fallback(f.duration);

        // Dynamic programming: best chain of beats that sit on strong onsets and stay near the period.
        double tightness = 100.0;
        double[] cum = new double[n];
        int[] back = new int[n];
        int lo = (int) Math.round(period * 2);
        int hi = (int) Math.max(1, Math.round(period / 2));
        for (int t = 0; t < n; t++) {
            double best = 0;
            int bestPrev = -1;
            for (int p = Math.max(0, t - lo); p <= t - hi; p++) {
                double l = Math.log((t - p) / period);
                double s = cum[p] - tightness * l * l;
                if (s > best) {
                    best = s;
                    bestPrev = p;
                }
            }
            cum[t] = env[t] + best;
            back[t] = bestPrev;
        }
        int last = n - 1;
        for (int t = Math.max(0, n - (int) Math.round(period)); t < n; t++) {
            if (cum[t] > cum[last]) last = t;
        }
        List<Integer> chain = new ArrayList<>();
        for (int t = last; t >= 0; t = back[t]) chain.add(t);
        int count = chain.size();
        if (count < 4) return fallback(f.duration);

        double[] raw = new double[count];
        for (int i = 0; i < count; i++) raw[i] = f.timeOf(refine(env, chain.get(count - 1 - i)));

        // Which of every four beats is the "one"? The kick/bass usually lands hardest there.
        double[] phase = new double[4];
        for (int i = 0; i < count; i++) {
            int fr = chain.get(count - 1 - i);
            float m = 0;
            for (int k = Math.max(0, fr - 2); k <= Math.min(n - 1, fr + 2); k++) {
                m = Math.max(m, f.lowFlux[k] + 0.25f * f.flux[k]);
            }
            phase[i % 4] += m;
        }
        int down = 0;
        for (int i = 1; i < 4; i++) if (phase[i] > phase[down]) down = i;

        // Extend the grid back to the start and forward to the end of the song.
        double secPerBeat = period / fps;
        List<Double> beats = new ArrayList<>();
        double t0 = raw[0];
        int before = 0;
        while (t0 - secPerBeat * (before + 1) >= 0) before++;
        for (int i = before; i >= 1; i--) beats.add(t0 - secPerBeat * i);
        for (double b : raw) beats.add(b);
        while (beats.get(beats.size() - 1) < f.duration) beats.add(beats.get(beats.size() - 1) + secPerBeat);
        down = ((down + before) % 4 + 4) % 4;

        // Chart time starts at 0, so a beat has to sit there. If the music's first beat comes a
        // little later, a short lead-in beat is added in front of it (charters sync songs the
        // same way, with a first tempo marker that only exists to line things up).
        double firstBeat = beats.get(0);
        if (firstBeat < 0.03) {
            beats.set(0, 0.0);
        } else {
            beats.add(0, 0.0);
            down = (down + 1) % 4;
        }
        double[] out = new double[beats.size()];
        for (int i = 0; i < out.length; i++) out[i] = beats.get(i);
        return new Result(out, down, 60.0 / secPerBeat);
    }

    private static Result fallback(double duration) {
        int count = (int) Math.ceil(Math.max(1.0, duration) / 0.5) + 2;
        double[] beats = new double[count];
        for (int i = 0; i < count; i++) beats[i] = i * 0.5;
        return new Result(beats, 0, 120.0);
    }

    /** Removes the slow-moving average and scales to unit spread, keeping only the peaks. */
    static float[] normalize(float[] x, int half) {
        int n = x.length;
        double[] prefix = new double[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + x[i];
        float[] out = new float[n];
        double sumSq = 0;
        for (int i = 0; i < n; i++) {
            int a = Math.max(0, i - half);
            int b = Math.min(n, i + half + 1);
            double mean = (prefix[b] - prefix[a]) / (b - a);
            float v = (float) Math.max(0, x[i] - mean);
            out[i] = v;
            sumSq += v * v;
        }
        double std = Math.sqrt(sumSq / Math.max(1, n));
        if (std > 1e-9) for (int i = 0; i < n; i++) out[i] = (float) (out[i] / std);
        return out;
    }

    /** Beat period in frames from the autocorrelation of the onset envelope. */
    private static double estimatePeriod(float[] env, double fps) {
        int n = env.length;
        int minLag = (int) Math.floor(fps * 60.0 / 240.0);
        int maxLag = (int) Math.ceil(fps * 60.0 / 40.0);
        int top = Math.min(n - 1, maxLag * 4 + 2);
        if (top <= minLag + 2) return -1;
        double[] ac = new double[top + 1];
        for (int lag = 1; lag <= top; lag++) {
            double s = 0;
            int m = n - lag;
            for (int i = 0; i < m; i++) s += env[i] * env[i + lag];
            ac[lag] = s / m;
        }
        double best = -1;
        int bestLag = -1;
        for (int lag = minLag; lag <= maxLag && lag * 4 <= top; lag++) {
            double bpm = 60.0 * fps / lag;
            double z = Math.log(bpm / 120.0) / Math.log(2) / 0.8;
            double w = Math.exp(-0.5 * z * z);
            // A real beat repeats at 1, 2, 3 and 4 times its length; a 1.5-beat or 3-beat
            // pattern does not, so demanding all four rejects those look-alikes.
            double s = 0;
            for (int k = 1; k <= 4; k++) s += peakNear(ac, lag * k, k);
            s = s / 4 * w;
            if (s > best) {
                best = s;
                bestLag = lag;
            }
        }
        if (bestLag < 0) return -1;
        // Use the longest clear multiple of the lag to sharpen the estimate.
        double period = bestLag;
        int mult = Math.max(1, Math.min(8, top / bestLag));
        for (int k = mult; k >= 1; k--) {
            int centre = bestLag * k;
            int spread = Math.max(1, k);
            int peak = -1;
            for (int lag = Math.max(1, centre - spread); lag <= Math.min(top - 1, centre + spread); lag++) {
                if (peak < 0 || ac[lag] > ac[peak]) peak = lag;
            }
            if (peak > 1 && peak < top && ac[peak] > 0.3 * ac[bestLag]) {
                double a = ac[peak - 1], b = ac[peak], c = ac[peak + 1];
                double denom = a - 2 * b + c;
                double shift = Math.abs(denom) > 1e-12 ? 0.5 * (a - c) / denom : 0;
                if (Math.abs(shift) > 1) shift = 0;
                period = (peak + shift) / k;
                break;
            }
        }
        return period;
    }

    private static double peakNear(double[] ac, int centre, int spread) {
        double m = 0;
        for (int i = Math.max(1, centre - spread); i <= Math.min(ac.length - 1, centre + spread); i++) m = Math.max(m, ac[i]);
        return m;
    }

    /** Sub-frame position of a peak near frame t. */
    private static double refine(float[] env, int t) {
        if (t <= 0 || t >= env.length - 1) return t;
        double a = env[t - 1], b = env[t], c = env[t + 1];
        if (b <= 1e-9 || b < a || b < c) return t;
        // Lean towards the frame before when the rise began there (the sound started earlier).
        if (a >= 0.3 * b) return t - 0.5 * Math.min(1.0, a / b) - 0.25;
        double denom = a - 2 * b + c;
        if (Math.abs(denom) < 1e-12) return t;
        double shift = 0.5 * (a - c) / denom;
        return t + Math.max(-0.5, Math.min(0.5, shift));
    }
}
