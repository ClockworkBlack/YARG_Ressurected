import com.clockworkblack.yargconverter.core.AudioAnalyzer;
import com.clockworkblack.yargconverter.core.Lrc;
import com.clockworkblack.yargconverter.core.MidiSong;
import com.clockworkblack.yargconverter.core.SongMeta;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.TempoMap;
import com.clockworkblack.yargconverter.core.VocalAnalyzer;
import com.clockworkblack.yargconverter.core.VocalCharter;
import com.clockworkblack.yargconverter.core.VoiceSplitter;

import java.io.File;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.Random;

/**
 * Checks for the singing side of the converter: hearing which note a voice is on, placing the
 * words of a lyric line where they are sung, telling singing from rap, splitting a song between
 * two singers, and writing it all into a notes.mid the game reads back intact.
 */
final class VocalTest {
    private static int failures;

    private static void check(boolean ok, String what) {
        if (!ok) {
            failures++;
            System.out.println("  FAIL: " + what);
        }
    }

    static int run() throws Exception {
        failures = 0;
        hearingNotes();
        sungAndRapped(44100, 11);
        sungAndRapped(48000, 12);
        heldWords();
        withoutTheVoice();
        marksAndArtists();
        voicesToldApart();
        wholeSong();
        return failures;
    }

    public static void main(String[] args) throws Exception {
        if (args.length == 2 && args[0].equals("write")) {
            writeForTheGame(new File(args[1]));
            return;
        }
        int f = run();
        System.out.println(f == 0 ? "All vocal checks passed" : "FAILED: " + f + " vocal check(s)");
        if (f > 0) System.exit(1);
    }

    static VocalAnalyzer.Voice listen(float[] audio, int sampleRate) {
        VocalAnalyzer an = new VocalAnalyzer(sampleRate);
        for (int i = 0; i < audio.length; i += 3000) an.feed(audio, i, Math.min(3000, audio.length - i));
        return an.finish();
    }

    /** Steady notes across the range of the human voice must be heard as the right note. */
    private static void hearingNotes() {
        System.out.println("Hearing which note a voice is on");
        int[] rates = {44100, 48000, 22050};
        int[] notes = {38, 45, 52, 57, 60, 64, 69, 76, 81, 84};
        double worst = 0;
        for (int sr : rates) {
            float[] x = new float[sr * notes.length];
            for (int k = 0; k < notes.length; k++) {
                double hz = 440.0 * Math.pow(2, (notes[k] - 69) / 12.0);
                for (int i = (int) (0.1 * sr); i < (int) (0.8 * sr); i++) {
                    double ph = 2 * Math.PI * hz * i / sr;
                    x[k * sr + i] = (float) (0.2 * (Math.sin(ph) + 0.5 * Math.sin(2 * ph) + 0.3 * Math.sin(3 * ph) + 0.2 * Math.sin(4 * ph)));
                }
            }
            VocalAnalyzer.Voice v = listen(x, sr);
            check(Math.abs(v.fps - 100) < 2, "about 100 frames a second (" + v.fps + ")");
            for (int k = 0; k < notes.length; k++) {
                int a = v.frameAt(k + 0.25), b = v.frameAt(k + 0.65);
                float[] heard = new float[b - a];
                int n = 0;
                for (int f = a; f < b; f++) if (!Float.isNaN(v.midi[f])) heard[n++] = v.midi[f];
                check(n >= (b - a) * 0.9, "note " + notes[k] + " at " + sr + " Hz is heard as a note (" + n + " of " + (b - a) + " frames)");
                if (n == 0) continue;
                Arrays.sort(heard, 0, n);
                double error = Math.abs(heard[n / 2] - notes[k]);
                worst = Math.max(worst, error);
                check(error < 0.2, "note " + notes[k] + " at " + sr + " Hz heard as " + heard[n / 2]);
                // Between notes there is silence: nothing should be heard there.
                int quiet = v.frameAt(k + 0.95);
                check(Float.isNaN(v.midi[quiet]), "silence after note " + notes[k] + " is not a note");
            }
        }
        System.out.printf(Locale.US, "  worst error %.2f of a semitone%n", worst);

        // Hiss on its own is not a note.
        Random rnd = new Random(5);
        float[] noise = new float[44100 * 3];
        for (int i = 0; i < noise.length; i++) noise[i] = (float) (0.2 * (rnd.nextDouble() * 2 - 1));
        VocalAnalyzer.Voice v = listen(noise, 44100);
        int notesHeard = 0;
        for (float m : v.midi) if (!Float.isNaN(m)) notesHeard++;
        check(notesHeard < v.frames() * 0.05, "noise is not heard as notes (" + notesHeard + " of " + v.frames() + " frames)");
    }

    private static TempoMap steadyMap(double seconds, double bpm) {
        int count = (int) (seconds * bpm / 60) + 8;
        double[] beats = new double[count];
        for (int i = 0; i < count; i++) beats[i] = i * 60.0 / bpm;
        return TempoMap.fromBeats(beats, 0, 4);
    }

    private static List<Lrc.Line> lyricFile(SynthVoice song, boolean marked) {
        StringBuilder sb = new StringBuilder();
        for (SynthVoice.Line l : song.lines) {
            sb.append(Lrc.stamp(l.stamp)).append(marked ? Lrc.mark(l.voice == 0 ? Lrc.FIRST : Lrc.SECOND) : "").append(l.text()).append('\n');
        }
        return Lrc.parse(sb.toString());
    }

    /** Phrases of one lyric line, in order. */
    private static List<VocalCharter.Note> notesOfLine(VocalCharter.Chart chart, int line, boolean[] spoken) {
        List<VocalCharter.Note> out = new ArrayList<>();
        for (VocalCharter.Phrase p : chart.phrases) {
            if (p.line != line) continue;
            spoken[0] = p.spoken;
            out.addAll(p.notes);
        }
        return out;
    }

    private static boolean sameNote(int a, int b) {
        int d = Math.floorMod(a - b, 12);
        return d <= 1 || d >= 11;
    }

    /** Sung lines get the notes that were sung; rapped lines become talkies; words land on the beat they are said. */
    private static void sungAndRapped(int sampleRate, long seed) {
        System.out.println("Sung and rapped lines, " + sampleRate + " Hz");
        SynthVoice song = new SynthVoice(sampleRate, "SSRRssrrSRsrSSRr", seed);
        VocalAnalyzer.Voice voice = listen(song.audio, sampleRate);
        TempoMap map = steadyMap(song.duration, 96);
        VocalCharter.Chart chart = VocalCharter.build(lyricFile(song, false), map, song.duration, voice);

        check(chart.heard, "the voice was used");
        check(chart.lines == song.lines.size(), "every line charted (" + chart.lines + " of " + song.lines.size() + ")");
        int kindRight = 0, sungWords = 0, noteRight = 0, words = 0, sungOnTime = 0, rapWords = 0, rapOnTime = 0, lineOnTime = 0;
        double worstLine = 0;
        for (int i = 0; i < song.lines.size(); i++) {
            SynthVoice.Line truth = song.lines.get(i);
            boolean[] spoken = new boolean[1];
            List<VocalCharter.Note> notes = notesOfLine(chart, i, spoken);
            if (notes.isEmpty()) continue;
            if (spoken[0] != truth.sung) kindRight++;
            double lineError = Math.abs(map.tickToTime(notes.get(0).tick) - truth.start);
            worstLine = Math.max(worstLine, lineError);
            if (lineError < 0.06) lineOnTime++;
            int k = 0;
            for (VocalCharter.Note n : notes) {
                if (n.slide || k >= truth.words.size()) continue;
                SynthVoice.Word w = truth.words.get(k++);
                check(n.text.equals(w.text), "word order kept: " + n.text + " / " + w.text);
                double error = Math.abs(map.tickToTime(n.tick) - w.start);
                words++;
                if (truth.sung) {
                    sungWords++;
                    if (error < 0.08) sungOnTime++;
                    if (!n.spoken && sameNote(n.pitch, w.midi)) noteRight++;
                    check(n.pitch >= VocalCharter.MIN_PITCH && n.pitch <= VocalCharter.MAX_PITCH, "note in range");
                } else {
                    rapWords++;
                    if (error < 0.08) rapOnTime++;
                }
            }
            check(k == truth.words.size(), "line " + i + " has all its words (" + k + " of " + truth.words.size() + ")");
        }
        System.out.printf(Locale.US, "  sung or rapped told apart on %d of %d lines; lines start on time: %d (worst %.0f ms)%n",
                kindRight, song.lines.size(), lineOnTime, worstLine * 1000);
        System.out.printf(Locale.US, "  sung words: right note %d%%, on time %d%%; rapped words on time %d%%%n",
                100 * noteRight / Math.max(1, sungWords), 100 * sungOnTime / Math.max(1, sungWords), 100 * rapOnTime / Math.max(1, rapWords));
        check(kindRight == song.lines.size(), "every line recognised as sung or rapped");
        check(lineOnTime >= song.lines.size() - 1, "lines start where they are sung");
        check(noteRight >= 0.9 * sungWords, "sung words get the right note");
        check(sungOnTime >= 0.85 * sungWords, "sung words start on time");
        check(rapOnTime >= 0.6 * rapWords, "rapped words start close to on time");
        check(chart.sungLines + chart.spokenLines == chart.lines, "line counts add up");
        structure(chart, "sung and rapped");
    }

    /** A long held word that moves to another note gets a joined-on note there. */
    private static void heldWords() {
        System.out.println("Held words that change note");
        SynthVoice song = new SynthVoice(44100, "LLLL", 21);
        VocalAnalyzer.Voice voice = listen(song.audio, 44100);
        TempoMap map = steadyMap(song.duration, 110);
        VocalCharter.Chart chart = VocalCharter.build(lyricFile(song, false), map, song.duration, voice);
        int words = 0, followed = 0, rightNote = 0, firstRight = 0;
        for (int i = 0; i < song.lines.size(); i++) {
            boolean[] spoken = new boolean[1];
            List<VocalCharter.Note> notes = notesOfLine(chart, i, spoken);
            check(!spoken[0], "held line " + i + " counts as sung");
            int k = -1;
            for (int j = 0; j < notes.size(); j++) {
                VocalCharter.Note n = notes.get(j);
                if (!n.slide) {
                    k++;
                    words++;
                    if (k < song.lines.get(i).words.size() && sameNote(n.pitch, song.lines.get(i).words.get(k).midi)) firstRight++;
                    if (j + 1 < notes.size() && notes.get(j + 1).slide) {
                        followed++;
                        if (k < song.lines.get(i).words.size() && sameNote(notes.get(j + 1).pitch, song.lines.get(i).words.get(k).midi2)) rightNote++;
                    }
                } else {
                    check(n.text.isEmpty(), "a joined-on note carries no new word");
                }
            }
        }
        System.out.printf(Locale.US, "  %d held words: first note right on %d, move to the second note found on %d, right on %d%n", words, firstRight, followed, rightNote);
        check(firstRight >= 0.8 * words, "held words open on the right note");
        check(followed >= 0.7 * words, "moves to a second note are found");
        check(rightNote >= 0.8 * followed, "the second note is the right one");
        structure(chart, "held words");
    }

    /** Without the separated voice every line is a talkie, spread at speaking pace, and long lines are cut up. */
    private static void withoutTheVoice() {
        System.out.println("Lyrics without the separated voice");
        List<Lrc.Line> lines = Lrc.parse("[00:05.00]Hello there world\n[00:08.50]Second line \"quoted\" well-\n[00:12.00]\n"
                + "[00:20.00]one two three four five six seven eight nine ten eleven twelve thirteen fourteen\n[00:21.00]squeezed in right behind\n[05:00.00]past the end");
        TempoMap map = steadyMap(60, 120);
        VocalCharter.Chart chart = VocalCharter.build(lines, map, 60, null);
        check(!chart.heard, "no voice, so nothing was heard");
        check(chart.lines == 4, "four lines charted, the one after the end of the song dropped (" + chart.lines + ")");
        check(chart.spokenLines == 4 && chart.sungLines == 0, "all talkies");
        check(chart.phrases.size() == 5, "the fourteen-word line is cut in two (" + chart.phrases.size() + " phrases)");
        for (VocalCharter.Phrase p : chart.phrases) {
            check(p.notes.size() <= 9, "no phrase over nine words");
            for (VocalCharter.Note n : p.notes) check(n.spoken && !n.text.isEmpty(), "talkie with a word");
        }
        check(chart.phrases.get(1).notes.get(3).text.equals("well="), "a trailing hyphen is kept as a literal hyphen: " + chart.phrases.get(1).notes.get(3).text);
        check(Math.abs(map.tickToTime(chart.phrases.get(0).notes.get(0).tick) - 5.0) < 0.02, "first word on its time stamp");
        structure(chart, "without the voice");
        check(VocalCharter.build(null, map, 60, null).phrases.isEmpty(), "no lyrics, no phrases");
        check(VocalCharter.words("[Intro] $hid^ wo#rd# a+ %").toString().equals("[Intro, hid, wo#rd, a]"), "control characters stripped: " + VocalCharter.words("[Intro] $hid^ wo#rd# a+ %"));
    }

    /** Rules every vocal chart has to obey for the game to load it. */
    static void structure(VocalCharter.Chart chart, String name) {
        long previousEnd = 0;
        for (VocalCharter.Phrase p : chart.phrases) {
            check(p.tick >= previousEnd, name + ": phrases in order and apart");
            check(!p.notes.isEmpty(), name + ": no empty phrases");
            long at = p.tick;
            for (VocalCharter.Note n : p.notes) {
                check(n.tick > at || (n.tick > p.tick && n == p.notes.get(0)), name + ": notes move forward");
                check(n.tick > p.tick, name + ": the phrase opens before its first word");
                check(n.length >= 1, name + ": notes have length");
                at = n.tick + n.length - 1;
                check(n.slide == n.text.isEmpty(), name + ": only joined-on notes are wordless");
            }
            VocalCharter.Note last = p.notes.get(p.notes.size() - 1);
            check(last.tick + last.length < p.tick + p.length, name + ": the phrase closes after its last word");
            check(!p.notes.get(0).slide, name + ": a phrase starts with a word");
            previousEnd = p.tick + p.length;
        }
    }

    private static void marksAndArtists() {
        System.out.println("Singer marks and artist names");
        List<Lrc.Line> l = Lrc.parse("[00:01.00]v1: First artist\n[00:02.00]v2: Second artist\n[00:03.00]vb: Both of them\n[00:04.00]Nobody marked\n[00:05.00]F: Old style mark\n[00:06.00]V 2 : spaced out\n[00:07.00]very: not a mark");
        check(l.size() == 7, "seven lines");
        if (l.size() == 7) {
            check(l.get(0).singer == Lrc.FIRST && l.get(0).text.equals("First artist"), "v1 mark");
            check(l.get(1).singer == Lrc.SECOND && l.get(1).text.equals("Second artist"), "v2 mark");
            check(l.get(2).singer == Lrc.BOTH && l.get(2).text.equals("Both of them"), "vb mark");
            check(l.get(3).singer == Lrc.UNMARKED, "unmarked line");
            check(l.get(4).singer == Lrc.SECOND && l.get(4).text.equals("Old style mark"), "F: mark");
            check(l.get(5).singer == Lrc.SECOND && l.get(5).text.equals("spaced out"), "spaced mark: " + l.get(5).singer + " '" + l.get(5).text + "'");
            check(l.get(6).singer == Lrc.UNMARKED && l.get(6).text.equals("very: not a mark"), "a word with a colon is not a mark");
        }
        String again = Lrc.format(l);
        List<Lrc.Line> l2 = Lrc.parse(again);
        boolean same = l2.size() == l.size();
        for (int i = 0; same && i < l.size(); i++) same = l2.get(i).singer == l.get(i).singer && l2.get(i).text.equals(l.get(i).text);
        check(same, "marks survive being written out and read back");

        check(Lrc.artists("Don Toliver feat. Travis Scott", "No Pole").toString().equals("[Don Toliver, Travis Scott]"), "feat.: " + Lrc.artists("Don Toliver feat. Travis Scott", "No Pole"));
        check(Lrc.artists("Don Toliver & Travis Scott", "").toString().equals("[Don Toliver, Travis Scott]"), "ampersand");
        check(Lrc.artists("Drake", "Sicko Mode (feat. Swae Lee)").toString().equals("[Drake, Swae Lee]"), "feat. in the title: " + Lrc.artists("Drake", "Sicko Mode (feat. Swae Lee)"));
        check(Lrc.artists("Don Toliver", "No Pole").toString().equals("[Don Toliver]"), "one artist");
        check(Lrc.artists("A ft. B, C", "").toString().equals("[A, B & C]"), "two parts at most, the guests together: " + Lrc.artists("A ft. B, C", ""));
        check(Lrc.artists("", null).isEmpty(), "no artist");
        check(Lrc.artists("Max x Lexi", "").toString().equals("[Max, Lexi]") && Lrc.artists("Maxx", "").size() == 1, "an x between names, not inside one");

        List<String> plain = Lrc.plainLines("[Intro]\nla la\n[Verse 1: Travis Scott]\nline one\nline two\n[Chorus: Don Toliver & Travis Scott]\nhook\n[Verse 2: Don Toliver]\nmine\n[Bridge]\nwhose\nv2: typed mark",
                Arrays.asList("Don Toliver", "Travis Scott"));
        check(plain.toString().equals("[la la, v2: line one, v2: line two, vb: hook, v1: mine, whose, v2: typed mark]"), "headers name the singer: " + plain);
    }

    /** Two different voices trading verses are told apart; one voice alone is not split in two. */
    private static void voicesToldApart() {
        System.out.println("Telling two singers apart by voice");
        String[] plans = {"SSSSssssRRRRrrrrSSss", "RRRsssRRRRssRRRss", "ssssSSRRssssSSRR"};
        long seed = 40;
        for (String plan : plans) {
            SynthVoice song = new SynthVoice(44100, plan, seed++);
            VocalAnalyzer.Voice voice = listen(song.audio, 44100);
            TempoMap map = steadyMap(song.duration, 100);
            List<Lrc.Line> lines = lyricFile(song, false);
            VocalCharter.Chart chart = VocalCharter.build(lines, map, song.duration, voice);
            VoiceSplitter.Guess guess = VoiceSplitter.guess(chart, voice, lines.size());
            check(guess != null, "two voices are told apart in " + plan);
            if (guess == null) continue;
            int same = 0;
            for (int i = 0; i < song.lines.size(); i++) if (guess.partOfLine[i] == song.lines.get(i).voice) same++;
            int right = Math.max(same, song.lines.size() - same);
            System.out.printf(Locale.US, "  %s: %d of %d lines given to the right singer (voices %.1f apart)%n", plan, right, song.lines.size(), guess.separation);
            check(right >= 0.85 * song.lines.size(), "most lines given to the right singer in " + plan);
            check(guess.partOfLine[0] == VocalCharter.PART_FIRST, "whoever sings the first line is the first singer");
            // Told there are two singers (two artists named), the answer must be at least as good
            VoiceSplitter.Guess sure = VoiceSplitter.guess(chart, voice, lines.size(), true);
            int sameSure = 0;
            for (int i = 0; sure != null && i < song.lines.size(); i++) if (sure.partOfLine[i] == song.lines.get(i).voice) sameSure++;
            int rightSure = sure == null ? 0 : Math.max(sameSure, song.lines.size() - sameSure);
            check(rightSure >= right - 1, "knowing there are two singers does not make it worse in " + plan + " (" + rightSure + " vs " + right + ")");
            chart.assignParts(guess.partOfLine);
            check(chart.twoParts() && chart.partLines[0] == guess.firstLines && chart.partLines[1] == guess.secondLines, "the guess is applied to the chart");
        }
        // One singer who both sings and raps must not be split into two people.
        SynthVoice alone = new SynthVoice(44100, "SSRRSSRRSSRRSSRRLL", 50);
        VocalAnalyzer.Voice voice = listen(alone.audio, 44100);
        List<Lrc.Line> lines = lyricFile(alone, false);
        VocalCharter.Chart chart = VocalCharter.build(lines, steadyMap(alone.duration, 100), alone.duration, voice);
        VoiceSplitter.Guess guess = VoiceSplitter.guess(chart, voice, lines.size());
        System.out.println("  one singer alone: " + (guess == null ? String.format(Locale.US, "not split (%.1f apart)", VoiceSplitter.lastSeparation) : "split " + guess.firstLines + "/" + guess.secondLines + String.format(Locale.US, " (%.1f apart)", guess.separation)));
        check(guess == null, "one voice is not split into two singers");
        check(VoiceSplitter.guess(chart, null, lines.size()) == null && VoiceSplitter.guess(new VocalCharter.Chart(), voice, 0) == null, "nothing to go on, no guess");
    }

    /** A band with two singers, analysed as the app would from separated instruments. */
    private static final class TestSong {
        SynthBand band;
        SynthVoice song;
        SongWriter.Stems stems;
        SongWriter.Result result;
    }

    private static TestSong twoSingerSong() {
        TestSong t = new TestSong();
        t.band = new SynthBand(44100, 104.0, 0.02, 0.3, 4, 9);
        AudioAnalyzer an = new AudioAnalyzer(t.band.sampleRate);
        for (int i = 0; i < t.band.audio.length; i += 4096) an.feed(t.band.audio, i, Math.min(4096, t.band.audio.length - i));
        t.song = new SynthVoice(44100, "SSrrSSrrSsRrLl", 31);
        t.stems = new SongWriter.Stems();
        t.stems.guitar = StemTest.analyze(t.band.guitarTrack, t.band.sampleRate);
        t.stems.melodic = t.stems.guitar;
        t.stems.bass = StemTest.analyze(t.band.bassTrack, t.band.sampleRate, true);
        t.stems.guitarEnergy = StemTest.energy(t.band.guitarTrack);
        t.stems.melodicEnergy = t.stems.guitarEnergy;
        t.stems.bassEnergy = StemTest.energy(t.band.bassTrack);
        t.stems.mixEnergy = StemTest.energy(t.band.audio);
        t.stems.voice = listen(t.song.audio, 44100);

        List<Lrc.Line> marked = lyricFile(t.song, true);
        // One line for both of them together.
        marked.set(4, new Lrc.Line(marked.get(4).time, marked.get(4).text, Lrc.BOTH));
        t.result = SongWriter.analyze(an.finish(), marked, null, t.stems);
        return t;
    }

    /**
     * Writes the two-singer test song as a song folder, with the numbers the game should find in
     * it next to it (expect.txt), for the check that runs the game's own scanner and loader
     * over the folder (tests/GameReaderCheck).
     */
    private static void writeForTheGame(File dir) throws Exception {
        TestSong t = twoSingerSong();
        SongWriter.Result r = t.result;
        SongMeta meta = new SongMeta();
        meta.title = "Two Voices";
        meta.artist = "First One feat. Second One";
        meta.singers = Lrc.artists(meta.artist, meta.title).toArray(new String[0]);
        SongWriter.writeFolder(dir, meta, r, "song.ogg");

        StringBuilder sb = new StringBuilder();
        sb.append("parts=").append(r.vocals.twoParts() ? 2 : 1).append('\n');
        String[] instruments = {"guitar", "bass"};
        com.clockworkblack.yargconverter.core.GuitarCharter.Chart[] charts = {r.chart, r.bassChart};
        for (int c = 0; c < 2; c++) {
            for (int d = 0; d < 4; d++) {
                int held = 0, chords = 0;
                for (com.clockworkblack.yargconverter.core.GuitarCharter.Note n : charts[c].notes.get(d)) {
                    if (n.sustain > 0) held++;
                    if (n.lane2 >= 0) chords++;
                }
                sb.append(instruments[c]).append('_').append(d).append('=').append(charts[c].notes.get(d).size()).append('\n');
                sb.append(instruments[c]).append('_').append(d).append("_held=").append(held).append('\n');
                sb.append(instruments[c]).append('_').append(d).append("_chords=").append(chords).append('\n');
            }
        }
        String[] keys = {"lead", "harm1", "harm2"};
        int[] which = {-1, VocalCharter.PART_FIRST, VocalCharter.PART_SECOND};
        for (int k = 0; k < 3; k++) {
            int phrases = 0, words = 0, spoken = 0, slides = 0, star = 0;
            for (VocalCharter.Phrase p : r.vocals.phrases) {
                if (which[k] >= 0 && p.part != which[k] && p.part != VocalCharter.PART_BOTH) continue;
                phrases++;
                if (p.starPower) star++;
                for (VocalCharter.Note n : p.notes) {
                    if (n.slide) slides++;
                    else {
                        words++;
                        if (n.spoken) spoken++;
                    }
                }
            }
            sb.append(keys[k]).append("_phrases=").append(phrases).append('\n');
            sb.append(keys[k]).append("_words=").append(words).append('\n');
            sb.append(keys[k]).append("_spoken=").append(spoken).append('\n');
            sb.append(keys[k]).append("_slides=").append(slides).append('\n');
            sb.append(keys[k]).append("_star=").append(star).append('\n');
        }
        VocalCharter.Phrase firstPhrase = r.vocals.phrases.get(0), lastPhrase = r.vocals.phrases.get(r.vocals.phrases.size() - 1);
        VocalCharter.Note lastWord = null;
        for (VocalCharter.Note n : lastPhrase.notes) if (!n.slide) lastWord = n;
        sb.append("first_word_ms=").append(Math.round(r.tempo.tickToTime(firstPhrase.notes.get(0).tick) * 1000)).append('\n');
        sb.append("last_word_ms=").append(Math.round(r.tempo.tickToTime(lastWord.tick) * 1000)).append('\n');
        Files.write(new File(dir, "expect.txt").toPath(), sb.toString().getBytes("UTF-8"));
        System.out.println("Wrote " + dir + ":");
        System.out.print(sb);
    }

    /** Guitar, bass, and two singers through the whole pipeline and back out of the MIDI file. */
    private static void wholeSong() throws Exception {
        System.out.println("A whole song with two singers, written and read back");
        TestSong t = twoSingerSong();
        SynthBand band = t.band;
        SynthVoice song = t.song;
        SongWriter.Stems stems = t.stems;
        SongWriter.Result r = t.result;
        check(r.bassChart != null, "bass charted");
        check(r.vocals.twoParts(), "two singers found in the marks");
        check(r.vocals.partLines[0] + r.vocals.partLines[1] + r.vocals.partLines[2] == r.vocals.lines, "every line has a singer");
        check(r.vocals.partLines[2] == 1, "one line for both");
        check(r.vocalTier >= 0 && r.vocalTier <= 6, "vocals tier in range (" + r.vocalTier + ")");
        int starred = 0;
        for (VocalCharter.Phrase p : r.vocals.phrases) if (p.starPower) starred++;
        check(starred >= 1, "some phrases carry star power");
        structure(r.vocals, "whole song");

        byte[] midi = MidiSong.build(r);
        List<String> problems = MidiCheck.verify(midi, r);
        for (String p : problems) check(false, "notes.mid: " + p);
        System.out.println("  notes.mid: " + midi.length + " bytes, " + r.vocals.phrases.size() + " phrases, "
                + r.vocals.sungLines + " sung and " + r.vocals.spokenLines + " rapped lines, singers "
                + r.vocals.partLines[0] + "/" + r.vocals.partLines[1] + "/" + r.vocals.partLines[2]);

        // One singer: no harmony tracks. No lyrics: no vocals at all.
        SongWriter.Result solo = SongWriter.analyze(StemTest.analyze(band.audio, band.sampleRate), lyricFile(song, false), null, stems);
        check(!solo.vocals.twoParts(), "unmarked lyrics are one singer");
        for (String p : MidiCheck.verify(MidiSong.build(solo), solo)) check(false, "one singer notes.mid: " + p);
        SongWriter.Result none = SongWriter.analyze(StemTest.analyze(band.audio, band.sampleRate), null, null, null);
        check(none.vocals.phrases.isEmpty() && none.vocalTier == -1, "no lyrics, no vocals");
        for (String p : MidiCheck.verify(MidiSong.build(none), none)) check(false, "no-lyrics notes.mid: " + p);

        // The folder the game scans.
        File dir = Files.createTempDirectory("yarg-song").toFile();
        Files.write(new File(dir, "notes.chart").toPath(), "old".getBytes("UTF-8"));
        SongMeta meta = new SongMeta();
        meta.title = "Two Voices";
        meta.artist = "First One feat. Second One";
        meta.singers = Lrc.artists(meta.artist, meta.title).toArray(new String[0]);
        SongWriter.writeFolder(dir, meta, r, "song.mp3");
        check(new File(dir, "notes.mid").length() == midi.length, "notes.mid written");
        check(!new File(dir, "notes.chart").exists(), "the old notes.chart is removed, so the song is not listed twice");
        String ini = new String(Files.readAllBytes(new File(dir, "song.ini").toPath()), "UTF-8");
        check(ini.contains("diff_vocals = " + r.vocalTier + "\n") && ini.contains("diff_vocals_harm = "), "song.ini lists the vocals");
        String parts = new String(Files.readAllBytes(new File(dir, SongWriter.PARTS_FILE).toPath()), "UTF-8");
        check(parts.equals("First One\nSecond One\n"), "singer names saved for the game: " + parts);
        SongWriter.writeFolder(dir, meta, solo, "song.mp3");
        check(!new File(dir, SongWriter.PARTS_FILE).exists(), "singer names removed when the song has one singer");
        for (File f : dir.listFiles()) f.delete();
        dir.delete();
    }
}
