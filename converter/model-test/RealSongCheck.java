import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.Lrc;
import com.clockworkblack.yargconverter.core.MidiSong;
import com.clockworkblack.yargconverter.core.SongMeta;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.StemSplitter;
import com.clockworkblack.yargconverter.core.VocalAnalyzer;
import com.clockworkblack.yargconverter.core.VocalCharter;
import com.clockworkblack.yargconverter.stems.OrtStemModel;

import java.io.BufferedInputStream;
import java.io.DataInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Converts one real recording the way the app does (separate, then chart) and prints what came
 * out, so the result on real music can be read in the build log.
 *
 *   java -cp out:onnxruntime.jar RealSongCheck model.onnx song.f32 outDir "Title" "Artist"
 *
 * song.f32 is raw stereo 32-bit float at 44.1 kHz (ffmpeg -f f32le -ac 2 -ar 44100).
 */
public final class RealSongCheck {
    public static void main(String[] args) throws Exception {
        File raw = new File(args[1]);
        final int total = (int) (raw.length() / 8);
        final float[] left = new float[total], right = new float[total];
        try (DataInputStream in = new DataInputStream(new BufferedInputStream(new FileInputStream(raw), 1 << 20))) {
            byte[] bytes = new byte[8 * 8192];
            int at = 0;
            while (at < total) {
                int frames = Math.min(8192, total - at);
                in.readFully(bytes, 0, frames * 8);
                ByteBuffer bb = ByteBuffer.wrap(bytes, 0, frames * 8).order(ByteOrder.LITTLE_ENDIAN);
                for (int i = 0; i < frames; i++) {
                    left[at + i] = bb.getFloat();
                    right[at + i] = bb.getFloat();
                }
                at += frames;
            }
        }
        double seconds = (double) total / StemSplitter.SAMPLE_RATE;
        System.out.printf(Locale.US, "Song: %s by %s, %.0f s%n", args[3], args[4], seconds);

        final int stemCount = StemSplitter.STEMS.length;
        final float[][] mono = new float[stemCount][total];
        final float[] mix = new float[total];
        for (int i = 0; i < total; i++) mix[i] = (left[i] + right[i]) * 0.5f;
        final int[] wrote = {0};
        final int[] readAt = {0};
        long t0 = System.currentTimeMillis();
        OrtStemModel model = new OrtStemModel(args[0], 0);
        StemSplitter.split(new StemSplitter.Source() {
            @Override
            public int read(float[] l, float[] r, int offset, int length) {
                int n = Math.min(length, total - readAt[0]);
                if (n <= 0) return 0;
                System.arraycopy(left, readAt[0], l, offset, n);
                System.arraycopy(right, readAt[0], r, offset, n);
                readAt[0] += n;
                return n;
            }
        }, total, model, new StemSplitter.Sink() {
            @Override
            public void accept(float[] l, float[] r, float[][][] stems, int count) {
                for (int s = 0; s < stemCount; s++) {
                    for (int i = 0; i < count; i++) mono[s][wrote[0] + i] = (stems[s][0][i] + stems[s][1][i]) * 0.5f;
                }
                wrote[0] += count;
            }
        }, null);
        model.close();
        double took = (System.currentTimeMillis() - t0) / 1000.0;
        System.out.printf(Locale.US, "Separated in %.0f s (%.2f x the song's length) on %d processor(s)%n",
                took, took / seconds, Runtime.getRuntime().availableProcessors());

        double[] energy = new double[stemCount];
        double all = 0, err = 0, ref = 0;
        for (int i = 0; i < total; i++) {
            double sum = 0;
            for (int s = 0; s < stemCount; s++) {
                energy[s] += mono[s][i] * mono[s][i];
                sum += mono[s][i];
            }
            err += (sum - mix[i]) * (sum - mix[i]);
            ref += mix[i] * mix[i];
        }
        for (double e : energy) all += e;
        StringBuilder shares = new StringBuilder("How loud each separated instrument is, as a share of the total:");
        for (int s = 0; s < stemCount; s++) shares.append(String.format(Locale.US, " %s %.0f%%", StemSplitter.STEMS[s], 100 * energy[s] / all));
        System.out.println(shares);
        System.out.printf(Locale.US, "All stems added together differ from the mix by %.1f dB%n", 10 * Math.log10(Math.max(1e-20, err / ref)));

        float[] melodic = new float[total];
        for (int i = 0; i < total; i++) melodic[i] = mono[StemSplitter.GUITAR][i] + mono[StemSplitter.OTHER][i] + mono[StemSplitter.PIANO][i];
        SongWriter.Stems stems = new SongWriter.Stems();
        stems.guitar = StemTest.analyze(mono[StemSplitter.GUITAR], StemSplitter.SAMPLE_RATE);
        stems.melodic = StemTest.analyze(melodic, StemSplitter.SAMPLE_RATE);
        stems.bass = StemTest.analyze(mono[StemSplitter.BASS], StemSplitter.SAMPLE_RATE, true);
        stems.guitarEnergy = energy[StemSplitter.GUITAR];
        stems.melodicEnergy = StemTest.energy(melodic);
        stems.bassEnergy = energy[StemSplitter.BASS];
        stems.mixEnergy = ref;
        long t1 = System.currentTimeMillis();
        Features mixFeatures = StemTest.analyze(mix, StemSplitter.SAMPLE_RATE);
        SongWriter.Result plain = SongWriter.analyze(mixFeatures, null, null);

        // The voice, as the app hears it. With a lyrics file as a sixth argument the words are
        // placed and pitched from it; without one, a made-up line every eight seconds shows what
        // the vocals stem holds (in an instrumental: next to nothing, so every line is a talkie).
        long tv = System.currentTimeMillis();
        stems.voice = VocalTest.listen(mono[StemSplitter.VOCALS], StemSplitter.SAMPLE_RATE);
        VocalAnalyzer.Voice voice = stems.voice;
        int sounding = 0, onNote = 0;
        float gate = voice.gate();
        for (int i = 0; i < voice.frames(); i++) {
            if (voice.level[i] <= gate) continue;
            sounding++;
            if (!Float.isNaN(voice.midi[i])) onNote++;
        }
        System.out.printf(Locale.US, "Voice: listened to in %.1f s; sounding %d%% of the song, on a clear note %d%% of that time%n",
                (System.currentTimeMillis() - tv) / 1000.0, 100 * sounding / Math.max(1, voice.frames()), 100 * onNote / Math.max(1, sounding));
        java.util.List<Lrc.Line> lyrics;
        if (args.length > 5) {
            lyrics = Lrc.parse(new String(java.nio.file.Files.readAllBytes(new File(args[5]).toPath()), "UTF-8"));
        } else {
            StringBuilder made = new StringBuilder();
            for (double t = 4; t < seconds - 6; t += 8) made.append(Lrc.stamp(t)).append("made up words to sing here\n");
            lyrics = Lrc.parse(made.toString());
        }
        stems.guessSingers = args.length > 5;
        SongWriter.Result split = SongWriter.analyze(mixFeatures, lyrics, null, stems);
        VocalCharter.Chart v = split.vocals;
        System.out.printf(Locale.US, "Vocals: %d lines (%d sung, %d rapped or spoken), %d words, %d phrases, voice %s, tier %d%s%n",
                v.lines, v.sungLines, v.spokenLines, v.words, v.phrases.size(), v.heard ? "heard" : "not heard at the lyric times", split.vocalTier,
                split.singerGuess != null ? String.format(Locale.US, ", two singers guessed %d/%d lines", split.singerGuess.firstLines, split.singerGuess.secondLines)
                        : split.singersGuessTried ? String.format(Locale.US, ", singers not told apart (%.1f)", com.clockworkblack.yargconverter.core.VoiceSplitter.lastSeparation) : "");
        // Every line, with what the sung-or-rapped decision was made on
        int lastLine = -1;
        for (VocalCharter.Phrase p : v.phrases) {
            if (p.line == lastLine) continue;
            lastLine = p.line;
            System.out.printf(Locale.US, "  LINE %2d %6.2f s %s %s | %s%n", p.line, p.lineStart, p.spoken ? "SPOKEN" : "sung  ",
                    VocalCharter.measure(voice, p.lineStart, p.lineEnd), lyrics.get(p.line).text);
        }
        int shown = 0;
        for (VocalCharter.Phrase p : v.phrases) {
            if (shown++ >= 12) break;
            StringBuilder line = new StringBuilder(String.format(Locale.US, "  %6.2f s %s:", split.tempo.tickToTime(p.tick), p.spoken ? "spoken" : "sung  "));
            for (VocalCharter.Note n : p.notes) line.append(' ').append(n.slide ? "+" : n.text).append(p.spoken ? "" : "(" + n.pitch + ")");
            System.out.println(line);
        }
        for (String problem : MidiCheck.verify(MidiSong.build(split), split)) {
            System.out.println("FAILED: notes.mid: " + problem);
            System.exit(1);
        }
        System.out.printf(Locale.US, "Listening and charting took %.1f s%n", (System.currentTimeMillis() - t1) / 1000.0);

        System.out.printf(Locale.US, "Tempo %.1f BPM, %d beats to the measure, %d tempo marker(s)%n", split.bpm, split.beatsPerMeasure, split.tempo.tick.length);
        describe("Guitar from the full mix", plain.chart, seconds);
        describe("Guitar from " + split.guitarSource, split.chart, seconds);
        if (split.bassChart != null) describe("Bass from the separated bass", split.bassChart, seconds);
        else System.out.println("No bass chart.");

        Set<Long> mixTicks = new HashSet<>();
        for (GuitarCharter.Note n : plain.chart.notes.get(GuitarCharter.EXPERT)) mixTicks.add(n.tick);
        int shared = 0;
        List<GuitarCharter.Note> stemNotes = split.chart.notes.get(GuitarCharter.EXPERT);
        for (GuitarCharter.Note n : stemNotes) if (mixTicks.contains(n.tick)) shared++;
        System.out.printf(Locale.US, "%d%% of the stem-based Expert guitar notes are also in the full-mix chart; the full-mix chart has %d notes the stem chart leaves out%n",
                stemNotes.isEmpty() ? 0 : 100 * shared / stemNotes.size(), mixTicks.size() - shared);

        int measures = split.tempo.measureOf(split.tempo.timeToTick(seconds));
        int[] show = {measures / 5, measures / 2, measures * 4 / 5};
        for (int m : show) {
            System.out.println("Measures " + m + "-" + (m + 1) + " (each column is a sixteenth note; G R Y B O are the frets, ~ is a held note)");
            tab("  mix guitar ", plain.chart.notes.get(GuitarCharter.EXPERT), split, m);
            tab("  stem guitar", split.chart.notes.get(GuitarCharter.EXPERT), split, m);
            tab("  stem guitar (Easy)", split.chart.notes.get(GuitarCharter.EASY), split, m);
            if (split.bassChart != null) tab("  stem bass  ", split.bassChart.notes.get(GuitarCharter.EXPERT), split, m);
        }

        SongMeta meta = new SongMeta();
        meta.title = args[3];
        meta.artist = args[4];
        meta.charter = "YARG Converter (auto, CI sample)";
        SongWriter.writeFolder(new File(args[2]), meta, split, "song.ogg");
        if (split.noteCount(GuitarCharter.EXPERT) == 0) {
            System.out.println("FAILED: no notes charted");
            System.exit(1);
        }
        System.out.println("Real-song conversion finished");
    }

    private static void describe(String name, GuitarCharter.Chart chart, double seconds) {
        int chords = 0, sustains = 0;
        int[] lanes = new int[5];
        List<GuitarCharter.Note> expert = chart.notes.get(GuitarCharter.EXPERT);
        for (GuitarCharter.Note n : expert) {
            if (n.lane2 >= 0) chords++;
            if (n.sustain > 0) sustains++;
            lanes[n.lane]++;
        }
        System.out.printf(Locale.US, "%s: notes E/M/H/X %d/%d/%d/%d (%.1f per second on Expert), %d chords, %d held notes, frets G/R/Y/B/O %d/%d/%d/%d/%d, %d distinct sounds, %d repeated measures%n",
                name, chart.notes.get(0).size(), chart.notes.get(1).size(), chart.notes.get(2).size(), expert.size(), expert.size() / seconds,
                chords, sustains, lanes[0], lanes[1], lanes[2], lanes[3], lanes[4], chart.soundGroups, chart.repeatedMeasures);
    }

    private static void tab(String label, List<GuitarCharter.Note> notes, SongWriter.Result r, int measure) {
        long start = r.tempo.measureStart(measure);
        int slots = (int) (r.tempo.measureTicks() / 48) * 2;
        String frets = "GRYBO";
        StringBuilder sb = new StringBuilder(String.format("%-22s", label));
        for (int s = 0; s < slots; s++) {
            long tick = start + s * 48L;
            String cell = ". ";
            for (GuitarCharter.Note n : notes) {
                if (n.tick != tick) continue;
                cell = "" + frets.charAt(n.lane) + (n.lane2 >= 0 ? "" + frets.charAt(n.lane2) : n.sustain > 0 ? "~" : " ");
            }
            sb.append(cell);
            if (s % 4 == 3) sb.append(' ');
        }
        System.out.println(sb);
    }
}
