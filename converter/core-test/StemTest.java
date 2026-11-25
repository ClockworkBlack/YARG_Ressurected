import com.clockworkblack.yargconverter.core.AudioAnalyzer;
import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.Resampler;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.StemSplitter;

import java.util.Random;

/**
 * Checks for everything around instrument separation that does not need the real model:
 * cutting a song into overlapping segments and joining the results, sample-rate conversion,
 * and charting from separated instruments (using the test song's true instrument tracks, i.e.
 * what a perfect separation would give).
 */
final class StemTest {
    private static int failures;

    static int run() throws Exception {
        failures = 0;
        splitting();
        resampling();
        charting("Charting from perfectly separated instruments, 118 BPM", new SynthBand(44100, 118.0, 0.0, 0.40, 4, 7));
        charting("Charting from perfectly separated instruments, busy mix 96 BPM", new SynthBand(44100, 96.0, 0.02, 0.50, 4, 7, true));
        return failures;
    }

    private static void check(boolean ok, String what) {
        if (!ok) {
            failures++;
            System.out.println("  FAIL: " + what);
        }
    }

    /** A stand-in model whose stems are fixed shares of the mix, so the right answer is known exactly. */
    private static final class ShareModel implements StemSplitter.Model {
        final float[] share = {0.5f, 0.3f, 0.2f};
        int runs;

        @Override
        public int stems() {
            return share.length;
        }

        @Override
        public void run(float[] left, float[] right, float[][][] out) {
            runs++;
            for (int s = 0; s < share.length; s++) {
                for (int i = 0; i < left.length; i++) {
                    out[s][0][i] = left[i] * share[s];
                    out[s][1][i] = right[i] * share[s];
                }
            }
        }
    }

    private static void splitting() throws Exception {
        System.out.println("Splitting a song into overlapping segments and joining the results");
        Random rnd = new Random(5);
        final int segment = 400;
        int[] lengths = {1, 37, 299, 300, 301, 399, 400, 401, 600, 601, 1000, 4321};
        for (final int total : lengths) {
            final float[] l = new float[total], r = new float[total];
            for (int i = 0; i < total; i++) {
                l[i] = (float) (rnd.nextDouble() * 2 - 1);
                r[i] = (float) (rnd.nextDouble() * 2 - 1);
            }
            final int[] readAt = {0};
            final int[] wrote = {0};
            final double[] worst = {0};
            final ShareModel model = new ShareModel();
            StemSplitter.Source source = new StemSplitter.Source() {
                @Override
                public int read(float[] left, float[] right, int offset, int length) {
                    // Hand data over in awkward small pieces, like a real file reader might.
                    int n = Math.min(Math.min(length, 97), total - readAt[0]);
                    if (n <= 0) return 0;
                    System.arraycopy(l, readAt[0], left, offset, n);
                    System.arraycopy(r, readAt[0], right, offset, n);
                    readAt[0] += n;
                    return n;
                }
            };
            StemSplitter.Sink sink = new StemSplitter.Sink() {
                @Override
                public void accept(float[] mixLeft, float[] mixRight, float[][][] stems, int count) {
                    for (int i = 0; i < count; i++) {
                        int at = wrote[0] + i;
                        if (at >= total) {
                            worst[0] = 1e9;
                            break;
                        }
                        worst[0] = Math.max(worst[0], Math.abs(mixLeft[i] - l[at]));
                        worst[0] = Math.max(worst[0], Math.abs(mixRight[i] - r[at]));
                        for (int s = 0; s < 3; s++) {
                            worst[0] = Math.max(worst[0], Math.abs(stems[s][0][i] - l[at] * model.share[s]));
                            worst[0] = Math.max(worst[0], Math.abs(stems[s][1][i] - r[at] * model.share[s]));
                        }
                    }
                    wrote[0] += count;
                }
            };
            boolean finished = StemSplitter.split(source, total, model, segment, sink, null);
            check(finished && wrote[0] == total, "length " + total + ": every sample comes out once (got " + wrote[0] + ")");
            check(worst[0] < 1e-5, "length " + total + ": stems line up with the mix and join seamlessly (worst error " + worst[0] + ")");
            check(model.runs == Math.max(1, (total + 299) / 300), "length " + total + ": expected number of model runs (" + model.runs + ")");
        }
        // Stopping early.
        final int[] calls = {0};
        boolean finished = StemSplitter.split(new StemSplitter.Source() {
            @Override
            public int read(float[] left, float[] right, int offset, int length) {
                return 0;
            }
        }, 5000, new ShareModel(), segment, new StemSplitter.Sink() {
            @Override
            public void accept(float[] a, float[] b, float[][][] c, int count) {}
        }, new StemSplitter.Progress() {
            @Override
            public boolean onSegment(int done, int total) {
                calls[0]++;
                return done < 3;
            }
        });
        check(!finished && calls[0] == 3, "can be cancelled part-way");
    }

    private static void resampling() {
        System.out.println("Sample-rate conversion");
        int[][] rates = {{48000, 44100}, {22050, 44100}, {32000, 44100}, {96000, 44100}, {44100, 44100}};
        for (int[] rate : rates) {
            int from = rate[0], to = rate[1];
            Resampler rs = new Resampler(from, to);
            int total = from * 2;
            float[] inL = new float[1000], inR = new float[1000];
            float[] outL = new float[rs.capacityFor(1000)], outR = new float[rs.capacityFor(1000)];
            int produced = 0;
            double worst = 0;
            for (int at = 0; at < total; at += 1000) {
                int n = Math.min(1000, total - at);
                for (int i = 0; i < n; i++) {
                    inL[i] = (float) Math.sin(2 * Math.PI * 440.0 * (at + i) / from);
                    inR[i] = (float) Math.sin(2 * Math.PI * 1000.0 * (at + i) / from);
                }
                int got = rs.process(inL, inR, n, outL, outR);
                for (int i = 0; i < got; i++) {
                    double t = (double) (produced + i) / to;
                    worst = Math.max(worst, Math.abs(outL[i] - Math.sin(2 * Math.PI * 440.0 * t)));
                    worst = Math.max(worst, Math.abs(outR[i] - Math.sin(2 * Math.PI * 1000.0 * t)));
                }
                produced += got;
            }
            check(Math.abs(produced - 2 * to) <= 2, from + " -> " + to + ": right number of samples (" + produced + ")");
            check(worst < 0.02, from + " -> " + to + ": tones keep their pitch and timing (worst error " + worst + ")");
        }
    }

    static Features analyze(float[] x, int sr) {
        return analyze(x, sr, false);
    }

    static Features analyze(float[] x, int sr, boolean bassRange) {
        AudioAnalyzer an = new AudioAnalyzer(sr, bassRange);
        for (int i = 0; i < x.length; i += 4096) an.feed(x, i, Math.min(4096, x.length - i));
        return an.finish();
    }

    static double energy(float[] x) {
        double e = 0;
        for (float v : x) e += v * v;
        return e;
    }

    static SongWriter.Stems stems(float[] guitar, float[] keys, float[] bass, float[] mix, int sr) {
        SongWriter.Stems s = new SongWriter.Stems();
        float[] melodic = new float[guitar.length];
        for (int i = 0; i < melodic.length; i++) melodic[i] = guitar[i] + keys[i];
        s.guitar = analyze(guitar, sr);
        s.melodic = analyze(melodic, sr);
        s.bass = analyze(bass, sr, true);
        s.guitarEnergy = energy(guitar);
        s.melodicEnergy = energy(melodic);
        s.bassEnergy = energy(bass);
        s.mixEnergy = energy(mix);
        return s;
    }

    private static void charting(String name, SynthBand song) throws Exception {
        System.out.println(name);
        Features mix = analyze(song.audio, song.sampleRate);
        SongWriter.Result plain = SongWriter.analyze(mix, null, null);
        SongWriter.Result split = SongWriter.analyze(mix, null, null, stems(song.guitarTrack, song.keysTrack, song.bassTrack, song.audio, song.sampleRate));
        ChartEval before = ChartEval.score(song, plain);
        ChartEval after = ChartEval.score(song, split);
        System.out.println("  guitar from " + plain.guitarSource + ":");
        System.out.println(before.report().replace("\n", "\n  "));
        System.out.println("  guitar from " + split.guitarSource + ":");
        System.out.println(after.report().replace("\n", "\n  "));
        check(split.guitarSource.contains("guitar"), "guitar chart comes from the guitar stem");
        check(after.coverage >= 0.95, "guitar notes are charted from the stem");
        check(after.offGuitar <= 0.03, "nothing but guitar in the guitar chart (" + after.offGuitar + ")");
        check(after.samePitchSameFret >= 0.9 && after.wrongDirection <= 0.05, "frets follow the guitar's pitch");
        check(after.repeatMatch >= 0.9, "repeats still match");
        check(Math.abs(after.beatAvgMs - before.beatAvgMs) < 1e-6, "beats still come from the full mix");

        check(split.bassChart != null, "a bass chart is written");
        if (split.bassChart != null) {
            ChartEval bass = ChartEval.score(song, split, song.bassNotes, split.bassChart);
            System.out.println("  bass from the separated bass:");
            System.out.println(bass.report().replace("\n", "\n  "));
            check(bass.coverage >= 0.9 && bass.offGuitar <= 0.05, "bass chart follows the bass and nothing else");
            check(bass.samePitchSameFret >= 0.85 && bass.wrongDirection <= 0.1, "bass frets follow the bass's pitch");
            for (int d = 0; d < 4; d++) {
                for (GuitarCharter.Note n : split.bassChart.notes.get(d)) check(n.lane2 < 0, "bass plays single notes");
            }
            check(split.bassTier >= 0, "bass difficulty rating set");
        }
        byte[] midi = com.clockworkblack.yargconverter.core.MidiSong.build(split);
        for (String problem : MidiCheck.verify(midi, split)) check(false, "notes.mid: " + problem);
        check(new MidiCheck(midi).track("PART BASS") != null, "the bass part is in the song file");
        java.io.StringWriter ini = new java.io.StringWriter();
        SongWriter.writeIni(ini, new com.clockworkblack.yargconverter.core.SongMeta(), split);
        check(ini.toString().contains("diff_bass = " + split.bassTier) && !ini.toString().contains("diff_bass = -1"), "song.ini lists the bass part");
    }

    public static void main(String[] args) throws Exception {
        int f = run();
        System.out.println(f == 0 ? "All stem checks passed" : "FAILED: " + f + " stem check(s)");
        if (f > 0) System.exit(1);
    }
}
