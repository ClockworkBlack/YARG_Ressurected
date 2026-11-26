package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

/**
 * Turns the note onsets of a song into 5-fret guitar charts for four difficulties.
 *
 * The aim is a chart that feels written rather than random:
 *  - pitched notes are preferred over bare drum hits;
 *  - notes that sound the same get the same fret, and higher sounds get higher frets;
 *  - measures that repeat in the music get the same pattern in the chart;
 *  - thick, strummed sounds become chords, ringing notes become sustains;
 *  - Hard, Medium and Easy are thinned-out versions of Expert, not separate guesses.
 */
public final class GuitarCharter {
    private GuitarCharter() {}

    public static final int EASY = 0, MEDIUM = 1, HARD = 2, EXPERT = 3;
    public static final String[] SECTION_NAMES = {"EasySingle", "MediumSingle", "HardSingle", "ExpertSingle"};

    /** Tuning the user can choose. */
    public static final class Options {
        /** 1 = standard. Above 1 keeps only stronger notes (sparser charts); below 1 keeps more. */
        public double threshold = 1.0;
        /** False for instruments that play one note at a time (bass). */
        public boolean chords = true;
        /** True for a separated bass part: notes are held for as long as they stay loud. */
        public boolean bass;

        public Options copy() {
            Options o = new Options();
            o.threshold = threshold;
            o.chords = chords;
            o.bass = bass;
            return o;
        }
    }

    // Per difficulty (Easy..Expert): the shortest regular note spacing in seconds, how short the
    // in-between "extra" notes may get, how strong a sound must be to earn an extra note or an
    // off-beat note, frets used, and the shortest gap (ticks) that earns a sustain.
    private static final double[] BASE_MIN_SEC = {0.55, 0.33, 0.17, 0.085};
    private static final double[] FINE_MIN_SEC = {0.50, 0.28, 0.12, 9.0};
    private static final float[] FINE_STRENGTH = {0.8f, 0.7f, 0.8f, 9f};
    private static final float[] OFF_STRENGTH = {0f, 0f, 0.3f, 0f};
    private static final int[] LANES = {3, 4, 5, 5};

    /** How many frets a difficulty (Easy..Expert) uses. */
    public static int lanes(int difficulty) {
        return LANES[Math.max(0, Math.min(LANES.length - 1, difficulty))];
    }
    private static final int[] SUSTAIN_MIN_GAP = {384, 384, 384, 288};
    // Expert: strength needed on a measure start, a beat, an eighth and a sixteenth. A full band
    // has something on nearly every subdivision; weak in-between sounds are left out so the chart
    // is not a constant stream.
    private static final float[] LEVEL_NEED = {0.10f, 0.10f, 0.20f, 0.45f};
    private static final float MIN_STRENGTH = 0.10f;
    /** How much a bare drum hit counts next to a pitched note. */
    private static final float PERC_WEIGHT = 0.4f;
    private static final float TONAL_SHARE = 0.20f;
    private static final float TONAL_CONF = 0.30f;
    /** Pitch confidence needed to count a change of bass note that has no new attack. */
    private static final float SURE_PITCH = 0.85f;
    /**
     * A bass note this far under the song's usual bass level (-24 dB) is other instruments leaking
     * into the separated bass, in a passage where the bass is not playing.
     */
    private static final float BASS_FLOOR = 0.063f;
    /** A held bass note lasts until it is this much quieter than its start (-15 dB). */
    private static final float BASS_HOLD = 0.178f;
    /** A bass note still within this of its own start (-6 dB) is still ringing at full strength. */
    private static final float STILL_FULL = 0.5f;
    /** On a bass, how much louder a same-note start must get to be a new note: 4 dB. */
    private static final float RETRIGGER_RISE = 1.585f;
    /** MIDI pitch above which sounds are charted as single notes (B3). */
    private static final float CHORD_CEILING = 59.5f;

    public static final class Onset {
        public final double time;
        public final int frame;
        public float strength;
        /** True when a pitched note (not just a drum) starts here. */
        public boolean tonal;
        /** Rough pitch as a MIDI note number. */
        public float height = Float.NaN;
        /** 0..1: how well one single pitch explains the new sound (low for chords). */
        public float conf;
        /** Pitch-class fingerprint of the new sound, unit length. */
        public float[] print;
        /** Group of onsets that sound alike (same note or chord), or -1. */
        public int group = -1;
        /** 0..1: how much of the new energy arrived as distinct pitched tones. */
        public float tonalShare;
        /** How bright the new sound is (low = kick drum, high = cymbal); used when no pitch can be read. */
        public float bright;

        Onset(double time, int frame, float strength) {
            this.time = time;
            this.frame = frame;
            this.strength = strength;
        }
    }

    public static final class Note {
        public final long tick;
        public final int lane;
        /** Second fret of a chord, or -1. */
        public final int lane2;
        public final long sustain;

        public Note(long tick, int lane, int lane2, long sustain) {
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
        /** How many distinct note/chord sounds were recognised. */
        public int soundGroups;
        /** How many measures were recognised as repeats of an earlier measure. */
        public int repeatedMeasures;
        /** Measures charted as a loop heard under other parts (see lockLoops). */
        public int loopMeasures;
        /** Measures charted like the one two or four before them (see linkCycles). */
        public int cycleMeasures;
        /** Repeat family of each measure (-1 for none), for checking how a song was understood. */
        public int[] measureFamily = new int[0];
    }

    // ------------------------------------------------------------------ onsets

    /** Finds the moments where a new sound starts, and describes each one. */
    public static List<Onset> detectOnsets(Features f) {
        return detectOnsets(f, false);
    }

    /**
     * @param groupByPitch true for an instrument that plays one note at a time (a separated
     *                     bass): notes are then told apart by their note name alone, which is
     *                     more dependable than a fingerprint when a sound has few overtones
     */
    public static List<Onset> detectOnsets(Features f, boolean groupByPitch) {
        int n = f.frames();
        double tMean = mean(f.tonal), pMean = mean(f.perc);
        if (tMean < 1e-9) tMean = 1;
        if (pMean < 1e-9) pMean = 1;
        float[] env = new float[n];
        for (int i = 0; i < n; i++) env[i] = (float) (f.tonal[i] / tMean + PERC_WEIGHT * f.perc[i] / pMean);

        int half = Math.max(4, (int) Math.round(f.fps * 0.4));
        int near = Math.max(2, (int) Math.round(f.fps * 0.035));
        double[] prefix = new double[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + env[i];
        double globalMean = n > 0 ? prefix[n] / n : 0;
        float[] scratch = new float[2 * half + 1];
        List<Onset> found = new ArrayList<>();
        for (int i = 1; i < n - 1; i++) {
            float v = env[i];
            if (v <= 0) continue;
            boolean peak = true;
            for (int k = Math.max(0, i - near); k <= Math.min(n - 1, i + near) && peak; k++) {
                if (env[k] > v || (env[k] == v && k < i)) peak = false;
            }
            if (!peak) continue;
            int a = Math.max(0, i - half);
            int b = Math.min(n, i + half + 1);
            int len = b - a;
            System.arraycopy(env, a, scratch, 0, len);
            Arrays.sort(scratch, 0, len);
            double threshold = scratch[len / 2] + 0.35 * (prefix[b] - prefix[a]) / len + 0.05 * globalMean;
            if (v <= threshold) continue;
            // The sound actually began where the rise started, a frame or two before the peak.
            int rise = i;
            while (rise > 0 && i - rise < 2 && env[rise - 1] >= 0.3f * v) rise--;
            Onset o = new Onset(f.timeOf(rise), i, (float) (v - threshold));
            describe(f, o, tMean, pMean);
            if (groupByPitch && !o.tonal) settledPitch(f, o);
            found.add(o);
        }
        if (found.isEmpty()) return found;

        // Strength is judged against the song's solid hits, with some allowance for quiet
        // passages so an intro or breakdown is not left empty.
        int count = found.size();
        float[] sorted = new float[count];
        for (int i = 0; i < count; i++) sorted[i] = found.get(i).strength;
        Arrays.sort(sorted);
        float global = Math.max(1e-6f, sorted[(int) (count * 0.9)]);
        float[] local = new float[count];
        int lo = 0, hi = 0;
        for (int i = 0; i < count; i++) {
            double t = found.get(i).time;
            while (found.get(lo).time < t - 10) lo++;
            while (hi < count && found.get(hi).time <= t + 10) hi++;
            float[] w = new float[hi - lo];
            for (int k = lo; k < hi; k++) w[k - lo] = found.get(k).strength;
            Arrays.sort(w);
            local[i] = w[(int) (w.length * 0.9)];
        }
        List<Onset> out = new ArrayList<>(count);
        for (int i = 0; i < count; i++) {
            Onset o = found.get(i);
            float ref = Math.max(0.35f * global, 0.6f * global + 0.4f * local[i]);
            // Anything this faint next to the song's solid hits is noise, not a note.
            if (o.strength < MIN_STRENGTH * global) continue;
            o.strength = o.strength / ref;
            out.add(o);
        }
        if (groupByPitch) {
            // By note name (C, C sharp, ...): pitch readings of a bass are often an octave out,
            // but rarely the wrong note. Clear octave jumps are told apart again when frets
            // are handed out.
            // Songs are not always tuned to A = 440 Hz; a bass that sits between two note names
            // would otherwise be read as one and then the other. The song's own tuning comes from
            // where its bass notes fall between the note names.
            double tuning = tuning(out);
            for (Onset o : out) if (o.tonal) o.group = Math.floorMod(Math.round(o.height - tuning), 12);
            out = dropRetriggers(f, out);
        } else {
            groupBySound(out);
        }
        return out;
    }

    /**
     * A long bass note (a trap 808 above all) pumps and buzzes while it rings, and each small swell
     * reads as a new note start. On a bass that plays one note at a time, a start on the same note
     * name as the note before only counts when the sound gets clearly louder again
     * ({@link #RETRIGGER_RISE}) from the dip just before it; otherwise the note is still ringing.
     * A change of note (a slide to a new pitch) counts when the pitch reading is sure of it; the
     * heavy distortion on an 808 often makes the reading jump to the note's fifth or octave.
     * Starts far below the song's usual bass level are leakage from other instruments.
     */
    private static List<Onset> dropRetriggers(Features f, List<Onset> onsets) {
        int n = f.frames();
        int before = Math.max(2, (int) Math.round(f.fps * 0.08));
        int after = Math.max(2, (int) Math.round(f.fps * 0.06));
        float[] levels = new float[n];
        int sounding = 0;
        for (int k = 0; k < n; k++) if (f.rms[k] > 0) levels[sounding++] = f.rms[k];
        Arrays.sort(levels, 0, sounding);
        float usual = sounding > 0 ? levels[(int) (sounding * 0.9)] : 0;
        List<Onset> kept = new ArrayList<>(onsets.size());
        Onset last = null;
        float lastPeak = 0;
        for (Onset o : onsets) {
            int at = Math.min(n - 1, Math.max(0, o.frame));
            float loudest = 0;
            for (int k = at; k <= Math.min(n - 1, at + after); k++) loudest = Math.max(loudest, f.rms[k]);
            if (loudest < BASS_FLOOR * usual) continue;
            // An unsure pitch reading is not enough to call it a new note without a new attack.
            // (Only notes that were kept count as "the note before".)
            boolean sameNote = last != null && (!o.tonal || !last.tonal || o.group == last.group || o.conf < SURE_PITCH);
            if (sameNote) {
                int p = Math.min(n - 1, Math.max(0, o.frame));
                float dip = Float.MAX_VALUE, peak = 0;
                for (int k = Math.max(0, p - before); k <= p; k++) dip = Math.min(dip, f.rms[k]);
                for (int k = p; k <= Math.min(n - 1, p + after); k++) peak = Math.max(peak, f.rms[k]);
                // A plucked string has already faded by its next pick, so a re-pick on the same
                // note counts even when it barely gets louder. Only a note that is still at full
                // strength (an 808, a held synth bass) needs a clear new attack.
                boolean stillFull = dip >= STILL_FULL * lastPeak;
                if (stillFull && peak < RETRIGGER_RISE * dip) continue;
            }
            kept.add(o);
            last = o;
            lastPeak = loudest;
        }
        return kept;
    }

    /**
     * How far the song's notes sit from A = 440 Hz tuning, in semitones (-0.5 to 0.5): the
     * average of where each confident pitch falls between two note names, taken on a circle so
     * that +0.45 and -0.45 count as close together.
     */
    static double tuning(List<Onset> onsets) {
        double x = 0, y = 0;
        for (Onset o : onsets) {
            if (!o.tonal || Float.isNaN(o.height)) continue;
            double angle = 2 * Math.PI * (o.height - Math.round(o.height));
            x += o.conf * Math.cos(angle);
            y += o.conf * Math.sin(angle);
        }
        if (x == 0 && y == 0) return 0;
        return Math.atan2(y, x) / (2 * Math.PI);
    }

    private static double mean(float[] x) {
        double s = 0;
        for (float v : x) s += v;
        return x.length > 0 ? s / x.length : 0;
    }

    /** Fills in whether the onset is a pitched note, its rough pitch, and its fingerprint. */
    private static void describe(Features f, Onset o, double tMean, double pMean) {
        int n = f.frames();
        int p = o.frame;
        double t = 0, all = 0;
        for (int k = Math.max(0, p - 1); k <= Math.min(n - 1, p + 1); k++) {
            t += f.tonal[k] / tMean;
            all += f.tonal[k] / tMean + f.perc[k] / pMean;
        }
        double share = all > 0 ? t / all : 0;
        o.tonalShare = (float) share;
        o.bright = f.bright[p];

        // The pitch window is long, so a new note is only fully "in" it some 60 ms after it
        // starts; that is where its pitch reads best (measured in ChartEval).
        float[] print = new float[12];
        float bestWeight = 0;
        for (int k = p + 3; k <= Math.min(n - 1, p + 8); k++) {
            float sum = 0;
            for (int c = 0; c < 12; c++) {
                float v = f.toneChroma[k * 12 + c];
                print[c] += v;
                sum += v;
            }
            float w = sum * f.pitchConf[k];
            if (!Float.isNaN(f.pitch[k]) && w > bestWeight) {
                bestWeight = w;
                o.height = f.pitch[k];
                o.conf = f.pitchConf[k];
            }
        }
        double norm = 0;
        for (float v : print) norm += v * v;
        norm = Math.sqrt(norm);
        if (norm > 1e-6 && share >= TONAL_SHARE && o.conf >= TONAL_CONF && !Float.isNaN(o.height)) {
            for (int c = 0; c < 12; c++) print[c] = (float) (print[c] / norm);
            o.print = print;
            o.tonal = true;
        }
    }

    /**
     * A bass note that starts with a click (a trap 808 with a kick on it) has no clear pitch at
     * first; its tone settles a little later. Reads the pitch from there instead, up to about a
     * quarter of a second in, so the note can get its own fret and be held.
     */
    private static void settledPitch(Features f, Onset o) {
        int n = f.frames();
        int from = o.frame + 9;
        int to = Math.min(n - 1, o.frame + (int) Math.round(f.fps * 0.23));
        float[] print = new float[12];
        float bestWeight = 0;
        float height = Float.NaN, conf = 0;
        for (int k = from; k <= to; k++) {
            float sum = 0;
            for (int c = 0; c < 12; c++) {
                float v = f.toneChroma[k * 12 + c];
                print[c] += v;
                sum += v;
            }
            float w = sum * f.pitchConf[k];
            if (!Float.isNaN(f.pitch[k]) && w > bestWeight) {
                bestWeight = w;
                height = f.pitch[k];
                conf = f.pitchConf[k];
            }
        }
        double norm = 0;
        for (float v : print) norm += v * v;
        norm = Math.sqrt(norm);
        if (norm > 1e-6 && conf >= TONAL_CONF && !Float.isNaN(height)) {
            for (int c = 0; c < 12; c++) print[c] = (float) (print[c] / norm);
            o.print = print;
            o.height = height;
            o.conf = conf;
            o.tonal = true;
        }
    }

    /**
     * Groups pitched onsets that sound alike. Two onsets with nearly the same fingerprint are
     * the same note or chord played again, and will later share a fret.
     */
    private static void groupBySound(List<Onset> onsets) {
        final double join = 0.86;
        List<float[]> sums = new ArrayList<>();
        for (Onset o : onsets) {
            if (!o.tonal) continue;
            int best = -1;
            double bestSim = join;
            for (int g = 0; g < sums.size(); g++) {
                double sim = Features.cosine(o.print, sums.get(g));
                if (sim >= bestSim) {
                    bestSim = sim;
                    best = g;
                }
            }
            if (best < 0) {
                sums.add(o.print.clone());
                best = sums.size() - 1;
            } else {
                float[] s = sums.get(best);
                for (int c = 0; c < 12; c++) s[c] += o.print[c];
            }
            o.group = best;
        }
        // Second pass against the settled group centres, so early onsets are not stuck with a
        // first impression.
        for (Onset o : onsets) {
            if (!o.tonal) continue;
            int best = o.group;
            double bestSim = Features.cosine(o.print, sums.get(best));
            for (int g = 0; g < sums.size(); g++) {
                double sim = Features.cosine(o.print, sums.get(g));
                if (sim > bestSim + 0.02) {
                    bestSim = sim;
                    best = g;
                }
            }
            o.group = best;
        }
    }

    // ------------------------------------------------------------------ charting

    /** One chart note while it is being worked out. */
    private static final class Pick {
        final long tick;
        final Onset onset;      // null when the note exists only because the pattern repeats
        final float strength;
        final int level;        // 0 measure start, 1 beat, 2 eighth, 3 sixteenth
        int lane = -1;
        int lane2 = -1;
        long sustain;
        /** Which distinct sound of its phrase this note is (sound group, split by octave where clear). */
        int sound = -1;

        Pick(long tick, Onset onset, float strength, int level) {
            this.tick = tick;
            this.onset = onset;
            this.strength = strength;
            this.level = level;
        }

        boolean tonal() {
            return onset != null && onset.tonal && onset.group >= 0;
        }
    }

    private static final class Context {
        Features f;
        TempoMap map;
        double beatSec;
        long origin;
        long measureTicks;
        /** Measures given to a loop by {@link #lockLoops}. */
        int loopMeasures;
        /** Measures linked to the one a cycle before them by {@link #linkCycles}. */
        int cycleMeasures;
        /** Repeat family of each measure (index = measure number), -1 for none. */
        int[] family;
        int[] familySize;
        int groups;
        /** Below this "one pitch explains it" reading, a sound is treated as a chord. */
        float chordLine;
        float songLow, songHigh;
        double threshold;
        boolean chords = true;
        boolean bass;
    }

    public static Chart build(Features f, List<Onset> onsets, TempoMap map, double bpm, Options options) {
        Chart chart = new Chart();
        chart.onsetCount = onsets.size();
        Context cx = new Context();
        cx.f = f;
        cx.map = map;
        cx.beatSec = 60.0 / Math.max(30.0, bpm);
        cx.origin = map.origin();
        cx.measureTicks = map.measureTicks();
        cx.threshold = options == null ? 1.0 : Math.max(0.3, Math.min(3.0, options.threshold));
        cx.chords = options == null || options.chords;
        cx.bass = options != null && options.bass;
        summariseGroups(cx, onsets);
        chart.soundGroups = cx.groups;

        List<Pick> expert = pickExpert(cx, onsets, chart);
        for (int d = EASY; d <= EXPERT; d++) {
            List<Pick> picks = d == EXPERT ? expert : reduce(cx, expert, d);
            assignFrets(cx, picks, d);
            addSustains(cx, picks, d);
            agreeOnSustains(cx, picks);
            List<Note> notes = new ArrayList<>(picks.size());
            for (int i = 0; i < picks.size(); i++) {
                Pick p = picks.get(i);
                long sustain = p.sustain;
                if (i + 1 < picks.size()) sustain = Math.min(sustain, Math.max(0, picks.get(i + 1).tick - p.tick - 96));
                if (sustain < TempoMap.RESOLUTION) sustain = 0;
                notes.add(new Note(p.tick, p.lane, p.lane2, sustain));
            }
            chart.notes.add(notes);
        }
        addStarPower(chart, map, f.duration);
        return chart;
    }

    /** Song-wide pitch range and the line between single notes and chords. */
    private static void summariseGroups(Context cx, List<Onset> onsets) {
        List<Float> heights = new ArrayList<>();
        List<Float> confs = new ArrayList<>();
        java.util.Set<Integer> distinct = new java.util.HashSet<>();
        for (Onset o : onsets) {
            if (!o.tonal || o.group < 0) continue;
            distinct.add(o.group);
            cx.groups = distinct.size();
            heights.add(o.height);
            confs.add(o.conf);
        }
        Collections.sort(heights);
        Collections.sort(confs);
        cx.songLow = heights.isEmpty() ? 40 : heights.get((int) (heights.size() * 0.1));
        cx.songHigh = heights.isEmpty() ? 76 : heights.get((int) (heights.size() * 0.9));
        // A single note is explained well by one pitch; a chord is not. The dividing line
        // follows the song, within limits, because a dense mix lowers every reading.
        float mid = confs.isEmpty() ? 0.55f : confs.get(confs.size() / 2);
        cx.chordLine = Math.max(0.45f, Math.min(0.60f, mid));
    }

    /**
     * Pitch of one sound within a phrase. Readings of the same sound can disagree by an octave
     * or a fifth, so take the pitch class most of them agree on, and of those the lower end,
     * which is usually the root.
     */
    private static float pitchOf(List<Pick> members) {
        int[] votes = new int[12];
        for (Pick p : members) votes[Math.floorMod(Math.round(p.onset.height), 12)]++;
        int pc = 0;
        for (int k = 1; k < 12; k++) if (votes[k] > votes[pc]) pc = k;
        float[] h = new float[votes[pc]];
        int at = 0;
        for (Pick p : members) if (Math.floorMod(Math.round(p.onset.height), 12) == pc) h[at++] = p.onset.height;
        Arrays.sort(h);
        return h[h.length / 4];
    }

    private static float medianConf(List<Pick> members) {
        float[] c = new float[members.size()];
        for (int i = 0; i < c.length; i++) c[i] = members.get(i).onset.conf;
        Arrays.sort(c);
        return c[c.length / 2];
    }

    private static boolean isChord(Context cx, List<Pick> members) {
        return medianConf(members) < cx.chordLine;
    }

    /** Smallest musical grid (in ticks) whose notes are at least {@code minSec} apart. */
    private static int gridFor(double beatSec, double minSec) {
        int grid = 48;
        while (grid < 768 && beatSec * grid / TempoMap.RESOLUTION < minSec) grid *= 2;
        return grid;
    }

    private static int levelOf(Context cx, long tick) {
        long rel = Math.floorMod(tick - cx.origin, cx.measureTicks);
        if (rel == 0) return 0;
        if (rel % 192 == 0) return 1;
        if (rel % 96 == 0) return 2;
        return 3;
    }

    /**
     * Chooses Expert's notes. Measures that repeat (same rhythm, same harmony) are pooled first,
     * so a riff played eight times gets one pattern, decided by all eight hearings, instead of
     * eight slightly different guesses.
     */
    private static List<Pick> pickExpert(Context cx, List<Onset> onsets, Chart chart) {
        TempoMap map = cx.map;
        int grid = Math.min(TempoMap.RESOLUTION, gridFor(cx.beatSec, BASE_MIN_SEC[EXPERT]));
        int slots = (int) (cx.measureTicks / grid);

        TreeMap<Long, Onset> bySlot = new TreeMap<>();
        long lastTick = 0;
        for (Onset o : onsets) {
            long tick = Math.round((map.timeToTickExact(o.time) - cx.origin) / grid) * grid + cx.origin;
            if (tick < 0) continue;
            Onset old = bySlot.get(tick);
            if (old == null || weight(cx, o, tick) > weight(cx, old, tick)) bySlot.put(tick, o);
            lastTick = Math.max(lastTick, tick);
        }
        int measures = Math.max(0, map.measureOf(lastTick)) + 1;
        Onset[][] grids = new Onset[measures][slots];
        for (Map.Entry<Long, Onset> e : bySlot.entrySet()) {
            int m = map.measureOf(e.getKey());
            if (m < 0) continue;
            grids[m][(int) ((e.getKey() - map.measureStart(m)) / grid)] = e.getValue();
        }

        findRepeats(cx, grids, measures, slots);
        int repeated = 0;
        for (int m = 0; m < measures; m++) if (cx.family[m] >= 0 && cx.familySize[cx.family[m]] > 1) repeated++;
        chart.repeatedMeasures = repeated;
        chart.measureFamily = cx.family.clone();
        chart.loopMeasures = cx.loopMeasures;
        chart.cycleMeasures = cx.cycleMeasures;

        // Pooled strength of every slot of every family.
        int families = cx.familySize.length;
        float[][] pooled = new float[families][slots];
        float[][] present = new float[families][slots];
        for (int m = 0; m < measures; m++) {
            int fam = cx.family[m];
            if (fam < 0) continue;
            for (int s = 0; s < slots; s++) {
                if (grids[m][s] == null) continue;
                pooled[fam][s] += grids[m][s].strength;
                present[fam][s] += 1f;
            }
        }

        List<Pick> picks = new ArrayList<>();
        // Pickup notes before the first full measure stand on their own.
        for (Map.Entry<Long, Onset> e : bySlot.headMap(cx.origin).entrySet()) {
            int level = levelOf(cx, e.getKey());
            if (e.getValue().strength >= LEVEL_NEED[Math.max(1, level)] * cx.threshold) {
                picks.add(new Pick(e.getKey(), e.getValue(), e.getValue().strength, Math.max(1, level)));
            }
        }
        for (int m = 0; m < measures; m++) {
            int fam = cx.family[m];
            if (fam < 0) continue;
            int size = cx.familySize[fam];
            for (int s = 0; s < slots; s++) {
                if (present[fam][s] == 0) continue;
                long tick = map.measureStart(m) + (long) s * grid;
                int level = levelOf(cx, tick);
                float strength = pooled[fam][s] / size;
                if (strength < LEVEL_NEED[level] * cx.threshold) continue;
                if (size > 1 && present[fam][s] * 2 < size) continue;
                picks.add(new Pick(tick, grids[m][s], strength, level));
            }
        }
        return picks;
    }

    private static double weight(Context cx, Onset o, long tick) {
        double w = o.strength;
        if (cx.map.isDownbeat(tick)) return w * 2.0;
        if (cx.map.isBeat(tick)) return w * 1.6;
        if (Math.floorMod(tick - cx.origin, 96L) == 0) return w * 1.25;
        return w;
    }

    /** Same note motion through the bar (cosine of the sixteenth-by-sixteenth pitch classes) that makes two measures repeats. */
    static double MOTION_MIN = Double.parseDouble(System.getProperty("yarg.motionMin", "0.80"));

    /**
     * Sorts measures into families that share a rhythm and a harmony. Each measure is compared
     * with the first measure of every family so far.
     */
    private static void findRepeats(Context cx, Onset[][] grids, int measures, int slots) {
        TempoMap map = cx.map;
        Features f = cx.f;
        int beats = map.beatsPerMeasure;
        float[][] rhythm = new float[measures][slots];
        float[][] harmony = new float[measures][beats * 12];
        // How the notes move through the measure: the pitch classes of each sixteenth. A riff
        // looping under the singing keeps this the same bar after bar even when other sounds
        // leaking into the separated track add stray note starts that change the rhythm grid.
        int steps = beats * 4;
        float[][] motion = new float[measures][steps * 12];
        boolean[] empty = new boolean[measures];
        for (int m = 0; m < measures; m++) {
            boolean any = false;
            for (int s = 0; s < slots; s++) {
                if (grids[m][s] != null) {
                    rhythm[m][s] = Math.min(1.5f, grids[m][s].strength);
                    any = true;
                }
            }
            empty[m] = !any;
            for (int q = 0; q < steps; q++) {
                long tick = map.measureStart(m) + (long) q * TempoMap.RESOLUTION / 4;
                int from = f.frameAt(map.tickToTime(tick));
                int to = f.frameAt(map.tickToTime(tick + TempoMap.RESOLUTION / 4));
                float[] c = f.chromaBetween(from, Math.max(from + 1, to));
                float mean = 0;
                for (float v : c) mean += v / 12f;
                for (int k = 0; k < 12; k++) motion[m][q * 12 + k] = c[k] - mean;
            }
            for (int b = 0; b < beats; b++) {
                long tick = map.measureStart(m) + (long) b * TempoMap.RESOLUTION;
                int from = f.frameAt(map.tickToTime(tick));
                int to = f.frameAt(map.tickToTime(tick + TempoMap.RESOLUTION));
                float[] c = f.chromaBetween(from, to);
                float mean = 0;
                for (float v : c) mean += v / 12f;
                for (int k = 0; k < 12; k++) harmony[m][b * 12 + k] = c[k] - mean;
            }
        }
        cx.family = new int[measures];
        List<Integer> first = new ArrayList<>();
        List<Integer> sizes = new ArrayList<>();
        for (int m = 0; m < measures; m++) {
            if (empty[m]) {
                cx.family[m] = -1;
                continue;
            }
            int best = -1;
            double bestScore = 0;
            for (int g = 0; g < first.size(); g++) {
                int rep = first.get(g);
                double r = Features.cosine(rhythm[m], rhythm[rep]);
                double h = Features.cosine(harmony[m], harmony[rep]);
                // Same rhythm is not enough: the same notes have to be played. Where both
                // measures have a pitched note on a slot, it must be the same sound.
                int shared = 0, same = 0;
                for (int s = 0; s < slots; s++) {
                    Onset a = grids[m][s], b = grids[rep][s];
                    if (a == null || b == null || !a.tonal || !b.tonal) continue;
                    shared++;
                    if (a.group == b.group) same++;
                }
                boolean sameNotes = shared >= 2 ? same >= 0.75 * shared : h >= 0.92;
                double mo = Features.cosine(motion[m], motion[rep]);
                boolean byOnsets = r >= 0.80 && h >= 0.80 && sameNotes;
                // Or: the notes move through the bar the same way, sixteenth by sixteenth
                // (Not for one note at a time: a bass line can move like another and play other notes)
                boolean byMotion = cx.chords && MOTION_MIN > 0 && mo >= MOTION_MIN && h >= 0.85 && r >= 0.55;
                double score = r + h + mo;
                if ((byOnsets || byMotion) && score > bestScore) {
                    bestScore = score;
                    best = g;
                }
            }
            if (best < 0) {
                first.add(m);
                sizes.add(1);
                best = first.size() - 1;
            } else {
                sizes.set(best, sizes.get(best) + 1);
            }
            cx.family[m] = best;
        }
        cx.familySize = new int[sizes.size()];
        for (int g = 0; g < cx.familySize.length; g++) cx.familySize[g] = sizes.get(g);
        // Loops are a guitar or keys thing: a bass line or a sung melody can share a loop's
        // rhythm and key and still play other notes, which the lock would then chart wrongly
        // A bass line gets the lock too, on stricter terms (see lockLoops): it can share a loop's
        // rhythm and key and still play other notes
        if (cx.chords && PERIOD_MIN > 0) linkCycles(cx, motion, harmony, measures);
        if (LOOP_PRESENCE > 0 && (cx.chords || BASS_LOCK)) lockLoops(cx, motion, measures, steps);
    }

    static double PERIOD_MIN = Double.parseDouble(System.getProperty("yarg.periodMin", "0.80"));

    /**
     * Songs built on two- or four-bar cycles (a chord progression going round, as in most pop):
     * a measure that sounds like the one two or four measures before it, in how its notes move
     * and in its harmony, plays the same part. Families are otherwise only compared with their
     * first measure, which misses a cycle whose measures each drift a little from that first one.
     */
    private static void linkCycles(Context cx, float[][] motion, float[][] harmony, int measures) {
        int linked = 0;
        for (int m = 2; m < measures; m++) {
            for (int length : new int[] {4, 2}) {
                int back = m - length;
                if (back < 0 || cx.family[back] < 0 || cx.family[m] == cx.family[back]) continue;
                double mo = Features.cosine(motion[m], motion[back]);
                double h = Features.cosine(harmony[m], harmony[back]);
                if (mo < PERIOD_MIN || h < 0.85) continue;
                if (cx.family[m] >= 0) cx.familySize[cx.family[m]]--;
                cx.family[m] = cx.family[back];
                cx.familySize[cx.family[m]]++;
                linked++;
                break;
            }
        }
        cx.cycleMeasures = linked;
    }

    /** A loop: a family that plays at least this many measures in a row. */
    static final int LOOP_RUN = 4;
    /** Share of a loop's notes that must still be heard (among the loudest) for a measure to be that loop. */
    static double LOOP_PRESENCE = Double.parseDouble(System.getProperty("yarg.loopPresence", "0.6"));

    static int[] LOOP_LENGTHS = java.util.Arrays.stream(System.getProperty("yarg.loopLengths", "1,2,4").split(",")).mapToInt(Integer::parseInt).toArray();
    static int LOOP_CYCLES = Integer.getInteger("yarg.loopCycles", 5);
    static double BASS_TOLERANT = Double.parseDouble(System.getProperty("yarg.bassTolerant", "0.6"));
    static boolean BASS_LOCK = !Boolean.getBoolean("yarg.noBassLock");
    static double BASS_PRESENCE = Double.parseDouble(System.getProperty("yarg.bassPresence", "0.6"));

    /**
     * Loop lock. A riff that loops through a song (an R&amp;B guitar or keys figure under the
     * singing) is heard alone at first, then with other parts on top: the singing leaking into
     * the separated track, pads, a second guitar. Its measures then stop looking alike and the
     * chart would turn the one riff into a different pattern every measure. Once a family has
     * played {@link #LOOP_RUN} measures in a row, any later or earlier measure in which the
     * riff's notes can still be heard, sixteenth by sixteenth, is counted as that riff.
     */
    private static void lockLoops(Context cx, float[][] motion, int measures, int steps) {
        int families = cx.familySize.length;
        // Families that play a run of measures in a row
        // A loop may be one, two or four measures long (A A A A, A B A B, A B C D A B C D): each
        // measure the same as the one a loop-length before it, for a run of measures
        boolean[] loop = new boolean[families];
        for (int length : LOOP_LENGTHS) {
            int run = 0;
            for (int m = length; m < measures; m++) {
                run = cx.family[m] >= 0 && cx.family[m] == cx.family[m - length] ? run + 1 : 0;
                // Longer loops must come round at least four times: a verse's chord sequence heard twice is not a loop
                if (run >= (length == 1 ? LOOP_RUN - 1 : LOOP_CYCLES * length)) {
                    for (int k = 0; k < length; k++) if (cx.family[m - k] >= 0) loop[cx.family[m - k]] = true;
                }
            }
        }
        // A bass line varies its loop with fills, so runs keep breaking. For one-note parts a loop
        // also counts when, over 16 measures, most are the same as the one a loop-length before.
        if (!cx.chords && BASS_TOLERANT > 0) {
            for (int length : new int[] {1, 2}) {
                for (int from = length; from + 16 <= measures; from++) {
                    int same = 0;
                    for (int m = from; m < from + 16; m++) if (cx.family[m] >= 0 && cx.family[m] == cx.family[m - length]) same++;
                    if (same < BASS_TOLERANT * 16) continue;
                    for (int m = from; m < from + 16; m++) if (cx.family[m] >= 0 && cx.family[m] == cx.family[m - length]) loop[cx.family[m]] = true;
                }
            }
        }
        // One note at a time (bass): the loop's note must be the loudest thing there, in more of the measure
        int allowed = cx.chords ? 2 : 0;
        double need = cx.chords ? LOOP_PRESENCE : Math.max(LOOP_PRESENCE, BASS_PRESENCE);
        // Each loop's note in each sixteenth: the pitch class that stands out most across its measures
        int[][] note = new int[families][];
        boolean[][] strong = new boolean[families][];
        for (int g = 0; g < families; g++) {
            if (!loop[g]) continue;
            float[] sum = new float[steps * 12];
            for (int m = 0; m < measures; m++) if (cx.family[m] == g) for (int k = 0; k < sum.length; k++) sum[k] += motion[m][k];
            note[g] = new int[steps];
            strong[g] = new boolean[steps];
            float[] peak = new float[steps];
            for (int q = 0; q < steps; q++) {
                int best = 0;
                for (int k = 1; k < 12; k++) if (sum[q * 12 + k] > sum[q * 12 + best]) best = k;
                note[g][q] = best;
                peak[q] = sum[q * 12 + best];
            }
            float[] sorted = peak.clone();
            Arrays.sort(sorted);
            float median = sorted[steps / 2];
            for (int q = 0; q < steps; q++) strong[g][q] = peak[q] > 0 && peak[q] >= 0.5f * median;
        }
        int moved = 0;
        for (int m = 0; m < measures; m++) {
            int own = cx.family[m];
            if (own >= 0 && loop[own]) continue;
            int best = -1;
            double bestShare = need;
            for (int g = 0; g < families; g++) {
                if (!loop[g]) continue;
                int heard = 0, count = 0;
                for (int q = 0; q < steps; q++) {
                    if (!strong[g][q]) continue;
                    count++;
                    float riff = motion[m][q * 12 + note[g][q]];
                    int louder = 0;
                    for (int k = 0; k < 12; k++) if (motion[m][q * 12 + k] > riff) louder++;
                    if (louder <= allowed && riff > 0) heard++;
                }
                double share = count == 0 ? 0 : (double) heard / count;
                if (count >= steps / 4 && share >= bestShare) {
                    bestShare = share;
                    best = g;
                }
            }
            if (best < 0) continue;
            if (own >= 0) cx.familySize[own]--;
            cx.family[m] = best;
            cx.familySize[best]++;
            moved++;
        }
        cx.loopMeasures = moved;
    }

    /**
     * Thins Expert down for an easier level: the notes on that level's regular grid stay, and a
     * few strong in-between notes are kept where there is room for them.
     */
    private static List<Pick> reduce(Context cx, List<Pick> expert, int d) {
        boolean four = cx.map.beatsPerMeasure == 4;
        int base = gridFor(cx.beatSec, BASE_MIN_SEC[d]);
        if (!four && base > TempoMap.RESOLUTION) base = (int) cx.measureTicks;
        int fine = four || base <= TempoMap.RESOLUTION ? base / 2 : TempoMap.RESOLUTION;
        boolean fineOk = fine >= 48 && cx.beatSec * fine / TempoMap.RESOLUTION >= FINE_MIN_SEC[d];

        TreeMap<Long, Pick> kept = new TreeMap<>();
        List<Pick> rest = new ArrayList<>();
        for (Pick p : expert) {
            long rel = Math.floorMod(p.tick - cx.origin, cx.measureTicks);
            boolean onBase = rel % base == 0;
            boolean offSlot = four && onBase && rel % (2L * base) != 0;
            if (onBase && !(offSlot && p.strength < OFF_STRENGTH[d] * cx.threshold)) {
                kept.put(p.tick, p);
            } else {
                rest.add(p);
            }
        }
        Collections.sort(rest, new Comparator<Pick>() {
            @Override
            public int compare(Pick a, Pick b) {
                int c = Float.compare(b.strength, a.strength);
                return c != 0 ? c : Long.compare(a.tick, b.tick);
            }
        });
        for (Pick p : rest) {
            Long before = kept.floorKey(p.tick);
            Long after = kept.ceilingKey(p.tick);
            long dist = Long.MAX_VALUE;
            if (before != null) dist = Math.min(dist, p.tick - before);
            if (after != null) dist = Math.min(dist, after - p.tick);
            boolean onFine = Math.floorMod(p.tick - cx.origin, (long) fine) == 0;
            if (dist >= base || (fineOk && onFine && dist >= fine && p.strength >= FINE_STRENGTH[d] * cx.threshold)) {
                kept.put(p.tick, p);
            }
        }
        List<Pick> out = new ArrayList<>(kept.size());
        for (Pick p : kept.values()) out.add(new Pick(p.tick, p.onset, p.strength, p.level));
        return out;
    }

    /**
     * Gives every note a fret. Within each four-measure phrase the distinct sounds are lined up
     * from lowest to highest and spread over the frets, so the same sound always lands on the
     * same fret and a higher sound never lands on a lower one. Bare drum hits repeat the fret of
     * the note before them, the way a strummed chord repeats.
     */
    private static void assignFrets(Context cx, List<Pick> picks, int d) {
        int lanes = LANES[d];
        // Cut the song into four-measure phrases.
        List<List<Pick>> phrases = new ArrayList<>();
        List<Integer> phraseBlock = new ArrayList<>();
        int i = 0;
        while (i < picks.size()) {
            int block = Math.max(0, cx.map.measureOf(picks.get(i).tick)) / 4;
            int j = i;
            while (j < picks.size() && Math.max(0, cx.map.measureOf(picks.get(j).tick)) / 4 == block) j++;
            phrases.add(picks.subList(i, j));
            phraseBlock.add(block);
            i = j;
        }
        // Phrases built from the same measures (a verse coming round again) are fretted together,
        // as one pool of notes, so the repeat comes out identical and each sound's pitch is
        // judged from more hearings.
        int count = phrases.size();
        int[] pool = new int[count];
        for (int a = 0; a < count; a++) pool[a] = a;
        for (int a = 0; a < count; a++) {
            for (int b = a + 1; b < count; b++) {
                if (pool[b] != b) continue;
                int same = 0, known = 0;
                for (int m = 0; m < 4; m++) {
                    int ma = phraseBlock.get(a) * 4 + m, mb = phraseBlock.get(b) * 4 + m;
                    if (ma >= cx.family.length || mb >= cx.family.length) continue;
                    if (cx.family[ma] < 0 && cx.family[mb] < 0) continue;
                    known++;
                    if (cx.family[ma] == cx.family[mb]) same++;
                }
                if (known >= 2 && same * 2 >= known && same >= 2) pool[b] = pool[a];
            }
        }
        for (int a = 0; a < count; a++) {
            if (pool[a] != a) continue;
            List<Pick> pooled = new ArrayList<>();
            for (int b = a; b < count; b++) if (pool[b] == a) pooled.addAll(phrases.get(b));
            fretPhrase(cx, pooled, lanes, d);
        }
        // Notes without a pitch of their own (drum hits, strums the pitch reader could not
        // make out) repeat the fret of the note just before them, the way a strummed chord
        // repeats, as long as a pitched note was heard recently.
        Pick prev = null;
        long anchor = Long.MIN_VALUE / 2;
        for (Pick p : picks) {
            if (p.lane >= 0) {
                anchor = p.tick;
                prev = p;
            } else if (prev != null && p.tick - prev.tick <= TempoMap.RESOLUTION && p.tick - anchor <= 8L * TempoMap.RESOLUTION) {
                copyFret(prev, p, d);
                prev = p;
            }
        }
        // Stretches with no readable pitch at all (drum breaks, very dense mixes) fall back to
        // brightness: dull thuds on the low frets, bright cracks on the high ones. The same
        // kind of hit lands on the same fret all through the song.
        List<Float> values = new ArrayList<>();
        for (Pick p : picks) if (p.lane < 0 && p.onset != null) values.add(p.onset.bright);
        if (!values.isEmpty()) {
            Collections.sort(values);
            float[] cut = new float[lanes - 1];
            for (int c = 0; c < cut.length; c++) cut[c] = values.get(Math.min(values.size() - 1, (c + 1) * values.size() / lanes));
            for (Pick p : picks) {
                if (p.lane >= 0 || p.onset == null) continue;
                int lane = 0;
                while (lane < cut.length && p.onset.bright > cut[lane]) lane++;
                p.lane = lane;
            }
        }
        // Whatever is left exists only because its measure repeats; borrow from a neighbour.
        prev = null;
        for (Pick p : picks) {
            if (p.lane < 0 && prev != null) copyFret(prev, p, d);
            if (p.lane >= 0) prev = p;
        }
        Pick next = null;
        for (int k = picks.size() - 1; k >= 0; k--) {
            Pick p = picks.get(k);
            if (p.lane < 0 && next != null) copyFret(next, p, d);
            if (p.lane >= 0) next = p;
        }
        for (Pick p : picks) if (p.lane < 0) p.lane = lanes / 2;
        agreeOnFrets(cx, picks, d);
    }

    /**
     * Measures of one repeat family play the same riff, so they get the same frets: at each
     * position in the measure, the fret (and chord) most of the family's measures were given.
     * Without this, each pass of a loop is fretted from its own hearing, and small differences in
     * how its notes were heard make the same riff look different every time it comes round.
     */
    private static void agreeOnFrets(Context cx, List<Pick> picks, int d) {
        if (!AGREE_ON_FRETS || cx.family == null) return;
        Map<Long, Map<Long, Integer>> votes = new HashMap<>();
        for (Pick p : picks) {
            int m = cx.map.measureOf(p.tick);
            if (m < 0 || m >= cx.family.length || cx.family[m] < 0 || cx.familySize[cx.family[m]] < 2) continue;
            long key = ((long) cx.family[m] << 32) | (p.tick - cx.map.measureStart(m));
            long fret = ((long) (p.lane + 1) << 8) | (p.lane2 + 1);
            votes.computeIfAbsent(key, k -> new HashMap<>()).merge(fret, 1, Integer::sum);
        }
        for (Pick p : picks) {
            int m = cx.map.measureOf(p.tick);
            if (m < 0 || m >= cx.family.length || cx.family[m] < 0 || cx.familySize[cx.family[m]] < 2) continue;
            Map<Long, Integer> v = votes.get(((long) cx.family[m] << 32) | (p.tick - cx.map.measureStart(m)));
            if (v == null) continue;
            long best = -1;
            int bestCount = 0;
            for (Map.Entry<Long, Integer> e : v.entrySet()) {
                if (e.getValue() > bestCount || (e.getValue() == bestCount && e.getKey() < best)) {
                    best = e.getKey();
                    bestCount = e.getValue();
                }
            }
            if (best < 0) continue;
            p.lane = (int) (best >> 8) - 1;
            int second = (int) (best & 0xFF) - 1;
            p.lane2 = chordAllowed(d, p) ? second : -1;
        }
    }

    static boolean AGREE_ON_FRETS = !Boolean.getBoolean("yarg.noFretVote");

    private static void copyFret(Pick from, Pick to, int d) {
        to.lane = from.lane;
        // A strummed chord stays a chord, except where this level only allows chords on beats.
        to.lane2 = chordAllowed(d, to) ? from.lane2 : -1;
    }

    private static boolean chordAllowed(int d, Pick p) {
        if (d == EASY) return false;
        if (d == MEDIUM) return p.level <= 1;
        if (d == HARD) return p.level <= 2;
        return true;
    }

    private static void fretPhrase(Context cx, List<Pick> phrase, int lanes, int d) {
        // The distinct sounds in this phrase, with their pitch here and whether they are chords.
        Map<Integer, List<Pick>> byGroup = new HashMap<>();
        for (Pick p : phrase) {
            p.sound = -1;
            if (!p.tonal()) continue;
            List<Pick> list = byGroup.get(p.onset.group);
            if (list == null) {
                list = new ArrayList<>();
                byGroup.put(p.onset.group, list);
            }
            list.add(p);
        }
        final Map<Integer, List<Pick>> bySound = new HashMap<>();
        int tonal = 0;
        for (Map.Entry<Integer, List<Pick>> e : byGroup.entrySet()) {
            // The fingerprint cannot tell a note from the same note an octave up. Where clearly
            // read single notes sit in two octaves (an octave riff), they are two sounds.
            int[] clear = new int[12];
            for (Pick p : e.getValue()) {
                if (p.onset.conf >= 0.6f) clear[Math.max(0, Math.min(11, Math.round(p.onset.height) / 12))]++;
            }
            int need = Math.max(2, e.getValue().size() / 4);
            int octaves = 0;
            for (int c : clear) if (c >= need) octaves++;
            for (Pick p : e.getValue()) {
                int octave = 15;
                if (octaves >= 2) {
                    int own = Math.max(0, Math.min(11, Math.round(p.onset.height) / 12));
                    octave = own;
                    for (int step = 0; step < 12 && clear[octave] < need; step++) {
                        if (own - step >= 0 && clear[own - step] >= need) octave = own - step;
                        else if (own + step <= 11 && clear[own + step] >= need) octave = own + step;
                    }
                }
                p.sound = e.getKey() * 16 + octave;
                List<Pick> list = bySound.get(p.sound);
                if (list == null) {
                    list = new ArrayList<>();
                    bySound.put(p.sound, list);
                }
                list.add(p);
                tonal++;
            }
        }
        // A faint, unclear sound that only turns up now and then (a drum with a bit of ring to
        // it, say) is not given a fret of its own; it will follow the note before it.
        for (java.util.Iterator<Map.Entry<Integer, List<Pick>>> it = bySound.entrySet().iterator(); it.hasNext(); ) {
            List<Pick> members = it.next().getValue();
            if (members.size() < 0.12 * tonal && medianConf(members) < 0.45f) it.remove();
        }
        tonal = 0;
        for (List<Pick> members : bySound.values()) tonal += members.size();
        if (bySound.isEmpty()) return;
        final Map<Integer, Float> height = new HashMap<>();
        Map<Integer, Boolean> chord = new HashMap<>();
        List<Integer> bySize = new ArrayList<>(bySound.keySet());
        Collections.sort(bySize, new Comparator<Integer>() {
            @Override
            public int compare(Integer a, Integer b) {
                int c = Integer.compare(bySound.get(b).size(), bySound.get(a).size());
                return c != 0 ? c : Integer.compare(a, b);
            }
        });
        float[] all = new float[tonal];
        int at = 0;
        for (int g : bySize) {
            float h = pitchOf(bySound.get(g));
            // Chords live in the lower register; an unclear reading up high is a melody note
            // sounding together with something else, not a chord.
            boolean c = cx.chords && isChord(cx, bySound.get(g)) && h < CHORD_CEILING;
            // An unclear sound that is the same note as a more common one in another octave
            // (typically the bass playing the chord's root on its own between strums) takes
            // the fret of the sound it doubles. A clearly read note keeps its own octave.
            for (int other : bySize) {
                if (other == g || medianConf(bySound.get(g)) >= 0.5f) break;
                float diff = h - height.get(other);
                if (Math.abs(diff - 12 * Math.round(diff / 12)) < 0.5f) {
                    h = height.get(other);
                    c = chord.get(other);
                    break;
                }
            }
            height.put(g, h);
            chord.put(g, c);
        }
        // A reading far from the rest of the phrase is most likely off by an octave.
        float[] spread = new float[tonal];
        for (int g : bySize) for (int i = 0; i < bySound.get(g).size(); i++) spread[at++] = height.get(g);
        Arrays.sort(spread);
        float centre = spread[tonal / 2];
        at = 0;
        for (int g : bySize) {
            float h = height.get(g);
            while (h - centre > 16f) h -= 12f;
            while (centre - h > 16f) h += 12f;
            height.put(g, h);
            for (int i = 0; i < bySound.get(g).size(); i++) all[at++] = h;
        }
        // The phrase's range comes from where most of its notes are, so one stray reading
        // far above or below cannot squash everything else onto two frets.
        Arrays.sort(all);
        float low = all[(int) (tonal * 0.1)];
        float high = all[Math.min(tonal - 1, (int) (tonal * 0.9))];
        List<Integer> core = new ArrayList<>();
        for (Map.Entry<Integer, Float> e : height.entrySet()) {
            if (e.getValue() >= low - 0.01f && e.getValue() <= high + 0.01f) core.add(e.getKey());
        }
        Collections.sort(core, new Comparator<Integer>() {
            @Override
            public int compare(Integer a, Integer b) {
                int c = Float.compare(height.get(a), height.get(b));
                return c != 0 ? c : Integer.compare(a, b);
            }
        });
        // Sounds within half a semitone of each other are the same pitch for fretting purposes.
        List<List<Integer>> steps = new ArrayList<>();
        for (int g : core) {
            if (!steps.isEmpty() && height.get(g) - height.get(steps.get(steps.size() - 1).get(0)) < 0.5f) {
                steps.get(steps.size() - 1).add(g);
            } else {
                List<Integer> one = new ArrayList<>();
                one.add(g);
                steps.add(one);
            }
        }
        int k = steps.size();
        // Position on the neck is half "how far up in pitch" (so big leaps look big) and half
        // "which step of the phrase's ladder" (so every distinct pitch has a chance of its own
        // fret even when chords sit low and a melody sits two octaves above them). At least two
        // semitones per fret, so a small step in the music is a small step on the neck.
        float perFret = Math.max((high - low) / (lanes - 1), 2.0f);
        int[] fret = new int[k];
        for (int i = 0; i < k; i++) {
            float byPitch = (height.get(steps.get(i).get(0)) - low) / perFret;
            float byStep = k > lanes ? (float) i * (lanes - 1) / (k - 1) : byPitch;
            fret[i] = Math.round(0.5f * byPitch + 0.5f * byStep);
        }
        if (k <= lanes) {
            // Few enough pitches to give each its own fret.
            for (int i = 1; i < k; i++) fret[i] = Math.max(fret[i], fret[i - 1] + 1);
            if (fret[k - 1] > lanes - 1) {
                fret[k - 1] = lanes - 1;
                for (int i = k - 2; i >= 0; i--) fret[i] = Math.min(fret[i], fret[i + 1] - 1);
            }
        }
        for (int i = 0; i < k; i++) fret[i] = Math.max(0, Math.min(lanes - 1, fret[i]));
        // Low phrases sit towards green, high phrases towards orange.
        int room = lanes - 1 - fret[k - 1];
        float span = Math.max(1f, cx.songHigh - cx.songLow);
        float where = Math.max(0f, Math.min(1f, ((low + high) / 2 - cx.songLow) / span));
        int shift = Math.round(room * where);
        Map<Integer, Integer> fretOf = new HashMap<>();
        for (int i = 0; i < k; i++) for (int g : steps.get(i)) fretOf.put(g, fret[i] + shift);

        for (Pick p : phrase) {
            if (p.sound < 0 || !bySound.containsKey(p.sound)) continue;
            int g = p.sound;
            Integer lane = fretOf.get(g);
            if (lane == null) {
                // A stray sound outside the phrase's range goes on the nearest end.
                lane = Math.max(0, Math.min(lanes - 1, Math.round((height.get(g) - low) / perFret) + shift));
                fretOf.put(g, lane);
            }
            p.lane = lane;
            if (chord.get(g) && chordAllowed(d, p)) {
                // Two shapes, so different chords do not all look alike; always the same shape
                // for the same sound.
                int step = d >= HARD && (g / 16) % 2 == 1 ? 2 : 1;
                if (lane + step <= lanes - 1) p.lane2 = lane + step;
                else if (lane + 1 <= lanes - 1) p.lane2 = lane + 1;
                else p.lane2 = lane - 1;
            }
        }
    }

    /** Repeats of a measure get the same sustain lengths: the middle value across the family. */
    private static void agreeOnSustains(Context cx, List<Pick> picks) {
        if (cx.family == null) return;
        Map<Long, List<Pick>> bySpot = new HashMap<>();
        for (Pick p : picks) {
            int m = cx.map.measureOf(p.tick);
            if (m < 0 || m >= cx.family.length) continue;
            int fam = cx.family[m];
            if (fam < 0 || cx.familySize[fam] < 2) continue;
            long key = fam * 100000L + (p.tick - cx.map.measureStart(m));
            List<Pick> list = bySpot.get(key);
            if (list == null) {
                list = new ArrayList<>();
                bySpot.put(key, list);
            }
            list.add(p);
        }
        for (List<Pick> list : bySpot.values()) {
            if (list.size() < 2) continue;
            long[] s = new long[list.size()];
            for (int i = 0; i < s.length; i++) s[i] = list.get(i).sustain;
            Arrays.sort(s);
            for (Pick p : list) p.sustain = s[s.length / 2];
        }
    }

    /**
     * A pitched note followed by a gap is held for as long as its own pitches keep sounding in
     * the mix. Drum hits are never held.
     */
    private static void addSustains(Context cx, List<Pick> picks, int d) {
        Features f = cx.f;
        int n = f.frames();
        for (int i = 0; i < picks.size(); i++) {
            Pick p = picks.get(i);
            p.sustain = 0;
            if (!p.tonal()) continue;
            long next = i + 1 < picks.size() ? picks.get(i + 1).tick : p.tick + 16L * TempoMap.RESOLUTION;
            if (next - p.tick < SUSTAIN_MIN_GAP[d]) continue;
            long limit = Math.min(next - 96, p.tick + 16L * TempoMap.RESOLUTION);

            // The two pitch classes that define this sound.
            float[] print = p.onset.print;
            int a = 0, b = 1;
            for (int c = 0; c < 12; c++) if (print[c] > print[a]) a = c;
            if (b == a) b = 0;
            for (int c = 0; c < 12; c++) if (c != a && print[c] > print[b]) b = c;

            int start = p.onset.frame;
            double ref = 0;
            float loud = 0;
            int counted = 0;
            for (int k = start + 3; k <= Math.min(n - 1, start + 6); k++) {
                ref += presence(f, k, a, b);
                counted++;
            }
            for (int k = start; k <= Math.min(n - 1, start + 6); k++) loud = Math.max(loud, f.rms[k]);
            if (counted == 0 || loud <= 1e-5f) continue;
            ref /= counted;
            // A separated bass is held by how loud it still is, so how far its pitches stand out
            // does not matter there (at a low E it can read as almost nothing).
            if (ref < 0.05 && !cx.bass) continue;
            int endFrame = f.frameAt(cx.map.tickToTime(limit));
            int k = start + 6;
            int weak = 0;
            while (k < endFrame && k < n) {
                // The note is still there while its pitches stand out from the rest of the
                // harmony and the music has not dropped to near silence. A separated bass plays
                // nothing else, so there it is simply how loud it still is (until 15 dB down):
                // the pitch reading of a distorted 808 wanders too much to follow.
                boolean gone = cx.bass
                        ? f.rms[k] < BASS_HOLD * loud
                        : presence(f, k, a, b) < 0.35 * ref || f.rms[k] < 0.08f * loud;
                if (gone) {
                    // A brief dip (a drum hit masking the note) does not end it.
                    if (++weak > 5) break;
                } else {
                    weak = 0;
                }
                k++;
            }
            k -= weak;
            long endTick = Math.min(limit, cx.map.timeToTick(f.timeOf(k)));
            long len = ((endTick - p.tick) / 48) * 48;
            p.sustain = len >= TempoMap.RESOLUTION ? len : 0;
        }
    }

    /** How far two pitch classes stand out from the rest of the harmony in a frame. */
    private static double presence(Features f, int frame, int a, int b) {
        int at = frame * 12;
        double all = 0;
        for (int c = 0; c < 12; c++) all += f.chroma[at + c];
        return (f.chroma[at + a] + f.chroma[at + b]) / 2.0 - all / 12.0;
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
