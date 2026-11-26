package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.List;

/**
 * Finds the tempo, the beat positions and the measure layout of a song.
 *
 * 1. Overall tempo from how the rhythm repeats.
 * 2. Local tempo every two seconds, so songs that speed up or slow down (most music played
 *    without a click track) stay locked.
 * 3. A dynamic-programming pass picks the chain of beats that best sits on strong onsets.
 * 4. Bass hits and chord changes say which beat is the "one", and whether measures have
 *    three beats or four.
 */
public final class BeatTracker {
    private BeatTracker() {}

    public static final class Result {
        /** Beat times in seconds; the first is always 0 and the last is at or past the end of the song. */
        public final double[] beats;
        /** Index of the first beat that starts a measure. */
        public final int firstDownbeat;
        /** Beats per measure: 3 or 4. */
        public final int beatsPerMeasure;
        /** Overall tempo in beats per minute. */
        public final double bpm;

        Result(double[] beats, int firstDownbeat, int beatsPerMeasure, double bpm) {
            this.beats = beats;
            this.firstDownbeat = firstDownbeat;
            this.beatsPerMeasure = beatsPerMeasure;
            this.bpm = bpm;
        }
    }

    public static Result track(Features f) {
        int n = f.frames();
        double fps = f.fps;
        if (n < fps * 2) return fallback(f.duration);

        // Bass counts extra: kick drum and bass guitar are the most reliable guide to the beat.
        float[] mixed = new float[n];
        for (int i = 0; i < n; i++) mixed[i] = f.flux[i] + 1.5f * f.lowFlux[i];
        float[] env = normalize(mixed, (int) Math.round(fps * 0.75));

        double activity = 0;
        for (float v : env) activity += v;
        // Silence or a steady drone has no beat to find.
        if (activity < 1e-3) return fallback(f.duration);
        double global = estimatePeriod(env, fps);
        if (global <= 0) return fallback(f.duration);
        double[] period = localPeriods(env, fps, global);

        // Dynamic programming: best chain of beats that sit on strong onsets and stay near the
        // local beat length.
        final double tightness = 100.0;
        double[] cum = new double[n];
        int[] back = new int[n];
        for (int t = 0; t < n; t++) {
            double p = period[t];
            int lo = (int) Math.round(p * 2);
            int hi = (int) Math.max(1, Math.round(p / 2));
            double best = 0;
            int bestPrev = -1;
            for (int q = Math.max(0, t - lo); q <= t - hi; q++) {
                double l = Math.log((t - q) / p);
                double s = cum[q] - tightness * l * l;
                if (s > best) {
                    best = s;
                    bestPrev = q;
                }
            }
            cum[t] = env[t] + best;
            back[t] = bestPrev;
        }
        int last = n - 1;
        for (int t = Math.max(0, n - (int) Math.round(period[n - 1])); t < n; t++) {
            if (cum[t] > cum[last]) last = t;
        }
        List<Integer> reversed = new ArrayList<>();
        for (int t = last; t >= 0; t = back[t]) reversed.add(t);
        int count = reversed.size();
        if (count < 4) return fallback(f.duration);
        int[] chain = new int[count];
        double[] raw = new double[count];
        for (int i = 0; i < count; i++) {
            chain[i] = reversed.get(count - 1 - i);
            raw[i] = f.timeOf(refine(env, chain[i]));
        }

        int[] meter = findMeter(f, chain, period);
        int beatsPerMeasure = meter[0];
        int down = meter[1];

        // Extend the grid back to the start and forward to the end of the song.
        double firstStep = period[chain[0]] / fps;
        double lastStep = period[chain[count - 1]] / fps;
        List<Double> beats = new ArrayList<>();
        int before = 0;
        while (raw[0] - firstStep * (before + 1) >= 0) before++;
        for (int i = before; i >= 1; i--) beats.add(raw[0] - firstStep * i);
        for (double b : raw) beats.add(b);
        while (beats.get(beats.size() - 1) < f.duration) beats.add(beats.get(beats.size() - 1) + lastStep);
        down = Math.floorMod(down + before, beatsPerMeasure);

        // Chart time starts at 0, so a beat has to sit there. If the music's first beat comes a
        // little later, a short lead-in beat is added in front of it (charters sync songs the
        // same way, with a first tempo marker that only exists to line things up).
        if (beats.get(0) < 0.03) {
            beats.set(0, 0.0);
        } else {
            beats.add(0, 0.0);
            down = (down + 1) % beatsPerMeasure;
        }
        double[] out = new double[beats.size()];
        for (int i = 0; i < out.length; i++) out[i] = beats.get(i);
        return new Result(out, down, beatsPerMeasure, 60.0 * fps / global);
    }

    /**
     * Nudges each beat onto the strongest note that starts within a few hundredths of a second
     * of it. Note starts are timed more exactly than the beat tracker's own guess, so this is
     * what makes notes in the game line up with what is heard. Beats with no note nearby take
     * the average nudge of their neighbours.
     */
    public static double[] alignToOnsets(double[] beats, double[] onsetTimes, float[] onsetStrengths) {
        int n = beats.length;
        double[] shift = new double[n];
        boolean[] known = new boolean[n];
        int at = 0;
        for (int i = 1; i < n; i++) {
            while (at < onsetTimes.length && onsetTimes[at] < beats[i] - 0.04) at++;
            int best = -1;
            for (int k = at; k < onsetTimes.length && onsetTimes[k] <= beats[i] + 0.04; k++) {
                if (best < 0 || onsetStrengths[k] > onsetStrengths[best]) best = k;
            }
            if (best >= 0) {
                shift[i] = onsetTimes[best] - beats[i];
                known[i] = true;
            }
        }
        double[] out = beats.clone();
        for (int i = 1; i < n; i++) {
            double sum = 0;
            int count = 0;
            for (int k = Math.max(1, i - 4); k <= Math.min(n - 1, i + 4); k++) {
                if (known[k]) {
                    sum += shift[k];
                    count++;
                }
            }
            if (known[i]) out[i] = beats[i] + 0.5 * shift[i] + 0.5 * sum / count;
            else if (count > 0) out[i] = beats[i] + sum / count;
        }
        return out;
    }

    private static Result fallback(double duration) {
        int count = (int) Math.ceil(Math.max(1.0, duration) / 0.5) + 2;
        double[] beats = new double[count];
        for (int i = 0; i < count; i++) beats[i] = i * 0.5;
        return new Result(beats, 0, 4, 120.0);
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
        // Long enough to see eight beats of the slowest look-alike tempo (see lookAlike).
        int top = Math.min(n - 1, maxLag * 12 + 16);
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
            double w = preference(fps, lag);
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
        bestLag = lookAlike(ac, bestLag, minLag, maxLag, fps);
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

    /** Look-alike tempos tried against the first choice: 3/4, 4/3, 2/3 and 3/2 of it. */
    private static final double[] LOOK_ALIKES = {4 / 3.0, 3 / 4.0, 3 / 2.0, 2 / 3.0};

    /**
     * A dotted or triplet pattern can pass for the beat: trap 808s that play in dotted eighths
     * (3-3-2) make a song at 95 BPM look like 127 BPM, 4/3 of it. Both repeat at every beat, but
     * only the real tempo makes the music repeat measure by measure, because songs are built out
     * of measures (a beat loop, a riff, a chord change). So a look-alike tempo replaces the first
     * choice when it is nearly as good a beat and makes the song repeat by the measure much more
     * strongly. Halving and doubling are left to the tempo preference above.
     *
     * @return the lag (beat length in frames) to use
     */
    static int lookAlike(double[] ac, int lag, int minLag, int maxLag, double fps) {
        double beat = beatScore(ac, lag);
        double score = beat * measureScore(ac, lag) * preference(fps, lag);
        int best = lag;
        double bestScore = score;
        for (double r : LOOK_ALIKES) {
            int alt = (int) Math.round(lag * r);
            if (alt < minLag || alt > maxLag || alt * 8 + 8 >= ac.length) continue;
            double altBeat = beatScore(ac, alt);
            double altScore = altBeat * measureScore(ac, alt) * preference(fps, alt);
            if (altBeat >= 0.8 * beat && altScore >= 1.5 * score && altScore > bestScore) {
                best = alt;
                bestScore = altScore;
            }
        }
        return best;
    }

    /** How well the lag repeats at 1, 2, 3 and 4 times its length. */
    private static double beatScore(double[] ac, int lag) {
        double s = 0;
        for (int k = 1; k <= 4; k++) s += peakNear(ac, lag * k, k);
        return s / 4;
    }

    /** How strongly the song repeats one and two measures on (of 4 or of 3 beats). */
    private static double measureScore(double[] ac, int lag) {
        double four = (peakNear(ac, lag * 4, 4) + peakNear(ac, lag * 8, 8)) / 2;
        double three = (peakNear(ac, lag * 3, 3) + peakNear(ac, lag * 6, 6)) / 2;
        return Math.max(four, three);
    }

    /** The preference for tempos near 120 BPM, the same one the first choice is made with. */
    private static double preference(double fps, int lag) {
        double bpm = 60.0 * fps / lag;
        double z = Math.log(bpm / 120.0) / Math.log(2) / 0.8;
        return Math.exp(-0.5 * z * z);
    }

    private static double peakNear(double[] ac, int centre, int spread) {
        double m = 0;
        for (int i = Math.max(1, centre - spread); i <= Math.min(ac.length - 1, centre + spread); i++) m = Math.max(m, ac[i]);
        return m;
    }

    /**
     * Beat length for every frame. The song is examined in overlapping 8-second windows; in
     * each, candidate tempos within about 20% of the overall tempo are scored by how well the
     * rhythm repeats at that spacing, and the smoothest high-scoring path through the windows wins.
     */
    private static double[] localPeriods(float[] env, double fps, double global) {
        int n = env.length;
        double[] out = new double[n];
        java.util.Arrays.fill(out, global);
        int win = (int) Math.round(8 * fps);
        int step = (int) Math.round(2 * fps);
        if (n < win * 2) return out;
        final int states = 41;
        double[] ratio = new double[states];
        for (int s = 0; s < states; s++) ratio[s] = Math.exp(Math.log(0.8) + s * (Math.log(1.25) - Math.log(0.8)) / (states - 1));
        int windows = (n - win) / step + 1;
        int maxLag = Math.min(win - 2, (int) Math.ceil(global * 1.25 * 4) + 2);
        double[][] score = new double[windows][states];
        double[] ac = new double[maxLag + 2];
        for (int j = 0; j < windows; j++) {
            int start = j * step;
            double zero = 0;
            for (int i = start; i < start + win; i++) zero += env[i] * env[i];
            zero /= win;
            if (zero < 1e-6) continue;
            for (int lag = 1; lag <= maxLag + 1 && lag < win; lag++) {
                double s = 0;
                int m = win - lag;
                for (int i = start; i < start + m; i++) s += env[i] * env[i + lag];
                ac[lag] = s / m / zero;
            }
            for (int s = 0; s < states; s++) {
                double sum = 0;
                for (int k = 1; k <= 4; k++) {
                    double pos = k * global * ratio[s];
                    int a = (int) Math.floor(pos);
                    if (a < 1 || a + 1 > maxLag + 1 || a + 1 >= win) continue;
                    double fr = pos - a;
                    sum += ac[a] * (1 - fr) + ac[a + 1] * fr;
                }
                score[j][s] = sum;
            }
        }
        // Viterbi: tempo may change between windows, but each step of change has a price.
        final double stepCost = 0.05;
        double[][] best = new double[windows][states];
        int[][] from = new int[windows][states];
        int mid = states / 2;
        for (int s = 0; s < states; s++) best[0][s] = score[0][s] - 0.01 * Math.abs(s - mid);
        for (int j = 1; j < windows; j++) {
            for (int s = 0; s < states; s++) {
                double b = -1e18;
                int arg = s;
                for (int q = Math.max(0, s - 8); q <= Math.min(states - 1, s + 8); q++) {
                    double v = best[j - 1][q] - stepCost * Math.abs(s - q);
                    if (v > b) {
                        b = v;
                        arg = q;
                    }
                }
                best[j][s] = b + score[j][s] - 0.01 * Math.abs(s - mid);
                from[j][s] = arg;
            }
        }
        int[] path = new int[windows];
        int end = 0;
        for (int s = 1; s < states; s++) if (best[windows - 1][s] > best[windows - 1][end]) end = s;
        path[windows - 1] = end;
        for (int j = windows - 1; j > 0; j--) path[j - 1] = from[j][path[j]];

        for (int i = 0; i < n; i++) {
            double pos = (i - win / 2.0) / step;
            int a = (int) Math.floor(pos);
            double fr = pos - a;
            int ja = Math.max(0, Math.min(windows - 1, a));
            int jb = Math.max(0, Math.min(windows - 1, a + 1));
            out[i] = global * (ratio[path[ja]] * (1 - fr) + ratio[path[jb]] * fr);
        }
        return out;
    }

    /**
     * Decides how many beats make a measure and which beat is the first of a measure.
     * Measures usually begin with a bass hit and a change of chord.
     *
     * @return {beats per measure, index of the first downbeat}
     */
    private static int[] findMeter(Features f, int[] chain, double[] period) {
        int count = chain.length;
        int n = f.frames();
        double[] low = new double[count];
        double[] hit = new double[count];
        double[] change = new double[count];
        float[] prev = null;
        for (int i = 0; i < count; i++) {
            int fr = chain[i];
            float l = 0, h = 0;
            for (int k = Math.max(0, fr - 2); k <= Math.min(n - 1, fr + 2); k++) {
                l = Math.max(l, f.lowFlux[k]);
                h = Math.max(h, f.flux[k]);
            }
            low[i] = l;
            hit[i] = h;
            int next = i + 1 < count ? chain[i + 1] : (int) Math.min(n, fr + Math.round(period[fr]));
            float[] c = f.chromaBetween(fr + 1, next);
            change[i] = prev == null ? 0 : 1 - Features.cosine(c, prev);
            prev = c;
        }
        zscore(low);
        zscore(hit);
        zscore(change);
        double[] feat = new double[count];
        for (int i = 0; i < count; i++) feat[i] = low[i] + change[i] + 0.3 * hit[i];

        double best4 = -1e9, best3 = -1e9;
        int phase4 = 0, phase3 = 0;
        for (int m = 3; m <= 4; m++) {
            for (int p = 0; p < m; p++) {
                double on = 0, off = 0;
                int nOn = 0, nOff = 0;
                for (int i = 0; i < count; i++) {
                    if (i % m == p) {
                        on += feat[i];
                        nOn++;
                    } else {
                        off += feat[i];
                        nOff++;
                    }
                }
                double s = (nOn > 0 ? on / nOn : 0) - (nOff > 0 ? off / nOff : 0);
                if (m == 4 && s > best4) {
                    best4 = s;
                    phase4 = p;
                } else if (m == 3 && s > best3) {
                    best3 = s;
                    phase3 = p;
                }
            }
        }
        // Four is by far the most common, so three has to win clearly.
        if (count >= 24 && best3 > best4 * 1.3 + 0.2) return new int[] {3, phase3};
        return new int[] {4, phase4};
    }

    private static void zscore(double[] x) {
        double mean = 0;
        for (double v : x) mean += v;
        mean /= Math.max(1, x.length);
        double var = 0;
        for (double v : x) var += (v - mean) * (v - mean);
        double std = Math.sqrt(var / Math.max(1, x.length));
        for (int i = 0; i < x.length; i++) x[i] = std > 1e-9 ? (x[i] - mean) / std : 0;
    }

    /** Sub-frame position of a beat near frame t. */
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

    /** Beats laid on one steady tempo, and the index of the first downbeat among them. */
    public static final class Steady {
        public final double[] beats;
        public final int firstDownbeat;
        public final double bpm;
        /** How far the beats found were from the steady grid (root mean square, seconds). */
        public final double rms;

        Steady(double[] beats, int firstDownbeat, double bpm, double rms) {
            this.beats = beats;
            this.firstDownbeat = firstDownbeat;
            this.bpm = bpm;
            this.rms = rms;
        }
    }

    /** Beats this close to one steady tempo (root mean square) are that tempo, with jitter. */
    static boolean DEBUG = Boolean.getBoolean("yarg.debugTempo");
    static double STEADY_RMS = Double.parseDouble(System.getProperty("yarg.steadyRms", "0.022"));

    /**
     * Studio recordings are mostly played to a click: one tempo from start to end. Beats found in
     * the audio wobble around it by a few hundredths of a second, and that wobble would become a
     * tempo change every few beats (Gee, a steady 100 BPM, came out as 16 changes between 94
     * and 107), so measures drift against the music and repeats stop lining up. When the beats
     * fit one tempo closely, they are replaced by that tempo's grid.
     *
     * @return the steady grid, or null when the tempo really moves (live playing, a slow-down)
     */
    public static Steady steady(double[] beats, int firstDownbeat, double duration) {
        int n = beats.length;
        if (n < 16 || STEADY_RMS <= 0) return null;
        // The middle beat length, then each beat's place on that grid (a missed or doubled beat is skipped over)
        double[] gaps = new double[n - 2];
        for (int i = 2; i < n; i++) gaps[i - 2] = beats[i] - beats[i - 1];
        double[] sorted = gaps.clone();
        java.util.Arrays.sort(sorted);
        double period = sorted[sorted.length / 2];
        if (period <= 0.2 || period >= 2.0) return null;
        double origin = beats[1];
        double[] k = new double[n - 1], t = new double[n - 1];
        // Counted from beat to beat, so a slightly wrong period cannot add up over the song
        k[0] = 0;
        t[0] = beats[1];
        for (int i = 2; i < n; i++) {
            k[i - 1] = k[i - 2] + Math.max(1, Math.round((beats[i] - beats[i - 1]) / period));
            t[i - 1] = beats[i];
        }
        // Least squares line through (k, t), twice: the second time without beats far off the first line
        boolean[] use = new boolean[k.length];
        java.util.Arrays.fill(use, true);
        double a = 0, b = period;
        for (int pass = 0; pass < 2; pass++) {
            double sk = 0, st = 0, skk = 0, skt = 0;
            int m = 0;
            for (int i = 0; i < k.length; i++) {
                if (!use[i]) continue;
                sk += k[i]; st += t[i]; skk += k[i] * k[i]; skt += k[i] * t[i]; m++;
            }
            double den = m * skk - sk * sk;
            if (m < 12 || den == 0) return null;
            b = (m * skt - sk * st) / den;
            a = (st - b * sk) / m;
            if (pass == 0) {
                int off = 0;
                for (int i = 0; i < k.length; i++) {
                    if (Math.abs(t[i] - (a + b * k[i])) > 0.1) { use[i] = false; off++; }
                }
                // Many beats far from the line: the tempo really changes
                if (DEBUG) System.err.printf("steady: period %.4f, %d of %d beats off the line%n", period, off, k.length);
                if (off > k.length / 10) return null;
            }
        }
        double sq = 0;
        int m = 0;
        for (int i = 0; i < k.length; i++) {
            if (!use[i]) continue;
            double r = t[i] - (a + b * k[i]);
            sq += r * r;
            m++;
        }
        double rms = Math.sqrt(sq / m);
        if (DEBUG) System.err.printf("steady: rms %.4f s%n", rms);
        if (rms > STEADY_RMS) return null;
        // Also no slow drift: the first and last thirds alone must agree on the tempo
        double first = slope(k, t, use, 0, k.length / 3), last = slope(k, t, use, 2 * k.length / 3, k.length);
        if (DEBUG) System.err.printf("steady: first third %.5f, last third %.5f, differ %.4f%n", first, last, Math.abs(first - last) / b);
        if (Double.isNaN(first) || Double.isNaN(last) || Math.abs(first - last) / b > 0.004) return null;

        // The grid: beat 0 stays at time zero (the lead-in), then every beat from the first one
        // after zero to past the end of the song
        long kFirst = (long) Math.ceil((0.01 - a) / b);
        long kLast = (long) Math.ceil((duration + 2 * b - a) / b);
        int count = (int) (kLast - kFirst + 1);
        double[] grid = new double[count + 1];
        grid[0] = 0;
        for (int i = 0; i < count; i++) grid[i + 1] = a + (kFirst + i) * b;
        // The downbeat found before, carried to the nearest beat of the grid
        double down = beats[Math.max(1, Math.min(n - 1, firstDownbeat))];
        int downIndex = (int) Math.round((down - a) / b - kFirst) + 1;
        downIndex = Math.max(1, Math.min(count, downIndex));
        return new Steady(grid, downIndex, 60.0 / b, rms);
    }

    /**
     * When the beats found do not fit one tempo, the audio may still keep one: Gee, a steady
     * 100 BPM, gave beats half of which were a tenth of a second or more off. Then the tempo is
     * measured straight from the rhythm of the whole song (how its note starts repeat), section
     * by section; if every section agrees, a grid at that tempo, set where the note starts fall,
     * replaces the beats.
     *
     * @param near the tempo the beat tracker found (seconds per beat): the answer must be close,
     *             so that double or half the tempo is never chosen
     */
    public static Steady steadyFromRhythm(Features f, double near, double[] beats, int firstDownbeat) {
        float[] flux = f.flux;
        if (flux == null || flux.length < 200 || near <= 0) return null;
        double fps = f.fps;
        int n = flux.length;
        double[] x = new double[n];
        double mean = 0;
        for (int i = 0; i < n; i++) mean += flux[i];
        mean /= n;
        for (int i = 0; i < n; i++) x[i] = Math.max(0, flux[i] - mean);
        int lo = (int) Math.floor(near * 0.92 * fps), hi = (int) Math.ceil(near * 1.08 * fps);
        double global = bestLag(x, 0, n, lo, hi);
        if (Double.isNaN(global)) return null;
        // Every 20-second section must find the same beat length
        int window = (int) (20 * fps);
        int sections = 0, agree = 0;
        for (int from = 0; from + window <= n; from += window) {
            double lag = bestLag(x, from, from + window, lo, hi);
            if (Double.isNaN(lag)) continue;
            sections++;
            if (Math.abs(lag - global) / global <= 0.006) agree++;
        }
        if (DEBUG) System.err.printf("rhythm: beat %.4f s, %d of %d sections agree%n", global / fps, agree, sections);
        if (sections < 4 || agree < sections) return null;
        double period = global / fps;
        // Where the grid sits: the offset at which the note starts land on beats the most
        int steps = 64;
        double bestPhase = 0, bestSum = -1;
        for (int p = 0; p < steps; p++) {
            double phase = period * p / steps, sum = 0;
            for (double t = phase; t < f.duration; t += period) {
                int fr = f.frameAt(t);
                if (fr >= 0 && fr < n) sum += x[fr];
            }
            if (sum > bestSum) {
                bestSum = sum;
                bestPhase = phase;
            }
        }
        // Fine tuning: the strongest note start near each grid beat, and the straight line through them
        for (int pass = 0; pass < 2; pass++) {
            double sk = 0, st = 0, skk = 0, skt = 0, sw = 0;
            int kk = 0;
            for (double t = bestPhase; t < f.duration; t += period, kk++) {
                int c = f.frameAt(t), reach = (int) Math.round(0.06 * fps);
                int bestF = -1;
                for (int fr = Math.max(0, c - reach); fr <= Math.min(n - 1, c + reach); fr++) if (bestF < 0 || x[fr] > x[bestF]) bestF = fr;
                if (bestF < 0 || x[bestF] <= 0) continue;
                double w = x[bestF], tt = f.timeOf(bestF);
                sw += w; sk += w * kk; st += w * tt; skk += w * kk * kk; skt += w * kk * tt;
            }
            double den = sw * skk - sk * sk;
            if (sw <= 0 || den == 0) break;
            double b = (sw * skt - sk * st) / den, a0 = (st - b * sk) / sw;
            if (Math.abs(b - period) / period > 0.01) break;
            period = b;
            bestPhase = a0;
            while (bestPhase < 0) bestPhase += period;
            while (bestPhase >= period) bestPhase -= period;
        }
        if (DEBUG) System.err.printf("rhythm: tuned beat %.5f s (%.3f BPM)%n", period, 60 / period);
        int count = (int) Math.ceil((f.duration + 2 * period - bestPhase) / period);
        double[] grid = new double[count + 1];
        grid[0] = 0;
        for (int i = 0; i < count; i++) grid[i + 1] = bestPhase + i * period + (bestPhase < 0.01 && i == 0 ? period : 0) * 0;
        if (grid[1] < 0.01) for (int i = 1; i <= count; i++) grid[i] += period;
        double down = beats[Math.max(1, Math.min(beats.length - 1, firstDownbeat))];
        int downIndex = (int) Math.round((down - grid[1]) / period) + 1;
        downIndex = Math.max(1, Math.min(count, downIndex));
        return new Steady(grid, downIndex, 60.0 / period, 0);
    }

    /** The lag (frames, with fractions) in [lo, hi] at which x[from..to) repeats best, or NaN. */
    private static double bestLag(double[] x, int from, int to, int lo, int hi) {
        if (to - from < 4 * hi) return Double.NaN;
        double[] ac = new double[hi + 2];
        for (int lag = Math.max(1, lo - 1); lag <= hi + 1; lag++) {
            double sum = 0;
            for (int i = from; i + lag < to; i++) sum += x[i] * x[i + lag];
            ac[lag] = sum;
        }
        int best = -1;
        for (int lag = lo; lag <= hi; lag++) if (best < 0 || ac[lag] > ac[best]) best = lag;
        if (best <= lo || best >= hi) return Double.NaN;
        double y0 = ac[best - 1], y1 = ac[best], y2 = ac[best + 1];
        double den = y0 - 2 * y1 + y2;
        return best + (den == 0 ? 0 : 0.5 * (y0 - y2) / den);
    }

    private static double slope(double[] k, double[] t, boolean[] use, int from, int to) {
        double sk = 0, st = 0, skk = 0, skt = 0;
        int m = 0;
        for (int i = from; i < to; i++) {
            if (!use[i]) continue;
            sk += k[i]; st += t[i]; skk += k[i] * k[i]; skt += k[i] * t[i]; m++;
        }
        double den = m * skk - sk * sk;
        return m < 6 || den == 0 ? Double.NaN : (m * skt - sk * st) / den;
    }
}
