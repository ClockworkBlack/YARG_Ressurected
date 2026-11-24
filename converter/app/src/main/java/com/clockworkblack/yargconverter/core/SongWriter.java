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
        public GuitarCharter.Chart chart;
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

    public static Result analyze(Features f, List<Lrc.Line> lyrics, GuitarCharter.Options options) {
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
        if (lyrics != null && !lyrics.isEmpty()) {
            r.lyrics = LyricsCharter.build(lyrics, r.tempo, f.duration);
            for (LyricsCharter.Event e : r.lyrics) if (e.text.equals("phrase_start")) r.lyricLines++;
        }
        double nps = f.duration > 1 ? r.noteCount(GuitarCharter.EXPERT) / f.duration : 0;
        r.guitarTier = (int) Math.max(0, Math.min(6, Math.round(nps * 1.1)));
        r.previewMs = previewStart(f);
        return r;
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

        for (int d = GuitarCharter.EXPERT; d >= GuitarCharter.EASY; d--) {
            List<GuitarCharter.Note> notes = r.chart.notes.get(d);
            if (notes.isEmpty()) continue;
            w.write("[" + GuitarCharter.SECTION_NAMES[d] + "]\n{\n");
            int sp = 0;
            List<long[]> phrases = r.chart.starPower;
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
        w.write("diff_bass = -1\n");
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
