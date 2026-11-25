import com.clockworkblack.yargconverter.core.ChartEdit;
import com.clockworkblack.yargconverter.core.MidiFile;
import com.clockworkblack.yargconverter.core.MidiSong;
import com.clockworkblack.yargconverter.core.SongWriter;

import java.io.File;
import java.nio.file.Files;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** The note editor: opening a converted song, changing notes, saving, and nothing else changing. */
final class EditTest {
    private static int failures;

    private static void check(boolean ok, String what) {
        if (!ok) {
            failures++;
            System.out.println("  FAIL: " + what);
        }
    }

    /**
     * {@code java EditTest <song folder>}: edits the folder's notes.mid the way a player would
     * (takes away the first Expert guitar note, adds a held chord) and updates expect.txt to
     * match, for the check that runs the game's own song scanner and loader on it.
     */
    public static void main(String[] args) throws Exception {
        File dir = new File(args[0]);
        File mid = new File(dir, "notes.mid");
        ChartEdit edit = ChartEdit.open(Files.readAllBytes(mid.toPath()));
        ChartEdit.Part guitar = edit.parts.get(0);
        List<ChartEdit.Note> notes = edit.notes(guitar, 3, 0, Long.MAX_VALUE);
        ChartEdit.Note first = notes.get(0);
        long gap = -1;
        for (int i = 1; i + 1 < notes.size() && gap < 0; i++) {
            if (notes.get(i + 1).tick - notes.get(i).tick >= 4L * edit.resolution()) gap = notes.get(i).tick + 2L * edit.resolution();
        }
        if (gap < 0) gap = notes.get(notes.size() - 1).tick + 4L * edit.resolution();
        edit.remove(guitar, 3, first.tick);
        edit.toggle(guitar, 3, gap, 1);
        edit.toggle(guitar, 3, gap, 3);
        edit.setLength(guitar, 3, gap, edit.resolution());
        Files.write(mid.toPath(), edit.save());

        File expectFile = new File(dir, "expect.txt");
        Map<String, Integer> expect = new LinkedHashMap<>();
        for (String line : Files.readAllLines(expectFile.toPath())) {
            int eq = line.indexOf('=');
            if (eq > 0) expect.put(line.substring(0, eq).trim(), Integer.parseInt(line.substring(eq + 1).trim()));
        }
        boolean firstChord = Integer.bitCount(first.lanes) > 1;
        expect.put("guitar_3_held", expect.get("guitar_3_held") - (edit.isHeld(first) ? 1 : 0) + 1);
        expect.put("guitar_3_chords", expect.get("guitar_3_chords") - (firstChord ? 1 : 0) + 1);
        StringBuilder sb = new StringBuilder();
        for (Map.Entry<String, Integer> e : expect.entrySet()) sb.append(e.getKey()).append('=').append(e.getValue()).append('\n');
        Files.write(expectFile.toPath(), sb.toString().getBytes("UTF-8"));
        System.out.println("Edited: took away the note at tick " + first.tick + ", added a held red and blue chord at tick " + gap);
    }

    static int run(SongWriter.Result r) throws Exception {
        System.out.println("Note editor");
        byte[] original = MidiSong.build(r);
        ChartEdit edit = ChartEdit.open(original);
        check(!edit.parts.isEmpty(), "guitar and bass are editable");
        ChartEdit.Part guitar = edit.parts.get(0);
        check(guitar.trackName.equals("PART GUITAR"), "first part is guitar");
        int expert = guitar.count(3);
        check(expert > 20, "expert guitar has notes (" + expert + ")");

        // Saving with no changes gives a file the game reads exactly as before
        byte[] same = edit.save();
        for (String p : MidiCheck.verify(same, r)) check(false, "saved unchanged: " + p);
        check(trackBytes(original, "PART VOCALS").equals(trackBytes(same, "PART VOCALS")), "vocals untouched");
        check(trackBytes(original, "EVENTS").equals(trackBytes(same, "EVENTS")), "sections untouched");
        check(nonFret(original, "PART GUITAR").equals(nonFret(same, "PART GUITAR")), "star power and other guitar events untouched");

        // Remove the first note, add a chord in an empty spot, hold a note
        List<ChartEdit.Note> notes = edit.notes(guitar, 3, 0, Long.MAX_VALUE);
        ChartEdit.Note first = notes.get(0);
        edit.remove(guitar, 3, first.tick);
        long gap = -1;
        for (int i = 1; i + 1 < notes.size(); i++) {
            if (notes.get(i + 1).tick - notes.get(i).tick >= 4L * edit.resolution()) {
                gap = notes.get(i).tick + 2L * edit.resolution();
                break;
            }
        }
        if (gap < 0) gap = notes.get(notes.size() - 1).tick + 4L * edit.resolution();
        edit.toggle(guitar, 3, gap, 0);
        edit.toggle(guitar, 3, gap, 2);
        edit.setLength(guitar, 3, gap, edit.resolution());
        edit.toggle(guitar, 0, gap, 1);
        edit.toggle(guitar, 0, gap, 1); // added then taken away again
        int easy = guitar.count(0);
        check(edit.hasChanges(), "edits noticed");

        ChartEdit again = ChartEdit.open(edit.save());
        ChartEdit.Part g2 = again.parts.get(0);
        check(g2.count(3) == expert, "one note gone, one added (" + g2.count(3) + " vs " + expert + ")");
        check(again.noteAt(g2, 3, first.tick, -1, 0) == null, "removed note stays gone");
        ChartEdit.Note added = again.noteAt(g2, 3, gap, -1, 0);
        check(added != null && added.lanes == 0b101, "added chord is green and yellow");
        check(added != null && added.length == edit.resolution() && again.isHeld(added), "added chord is held a beat");
        check(g2.count(0) == easy, "easy unchanged by add then remove");
        check(trackBytes(original, "PART VOCALS").equals(trackBytes(edit.save(), "PART VOCALS")), "vocals untouched after edits");

        // Undo puts everything back
        while (edit.canUndo()) edit.undo();
        check(Arrays.equals(edit.save(), same), "undoing everything gives the original back");

        // Hold length stops at the next note
        List<ChartEdit.Note> n = edit.notes(guitar, 3, 0, Long.MAX_VALUE);
        edit.beginDrag(guitar, 3);
        edit.setLength(guitar, 3, n.get(0).tick, 1_000_000);
        ChartEdit.Note held = edit.noteAt(guitar, 3, n.get(0).tick, -1, 0);
        check(held.length == n.get(1).tick - n.get(0).tick, "hold stops at the next note");

        // Copying expert down squeezes onto the frets easy has
        edit.copyDifficulty(guitar, 3, 0);
        boolean fits = true;
        for (ChartEdit.Note x : edit.notes(guitar, 0, 0, Long.MAX_VALUE)) fits &= x.lanes < 8;
        check(fits && guitar.count(0) == guitar.count(3), "expert copied to easy uses three frets");

        // Time conversions agree with each other
        long t = n.get(5).tick;
        check(Math.abs(edit.timeToTickExact(edit.tickToTime(t)) - t) < 1e-6, "tick and time round trip");
        check(edit.snap(edit.tickToTime(t) + 0.001, 4) % (edit.resolution() / 4) == 0, "snapping lands on the grid");

        // Files from other tools use running status; read one
        byte[] running = runningStatusFile();
        ChartEdit other = ChartEdit.open(running);
        check(other.parts.get(0).count(3) == 3, "running status file read (" + other.parts.get(0).count(3) + " notes)");
        check(other.resolution() == 480, "other resolution kept");
        return failures;
    }

    private static String trackBytes(byte[] mid, String name) throws Exception {
        MidiFile f = MidiFile.read(mid);
        MidiFile.Track t = f.track(name);
        if (t == null) return "none";
        StringBuilder sb = new StringBuilder();
        for (MidiFile.Event e : t.events) sb.append(e.tick).append(':').append(Arrays.toString(e.data)).append(';');
        return sb.toString();
    }

    private static String nonFret(byte[] mid, String name) throws Exception {
        MidiFile.Track t = MidiFile.read(mid).track(name);
        StringBuilder sb = new StringBuilder();
        for (MidiFile.Event e : t.events) {
            boolean note = (e.isNoteOn() || e.isNoteOff());
            int p = note ? e.pitch() : -1;
            boolean fret = note && ((p >= 60 && p < 65) || (p >= 72 && p < 77) || (p >= 84 && p < 89) || (p >= 96 && p < 101));
            if (!fret) sb.append(e.tick).append(':').append(Arrays.toString(e.data)).append(';');
        }
        return sb.toString();
    }

    /** A tiny two-track file at 480 ticks a beat, notes written with running status and note-on velocity 0 as off. */
    private static byte[] runningStatusFile() {
        java.io.ByteArrayOutputStream o = new java.io.ByteArrayOutputStream();
        byte[] head = {'M', 'T', 'h', 'd', 0, 0, 0, 6, 0, 1, 0, 2, 0x01, (byte) 0xE0};
        o.write(head, 0, head.length);
        byte[] t0 = {0, (byte) 0xFF, 0x51, 3, 0x07, (byte) 0xA1, 0x20, 0, (byte) 0xFF, 0x2F, 0};
        track(o, t0);
        byte[] name = "PART GUITAR".getBytes();
        java.io.ByteArrayOutputStream b = new java.io.ByteArrayOutputStream();
        b.write(0); b.write(0xFF); b.write(0x03); b.write(name.length); b.write(name, 0, name.length);
        b.write(0); b.write(0x90); b.write(96); b.write(100);
        b.write(0x60); b.write(96); b.write(0);           // running status, velocity 0 = off
        b.write(0x60); b.write(97); b.write(100);
        b.write(0); b.write(98); b.write(100);            // chord with 97
        b.write(0x60); b.write(97); b.write(0);
        b.write(0); b.write(98); b.write(0);
        b.write(0x83); b.write(0x60); b.write(100); b.write(100);
        b.write(0x60); b.write(100); b.write(0);
        b.write(0); b.write(0xFF); b.write(0x2F); b.write(0);
        track(o, b.toByteArray());
        return o.toByteArray();
    }

    private static void track(java.io.ByteArrayOutputStream o, byte[] body) {
        o.write('M'); o.write('T'); o.write('r'); o.write('k');
        o.write(body.length >>> 24); o.write((body.length >>> 16) & 0xFF); o.write((body.length >>> 8) & 0xFF); o.write(body.length & 0xFF);
        o.write(body, 0, body.length);
    }
}
