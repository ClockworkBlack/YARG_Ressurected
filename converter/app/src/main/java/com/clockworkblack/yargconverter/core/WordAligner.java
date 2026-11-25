package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * Finds where each line of the lyrics is sung by matching its letters to the separated voice.
 *
 * A speech model (wav2vec2, run by the app; see stems/OrtWordModel) listens to the voice and says,
 * for every fiftieth of a second, how likely each letter is. Because the words are already known,
 * nothing has to be recognised: the one way of laying the letters of the whole song along the
 * recording that fits those likelihoods best is found ("forced alignment"), and a line starts
 * where its first letter lands.
 *
 * Then the song's own structure is used as a check. A hook is sung on the same melody each time,
 * so its lines come back a whole number of bars later, and inside the hook each line is the same
 * distance after the first. A line that breaks that pattern, where most repeats keep it, was
 * misheard (ad-libs over the last hook, a fading outro) and is put where the pattern says.
 *
 * Measured on a real song against every other way tried (lyric sites' own times, the voice's
 * entries alone, a speech recogniser's word times): this was the only one whose hook lines came
 * out the same in every repeat, within a twentieth of a second.
 *
 * English letters only for now: lyrics in other scripts are left to {@link LyricAligner}.
 */
public final class WordAligner {
    private WordAligner() {}

    /** The model hears the voice at this rate. */
    public static final int SAMPLE_RATE = 16000;
    /** Seconds between two rows of likelihoods. */
    public static final double FRAME_SEC = 0.02;
    /** Number of symbols the model tells apart (wav2vec2-base-960h). */
    public static final int SYMBOLS = 32;

    private static final int BLANK = 0, SPACE = 4;
    /** The model's symbols for A to Z. */
    private static final int[] LETTER = {7, 24, 19, 14, 5, 20, 21, 11, 10, 29, 26, 15, 17, 9, 8, 23, 30, 13, 12, 6, 16, 25, 18, 28, 22, 31};
    private static final int APOSTROPHE = 27;

    /**
     * What a moment that fits neither silence nor the next letter costs (log likelihood): singing
     * the lyrics leave out, ad-libs, a second voice.
     */
    static final float ANYTHING = -4f;
    /** How close two repeats must agree, in seconds. */
    static final double AGREE_SEC = 0.25;

    public static final class Aligned {
        /** Where each line starts in seconds, by index into the lines given; NaN for a line with no letters. */
        public double[] start;
        /** How well each line's letters fitted (0 is perfect, below about -3 is doubtful). */
        public double[] fit;
    }

    /** True when enough of the lyrics is written in English letters for the model to follow. */
    public static boolean canAlign(List<Lrc.Line> lines) {
        if (lines == null) return false;
        int latin = 0, other = 0, with = 0;
        for (Lrc.Line l : lines) {
            if (symbols(l.text).length > 0) with++;
            for (int i = 0; i < l.text.length(); i++) {
                char c = l.text.charAt(i);
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
                else if (Character.isLetter(c)) other++;
            }
        }
        return with >= 4 && latin >= 20 && other * 5 <= latin;
    }

    /** A line as the model's symbols: its words' letters, a space symbol after each word. Bracketed asides are left out. */
    public static int[] symbols(String text) {
        if (text == null) return new int[0];
        String body = text.replaceAll("\\([^)]*\\)", " ");
        boolean any = false;
        for (int i = 0; i < body.length() && !any; i++) any = Character.isLetter(body.charAt(i));
        if (!any) body = text;
        List<Integer> out = new ArrayList<>();
        boolean inWord = false;
        String upper = body.toUpperCase(Locale.US).replace('’', '\'');
        for (int i = 0; i < upper.length(); i++) {
            char c = upper.charAt(i);
            int symbol = c >= 'A' && c <= 'Z' ? LETTER[c - 'A'] : c == '\'' && inWord ? APOSTROPHE : -1;
            if (symbol >= 0) {
                out.add(symbol);
                inWord = true;
            } else if (Character.isWhitespace(c) && inWord) {
                out.add(SPACE);
                inWord = false;
            }
        }
        if (inWord) out.add(SPACE);
        int[] a = new int[out.size()];
        for (int i = 0; i < a.length; i++) a[i] = out.get(i);
        return a;
    }

    /**
     * Lays the letters of all the lines along the recording.
     *
     * @param logProbs one row per {@link #FRAME_SEC}: the log likelihood of each of the {@link #SYMBOLS}
     */
    public static Aligned align(List<Lrc.Line> lines, float[][] logProbs) {
        return align(lines, logProbs, null, 0);
    }

    /**
     * @param near   roughly where each line is (its time in a lyric file), or null when not known
     * @param window how far from there a line may start, in seconds: keeps a line from being laid
     *               on the same words in another part of the song
     */
    public static Aligned align(List<Lrc.Line> lines, float[][] logProbs, double[] near, double window) {
        Aligned out = alignOnce(lines, logProbs, near, window);
        // Windows that cannot all be kept (times out of order, too many lines in one place): without them
        if (out == null) out = alignOnce(lines, logProbs, null, 0);
        if (out == null) {
            out = new Aligned();
            out.start = new double[lines.size()];
            out.fit = new double[lines.size()];
            Arrays.fill(out.start, Double.NaN);
        }
        return out;
    }

    private static Aligned alignOnce(List<Lrc.Line> lines, float[][] logProbs, double[] near, double window) {
        int n = lines.size();
        Aligned out = new Aligned();
        out.start = new double[n];
        out.fit = new double[n];
        Arrays.fill(out.start, Double.NaN);
        int[][] perLine = new int[n][];
        int total = 0;
        for (int i = 0; i < n; i++) {
            perLine[i] = symbols(lines.get(i).text);
            total += perLine[i].length;
        }
        int frames = logProbs == null ? 0 : logProbs.length;
        if (total == 0 || frames < total) return near == null ? out : null;
        int[] symbol = new int[total];
        int[] lineOf = new int[total];
        int[] firstOf = new int[n];
        int at = 0;
        for (int i = 0; i < n; i++) {
            firstOf[i] = at;
            for (int s : perLine[i]) {
                symbol[at] = s;
                lineOf[at] = i;
                at++;
            }
        }

        // The frames in which each line's first symbol may be entered
        int[] enterFrom = null, enterUntil = null;
        if (near != null && window > 0) {
            enterFrom = new int[total];
            enterUntil = new int[total];
            Arrays.fill(enterUntil, frames);
            for (int i = 0; i < n; i++) {
                if (perLine[i].length == 0 || Double.isNaN(near[i])) continue;
                enterFrom[firstOf[i]] = (int) Math.max(0, (near[i] - window) / FRAME_SEC);
                enterUntil[firstOf[i]] = (int) Math.min(frames, (near[i] + window) / FRAME_SEC);
            }
        }

        // States: a pause, then each symbol with a pause after it. From one frame to the next a
        // state is kept, or the next one is entered, or a pause is skipped (not between two of
        // the same symbol, which need the pause to be told apart).
        int states = 2 * total + 1;
        final float never = -1e30f;
        float[] prev = new float[states], cur = new float[states];
        Arrays.fill(prev, never);
        Arrays.fill(cur, never);
        prev[0] = Math.max(logProbs[0][BLANK], ANYTHING);
        prev[1] = logProbs[0][symbol[0]];
        // Two bits per state and frame: how many states back the best way in came from
        byte[][] back = new byte[frames][(states + 3) / 4];
        for (int f = 1; f < frames; f++) {
            float[] e = logProbs[f];
            float pause = Math.max(e[BLANK], ANYTHING);
            byte[] row = back[f];
            // No state further on than this can be reached yet, none further back can still finish
            int last = Math.min(states - 1, 2 * f + 1);
            int firstState = Math.max(0, states - 2 * (frames - f) - 1);
            for (int s = firstState; s <= last; s++) {
                float best = prev[s];
                int from = 0;
                boolean mayEnter = enterFrom == null || (s & 1) == 0 || (f >= enterFrom[s / 2] && f < enterUntil[s / 2]);
                if (!mayEnter) {
                    // Kept if already in it, but not entered now
                } else if (s >= 1 && prev[s - 1] > best) {
                    best = prev[s - 1];
                    from = 1;
                }
                if (mayEnter && (s & 1) == 1 && s >= 3 && symbol[s / 2] != symbol[s / 2 - 1] && prev[s - 2] > best) {
                    best = prev[s - 2];
                    from = 2;
                }
                cur[s] = best <= never ? never : best + ((s & 1) == 0 ? pause : e[symbol[s / 2]]);
                row[s >> 2] |= (byte) (from << ((s & 3) * 2));
            }
            if (firstState > 0) cur[firstState - 1] = never;
            if (last + 1 < states) cur[last + 1] = never;
            float[] swap = prev;
            prev = cur;
            cur = swap;
        }

        int s = prev[states - 1] >= prev[states - 2] ? states - 1 : states - 2;
        if (prev[s] <= never / 2) return null;
        int[] firstFrame = new int[total];
        int[] lastFrame = new int[total];
        Arrays.fill(lastFrame, -1);
        double[] sum = new double[total];
        int[] count = new int[total];
        for (int f = frames - 1; f >= 0; f--) {
            if ((s & 1) == 1) {
                int k = s / 2;
                firstFrame[k] = f;
                if (lastFrame[k] < 0) lastFrame[k] = f;
                sum[k] += logProbs[f][symbol[k]];
                count[k]++;
            }
            if (f > 0) s -= (back[f][s >> 2] >> ((s & 3) * 2)) & 3;
        }
        for (int i = 0; i < n; i++) {
            if (perLine[i].length == 0) continue;
            out.start[i] = firstFrame[lineOpens(firstOf[i], perLine[i].length, firstFrame, lastFrame, logProbs)] * FRAME_SEC;
            double fit = 0;
            for (int k = firstOf[i]; k < firstOf[i] + perLine[i].length; k++) fit += sum[k] / Math.max(1, count[k]);
            out.fit[i] = fit / perLine[i].length;
        }
        return out;
    }

    /** Other letters heard for this many frames between two of a line's own mean other words were sung there. */
    private static final int STRAY_FRAMES = 5;
    private static final int STRAY_SYMBOLS = 2;
    private static final float HALF = (float) Math.log(0.5);

    /**
     * The symbol a line really opens on. When singing that is not in the lyrics comes before a
     * line (an ad-lib, a pass of the hook the lyric sheet leaves out), the line's first letter or
     * two can be laid on matching letters there, seconds early, at no cost. They give themselves
     * away: between them and the rest of the line, other letters are heard. (While a singer holds
     * a note, the model hears no letters at all.)
     */
    private static int lineOpens(int first, int count, int[] firstFrame, int[] lastFrame, float[][] logProbs) {
        int opens = first;
        // Only a letter or two can stray like this; a whole word laid early is a word sung early
        int until = first + STRAY_SYMBOLS;
        for (int k = first; k < until && k + 1 < first + count; k++) {
            int from = lastFrame[k] + 1, to = firstFrame[k + 1];
            if ((to - from) * FRAME_SEC < 0.5) continue;
            int other = 0;
            for (int f = from; f < to; f++) if (logProbs[f][BLANK] < HALF) other++;
            if (other >= STRAY_FRAMES) opens = k + 1;
        }
        return opens;
    }

    /**
     * The lyrics with every line where it is sung: aligned to the voice, then checked against the
     * song's repeats. Lines without letters are placed between their neighbours; blank lines
     * (gaps) are dropped, since each line now ends where the next begins.
     *
     * @return the re-timed lyrics, or the lines as given when nothing could be aligned
     */
    public static LyricAligner.Result place(List<Lrc.Line> lines, float[][] logProbs, TempoMap map, int beatsPerMeasure, double bpm) {
        return place(lines, logProbs, map, beatsPerMeasure, bpm, true);
    }

    /** How far from its time in the lyric file a line may be laid, in seconds. */
    static final double WINDOW_SEC = 10;
    /** A line fitting worse than this (and not part of the song's repeats) is not in the recording. */
    static final double UNSUNG_FIT = -4.0;
    /** A line whose letters fitted at least this well is where it was heard, whatever the song's repeats say. */
    static final double SURE_FIT = -2.0;
    /** A lyric file agreeing with the voice on at least this share of the lines is taken at its word where the voice is unclear. */
    static final double TRUSTED_SHARE = 0.7;
    /** A line whose letters fitted at least this well was heard clearly: the voice outweighs even a good lyric file. */
    static final double CLEAR_FIT = -0.5;
    /** A line taken out is put back when, among the lines that stayed, it fits at least this well. */
    static final double BACK_IN_FIT = -3.0;
    /** Lyrics fitting worse than this on average are not this song's (right ones: about -1.7; wrong ones: -2.7 and below). */
    static final double MIN_FIT = -2.3;

    /**
     * @param timesKnown false when the lyrics came without times and the ones on the lines mean nothing
     */
    public static LyricAligner.Result place(List<Lrc.Line> lines, float[][] logProbs, TempoMap map, int beatsPerMeasure, double bpm,
                                            boolean timesKnown) {
        LyricAligner.Result r = new LyricAligner.Result();
        r.lines = lines;
        if (!canAlign(lines) || logProbs == null) return r;
        List<Lrc.Line> sung = new ArrayList<>();
        for (Lrc.Line l : lines) if (!VocalCharter.words(l.text).isEmpty()) sung.add(l);
        double[] near = null;
        if (timesKnown) {
            near = new double[sung.size()];
            for (int i = 0; i < near.length; i++) near[i] = sung.get(i).time;
        }
        Aligned a = align(sung, logProbs, near, WINDOW_SEC);

        // Lines the recording does not have (a verse the lyric sheet has and this cut leaves out):
        // their letters fit nowhere, and they push the lines around them out of place. The worst
        // fitting line is taken out and the rest laid again, until none fits that badly; then each
        // line taken out gets another chance among the lines that stayed. Lines that are part of
        // the song's repeats are never taken out: the repeats place those.
        Map<String, Integer> times = new HashMap<>();
        for (Lrc.Line l : sung) {
            String k = key(l.text);
            Integer c = times.get(k);
            times.put(k, c == null ? 1 : c + 1);
        }
        List<Lrc.Line> all = sung;
        boolean[] gone = new boolean[all.size()];
        int limit = all.size() / 4, taken = 0;
        // Twice: putting a line back can show up another that only seemed to fit
        for (int round = 0; round < 2; round++) {
        while (taken < limit) {
            // One line heard badly is a line heard badly. Two or more next to each other (or
            // next to lines already taken out) are a piece of the lyrics this recording lacks.
            boolean[] bad = new boolean[all.size()];
            double[] fitOf = new double[all.size()];
            int at = 0;
            for (int i = 0; i < all.size(); i++) {
                if (gone[i]) continue;
                fitOf[i] = a.fit[at];
                bad[i] = !Double.isNaN(a.start[at]) && a.fit[at] < UNSUNG_FIT && times.get(key(all.get(i).text)) < 3;
                at++;
            }
            int worst = -1;
            double worstFit = UNSUNG_FIT;
            for (int i = 0; i < all.size(); i++) {
                if (!bad[i]) continue;
                boolean beside = (i > 0 && (bad[i - 1] || gone[i - 1])) || (i + 1 < all.size() && (bad[i + 1] || gone[i + 1]));
                if (beside && fitOf[i] < worstFit) {
                    worstFit = fitOf[i];
                    worst = i;
                }
            }
            if (worst < 0) break;
            gone[worst] = true;
            taken++;
            a = align(without(all, gone), logProbs, without(near, gone), WINDOW_SEC);
        }
        for (int i = 0; round == 0 && i < all.size() && taken > 0; i++) {
            if (!gone[i]) continue;
            gone[i] = false;
            Aligned with = align(without(all, gone), logProbs, without(near, gone), WINDOW_SEC);
            int at = 0;
            for (int k = 0; k < i; k++) if (!gone[k]) at++;
            if (!Double.isNaN(with.start[at]) && with.fit[at] >= BACK_IN_FIT) {
                a = with;
                taken--;
            } else {
                gone[i] = true;
            }
        }
        }
        sung = without(all, gone);
        r.leftOut = taken;

        int n = sung.size();
        double[] t = a.start.clone();
        int placed = 0;
        double fitSum = 0;
        for (int i = 0; i < n; i++) {
            if (Double.isNaN(t[i])) continue;
            placed++;
            fitSum += a.fit[i];
        }
        // Lyrics of another song, another language, a voice the model cannot follow: not trusted
        if (placed < n * 0.8 || fitSum / Math.max(1, placed) < MIN_FIT) {
            r.leftOut = 0;
            return r;
        }
        fillGaps(t);

        // The repeats, counted in beats so that a drifting tempo does not matter
        String[] keys = new String[n];
        double[] beat = new double[n];
        for (int i = 0; i < n; i++) {
            keys[i] = key(sung.get(i).text);
            beat[i] = map.timeToTick(t[i]) / (double) TempoMap.RESOLUTION;
        }
        // Sure of a line: its letters fitted well, or the lyric file puts it where the voice does
        // (allowing for how far ahead of the singing this file's times run)
        boolean[] sure = new boolean[n];
        double lead = 0;
        if (timesKnown) {
            double[] ahead = new double[n];
            for (int i = 0; i < n; i++) ahead[i] = t[i] - sung.get(i).time;
            Arrays.sort(ahead);
            lead = ahead[n / 2];
        }
        boolean[] heardWell = new boolean[n];
        int agreeing = 0;
        for (int i = 0; i < n; i++) {
            heardWell[i] = !Double.isNaN(a.start[i]) && a.fit[i] >= SURE_FIT;
            sure[i] = timesKnown && Math.abs(t[i] - sung.get(i).time - lead) <= 0.6;
            if (sure[i]) agreeing++;
        }
        List<String> log = new ArrayList<>();
        int corrected = 0, fromFile = 0;
        if (timesKnown && agreeing >= TRUSTED_SHARE * n) {
            // A lyric file the voice bears out on most lines is a good one. On the few lines where
            // the two differ, the voice decides only where it was heard clearly; elsewhere the
            // file is the better witness. The song's repeats are not needed.
            for (int i = 0; i < n; i++) {
                if (Math.abs(t[i] - sung.get(i).time - lead) > 1.0 && a.fit[i] < CLEAR_FIT) {
                    t[i] = Math.max(0, sung.get(i).time + lead);
                    fromFile++;
                }
            }
        } else {
            corrected = repeats(keys, beat, sure, heardWell, Math.max(1, beatsPerMeasure), AGREE_SEC * Math.max(40, bpm) / 60.0, log);
            for (int i = 0; i < n; i++) t[i] = map.tickToTime(Math.round(beat[i] * TempoMap.RESOLUTION));
        }
        for (int i = 1; i < n; i++) if (t[i] <= t[i - 1] + 0.2) t[i] = t[i - 1] + 0.3;

        List<Lrc.Line> out = new ArrayList<>(n);
        int moved = 0;
        for (int i = 0; i < n; i++) {
            Lrc.Line l = sung.get(i);
            if (!timesKnown || Math.abs(t[i] - l.time) > 0.5) moved++;
            out.add(new Lrc.Line(Math.max(0, t[i]), l.text, l.singer));
        }
        r.lines = out;
        r.heard = a.start;
        r.fit = a.fit;
        r.repeatNotes = log;
        r.note = String.format(Locale.US, "The lyrics were lined up with the voice word by word: %d of %d lines moved by more than half a second%s.",
                moved, n, corrected > 0 ? ", " + corrected + " of them set by the song's repeats" : "")
                + (fromFile > 0 ? " " + fromFile + " line" + (fromFile == 1 ? "" : "s") + " heard unclearly kept the lyric file's time." : "")
                + (r.leftOut > 0 ? " " + r.leftOut + " line" + (r.leftOut == 1 ? " is" : "s are") + " not sung in this recording and "
                + (r.leftOut == 1 ? "was" : "were") + " left out." : "");
        return r;
    }

    private static List<Lrc.Line> without(List<Lrc.Line> lines, boolean[] out) {
        List<Lrc.Line> kept = new ArrayList<>();
        for (int i = 0; i < lines.size(); i++) if (!out[i]) kept.add(lines.get(i));
        return kept;
    }

    private static double[] without(double[] values, boolean[] out) {
        if (values == null) return null;
        int n = 0;
        for (boolean b : out) if (!b) n++;
        double[] kept = new double[n];
        int at = 0;
        for (int i = 0; i < values.length; i++) if (!out[i]) kept[at++] = values[i];
        return kept;
    }

    /** Lines that could not be aligned (no letters) go between their neighbours. */
    private static void fillGaps(double[] t) {
        int n = t.length;
        for (int i = 0; i < n; i++) {
            if (!Double.isNaN(t[i])) continue;
            int a = i - 1, b = i + 1;
            while (b < n && Double.isNaN(t[b])) b++;
            double before = a >= 0 ? t[a] : Double.NaN, after = b < n ? t[b] : Double.NaN;
            if (!Double.isNaN(before) && !Double.isNaN(after)) t[i] = before + (after - before) / (b - a);
            else if (!Double.isNaN(before)) t[i] = before + 3.0;
            else if (!Double.isNaN(after)) t[i] = Math.max(0, after - 3.0 * (b - i));
            else t[i] = 0;
        }
    }

    /** The letters of a line without asides in brackets: lines with the same key are the same words. */
    public static String key(String text) {
        String k = text.replaceAll("\\([^)]*\\)", "").toLowerCase(Locale.US).replaceAll("[^a-z]", "");
        return k.isEmpty() ? text.toLowerCase(Locale.US).replaceAll("[^a-z]", "") : k;
    }

    /**
     * Puts lines that break the song's repeats where the repeats say.
     *
     * A block is a run of lines that comes back at least three times with the same words in the
     * same order (a hook; a two-line outro sung round and round).
     * <ol>
     *   <li>The blocks' first lines lie a whole number of bars apart: whichever of 16, 8, 4
     *       or 2 bars most of them keep (at least three and 60%, and no shorter than the block
     *       takes to sing) is the block's period, and the
     *       others move to the nearest place that keeps it (straight after the pass before, when
     *       the passes follow each other evenly).</li>
     *   <li>Passes sung straight after one another with no such period (a fading outro): where
     *       three in a row keep a whole number of bars, the rest follow on at that pace.</li>
     *   <li>Inside a block each line is the same distance after the first one in every pass: where
     *       at least three passes and a third of them agree, the others follow.</li>
     * </ol>
     *
     * @param beat       where each line starts, in beats; corrected in place
     * @param agreeBeats how close counts as the same place
     * @return how many lines were moved
     */
    public static int repeats(String[] keys, double[] beat, int beatsPerMeasure, double agreeBeats, List<String> log) {
        return repeats(keys, beat, null, beatsPerMeasure, agreeBeats, log);
    }

    /**
     * @param sure lines that were heard well, or where the lyric file says the same as the voice:
     *             these stay where they are whatever the repeats say (a last line held back on
     *             purpose breaks the pattern and is right). Null: none.
     */
    public static int repeats(String[] keys, double[] beat, boolean[] sure, int beatsPerMeasure, double agreeBeats, List<String> log) {
        return repeats(keys, beat, sure, null, beatsPerMeasure, agreeBeats, log);
    }

    /**
     * @param sure      lines the lyric file confirms: never moved
     * @param heardWell lines whose letters fitted well: moved only by whole periods, since the
     *                  same words in another pass of the block fit just as well, but not off the
     *                  pattern to somewhere else. Null: none.
     */
    public static int repeats(String[] keys, double[] beat, boolean[] sure, boolean[] heardWell, int beatsPerMeasure, double agreeBeats,
                              List<String> log) {
        int n = keys.length;
        if (sure == null) sure = new boolean[n];
        if (heardWell == null) heardWell = new boolean[n];
        Map<String, List<Integer>> where = new HashMap<>();
        for (int i = 0; i < n; i++) {
            if (keys[i].isEmpty()) continue;
            List<Integer> list = where.get(keys[i]);
            if (list == null) where.put(keys[i], list = new ArrayList<>());
            list.add(i);
        }
        boolean[] used = new boolean[n];
        int moved = 0;
        for (int i = 0; i < n; i++) {
            if (used[i] || keys[i].isEmpty()) continue;
            List<Integer> occ = new ArrayList<>();
            for (int j : where.get(keys[i])) if (!used[j]) occ.add(j);
            int m = occ.size();
            if (m < 3) continue;
            int length = 1;
            grow:
            while (true) {
                int first = occ.get(0) + length;
                if (first >= n || keys[first].isEmpty() || where.get(keys[first]).size() < m) break;
                for (int j : occ) {
                    int k = j + length;
                    if (k >= n || used[k] || occ.contains(k) || !keys[k].equals(keys[first])) break grow;
                }
                length++;
            }
            for (int j : occ) for (int d = 0; d < length; d++) used[j + d] = true;

            // 1. The first lines of the passes, a whole number of bars apart
            double[] start = new double[m];
            for (int q = 0; q < m; q++) start[q] = beat[occ.get(q)];
            double blockPeriod = 0;
            // A block cannot come round faster than it takes to sing: from its first line to its
            // last in the middle pass, and a beat for the last line itself
            double[] spans = new double[m];
            for (int q = 0; q < m; q++) spans[q] = beat[occ.get(q) + length - 1] - beat[occ.get(q)];
            Arrays.sort(spans);
            double shortest = Math.max(2 * beatsPerMeasure, spans[m / 2] + 1);
            // The period most passes keep; of two that as many keep, the longer (it says more).
            // Short periods are not asked: almost anything fits a period of a bar by chance.
            int bestBars = 0, bestCount = 0;
            for (int bars : new int[] {16, 8, 4, 2}) {
                double period = bars * beatsPerMeasure;
                if (period < shortest) continue;
                double[] phase = new double[m];
                for (int q = 0; q < m; q++) phase[q] = start[q] % period;
                int count = 0;
                for (boolean b : cluster(phase, agreeBeats, period)) if (b) count++;
                if (count > bestCount) {
                    bestCount = count;
                    bestBars = bars;
                }
            }
            if (bestCount >= Math.max(3, (int) Math.ceil(0.6 * m))) {
                int bars = bestBars;
                double period = bars * beatsPerMeasure;
                double[] phase = new double[m];
                for (int q = 0; q < m; q++) phase[q] = start[q] % period;
                boolean[] in = cluster(phase, agreeBeats, period);
                double ref = Double.NaN;
                List<Double> around = new ArrayList<>();
                for (int q = 0; q < m; q++) {
                    if (!in[q]) continue;
                    if (Double.isNaN(ref)) ref = phase[q];
                    around.add(((phase[q] - ref + period / 2) % period + period) % period - period / 2);
                }
                java.util.Collections.sort(around);
                double middle = ref + around.get(around.size() / 2);
                for (int q = 0; q < m; q++) {
                    if (in[q] || sure[occ.get(q)] || heardWell[occ.get(q)]) continue;
                    double target = start[q];
                    if (q >= 2 && occ.get(q) - occ.get(q - 1) == occ.get(q - 1) - occ.get(q - 2)) {
                        target = start[q - 1] + (start[q - 1] - start[q - 2]);
                    }
                    double fixed = middle + Math.round((target - middle) / period) * period;
                    if (log != null) log.add(String.format(Locale.US, "line %d: moved %+.1f beats to keep the %d-bar repeat", occ.get(q) + 1, fixed - start[q], bars));
                    start[q] = fixed;
                    moved++;
                }
                blockPeriod = period;
            }
            // Passes sung straight after one another (an outro going round and round) with no
            // period most of them keep: the voice fades, ad-libs take over, and the letters could
            // be laid many ways. Where three passes in a row do keep a whole number of bars, the
            // rest follow on at that pace, one after the other.
            if (blockPeriod == 0) {
                for (int q = 0; q + 2 < m && blockPeriod == 0; q++) {
                    if (occ.get(q + 1) != occ.get(q) + length || occ.get(q + 2) != occ.get(q + 1) + length) continue;
                    double step = (start[q + 2] - start[q]) / 2;
                    double period = Math.round(step / beatsPerMeasure) * beatsPerMeasure;
                    if (period <= 0) continue;
                    double mid = start[q + 1];
                    if (Math.abs(start[q] + period - mid) > agreeBeats || Math.abs(start[q + 2] - period - mid) > agreeBeats) continue;
                    blockPeriod = period;
                    for (int k = q + 3; k < m && occ.get(k) == occ.get(k - 1) + length; k++) {
                        double expected = start[k - 1] + period;
                        if (Math.abs(start[k] - expected) <= agreeBeats || sure[occ.get(k)]
                                || (heardWell[occ.get(k)] && !wholePeriods(expected - start[k], period, agreeBeats))) continue;
                        if (log != null) log.add(String.format(Locale.US, "line %d: moved %+.1f beats to follow straight on, %d bars after the pass before",
                                occ.get(k) + 1, expected - start[k], (int) (period / beatsPerMeasure)));
                        start[k] = expected;
                        moved++;
                    }
                    for (int k = q - 1; k >= 0 && occ.get(k + 1) == occ.get(k) + length; k--) {
                        double expected = start[k + 1] - period;
                        if (Math.abs(start[k] - expected) <= agreeBeats || sure[occ.get(k)]
                                || (heardWell[occ.get(k)] && !wholePeriods(expected - start[k], period, agreeBeats))) continue;
                        if (log != null) log.add(String.format(Locale.US, "line %d: moved %+.1f beats to lead straight on, %d bars before the pass after",
                                occ.get(k) + 1, expected - start[k], (int) (period / beatsPerMeasure)));
                        start[k] = expected;
                        moved++;
                    }
                }
            }
            for (int q = 0; q < m; q++) beat[occ.get(q)] = start[q];

            // 2. Inside the block, each line in the same place in every pass: the same place in
            //    the period when there is one (which does not lean on where each pass's first
            //    line was heard), else the same distance after the pass's first line
            for (int d = 1; d < length; d++) {
                double[] place = new double[m];
                for (int q = 0; q < m; q++) {
                    place[q] = blockPeriod > 0 ? ((beat[occ.get(q) + d] % blockPeriod) + blockPeriod) % blockPeriod
                            : beat[occ.get(q) + d] - beat[occ.get(q)];
                }
                boolean[] in = cluster(place, agreeBeats, blockPeriod);
                List<Double> agreed = new ArrayList<>();
                double ref = Double.NaN;
                for (int q = 0; q < m; q++) {
                    if (!in[q]) continue;
                    if (Double.isNaN(ref)) ref = place[q];
                    // On the circle of the period, measured from the first that agrees
                    agreed.add(blockPeriod > 0 ? ((place[q] - ref + blockPeriod / 2) % blockPeriod + blockPeriod) % blockPeriod - blockPeriod / 2 + ref : place[q]);
                }
                if (agreed.size() < 3 || agreed.size() < 0.34 * m) continue;
                java.util.Collections.sort(agreed);
                double usual = agreed.get(agreed.size() / 2);
                for (int q = 0; q < m; q++) {
                    int line = occ.get(q) + d;
                    double from = beat[occ.get(q)];
                    // With a period: the place with the usual phase that follows the pass's first line
                    double target = blockPeriod > 0 ? from + (((usual - from) % blockPeriod) + blockPeriod) % blockPeriod : from + usual;
                    if (sure[line] || (in[q] && Math.abs(beat[line] - target) <= agreeBeats)) continue;
                    if (heardWell[line] && !wholePeriods(target - beat[line], blockPeriod, agreeBeats)) continue;
                    if (log != null) log.add(String.format(Locale.US, "line %d: moved %+.1f beats to where it is in %d other passes", line + 1,
                            target - beat[line], agreed.size()));
                    beat[line] = target;
                    moved++;
                }
            }
        }
        return moved;
    }

    /** True when {@code move} is one or more whole periods (the same words in another pass of the block). */
    private static boolean wholePeriods(double move, double period, double tol) {
        if (period <= 0) return false;
        long k = Math.round(move / period);
        return k != 0 && Math.abs(move - k * period) <= tol;
    }

    /** The largest set of values within {@code tol} of one of them; on a circle of {@code period} when that is above 0. */
    private static boolean[] cluster(double[] v, double tol, double period) {
        boolean[] best = new boolean[v.length];
        int bestCount = 0;
        for (double c : v) {
            boolean[] in = new boolean[v.length];
            int count = 0;
            for (int i = 0; i < v.length; i++) {
                double d = Math.abs(v[i] - c);
                if (period > 0) d = Math.min(d, period - d);
                if (d <= tol) {
                    in[i] = true;
                    count++;
                }
            }
            if (count > bestCount) {
                bestCount = count;
                best = in;
            }
        }
        return best;
    }
}
