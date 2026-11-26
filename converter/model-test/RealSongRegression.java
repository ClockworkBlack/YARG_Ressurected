import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.SongMeta;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.StemSplitter;
import com.clockworkblack.yargconverter.stems.OrtStemModel;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.BufferedReader;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.FileReader;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;

/**
 * Converts one real recording the way the app does and checks the result against what that
 * song is known to produce (converter/real-songs/expected.tsv). The recordings themselves are
 * not in the repository; converter/real-songs/check.sh runs this for every audio file in a
 * folder you give it.
 *
 *   java -cp out:onnxruntime.jar RealSongRegression model.onnx expected.tsv SHA256 song.f32 cacheDir [outDir]
 *
 * song.f32 is raw stereo 32-bit float at 44.1 kHz. The separated instruments are kept in
 * cacheDir (as SHA256.stems) so a second run does not separate again.
 *
 * What is measured is independent of the charting code where it can be: note starts in each
 * separated instrument are found here by plain "did it suddenly get louder" tests, and a start
 * is covered when the chart has a note within 70 ms of it (on time: within 35 ms).
 */
public final class RealSongRegression {
    private static final int SR = StemSplitter.SAMPLE_RATE;

    public static void main(String[] args) throws Exception {
        String modelPath = args[0], sha = args[2];
        File expected = new File(args[1]), raw = new File(args[3]), cacheDir = new File(args[4]);
        File outDir = args.length > 5 ? new File(args[5]) : null;

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
        double seconds = (double) total / SR;
        float[] mix = new float[total];
        for (int i = 0; i < total; i++) mix[i] = (left[i] + right[i]) * 0.5f;

        float[][] mono = stems(modelPath, new File(cacheDir, sha + ".stems"), left, right, total);

        float[] melodic = new float[total];
        for (int i = 0; i < total; i++) melodic[i] = mono[StemSplitter.GUITAR][i] + mono[StemSplitter.OTHER][i] + mono[StemSplitter.PIANO][i];
        SongWriter.Stems stems = new SongWriter.Stems();
        stems.guitar = StemTest.analyze(mono[StemSplitter.GUITAR], SR);
        stems.melodic = StemTest.analyze(melodic, SR);
        stems.bass = StemTest.analyze(mono[StemSplitter.BASS], SR, true);
        stems.guitarEnergy = StemTest.energy(mono[StemSplitter.GUITAR]);
        stems.melodicEnergy = StemTest.energy(melodic);
        stems.bassEnergy = StemTest.energy(mono[StemSplitter.BASS]);
        stems.mixEnergy = StemTest.energy(mix);
        StemTest.addVocals(stems, mono[StemSplitter.VOCALS], mono[StemSplitter.DRUMS], SR);
        Features mixFeatures = StemTest.analyze(mix, SR);
        SongWriter.Result r = SongWriter.analyze(mixFeatures, null, null, stems);

        // What came out.
        String from = r.guitarSource.contains("vocals") ? "vocals" : r.guitarSource.equals("the separated guitar") ? "guitar"
                : r.guitarSource.contains("keys") ? "keys" : "mix";
        double[] instrumentStarts = starts(melodic), vocalStarts = starts(mono[StemSplitter.VOCALS]);
        double[] bassStarts = bassHits(mono[StemSplitter.BASS]), drumStarts = starts(mono[StemSplitter.DRUMS]);
        double[] guitarStarts = starts(mono[StemSplitter.GUITAR]);
        int guitarVocals = percent(r.chart, r, vocalStarts);
        int guitarGuitar = percent(r.chart, r, guitarStarts);
        int deepInstruments = r.deepChart == null ? 0 : percent(r.deepChart, r, instrumentStarts);
        int deepVocals = r.deepChart == null ? 0 : percent(r.deepChart, r, vocalStarts);
        int bass = r.bassChart == null ? 0 : percent(r.bassChart, r, bassStarts);
        int onTime = r.bassChart == null ? 0 : onTime(r.bassChart, r, bassStarts);
        int deepDrums = r.deepBassChart == null ? 0 : percent(r.deepBassChart, r, drumStarts);

        double all = 0;
        double[] energy = new double[mono.length];
        for (int s = 0; s < mono.length; s++) {
            energy[s] = StemTest.energy(mono[s]);
            all += energy[s];
        }
        StringBuilder shares = new StringBuilder();
        for (int s = 0; s < mono.length; s++) shares.append(String.format(Locale.US, " %s %.0f%%", StemSplitter.STEMS[s], 100 * energy[s] / all));
        System.out.printf(Locale.US, "  %.0f s; share of the sound:%s%n", seconds, shares);
        System.out.printf(Locale.US, "  tempo %.1f BPM, %d beats to the measure, %d tempo markers; guitar chart from %s%n",
                r.bpm, r.beatsPerMeasure, r.tempo.tick.length, r.guitarSource);
        System.out.printf(Locale.US, "  Expert notes: guitar %d, bass %d, Deep Music Mode guitar %d, Deep Music Mode bass %d%n",
                r.chart.notes.get(GuitarCharter.EXPERT).size(), r.bassChart == null ? 0 : r.bassChart.notes.get(GuitarCharter.EXPERT).size(),
                r.deepChart == null ? 0 : r.deepChart.notes.get(GuitarCharter.EXPERT).size(),
                r.deepBassChart == null ? 0 : r.deepBassChart.notes.get(GuitarCharter.EXPERT).size());
        System.out.printf(Locale.US, "  covered: guitar has %d%% of %d vocal starts and %d%% of %d guitar starts; Deep Music Mode guitar has %d%% of %d instrument starts and %d%% of the vocal starts;%n"
                        + "           bass has %d%% of %d bass hits, %d%% of those within 35 ms of the hit; Deep Music Mode bass has %d%% of %d drum hits%n",
                guitarVocals, vocalStarts.length, guitarGuitar, guitarStarts.length, deepInstruments, instrumentStarts.length, deepVocals,
                bass, bassStarts.length, onTime, deepDrums, drumStarts.length);
        // A line in the format of expected.tsv, with floors 5 points under what was measured.
        String lead = from.equals("vocals") ? String.valueOf(Math.max(0, guitarVocals - 5)) : String.valueOf(Math.max(0, guitarGuitar - 5));
        System.out.printf(Locale.US, "  row: %s\t%.1f\t%s\t%s\t%d\t%d\t%d\t%d%n", sha, r.bpm, from, lead,
                Math.max(0, deepInstruments - 5), Math.max(0, bass - 5), Math.max(0, onTime - 5), Math.max(0, deepDrums - 5));

        if (outDir != null) {
            SongMeta meta = new SongMeta();
            meta.title = outDir.getName();
            meta.charter = "YARG Converter (real-song check)";
            SongWriter.writeFolder(outDir, meta, r, "song.ogg");
        }

        String[] want = find(expected, sha);
        if (want == null) {
            System.out.println("  NOT IN expected.tsv: nothing to compare with (add the row above to make it part of the check)");
            return;
        }
        int failures = 0;
        double bpm = Double.parseDouble(want[1]);
        failures += check(Math.abs(r.bpm / bpm - 1) <= 0.02, String.format(Locale.US, "tempo %.1f BPM (expected %.1f)", r.bpm, bpm));
        failures += check(from.equals(want[2]), "guitar chart from " + from + " (expected " + want[2] + ")");
        failures += check((from.equals("vocals") ? guitarVocals : guitarGuitar) >= Integer.parseInt(want[3]),
                "guitar covers what it follows: " + (from.equals("vocals") ? guitarVocals : guitarGuitar) + "% (at least " + want[3] + "%)");
        failures += check(deepInstruments >= Integer.parseInt(want[4]), "Deep Music Mode guitar covers the instruments: " + deepInstruments + "% (at least " + want[4] + "%)");
        failures += check(bass >= Integer.parseInt(want[5]), "bass covers the bass hits: " + bass + "% (at least " + want[5] + "%)");
        failures += check(onTime >= Integer.parseInt(want[6]), "bass notes within 35 ms of their hit: " + onTime + "% (at least " + want[6] + "%)");
        failures += check(deepDrums >= Integer.parseInt(want[7]), "Deep Music Mode bass covers the drums: " + deepDrums + "% (at least " + want[7] + "%)");
        System.out.println(failures == 0 ? "  PASSED" : "  FAILED: " + failures + " check(s)");
        if (failures > 0) System.exit(1);
    }

    private static int check(boolean ok, String what) {
        System.out.println((ok ? "  ok    " : "  FAIL  ") + what);
        return ok ? 0 : 1;
    }

    /** The row of expected.tsv for this recording (columns split on tabs), or null. */
    private static String[] find(File expected, String sha) throws Exception {
        try (BufferedReader in = new BufferedReader(new FileReader(expected))) {
            String line;
            while ((line = in.readLine()) != null) {
                if (line.startsWith("#") || line.trim().isEmpty()) continue;
                String[] cols = line.split("\t");
                if (cols.length >= 8 && cols[0].equals(sha)) return cols;
            }
        }
        return null;
    }

    /** The six separated instruments as mono, from the cache or by running the model. */
    private static float[][] stems(String modelPath, File cache, final float[] left, final float[] right, final int total) throws Exception {
        final int count = StemSplitter.STEMS.length;
        final float[][] mono = new float[count][total];
        if (cache.exists() && cache.length() == 4L * count * total) {
            try (DataInputStream in = new DataInputStream(new BufferedInputStream(new FileInputStream(cache), 1 << 20))) {
                byte[] bytes = new byte[4 * 16384];
                for (int s = 0; s < count; s++) {
                    int at = 0;
                    while (at < total) {
                        int n = Math.min(16384, total - at);
                        in.readFully(bytes, 0, n * 4);
                        ByteBuffer.wrap(bytes, 0, n * 4).asFloatBuffer().get(mono[s], at, n);
                        at += n;
                    }
                }
            }
            return mono;
        }
        final int[] wrote = {0}, readAt = {0};
        long t0 = System.currentTimeMillis();
        OrtStemModel model = new OrtStemModel(modelPath, 0);
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
            public void accept(float[] l, float[] r, float[][][] stems, int n) {
                for (int s = 0; s < count; s++) {
                    for (int i = 0; i < n; i++) mono[s][wrote[0] + i] = (stems[s][0][i] + stems[s][1][i]) * 0.5f;
                }
                wrote[0] += n;
            }
        }, null);
        model.close();
        System.out.printf(Locale.US, "  separated in %.0f s%n", (System.currentTimeMillis() - t0) / 1000.0);
        cache.getParentFile().mkdirs();
        try (DataOutputStream out = new DataOutputStream(new BufferedOutputStream(new FileOutputStream(cache), 1 << 20))) {
            ByteBuffer bb = ByteBuffer.allocate(4 * 16384);
            for (int s = 0; s < count; s++) {
                int at = 0;
                while (at < total) {
                    int n = Math.min(16384, total - at);
                    bb.clear();
                    bb.asFloatBuffer().put(mono[s], at, n);
                    out.write(bb.array(), 0, n * 4);
                    at += n;
                }
            }
        }
        return mono;
    }

    /**
     * Where notes start in one separated instrument, found without any of the charting code: the
     * level (over 23 ms, every 6 ms) rises 3 dB in one step and is within 30 dB of the part's
     * loudest moments. Starts closer than 80 ms count once.
     */
    static double[] starts(float[] x) {
        final int hop = 256, win = 1024;
        int frames = Math.max(0, (x.length - win) / hop + 1);
        if (frames < 4) return new double[0];
        double[] db = new double[frames];
        for (int f = 0; f < frames; f++) {
            double sum = 0;
            for (int i = f * hop; i < f * hop + win; i++) sum += (double) x[i] * x[i];
            db[f] = 20 * Math.log10(Math.sqrt(sum / win) + 1e-9);
        }
        double[] sorted = db.clone();
        Arrays.sort(sorted);
        double top = sorted[(int) (frames * 0.99)];
        double fps = (double) SR / hop;
        List<Double> out = new ArrayList<>();
        int skip = (int) (0.08 * fps);
        for (int i = 1; i < frames - 2; ) {
            if (db[i] - db[i - 1] > 3.0 && db[i + 2] > top - 30) {
                out.add(i / fps);
                i += skip;
            } else {
                i++;
            }
        }
        double[] a = new double[out.size()];
        for (int i = 0; i < a.length; i++) a[i] = out.get(i);
        return a;
    }

    /**
     * Bass / 808 hits, found without any of the charting code. A hit is a clear jump in level
     * (2.4 dB or more within 25 ms; the wave's highest point over 30 ms, every 5 ms), or a click (a burst in the high
     * frequencies 10 dB over their usual level) together with a jump of 1.5 dB, within 20 dB of
     * the part's loudest moments (quieter than that are ghost notes and rumble). An 808's own
     * flutter (ripples of 1-1.6 dB) is neither, and a click with no jump is a hi-hat leaking into
     * the bass. Hits closer than 120 ms count once, so of a double hit only the first is counted.
     * (An 808 hit again while it still rings only gets a few dB louder; a cruder test misses those.)
     */
    static double[] bassHits(float[] x) {
        final int hop = (int) (0.005 * SR), win = (int) (0.03 * SR), clickWin = (int) (0.012 * SR);
        int n = x.length, frames = Math.max(0, (n - win) / hop + 1);
        if (frames < 16) return new double[0];
        double[] hsq = new double[n + 1];
        for (int i = 0; i < n; i++) {
            double d = i >= 2 ? x[i] - 2.0 * x[i - 1] + x[i - 2] : 0;
            hsq[i + 1] = hsq[i] + d * d;
        }
        double[] db = new double[frames], hf = new double[frames];
        for (int f = 0; f < frames; f++) {
            int a = f * hop;
            // The level is the highest point of the wave in the window, not its average power:
            // 30 ms is barely one cycle of a 41 Hz note, and the average wobbles by a dB or two
            // from one window to the next, which looks like hits inside a held note.
            float peak = 0;
            for (int i = a; i < a + win; i++) peak = Math.max(peak, Math.abs(x[i]));
            db[f] = 20 * Math.log10(peak + 1e-9);
            hf[f] = 20 * Math.log10(Math.sqrt((hsq[Math.min(n, a + clickWin)] - hsq[a]) / clickWin) + 1e-9);
        }
        double[] sorted = db.clone();
        Arrays.sort(sorted);
        double top = sorted[(int) (frames * 0.99)];
        double[] loud = new double[frames];
        int loudCount = 0;
        for (int f = 0; f < frames; f++) if (db[f] > top - 20) loud[loudCount++] = hf[f];
        Arrays.sort(loud, 0, loudCount);
        double usualClick = loudCount > 0 ? loud[loudCount / 2] : -200;
        final int span = 5, skip = 24;
        List<Double> out = new ArrayList<>();
        for (int i = span; i < frames - 6; ) {
            double low = db[i - 1];
            for (int k = i - span; k < i; k++) low = Math.min(low, db[k]);
            double rise = db[i] - low;
            boolean click = hf[i] > usualClick + 10 && Math.max(rise, db[i + 3] - low) >= 1.5;
            if ((rise >= 2.4 || click) && db[i + 6] > top - 20) {
                out.add((double) i * hop / SR + 0.02); // the 30 ms window sees a hit about 20 ms early
                i += skip;
            } else {
                i++;
            }
        }
        double[] a = new double[out.size()];
        for (int i = 0; i < a.length; i++) a[i] = out.get(i);
        return a;
    }

    /** Of the {@code times} the chart covers (a note within 70 ms), the percent with one within 35 ms. */
    private static int onTime(GuitarCharter.Chart chart, SongWriter.Result r, double[] times) {
        List<GuitarCharter.Note> notes = chart.notes.get(GuitarCharter.EXPERT);
        double[] at = new double[notes.size()];
        for (int i = 0; i < at.length; i++) at[i] = r.tempo.tickToTime(notes.get(i).tick);
        Arrays.sort(at);
        int covered = 0, close = 0;
        for (double t : times) {
            int k = Arrays.binarySearch(at, t);
            if (k < 0) k = -k - 1;
            double d = Double.MAX_VALUE;
            if (k < at.length) d = Math.abs(at[k] - t);
            if (k > 0) d = Math.min(d, Math.abs(at[k - 1] - t));
            if (d < 0.07) covered++;
            if (d < 0.035) close++;
        }
        return covered == 0 ? 0 : (int) Math.round(100.0 * close / covered);
    }

    /** Percent of {@code times} with an Expert note of the chart within 70 ms. */
    private static int percent(GuitarCharter.Chart chart, SongWriter.Result r, double[] times) {
        List<GuitarCharter.Note> notes = chart.notes.get(GuitarCharter.EXPERT);
        double[] at = new double[notes.size()];
        for (int i = 0; i < at.length; i++) at[i] = r.tempo.tickToTime(notes.get(i).tick);
        Arrays.sort(at);
        int hit = 0;
        for (double t : times) {
            int k = Arrays.binarySearch(at, t);
            if (k < 0) k = -k - 1;
            if ((k < at.length && Math.abs(at[k] - t) < 0.07) || (k > 0 && Math.abs(at[k - 1] - t) < 0.07)) hit++;
        }
        return times.length == 0 ? 0 : (int) Math.round(100.0 * hit / times.length);
    }
}
