package com.clockworkblack.yargconverter.core;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/**
 * A standard MIDI file taken apart into tracks of timed events and put back together, for
 * editing a song's notes.mid without disturbing anything that is not being edited.
 *
 * Reading accepts what other charting tools write too (running status, any number of tracks,
 * SysEx). Writing always spells out every status byte, which every reader accepts.
 */
public final class MidiFile {
    /** Ticks per beat. */
    public int resolution;
    public final List<Track> tracks = new ArrayList<>();

    public static final class Event {
        public final long tick;
        /** The whole message: status byte and data, or FF type length data for meta, F0/F7 length data for SysEx. */
        public final byte[] data;

        public Event(long tick, byte[] data) {
            this.tick = tick;
            this.data = data;
        }

        public int status() {
            return data[0] & 0xFF;
        }

        public boolean isNoteOn() {
            return (status() & 0xF0) == 0x90 && data.length >= 3 && (data[2] & 0xFF) > 0;
        }

        /** Note off, or note on with no velocity (which means the same). */
        public boolean isNoteOff() {
            int kind = status() & 0xF0;
            return data.length >= 3 && (kind == 0x80 || (kind == 0x90 && (data[2] & 0xFF) == 0));
        }

        public int pitch() {
            return data[1] & 0x7F;
        }

        public int channel() {
            return status() & 0x0F;
        }

        public boolean isMeta(int type) {
            return status() == 0xFF && data.length >= 2 && (data[1] & 0xFF) == type;
        }

        /** The text of a meta event. */
        public String text() {
            int at = 2;
            int length = 0;
            int c;
            do {
                c = data[at++] & 0xFF;
                length = (length << 7) | (c & 0x7F);
            } while ((c & 0x80) != 0);
            return new String(data, at, Math.min(length, data.length - at), StandardCharsets.UTF_8);
        }
    }

    public static final class Track {
        public String name = "";
        /** In file order; the end-of-track mark is left out (it is put back when writing). */
        public final List<Event> events = new ArrayList<>();
    }

    public static MidiFile read(byte[] b) throws IOException {
        if (b.length < 14 || b[0] != 'M' || b[1] != 'T' || b[2] != 'h' || b[3] != 'd') {
            throw new IOException("This is not a MIDI file.");
        }
        int headerLength = int32(b, 4);
        MidiFile file = new MidiFile();
        int count = int16(b, 10);
        int division = int16(b, 12);
        if ((division & 0x8000) != 0) throw new IOException("MIDI files timed in frames are not supported.");
        file.resolution = division;
        int at = 8 + headerLength;
        for (int i = 0; i < count && at + 8 <= b.length; i++) {
            boolean isTrack = b[at] == 'M' && b[at + 1] == 'T' && b[at + 2] == 'r' && b[at + 3] == 'k';
            int length = int32(b, at + 4);
            at += 8;
            if (length < 0 || at + length > b.length) throw new IOException("The MIDI file is cut short.");
            if (isTrack) file.tracks.add(readTrack(b, at, at + length));
            else i--; // Unknown chunks are skipped and do not count as tracks
            at += length;
        }
        return file;
    }

    private static Track readTrack(byte[] b, int at, int end) throws IOException {
        Track t = new Track();
        long tick = 0;
        int running = 0;
        while (at < end) {
            long delta = 0;
            int c;
            do {
                if (at >= end) throw new IOException("A MIDI track is cut short.");
                c = b[at++] & 0xFF;
                delta = (delta << 7) | (c & 0x7F);
            } while ((c & 0x80) != 0);
            tick += delta;
            if (at >= end) throw new IOException("A MIDI track is cut short.");
            int status = b[at] & 0xFF;
            int start = at;
            byte[] data;
            if (status == 0xFF) {
                at += 2;
                int[] len = varLen(b, at);
                at = len[1] + len[0];
                data = copy(b, start, at, end);
                if ((data[1] & 0xFF) == 0x2F) break;
                if ((data[1] & 0xFF) == 0x03 && t.name.isEmpty()) t.name = new Event(tick, data).text();
            } else if (status == 0xF0 || status == 0xF7) {
                at += 1;
                int[] len = varLen(b, at);
                at = len[1] + len[0];
                data = copy(b, start, at, end);
            } else {
                boolean hasStatus = status >= 0x80;
                if (hasStatus) {
                    running = status;
                    at++;
                } else if (running == 0) {
                    throw new IOException("A MIDI track has data without a status byte.");
                }
                int kind = running & 0xF0;
                int size = kind == 0xC0 || kind == 0xD0 ? 1 : 2;
                if (at + size > end) throw new IOException("A MIDI track is cut short.");
                data = new byte[1 + size];
                data[0] = (byte) running;
                System.arraycopy(b, at, data, 1, size);
                at += size;
            }
            t.events.add(new Event(tick, data));
        }
        return t;
    }

    private static int[] varLen(byte[] b, int at) throws IOException {
        int value = 0;
        int c;
        int guard = 0;
        do {
            if (at >= b.length || ++guard > 4) throw new IOException("A MIDI event has a bad length.");
            c = b[at++] & 0xFF;
            value = (value << 7) | (c & 0x7F);
        } while ((c & 0x80) != 0);
        return new int[] {value, at};
    }

    private static byte[] copy(byte[] b, int from, int to, int end) throws IOException {
        if (to > end) throw new IOException("A MIDI event runs past the end of its track.");
        byte[] out = new byte[to - from];
        System.arraycopy(b, from, out, 0, out.length);
        return out;
    }

    public Track track(String name) {
        for (Track t : tracks) if (t.name.equals(name)) return t;
        return null;
    }

    public byte[] write() {
        ByteArrayOutputStream out = new ByteArrayOutputStream(1 << 16);
        ascii(out, "MThd");
        int32(out, 6);
        int16(out, 1);
        int16(out, tracks.size());
        int16(out, resolution);
        for (Track t : tracks) {
            ByteArrayOutputStream body = new ByteArrayOutputStream(t.events.size() * 5 + 16);
            long at = 0;
            for (Event e : t.events) {
                MidiSong.writeVarLen(body, Math.max(0, e.tick - at));
                at = Math.max(at, e.tick);
                body.write(e.data, 0, e.data.length);
            }
            body.write(0x00);
            body.write(0xFF);
            body.write(0x2F);
            body.write(0x00);
            byte[] bytes = body.toByteArray();
            ascii(out, "MTrk");
            int32(out, bytes.length);
            out.write(bytes, 0, bytes.length);
        }
        return out.toByteArray();
    }

    private static int int32(byte[] b, int at) {
        return ((b[at] & 0xFF) << 24) | ((b[at + 1] & 0xFF) << 16) | ((b[at + 2] & 0xFF) << 8) | (b[at + 3] & 0xFF);
    }

    private static int int16(byte[] b, int at) {
        return ((b[at] & 0xFF) << 8) | (b[at + 1] & 0xFF);
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
