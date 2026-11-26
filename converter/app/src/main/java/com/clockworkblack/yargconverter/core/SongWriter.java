package com.clockworkblack.yargconverter.core;

import java.io.BufferedWriter;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStreamWriter;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
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
        /**
         * In a song carried by the vocals: measures without vocals (intro, breaks, outro) whose
         * guitar chart follows the instruments on their own. 0 otherwise.
         */
        public int instrumentalMeasures;
        /**
         * Deep Music Mode guitar (written as the co-op guitar part, which the game's Deep Music
         * Mode modifier plays in place of the guitar): the guitar chart plus the vocals and the
         * other instruments' melody, so that with no drum or vocal chart in use the song is still
         * all there. Null when the song was not split into instruments.
         */
        public GuitarCharter.Chart deepChart;
        /** Deep Music Mode bass (written as the rhythm part): the bass chart plus the drum hits. */
        public GuitarCharter.Chart deepBassChart;
        /** Notes Deep Music Mode adds on Expert, to the guitar and to the bass (drum hits). */
        public int deepGuitarNotesAdded, drumNotesInBass;
        public int deepGuitarTier = -1, deepBassTier = -1;
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
        /** The vocals on their own, for songs where the voice carries the tune. Null if not measured. */
        public Features vocals;
        /** The drums on their own, which the bass covers in a song carried by the vocals. Null if not measured. */
        public Features drums;
        /** Sum of squared samples of each, for comparing how much of the song they make up. */
        public double guitarEnergy, melodicEnergy, bassEnergy, mixEnergy, vocalsEnergy, drumsEnergy;
    }

    /**
     * Below this share of (pitched instruments + vocals), the instruments are too thin to chart on
     * their own and the guitar chart follows the vocals with them. Two hip-hop tracks measured 18%
     * and 29% (the second has a quiet piano loop); band recordings with a real guitar part sit
     * far above it.
     */
    static final double VOCAL_LED_SHARE = 0.35;

    /** A measure whose vocals are under this share of the song's usual vocal level has none. */
    static final double NO_VOCALS_LEVEL = 0.1;

    /**
     * Note threshold for the instruments in a song carried by the vocals, against 1 for standard.
     * A chime or keys loop under a rap is quiet, and at the standard threshold a soft piano intro
     * loses a third of its notes.
     */
    static final double INSTRUMENTAL_THRESHOLD = 0.5;

    /** True when the separated instruments are a small part of the song next to the vocals. */
    static boolean vocalLed(Stems stems) {
        if (stems.vocals == null || stems.vocalsEnergy <= 0) return false;
        double pitched = stems.melodicEnergy;
        return pitched < VOCAL_LED_SHARE * (pitched + stems.vocalsEnergy);
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
        // In a song carried by the voice (rap over a beat, say) the guitar covers the vocals and
        // whatever melody the beat has (a chime, a keys loop), and the bass covers the drums too.
        boolean vocalLed = vocalLed(stems) && chartVocalLed(r, stems, options, mixNotes);
        for (int i = 0; i < sources.length && !vocalLed; i++) {
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
            bassOptions.bass = true;
            List<GuitarCharter.Onset> onsets = GuitarCharter.detectOnsets(stems.bass, true);
            GuitarCharter.Chart chart = GuitarCharter.build(stems.bass, onsets, r.tempo, r.bpm, bassOptions);
            int notes = chart.notes.get(GuitarCharter.EXPERT).size();
            if (notes >= 16) r.bassChart = chart;
        }
        if (r.bassChart != null) r.bassTier = tier(r.bassChart, r.duration);
        chartDeep(r, stems, options);
    }

    private static int tier(GuitarCharter.Chart chart, double duration) {
        double nps = duration > 1 ? chart.notes.get(GuitarCharter.EXPERT).size() / duration : 0;
        return (int) Math.max(0, Math.min(6, Math.round(nps * 1.1)));
    }

    /**
     * Deep Music Mode: with no drum or vocal chart in use, guitar and bass cover the whole song.
     * Guitar adds the vocals and the other instruments' melody (a chime, a keys loop) wherever
     * they do not fall right next to a guitar note; bass adds the drum hits between its notes.
     */
    private static void chartDeep(Result r, Stems stems, GuitarCharter.Options options) {
        if (r.deepChart == null && r.chart != null) {
            GuitarCharter.Options single = options == null ? new GuitarCharter.Options() : options.copy();
            single.chords = false;
            GuitarCharter.Options soft = single.copy();
            soft.threshold *= INSTRUMENTAL_THRESHOLD;
            GuitarCharter.Chart vocals = stems.vocals == null || stems.vocalsEnergy < 0.003 * Math.max(1e-12, stems.mixEnergy) ? null
                    : GuitarCharter.build(stems.vocals, GuitarCharter.detectOnsets(stems.vocals), r.tempo, r.bpm, single);
            boolean[] quiet = vocals == null ? null : measuresWithoutVocals(stems.vocals, r.tempo, r.duration);
            GuitarCharter.Chart melody = stems.melodic == null || r.guitarSource.startsWith("the separated guitar and keys") ? null
                    : GuitarCharter.build(stems.melodic, GuitarCharter.detectOnsets(stems.melodic), r.tempo, r.bpm, soft);
            GuitarCharter.Chart deep = copy(r.chart);
            for (int d = 0; d < deep.notes.size(); d++) {
                List<GuitarCharter.Note> extra = new ArrayList<>();
                if (vocals != null) for (GuitarCharter.Note n : vocals.notes.get(d)) if (!isQuiet(quiet, r.tempo, n.tick)) extra.add(n);
                if (melody != null) extra = merge(extra, melody.notes.get(d), MERGE_GAP[d]);
                deep.notes.set(d, merge(deep.notes.get(d), extra, MERGE_GAP[d]));
            }
            r.deepChart = deep;
        }
        r.deepGuitarNotesAdded = r.deepChart.notes.get(GuitarCharter.EXPERT).size() - r.chart.notes.get(GuitarCharter.EXPERT).size();
        r.deepGuitarTier = tier(r.deepChart, r.duration);
        addDrumsToBass(r, stems, options);
        if (r.deepBassChart == null && r.bassChart != null) r.deepBassChart = copy(r.bassChart);
        if (r.deepBassChart != null) r.deepBassTier = tier(r.deepBassChart, r.duration);
    }

    private static GuitarCharter.Chart copy(GuitarCharter.Chart c) {
        GuitarCharter.Chart out = new GuitarCharter.Chart();
        for (List<GuitarCharter.Note> l : c.notes) out.notes.add(new ArrayList<>(l));
        out.starPower.addAll(c.starPower);
        out.onsetCount = c.onsetCount;
        out.soundGroups = c.soundGroups;
        out.repeatedMeasures = c.repeatedMeasures;
        return out;
    }

    /**
     * Closest a note from a second source may come to a note already in the chart, in ticks, per
     * difficulty (Easy..Expert): a beat, an eighth, an eighth, a sixteenth.
     */
    private static final long[] MERGE_GAP = {192, 96, 96, 48};

    /**
     * Guitar for a song carried by the vocals: the vocals charted on their own, and the
     * instruments (with a lower bar, since a chime or keys loop under a rap is quiet) in the
     * measures without vocals, where the vocal part holds only leakage. Deep Music Mode adds every
     * instrument note that does not fall right next to a vocal note. Single notes throughout: a
     * voice sings one note at a time, and its breathy sound would otherwise read as chords.
     *
     * @return false if there is too little to chart, leaving the normal sources to be tried
     */
    private static boolean chartVocalLed(Result r, Stems stems, GuitarCharter.Options options, int mixNotes) {
        GuitarCharter.Options single = options == null ? new GuitarCharter.Options() : options.copy();
        single.chords = false;
        GuitarCharter.Options soft = single.copy();
        soft.threshold *= INSTRUMENTAL_THRESHOLD;
        GuitarCharter.Chart vocals = GuitarCharter.build(stems.vocals, GuitarCharter.detectOnsets(stems.vocals), r.tempo, r.bpm, single);
        GuitarCharter.Chart instruments = stems.melodic == null ? null
                : GuitarCharter.build(stems.melodic, GuitarCharter.detectOnsets(stems.melodic), r.tempo, r.bpm, soft);
        boolean[] quiet = measuresWithoutVocals(stems.vocals, r.tempo, r.duration);
        GuitarCharter.Chart out = new GuitarCharter.Chart(), deep = new GuitarCharter.Chart();
        for (int d = 0; d < vocals.notes.size(); d++) {
            List<GuitarCharter.Note> sung = new ArrayList<>();
            for (GuitarCharter.Note n : vocals.notes.get(d)) if (!isQuiet(quiet, r.tempo, n.tick)) sung.add(n);
            List<GuitarCharter.Note> played = instruments == null ? new ArrayList<GuitarCharter.Note>() : instruments.notes.get(d);
            List<GuitarCharter.Note> between = new ArrayList<>();
            for (GuitarCharter.Note n : played) if (isQuiet(quiet, r.tempo, n.tick)) between.add(n);
            // Normal guitar: the vocals, and the instruments where nobody sings. Deep Music Mode:
            // the instruments' melody all the way through as well.
            out.notes.add(merge(sung, between, MERGE_GAP[d]));
            deep.notes.add(merge(sung, played, MERGE_GAP[d]));
        }
        int notes = out.notes.get(GuitarCharter.EXPERT).size();
        if (notes < 24 || notes < 0.2 * mixNotes) return false;
        for (GuitarCharter.Chart c : new GuitarCharter.Chart[] {out, deep}) {
            c.starPower.addAll(vocals.starPower);
            c.onsetCount = vocals.onsetCount + (instruments == null ? 0 : instruments.onsetCount);
            c.soundGroups = vocals.soundGroups + (instruments == null ? 0 : instruments.soundGroups);
            c.repeatedMeasures = instruments == null ? vocals.repeatedMeasures : Math.max(vocals.repeatedMeasures, instruments.repeatedMeasures);
        }
        r.chart = out;
        r.deepChart = deep;
        r.guitarSource = "the separated instruments and vocals, as single notes";
        int quietCount = 0;
        for (boolean q : quiet) if (q) quietCount++;
        r.instrumentalMeasures = quietCount;
        return true;
    }

    /**
     * Deep Music Mode bass: the drum hits between the bass notes join a copy of the bass chart. A hit takes the fret of the bass note that is sounding
     * (a kick under an 808 plays its note); a bright hit (snare, clap, hat) takes the fret next to
     * it, so the two feel different. A held note ends where a drum hit comes in.
     */
    private static void addDrumsToBass(Result r, Stems stems, GuitarCharter.Options options) {
        if (stems.drums == null || stems.drumsEnergy < 0.003 * Math.max(1e-12, stems.mixEnergy)) return;
        GuitarCharter.Options single = options == null ? new GuitarCharter.Options() : options.copy();
        single.chords = false;
        List<GuitarCharter.Onset> hits = GuitarCharter.detectOnsets(stems.drums);
        if (hits.isEmpty()) return;
        GuitarCharter.Chart drums = GuitarCharter.build(stems.drums, hits, r.tempo, r.bpm, single);
        float[] bright = new float[hits.size()];
        double[] times = new double[hits.size()];
        for (int i = 0; i < bright.length; i++) {
            bright[i] = hits.get(i).bright;
            times[i] = hits.get(i).time;
        }
        float[] sorted = bright.clone();
        Arrays.sort(sorted);
        float brightLine = sorted[sorted.length / 2];
        GuitarCharter.Chart bass = r.bassChart != null ? copy(r.bassChart) : new GuitarCharter.Chart();
        int added = 0;
        for (int d = 0; d < drums.notes.size(); d++) {
            List<GuitarCharter.Note> base = d < bass.notes.size() ? bass.notes.get(d) : new ArrayList<GuitarCharter.Note>();
            int lanes = GuitarCharter.lanes(d);
            List<GuitarCharter.Note> placed = new ArrayList<>();
            int at = 0;
            for (GuitarCharter.Note hit : drums.notes.get(d)) {
                while (at + 1 < base.size() && base.get(at + 1).tick <= hit.tick) at++;
                int lane = base.isEmpty() ? 0 : base.get(at).tick <= hit.tick ? base.get(at).lane : base.get(0).lane;
                if (isBright(times, bright, brightLine, r.tempo.tickToTime(hit.tick))) lane = lane + 1 < lanes ? lane + 1 : lane - 1;
                placed.add(new GuitarCharter.Note(hit.tick, Math.max(0, lane), -1, 0));
            }
            List<GuitarCharter.Note> merged = merge(base, placed, MERGE_GAP[d]);
            if (d == GuitarCharter.EXPERT) added = merged.size() - base.size();
            if (d < bass.notes.size()) bass.notes.set(d, merged);
            else bass.notes.add(merged);
        }
        if (bass.notes.get(GuitarCharter.EXPERT).size() < 16) return;
        r.deepBassChart = bass;
        r.drumNotesInBass = added;
    }

    /** Whether the drum hit nearest to {@code time} is a bright one (snare, clap, hat). */
    private static boolean isBright(double[] times, float[] bright, float line, double time) {
        int k = Arrays.binarySearch(times, time);
        if (k < 0) k = -k - 1;
        int best = -1;
        for (int j = Math.max(0, k - 1); j <= Math.min(times.length - 1, k); j++) {
            if (best < 0 || Math.abs(times[j] - time) < Math.abs(times[best] - time)) best = j;
        }
        return best >= 0 && Math.abs(times[best] - time) < 0.08 && bright[best] > line;
    }

    /**
     * The notes of {@code base}, plus each note of {@code extra} that is at least {@code gap}
     * ticks from every base note, in order. Held notes let go before the next note.
     */
    static List<GuitarCharter.Note> merge(List<GuitarCharter.Note> base, List<GuitarCharter.Note> extra, long gap) {
        List<GuitarCharter.Note> merged = new ArrayList<>(base);
        long[] ticks = new long[base.size()];
        for (int i = 0; i < ticks.length; i++) ticks[i] = base.get(i).tick;
        Arrays.sort(ticks);
        for (GuitarCharter.Note n : extra) {
            int k = Arrays.binarySearch(ticks, n.tick);
            if (k >= 0) continue;
            k = -k - 1;
            boolean clash = (k < ticks.length && ticks[k] - n.tick < gap) || (k > 0 && n.tick - ticks[k - 1] < gap);
            if (!clash) merged.add(n);
        }
        Collections.sort(merged, new Comparator<GuitarCharter.Note>() {
            @Override
            public int compare(GuitarCharter.Note a, GuitarCharter.Note b) {
                return Long.compare(a.tick, b.tick);
            }
        });
        for (int i = 0; i + 1 < merged.size(); i++) {
            GuitarCharter.Note n = merged.get(i);
            long room = merged.get(i + 1).tick - n.tick - TempoMap.RESOLUTION / 2;
            if (n.sustain > 0 && n.sustain > room) {
                long sustain = room >= TempoMap.RESOLUTION / 2 ? room : 0;
                merged.set(i, new GuitarCharter.Note(n.tick, n.lane, n.lane2, sustain));
            }
        }
        return merged;
    }

    /**
     * Which measures have no vocals, indexed by measure + 1 (so the pickup before the first full
     * measure is entry 0): those whose vocal level is a small part of the song's usual level (the
     * level that a quarter of the measures reach).
     */
    static boolean[] measuresWithoutVocals(Features v, TempoMap tempo, double duration) {
        int count = Math.max(0, tempo.measureOf(tempo.timeToTick(duration))) + 2;
        double[] sum = new double[count];
        int[] frames = new int[count];
        for (int i = 0; i < v.frames(); i++) {
            int at = tempo.measureOf(tempo.timeToTick(v.timeOf(i))) + 1;
            if (at < 0 || at >= count) continue;
            sum[at] += (double) v.rms[i] * v.rms[i];
            frames[at]++;
        }
        double[] level = new double[count];
        int used = 0;
        for (int k = 0; k < count; k++) if (frames[k] > 0) level[used++] = sum[k] / frames[k];
        boolean[] quiet = new boolean[count];
        if (used == 0) return quiet;
        double[] sorted = Arrays.copyOf(level, used);
        Arrays.sort(sorted);
        double usual = sorted[(int) (used * 0.75)];
        if (usual <= 0) return quiet;
        for (int k = 0; k < count; k++) quiet[k] = frames[k] > 0 && sum[k] / frames[k] < NO_VOCALS_LEVEL * usual;
        return quiet;
    }

    private static boolean isQuiet(boolean[] quiet, TempoMap tempo, long tick) {
        int at = tempo.measureOf(tick) + 1;
        return at >= 0 && at < quiet.length && quiet[at];
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
        // Deep Music Mode parts, in the co-op guitar and rhythm slots that YARG Resurrected's
        // Deep Music Mode modifier plays in place of guitar and bass.
        if (r.deepChart != null) writeInstrument(w, r.deepChart, DEEP_GUITAR_SECTION_NAMES);
        if (r.deepBassChart != null) writeInstrument(w, r.deepBassChart, DEEP_BASS_SECTION_NAMES);
    }

    private static final String[] BASS_SECTION_NAMES = {"EasyDoubleBass", "MediumDoubleBass", "HardDoubleBass", "ExpertDoubleBass"};
    private static final String[] DEEP_GUITAR_SECTION_NAMES = {"EasyDoubleGuitar", "MediumDoubleGuitar", "HardDoubleGuitar", "ExpertDoubleGuitar"};
    private static final String[] DEEP_BASS_SECTION_NAMES = {"EasyDoubleRhythm", "MediumDoubleRhythm", "HardDoubleRhythm", "ExpertDoubleRhythm"};

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
        w.write("diff_rhythm = " + (r.deepBassChart != null ? r.deepBassTier : -1) + "\n");
        w.write("diff_guitar_coop = " + (r.deepChart != null ? r.deepGuitarTier : -1) + "\n");
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
