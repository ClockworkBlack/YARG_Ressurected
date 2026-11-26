package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.List;

/** Converts between seconds and chart ticks, and holds the tempo markers written to the chart. */
public final class TempoMap {
    public static final int RESOLUTION = 192;

    /** Tick where each tempo marker sits. */
    public final long[] tick;
    /** Tempo of each marker in thousandths of a BPM (the unit .chart files use). */
    public final long[] milliBpm;
    /** Time in seconds of each marker, as the game will compute it. */
    public final double[] time;
    /** Number of beats before the first full measure. */
    public final int pickupBeats;
    /** Beats in a measure: 4, or 3 for waltz-time songs. */
    public final int beatsPerMeasure;

    private TempoMap(long[] tick, long[] milliBpm, double[] time, int pickupBeats, int beatsPerMeasure) {
        this.tick = tick;
        this.milliBpm = milliBpm;
        this.time = time;
        this.pickupBeats = pickupBeats;
        this.beatsPerMeasure = beatsPerMeasure;
    }

    /**
     * The same timing counted twice as fast: every beat becomes two, every tick two ticks, so a
     * tick of this map is exactly tick * 2 of the one returned.
     */
    public TempoMap doubled() {
        long[] t = new long[tick.length], m = new long[tick.length];
        for (int i = 0; i < t.length; i++) {
            t[i] = tick[i] * 2;
            m[i] = milliBpm[i] * 2;
        }
        return new TempoMap(t, m, time, (2 * pickupBeats) % beatsPerMeasure, beatsPerMeasure);
    }

    /**
     * @param beats beat times in seconds, beats[0] == 0
     * @param firstDownbeat index of the first beat that starts a measure
     * @param beatsPerMeasure 3 or 4
     */
    public static TempoMap fromBeats(double[] rawBeats, int firstDownbeat, int beatsPerMeasure) {
        final double tolerance = 0.018;
        double[] beats = smooth(rawBeats);
        List<Integer> bounds = new ArrayList<>();
        bounds.add(0);
        int s = 0;
        int n = beats.length;
        while (s < n - 1) {
            int e = s + 1;
            while (e + 1 < n && fits(beats, s, e + 1, tolerance)) e++;
            bounds.add(e);
            s = e;
        }
        int segs = bounds.size() - 1;
        long[] tick = new long[segs];
        long[] milli = new long[segs];
        double[] time = new double[segs];
        double chartTime = 0;
        for (int i = 0; i < segs; i++) {
            int a = bounds.get(i);
            int b = bounds.get(i + 1);
            // Aim at the real time of the segment's last beat from wherever rounding has left us,
            // so small rounding errors never pile up over the song.
            double span = Math.max(0.03 * (b - a), beats[b] - chartTime);
            long m = Math.round(60000.0 * (b - a) / span);
            m = Math.max(20000, Math.min(2000000, m));
            tick[i] = (long) a * RESOLUTION;
            milli[i] = m;
            time[i] = chartTime;
            chartTime += (b - a) * 60000.0 / m;
        }
        return new TempoMap(tick, milli, time, Math.floorMod(firstDownbeat, beatsPerMeasure), beatsPerMeasure);
    }

    /**
     * Irons out frame-to-frame jitter: each beat is moved onto the straight line through its
     * three neighbours on either side. Beat 0 (the lead-in at time zero) is left alone.
     */
    private static double[] smooth(double[] beats) {
        int n = beats.length;
        double[] out = beats.clone();
        for (int i = 1; i < n; i++) {
            int a = Math.max(1, i - 3);
            int b = Math.min(n - 1, i + 3);
            int count = b - a + 1;
            if (count < 3) continue;
            double mx = 0, my = 0;
            for (int k = a; k <= b; k++) {
                mx += k;
                my += beats[k];
            }
            mx /= count;
            my /= count;
            double sxy = 0, sxx = 0;
            for (int k = a; k <= b; k++) {
                sxy += (k - mx) * (beats[k] - my);
                sxx += (k - mx) * (k - mx);
            }
            out[i] = my + (sxx > 0 ? sxy / sxx : 0) * (i - mx);
        }
        // Keep time moving forwards whatever the input looked like.
        for (int i = 1; i < n; i++) if (out[i] <= out[i - 1] + 0.02) out[i] = out[i - 1] + 0.02;
        return out;
    }

    private static boolean fits(double[] beats, int s, int e, double tol) {
        double step = (beats[e] - beats[s]) / (e - s);
        for (int i = s + 1; i < e; i++) {
            if (Math.abs(beats[s] + step * (i - s) - beats[i]) > tol) return false;
        }
        return true;
    }

    private int segmentAtTime(double t) {
        int i = time.length - 1;
        while (i > 0 && time[i] > t) i--;
        return i;
    }

    private int segmentAtTick(long tk) {
        int i = tick.length - 1;
        while (i > 0 && tick[i] > tk) i--;
        return i;
    }

    public double tickToTime(long tk) {
        int i = segmentAtTick(tk);
        return time[i] + (tk - tick[i]) * 60000.0 / (milliBpm[i] * (double) RESOLUTION);
    }

    public double timeToTickExact(double t) {
        int i = segmentAtTime(t);
        return tick[i] + (t - time[i]) * milliBpm[i] * RESOLUTION / 60000.0;
    }

    public long timeToTick(double t) {
        return Math.max(0, Math.round(timeToTickExact(Math.max(0, t))));
    }

    /** Nearest tick on a grid of {@code grid} ticks. */
    public long snap(double t, int grid) {
        return Math.max(0, Math.round(timeToTickExact(Math.max(0, t)) / grid) * grid);
    }

    /** Ticks in one measure. */
    public long measureTicks() {
        return (long) beatsPerMeasure * RESOLUTION;
    }

    /** Tick where the first full measure starts. */
    public long origin() {
        return (long) pickupBeats * RESOLUTION;
    }

    /** Tick where measure {@code index} starts (measure 0 is the first full measure). */
    public long measureStart(int index) {
        return origin() + index * measureTicks();
    }

    /** Measure a tick falls in; -1 for the pickup before the first full measure. */
    public int measureOf(long tk) {
        return (int) Math.floorDiv(tk - origin(), measureTicks());
    }

    public boolean isBeat(long tk) {
        return tk % RESOLUTION == 0;
    }

    public boolean isDownbeat(long tk) {
        long rel = tk - origin();
        return rel >= 0 && rel % measureTicks() == 0;
    }
}
