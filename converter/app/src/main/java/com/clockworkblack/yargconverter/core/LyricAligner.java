package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;

/**
 * Moves time-stamped lyrics to where they are really sung in THIS audio file.
 *
 * Lyric files are timed against one recording of a song, usually the album version. The audio
 * being converted is often another cut: a music video with a spoken scene or a few seconds of
 * nothing before the song, a longer intro, a break in the middle that the album does not have.
 * The words then come up while nobody is singing yet.
 *
 * With the voice separated from the band the answer can be heard: the lines of a lyric file make
 * a pattern (sung here, quiet there, a long break after the second chorus), and that pattern is
 * slid along the voice until the two agree. One later part of the song may need a different move
 * than the start (the video's break in the middle), so one such change is looked for as well.
 *
 * Without the voice (Normal mode) only one thing can be told: silence at the very start of the
 * file that the lyric file cannot know about.
 *
 * When in doubt the lyrics are left exactly as they were.
 */
public final class LyricAligner {
    private LyricAligner() {}

    public static final class Result {
        /** The lyrics to use: the ones given when nothing was moved. */
        public List<Lrc.Line> lines;
        /** Seconds the lyrics (or their first part) were moved; more than 0 is later. */
        public double shift;
        /** The first line (index into {@link #lines}) of a later part that moved by another amount, or -1. */
        public int changeLine = -1;
        /** Seconds that later part was moved. */
        public double laterShift;
        /** What was done, for the user; null when the lyrics were left alone. */
        public String note;
        /** Word-by-word alignment only, one entry per line of {@link #lines}: where the letters were heard, and how well they fitted. */
        public double[] heard, fit;
        /** Lines of the lyrics found not to be sung in this recording and left out (word-by-word alignment only). */
        public int leftOut;
        /** Lines the song's repeats corrected, one note each (word-by-word alignment only); may be null. */
        public List<String> repeatNotes;

        public boolean moved() {
            return note != null;
        }
    }

    /** The pattern is tried every this many seconds. */
    static final double HOP = 0.05;
    /** How far the lyrics may be moved: earlier (the audio starts part-way into the song) and later. */
    static final double EARLIEST = -30, LATEST = 180;
    /** Fewer lines than this make too thin a pattern to trust. */
    static final int MIN_LINES = 6;
    /** A move smaller than this is not worth making: the charter finds a line within that reach itself. */
    static final double MIN_SHIFT = 0.75;
    /** How much better per line the new place must fit than the old one. */
    static final double MIN_GAIN = 0.08;
    /** How much of each line must be voice at the new place, on average. */
    static final double MIN_VOICED = 0.45;
    /** Lyric stamps run a little early; the charter copes with early far better than with late. */
    static final double EARLY_BIAS = 0.10;
    /** A later part must have at least this many lines to be moved on its own. */
    static final int MIN_PART = 5;

    /**
     * @param lines the lyrics, in order of time (blank lines mark where singing stops)
     * @param voice the separated voice, or null
     */
    public static Result align(List<Lrc.Line> lines, VocalAnalyzer.Voice voice) {
        Result r = new Result();
        r.lines = lines;
        if (lines == null || voice == null || voice.frames() < 50) return r;

        // The lines with words, and where each one probably ends
        List<Integer> index = new ArrayList<>();
        for (int i = 0; i < lines.size(); i++) if (!VocalCharter.words(lines.get(i).text).isEmpty()) index.add(i);
        int n = index.size();
        if (n < MIN_LINES) return r;
        double[] start = new double[n], end = new double[n];
        for (int k = 0; k < n; k++) {
            int i = index.get(k);
            start[k] = lines.get(i).time;
            int syllables = 0;
            for (String w : VocalCharter.words(lines.get(i).text)) syllables += VocalCharter.syllables(w);
            double length = Math.max(0.8, Math.min(6.0, 0.3 + 0.35 * syllables));
            double next = i + 1 < lines.size() ? lines.get(i + 1).time : Double.MAX_VALUE;
            end[k] = Math.max(start[k] + 0.5, Math.min(next, start[k] + length));
        }

        // How much of each twentieth of a second is voice, summed up so any stretch is one subtraction
        int bins = (int) Math.ceil(voice.duration / HOP) + 1;
        float[] active = new float[bins], total = new float[bins];
        float gate = voice.gate();
        for (int f = 0; f < voice.frames(); f++) {
            int b = (int) (voice.timeOf(f) / HOP);
            if (b < 0 || b >= bins) continue;
            total[b]++;
            if (voice.level[f] > gate) active[b]++;
        }
        double[] sum = new double[bins + 1];
        for (int b = 0; b < bins; b++) sum[b + 1] = sum[b] + (total[b] > 0 ? active[b] / total[b] : 0);

        int steps = (int) Math.round((Math.min(LATEST, voice.duration) - EARLIEST) / HOP) + 1;
        int zero = (int) Math.round(-EARLIEST / HOP);
        // fit[k][j]: how well line k sits on the voice when moved by step j; voiced[k][j]: how much of it is voice
        float[][] fit = new float[n][steps];
        float[][] voiced = new float[n][steps];
        for (int k = 0; k < n; k++) {
            double gapBefore = k == 0 ? Double.MAX_VALUE : start[k] - end[k - 1];
            double nextStart = k + 1 < n ? start[k + 1] : Double.MAX_VALUE;
            double gapAfter = nextStart - end[k];
            for (int j = 0; j < steps; j++) {
                double d = EARLIEST + j * HOP;
                double in = mean(sum, start[k] + d, end[k] + d);
                double score = in;
                // Just before a line that follows a pause, nobody should be singing
                if (gapBefore >= 0.6) score -= 0.6 * mean(sum, start[k] + d - 0.5, start[k] + d - 0.1);
                // Nor before the first line of all
                if (k == 0) score -= 0.5 * mean(sum, start[k] + d - 6.0, start[k] + d - 0.6);
                // Nor in a long break between lines, or after the last one
                if (gapAfter >= 3.0) {
                    double to = Math.min(nextStart, end[k] + 10.0) - 0.5;
                    score -= 0.5 * mean(sum, end[k] + d + 0.7, to + d);
                }
                fit[k][j] = (float) score;
                voiced[k][j] = (float) in;
            }
        }

        // Running totals over the lines, so any run of lines at any step is one subtraction
        double[][] upTo = new double[n + 1][steps];
        for (int k = 0; k < n; k++) for (int j = 0; j < steps; j++) upTo[k + 1][j] = upTo[k][j] + fit[k][j];

        // The whole song moved as one
        int best = bestStep(upTo, 0, n, 0, steps);
        double here = upTo[n][zero];
        double whole = upTo[n][best];

        // Or two parts, each moved on its own (a break put into, or cut out of, the middle)
        int cut = -1, head = best, tail = best;
        double two = whole;
        if (n >= 2 * MIN_PART) {
            for (int c = MIN_PART; c <= n - MIN_PART; c++) {
                int h = bestStep(upTo, 0, c, 0, steps);
                // The second part may not be moved back over the first
                int least = h - (int) Math.floor((start[c] - end[c - 1] + 0.3) / HOP);
                int t = bestStep(upTo, c, n, Math.max(0, least), steps);
                if (Math.abs(t - h) * HOP < 1.0) continue;
                double score = (upTo[c][h]) + (upTo[n][t] - upTo[c][t]);
                if (score > two) {
                    two = score;
                    cut = c;
                    head = h;
                    tail = t;
                }
            }
            // Worth it only when the second part, moved on its own, fits clearly better
            if (cut >= 0) {
                int parted = n - cut;
                double gain = two - whole;
                if (gain < Math.max(1.5, 0.12 * Math.min(cut, parted))) cut = -1;
            }
        }

        double shiftHead, shiftTail;
        if (cut >= 0) {
            shiftHead = worthIt(upTo, voiced, 0, cut, head, zero) ? EARLIEST + head * HOP - EARLY_BIAS : 0;
            shiftTail = worthIt(upTo, voiced, cut, n, tail, zero) ? EARLIEST + tail * HOP - EARLY_BIAS : 0;
            if (Math.abs(shiftTail - shiftHead) < 1.0) cut = -1;
        } else {
            shiftHead = shiftTail = 0;
        }
        if (cut < 0) {
            if (!worthIt(upTo, voiced, 0, n, best, zero)) return r;
            shiftHead = shiftTail = EARLIEST + best * HOP - EARLY_BIAS;
        }
        if (shiftHead == 0 && shiftTail == 0) return r;

        int changeAt = cut >= 0 ? index.get(cut) : lines.size();
        // A blank line just before the later part (the end of the line before) stays with the first part
        List<Lrc.Line> out = new ArrayList<>(lines.size());
        int changeLine = -1;
        for (int i = 0; i < lines.size(); i++) {
            Lrc.Line l = lines.get(i);
            boolean later = i >= changeAt;
            double t = l.time + (later ? shiftTail : shiftHead);
            // Words for a part of the song this audio does not have
            if (t < -0.5 || t > voice.duration + 1.0) continue;
            if (later && changeLine < 0) changeLine = out.size();
            out.add(new Lrc.Line(Math.max(0, t), l.text, l.singer));
        }
        if (out.isEmpty()) return r;

        r.lines = out;
        r.shift = shiftHead;
        if (cut >= 0 && changeLine >= 0) {
            r.changeLine = changeLine;
            r.laterShift = shiftTail;
            r.note = "The lyrics were timed for a different cut of this song, so they were lined up with the voice: "
                    + (shiftHead == 0 ? "the first part was left as it was" : "the first part was moved " + amount(shiftHead))
                    + ", and from " + Lrc.stamp(lines.get(changeAt).time) + " on " + (shiftTail == 0 ? "left as it was" : "moved " + amount(shiftTail)) + ".";
        } else {
            r.note = "The lyrics were timed for a different cut of this song, so they were moved " + amount(shiftHead)
                    + " to line up with the voice.";
        }
        return r;
    }

    /**
     * Normal mode, where the voice is not heard on its own: lyrics are moved later by the silence
     * the file starts with, when that is far more than a recording normally has.
     */
    public static Result alignToStart(List<Lrc.Line> lines, Features f) {
        Result r = new Result();
        r.lines = lines;
        if (lines == null || lines.isEmpty() || f == null || f.rms == null || f.frames() < 50) return r;
        double lead = leadIn(f);
        if (lead < SILENT_START) return r;
        double shift = lead - USUAL_START;
        // The words must still fit in the song
        double last = lines.get(lines.size() - 1).time;
        if (last + shift > f.duration - 1.0) return r;
        r.lines = shift(lines, shift);
        r.shift = shift;
        r.note = String.format(Locale.US, "The audio starts with %.1f seconds of silence, so the lyrics were moved %s.", lead, amount(shift));
        return r;
    }

    /** Silence at the start of a file longer than this is not part of the recording the lyrics were timed to. */
    static final double SILENT_START = 2.0;
    /** A recording normally starts this long after the start of its file. */
    static final double USUAL_START = 0.3;

    /** Seconds until the first sound that is not just hiss: a hundredth as loud as the song's loud moments. */
    static double leadIn(Features f) {
        float[] sorted = f.rms.clone();
        Arrays.sort(sorted);
        float loud = sorted[(int) Math.min(sorted.length - 1, Math.round(sorted.length * 0.95))];
        if (loud <= 0) return 0;
        float gate = loud * 0.01f;
        int need = Math.max(2, (int) Math.round(0.15 * f.fps));
        int run = 0;
        for (int i = 0; i < f.frames(); i++) {
            run = f.rms[i] > gate ? run + 1 : 0;
            if (run >= need) return Math.max(0, f.timeOf(i - need + 1));
        }
        return 0;
    }

    /** The same lyrics, every line moved by {@code seconds} (more than 0 is later). */
    public static List<Lrc.Line> shift(List<Lrc.Line> lines, double seconds) {
        List<Lrc.Line> out = new ArrayList<>(lines.size());
        for (Lrc.Line l : lines) {
            double t = l.time + seconds;
            if (t < -0.5) continue;
            out.add(new Lrc.Line(Math.max(0, t), l.text, l.singer));
        }
        return out;
    }

    private static String amount(double seconds) {
        return String.format(Locale.US, "%.1f seconds %s", Math.abs(seconds), seconds > 0 ? "later" : "earlier");
    }

    /**
     * True when moving lines {@code from..to} by step {@code j} is clearly better than leaving
     * them: far enough to matter, a much better fit, and mostly voice under the lines.
     */
    private static boolean worthIt(double[][] upTo, float[][] voiced, int from, int to, int j, int zero) {
        int count = to - from;
        if (count < MIN_PART) return false;
        double moved = EARLIEST + j * HOP - EARLY_BIAS;
        if (Math.abs(moved) < MIN_SHIFT) return false;
        double gain = (upTo[to][j] - upTo[from][j]) - (upTo[to][zero] - upTo[from][zero]);
        if (gain < MIN_GAIN * count) return false;
        double in = 0;
        for (int k = from; k < to; k++) in += voiced[k][j];
        return in / count >= MIN_VOICED;
    }

    /** The step from {@code least} on at which lines {@code from..to} fit the voice best. */
    private static int bestStep(double[][] upTo, int from, int to, int least, int steps) {
        int best = Math.min(least, steps - 1);
        double bestScore = Double.NEGATIVE_INFINITY;
        for (int j = least; j < steps; j++) {
            double s = upTo[to][j] - upTo[from][j];
            if (s > bestScore) {
                bestScore = s;
                best = j;
            }
        }
        return best;
    }

    /** The share of the time from {@code a} to {@code b} that is voice; outside the audio there is none. */
    private static double mean(double[] sum, double a, double b) {
        int ia = (int) Math.round(a / HOP), ib = (int) Math.round(b / HOP);
        if (ib <= ia) ib = ia + 1;
        int last = sum.length - 1;
        int ca = Math.max(0, Math.min(last, ia)), cb = Math.max(0, Math.min(last, ib));
        return (sum[cb] - sum[ca]) / (ib - ia);
    }

    // ------------------------------------------------------------------ line by line

    /** How long the voice must be silent before it "comes in" again, in seconds. */
    static final double ONSET_QUIET = 0.15;
    /** How far back a line may be moved to where the singing it falls in began. */
    static final double SNAP_BACK = 1.0;
    /** A line with less room than this share of what it takes to sing counts as crammed. */
    static final double CRAMPED = 0.4;
    /** Seconds it takes to sing a syllable, roughly, for judging room. */
    static final double SYLLABLE_SEC = 0.2;

    /**
     * Fixes single lines of lyrics found online, whose times are mostly right but not every one:
     * NetEase and LRCLIB files often have a line stamped seconds late (leaving it crammed against
     * the next), or stamped part-way into the singing it belongs to. Only the separated voice is
     * used: where it comes in after a silence, and how much room each line has.
     *
     * <ol>
     *   <li>Two lines crammed together (less room than it takes to sing the first): one of them is
     *       stamped wrong. It moves to the nearest place the voice comes in, choosing the move that
     *       leaves the lines around it with this song's usual room. (Spreading a whole run of lines
     *       out instead was tried and moved lines that were right.)</li>
     *   <li>A line stamped while the voice is already singing, with no new entry just after the
     *       stamp, moves back to where that singing began (up to {@link #SNAP_BACK}).</li>
     * </ol>
     *
     * Learned from songs checked word by word with speech recognition (converter/desktop).
     */
    public static Result checkLines(List<Lrc.Line> lines, VocalAnalyzer.Voice voice) {
        Result r = new Result();
        r.lines = lines;
        if (lines == null || voice == null || voice.frames() < 50) return r;
        List<Integer> index = new ArrayList<>();
        for (int i = 0; i < lines.size(); i++) if (!VocalCharter.words(lines.get(i).text).isEmpty()) index.add(i);
        int n = index.size();
        if (n < 2) return r;

        float gate = voice.gate();
        boolean[] voiced = new boolean[voice.frames()];
        for (int f = 0; f < voiced.length; f++) voiced[f] = voice.level[f] > gate;
        int quiet = Math.max(2, (int) Math.round(ONSET_QUIET * voice.fps));

        double[] t = new double[n];
        double[] need = new double[n];
        for (int k = 0; k < n; k++) {
            Lrc.Line l = lines.get(index.get(k));
            t[k] = l.time;
            int syllables = 0;
            for (String w : VocalCharter.words(l.text)) syllables += VocalCharter.syllables(w);
            need[k] = Math.max(0.8, SYLLABLE_SEC * syllables);
        }
        double[] given = t.clone();

        // 1. Crammed pairs: one of the two lines is stamped wrong, usually the first one seconds
        //    late, against the next. Of moving the first back or the second on, the move that
        //    lands on a place where the voice comes in, at this song's usual room per line, wins.
        double usual = usualRoom(t, need);
        int spread = 0;
        for (int k = 0; k + 1 < n; k++) {
            if (t[k + 1] - t[k] >= CRAMPED * need[k]) continue;
            double before = k > 0 ? t[k - 1] + 0.5 : 0;
            double after = k + 2 < n ? t[k + 2] - 0.5 : voice.duration;
            // The first line back, ending where the second starts
            double backTarget = t[k + 1] - usual * need[k];
            double back = nearestEntry(voice, voiced, quiet, Math.max(before, backTarget - 2.0), t[k + 1] - 0.6 * need[k], backTarget);
            // The second line on, starting when the first has had its room
            double onTarget = t[k] + usual * need[k];
            double on = nearestEntry(voice, voiced, quiet, t[k] + 0.6 * need[k], Math.min(after, onTarget + 2.0), onTarget);
            // Judged by how normal it leaves the room of the lines around the pair
            double backCost = back < 0 ? Double.MAX_VALUE : roomCost(t, need, usual, k, back, k);
            double onCost = on < 0 ? Double.MAX_VALUE : roomCost(t, need, usual, k + 1, on, k);
            if (back >= 0 && backCost <= onCost) {
                t[k] = back;
                spread++;
            } else if (on >= 0) {
                t[k + 1] = on;
                spread++;
            }
        }

        // 2. Stamped part-way into singing: back to where it began
        int snapped = 0;
        for (int k = 0; k < n; k++) {
            int f = voice.frameAt(t[k]);
            if (!sounding(voiced, f - 5, f + 5)) continue;
            double lo = k > 0 ? t[k - 1] + 0.5 : 0;
            double hi = k + 1 < n ? t[k + 1] - 0.3 : Double.MAX_VALUE;
            if (entryNear(voice, voiced, quiet, t[k], Math.min(hi, t[k] + 0.25)) >= 0) continue;
            double entry = lastEntry(voice, voiced, quiet, Math.max(lo, t[k] - SNAP_BACK), t[k]);
            if (entry >= 0 && t[k] - entry > 0.12) {
                t[k] = entry;
                snapped++;
            }
        }

        int moved = 0;
        for (int k = 0; k < n; k++) if (Math.abs(t[k] - given[k]) > 0.05) moved++;
        if (moved == 0) return r;
        List<Lrc.Line> out = new ArrayList<>(lines);
        for (int k = 0; k < n; k++) {
            Lrc.Line l = lines.get(index.get(k));
            out.set(index.get(k), new Lrc.Line(Math.max(0, t[k]), l.text, l.singer));
        }
        r.lines = out;
        r.note = String.format(Locale.US, "%d line%s of the lyrics %s moved to where the voice comes in.", moved, moved == 1 ? "" : "s",
                moved == 1 ? "was" : "were");
        return r;
    }

    /** How far from this song's usual room the lines around {@code around} are with line {@code which} at {@code at}. */
    private static double roomCost(double[] t, double[] need, double usual, int which, double at, int around) {
        double[] u = t.clone();
        u[which] = at;
        double cost = 0;
        for (int k = Math.max(0, around - 1); k <= Math.min(t.length - 2, around + 1); k++) {
            double room = (u[k + 1] - u[k]) / need[k];
            if (room <= 0) return Double.MAX_VALUE;
            cost += Math.abs(Math.log(room / usual));
        }
        return cost;
    }

    /** The middle room a line has in this song, as a multiple of what it takes to sing it. */
    private static double usualRoom(double[] t, double[] need) {
        List<Double> r = new ArrayList<>();
        for (int k = 0; k + 1 < t.length; k++) {
            double room = (t[k + 1] - t[k]) / need[k];
            if (room >= CRAMPED && room < 8) r.add(room);
        }
        if (r.isEmpty()) return 1.5;
        java.util.Collections.sort(r);
        return r.get(r.size() / 2);
    }

    /** Of the places in [a, b] where the voice comes in, the one nearest {@code target}; -1 if none. */
    private static double nearestEntry(VocalAnalyzer.Voice v, boolean[] voiced, int quiet, double a, double b, double target) {
        double best = -1, miss = Double.MAX_VALUE;
        for (int f = v.frameAt(a); f <= v.frameAt(b); f++) {
            if (!isEntry(voiced, f, quiet)) continue;
            double d = Math.abs(v.timeOf(f) - target);
            if (d < miss) {
                miss = d;
                best = v.timeOf(f);
            }
        }
        return best;
    }

    private static boolean sounding(boolean[] voiced, int from, int to) {
        for (int f = Math.max(0, from); f <= Math.min(voiced.length - 1, to); f++) if (voiced[f]) return true;
        return false;
    }

    private static boolean isEntry(boolean[] voiced, int f, int quiet) {
        if (f < quiet || f >= voiced.length || !voiced[f]) return false;
        for (int g = f - quiet; g < f; g++) if (voiced[g]) return false;
        return true;
    }

    /** The first place in [a, b] where the voice comes in after a silence, or -1. */
    private static double entryNear(VocalAnalyzer.Voice v, boolean[] voiced, int quiet, double a, double b) {
        for (int f = v.frameAt(a); f <= v.frameAt(b); f++) if (isEntry(voiced, f, quiet)) return v.timeOf(f);
        return -1;
    }

    /** The last place in [a, b] where the voice comes in after a silence, or -1. */
    private static double lastEntry(VocalAnalyzer.Voice v, boolean[] voiced, int quiet, double a, double b) {
        for (int f = v.frameAt(b); f >= v.frameAt(a); f--) if (isEntry(voiced, f, quiet)) return v.timeOf(f);
        return -1;
    }
}
