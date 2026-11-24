package com.clockworkblack.yargconverter.core;

import java.io.BufferedWriter;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStreamWriter;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;

/** Writes the notes.chart and song.ini files of a YARG song folder. */
public final class SongWriter {
    private SongWriter() {}

    /** Everything the analysis produced for one song. */
    public static final class Result {
        public TempoMap tempo;
        /** Guitar charts. */
        public GuitarCharter.Chart chart;
        /** Bass charts, or null when there is no separate bass to chart. */
        public GuitarCharter.Chart bassChart;
        /** What the guitar chart was made from, in words for the user. */
        public String guitarSource = "the full mix";
        public int bassTier = -1;
        public List<LyricsCharter.Event> lyrics = new ArrayList<>();
        public double bpm;
        public double duration;
        public int lyricLines;
        public int beatsPerMeasure = 4;
        public int guitarTier;
        public long previewMs;

        public int noteCount(int difficulty) {
            return chart.notes.get(difficulty).size();
        }
    }

    /** Runs the whole analysis on already-measured audio. */
    public static Result analyze(Features f, List<Lrc.Line> lyrics) {
        return analyze(f, lyrics, null);
    }

    /** Measurements of the separated instruments, when the song has been split into stems. */
    public static final class Stems {
        /** The guitar on its own. */
        public Features guitar;
        /** Guitar, keys and other pitched instruments together (no drums, bass or vocals). */
        public Features melodic;
        /** The bass on its own. */
        public Features bass;
        /** Sum of squared samples of each, for comparing how much of the song they make up. */
        public double guitarEnergy, melodicEnergy, bassEnergy, mixEnergy;
    }

    public static Result analyze(Features f, List<Lrc.Line> lyrics, GuitarCharter.Options options) {
        return analyze(f, lyrics, options, null);
    }

    /**
     * @param f the full mix; always used for tempo, beats and measures
     * @param stems separated instruments, or null to chart everything from the full mix
     */
    public static Result analyze(Features f, List<Lrc.Line> lyrics, GuitarCharter.Options options, Stems stems) {
        Result r = new Result();
        BeatTracker.Result beats = BeatTracker.track(f);
        r.bpm = beats.bpm;
        r.duration = f.duration;
        r.beatsPerMeasure = beats.beatsPerMeasure;
        List<GuitarCharter.Onset> onsets = GuitarCharter.detectOnsets(f);
        double[] times = new double[onsets.size()];
        float[] strengths = new float[onsets.size()];
        for (int i = 0; i < times.length; i++) {
            times[i] = onsets.get(i).time;
            strengths[i] = onsets.get(i).strength;
        }
        double[] aligned = BeatTracker.alignToOnsets(beats.beats, times, strengths);
        r.tempo = TempoMap.fromBeats(aligned, beats.firstDownbeat, beats.beatsPerMeasure);
        r.chart = GuitarCharter.build(f, onsets, r.tempo, r.bpm, options);
        if (stems != null) chartFromStems(r, stems, options);
        if (lyrics != null && !lyrics.isEmpty()) {
            r.lyrics = LyricsCharter.build(lyrics, r.tempo, f.duration);
            for (LyricsCharter.Event e : r.lyrics) if (e.text.equals("phrase_start")) r.lyricLines++;
        }
        double nps = f.duration > 1 ? r.noteCount(GuitarCharter.EXPERT) / f.duration : 0;
        r.guitarTier = (int) Math.max(0, Math.min(6, Math.round(nps * 1.1)));
        r.previewMs = previewStart(f);
        return r;
    }

    /**
     * Replaces the full-mix guitar chart with one made from the separated guitar, and adds a
     * bass chart. If a stem turns out to be nearly empty (a song with no guitar, say), the next
     * best source is used instead, down to the full-mix chart already in {@code r}.
     */
    private static void chartFromStems(Result r, Stems stems, GuitarCharter.Options options) {
        int mixNotes = r.noteCount(GuitarCharter.EXPERT);
        // The guitar stem is used when guitar is a real part of the song's pitched instruments;
        // in a piano- or synth-led song the pitched instruments together stand in for it.
        boolean guitarLed = stems.guitar != null && stems.guitarEnergy >= 0.30 * Math.max(1e-12, stems.melodicEnergy);
        Features[] sources = guitarLed ? new Features[] {stems.guitar, stems.melodic} : new Features[] {stems.melodic, stems.guitar};
        String[] names = guitarLed
                ? new String[] {"the separated guitar", "the separated guitar and keys"}
                : new String[] {"the separated guitar and keys", "the separated guitar"};
        for (int i = 0; i < sources.length; i++) {
            if (sources[i] == null) continue;
            List<GuitarCharter.Onset> onsets = GuitarCharter.detectOnsets(sources[i]);
            GuitarCharter.Chart chart = GuitarCharter.build(sources[i], onsets, r.tempo, r.bpm, options);
            int notes = chart.notes.get(GuitarCharter.EXPERT).size();
            // A stem with almost nothing in it is not worth charting from.
            if (notes >= 24 && notes >= 0.2 * mixNotes) {
                r.chart = chart;
                r.guitarSource = names[i];
                break;
            }
        }
        if (stems.bass != null && stems.bassEnergy >= 0.003 * Math.max(1e-12, stems.mixEnergy)) {
            GuitarCharter.Options bassOptions = options == null ? new GuitarCharter.Options() : options.copy();
            bassOptions.chords = false;
            List<GuitarCharter.Onset> onsets = GuitarCharter.detectOnsets(stems.bass);
            GuitarCharter.Chart chart = GuitarCharter.build(stems.bass, onsets, r.tempo, r.bpm, bassOptions);
            int notes = chart.notes.get(GuitarCharter.EXPERT).size();
            if (notes >= 16) {
                r.bassChart = chart;
                double nps = r.duration > 1 ? notes / r.duration : 0;
                r.bassTier = (int) Math.max(0, Math.min(6, Math.round(nps * 1.1)));
            }
        }
    }

    /** Start of the loudest 30 seconds, used as the song preview in the menu. */
    private static long previewStart(Features f) {
        int n = f.frames();
        int win = (int) (f.fps * 30);
        if (n <= win + 1) return 0;
        double[] prefix = new double[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + f.rms[i];
        int best = 0;
        double bestSum = -1;
        int step = Math.max(1, (int) f.fps);
        for (int i = 0; i + win <= n; i += step) {
            double s = prefix[i + win] - prefix[i];
            if (s > bestSum) {
                bestSum = s;
                best = i;
            }
        }
        return Math.max(0, Math.round(f.timeOf(best) * 1000));
    }

    public static void writeFolder(File dir, SongMeta meta, Result r, String audioFileName) throws IOException {
        if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("Could not create " + dir);
        try (Writer w = open(new File(dir, "notes.chart"))) {
            writeChart(w, meta, r, audioFileName);
        }
        try (Writer w = open(new File(dir, "song.ini"))) {
            writeIni(w, meta, r);
        }
    }

    private static Writer open(File f) throws IOException {
        return new BufferedWriter(new OutputStreamWriter(new FileOutputStream(f), StandardCharsets.UTF_8));
    }

    private static String quote(String s) {
        return s.replace("\"", "'").replace("\r", " ").replace("\n", " ").trim();
    }

    public static void writeChart(Writer w, SongMeta meta, Result r, String audioFileName) throws IOException {
        TempoMap map = r.tempo;
        w.write("[Song]\n{\n");
        w.write("  Name = \"" + quote(meta.titleOrDefault()) + "\"\n");
        w.write("  Artist = \"" + quote(meta.artistOrDefault()) + "\"\n");
        w.write("  Charter = \"" + quote(meta.charter) + "\"\n");
        if (!meta.album.trim().isEmpty()) w.write("  Album = \"" + quote(meta.album) + "\"\n");
        if (!meta.year.trim().isEmpty()) w.write("  Year = \", " + quote(meta.year) + "\"\n");
        w.write("  Offset = 0\n");
        w.write("  Resolution = " + TempoMap.RESOLUTION + "\n");
        w.write("  Player2 = bass\n");
        w.write("  Difficulty = " + r.guitarTier + "\n");
        w.write("  PreviewStart = " + String.format(Locale.US, "%.2f", r.previewMs / 1000.0) + "\n");
        w.write("  PreviewEnd = 0\n");
        if (!meta.genre.trim().isEmpty()) w.write("  Genre = \"" + quote(meta.genre) + "\"\n");
        w.write("  MediaType = \"cd\"\n");
        w.write("  MusicStream = \"" + quote(audioFileName) + "\"\n");
        w.write("}\n");

        w.write("[SyncTrack]\n{\n");
        // A short first measure holds any beats before the first real "one".
        int full = map.beatsPerMeasure;
        long fullTick = map.origin();
        w.write("  0 = TS " + (map.pickupBeats > 0 ? map.pickupBeats : full) + "\n");
        boolean wroteFull = map.pickupBeats == 0;
        for (int i = 0; i < map.tick.length; i++) {
            if (!wroteFull && map.tick[i] >= fullTick) {
                w.write("  " + fullTick + " = TS " + full + "\n");
                wroteFull = true;
            }
            w.write("  " + map.tick[i] + " = B " + map.milliBpm[i] + "\n");
        }
        if (!wroteFull) w.write("  " + fullTick + " = TS " + full + "\n");
        w.write("}\n");

        // Global events: practice sections every 16 measures, plus lyrics. At the same tick a
        // phrase must close before the next opens, and open before its first word.
        List<Object[]> events = new ArrayList<>();
        long endTick = map.timeToTick(r.duration);
        int part = 1;
        for (int m = 0; map.measureStart(m) < endTick; m += 16) {
            long t = m == 0 ? 0 : map.measureStart(m);
            events.add(new Object[] {t, 0, "section Part " + part++});
        }
        for (LyricsCharter.Event e : r.lyrics) {
            int order = e.text.equals("phrase_end") ? 1 : e.text.equals("phrase_start") ? 2 : 3;
            events.add(new Object[] {e.tick, order, e.text});
        }
        Collections.sort(events, new Comparator<Object[]>() {
            @Override
            public int compare(Object[] a, Object[] b) {
                int c = Long.compare((Long) a[0], (Long) b[0]);
                return c != 0 ? c : Integer.compare((Integer) a[1], (Integer) b[1]);
            }
        });
        w.write("[Events]\n{\n");
        for (Object[] e : events) w.write("  " + e[0] + " = E \"" + e[2] + "\"\n");
        w.write("}\n");

        writeInstrument(w, r.chart, GuitarCharter.SECTION_NAMES);
        if (r.bassChart != null) writeInstrument(w, r.bassChart, BASS_SECTION_NAMES);
    }

    private static final String[] BASS_SECTION_NAMES = {"EasyDoubleBass", "MediumDoubleBass", "HardDoubleBass", "ExpertDoubleBass"};

    private static void writeInstrument(Writer w, GuitarCharter.Chart chart, String[] sectionNames) throws IOException {
        for (int d = GuitarCharter.EXPERT; d >= GuitarCharter.EASY; d--) {
            List<GuitarCharter.Note> notes = chart.notes.get(d);
            if (notes.isEmpty()) continue;
            w.write("[" + sectionNames[d] + "]\n{\n");
            int sp = 0;
            List<long[]> phrases = chart.starPower;
            for (GuitarCharter.Note n : notes) {
                while (sp < phrases.size() && phrases.get(sp)[0] <= n.tick) {
                    long[] p = phrases.get(sp++);
                    if (hasNoteIn(notes, p[0], p[0] + p[1])) w.write("  " + p[0] + " = S 2 " + p[1] + "\n");
                }
                int a = n.lane2 >= 0 ? Math.min(n.lane, n.lane2) : n.lane;
                w.write("  " + n.tick + " = N " + a + " " + n.sustain + "\n");
                if (n.lane2 >= 0) w.write("  " + n.tick + " = N " + Math.max(n.lane, n.lane2) + " " + n.sustain + "\n");
            }
            w.write("}\n");
        }
    }

    private static boolean hasNoteIn(List<GuitarCharter.Note> notes, long a, long b) {
        for (GuitarCharter.Note n : notes) {
            if (n.tick >= b) return false;
            if (n.tick >= a) return true;
        }
        return false;
    }

    public static void writeIni(Writer w, SongMeta meta, Result r) throws IOException {
        w.write("[song]\n");
        w.write("name = " + line(meta.titleOrDefault()) + "\n");
        w.write("artist = " + line(meta.artistOrDefault()) + "\n");
        if (!meta.album.trim().isEmpty()) w.write("album = " + line(meta.album) + "\n");
        if (!meta.genre.trim().isEmpty()) w.write("genre = " + line(meta.genre) + "\n");
        if (!meta.year.trim().isEmpty()) w.write("year = " + line(meta.year) + "\n");
        w.write("charter = " + line(meta.charter) + "\n");
        w.write("song_length = " + Math.round(r.duration * 1000) + "\n");
        w.write("preview_start_time = " + r.previewMs + "\n");
        w.write("diff_guitar = " + r.guitarTier + "\n");
        w.write("diff_bass = " + r.bassTier + "\n");
        w.write("diff_rhythm = -1\n");
        w.write("diff_drums = -1\n");
        w.write("diff_keys = -1\n");
        w.write("diff_vocals = -1\n");
        w.write("delay = 0\n");
        w.write("loading_phrase = Auto-charted from audio by YARG Converter.\n");
    }

    private static String line(String s) {
        return s.replace("\r", " ").replace("\n", " ").trim();
    }
}
