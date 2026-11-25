package com.clockworkblack.yargconverter.core;

import java.io.IOException;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

/**
 * A song's notes.mid opened for hand editing: the guitar-style notes of each instrument and
 * difficulty can be added, removed and held longer or shorter, and the file saved again.
 *
 * Only those notes are rewritten. Everything else in the file (the tempo, the vocals, star
 * power, practice sections, anything another charting tool put there) is written back as it was.
 */
public final class ChartEdit {
    /** Instruments that use five frets, by the track name the game knows them by. */
    public static final String[] INSTRUMENT_TRACKS = {"PART GUITAR", "PART BASS", "PART RHYTHM", "PART GUITAR COOP", "PART KEYS"};
    public static final String[] INSTRUMENT_NAMES = {"Guitar", "Bass", "Rhythm", "Co-op guitar", "Keys"};
    public static final String[] DIFFICULTY_NAMES = {"Easy", "Medium", "Hard", "Expert"};
    public static final int LANES = 5;

    /** One strum: which frets (bit 0 green to bit 4 orange) and how long it is held, in ticks. */
    public static final class Note {
        public final long tick;
        public final int lanes;
        public final long length;

        public Note(long tick, int lanes, long length) {
            this.tick = tick;
            this.lanes = lanes;
            this.length = length;
        }

        public boolean has(int lane) {
            return (lanes & (1 << lane)) != 0;
        }
    }

    /** One instrument's track: its notes per difficulty, and everything else in the track kept as it is. */
    public static final class Part {
        public final String trackName;
        public final String name;
        @SuppressWarnings("unchecked")
        final TreeMap<Long, Note>[] notes = new TreeMap[4];
        final List<MidiFile.Event> kept = new ArrayList<>();
        final MidiFile.Track track;

        Part(String trackName, String name, MidiFile.Track track) {
            this.trackName = trackName;
            this.name = name;
            this.track = track;
            for (int d = 0; d < 4; d++) notes[d] = new TreeMap<>();
        }

        public int count(int difficulty) {
            return notes[difficulty].size();
        }
    }

    private final MidiFile file;
    public final List<Part> parts = new ArrayList<>();
    /** Tempo: tick and microseconds per beat of each change, in order, starting at tick 0. */
    private final long[] tempoTick;
    private final long[] tempoMicros;
    private final double[] tempoTime;
    /** Time signatures: tick and beats per measure of each change. */
    private final long[] signatureTick;
    private final int[] signatureBeats;

    private final Deque<Change> undo = new ArrayDeque<>();
    private boolean changed;

    /** What one edit replaced, to put it back. */
    private static final class Change {
        final Part part;
        final int difficulty;
        final TreeMap<Long, Note> before;

        Change(Part part, int difficulty, TreeMap<Long, Note> before) {
            this.part = part;
            this.difficulty = difficulty;
            this.before = before;
        }
    }

    private static final int MAX_UNDO = 200;

    private ChartEdit(MidiFile file) throws IOException {
        this.file = file;
        if (file.tracks.isEmpty() || file.resolution <= 0) throw new IOException("The song file has no notes in it.");

        // Tempo and time signatures are in the first track
        List<long[]> tempos = new ArrayList<>();
        List<long[]> signatures = new ArrayList<>();
        for (MidiFile.Event e : file.tracks.get(0).events) {
            if (e.isMeta(0x51) && e.data.length >= 6) {
                long us = ((e.data[3] & 0xFFL) << 16) | ((e.data[4] & 0xFFL) << 8) | (e.data[5] & 0xFFL);
                if (us > 0) tempos.add(new long[] {e.tick, us});
            } else if (e.isMeta(0x58) && e.data.length >= 4) {
                signatures.add(new long[] {e.tick, Math.max(1, e.data[3] & 0xFF)});
            }
        }
        if (tempos.isEmpty() || tempos.get(0)[0] > 0) tempos.add(0, new long[] {0, 500000});
        if (signatures.isEmpty() || signatures.get(0)[0] > 0) signatures.add(0, new long[] {0, 4});
        tempoTick = new long[tempos.size()];
        tempoMicros = new long[tempos.size()];
        tempoTime = new double[tempos.size()];
        double time = 0;
        for (int i = 0; i < tempos.size(); i++) {
            tempoTick[i] = tempos.get(i)[0];
            tempoMicros[i] = tempos.get(i)[1];
            if (i > 0) time += (tempoTick[i] - tempoTick[i - 1]) * tempoMicros[i - 1] / (1e6 * file.resolution);
            tempoTime[i] = time;
        }
        signatureTick = new long[signatures.size()];
        signatureBeats = new int[signatures.size()];
        for (int i = 0; i < signatures.size(); i++) {
            signatureTick[i] = signatures.get(i)[0];
            signatureBeats[i] = (int) signatures.get(i)[1];
        }

        for (int k = 0; k < INSTRUMENT_TRACKS.length; k++) {
            MidiFile.Track t = file.track(INSTRUMENT_TRACKS[k]);
            if (t != null) parts.add(readPart(t, INSTRUMENT_TRACKS[k], INSTRUMENT_NAMES[k]));
        }
        if (parts.isEmpty()) throw new IOException("The song has no guitar, bass or keys notes to edit.");
    }

    public static ChartEdit open(byte[] notesMid) throws IOException {
        return new ChartEdit(MidiFile.read(notesMid));
    }

    /** The difficulty a note number belongs to, or -1; the fret is {@code pitch - base}. */
    private static int difficultyOf(int pitch) {
        for (int d = 0; d < 4; d++) {
            int base = MidiSong.DIFFICULTY_BASE[d];
            if (pitch >= base && pitch < base + LANES) return d;
        }
        return -1;
    }

    private Part readPart(MidiFile.Track t, String trackName, String name) {
        Part part = new Part(trackName, name, t);
        Map<Integer, MidiFile.Event> open = new HashMap<>();
        for (MidiFile.Event e : t.events) {
            boolean fretNote = (e.isNoteOn() || e.isNoteOff()) && difficultyOf(e.pitch()) >= 0;
            if (!fretNote) {
                part.kept.add(e);
                continue;
            }
            int key = e.channel() * 128 + e.pitch();
            if (e.isNoteOn()) {
                if (!open.containsKey(key)) open.put(key, e);
                continue;
            }
            MidiFile.Event on = open.remove(key);
            if (on == null) continue;
            int d = difficultyOf(e.pitch());
            int lane = e.pitch() - MidiSong.DIFFICULTY_BASE[d];
            Note old = part.notes[d].get(on.tick);
            long length = Math.max(1, e.tick - on.tick);
            part.notes[d].put(on.tick, old == null
                    ? new Note(on.tick, 1 << lane, length)
                    : new Note(on.tick, old.lanes | (1 << lane), Math.max(old.length, length)));
        }
        return part;
    }

    // ------------------------------------------------------------------ time

    public int resolution() {
        return file.resolution;
    }

    public double tickToTime(long tick) {
        int i = tempoTick.length - 1;
        while (i > 0 && tempoTick[i] > tick) i--;
        return tempoTime[i] + (tick - tempoTick[i]) * tempoMicros[i] / (1e6 * file.resolution);
    }

    public double timeToTickExact(double time) {
        int i = tempoTime.length - 1;
        while (i > 0 && tempoTime[i] > time) i--;
        return tempoTick[i] + (time - tempoTime[i]) * 1e6 * file.resolution / tempoMicros[i];
    }

    /** The nearest tick on a grid of {@code division} notes per beat (4 = sixteenths). */
    public long snap(double time, int division) {
        double step = file.resolution / (double) Math.max(1, division);
        long tick = Math.round(Math.round(timeToTickExact(Math.max(0, time)) / step) * step);
        return Math.max(0, tick);
    }

    /** Whether a tick starts a measure (for drawing the bar lines). */
    public boolean isMeasureStart(long tick) {
        int i = signatureTick.length - 1;
        while (i > 0 && signatureTick[i] > tick) i--;
        long measure = (long) signatureBeats[i] * file.resolution;
        return (tick - signatureTick[i]) % measure == 0;
    }

    // ------------------------------------------------------------------ notes

    /** The notes of one instrument and difficulty from {@code fromTick} to {@code toTick}, in order. */
    public List<Note> notes(Part part, int difficulty, long fromTick, long toTick) {
        return new ArrayList<>(part.notes[difficulty].subMap(fromTick, true, toTick, true).values());
    }

    /** The note nearest to {@code tick} on {@code lane}, within {@code reach} ticks, or null. */
    public Note noteAt(Part part, int difficulty, long tick, int lane, long reach) {
        Note best = null;
        for (Note n : part.notes[difficulty].subMap(tick - reach, true, tick + reach, true).values()) {
            if (lane >= 0 && !n.has(lane)) continue;
            if (best == null || Math.abs(n.tick - tick) < Math.abs(best.tick - tick)) best = n;
        }
        return best;
    }

    private void remember(Part part, int difficulty) {
        undo.push(new Change(part, difficulty, new TreeMap<>(part.notes[difficulty])));
        while (undo.size() > MAX_UNDO) undo.removeLast();
        changed = true;
    }

    /** Adds the fret at a tick (making a chord with any note already there), or takes it away if it is there. */
    public void toggle(Part part, int difficulty, long tick, int lane) {
        remember(part, difficulty);
        TreeMap<Long, Note> map = part.notes[difficulty];
        Note old = map.get(tick);
        if (old == null) {
            map.put(tick, new Note(tick, 1 << lane, MidiSong.SHORT_NOTE));
        } else if (old.has(lane)) {
            int lanes = old.lanes & ~(1 << lane);
            if (lanes == 0) map.remove(tick);
            else map.put(tick, new Note(tick, lanes, old.length));
        } else {
            map.put(tick, new Note(tick, old.lanes | (1 << lane), old.length));
        }
    }

    /** Takes away a whole note (every fret of a chord). */
    public void remove(Part part, int difficulty, long tick) {
        if (!part.notes[difficulty].containsKey(tick)) return;
        remember(part, difficulty);
        part.notes[difficulty].remove(tick);
    }

    /** Begins a change made in many small steps (dragging out a held note): one undo for all of it. */
    public void beginDrag(Part part, int difficulty) {
        remember(part, difficulty);
    }

    /** Sets how long a note is held, up to the next note. A length under a third of a beat is not held. */
    public void setLength(Part part, int difficulty, long tick, long length) {
        TreeMap<Long, Note> map = part.notes[difficulty];
        Note old = map.get(tick);
        if (old == null) return;
        Long next = map.higherKey(tick);
        long most = next == null ? Long.MAX_VALUE : next - tick;
        long held = Math.max(MidiSong.SHORT_NOTE, Math.min(length, most));
        if (held < file.resolution / 3) held = MidiSong.SHORT_NOTE;
        map.put(tick, new Note(tick, old.lanes, held));
        changed = true;
    }

    /** Whether a held length counts as a sustain in the game (a third of a beat or more). */
    public boolean isHeld(Note n) {
        return n.length >= file.resolution / 3;
    }

    /** Copies one difficulty's notes over another's (say, Expert onto Hard, to thin out by hand). */
    public void copyDifficulty(Part part, int from, int to) {
        if (from == to) return;
        remember(part, to);
        part.notes[to] = new TreeMap<>(part.notes[from]);
        if (to < 3) {
            // Easier levels have fewer frets: Easy uses three, Medium four
            int top = to == 0 ? 2 : to == 1 ? 3 : 4;
            TreeMap<Long, Note> squeezed = new TreeMap<>();
            for (Note n : part.notes[to].values()) {
                int lanes = 0;
                for (int lane = 0; lane < LANES; lane++) if (n.has(lane)) lanes |= 1 << Math.min(lane, top);
                squeezed.put(n.tick, new Note(n.tick, lanes, n.length));
            }
            part.notes[to] = squeezed;
        }
    }

    public boolean canUndo() {
        return !undo.isEmpty();
    }

    /** Puts back the last change. Returns the part and difficulty it was in, or null. */
    public Object[] undo() {
        Change c = undo.poll();
        if (c == null) return null;
        c.part.notes[c.difficulty] = c.before;
        changed = true;
        return new Object[] {c.part, c.difficulty};
    }

    public boolean hasChanges() {
        return changed;
    }

    // ------------------------------------------------------------------ saving

    /** The song file with the edited notes in it. */
    public byte[] save() {
        for (Part part : parts) {
            List<MidiFile.Event> events = new ArrayList<>(part.kept);
            // At one tick, notes ending come before notes starting, so the game pairs them right
            List<long[]> order = new ArrayList<>();
            for (int d = 0; d < 4; d++) {
                List<Note> list = new ArrayList<>(part.notes[d].values());
                for (int i = 0; i < list.size(); i++) {
                    Note n = list.get(i);
                    long length = Math.max(1, n.length);
                    if (i + 1 < list.size()) length = Math.min(length, list.get(i + 1).tick - n.tick);
                    for (int lane = 0; lane < LANES; lane++) {
                        if (!n.has(lane)) continue;
                        int pitch = MidiSong.DIFFICULTY_BASE[d] + lane;
                        events.add(new MidiFile.Event(n.tick, new byte[] {(byte) 0x90, (byte) pitch, 100}));
                        events.add(new MidiFile.Event(n.tick + length, new byte[] {(byte) 0x80, (byte) pitch, 0}));
                    }
                }
            }
            events.sort((a, b) -> {
                if (a.tick != b.tick) return Long.compare(a.tick, b.tick);
                return Integer.compare(rank(a), rank(b));
            });
            part.track.events.clear();
            part.track.events.addAll(events);
        }
        changed = false;
        return file.write();
    }

    /** Within one tick: the track name first, then other meta and text, note offs, then note ons. */
    private static int rank(MidiFile.Event e) {
        if (e.isMeta(0x03)) return 0;
        if (e.status() == 0xFF || e.status() == 0xF0 || e.status() == 0xF7) return 1;
        if (e.isNoteOff()) return 2;
        return 3;
    }
}
