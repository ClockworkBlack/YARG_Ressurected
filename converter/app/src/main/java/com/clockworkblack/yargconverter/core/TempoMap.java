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
    /** Number of beats before the first full measure (0-3). */
    public final int pickupBeats;

    private TempoMap(long[] tick, long[] milliBpm, double[] time, int pickupBeats) {
        this.tick = tick;
        this.milliBpm = milliBpm;
        this.time = time;
        this.pickupBeats = pickupBeats;
    }

    /**
     * @param beats beat times in seconds, beats[0] == 0
     * @param firstDownbeat index of the first beat that starts a measure (0-3)
     */
    public static TempoMap fromBeats(double[] beats, int firstDownbeat) {
        final double tolerance = 0.022;
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
        return new TempoMap(tick, milli, time, ((firstDownbeat % 4) + 4) % 4);
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

    /** Tick where measure {@code index} starts (measure 0 is the first full measure). */
    public long measureStart(int index) {
        return (long) pickupBeats * RESOLUTION + (long) index * 4 * RESOLUTION;
    }

    public boolean isBeat(long tk) {
        return tk % RESOLUTION == 0;
    }

    public boolean isDownbeat(long tk) {
        long rel = tk - (long) pickupBeats * RESOLUTION;
        return rel >= 0 && rel % (4L * RESOLUTION) == 0;
    }
}
