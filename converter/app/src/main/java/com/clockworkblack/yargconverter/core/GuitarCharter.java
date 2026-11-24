package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.TreeMap;

/** Turns the detected note onsets of a song into 5-fret guitar charts for four difficulties. */
public final class GuitarCharter {
    private GuitarCharter() {}

    public static final int EASY = 0, MEDIUM = 1, HARD = 2, EXPERT = 3;
    public static final String[] SECTION_NAMES = {"EasySingle", "MediumSingle", "HardSingle", "ExpertSingle"};

    // Per difficulty: the shortest note spacing a player should face (seconds), how short the
    // in-between "extra" notes may get, how strong a sound must be to earn an extra note, number
    // of frets used, share of notes turned into chords, and the shortest gap that earns a sustain.
    private static final double[] BASE_MIN_SEC = {0.55, 0.33, 0.17, 0.085};
    private static final double[] FINE_MIN_SEC = {0.50, 0.24, 0.12, 9.0};
    private static final float[] FINE_STRENGTH = {0.8f, 0.6f, 0.8f, 9f};
    // A full band has something happening on almost every subdivision; weaker sounds on the
    // in-between slots are left out so Hard and Expert are not a constant stream of notes.
    private static final float[] OFF_STRENGTH = {0f, 0f, 0.3f, 0.45f};
    private static final int[] LANES = {3, 4, 5, 5};
    private static final double[] CHORD_SHARE = {0.0, 0.05, 0.10, 0.15};
    private static final int[] SUSTAIN_MIN_GAP = {576, 576, 384, 384};
    private static final float MIN_STRENGTH = 0.10f;

    public static final class Onset {
        public final double time;
        public final int frame;
        public final float strength;
        public final float pitch;
        Onset(double time, int frame, float strength, float pitch) {
            this.time = time;
            this.frame = frame;
            this.strength = strength;
            this.pitch = pitch;
        }
    }

    public static final class Note {
        public final long tick;
        public final int lane;
        /** Second fret of a chord, or -1. */
        public final int lane2;
        public final long sustain;

        Note(long tick, int lane, int lane2, long sustain) {
            this.tick = tick;
            this.lane = lane;
            this.lane2 = lane2;
            this.sustain = sustain;
        }
    }

    public static final class Chart {
        /** Notes for EASY..EXPERT, each sorted by tick. */
        public final List<List<Note>> notes = new ArrayList<>();
        /** Star Power phrases as {tick, length}. */
        public final List<long[]> starPower = new ArrayList<>();
        public int onsetCount;
    }

    /** Finds the moments where a new sound starts. */
    public static List<Onset> detectOnsets(Features f) {
        int n = f.frames();
        float[] flux = f.flux;
        int half = Math.max(4, (int) Math.round(f.fps * 0.4));
        int near = Math.max(2, (int) Math.round(f.fps * 0.035));
        double[] prefix = new double[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + flux[i];
        double globalMean = n > 0 ? prefix[n] / n : 0;
        float[] scratch = new float[2 * half + 1];
        List<Onset> out = new ArrayList<>();
        for (int i = 1; i < n - 1; i++) {
            float v = flux[i];
            if (v <= 0) continue;
            boolean peak = true;
            for (int k = Math.max(0, i - near); k <= Math.min(n - 1, i + near) && peak; k++) {
                if (flux[k] > v || (flux[k] == v && k < i)) peak = false;
            }
            if (!peak) continue;
            int a = Math.max(0, i - half);
            int b = Math.min(n, i + half + 1);
            int len = b - a;
            System.arraycopy(flux, a, scratch, 0, len);
            Arrays.sort(scratch, 0, len);
            double median = scratch[len / 2];
            double mean = (prefix[b] - prefix[a]) / len;
            double threshold = median + 0.35 * mean + 0.05 * globalMean;
            if (v <= threshold) continue;
            // Pitch just after the attack, once the note has settled.
            double p = 0;
            int c = 0;
            for (int k = i + 1; k <= Math.min(n - 1, i + 4); k++) {
                p += f.pitch[k];
                c++;
            }
            float pitch = c > 0 ? (float) (p / c) : f.pitch[i];
            // The sound actually began where the rise started, which can be a frame or two
            // before the peak.
            int rise = i;
            while (rise > 0 && i - rise < 2 && flux[rise - 1] >= 0.3f * v) rise--;
            out.add(new Onset(f.timeOf(rise), i, (float) (v - threshold), pitch));
        }
        if (!out.isEmpty()) {
            float[] s = new float[out.size()];
            for (int i = 0; i < s.length; i++) s[i] = out.get(i).strength;
            Arrays.sort(s);
            float ref = Math.max(1e-6f, s[(int) (s.length * 0.9)]);
            List<Onset> scaled = new ArrayList<>(out.size());
            for (Onset o : out) {
                // Anything this faint next to the song's solid hits is noise, not a note.
                if (o.strength < MIN_STRENGTH * ref) continue;
                scaled.add(new Onset(o.time, o.frame, o.strength / ref, o.pitch));
            }
            out = scaled;
        }
        return out;
    }

    /**
     * Ranks each note's pitch against the notes around it (0 = lowest, 1 = highest) so the
     * chart moves across the frets the way the music moves up and down.
     */
    private static float[] heights(List<Candidate> notes) {
        int n = notes.size();
        final int reach = 8;
        float[] out = new float[n];
        for (int i = 0; i < n; i++) {
            float pitch = notes.get(i).onset.pitch;
            if (i > 0 && Math.abs(pitch - notes.get(i - 1).onset.pitch) < 0.03f) {
                out[i] = out[i - 1];
                continue;
            }
            int below = 0, same = 0, total = 0;
            for (int k = Math.max(0, i - reach); k <= Math.min(n - 1, i + reach); k++) {
                float p = notes.get(k).onset.pitch;
                if (p < pitch - 0.01f) below++;
                else if (p <= pitch + 0.01f) same++;
                total++;
            }
            out[i] = (below + 0.5f * same) / total;
        }
        return out;
    }

    public static Chart build(Features f, List<Onset> onsets, TempoMap map, double bpm) {
        Chart chart = new Chart();
        chart.onsetCount = onsets.size();
        for (int d = EASY; d <= EXPERT; d++) chart.notes.add(buildDifficulty(f, onsets, map, d, bpm));
        addStarPower(chart, map, f.duration);
        return chart;
    }

    private static final class Candidate {
        final long tick;
        final Onset onset;
        final double weight;

        Candidate(long tick, Onset onset, double weight) {
            this.tick = tick;
            this.onset = onset;
            this.weight = weight;
        }
    }

    /** Smallest musical grid (in ticks) whose notes are at least {@code minSec} apart. */
    private static int gridFor(double beatSec, double minSec) {
        int grid = 48;
        while (grid < 768 && beatSec * grid / TempoMap.RESOLUTION < minSec) grid *= 2;
        return grid;
    }

    private static List<Note> buildDifficulty(Features f, List<Onset> onsets, TempoMap map, int d, double bpm) {
        double beatSec = 60.0 / Math.max(30.0, bpm);
        int base = gridFor(beatSec, BASE_MIN_SEC[d]);
        int fine = base / 2;
        boolean useFine = fine >= 48 && beatSec * fine / TempoMap.RESOLUTION >= FINE_MIN_SEC[d];
        int grid = useFine ? fine : base;
        long origin = (long) map.pickupBeats * TempoMap.RESOLUTION;

        // Snap every onset to the grid, measured from the first full measure so that slow
        // grids land on beats 1 and 3. When several land on one slot the strongest wins.
        TreeMap<Long, Candidate> slots = new TreeMap<>();
        for (Onset o : onsets) {
            long tick = Math.round((map.timeToTickExact(o.time) - origin) / grid) * grid + origin;
            if (tick < 0) continue;
            boolean onBase = Math.floorMod(tick - origin, (long) base) == 0;
            if (!onBase && o.strength < FINE_STRENGTH[d]) continue;
            boolean offSlot = onBase && Math.floorMod(tick - origin, 2L * base) != 0;
            if (offSlot && o.strength < OFF_STRENGTH[d]) continue;
            double w = o.strength;
            if (map.isDownbeat(tick)) w *= 2.0;
            else if (map.isBeat(tick)) w *= 1.6;
            Candidate old = slots.get(tick);
            if (old == null || w > old.weight) slots.put(tick, new Candidate(tick, o, w));
        }
        List<Candidate> list = new ArrayList<>(slots.values());
        if (list.isEmpty()) return new ArrayList<>();
        float[] height = heights(list);

        // 3. Chords on the hardest-hitting on-beat notes.
        double chordThreshold = Double.MAX_VALUE;
        if (CHORD_SHARE[d] > 0) {
            double[] w = new double[list.size()];
            for (int i = 0; i < w.length; i++) w[i] = list.get(i).onset.strength;
            Arrays.sort(w);
            chordThreshold = w[(int) Math.min(w.length - 1, Math.floor(w.length * (1 - CHORD_SHARE[d])))];
        }

        int lanes = LANES[d];
        List<Note> notes = new ArrayList<>(list.size());
        for (int i = 0; i < list.size(); i++) {
            Candidate c = list.get(i);
            int lane = Math.max(0, Math.min(lanes - 1, (int) (height[i] * lanes)));
            int lane2 = -1;
            if (c.onset.strength >= chordThreshold && map.isBeat(c.tick)) {
                int step = d == MEDIUM ? 1 : 2;
                if (lane + step < lanes) lane2 = lane + step;
                else if (lane - step >= 0) lane2 = lane - step;
            }
            long sustain = 0;
            long next = i + 1 < list.size() ? list.get(i + 1).tick : c.tick + 8L * TempoMap.RESOLUTION;
            if (next - c.tick >= SUSTAIN_MIN_GAP[d]) {
                sustain = sustainLength(f, map, c, next);
            }
            notes.add(new Note(c.tick, lane, lane2, sustain));
        }
        return notes;
    }

    /** Holds a note for as long as the sound keeps ringing, up to just before the next note. */
    private static long sustainLength(Features f, TempoMap map, Candidate c, long nextTick) {
        long limit = Math.min(nextTick - 96, c.tick + 8L * TempoMap.RESOLUTION);
        int start = c.onset.frame;
        int n = f.frames();
        float peak = 0;
        for (int k = start; k <= Math.min(n - 1, start + 6); k++) peak = Math.max(peak, f.rms[k]);
        if (peak <= 1e-5f) return 0;
        int endFrame = f.frameAt(map.tickToTime(limit));
        int k = start + 6;
        while (k < endFrame && k < n && f.rms[k] > 0.4f * peak) k++;
        long endTick = Math.min(limit, map.timeToTick(f.timeOf(k)));
        long len = ((endTick - c.tick) / 48) * 48;
        return len >= TempoMap.RESOLUTION ? len : 0;
    }

    /** Two-measure Star Power phrases every ten measures, wherever there are notes to hit. */
    private static void addStarPower(Chart chart, TempoMap map, double duration) {
        List<Note> expert = chart.notes.get(EXPERT);
        long endTick = map.timeToTick(duration);
        for (int m = 6; map.measureStart(m + 2) <= endTick; m += 10) {
            long a = map.measureStart(m);
            long b = map.measureStart(m + 2);
            int inside = 0;
            for (Note n : expert) if (n.tick >= a && n.tick < b) inside++;
            if (inside >= 3) chart.starPower.add(new long[] {a, b - a});
        }
    }
}
