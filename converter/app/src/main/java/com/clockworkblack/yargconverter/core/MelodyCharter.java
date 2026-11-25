package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

/**
 * R&amp;B mode: a guitar chart that follows the lead singer instead of the band.
 *
 * In R&amp;B, soul and a lot of pop the song is carried by the voice; the instruments sit back and
 * leave it room. A chart made from the guitar then feels like playing the accompaniment. This one
 * is made from the separated vocals, so the notes come where the words and runs are sung, the
 * frets climb when the melody climbs, and a held note is a sustain.
 *
 * It reuses the guitar charter (grid, difficulties, frets, sustains, Star Power) and changes what
 * it is fed:
 *  - the onsets are found in the vocal stem, and each one's note is the one the voice tracker
 *    heard, which reads a single voice far better than the general pitch reading does;
 *  - a run (the voice moving through notes inside one held word) has no new attack to find, so
 *    every place the voice settles on a new note for a moment is added as an onset of its own;
 *  - a voice sings one note at a time, so there are no chords.
 */
public final class MelodyCharter {
    private MelodyCharter() {}

    /** Seconds the voice must stay on a new note, within a semitone, before it counts as a note of a run. */
    static final double SETTLE_SEC = 0.06;
    /** A run note closer than this to an onset already found is the same note. */
    static final double SAME_NOTE_SEC = 0.07;
    /** How strong a run note is, against the song's solid hits (1). Strong enough for Expert and Hard. */
    static final float RUN_STRENGTH = 0.8f;

    /**
     * @param vocals the separated vocals, measured like any other instrument
     * @param voice  the same vocals as heard by the voice tracker
     */
    public static GuitarCharter.Chart build(Features vocals, VocalAnalyzer.Voice voice, TempoMap map, double bpm,
                                            GuitarCharter.Options options) {
        List<GuitarCharter.Onset> onsets = onsets(vocals, voice);
        GuitarCharter.Options lead = options == null ? new GuitarCharter.Options() : options.copy();
        lead.chords = false;
        GuitarCharter.Chart chart = GuitarCharter.build(vocals, onsets, map, bpm, lead);
        if (voice != null && voice.frames() > 0) holdWithTheVoice(chart, voice, map);
        return chart;
    }

    /** Shortest sustain worth showing, in ticks (half a beat: a slow song's held word). */
    static final long MIN_SUSTAIN = TempoMap.RESOLUTION / 2;
    /** Ticks left between the end of a sustain and the next note (a sixteenth), so the player can let go. */
    static final long RELEASE_GAP = TempoMap.RESOLUTION / 4;

    /**
     * Sustains that last as long as the singer holds the note. The guitar charter judges sustains
     * by how long a sound rings, which suits a guitar; a voice is easier to read directly: a note
     * lasts while the voice stays on it.
     */
    private static void holdWithTheVoice(GuitarCharter.Chart chart, VocalAnalyzer.Voice voice, TempoMap map) {
        float gate = voice.gate();
        for (int d = 0; d < chart.notes.size(); d++) {
            List<GuitarCharter.Note> notes = chart.notes.get(d);
            List<GuitarCharter.Note> out = new ArrayList<>(notes.size());
            for (int i = 0; i < notes.size(); i++) {
                GuitarCharter.Note n = notes.get(i);
                double start = map.tickToTime(n.tick);
                double end = heldUntil(voice, gate, start);
                long sustain = n.sustain;
                if (end > start) {
                    long held = map.timeToTick(end) - n.tick;
                    if (i + 1 < notes.size()) held = Math.min(held, notes.get(i + 1).tick - n.tick - RELEASE_GAP);
                    sustain = held >= MIN_SUSTAIN ? held : 0;
                }
                out.add(new GuitarCharter.Note(n.tick, n.lane, n.lane2, sustain));
            }
            chart.notes.set(d, out);
        }
    }

    /**
     * When the voice leaves the note it is on just after {@code start}: it moves more than a
     * semitone away for a moment, or stops for longer than a breath. Returns {@code start} when no
     * note is heard there.
     */
    static double heldUntil(VocalAnalyzer.Voice v, float gate, double start) {
        float note = noteAt(v, gate, start);
        if (Float.isNaN(note)) return start;
        int breath = Math.max(4, (int) Math.round(0.12 * v.fps));
        int away = Math.max(3, (int) Math.round(SETTLE_SEC * v.fps));
        int last = v.frameAt(start);
        int quiet = 0, moved = 0;
        for (int f = v.frameAt(start + 0.03); f < v.frames(); f++) {
            float m = v.midi[f];
            boolean voiced = !Float.isNaN(m) && v.level[f] > gate;
            if (!voiced) {
                if (++quiet >= breath) break;
                continue;
            }
            quiet = 0;
            if (Math.abs(m - note) > 1.0f) {
                if (++moved >= away) break;
                continue;
            }
            moved = 0;
            last = f;
        }
        return v.timeOf(last);
    }

    /** The onsets of the vocal stem, with the voice tracker's notes, plus the notes of runs. */
    static List<GuitarCharter.Onset> onsets(Features vocals, VocalAnalyzer.Voice voice) {
        List<GuitarCharter.Onset> found = GuitarCharter.detectOnsets(vocals, true);
        if (voice == null || voice.frames() == 0) return found;
        float gate = voice.gate();

        List<GuitarCharter.Onset> kept = new ArrayList<>(found.size());
        for (GuitarCharter.Onset o : found) {
            // Drums and keys always leak a little into a separated voice: a sound where nobody
            // is singing is not part of the melody
            if (!sounding(voice, gate, o.time)) continue;
            float note = noteAt(voice, gate, o.time);
            if (!Float.isNaN(note)) setNote(o, note);
            kept.add(o);
        }
        found = kept;

        List<GuitarCharter.Onset> runs = new ArrayList<>();
        for (double t : runNotes(voice, gate)) {
            if (near(found, t)) continue;
            int frame = vocals.frameAt(t);
            GuitarCharter.Onset o = new GuitarCharter.Onset(vocals.timeOf(frame), frame, RUN_STRENGTH);
            float note = noteAt(voice, gate, t);
            if (Float.isNaN(note)) continue;
            o.bright = vocals.bright[frame];
            setNote(o, note);
            runs.add(o);
        }
        if (runs.isEmpty()) return found;

        List<GuitarCharter.Onset> all = new ArrayList<>(found.size() + runs.size());
        all.addAll(found);
        all.addAll(runs);
        Collections.sort(all, new Comparator<GuitarCharter.Onset>() {
            @Override
            public int compare(GuitarCharter.Onset a, GuitarCharter.Onset b) {
                return Double.compare(a.time, b.time);
            }
        });
        return all;
    }

    /** Gives an onset a clean single note: a pitched sound, grouped by note name as a bass is. */
    private static void setNote(GuitarCharter.Onset o, float note) {
        o.tonal = true;
        o.height = note;
        o.conf = 0.9f;
        o.tonalShare = Math.max(o.tonalShare, 0.9f);
        int pc = Math.floorMod(Math.round(note), 12);
        float[] print = new float[12];
        print[pc] = 1f;
        o.print = print;
        o.group = pc;
    }

    /**
     * The note the voice is on just after {@code time}: the middle reading of its voiced frames
     * from 30 to 150 ms later (the attack of a word wobbles before it settles), or NaN when the
     * voice is silent there.
     */
    static float noteAt(VocalAnalyzer.Voice v, float gate, double time) {
        int a = v.frameAt(time + 0.03);
        int b = Math.min(v.frames(), v.frameAt(time + 0.15) + 1);
        float[] heard = new float[Math.max(1, b - a)];
        int count = 0;
        for (int f = a; f < b; f++) {
            if (!Float.isNaN(v.midi[f]) && v.level[f] > gate) heard[count++] = v.midi[f];
        }
        if (count < 3) return Float.NaN;
        Arrays.sort(heard, 0, count);
        return heard[count / 2];
    }

    /**
     * Times where the voice, still singing, settles on a new note: a held word moving through a
     * run, a riff, a slide up to the top of a phrase. The voice must stay within a semitone of the
     * new note for {@link #SETTLE_SEC}, and the new note must be at least a semitone from the last.
     */
    static List<Double> runNotes(VocalAnalyzer.Voice v, float gate) {
        List<Double> times = new ArrayList<>();
        int hold = Math.max(3, (int) Math.round(SETTLE_SEC * v.fps));
        float[] window = new float[hold];
        // A voice dips for a frame or two as it moves between notes; only a real gap (a breath,
        // the end of a word) ends the line of notes, and the next word is an onset of its own.
        int breath = Math.max(4, (int) Math.round(0.15 * v.fps));
        float current = Float.NaN;
        int run = 0;
        int quiet = 0;
        for (int f = 0; f < v.frames(); f++) {
            float m = v.midi[f];
            if (Float.isNaN(m) || v.level[f] <= gate) {
                if (++quiet >= breath) current = Float.NaN;
                run = 0;
                continue;
            }
            quiet = 0;
            if (run > 0 && Math.abs(m - window[0]) > 1.0f) run = 0;
            window[run++] = m;
            if (run < hold) continue;
            float[] sorted = window.clone();
            Arrays.sort(sorted);
            float settled = sorted[hold / 2];
            run = 0;
            if (Float.isNaN(current)) {
                current = settled;
                continue;
            }
            if (Math.abs(settled - current) >= 0.9f) {
                // A leap of more than an octave inside one breath is the tracker slipping, not the tune
                if (Math.abs(settled - current) <= 12f) times.add(v.timeOf(f - hold + 1));
                current = settled;
            }
        }
        return times;
    }

    /** True when the voice is heard at all in the 150 ms after {@code time} (sung, rapped or spoken). */
    static boolean sounding(VocalAnalyzer.Voice v, float gate, double time) {
        int a = v.frameAt(time);
        int b = Math.min(v.frames(), v.frameAt(time + 0.15) + 1);
        for (int f = a; f < b; f++) {
            if (v.level[f] > gate) return true;
        }
        return false;
    }

    /** Below this share of the song sung (a mostly instrumental track), the band is charted instead. */
    public static final double MIN_SINGING_SHARE = 0.15;

    /** How much of the song the voice is heard in, 0 to 1. */
    public static double singingShare(VocalAnalyzer.Voice v) {
        if (v == null || v.frames() == 0) return 0;
        float gate = v.gate();
        int heard = 0;
        for (int f = 0; f < v.frames(); f++) if (v.level[f] > gate) heard++;
        return (double) heard / v.frames();
    }

    private static boolean near(List<GuitarCharter.Onset> onsets, double t) {
        int lo = 0, hi = onsets.size() - 1;
        while (lo <= hi) {
            int mid = (lo + hi) >>> 1;
            double d = onsets.get(mid).time - t;
            if (Math.abs(d) < SAME_NOTE_SEC) return true;
            if (d < 0) lo = mid + 1;
            else hi = mid - 1;
        }
        return false;
    }
}
