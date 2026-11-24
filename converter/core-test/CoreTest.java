import com.clockworkblack.yargconverter.core.AudioAnalyzer;
import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.Lrc;
import com.clockworkblack.yargconverter.core.SongMeta;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.TempoMap;

import java.io.BufferedReader;
import java.io.DataInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.StringReader;
import java.io.StringWriter;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/**
 * Self-contained checks for the chart engine. No Android and no test framework needed:
 *   javac -d out $(find ../app/src/main/java -path '*core*' -name '*.java') CoreTest.java
 *   java -cp out CoreTest
 * With arguments it converts raw audio instead (mono 32-bit float little-endian):
 *   java -cp out CoreTest song.f32 44100 outDir [lyrics.lrc]
 */
public final class CoreTest {
    private static int failures;

    public static void main(String[] args) throws Exception {
        if (args.length >= 3) {
            cli(args);
            return;
        }
        synthetic(44100, 128.0, 0.31, 70, 1);
        synthetic(48000, 93.0, 0.05, 60, 2);
        synthetic(44100, 150.0, 0.62, 45, 3);
        synthetic(22050, 110.0, 0.20, 40, 4);
        band("Band, steady 118 BPM", new SynthBand(44100, 118.0, 0.0, 0.40, 4, 7), 4);
        band("Band, tempo drifting around 104 BPM", new SynthBand(44100, 104.0, 0.03, 0.25, 4, 7), 4);
        band("Band, fast 142 BPM at 48 kHz", new SynthBand(48000, 142.0, 0.015, 0.10, 4, 7), 4);
        band("Band, busy mix 96 BPM", new SynthBand(44100, 96.0, 0.02, 0.50, 4, 7, true), 4);
        band("Band, waltz 126 BPM", new SynthBand(44100, 126.0, 0.0, 0.30, 3, 7), 3);
        oddInputs();
        lyrics();
        if (failures > 0) {
            System.out.println("FAILED: " + failures + " check(s)");
            System.exit(1);
        }
        System.out.println("All checks passed");
    }

    private static void check(boolean ok, String what) {
        if (!ok) {
            failures++;
            System.out.println("  FAIL: " + what);
        }
    }

    /**
     * A whole synthetic band with verses and choruses: checks that the charts follow the guitar,
     * that the same note keeps its fret, that higher notes go to higher frets, that repeated
     * sections get the same chart, and that chords and single notes are told apart.
     */
    private static void band(String name, SynthBand song, int beatsPerMeasure) throws Exception {
        System.out.println(name);
        AudioAnalyzer an = new AudioAnalyzer(song.sampleRate);
        for (int i = 0; i < song.audio.length; i += 4096) an.feed(song.audio, i, Math.min(4096, song.audio.length - i));
        SongWriter.Result r = SongWriter.analyze(an.finish(), null);
        ChartEval e = ChartEval.score(song, r);
        System.out.println(e.report());
        boolean four = beatsPerMeasure == 4;
        check(r.beatsPerMeasure == beatsPerMeasure, "measures of " + beatsPerMeasure + " beats (found " + r.beatsPerMeasure + ")");
        check(Math.abs(e.tempoRatio - 1) < 0.04, "tempo right");
        check(e.beatWorstMs < 40 && Math.abs(e.beatAvgMs) < 15, "beat grid tight");
        check(e.downbeatRate >= 0.9, "measures start on the downbeat");
        check(e.coverage >= (four ? 0.95 : 0.9), "guitar notes are charted");
        check(e.samePitchSameFret >= (four ? 0.85 : 0.7), "same note keeps its fret");
        check(e.rightDirection >= 0.6 && e.wrongDirection <= (four ? 0.1 : 0.2), "frets follow pitch");
        check(e.repeatMatch >= (four ? 0.85 : 0.7), "repeated sections get the same chart");
        check(e.chordAsChord >= (four ? 0.85 : 0.7) && e.singleAsSingle >= 0.85, "chords and single notes told apart");
        check(e.subset >= 0.99, "easier levels are thinned-out Expert");
        check(e.counts[0] < e.counts[1] && e.counts[1] < e.counts[2] && e.counts[2] <= e.counts[3], "note counts rise with difficulty");
        for (int d = 0; d < 4; d++) {
            for (GuitarCharter.Note n : r.chart.notes.get(d)) {
                int top = d == 0 ? 2 : d == 1 ? 3 : 4;
                check(n.lane >= 0 && n.lane <= top && n.lane2 <= top && n.lane2 != n.lane, "frets in range for level " + d);
                check(d != 0 || n.lane2 < 0, "no chords on Easy");
            }
        }
        StringWriter sw = new StringWriter();
        SongWriter.writeChart(sw, new SongMeta(), r, "song.mp3");
        validateChart(sw.toString(), 7, false);
    }

    /** Inputs with nothing sensible to chart must still produce a valid song, never a crash. */
    private static void oddInputs() throws Exception {
        System.out.println("Odd inputs (silence, noise, one long tone, clicks, two seconds of audio)");
        Random rnd = new Random(3);
        int sr = 44100;
        float[][] cases = new float[5][];
        cases[0] = new float[sr * 20];
        cases[1] = new float[sr * 20];
        for (int i = 0; i < cases[1].length; i++) cases[1][i] = (float) (rnd.nextDouble() - 0.5);
        cases[2] = new float[sr * 20];
        for (int i = 0; i < cases[2].length; i++) cases[2][i] = (float) (0.4 * Math.sin(2 * Math.PI * 220 * i / sr));
        cases[3] = new float[sr * 20];
        for (int i = 0; i < 40; i++) for (int k = 0; k < 200; k++) cases[3][i * sr / 2 + k] = (float) (rnd.nextDouble() - 0.5);
        cases[4] = new float[sr * 2];
        for (int i = 0; i < cases[4].length; i++) cases[4][i] = (float) (0.3 * Math.sin(2 * Math.PI * 330 * i / sr) * (i % 11025 < 4000 ? 1 : 0));
        for (float[] x : cases) {
            AudioAnalyzer an = new AudioAnalyzer(sr);
            an.feed(x, 0, x.length);
            SongWriter.Result r = SongWriter.analyze(an.finish(), Lrc.parse("[00:01.00]La la\n[00:30.00]Too late"));
            StringWriter sw = new StringWriter();
            SongWriter.writeChart(sw, new SongMeta(), r, "song.wav");
            validateChart(sw.toString(), -1, true);
            check(r.bpm > 30 && r.bpm < 300, "sane tempo for odd input (" + r.bpm + ")");
        }
    }

    /** A drum + melody loop at a known tempo, so beats and note timing can be verified exactly. */
    private static void synthetic(int sr, double bpm, double offset, int seconds, long seed) throws Exception {
        System.out.println("Synthetic song: " + bpm + " BPM, " + sr + " Hz, first beat at " + offset + " s");
        Random rnd = new Random(seed);
        int n = sr * seconds;
        float[] x = new float[n];
        double beat = 60.0 / bpm;
        List<Double> noteTimes = new ArrayList<>();
        int[] scale = {0, 2, 4, 7, 9, 12, 14, 16};
        int beats = (int) ((seconds - offset - 1) / beat);
        for (int b = 0; b < beats; b++) {
            double t = offset + b * beat;
            boolean down = b % 4 == 0;
            // Kick on every beat, louder on the one.
            burst(x, sr, t, 0.12, 60, down ? 0.9f : 0.55f, false, rnd);
            burst(x, sr, t, 0.008, 0, down ? 0.3f : 0.2f, true, rnd); // beater click
            // Hi-hat on the off-beat.
            burst(x, sr, t + beat / 2, 0.03, 0, 0.12f, true, rnd);
            // Melody: eighth notes with some gaps and a few sixteenths.
            for (int s = 0; s < 4; s++) {
                boolean play = s == 0 ? rnd.nextInt(10) < 8 : s == 2 ? rnd.nextInt(10) < 6 : rnd.nextInt(10) < 2;
                if (!play) continue;
                double nt = t + s * beat / 4;
                double hz = 220.0 * Math.pow(2, scale[rnd.nextInt(scale.length)] / 12.0);
                tone(x, sr, nt, beat * 0.45, hz, 0.35f);
                noteTimes.add(nt);
            }
        }
        AudioAnalyzer an = new AudioAnalyzer(sr);
        for (int i = 0; i < n; i += 4096) an.feed(x, i, Math.min(4096, n - i));
        Features f = an.finish();

        List<Lrc.Line> lrc = Lrc.parse("[00:05.00]Hello there world\n[00:08.50]Second line \"quoted\" well-\n[00:12.00]\n[00:20.00]Last one");
        SongWriter.Result r = SongWriter.analyze(f, lrc);
        System.out.printf("  tempo %.2f BPM, %d tempo marker(s), pickup %d, notes E/M/H/X = %d/%d/%d/%d, star power %d%n",
                r.bpm, r.tempo.tick.length, r.tempo.pickupBeats,
                r.noteCount(0), r.noteCount(1), r.noteCount(2), r.noteCount(3), r.chart.starPower.size());

        double ratio = r.bpm / bpm;
        check(Math.abs(ratio - 1) < 0.01 || Math.abs(ratio - 0.5) < 0.005 || Math.abs(ratio - 2) < 0.02,
                "tempo " + r.bpm + " should match " + bpm);

        // Every true beat must sit on the chart's beat grid (allowing a half/double-time grid).
        double worst = 0, sum = 0;
        for (int b = 0; b < beats; b++) {
            double t = offset + b * beat;
            long tick = r.tempo.snap(t, TempoMap.RESOLUTION / 2);
            double err = r.tempo.tickToTime(tick) - t;
            worst = Math.max(worst, Math.abs(err));
            sum += err;
        }
        System.out.printf("  beat grid error: worst %.1f ms, average %+.1f ms%n", worst * 1000, sum / beats * 1000);
        check(worst < 0.030, "beat grid within 30 ms (worst " + worst + ")");
        check(Math.abs(sum / beats) < 0.012, "beat grid not biased (avg " + sum / beats + ")");

        // The first full measure should start on a kick-heavy "one".
        double firstMeasure = r.tempo.tickToTime(r.tempo.measureStart(0));
        double rel = (firstMeasure - offset) / beat;
        if (Math.abs(ratio - 1) < 0.01) {
            check(Math.abs(rel - Math.round(rel)) < 0.1 && Math.floorMod(Math.round(rel), 4) == 0,
                    "measure starts on the downbeat (found beat " + rel + ")");
        }

        // Expert notes should land on real note starts.
        List<GuitarCharter.Note> expert = r.chart.notes.get(GuitarCharter.EXPERT);
        int near = 0;
        double bias = 0;
        for (GuitarCharter.Note note : expert) {
            double t = r.tempo.tickToTime(note.tick);
            double best = 1e9;
            for (double nt : noteTimes) if (Math.abs(nt - t) < Math.abs(best)) best = nt - t;
            for (int b = 0; b < beats * 2; b++) {
                double bt = offset + b * beat / 2;
                if (Math.abs(bt - t) < Math.abs(best)) best = bt - t;
            }
            if (Math.abs(best) < 0.045) near++;
            bias += best;
        }
        check(!expert.isEmpty(), "expert chart has notes");
        if (!expert.isEmpty()) {
            System.out.printf("  expert notes on real sounds: %d%% (avg offset %+.1f ms)%n", near * 100 / expert.size(), bias / expert.size() * 1000);
            check(near >= expert.size() * 0.92, "expert notes line up with sounds");
        }
        check(r.noteCount(0) > 0 && r.noteCount(0) < r.noteCount(1) && r.noteCount(1) < r.noteCount(2) && r.noteCount(2) <= r.noteCount(3),
                "note counts rise with difficulty");
        int[] maxLane = new int[4];
        for (int d = 0; d < 4; d++) {
            long prev = -1;
            boolean[] used = new boolean[5];
            for (GuitarCharter.Note note : r.chart.notes.get(d)) {
                check(note.tick > prev, "notes strictly ordered");
                prev = note.tick;
                maxLane[d] = Math.max(maxLane[d], Math.max(note.lane, note.lane2));
                used[note.lane] = true;
                check(note.lane2 != note.lane, "chord uses two different frets");
            }
            int count = 0;
            for (boolean u : used) if (u) count++;
            check(count >= (d == 0 ? 3 : d == 1 ? 3 : 4), "difficulty " + d + " uses a spread of frets (" + count + ")");
        }
        check(maxLane[0] <= 2 && maxLane[1] <= 3, "easy uses 3 frets, medium 4");
        check(r.lyricLines == 3, "three lyric lines charted (got " + r.lyricLines + ")");

        SongMeta meta = new SongMeta();
        meta.title = "Test \"Song\"";
        meta.artist = "Tester";
        StringWriter sw = new StringWriter();
        SongWriter.writeChart(sw, meta, r, "song.mp3");
        validateChart(sw.toString(), 7, true);
    }

    private static void burst(float[] x, int sr, double t, double len, double hz, float amp, boolean noise, Random rnd) {
        int start = (int) Math.round(t * sr);
        int count = (int) (len * sr);
        for (int i = 0; i < count && start + i < x.length; i++) {
            double env = Math.exp(-5.0 * i / count);
            double v = noise ? rnd.nextDouble() * 2 - 1 : Math.sin(2 * Math.PI * hz * i / sr * (1 + 0.5 * env));
            x[start + i] += (float) (amp * env * v);
        }
    }

    private static void tone(float[] x, int sr, double t, double len, double hz, float amp) {
        int start = (int) Math.round(t * sr);
        int count = (int) (len * sr);
        for (int i = 0; i < count && start + i < x.length; i++) {
            double env = Math.min(1.0, i / (0.004 * sr)) * Math.exp(-3.0 * i / count);
            double ph = 2 * Math.PI * hz * i / sr;
            double v = Math.sin(ph) + 0.5 * Math.sin(2 * ph) + 0.25 * Math.sin(3 * ph);
            x[start + i] += (float) (amp * env * v / 1.75);
        }
    }

    /** Structural check of a .chart file: sections, ordering, and well-formed lines. */
    private static void validateChart(String text, int expectSections, boolean testTitle) throws Exception {
        BufferedReader br = new BufferedReader(new StringReader(text));
        String line;
        String section = null;
        long prev = -1;
        int sections = 0;
        boolean inPhrase = false;
        boolean sawTempo = false;
        while ((line = br.readLine()) != null) {
            String s = line.trim();
            if (s.startsWith("[")) {
                section = s;
                sections++;
                prev = -1;
                continue;
            }
            if (s.equals("{") || s.equals("}") || section == null || section.equals("[Song]")) continue;
            String[] kv = s.split(" = ", 2);
            check(kv.length == 2, "chart line has key = value: " + s);
            long tick = Long.parseLong(kv[0]);
            check(tick >= prev, "ticks never go backwards in " + section + ": " + s);
            prev = tick;
            if (section.equals("[SyncTrack]")) {
                check(kv[1].matches("(TS \\d+|B \\d+)"), "sync line: " + s);
                if (kv[1].startsWith("B ")) {
                    if (!sawTempo) check(tick == 0, "first tempo marker at tick 0");
                    sawTempo = true;
                }
            } else if (section.equals("[Events]")) {
                check(kv[1].matches("E \"[^\"]+\""), "event line: " + s);
                if (kv[1].contains("phrase_start")) {
                    check(!inPhrase, "phrase opened twice");
                    inPhrase = true;
                } else if (kv[1].contains("phrase_end")) {
                    check(inPhrase, "phrase closed without opening");
                    inPhrase = false;
                } else if (kv[1].startsWith("E \"lyric ")) {
                    check(inPhrase, "lyric outside a phrase: " + s);
                }
            } else {
                check(kv[1].matches("(N [0-4] \\d+|S 2 \\d+)"), "note line: " + s);
            }
        }
        check(sawTempo, "chart has a tempo");
        check(expectSections < 0 ? sections >= 3 : sections == expectSections, "chart has Song, SyncTrack, Events and four difficulties (got " + sections + ")");
        if (testTitle && expectSections > 0) check(text.contains("Name = \"Test 'Song'\""), "quotes in the title are made safe");
    }

    private static void lyrics() {
        System.out.println("Lyrics parsing");
        List<Lrc.Line> l = Lrc.parse("[ar:Someone]\n[offset:500]\n[00:10.50][01:00]Chorus line\n[00:02.5]First <00:03.00>word\nno stamp\n[02:03.456]End");
        check(l.size() == 4, "four timed lines (got " + l.size() + ")");
        if (l.size() == 4) {
            check(Math.abs(l.get(0).time - 2.0) < 1e-6 && l.get(0).text.equals("First word"), "word stamps stripped, offset applied: " + l.get(0).time + " '" + l.get(0).text + "'");
            check(Math.abs(l.get(1).time - 10.0) < 1e-6, "second line time");
            check(Math.abs(l.get(2).time - 59.5) < 1e-6, "repeated stamp");
            check(Math.abs(l.get(3).time - 122.956) < 1e-6, "millisecond stamp");
        }
        check(Lrc.hasTimestamps("[00:01.00]a") && !Lrc.hasTimestamps("just words\n[Chorus]"), "timestamp detection");
        List<String> plain = Lrc.plainLines("[Verse 1]\nOne  two\n\n(Chorus)\nThree");
        check(plain.size() == 2 && plain.get(0).equals("One two"), "plain lines skip headers: " + plain);
        check(Lrc.stamp(83.456).equals("[01:23.46]"), "stamp format " + Lrc.stamp(83.456));
        SongMeta m = new SongMeta();
        m.artist = "AC/DC";
        m.title = "What? Why: \"Yes\".";
        check(m.folderName().equals("AC_DC - What_ Why_ _Yes_"), "folder name: " + m.folderName());
    }

    private static void cli(String[] args) throws Exception {
        File in = new File(args[0]);
        int sr = Integer.parseInt(args[1]);
        File out = new File(args[2]);
        AudioAnalyzer an = new AudioAnalyzer(sr);
        byte[] bytes = new byte[4096 * 4];
        float[] buf = new float[4096];
        try (DataInputStream dis = new DataInputStream(new FileInputStream(in))) {
            while (true) {
                int got = dis.readNBytes(bytes, 0, bytes.length);
                if (got < 4) break;
                int count = got / 4;
                ByteBuffer.wrap(bytes, 0, count * 4).order(ByteOrder.LITTLE_ENDIAN).asFloatBuffer().get(buf, 0, count);
                an.feed(buf, 0, count);
            }
        }
        List<Lrc.Line> lrc = args.length > 3 ? Lrc.parse(new String(Files.readAllBytes(new File(args[3]).toPath()), "UTF-8")) : null;
        SongWriter.Result r = SongWriter.analyze(an.finish(), lrc);
        SongMeta meta = new SongMeta();
        meta.title = in.getName();
        SongWriter.writeFolder(out, meta, r, "song.mp3");
        System.out.printf("%.1f s, %.2f BPM, %d tempo marker(s), notes E/M/H/X = %d/%d/%d/%d, lyric lines %d, tier %d%n",
                r.duration, r.bpm, r.tempo.tick.length, r.noteCount(0), r.noteCount(1), r.noteCount(2), r.noteCount(3), r.lyricLines, r.guitarTier);
    }
}
