package com.clockworkblack.yargconverter.core;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

/**
 * Writes a song's charts as a Rock Band style "notes.mid", the one chart format the game reads
 * singing from. Guitar and bass go in as before; lyrics become a vocals track with a note under
 * every word, and a song with two singers gets a part for each.
 *
 * Track by track:
 *   tempo        beat lengths and time signatures
 *   EVENTS       practice sections
 *   PART GUITAR  four difficulties (Easy from note 60, Medium 72, Hard 84, Expert 96; green first)
 *   PART BASS    the same
 *   PART VOCALS  every line: note 105 spans a phrase, a lyric sits on each sung note
 *   HARM1/HARM2  the first and second singer's lines, when the song has two
 */
public final class MidiSong {
    private MidiSong() {}

    /** Lowest note of each guitar difficulty; the five frets follow upwards from it. */
    static final int[] DIFFICULTY_BASE = {60, 72, 84, 96};
    static final int PHRASE_NOTE = 105;
    static final int STAR_POWER_NOTE = 116;
    /** Length written for a note that is not held. Anything under a third of a beat is not a sustain. */
    static final long SHORT_NOTE = TempoMap.RESOLUTION / 8;

    // At the same tick, ends come before starts, and a phrase opens before its first word.
    private static final int FIRST = -1, OFF = 0, PHRASE_OFF = 1, PHRASE_ON = 2, TEXT = 3, ON = 4;

    private static final class Event {
        final long tick;
        final int order;
        final int seq;
        final byte[] data;

        Event(long tick, int order, int seq, byte[] data) {
            this.tick = tick;
            this.order = order;
            this.seq = seq;
            this.data = data;
        }
    }

    static final class Track {
        final List<Event> events = new ArrayList<>();

        Track(String name) {
            meta(0, FIRST, 0x03, name);
        }

        void meta(long tick, int order, int type, String text) {
            byte[] bytes = text.getBytes(StandardCharsets.UTF_8);
            ByteArrayOutputStream out = new ByteArrayOutputStream(bytes.length + 6);
            out.write(0xFF);
            out.write(type);
            writeVarLen(out, bytes.length);
            out.write(bytes, 0, bytes.length);
            add(tick, order, out.toByteArray());
        }

        void raw(long tick, int order, int... bytes) {
            byte[] data = new byte[bytes.length];
            for (int i = 0; i < bytes.length; i++) data[i] = (byte) bytes[i];
            add(tick, order, data);
        }

        /** A note from {@code tick} lasting {@code length} ticks (at least one). */
        void note(int pitch, long tick, long length, boolean phrase) {
            if (length < 1) length = 1;
            raw(tick, phrase ? PHRASE_ON : ON, 0x90, pitch, 100);
            raw(tick + length, phrase ? PHRASE_OFF : OFF, 0x80, pitch, 0);
        }

        private void add(long tick, int order, byte[] data) {
            events.add(new Event(Math.max(0, tick), order, events.size(), data));
        }

        byte[] bytes() {
            List<Event> sorted = new ArrayList<>(events);
            Collections.sort(sorted, new Comparator<Event>() {
                @Override
                public int compare(Event a, Event b) {
                    if (a.tick != b.tick) return Long.compare(a.tick, b.tick);
                    if (a.order != b.order) return Integer.compare(a.order, b.order);
                    return Integer.compare(a.seq, b.seq);
                }
            });
            ByteArrayOutputStream out = new ByteArrayOutputStream(events.size() * 5 + 16);
            long at = 0;
            for (Event e : sorted) {
                writeVarLen(out, e.tick - at);
                at = e.tick;
                out.write(e.data, 0, e.data.length);
            }
            // End of track.
            out.write(0x00);
            out.write(0xFF);
            out.write(0x2F);
            out.write(0x00);
            return out.toByteArray();
        }
    }

    /** MIDI's variable-length number: seven bits a byte, high bit set on all but the last. */
    static void writeVarLen(ByteArrayOutputStream out, long value) {
        if (value < 0) value = 0;
        if (value > 0x0FFFFFFFL) value = 0x0FFFFFFFL;
        int shift = 21;
        boolean started = false;
        for (; shift > 0; shift -= 7) {
            int part = (int) ((value >> shift) & 0x7F);
            if (part != 0 || started) {
                out.write(part | 0x80);
                started = true;
            }
        }
        out.write((int) (value & 0x7F));
    }

    public static void write(File file, SongWriter.Result r) throws IOException {
        byte[] bytes = build(r);
        try (OutputStream out = new FileOutputStream(file)) {
            out.write(bytes);
        }
    }

    public static byte[] build(SongWriter.Result r) {
        List<Track> tracks = new ArrayList<>();
        tracks.add(tempo(r.tempo));
        tracks.add(events(r));
        tracks.add(instrument("PART GUITAR", r.chart));
        if (r.bassChart != null) tracks.add(instrument("PART BASS", r.bassChart));
        VocalCharter.Chart v = r.vocals;
        if (v != null && !v.phrases.isEmpty()) {
            tracks.add(vocals("PART VOCALS", v, -1, true));
            if (v.twoParts()) {
                // The first part carries the phrase marks for the whole song: the game takes the
                // second part's scoring phrases from there.
                tracks.add(vocals("HARM1", v, VocalCharter.PART_FIRST, true));
                tracks.add(vocals("HARM2", v, VocalCharter.PART_SECOND, false));
            }
        }

        ByteArrayOutputStream out = new ByteArrayOutputStream(1 << 16);
        ascii(out, "MThd");
        int32(out, 6);
        int16(out, 1);
        int16(out, tracks.size());
        int16(out, TempoMap.RESOLUTION);
        for (Track t : tracks) {
            byte[] body = t.bytes();
            ascii(out, "MTrk");
            int32(out, body.length);
            out.write(body, 0, body.length);
        }
        return out.toByteArray();
    }

    private static Track tempo(TempoMap map) {
        Track t = new Track("tempo");
        // A short first measure holds any beats before the first real "one".
        int full = map.beatsPerMeasure;
        long origin = map.origin();
        timeSignature(t, 0, map.pickupBeats > 0 ? map.pickupBeats : full);
        if (map.pickupBeats > 0) timeSignature(t, origin, full);
        for (int i = 0; i < map.tick.length; i++) {
            long us = map.microsPerBeat[i];
            t.raw(map.tick[i], TEXT, 0xFF, 0x51, 0x03, (int) (us >> 16) & 0xFF, (int) (us >> 8) & 0xFF, (int) us & 0xFF);
        }
        return t;
    }

    private static void timeSignature(Track t, long tick, int beats) {
        // beats / 4, with the usual metronome fields (24 clocks a beat, 8 thirty-seconds a beat).
        t.raw(tick, PHRASE_ON, 0xFF, 0x58, 0x04, Math.max(1, Math.min(32, beats)), 2, 24, 8);
    }

    /** Practice sections every 16 measures. */
    private static Track events(SongWriter.Result r) {
        Track t = new Track("EVENTS");
        TempoMap map = r.tempo;
        long endTick = map.timeToTick(r.duration);
        int part = 1;
        for (int m = 0; map.measureStart(m) < endTick; m += 16) {
            long tick = m == 0 ? 0 : map.measureStart(m);
            t.meta(tick, TEXT, 0x01, "[section Part " + part++ + "]");
        }
        return t;
    }

    private static Track instrument(String name, GuitarCharter.Chart chart) {
        Track t = new Track(name);
        for (int d = GuitarCharter.EASY; d <= GuitarCharter.EXPERT; d++) {
            List<GuitarCharter.Note> notes = chart.notes.get(d);
            for (int i = 0; i < notes.size(); i++) {
                GuitarCharter.Note n = notes.get(i);
                long length = n.sustain > 0 ? n.sustain : SHORT_NOTE;
                if (i + 1 < notes.size()) length = Math.min(length, notes.get(i + 1).tick - n.tick);
                t.note(DIFFICULTY_BASE[d] + n.lane, n.tick, length, false);
                if (n.lane2 >= 0) t.note(DIFFICULTY_BASE[d] + n.lane2, n.tick, length, false);
            }
        }
        List<GuitarCharter.Note> expert = chart.notes.get(GuitarCharter.EXPERT);
        long free = 0;
        for (long[] p : chart.starPower) {
            if (p[0] < free || p[1] < 1 || !hasNoteIn(expert, p[0], p[0] + p[1])) continue;
            t.note(STAR_POWER_NOTE, p[0], p[1], true);
            free = p[0] + p[1];
        }
        return t;
    }

    private static boolean hasNoteIn(List<GuitarCharter.Note> notes, long a, long b) {
        for (GuitarCharter.Note n : notes) {
            if (n.tick >= b) return false;
            if (n.tick >= a) return true;
        }
        return false;
    }

    /**
     * @param which {@link VocalCharter#PART_FIRST} or {@link VocalCharter#PART_SECOND} for one
     *              singer's lines (lines for both go to each), or -1 for every line
     * @param markAll write the phrase mark of every line, even those this singer sits out
     */
    private static Track vocals(String name, VocalCharter.Chart chart, int which, boolean markAll) {
        Track t = new Track(name);
        for (VocalCharter.Phrase p : chart.phrases) {
            boolean mine = which < 0 || p.part == which || p.part == VocalCharter.PART_BOTH;
            if (!mine && !markAll) continue;
            t.note(PHRASE_NOTE, p.tick, p.length, true);
            if (p.starPower && which != VocalCharter.PART_SECOND) t.note(STAR_POWER_NOTE, p.tick, p.length, true);
            if (!mine) continue;
            for (VocalCharter.Note n : p.notes) {
                // "+" joins a held word on to a new note; "#" marks a spoken word.
                String lyric = n.slide ? "+" : n.text + (n.spoken ? "#" : "");
                t.meta(n.tick, TEXT, 0x05, lyric);
                t.note(n.pitch, n.tick, n.length, false);
            }
        }
        return t;
    }

    private static void ascii(ByteArrayOutputStream out, String s) {
        for (int i = 0; i < s.length(); i++) out.write(s.charAt(i));
    }

    private static void int32(ByteArrayOutputStream out, int v) {
        out.write((v >>> 24) & 0xFF);
        out.write((v >>> 16) & 0xFF);
        out.write((v >>> 8) & 0xFF);
        out.write(v & 0xFF);
    }

    private static void int16(ByteArrayOutputStream out, int v) {
        out.write((v >>> 8) & 0xFF);
        out.write(v & 0xFF);
    }
}
