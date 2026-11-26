package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * Turns timed lyric lines into the notes a singer follows in the game.
 *
 * With the separated voice to listen to ({@link VocalAnalyzer}) each word is placed where it is
 * actually sung and given the note it is sung on; lines that are rapped or spoken become
 * "talkies", where any sound in time with the words counts and the note does not matter.
 * Without the voice every line is a talkie, spread over a natural speaking pace.
 */
public final class VocalCharter {
    private VocalCharter() {}

    /** Which singer a phrase belongs to. */
    public static final int PART_FIRST = 0, PART_SECOND = 1, PART_BOTH = 2;
    /** Lowest and highest notes the game's vocal charts can hold. */
    public static final int MIN_PITCH = 36, MAX_PITCH = 84;
    /** Note written for spoken words (the game ignores it; it only has to be in range). */
    static final int SPOKEN_PITCH = 60;
    /** A word held at least this long (seconds) is followed if the voice moves to other notes. */
    static final double HELD_WORD_SEC = 0.5;
    /** The same in R&amp;B mode, where shorter runs are followed too. */
    static final double RUN_WORD_SEC = 0.3;
    /** Lyric lines longer than this are cut into phrases of about equal length. */
    static final int MAX_WORDS = 9;

    /** One word to sing, or a held word moving on to another note. */
    public static final class Note {
        public long tick;
        public long length;
        /** MIDI note number, {@link #MIN_PITCH}..{@link #MAX_PITCH}. */
        public int pitch;
        /** Rapped or spoken: any sound counts. */
        public boolean spoken;
        /** The same word carrying on at a new note (no new text). */
        public boolean slide;
        public String text = "";

        // Working values in seconds, before the beat map turns them into ticks.
        double start, end;
    }

    /** A line (or part of a long line) shown and scored as one. */
    public static final class Phrase {
        public long tick;
        public long length;
        public int part = PART_FIRST;
        public boolean spoken;
        /** Earns the singer star power when sung well. */
        public boolean starPower;
        /** Index of the lyric line this came from. */
        public int line;
        public final List<Note> notes = new ArrayList<>();
        /** When the line is sung, in seconds (the same for every phrase cut from one line). */
        public double lineStart, lineEnd;
    }

    public static final class Chart {
        public final List<Phrase> phrases = new ArrayList<>();
        /** True when the separated voice was used; false when the words were spread by guesswork. */
        public boolean heard;
        public int lines, sungLines, spokenLines, words;
        /** What most of the song's clear-cut lines are: a borderline line goes the same way. */
        public boolean mostlySung;
        /** Lines given to the first singer, the second, and both. */
        public final int[] partLines = new int[3];

        /** Whether the song has lines for two different singers. */
        public boolean twoParts() {
            return partLines[PART_SECOND] > 0 && partLines[PART_FIRST] + partLines[PART_BOTH] > 0;
        }

        /** Gives every phrase of a line to a singer; {@code partOfLine} is indexed like the lyric lines. */
        public void assignParts(int[] partOfLine) {
            Arrays.fill(partLines, 0);
            int last = -1;
            for (Phrase p : phrases) {
                if (p.line >= 0 && p.line < partOfLine.length) p.part = partOfLine[p.line];
                if (p.line != last) partLines[p.part]++;
                last = p.line;
            }
        }
    }

    public static Chart build(List<Lrc.Line> lines, TempoMap map, double duration, VocalAnalyzer.Voice voice) {
        return build(lines, map, duration, voice, false);
    }

    /**
     * @param runs R&amp;B mode: follow the voice through runs and riffs. Shorter held words get
     *             joined-on notes too, and a word may move through more notes, so a melisma is
     *             sung note by note instead of as one or two long notes.
     */
    public static Chart build(List<Lrc.Line> lines, TempoMap map, double duration, VocalAnalyzer.Voice voice, boolean runs) {
        return build(lines, map, duration, voice, runs, null);
    }

    /**
     * @param onsets times (seconds, ascending) where a new sound starts in the full song, or
     *               null. Without the separated voice, words are moved onto these, so they fall
     *               on the rhythm of the song instead of drifting between its beats.
     */
    public static Chart build(List<Lrc.Line> lines, TempoMap map, double duration, VocalAnalyzer.Voice voice, boolean runs,
                              double[] onsets) {
        // Timing words from the lyric file's own pace and the song's sounds was tried and did not
        // place words better than a plain speaking pace (see RnbTest.normalModeKeepsPace), so it
        // is switched off: the code is kept for the next attempt.
        double pace = PACE_FROM_SONG ? songPace(lines, duration) : 0;
        if (!PACE_FROM_SONG) onsets = null;
        Chart chart = new Chart();
        if (lines == null) return chart;
        float gate = voice != null ? voice.gate() : 0f;
        // A first look at every line, so borderline lines can follow the rest of the song
        int[] verdict = voice != null ? clearVerdicts(lines, duration, voice, gate) : new int[lines.size()];
        boolean songLean = mostlySung(verdict);
        chart.mostlySung = songLean;
        long floor = 0;
        double previousEnd = 0;
        boolean anyHeard = false;
        for (int i = 0; i < lines.size(); i++) {
            Lrc.Line line = lines.get(i);
            // "[Chorus x 2]", "(instrumental)": notes in the lyrics file, not words to sing
            if (isAnnotation(line.text)) continue;
            List<String> words = words(line.text);
            if (words.isEmpty()) continue;
            double start = line.time;
            if (start >= duration - 0.2) break;
            double next = i + 1 < lines.size() ? lines.get(i + 1).time : duration;
            double limit = Math.max(Math.min(next, duration) - 0.06, start + 0.2);

            int n = words.size();
            double[] wordStart = new double[n];
            double[] wordEnd = new double[n];
            int[] frameStart = new int[n];
            int[] frameEnd = new int[n];
            boolean heard = voice != null && voice.frames() > 20
                    && timeFromVoice(voice, gate, start, limit, previousEnd, words, frameStart, frameEnd);
            if (heard) {
                for (int k = 0; k < n; k++) {
                    wordStart[k] = voice.timeOf(frameStart[k]);
                    wordEnd[k] = voice.timeOf(frameEnd[k]);
                }
            } else {
                timeByPace(start, limit, words, wordStart, wordEnd, pace);
                if (onsets != null) snapToOnsets(onsets, wordStart, wordEnd, limit);
            }

            // What is sung on each word, and is this line sung at all?
            List<Note> notes = new ArrayList<>();
            boolean leanSung = twinVerdict(lines, verdict, i, songLean);
            boolean sung = heard && pitchLine(voice, gate, words, frameStart, frameEnd, wordStart, wordEnd, notes, leanSung, runs);
            if (!sung) {
                notes.clear();
                for (int k = 0; k < n; k++) {
                    Note note = new Note();
                    note.text = words.get(k);
                    note.start = wordStart[k];
                    note.end = wordEnd[k];
                    note.pitch = SPOKEN_PITCH;
                    note.spoken = true;
                    notes.add(note);
                }
            }
            anyHeard |= heard;

            // Seconds to ticks, never letting two notes share a tick or run into each other.
            long last = floor;
            for (Note note : notes) {
                long t = map.timeToTick(note.start);
                if (t <= last) t = last + 1;
                note.tick = t;
                last = t;
            }
            final long gap = TempoMap.RESOLUTION / 16;
            for (int k = 0; k < notes.size(); k++) {
                Note note = notes.get(k);
                long end = map.timeToTick(note.end);
                long room = k + 1 < notes.size() ? notes.get(k + 1).tick - note.tick : Long.MAX_VALUE;
                long length = end - note.tick;
                if (room != Long.MAX_VALUE) {
                    boolean joined = notes.get(k + 1).slide;
                    length = Math.min(length, room - (joined || room <= gap * 2 ? 1 : gap));
                }
                note.length = Math.max(1, length);
            }

            // Long lines are shown a few words at a time.
            int chunks = (n + MAX_WORDS - 1) / MAX_WORDS;
            int perChunk = (n + chunks - 1) / chunks;
            int partOfLine = line.singer == Lrc.SECOND ? PART_SECOND : line.singer == Lrc.BOTH ? PART_BOTH : PART_FIRST;
            int at = 0;
            int wordsTaken = 0;
            double lineEnd = notes.get(notes.size() - 1).end;
            for (int c = 0; c < chunks; c++) {
                Phrase phrase = new Phrase();
                phrase.line = i;
                phrase.part = partOfLine;
                phrase.spoken = !sung;
                phrase.lineStart = notes.get(0).start;
                phrase.lineEnd = lineEnd;
                int wordsHere = 0;
                while (at < notes.size()) {
                    Note note = notes.get(at);
                    if (!note.slide) {
                        if (wordsHere == perChunk && c + 1 < chunks) break;
                        wordsHere++;
                    }
                    phrase.notes.add(note);
                    at++;
                }
                wordsTaken += wordsHere;
                if (phrase.notes.isEmpty()) continue;
                Note firstNote = phrase.notes.get(0);
                Note lastNote = phrase.notes.get(phrase.notes.size() - 1);
                phrase.tick = Math.max(floor, firstNote.tick - TempoMap.RESOLUTION / 8);
                long end = lastNote.tick + lastNote.length + TempoMap.RESOLUTION / 16;
                if (at < notes.size()) end = Math.min(end, notes.get(at).tick - 1);
                if (end <= lastNote.tick) end = lastNote.tick + 1;
                phrase.length = end - phrase.tick;
                floor = end;
                chart.phrases.add(phrase);
            }
            chart.words += wordsTaken;
            chart.lines++;
            if (sung) chart.sungLines++;
            else chart.spokenLines++;
            chart.partLines[partOfLine]++;
            previousEnd = lineEnd;
        }
        chart.heard = anyHeard;
        // Star power on a phrase every so often, as in the band games.
        for (int p = 4; p < chart.phrases.size(); p += 6) chart.phrases.get(p).starPower = true;
        return chart;
    }

    /** Words spread over a natural pace: all that can be done when only the start of the line is known. */
    /** Fastest and slowest a syllable is taken to be, in seconds, when the voice cannot be listened to. */
    static final double MIN_SYLLABLE_SEC = 0.11, MAX_SYLLABLE_SEC = 0.5;
    /** A word is moved onto a sound of the song that starts within this many seconds of it. */
    static final double SNAP_SEC = 0.09;

    /**
     * How long this song takes over a syllable, worked out from the lyric file alone: where one
     * line follows straight on from another, the singer filled the time between their stamps, so
     * that time divided by the line's syllables is the pace. The middle value over such lines is
     * used for the whole song: a rap comes out quick, a ballad slow.
     */
    static double songPace(List<Lrc.Line> lines, double duration) {
        if (lines == null) return 0.19;
        List<Double> paces = new ArrayList<>();
        for (int i = 0; i < lines.size(); i++) {
            List<String> words = words(lines.get(i).text);
            if (words.isEmpty()) continue;
            int syllables = 0;
            for (String w : words) syllables += syllables(w);
            double next = i + 1 < lines.size() ? lines.get(i + 1).time : duration;
            double per = (next - lines.get(i).time - 0.15) / syllables;
            // A longer wait than this is a pause or an instrumental break, not singing
            if (per >= MIN_SYLLABLE_SEC && per <= 0.6) paces.add(per);
        }
        if (paces.size() < 3) return 0.19;
        java.util.Collections.sort(paces);
        // The quicker side of the middle: stamps include the breath before the next line
        double pace = paces.get(paces.size() * 1 / 10) * 0.9;
        return Math.max(MIN_SYLLABLE_SEC, Math.min(MAX_SYLLABLE_SEC, pace));
    }

    /** Experiment, off: see {@link #build(List, TempoMap, double, VocalAnalyzer.Voice, boolean, double[])}. */
    static final boolean PACE_FROM_SONG = false;

    /** Spreads the words of a line from its time stamp at a speaking pace (or, in the experiment, the song's own). */
    private static void timeByPace(double start, double limit, List<String> words, double[] wordStart, double[] wordEnd, double pace) {
        if (pace <= 0) {
            int chars = 0;
            for (String w : words) chars += w.length() + 1;
            double natural = 0.4 + chars * 0.075;
            double end = Math.min(start + natural, limit);
            if (end < start + 0.2) end = start + 0.2;
            int seen = 0;
            for (int k = 0; k < words.size(); k++) {
                wordStart[k] = start + (end - start) * seen / chars;
                seen += words.get(k).length() + 1;
                wordEnd[k] = start + (end - start) * seen / chars;
            }
            return;
        }
        int total = 0;
        for (String w : words) total += syllables(w);
        double end = Math.min(start + total * pace, limit);
        if (end < start + 0.2) end = start + 0.2;
        int seen = 0;
        for (int k = 0; k < words.size(); k++) {
            wordStart[k] = start + (end - start) * seen / total;
            seen += syllables(words.get(k));
            // A short gap before the next word, so two words on one note are still two
            wordEnd[k] = start + (end - start) * seen / total - Math.min(0.03, (end - start) / total * 0.2);
        }
    }

    /**
     * Moves each word onto the nearest sound of the song that starts close to it, keeping the
     * words in order. Sung and rapped syllables land on the song's rhythm, so this pulls words
     * placed by pace onto the beat they are really said on.
     */
    static void snapToOnsets(double[] onsets, double[] wordStart, double[] wordEnd, double limit) {
        int n = wordStart.length;
        double floor = Double.NEGATIVE_INFINITY;
        for (int k = 0; k < n; k++) {
            int at = java.util.Arrays.binarySearch(onsets, wordStart[k]);
            if (at < 0) at = -at - 1;
            double best = Double.NaN;
            for (int j = Math.max(0, at - 1); j <= Math.min(onsets.length - 1, at); j++) {
                double d = Math.abs(onsets[j] - wordStart[k]);
                if (d <= SNAP_SEC && onsets[j] >= floor + 0.05 && (Double.isNaN(best) || d < Math.abs(best - wordStart[k]))) best = onsets[j];
            }
            if (!Double.isNaN(best)) {
                double shift = best - wordStart[k];
                wordStart[k] = best;
                // The word before now ends where this one begins, less a breath
                if (k > 0 && shift < 0) wordEnd[k - 1] = Math.min(wordEnd[k - 1], best - 0.02);
            }
            if (wordStart[k] < floor + 0.05) wordStart[k] = floor + 0.05;
            floor = wordStart[k];
        }
        for (int k = 0; k < n; k++) {
            double nextStart = k + 1 < n ? wordStart[k + 1] : limit;
            wordEnd[k] = Math.max(wordStart[k] + 0.04, Math.min(wordEnd[k], nextStart - 0.02));
        }
    }

    /**
     * Finds where a line is really sung and shares its words out over the moments the voice is
     * sounding, in proportion to their length, nudging each word onto the nearest point where a
     * new sound starts.
     *
     * @return false when the voice cannot be heard around this line (the caller falls back to pacing)
     */
    private static boolean timeFromVoice(VocalAnalyzer.Voice v, float gate, double start, double limit, double previousEnd,
                                         List<String> words, int[] frameStart, int[] frameEnd) {
        int n = words.size();
        int frames = v.frames();
        int stamp = v.frameAt(start);
        int fLimit = Math.min(frames, v.frameAt(limit) + 1);
        int fMin = Math.max(0, Math.max(v.frameAt(previousEnd) + 1, v.frameAt(start - 0.30)));
        if (fMin > stamp) fMin = stamp;

        // The stamp in a lyric file is close, not exact: slide to where this burst of singing begins.
        int f0;
        if (v.level[stamp] > gate) {
            int back = stamp;
            while (back - 1 >= fMin && v.level[back - 1] > gate) back--;
            f0 = back - 1 >= fMin ? back : stamp;
        } else {
            f0 = -1;
            int reach = Math.min(fLimit, v.frameAt(start + 1.5) + 1);
            for (int f = stamp; f < reach; f++) {
                if (v.level[f] > gate) {
                    f0 = f;
                    break;
                }
            }
            if (f0 < 0) return false;
        }

        // The line ends at the last sound before a clear pause (or before the next line).
        double maxLength = Math.max(2.5, n * 1.2 + 1.5);
        int fEnd = Math.min(fLimit, v.frameAt(v.timeOf(f0) + maxLength) + 1);
        int pause = (int) Math.round(0.9 * v.fps);
        int enough = Math.max(8, n * 4);
        int[] active = new int[Math.max(1, fEnd - f0)];
        int count = 0;
        int silent = 0;
        for (int f = f0; f < fEnd; f++) {
            if (v.level[f] > gate) {
                active[count++] = f;
                silent = 0;
            } else if (++silent >= pause && count >= enough) {
                break;
            }
        }
        if (count < Math.max(4, n * 2)) return false;
        int lastActive = active[count - 1];

        if (!align(v, gate, f0, lastActive, active, count, words, frameStart)) return false;

        // A word lasts until the next one, or until the voice stops.
        for (int k = 0; k < n; k++) {
            int until = k + 1 < n ? frameStart[k + 1] : lastActive + 1;
            int end = frameStart[k] + 1;
            for (int f = Math.min(until, frames) - 1; f > frameStart[k]; f--) {
                if (v.level[f] > gate) {
                    end = f + 1;
                    break;
                }
            }
            frameEnd[k] = Math.max(end, Math.min(until, frameStart[k] + 4));
        }
        return true;
    }

    /** Measurements of a stretch of voice, for judging sung against rapped (and for the build's logs). */
    public static final class Delivery {
        /** Share of the sounding time spent holding a note (see {@link #heldShare}). */
        public double held;
        /** Share of the sounding time with a clear note in it at all. */
        public double voiced;
        /** Share of the voiced time within a semitone of the local middle note: steady even with vibrato. */
        public double steady;
        /** Seconds of sound. */
        public double seconds;

        @Override
        public String toString() {
            return String.format(java.util.Locale.US, "held %.2f voiced %.2f steady %.2f over %.1fs", held, voiced, steady, seconds);
        }
    }

    /** Measures the voice between two times. */
    public static Delivery measure(VocalAnalyzer.Voice v, double fromSeconds, double toSeconds) {
        return measure(v, v.gate(), v.frameAt(fromSeconds), Math.min(v.frames(), v.frameAt(toSeconds) + 1));
    }

    static Delivery measure(VocalAnalyzer.Voice v, float gate, int a, int b) {
        Delivery d = new Delivery();
        d.held = heldShare(v, gate, a, b);
        int sounding = 0, voiced = 0, steady = 0;
        float[] window = new float[21];
        for (int f = a; f < b; f++) {
            if (v.level[f] <= gate) continue;
            sounding++;
            if (Float.isNaN(v.midi[f])) continue;
            voiced++;
            int k = 0;
            for (int g = Math.max(a, f - 10); g <= Math.min(b - 1, f + 10); g++) {
                if (!Float.isNaN(v.midi[g]) && v.level[g] > gate) window[k++] = v.midi[g];
            }
            Arrays.sort(window, 0, k);
            if (Math.abs(v.midi[f] - window[k / 2]) <= 1.0f) steady++;
        }
        d.voiced = sounding == 0 ? 0 : (double) voiced / sounding;
        d.steady = voiced == 0 ? 0 : (double) steady / voiced;
        d.seconds = sounding / v.fps;
        return d;
    }

    /** The one rule for sung against rapped or spoken, from a stretch's measurements, with nothing else known. */
    public static boolean judgeSung(Delivery d) {
        return d.held >= HELD_SHARE;
    }

    /**
     * The same, in a song: a line that is clearly one or the other is that, and a borderline line
     * (a sung line full of slides, a rapped line that leans into a few notes) goes the way most of
     * the song goes.
     */
    public static boolean judgeSung(Delivery d, boolean songMostlySung) {
        if (d.held >= HELD_SHARE) return true;
        if (d.held < BORDER_SHARE) return false;
        return songMostlySung;
    }

    /** Below this share of held notes a line is rapped or spoken, whatever the rest of the song does. */
    static final double BORDER_SHARE = 0.15;

    static final int UNCLEAR = 0, CLEARLY_SUNG = 1, CLEARLY_RAPPED = 2;

    /**
     * For every lyric line: clearly sung, clearly rapped, or in between, measured from its time
     * stamp to the next one (at most six seconds).
     */
    static int[] clearVerdicts(List<Lrc.Line> lines, double duration, VocalAnalyzer.Voice v, float gate) {
        int[] out = new int[lines.size()];
        for (int i = 0; i < lines.size(); i++) {
            Lrc.Line l = lines.get(i);
            if (l.text.trim().isEmpty() || isAnnotation(l.text) || l.time >= duration) continue;
            double end = Math.min(i + 1 < lines.size() ? lines.get(i + 1).time : duration, l.time + 6.0);
            if (end <= l.time + 0.5) continue;
            Delivery d = measure(v, gate, v.frameAt(l.time), Math.min(v.frames(), v.frameAt(end) + 1));
            if (d.seconds < 0.5) continue;
            out[i] = d.held >= HELD_SHARE ? CLEARLY_SUNG : d.held < BORDER_SHARE ? CLEARLY_RAPPED : UNCLEAR;
        }
        return out;
    }

    /**
     * Whether the song is a sung one. It takes a clear majority: rap, once separated from the
     * beat, often holds a few notes, so a song with sung hooks and rapped verses must not tip a
     * rapped verse over into singing.
     */
    static boolean mostlySung(int[] verdict) {
        int sung = 0, rapped = 0;
        for (int v : verdict) {
            if (v == CLEARLY_SUNG) sung++;
            else if (v == CLEARLY_RAPPED) rapped++;
        }
        return sung >= 3 && sung >= 2 * rapped;
    }

    /**
     * Which way a borderline line leans: the same words elsewhere in the song (a chorus comes
     * round again), going with most of those that were clear, or else the song as a whole.
     */
    public static boolean twinVerdict(List<Lrc.Line> lines, int[] verdict, int index, boolean songLean) {
        String text = normalised(lines.get(index).text);
        int sung = 0, rapped = 0;
        if (text.length() >= 4) {
            for (int j = 0; j < lines.size(); j++) {
                if (j == index || verdict[j] == UNCLEAR || !normalised(lines.get(j).text).equals(text)) continue;
                if (verdict[j] == CLEARLY_SUNG) sung++;
                else rapped++;
            }
        }
        // The repeats that were clear decide by majority; with none, or a tie, the song does
        return sung == rapped ? songLean : sung > rapped;
    }

    private static String normalised(String text) {
        return text == null ? "" : text.toLowerCase(java.util.Locale.US).replaceAll("[^a-z0-9]+", " ").trim();
    }

    /** A lyric line that is only a note to the reader: "[Chorus x 2]", "(Instrumental)". */
    static boolean isAnnotation(String text) {
        String t = text == null ? "" : text.trim();
        return t.length() >= 2 && t.matches("^[\\[(].*[\\])]$");
    }

    /** A line is sung when at least this much of its sounding time is spent on held notes. */
    static final double HELD_SHARE = 0.30;

    /**
     * How much of the time the voice sounds between two frames is spent holding a note: staying
     * within three quarters of a semitone of one pitch for at least 0.16 seconds.
     */
    static double heldShare(VocalAnalyzer.Voice v, float gate, int from, int to) {
        final int longEnough = (int) Math.round(0.16 * v.fps);
        final float tolerance = 0.75f;
        int sounding = 0, held = 0;
        int run = 0;
        double sum = 0;
        for (int f = from; f <= to; f++) {
            boolean inside = f < to;
            boolean on = inside && v.level[f] > gate;
            if (on) sounding++;
            float m = on ? v.midi[f] : Float.NaN;
            if (!Float.isNaN(m) && (run == 0 || Math.abs(m - sum / run) <= tolerance)) {
                run++;
                sum += m;
                continue;
            }
            if (run >= longEnough) held += run;
            run = 0;
            sum = 0;
            if (!Float.isNaN(m)) {
                run = 1;
                sum = m;
            }
        }
        return sounding == 0 ? 0 : (double) held / sounding;
    }

    /**
     * How much a word of the wrong length costs, against the reward for starting a word on a
     * clear new sound (see {@link #onsetStrength}). Kept low: where a voice comes back in after a
     * silence is a far better guide to where words start than counting syllables is.
     */
    private static final double LENGTH_WEIGHT = 2.0;

    /**
     * Decides where each word of a line starts. Every point where a new sound begins (the voice
     * comes in after a silence, gets suddenly louder, or jumps to another note) is a likely place
     * for a word to start, the clearer the better; and a word should last about as long as its
     * syllables suggest. The starts are chosen to fit both as well as possible over the whole line.
     */
    private static boolean align(VocalAnalyzer.Voice v, float gate, int f0, int lastActive, int[] active, int count,
                                 List<String> words, int[] frameStart) {
        int n = words.size();
        frameStart[0] = f0;
        if (n == 1) return true;

        // sounding[i]: how many frames the voice sounds in between f0 and f0 + i.
        int span = lastActive - f0 + 1;
        int[] sounding = new int[span + 1];
        int a = 0;
        for (int i = 0; i < span; i++) {
            sounding[i + 1] = sounding[i];
            if (a < count && active[a] == f0 + i) {
                sounding[i + 1]++;
                a++;
            }
        }

        double[] expected = new double[n];
        double total = 0;
        for (int k = 0; k < n; k++) {
            expected[k] = syllables(words.get(k)) + 0.4;
            total += expected[k];
        }
        for (int k = 0; k < n; k++) expected[k] = Math.max(1.0, expected[k] / total * count);

        // Places a word may start: every new sound, plus a plain grid so there is always a way through.
        int[] candidate = new int[span + 1];
        float[] strength = new float[span + 1];
        int c = 0;
        candidate[c++] = f0;
        for (int f = f0 + 1; f <= lastActive; f++) {
            float s = onsetStrength(v, gate, f);
            if (s > 0 || (f - f0) % 5 == 0) {
                candidate[c] = f;
                strength[c++] = s;
            }
        }
        if (c < n) return false;

        final int apart = 4;
        double[][] best = new double[n][c];
        int[][] via = new int[n][c];
        for (double[] row : best) Arrays.fill(row, Double.NEGATIVE_INFINITY);
        best[0][0] = 0;
        for (int k = 1; k < n; k++) {
            for (int j = k; j < c; j++) {
                double top = Double.NEGATIVE_INFINITY;
                int topFrom = -1;
                for (int i = k - 1; i < j; i++) {
                    double before = best[k - 1][i];
                    if (before == Double.NEGATIVE_INFINITY || candidate[j] - candidate[i] < apart) continue;
                    int length = Math.max(1, sounding[candidate[j] - f0] - sounding[candidate[i] - f0]);
                    double score = before - LENGTH_WEIGHT * Math.abs(Math.log(length / expected[k - 1]));
                    if (score > top) {
                        top = score;
                        topFrom = i;
                    }
                }
                if (topFrom >= 0) {
                    best[k][j] = top + strength[j];
                    via[k][j] = topFrom;
                }
            }
        }
        double top = Double.NEGATIVE_INFINITY;
        int at = -1;
        for (int j = n - 1; j < c; j++) {
            if (best[n - 1][j] == Double.NEGATIVE_INFINITY) continue;
            int length = Math.max(1, count - sounding[candidate[j] - f0]);
            double score = best[n - 1][j] - LENGTH_WEIGHT * Math.abs(Math.log(length / expected[n - 1]));
            if (score > top) {
                top = score;
                at = j;
            }
        }
        if (at < 0) return false;
        for (int k = n - 1; k >= 1; k--) {
            frameStart[k] = candidate[at];
            at = via[k][at];
        }
        return true;
    }

    /**
     * How clearly a new sound starts on a frame: 6 when the voice comes in after a silence, less
     * when it only gets suddenly louder or jumps to another note, 0 when nothing starts here.
     */
    static float onsetStrength(VocalAnalyzer.Voice v, float gate, int f) {
        if (f < 4 || v.level[f] <= gate) return 0f;
        if (v.level[f - 1] <= gate) return v.level[f - 2] <= gate ? 6f : 4f;
        float s = 0f;
        boolean louder = v.level[f] > 1.7f * v.level[f - 3];
        boolean alreadyLouder = v.level[f - 1] > 1.7f * v.level[f - 4];
        if (louder && !alreadyLouder) s = 2.5f;
        float now = v.midi[f], before = v.midi[f - 2];
        if (!Float.isNaN(now) && !Float.isNaN(before) && Math.abs(now - before) > 1.5f) {
            float earlier = v.midi[f - 1], evenEarlier = v.midi[f - 3];
            boolean alreadyJumped = !Float.isNaN(earlier) && !Float.isNaN(evenEarlier) && Math.abs(earlier - evenEarlier) > 1.5f;
            if (!alreadyJumped) s = Math.max(s, 2f);
        }
        return s;
    }

    /**
     * A rough count of the syllables in a word, good enough to share out time. In English and other
     * languages written with letters each run of vowels is one; in Korean, Chinese and Japanese
     * every character is one.
     */
    public static int syllables(String word) {
        int blocks = 0, letters = 0;
        for (int i = 0; i < word.length(); ) {
            int c = word.codePointAt(i);
            i += Character.charCount(c);
            if (isSyllableCharacter(c)) blocks++;
            else if (Character.isLetter(c)) letters++;
        }
        if (blocks > 0 && letters == 0) return blocks;

        String w = word.toLowerCase(java.util.Locale.ROOT);
        int count = 0;
        boolean inVowels = false, latin = false;
        for (int i = 0; i < w.length(); i++) {
            char c = w.charAt(i);
            if (c >= 'a' && c <= 'z') latin = true;
            if (!Character.isLetter(c)) continue;
            boolean vowel = VOWELS.indexOf(c) >= 0;
            if (vowel && !inVowels) count++;
            inVowels = vowel;
        }
        if (count == 0) return Math.max(1, blocks + (letters + 1) / 3);
        if (latin && count > 1 && w.endsWith("e") && !w.endsWith("le")) count--;
        return Math.max(1, count + blocks);
    }

    /** Vowels of the Latin (with accents), Cyrillic and Greek alphabets. */
    private static final String VOWELS = "aeiouy\u00e0\u00e1\u00e2\u00e3\u00e4\u00e5\u00e6\u00e8\u00e9\u00ea\u00eb\u00ec\u00ed\u00ee\u00ef"
            + "\u00f2\u00f3\u00f4\u00f5\u00f6\u00f8\u00f9\u00fa\u00fb\u00fc\u00fd\u00ff\u0153\u0131"
            + "\u0430\u0435\u0451\u0438\u043e\u0443\u044b\u044d\u044e\u044f\u0456\u0457\u0454"
            + "\u03b1\u03b5\u03b7\u03b9\u03bf\u03c5\u03c9\u03ac\u03ad\u03ae\u03af\u03cc\u03cd\u03ce";

    /** True for a character that is a syllable by itself: Korean blocks, Chinese characters, Japanese kana. */
    static boolean isSyllableCharacter(int c) {
        Character.UnicodeScript script = Character.UnicodeScript.of(c);
        return script == Character.UnicodeScript.HANGUL || script == Character.UnicodeScript.HAN
                || script == Character.UnicodeScript.HIRAGANA || script == Character.UnicodeScript.KATAKANA;
    }

    /**
     * Works out the note sung on each word and whether the line is sung at all.
     *
     * Singing holds notes: the voice sits on one pitch for a good part of a second, then moves to
     * another. Rap and speech never settle, the pitch keeps gliding and the syllables are short.
     * So a line counts as sung when a fair share of it is spent on held notes.
     *
     * @return true and fills {@code out} when the line is sung
     */
    private static boolean pitchLine(VocalAnalyzer.Voice v, float gate, List<String> words, int[] frameStart, int[] frameEnd,
                                     double[] wordStart, double[] wordEnd, List<Note> out, boolean leanSung, boolean runs) {
        int from0 = frameStart[0], to0 = Math.min(frameEnd[words.size() - 1], v.frames());
        if (!judgeSung(measure(v, gate, from0, to0), leanSung)) return false;
        return pitchWords(v, gate, words, frameStart, frameEnd, wordStart, wordEnd, out, runs);
    }

    private static boolean pitchWords(VocalAnalyzer.Voice v, float gate, List<String> words, int[] frameStart, int[] frameEnd,
                                     double[] wordStart, double[] wordEnd, List<Note> out, boolean runs) {
        int n = words.size();
        int from = frameStart[0], to = Math.min(frameEnd[n - 1], v.frames());

        float[] median = new float[n];
        float[] buf = new float[64];
        int heard = 0;
        for (int k = 0; k < n; k++) {
            int a = frameStart[k], b = Math.min(frameEnd[k], v.frames());
            if (buf.length < b - a) buf = new float[b - a];
            int voiced = 0;
            for (int f = a; f < b; f++) {
                if (!Float.isNaN(v.midi[f]) && v.level[f] > gate) buf[voiced++] = v.midi[f];
            }
            median[k] = Float.NaN;
            if (voiced < 3) continue;
            Arrays.sort(buf, 0, voiced);
            median[k] = buf[voiced / 2];
            heard++;
        }
        if (heard == 0) return false;

        // Words whose note could not be heard borrow their neighbour's.
        float[] known = new float[heard];
        int s = 0;
        for (int k = 0; k < n; k++) if (!Float.isNaN(median[k])) known[s++] = median[k];
        Arrays.sort(known);
        float lineNote = known[heard / 2];
        for (int k = 0; k < n; k++) {
            if (!Float.isNaN(median[k])) continue;
            float borrowed = Float.NaN;
            for (int d = 1; d < n && Float.isNaN(borrowed); d++) {
                if (k - d >= 0 && !Float.isNaN(median[k - d])) borrowed = median[k - d];
                else if (k + d < n && !Float.isNaN(median[k + d])) borrowed = median[k + d];
            }
            median[k] = Float.isNaN(borrowed) ? lineNote : borrowed;
        }
        // Bring the whole line into the range the game can show, an octave at a time.
        int shift = 0;
        while (lineNote + shift < MIN_PITCH + 5) shift += 12;
        while (lineNote + shift > MAX_PITCH - 5) shift -= 12;

        for (int k = 0; k < n; k++) {
            float note = median[k];
            // A word an octave away from the rest of the line is almost always a mishearing.
            while (note - lineNote > 9.5f) note -= 12f;
            while (lineNote - note > 9.5f) note += 12f;
            Note first = new Note();
            first.text = words.get(k);
            first.start = wordStart[k];
            first.end = wordEnd[k];
            first.pitch = clampPitch(Math.round(note) + shift);
            out.add(first);
            if (wordEnd[k] - wordStart[k] >= (runs ? RUN_WORD_SEC : HELD_WORD_SEC)) {
                addSlides(v, gate, frameStart[k], Math.min(frameEnd[k], v.frames()), note, shift, first, out, runs);
            }
        }
        return true;
    }

    /**
     * A long held word that moves to other notes (a run, an "oh-oh-oh") gets a joined-on note for
     * each place the voice settles on a new note for a moment.
     */
    private static void addSlides(VocalAnalyzer.Voice v, float gate, int a, int b, float startNote, int shift, Note word, List<Note> out,
                                  boolean runs) {
        // A run moves quickly: in R&B mode a note counts once the voice has sat on it for 50 ms
        // (70 ms otherwise), and a word may pass through up to eight of them.
        final int hold = runs ? 5 : 7;
        final int most = runs ? 8 : 3;
        final double gap = runs ? 0.08 : 0.12;
        float current = Float.NaN;
        // The note the word opens on: the middle value of its first voiced frames.
        float[] head = new float[hold];
        int h = 0;
        int f = a;
        for (; f < b && h < hold; f++) {
            if (!Float.isNaN(v.midi[f]) && v.level[f] > gate) head[h++] = v.midi[f];
        }
        if (h < hold) return;
        Arrays.sort(head);
        current = head[hold / 2];
        word.pitch = clampPitch(Math.round(nearOctave(current, startNote)) + shift);

        Note last = word;
        int run = 0;
        float[] moved = new float[hold];
        int added = 0;
        for (; f < b && added < most; f++) {
            float m = v.midi[f];
            if (Float.isNaN(m) || v.level[f] <= gate) {
                run = 0;
                continue;
            }
            if (Math.abs(m - current) < 1.5f || (run > 0 && Math.abs(m - moved[0]) > 1.0f)) {
                run = 0;
                continue;
            }
            moved[run++] = m;
            if (run < hold) continue;
            float[] sorted = moved.clone();
            Arrays.sort(sorted);
            float settled = sorted[hold / 2];
            int startFrame = f - hold + 1;
            double at = v.timeOf(startFrame);
            // Leave a short lead-in for the voice to travel between the two notes.
            if (at - last.start < gap) {
                run = 0;
                continue;
            }
            Note slide = new Note();
            slide.slide = true;
            slide.start = at;
            slide.end = last.end;
            slide.pitch = clampPitch(Math.round(nearOctave(settled, startNote)) + shift);
            // Within one word a voice moves by steps and small leaps. A jump of more than a fifth
            // is the listening slipping (a breath, a creak at the end of a note), not the tune.
            if (Math.abs(slide.pitch - last.pitch) > 7) {
                run = 0;
                continue;
            }
            last.end = at - 0.03;
            if (slide.pitch != last.pitch) {
                out.add(slide);
                last = slide;
                added++;
            } else {
                last.end = slide.end;
            }
            current = settled;
            run = 0;
        }
    }

    private static float nearOctave(float note, float reference) {
        while (note - reference > 9.5f) note -= 12f;
        while (reference - note > 9.5f) note += 12f;
        return note;
    }

    private static int clampPitch(int pitch) {
        while (pitch < MIN_PITCH) pitch += 12;
        while (pitch > MAX_PITCH) pitch -= 12;
        return pitch;
    }

    /** Splits a line into words that are safe to put in a vocal chart. */
    public static List<String> words(String text) {
        List<String> out = new ArrayList<>();
        if (text == null) return out;
        for (String raw : text.trim().split("[\\s\u3000]+")) {
            String w = raw.replace("\"", "").replace("\u201c", "").replace("\u201d", "")
                    .replace("[", "").replace("]", "");
            // These characters are control codes in vocal charts (joins, pitch flags, hidden text).
            w = w.replaceAll("^[$]+", "");
            w = w.replaceAll("[+#^%$/\u00a7_*]+$", "");
            if (w.endsWith("-")) w = w.substring(0, w.length() - 1) + "=";
            if (w.isEmpty()) continue;
            if (!splitUnspaced(w, out)) out.add(w);
        }
        return out;
    }

    /** Characters of Chinese or Japanese sung to one note when a line is written without spaces. */
    static final int UNSPACED_GROUP = 2;

    /**
     * Chinese and Japanese are written without spaces, so a whole line would be one "word" on one
     * note. Such a run is cut into pieces of two characters, each ending in "-", which a vocal
     * chart shows joined to the next piece with no space.
     *
     * @return false when the word is not such a run and was left alone
     */
    private static boolean splitUnspaced(String w, List<String> out) {
        int count = 0;
        for (int i = 0; i < w.length(); ) {
            int c = w.codePointAt(i);
            i += Character.charCount(c);
            Character.UnicodeScript script = Character.UnicodeScript.of(c);
            if (script == Character.UnicodeScript.HAN || script == Character.UnicodeScript.HIRAGANA
                    || script == Character.UnicodeScript.KATAKANA) count++;
        }
        if (count < 2 * UNSPACED_GROUP) return false;

        StringBuilder piece = new StringBuilder();
        int inPiece = 0;
        List<String> pieces = new ArrayList<>();
        for (int i = 0; i < w.length(); ) {
            int c = w.codePointAt(i);
            i += Character.charCount(c);
            boolean syllable = isSyllableCharacter(c);
            // Punctuation and small marks stay with the piece before them
            if (syllable && inPiece >= UNSPACED_GROUP) {
                pieces.add(piece.toString());
                piece.setLength(0);
                inPiece = 0;
            }
            piece.appendCodePoint(c);
            if (syllable) inPiece++;
        }
        if (piece.length() > 0) pieces.add(piece.toString());
        for (int k = 0; k < pieces.size(); k++) {
            String p = pieces.get(k);
            boolean lastPiece = k == pieces.size() - 1;
            if (!lastPiece) {
                // The join mark must follow a character, not a "-" or "=" of the text's own
                while (p.endsWith("-") || p.endsWith("=")) p = p.substring(0, p.length() - 1);
                if (p.isEmpty()) continue;
                p = p + "-";
            }
            out.add(p);
        }
        return true;
    }
}
