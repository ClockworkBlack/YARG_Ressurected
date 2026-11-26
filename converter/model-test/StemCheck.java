import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.StemSplitter;
import com.clockworkblack.yargconverter.stems.OrtStemModel;

import java.util.Locale;

/**
 * Runs the real separation model (the exact file the app downloads) through the app's own
 * code on a test song, on a desktop JVM. GitHub Actions runs this on every change:
 *
 *   java -cp out:onnxruntime.jar StemCheck /path/to/htdemucs_6s_fp16weights.onnx
 *
 * The test song is synthetic, so the instruments do not sound like what the model was trained
 * on; this checks that the plumbing is right (shapes, order of stems, segment joins, timing)
 * and reports where each true instrument ends up, rather than grading audio quality.
 */
public final class StemCheck {
    public static void main(String[] args) throws Exception {
        if (args.length < 1) {
            System.out.println("usage: StemCheck model.onnx");
            System.exit(2);
        }
        boolean busy = args.length > 1 && args[1].equals("busy");
        final SynthBand song = busy ? new SynthBand(44100, 96.0, 0.02, 0.50, 4, 7, true) : new SynthBand(44100, 118.0, 0.0, 0.40, 4, 7);
        final int total = song.audio.length;
        final int stemCount = StemSplitter.STEMS.length;
        final float[][] mono = new float[stemCount][total];
        final int[] wrote = {0};
        final int[] readAt = {0};
        final double[] mixError = {0};

        long t0 = System.currentTimeMillis();
        OrtStemModel model = new OrtStemModel(args[0], 0);
        long loadMs = System.currentTimeMillis() - t0;
        final long[] segmentStart = {System.currentTimeMillis()};
        final long[] slowest = {0};
        final long[] runMs = {0};

        StemSplitter.Source source = new StemSplitter.Source() {
            @Override
            public int read(float[] left, float[] right, int offset, int length) {
                int n = Math.min(length, total - readAt[0]);
                if (n <= 0) return 0;
                System.arraycopy(song.audio, readAt[0], left, offset, n);
                System.arraycopy(song.audio, readAt[0], right, offset, n);
                readAt[0] += n;
                return n;
            }
        };
        StemSplitter.Sink sink = new StemSplitter.Sink() {
            @Override
            public void accept(float[] mixLeft, float[] mixRight, float[][][] stems, int count) {
                for (int i = 0; i < count; i++) {
                    int at = wrote[0] + i;
                    mixError[0] = Math.max(mixError[0], Math.abs(mixLeft[i] - song.audio[at]));
                    for (int s = 0; s < stemCount; s++) mono[s][at] = (stems[s][0][i] + stems[s][1][i]) * 0.5f;
                }
                wrote[0] += count;
            }
        };
        StemSplitter.Progress progress = new StemSplitter.Progress() {
            @Override
            public boolean onSegment(int done, int totalSegments) {
                long now = System.currentTimeMillis();
                long took = now - segmentStart[0];
                segmentStart[0] = now;
                slowest[0] = Math.max(slowest[0], took);
                runMs[0] += took;
                System.out.printf(Locale.US, "  segment %d of %d: %.1f s%n", done, totalSegments, took / 1000.0);
                return true;
            }
        };
        StemSplitter.split(source, total, model, sink, progress);
        model.close();

        double seconds = (double) total / song.sampleRate;
        System.out.printf(Locale.US, "Model loaded in %.1f s; separated %.0f s of audio in %.1f s (%.2f x the song's length) on %d processor(s)%n",
                loadMs / 1000.0, seconds, runMs[0] / 1000.0, runMs[0] / 1000.0 / seconds, Runtime.getRuntime().availableProcessors());

        int failures = 0;
        if (wrote[0] != total || mixError[0] > 1e-6) {
            System.out.println("FAIL: output is not aligned with the input (" + wrote[0] + " of " + total + " samples)");
            failures++;
        }

        // Do the stems add back up to the mix?
        double err = 0, ref = 0;
        boolean finite = true;
        for (int i = 0; i < total; i++) {
            double sum = 0;
            for (int s = 0; s < stemCount; s++) {
                sum += mono[s][i];
                if (Float.isNaN(mono[s][i]) || Float.isInfinite(mono[s][i])) finite = false;
            }
            err += (sum - song.audio[i]) * (sum - song.audio[i]);
            ref += song.audio[i] * song.audio[i];
        }
        double residual = 10 * Math.log10(Math.max(1e-20, err / ref));
        System.out.printf(Locale.US, "All stems added together differ from the mix by %.1f dB (lower is better; around -15 dB or below means the joins and levels are right)%n", residual);
        if (!finite) {
            System.out.println("FAIL: the model returned values that are not numbers");
            failures++;
        }
        if (residual > -8) {
            System.out.println("FAIL: stems do not add back up to the mix");
            failures++;
        }

        // Where does each true instrument end up?
        String[] trueNames = {"drums", "bass", "guitar", "keys"};
        float[][] truth = {song.drumsTrack, song.bassTrack, song.guitarTrack, song.keysTrack};
        System.out.println("Share of each true instrument found in each model output:");
        StringBuilder head = new StringBuilder("              ");
        for (String s : StemSplitter.STEMS) head.append(String.format("%8s", s));
        System.out.println(head);
        int[] best = new int[truth.length];
        for (int t = 0; t < truth.length; t++) {
            double tt = 0;
            for (int i = 0; i < total; i++) tt += truth[t][i] * truth[t][i];
            StringBuilder row = new StringBuilder(String.format("  true %-7s", trueNames[t]));
            double bestShare = -1;
            for (int s = 0; s < stemCount; s++) {
                double st = 0;
                for (int i = 0; i < total; i++) st += mono[s][i] * truth[t][i];
                double share = st / Math.max(1e-12, tt);
                row.append(String.format(Locale.US, "%7.0f%%", share * 100));
                if (share > bestShare) {
                    bestShare = share;
                    best[t] = s;
                }
            }
            System.out.println(row);
        }
        if (best[0] != StemSplitter.DRUMS) System.out.println("NOTE: the synthetic drums mostly went to '" + StemSplitter.STEMS[best[0]] + "'");
        if (best[1] != StemSplitter.BASS) System.out.println("NOTE: the synthetic bass mostly went to '" + StemSplitter.STEMS[best[1]] + "'");
        System.out.println("The synthetic guitar mostly went to '" + StemSplitter.STEMS[best[2]] + "'");

        // Chart from the model's stems, exactly as the app does.
        float[] melodic = new float[total];
        for (int i = 0; i < total; i++) melodic[i] = mono[StemSplitter.GUITAR][i] + mono[StemSplitter.OTHER][i] + mono[StemSplitter.PIANO][i];
        SongWriter.Stems stems = new SongWriter.Stems();
        stems.guitar = StemTest.analyze(mono[StemSplitter.GUITAR], song.sampleRate);
        stems.melodic = StemTest.analyze(melodic, song.sampleRate);
        stems.bass = StemTest.analyze(mono[StemSplitter.BASS], song.sampleRate, true);
        stems.guitarEnergy = StemTest.energy(mono[StemSplitter.GUITAR]);
        stems.melodicEnergy = StemTest.energy(melodic);
        stems.bassEnergy = StemTest.energy(mono[StemSplitter.BASS]);
        stems.mixEnergy = StemTest.energy(song.audio);
        StemTest.addVocals(stems, melodic, mono[StemSplitter.VOCALS], song.sampleRate);
        Features mix = StemTest.analyze(song.audio, song.sampleRate);
        SongWriter.Result plain = SongWriter.analyze(mix, null, null);
        SongWriter.Result split = SongWriter.analyze(mix, null, null, stems);
        System.out.println("Guitar chart from the full mix:");
        System.out.println(ChartEval.score(song, plain).report());
        System.out.println("Guitar chart from " + split.guitarSource + " (model output):");
        System.out.println(ChartEval.score(song, split).report());
        if (split.bassChart != null) {
            System.out.println("Bass chart from the separated bass (model output):");
            System.out.println(ChartEval.score(song, split, song.bassNotes, split.bassChart).report());
        } else {
            System.out.println("No bass chart was written from the model's bass output.");
        }

        if (failures > 0) {
            System.out.println("FAILED: " + failures + " check(s)");
            System.exit(1);
        }
        System.out.println("Separation pipeline checks passed");
    }
}
