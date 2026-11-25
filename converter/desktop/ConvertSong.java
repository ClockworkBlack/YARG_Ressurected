import com.clockworkblack.yargconverter.core.AudioAnalyzer;
import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.Lrc;
import com.clockworkblack.yargconverter.core.SongMeta;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.StemSplitter;
import com.clockworkblack.yargconverter.core.VocalAnalyzer;
import com.clockworkblack.yargconverter.core.VocalCharter;
import com.clockworkblack.yargconverter.stems.OrtStemModel;

import java.io.BufferedInputStream;
import java.io.DataOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * The phone app's conversion, run on a computer: the same chart code, the same separation model,
 * the same folder. Audio is read with ffmpeg instead of Android's decoder.
 *
 *   java -cp out:onnxruntime.jar ConvertSong --audio song.mp3 --out songs/ --mode split --notes more \
 *       --title "..." --artist "..." [--album --year --genre] [--lyrics lyrics.lrc] \
 *       [--model htdemucs_6s.onnx] [--words-model wav2vec2-base-960h.onnx] [--two-singers] [--vocals-out vocals.wav --wait-for-lyrics lyrics.lrc]
 *
 * --vocals-out writes the separated voice to a WAV file; with --wait-for-lyrics the run then waits
 * until that lyrics file exists (made from the voice, for instance) and uses it.
 */
public final class ConvertSong {
    public static void main(String[] args) throws Exception {
        Map<String, String> a = new HashMap<>();
        for (int i = 0; i < args.length; i++) {
            if (!args[i].startsWith("--")) continue;
            String key = args[i].substring(2);
            boolean flag = i + 1 >= args.length || args[i + 1].startsWith("--");
            a.put(key, flag ? "true" : args[++i]);
        }
        File audio = new File(req(a, "audio"));
        String mode = a.getOrDefault("mode", "normal").toLowerCase(Locale.US);
        boolean useStems = mode.equals("split") || mode.equals("rnb");
        boolean rnb = mode.equals("rnb");
        String notes = a.getOrDefault("notes", "standard").toLowerCase(Locale.US);

        SongMeta meta = new SongMeta();
        meta.title = a.getOrDefault("title", "");
        meta.artist = a.getOrDefault("artist", "");
        meta.album = a.getOrDefault("album", "");
        meta.year = a.getOrDefault("year", "");
        meta.genre = a.getOrDefault("genre", "");
        meta.singers = Lrc.artists(meta.artist, meta.title).toArray(new String[0]);

        GuitarCharter.Options options = new GuitarCharter.Options();
        options.threshold = notes.equals("fewer") ? 1.5 : notes.equals("more") ? 0.65 : 1.0;

        File dir = new File(new File(req(a, "out")), meta.folderName());
        if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("Could not create " + dir);

        String name = audio.getName();
        String ext = name.substring(name.lastIndexOf('.') + 1).toLowerCase(Locale.US);
        boolean copyAsIs = ext.equals("mp3") || ext.equals("wav") || ext.equals("ogg") || ext.equals("opus");
        String audioFile = copyAsIs ? "song." + ext : "song.wav";

        // The mix, as the app measures it: all channels averaged, at the file's own rate
        int rate = sampleRate(audio);
        log("Measuring the mix (" + rate + " Hz)…");
        AudioAnalyzer mix = new AudioAnalyzer(rate);
        try (InputStream in = ffmpeg(audio, "-ac", "1", "-ar", String.valueOf(rate), "-f", "f32le")) {
            readFloats(in, 1, (buf, n) -> mix.feed(buf[0], 0, n));
        }
        Features features = mix.finish();
        log(String.format(Locale.US, "  %.1f s long", features.duration));

        SongWriter.Stems stems = null;
        long separationMs = 0;
        if (useStems) {
            long t0 = System.currentTimeMillis();
            stems = separate(audio, req(a, "model"), rnb, a.get("vocals-out"), a.get("words-model"));
            separationMs = System.currentTimeMillis() - t0;
            log(String.format(Locale.US, "  split into instruments in %d min %02d s", separationMs / 60000, (separationMs / 1000) % 60));
            stems.guessSingers = a.containsKey("two-singers");
            stems.rnb = rnb;
            boolean two = meta.singers.length >= 2;
            stems.singerNames = two ? meta.singers : null;
        }

        String lyricsPath = a.get("lyrics");
        if (a.containsKey("wait-for-lyrics")) {
            File wait = new File(a.get("wait-for-lyrics"));
            log("Waiting for " + wait + "…");
            while (!wait.exists()) Thread.sleep(2000);
            Thread.sleep(1000);
            lyricsPath = wait.getPath();
        }
        String lrcText = lyricsPath == null ? null : Lrc.decode(Files.readAllBytes(new File(lyricsPath).toPath()));
        List<Lrc.Line> lyrics = lrcText == null ? new ArrayList<>() : Lrc.parse(lrcText);

        log("Finding the beat and writing the charts…");
        SongWriter.Result r = SongWriter.analyze(features, lyrics, options, stems, Lrc.keepsTiming(lrcText));
        if (r.singerGuess != null || r.lyricsMoved) lrcText = Lrc.format(r.lyricsOut);
        SongWriter.writeFolder(dir, meta, r, audioFile);
        // Read back by the game's rules (core-test/MidiCheck): every note and word must come through
        List<String> problems = MidiCheck.verify(Files.readAllBytes(new File(dir, "notes.mid").toPath()), r);
        for (String problem : problems) log("notes.mid problem: " + problem);
        if (!problems.isEmpty()) throw new IOException(problems.size() + " problem(s) reading notes.mid back");
        log("notes.mid read back as the game reads it: OK");

        for (String old : new String[] {"song.mp3", "song.wav", "song.ogg", "song.opus"}) new File(dir, old).delete();
        if (copyAsIs) {
            Files.copy(audio.toPath(), new File(dir, audioFile).toPath(), StandardCopyOption.REPLACE_EXISTING);
        } else {
            run("ffmpeg", "-v", "error", "-y", "-i", audio.getPath(), "-c:a", "pcm_s16le", new File(dir, audioFile).getPath());
        }
        File lrcFile = new File(dir, "lyrics.lrc");
        if (r.lyricLines > 0) Files.write(lrcFile.toPath(), lrcText.getBytes(StandardCharsets.UTF_8));
        else lrcFile.delete();
        albumArt(audio, dir);

        StringBuilder sb = new StringBuilder();
        sb.append("Done: ").append(dir).append('\n');
        sb.append(String.format(Locale.US, "Tempo: about %.0f BPM, %d beats to the measure\n", r.bpm, r.beatsPerMeasure));
        sb.append("Guitar chart made from ").append(r.guitarSource).append('\n');
        if (r.rnbNote != null) sb.append(r.rnbNote).append('\n');
        sb.append(String.format(Locale.US, "Guitar notes: Easy %d, Medium %d, Hard %d, Expert %d\n",
                r.noteCount(GuitarCharter.EASY), r.noteCount(GuitarCharter.MEDIUM), r.noteCount(GuitarCharter.HARD), r.noteCount(GuitarCharter.EXPERT)));
        if (r.bassChart != null) {
            sb.append(String.format(Locale.US, "Bass notes: Easy %d, Medium %d, Hard %d, Expert %d\n",
                    r.bassChart.notes.get(GuitarCharter.EASY).size(), r.bassChart.notes.get(GuitarCharter.MEDIUM).size(),
                    r.bassChart.notes.get(GuitarCharter.HARD).size(), r.bassChart.notes.get(GuitarCharter.EXPERT).size()));
        } else if (stems != null) {
            sb.append("No bass chart: the song has little or no separate bass.\n");
        }
        if (r.lyricLines > 0) {
            VocalCharter.Chart v = r.vocals;
            sb.append("Vocals: ").append(v.lines).append(" lines, ").append(v.sungLines).append(" sung, ")
                    .append(v.spokenLines).append(" rapped or spoken").append(v.heard ? "" : " (voice not heard: spread at speaking pace)").append('\n');
            if (r.lyricTimingNote != null) sb.append(r.lyricTimingNote).append('\n');
            if (r.repeatNotes != null) for (String note : r.repeatNotes) sb.append("  ").append(note).append('\n');
            if (v.twoParts()) {
                sb.append(String.format(Locale.US, "Singers: %s %d lines, %s %d lines, both %d%s\n", meta.singerName(0), v.partLines[VocalCharter.PART_FIRST],
                        meta.singerName(1), v.partLines[VocalCharter.PART_SECOND], v.partLines[VocalCharter.PART_BOTH],
                        r.singerGuess != null ? " (guessed from their voices)" : ""));
            }
        } else {
            sb.append("Vocals: none (no timed lyrics)\n");
        }
        System.out.print(sb);
    }

    private static SongWriter.Stems separate(File audio, String modelPath, boolean rnb, String vocalsOut, String wordsModel) throws Exception {
        // The voice at 16 kHz, kept for the speech model that times the lyrics word by word
        final com.clockworkblack.yargconverter.core.Resampler to16k = wordsModel == null ? null
                : new com.clockworkblack.yargconverter.core.Resampler(StemSplitter.SAMPLE_RATE, com.clockworkblack.yargconverter.core.WordAligner.SAMPLE_RATE);
        final float[][] voice16k = {new float[1 << 20]};
        final int[] voice16kLength = {0};
        final float[] rsL = to16k == null ? null : new float[to16k.capacityFor(StemSplitter.SEGMENT)];
        final float[] rsR = to16k == null ? null : new float[to16k.capacityFor(StemSplitter.SEGMENT)];
        final AudioAnalyzer guitar = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer vocals = rnb ? new AudioAnalyzer(StemSplitter.SAMPLE_RATE) : null;
        final AudioAnalyzer melodic = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer bass = new AudioAnalyzer(StemSplitter.SAMPLE_RATE, true);
        final VocalAnalyzer voice = new VocalAnalyzer(StemSplitter.SAMPLE_RATE);
        final float[] vox = new float[StemSplitter.SEGMENT];
        final float[] g = new float[StemSplitter.SEGMENT], m = new float[StemSplitter.SEGMENT], b = new float[StemSplitter.SEGMENT];
        final double[] energy = new double[4];
        final WavOut wav = vocalsOut == null ? null : new WavOut(new File(vocalsOut), StemSplitter.SAMPLE_RATE);

        // Decoded once to 44.1 kHz stereo, 16 bit, as the app parks it for the model
        File pcm = File.createTempFile("mix44k", ".pcm");
        pcm.deleteOnExit();
        run("ffmpeg", "-v", "error", "-y", "-i", audio.getPath(), "-ac", "2", "-ar", "44100", "-f", "s16le", pcm.getPath());
        final long frames = pcm.length() / 4;

        log("Splitting into instruments…");
        final long started = System.currentTimeMillis();
        try (OrtStemModel model = new OrtStemModel(modelPath, Runtime.getRuntime().availableProcessors());
             InputStream in = new BufferedInputStream(Files.newInputStream(pcm.toPath()), 1 << 16)) {
            final byte[] bytes = new byte[1 << 16];
            StemSplitter.Source source = (left, right, offset, length) -> {
                int want = Math.min(length, bytes.length / 4) * 4, got = 0;
                while (got < want) {
                    int n = in.read(bytes, got, want - got);
                    if (n <= 0) break;
                    got += n;
                }
                int count = got / 4;
                for (int i = 0; i < count; i++) {
                    int at = i * 4;
                    left[offset + i] = (short) ((bytes[at] & 0xFF) | (bytes[at + 1] << 8)) / 32768f;
                    right[offset + i] = (short) ((bytes[at + 2] & 0xFF) | (bytes[at + 3] << 8)) / 32768f;
                }
                return count;
            };
            StemSplitter.Sink sink = (mixLeft, mixRight, st, count) -> {
                float[][] gt = st[StemSplitter.GUITAR], ot = st[StemSplitter.OTHER], pn = st[StemSplitter.PIANO];
                float[][] bs = st[StemSplitter.BASS], vc = st[StemSplitter.VOCALS];
                for (int i = 0; i < count; i++) {
                    vox[i] = (vc[0][i] + vc[1][i]) * 0.5f;
                    float gv = (gt[0][i] + gt[1][i]) * 0.5f;
                    float mv = gv + (ot[0][i] + ot[1][i] + pn[0][i] + pn[1][i]) * 0.5f;
                    float bv = (bs[0][i] + bs[1][i]) * 0.5f;
                    float xv = (mixLeft[i] + mixRight[i]) * 0.5f;
                    g[i] = gv;
                    m[i] = mv;
                    b[i] = bv;
                    energy[0] += gv * gv;
                    energy[1] += mv * mv;
                    energy[2] += bv * bv;
                    energy[3] += xv * xv;
                }
                guitar.feed(g, 0, count);
                melodic.feed(m, 0, count);
                bass.feed(b, 0, count);
                voice.feed(vox, 0, count);
                if (vocals != null) vocals.feed(vox, 0, count);
                if (to16k != null) {
                    int made = to16k.process(vox, vox, count, rsL, rsR);
                    if (voice16kLength[0] + made > voice16k[0].length) {
                        voice16k[0] = java.util.Arrays.copyOf(voice16k[0], Math.max(voice16k[0].length * 2, voice16kLength[0] + made));
                    }
                    System.arraycopy(rsL, 0, voice16k[0], voice16kLength[0], made);
                    voice16kLength[0] += made;
                }
                if (wav != null) {
                    try {
                        wav.write(vox, count);
                    } catch (IOException e) {
                        throw new IllegalStateException(e);
                    }
                }
            };
            StemSplitter.Progress report = (done, total) -> {
                long el = System.currentTimeMillis() - started;
                log(String.format(Locale.US, "  part %d of %d (%d s so far)", done, total, el / 1000));
                return true;
            };
            StemSplitter.split(source, frames, model, sink, report);
        } finally {
            if (wav != null) wav.close();
            pcm.delete();
        }
        SongWriter.Stems out = new SongWriter.Stems();
        out.guitar = guitar.finish();
        out.melodic = melodic.finish();
        out.bass = bass.finish();
        out.voice = voice.finish();
        if (vocals != null) out.vocals = vocals.finish();
        out.guitarEnergy = energy[0];
        out.melodicEnergy = energy[1];
        out.bassEnergy = energy[2];
        out.mixEnergy = energy[3];
        if (wordsModel != null) {
            log("Hearing the words in the voice…");
            try (com.clockworkblack.yargconverter.stems.OrtWordModel words =
                         new com.clockworkblack.yargconverter.stems.OrtWordModel(wordsModel, Runtime.getRuntime().availableProcessors())) {
                out.letters = words.letters(voice16k[0], voice16kLength[0], null);
            }
        }
        if (vocalsOut != null) log("Separated voice written to " + vocalsOut);
        return out;
    }

    /** Mono 16-bit WAV, the header filled in on close. */
    private static final class WavOut {
        private final File file;
        private final DataOutputStream out;
        private long samples;

        WavOut(File file, int rate) throws IOException {
            this.file = file;
            out = new DataOutputStream(new java.io.BufferedOutputStream(new FileOutputStream(file), 1 << 16));
            byte[] h = header(rate, 0);
            out.write(h);
        }

        void write(float[] x, int n) throws IOException {
            byte[] buf = new byte[n * 2];
            for (int i = 0; i < n; i++) {
                int v = Math.max(-32768, Math.min(32767, Math.round(x[i] * 32767f)));
                buf[2 * i] = (byte) v;
                buf[2 * i + 1] = (byte) (v >> 8);
            }
            out.write(buf);
            samples += n;
        }

        void close() throws IOException {
            out.close();
            try (java.io.RandomAccessFile f = new java.io.RandomAccessFile(file, "rw")) {
                f.write(header(StemSplitter.SAMPLE_RATE, samples));
            }
        }

        private static byte[] header(int rate, long samples) {
            java.nio.ByteBuffer h = java.nio.ByteBuffer.allocate(44).order(java.nio.ByteOrder.LITTLE_ENDIAN);
            long data = samples * 2;
            h.put("RIFF".getBytes(StandardCharsets.US_ASCII)).putInt((int) (36 + data)).put("WAVE".getBytes(StandardCharsets.US_ASCII));
            h.put("fmt ".getBytes(StandardCharsets.US_ASCII)).putInt(16).putShort((short) 1).putShort((short) 1)
                    .putInt(rate).putInt(rate * 2).putShort((short) 2).putShort((short) 16);
            h.put("data".getBytes(StandardCharsets.US_ASCII)).putInt((int) data);
            return h.array();
        }
    }

    private interface FloatSink {
        void accept(float[][] buf, int n);
    }

    private static void readFloats(InputStream in, int channels, FloatSink sink) throws IOException {
        byte[] bytes = new byte[4 * 8192 * channels];
        float[][] buf = new float[channels][8192];
        java.nio.ByteBuffer bb = java.nio.ByteBuffer.wrap(bytes).order(java.nio.ByteOrder.LITTLE_ENDIAN);
        int have = 0;
        while (true) {
            int n = in.read(bytes, have, bytes.length - have);
            if (n <= 0) break;
            have += n;
            int frames = have / (4 * channels);
            for (int i = 0; i < frames; i++) for (int c = 0; c < channels; c++) buf[c][i] = bb.getFloat((i * channels + c) * 4);
            sink.accept(buf, frames);
            int used = frames * 4 * channels;
            System.arraycopy(bytes, used, bytes, 0, have - used);
            have -= used;
        }
    }

    private static InputStream ffmpeg(File audio, String... format) throws IOException {
        List<String> cmd = new ArrayList<>(List.of("ffmpeg", "-v", "error", "-i", audio.getPath()));
        cmd.addAll(List.of(format));
        cmd.add("-");
        Process p = new ProcessBuilder(cmd).redirectError(ProcessBuilder.Redirect.INHERIT).start();
        return p.getInputStream();
    }

    private static int sampleRate(File audio) throws Exception {
        Process p = new ProcessBuilder("ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=sample_rate",
                "-of", "default=nw=1:nk=1", audio.getPath()).start();
        String s = new String(p.getInputStream().readAllBytes(), StandardCharsets.US_ASCII).trim();
        p.waitFor();
        return Integer.parseInt(s);
    }

    /** The cover picture inside the file, if it has one. */
    private static void albumArt(File audio, File dir) {
        try {
            File png = new File(dir, "album.png");
            Process p = new ProcessBuilder("ffmpeg", "-v", "error", "-y", "-i", audio.getPath(), "-an", "-frames:v", "1", png.getPath())
                    .redirectErrorStream(true).start();
            p.getInputStream().readAllBytes();
            if (p.waitFor() != 0 || png.length() < 100) png.delete();
        } catch (Exception ignored) {
        }
    }

    private static void run(String... cmd) throws Exception {
        Process p = new ProcessBuilder(cmd).inheritIO().start();
        if (p.waitFor() != 0) throw new IOException("Failed: " + String.join(" ", cmd));
    }

    private static String req(Map<String, String> a, String key) {
        String v = a.get(key);
        if (v == null) throw new IllegalArgumentException("--" + key + " is needed");
        return v;
    }

    private static void log(String s) {
        System.err.println(s);
    }
}
