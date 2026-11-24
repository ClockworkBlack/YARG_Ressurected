package com.clockworkblack.yargconverter.core;

import java.io.BufferedWriter;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStreamWriter;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/** Runs the analysis and writes the notes.mid and song.ini files of a YARG song folder. */
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
        /** R&amp;B mode was asked for and the guitar chart follows the singer. */
        public boolean rnb;
        /** Why R&amp;B mode could not follow the singer on this song, in words for the user; null when it did or was not asked. */
        public String rnbNote;
        public int bassTier = -1;
        /** Lyrics as notes to sing; empty when the song has no lyrics. */
        public VocalCharter.Chart vocals = new VocalCharter.Chart();
        public int vocalTier = -1;
        /** The lyric lines as charted, carrying who sings each (including a guess made from the voices). */
        public List<Lrc.Line> lyricsOut = new ArrayList<>();
        /** True when the app was asked to tell two singers apart by voice. */
        public boolean singersGuessTried;
        /** What it came up with; null when it was not asked or the voices could not be told apart. */
        public VoiceSplitter.Guess singerGuess;
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
        /** The voice on its own: where each word is sung and on what note. Null when not listened to. */
        public VocalAnalyzer.Voice voice;
        /** For a song with two singers whose lines are not marked: work out who sings what from their voices. */
        public boolean guessSingers;
        /** The vocals on their own, measured like an instrument; only needed for R&amp;B mode. */
        public Features vocals;
        /**
         * R&amp;B mode: the voice comes first. The guitar chart follows the lead singer's melody
         * (see {@link MelodyCharter}) and the vocals part follows runs note by note.
         */
        public boolean rnb;
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
            VocalAnalyzer.Voice voice = stems == null ? null : stems.voice;
            r.vocals = VocalCharter.build(lyrics, r.tempo, f.duration, voice, stems != null && stems.rnb);
            r.lyricLines = r.vocals.lines;
            r.lyricsOut = lyrics;
            boolean marked = false;
            for (Lrc.Line l : lyrics) marked |= l.singer != Lrc.UNMARKED;
            if (stems != null && stems.guessSingers && voice != null && !marked) {
                r.singersGuessTried = true;
                r.singerGuess = VoiceSplitter.guess(r.vocals, voice, lyrics.size());
                if (r.singerGuess != null) {
                    r.vocals.assignParts(r.singerGuess.partOfLine);
                    // Written back into the lyrics, so the guess is saved and can be corrected by hand.
                    List<Lrc.Line> out = new ArrayList<>();
                    for (int i = 0; i < lyrics.size(); i++) {
                        Lrc.Line l = lyrics.get(i);
                        boolean second = r.singerGuess.partOfLine[i] == VocalCharter.PART_SECOND;
                        out.add(l.text.isEmpty() ? l : new Lrc.Line(l.time, l.text, second ? Lrc.SECOND : Lrc.FIRST));
                    }
                    r.lyricsOut = out;
                }
            }
            if (r.lyricLines > 0) {
                // Sung songs are harder than rapped ones (the note has to be right), busy ones harder still.
                double sung = (double) r.vocals.sungLines / r.lyricLines;
                double pace = f.duration > 1 ? r.vocals.words / f.duration : 0;
                r.vocalTier = (int) Math.max(0, Math.min(6, Math.round(1 + 3 * sung + pace)));
            }
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
        if (stems.rnb) chartFromVoice(r, stems, options);
        if (stems.bass != null && stems.bassEnergy >= 0.003 * Math.max(1e-12, stems.mixEnergy)) {
            GuitarCharter.Options bassOptions = options == null ? new GuitarCharter.Options() : options.copy();
            bassOptions.chords = false;
            List<GuitarCharter.Onset> onsets = GuitarCharter.detectOnsets(stems.bass, true);
            GuitarCharter.Chart chart = GuitarCharter.build(stems.bass, onsets, r.tempo, r.bpm, bassOptions);
            int notes = chart.notes.get(GuitarCharter.EXPERT).size();
            if (notes >= 16) {
                r.bassChart = chart;
                double nps = r.duration > 1 ? notes / r.duration : 0;
                r.bassTier = (int) Math.max(0, Math.min(6, Math.round(nps * 1.1)));
            }
        }
    }

    /**
     * R&amp;B mode: replaces the guitar chart with one that follows the lead singer. A song with
     * little singing in it (an instrumental, a long intro and one chorus) keeps the chart made
     * from the instruments, and the user is told why.
     */
    private static void chartFromVoice(Result r, Stems stems, GuitarCharter.Options options) {
        if (stems.vocals == null || stems.voice == null) {
            r.rnbNote = "R&B mode needs the song split into instruments, so the guitar chart follows the band instead.";
            return;
        }
        GuitarCharter.Chart chart = MelodyCharter.build(stems.vocals, stems.voice, r.tempo, r.bpm, options);
        int notes = chart.notes.get(GuitarCharter.EXPERT).size();
        // A vocal-led chart is sparser than one from the band, so it is judged by how much of the
        // song is sung, not by how many notes the band chart had
        if (notes >= 24 && MelodyCharter.singingShare(stems.voice) >= MelodyCharter.MIN_SINGING_SHARE) {
            r.chart = chart;
            r.rnb = true;
            r.guitarSource = "the lead vocal (R&B mode)";
        } else {
            r.rnbNote = "R&B mode: there was too little singing to follow, so the guitar chart follows the band instead.";
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

    /** Name of the file that tells the game who the two singers of a song are (one name a line). */
    public static final String PARTS_FILE = "vocal_parts.txt";

    public static void writeFolder(File dir, SongMeta meta, Result r, String audioFileName) throws IOException {
        if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("Could not create " + dir);
        MidiSong.write(new File(dir, "notes.mid"), r);
        // A chart left by an older version of the app would be a second, stale copy of the song.
        File old = new File(dir, "notes.chart");
        if (old.exists() && !old.delete()) throw new IOException("Could not replace " + old);
        try (Writer w = open(new File(dir, "song.ini"))) {
            writeIni(w, meta, r);
        }
        File parts = new File(dir, PARTS_FILE);
        if (r.vocals.twoParts()) {
            try (Writer w = open(parts)) {
                w.write(meta.singerName(0) + "\n" + meta.singerName(1) + "\n");
            }
        } else if (parts.exists()) {
            //noinspection ResultOfMethodCallIgnored
            parts.delete();
        }
    }

    private static Writer open(File f) throws IOException {
        return new BufferedWriter(new OutputStreamWriter(new FileOutputStream(f), StandardCharsets.UTF_8));
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
        w.write("diff_vocals = " + r.vocalTier + "\n");
        if (r.vocals.twoParts()) w.write("diff_vocals_harm = " + r.vocalTier + "\n");
        w.write("delay = 0\n");
        w.write("loading_phrase = Auto-charted from audio by YARG Converter.\n");
    }

    private static String line(String s) {
        return s.replace("\r", " ").replace("\n", " ").trim();
    }
}
