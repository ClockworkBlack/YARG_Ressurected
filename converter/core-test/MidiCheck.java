import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.TempoMap;
import com.clockworkblack.yargconverter.core.VocalCharter;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Reads a notes.mid back following the same rules as the game's own reader (YARG.Core's
 * MidReader, its song scanner and its vocals loader) and checks the song came through whole:
 * every guitar and bass note, every sung word with its note and lyric, and each singer's part.
 */
final class MidiCheck {
    static final class Ev {
        long tick;
        /** 0x90 note on, 0x80 note off, 0xFF meta. */
        int kind;
        int pitch, velocity;
        int metaType;
        byte[] data;

        String text() {
            return new String(data, StandardCharsets.UTF_8);
        }
    }

    static final class Track {
        String name = "";
        final List<Ev> events = new ArrayList<>();
    }

    static final class Note {
        long tick, length;
        int pitch;
    }

    /** One word as the game's vocals loader would see it. */
    static final class Word {
        long tick, length;
        int pitch;
        String lyric;
    }

    static final class Phrase {
        long tick, length;
        final List<Word> words = new ArrayList<>();
    }

    private final List<String> problems = new ArrayList<>();
    final List<Track> tracks = new ArrayList<>();
    int resolution;

    private void fail(String what) {
        if (problems.size() < 40) problems.add(what);
    }

    List<String> problems() {
        return problems;
    }

    Track track(String name) {
        for (Track t : tracks) if (t.name.equals(name)) return t;
        return null;
    }

    MidiCheck(byte[] b) {
        if (b.length < 14 || b[0] != 'M' || b[1] != 'T' || b[2] != 'h' || b[3] != 'd') {
            fail("no MThd header");
            return;
        }
        if (int32(b, 4) != 6) fail("header length is not 6");
        if (int16(b, 8) != 1) fail("not a format 1 file");
        int count = int16(b, 10);
        resolution = int16(b, 12);
        if (resolution != TempoMap.RESOLUTION) fail("resolution " + resolution);
        int at = 14;
        for (int i = 0; i < count; i++) {
            if (at + 8 > b.length || b[at] != 'M' || b[at + 1] != 'T' || b[at + 2] != 'r' || b[at + 3] != 'k') {
                fail("track " + i + " has no MTrk tag");
                return;
            }
            int length = int32(b, at + 4);
            at += 8;
            if (at + length > b.length) {
                fail("track " + i + " runs past the end of the file");
                return;
            }
            tracks.add(readTrack(b, at, at + length, i));
            at += length;
        }
        if (at != b.length) fail("bytes left over after the last track");
    }

    private Track readTrack(byte[] b, int at, int end, int index) {
        Track t = new Track();
        long tick = 0;
        boolean ended = false;
        boolean first = true;
        while (at < end) {
            long delta = 0;
            int c;
            int guard = 0;
            do {
                c = b[at++] & 0xFF;
                delta = (delta << 7) | (c & 0x7F);
                if (++guard > 4) {
                    fail("track " + index + ": over-long delta time");
                    return t;
                }
            } while ((c & 0x80) != 0 && at < end);
            tick += delta;
            if (at >= end) {
                fail("track " + index + ": ends inside an event");
                return t;
            }
            int status = b[at++] & 0xFF;
            Ev e = new Ev();
            e.tick = tick;
            if (status == 0xFF) {
                e.kind = 0xFF;
                e.metaType = b[at++] & 0xFF;
                int len = 0;
                do {
                    c = b[at++] & 0xFF;
                    len = (len << 7) | (c & 0x7F);
                } while ((c & 0x80) != 0);
                e.data = new byte[len];
                System.arraycopy(b, at, e.data, 0, len);
                at += len;
                if (e.metaType == 0x2F) {
                    ended = true;
                    if (at != end) fail("track " + index + ": data after the end-of-track mark");
                    break;
                }
                if (e.metaType == 0x03) {
                    // The game takes the name from the events at tick 0, and refuses two different ones.
                    if (tick != 0) fail("track " + index + ": track name not at the start");
                    if (!t.name.isEmpty()) fail("track " + index + ": named twice");
                    t.name = e.text();
                    if (!first) fail("track " + index + ": the name is not the first event (the game skips event 0 as the name)");
                }
            } else if ((status & 0xF0) == 0x90 || (status & 0xF0) == 0x80) {
                e.kind = status & 0xF0;
                e.pitch = b[at++] & 0xFF;
                e.velocity = b[at++] & 0xFF;
                if ((status & 0x0F) != 0) fail("track " + index + ": note not on channel 0");
                if (e.pitch > 127 || e.velocity > 127) fail("track " + index + ": bad note data");
                if (e.kind == 0x90 && e.velocity == 0) e.kind = 0x80;
            } else {
                fail("track " + index + ": unexpected status byte " + Integer.toHexString(status) + " (running status is not written)");
                return t;
            }
            first = false;
            t.events.add(e);
        }
        if (!ended) fail("track " + index + " (" + t.name + ") has no end-of-track mark");
        if (t.name.isEmpty()) fail("track " + index + " has no name");
        return t;
    }

    private static int int32(byte[] b, int at) {
        return ((b[at] & 0xFF) << 24) | ((b[at + 1] & 0xFF) << 16) | ((b[at + 2] & 0xFF) << 8) | (b[at + 3] & 0xFF);
    }

    private static int int16(byte[] b, int at) {
        return ((b[at] & 0xFF) << 8) | (b[at + 1] & 0xFF);
    }

    /** Pairs note-ons with note-offs as the game does (first open note of the same pitch). */
    List<Note> notes(Track t) {
        List<Note> out = new ArrayList<>();
        Map<Integer, Long> open = new HashMap<>();
        for (Ev e : t.events) {
            if (e.kind == 0x90) {
                if (open.containsKey(e.pitch)) fail(t.name + ": note " + e.pitch + " starts again at tick " + e.tick + " while still sounding");
                else open.put(e.pitch, e.tick);
            } else if (e.kind == 0x80) {
                Long start = open.remove(e.pitch);
                if (start == null) {
                    fail(t.name + ": note " + e.pitch + " ends at tick " + e.tick + " without having started");
                    continue;
                }
                Note n = new Note();
                n.tick = start;
                n.length = e.tick - start;
                n.pitch = e.pitch;
                if (n.length < 1) fail(t.name + ": zero-length note " + e.pitch + " at tick " + start);
                out.add(n);
            }
        }
        if (!open.isEmpty()) fail(t.name + ": " + open.size() + " note(s) never end");
        return out;
    }

    /** YARG.Core's Midi_Vocal_Preparser: is there a sung note the scanner accepts? */
    boolean scannerFindsVocals(Track t, boolean lead) {
        long vocalPosition = -1;
        long phrasePosition = -1;
        for (Ev e : t.events) {
            if (e.kind == 0x90) {
                if (e.pitch >= 36 && e.pitch <= 84) vocalPosition = e.tick;
                else if (e.pitch == 105 || e.pitch == 106) phrasePosition = Integer.MAX_VALUE;
            } else if (e.kind == 0x80) {
                if (e.pitch >= 36 && e.pitch <= 84) {
                    if (vocalPosition >= 0 && (e.tick <= phrasePosition || !lead)) return true;
                    vocalPosition = -1;
                } else if (e.pitch == 105 || e.pitch == 106) {
                    phrasePosition = e.tick;
                }
            }
        }
        return false;
    }

    /**
     * YARG.Core's vocals loader: phrases are the 105 notes (taken from {@code phrasesFrom}, which
     * for the second singer is the first singer's track), a note belongs to the phrase its start
     * falls in, and its lyric is the text event on the same tick. Phrases left without notes are dropped.
     */
    List<Phrase> vocalPhrases(Track t, Track phrasesFrom) {
        List<Phrase> out = new ArrayList<>();
        Map<Long, String> lyrics = new HashMap<>();
        for (Ev e : t.events) {
            if (e.kind != 0xFF || (e.metaType != 0x01 && e.metaType != 0x05)) continue;
            String text = e.text();
            if (text.contains("[")) continue;
            if (!text.equals(text.trim()) || text.isEmpty()) fail(t.name + ": lyric '" + text + "' is empty or padded");
            if (lyrics.put(e.tick, text) != null) fail(t.name + ": two lyrics on tick " + e.tick);
        }
        List<Note> all = notes(t);
        List<Note> marks = phrasesFrom == t ? all : notes(phrasesFrom);
        List<Note> sung = new ArrayList<>();
        for (Note n : all) if (n.pitch >= 36 && n.pitch <= 84) sung.add(n);
        sung.sort((a, b) -> Long.compare(a.tick, b.tick));
        boolean[] used = new boolean[sung.size()];
        long previousEnd = -1;
        List<Note> phrases = new ArrayList<>();
        for (Note n : marks) if (n.pitch == 105 || n.pitch == 106) phrases.add(n);
        phrases.sort((a, b) -> Long.compare(a.tick, b.tick));
        for (Note mark : phrases) {
            if (mark.tick < previousEnd) fail(t.name + ": phrases overlap at tick " + mark.tick);
            previousEnd = mark.tick + mark.length;
            Phrase p = new Phrase();
            p.tick = mark.tick;
            p.length = mark.length;
            for (int i = 0; i < sung.size(); i++) {
                Note n = sung.get(i);
                if (n.tick < mark.tick || n.tick >= mark.tick + mark.length) continue;
                used[i] = true;
                if (n.tick + n.length > mark.tick + mark.length) fail(t.name + ": note at tick " + n.tick + " runs past its phrase");
                if (n.tick == mark.tick) fail(t.name + ": first word on the same tick as its phrase mark (the lyric bar needs the phrase to open first)");
                Word w = new Word();
                w.tick = n.tick;
                w.length = n.length;
                w.pitch = n.pitch;
                w.lyric = lyrics.get(n.tick);
                if (w.lyric == null) fail(t.name + ": note at tick " + n.tick + " has no lyric");
                p.words.add(w);
            }
            if (!p.words.isEmpty()) out.add(p);
        }
        for (int i = 0; i < used.length; i++) {
            if (!used[i]) fail(t.name + ": note at tick " + sung.get(i).tick + " is outside every phrase");
        }
        for (int i = 1; i < sung.size(); i++) {
            Note a = sung.get(i - 1), b = sung.get(i);
            if (a.tick + a.length > b.tick) fail(t.name + ": notes overlap at tick " + b.tick);
        }
        return out;
    }

    /** Checks one singer's track against the phrases it was written from. */
    private void compareVocals(String name, List<Phrase> read, VocalCharter.Chart chart, int which) {
        List<VocalCharter.Phrase> want = new ArrayList<>();
        for (VocalCharter.Phrase p : chart.phrases) {
            if (which < 0 || p.part == which || p.part == VocalCharter.PART_BOTH) want.add(p);
        }
        if (read.size() != want.size()) {
            fail(name + ": " + read.size() + " phrases read back, " + want.size() + " written");
            return;
        }
        for (int i = 0; i < want.size(); i++) {
            VocalCharter.Phrase w = want.get(i);
            Phrase r = read.get(i);
            if (r.tick != w.tick || r.length != w.length) fail(name + ": phrase " + i + " moved");
            if (r.words.size() != w.notes.size()) {
                fail(name + ": phrase " + i + " has " + r.words.size() + " notes, wanted " + w.notes.size());
                continue;
            }
            for (int k = 0; k < w.notes.size(); k++) {
                VocalCharter.Note a = w.notes.get(k);
                Word b = r.words.get(k);
                String lyric = a.slide ? "+" : a.text + (a.spoken ? "#" : "");
                if (a.tick != b.tick || a.length != b.length || a.pitch != b.pitch || !lyric.equals(b.lyric)) {
                    fail(name + ": phrase " + i + " note " + k + " differs: wrote " + a.tick + "/" + a.length + "/" + a.pitch + "/" + lyric
                            + ", read " + b.tick + "/" + b.length + "/" + b.pitch + "/" + b.lyric);
                }
                if (a.spoken != w.spoken && !a.slide) fail(name + ": phrase " + i + " mixes sung and spoken words");
            }
        }
    }

    /** Checks a guitar or bass track note for note, applying the game's "short notes are not held" rule. */
    private void compareInstrument(String name, GuitarCharter.Chart chart) {
        Track t = track(name);
        if (t == null) {
            fail(name + " track missing");
            return;
        }
        List<Note> all = notes(t);
        long cutoff = resolution / 3;
        for (int d = 0; d < 4; d++) {
            int base = 60 + 12 * d;
            Map<Long, List<Note>> byTick = new HashMap<>();
            int count = 0;
            for (Note n : all) {
                if (n.pitch < base || n.pitch > base + 4) continue;
                byTick.computeIfAbsent(n.tick, k -> new ArrayList<>()).add(n);
                count++;
            }
            int want = 0;
            for (GuitarCharter.Note n : chart.notes.get(d)) {
                want += n.lane2 >= 0 ? 2 : 1;
                List<Note> got = byTick.get(n.tick);
                if (got == null || got.size() != (n.lane2 >= 0 ? 2 : 1)) {
                    fail(name + " level " + d + ": note at tick " + n.tick + " missing or doubled");
                    continue;
                }
                for (Note g : got) {
                    int lane = g.pitch - base;
                    if (lane != n.lane && lane != n.lane2) fail(name + " level " + d + ": wrong fret at tick " + n.tick);
                    long sustain = g.length < cutoff ? 0 : g.length;
                    if (sustain != n.sustain) fail(name + " level " + d + ": tick " + n.tick + " held for " + sustain + ", wanted " + n.sustain);
                }
            }
            if (count != want) fail(name + " level " + d + ": " + count + " notes read back, " + want + " written");
        }
        int phrases = 0;
        for (Note n : all) {
            if (n.pitch == 116) phrases++;
            boolean known = n.pitch == 116 || (n.pitch >= 60 && n.pitch <= 100 && (n.pitch - 60) % 12 <= 4);
            if (!known) fail(name + ": stray note " + n.pitch);
        }
        if (phrases > chart.starPower.size()) fail(name + ": more star power phrases than charted");
        if (!chart.starPower.isEmpty() && phrases == 0) fail(name + ": star power lost");
    }

    /** The whole file against the analysis it was written from. Returns the list of problems (empty = good). */
    static List<String> verify(byte[] bytes, SongWriter.Result r) {
        MidiCheck m = new MidiCheck(bytes);
        if (!m.problems.isEmpty()) return m.problems;

        // Tempo track first, with a tempo at the very start.
        Track tempo = m.tracks.get(0);
        boolean tempoAtZero = false;
        int tempos = 0, signatures = 0;
        for (Ev e : tempo.events) {
            if (e.kind != 0xFF) m.fail("notes in the tempo track");
            if (e.metaType == 0x51) {
                tempos++;
                if (e.tick == 0) tempoAtZero = true;
                if (e.data.length != 3) m.fail("bad tempo event");
            }
            if (e.metaType == 0x58) signatures++;
        }
        if (!tempoAtZero) m.fail("no tempo at tick 0");
        if (tempos != r.tempo.tick.length) m.fail(tempos + " tempo markers read back, " + r.tempo.tick.length + " written");
        if (signatures < 1) m.fail("no time signature");

        Track events = m.track("EVENTS");
        if (events == null) m.fail("EVENTS track missing");
        else {
            int sections = 0;
            for (Ev e : events.events) if (e.kind == 0xFF && e.metaType == 0x01 && e.text().startsWith("[section ")) sections++;
            if (sections < 1) m.fail("no practice sections");
        }

        m.compareInstrument("PART GUITAR", r.chart);
        if (r.bassChart != null) m.compareInstrument("PART BASS", r.bassChart);
        else if (m.track("PART BASS") != null) m.fail("bass track written without a bass chart");

        VocalCharter.Chart v = r.vocals;
        Track lead = m.track("PART VOCALS");
        if (v.phrases.isEmpty()) {
            if (lead != null) m.fail("vocals track written without lyrics");
        } else if (lead == null) {
            m.fail("PART VOCALS track missing");
        } else {
            if (!m.scannerFindsVocals(lead, true)) m.fail("the game's song scan would not find the vocals");
            m.compareVocals("PART VOCALS", m.vocalPhrases(lead, lead), v, -1);
            // The lyric bar other players see: each phrase opens before its first word and closes after its last.
            Track h1 = m.track("HARM1"), h2 = m.track("HARM2");
            if (v.twoParts()) {
                if (h1 == null || h2 == null) {
                    m.fail("two singers but no HARM1/HARM2 tracks");
                } else {
                    if (!m.scannerFindsVocals(h1, true)) m.fail("the game's song scan would not find the first singer's part");
                    if (!m.scannerFindsVocals(h2, false)) m.fail("the game's song scan would not find the second singer's part");
                    m.compareVocals("HARM1", m.vocalPhrases(h1, h1), v, VocalCharter.PART_FIRST);
                    m.compareVocals("HARM2", m.vocalPhrases(h2, h1), v, VocalCharter.PART_SECOND);
                }
            } else if (h1 != null || h2 != null) {
                m.fail("harmony tracks written for a song with one singer");
            }
        }
        return m.problems;
    }
}
