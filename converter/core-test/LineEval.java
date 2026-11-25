import com.clockworkblack.yargconverter.core.*;

import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Measures how close the converter puts each lyric line to where it is really sung, on a real
 * song: java LineEval vocals.wav lyrics.lrc truth.tsv
 * truth.tsv has one row per line with words: the start heard by speech recognition, or empty.
 * Compares the final vocals chart with the lyrics as given, and after LyricAligner.checkLines.
 */
public final class LineEval {
    public static void main(String[] a) throws Exception {
        VocalAnalyzer.Voice voice = listen(new File(a[0]));
        List<Lrc.Line> lines = new ArrayList<>();
        for (Lrc.Line l : Lrc.parse(Lrc.decode(Files.readAllBytes(new File(a[1]).toPath())))) if (!VocalCharter.words(l.text).isEmpty()) lines.add(l);
        List<String> rows = Files.readAllLines(new File(a[2]).toPath());
        double[] truth = new double[rows.size()];
        for (int i = 0; i < rows.size(); i++) {
            String s = rows.get(i).split("\t", -1)[0];
            truth[i] = s.isEmpty() ? Double.NaN : Double.parseDouble(s);
        }
        if (truth.length != lines.size()) throw new IllegalStateException(truth.length + " truth rows, " + lines.size() + " lines");

        report("stamps as given", stamps(lines), truth);
        report("chart, as given", chart(lines, voice), truth);
        LyricAligner.Result fixed = LyricAligner.checkLines(lines, voice);
        report("stamps, checked", stamps(fixed.lines), truth);
        report("chart, checked", chart(fixed.lines, voice), truth);
        if (a.length > 3) {
            double[] before = chart(lines, voice), after = chart(fixed.lines, voice);
            for (int i = 0; i < truth.length; i++) {
                System.out.printf(Locale.US, "%2d truth %7s  given %7.2f -> %7.2f   chart %7.2f -> %7.2f  %s%n", i + 1,
                        Double.isNaN(truth[i]) ? "-" : String.format(Locale.US, "%.2f", truth[i]), lines.get(i).time, fixed.lines.get(i).time,
                        before[i], after[i], lines.get(i).text);
            }
        }
    }

    static double[] stamps(List<Lrc.Line> lines) {
        double[] t = new double[lines.size()];
        for (int i = 0; i < t.length; i++) t[i] = lines.get(i).time;
        return t;
    }

    /** Where the vocals chart starts each line. */
    static double[] chart(List<Lrc.Line> lines, VocalAnalyzer.Voice voice) {
        double[] beats = new double[(int) (voice.duration * 2) + 4];
        for (int i = 0; i < beats.length; i++) beats[i] = i * 0.5;
        TempoMap map = TempoMap.fromBeats(beats, 0, 4);
        VocalCharter.Chart c = VocalCharter.build(lines, map, voice.duration, voice);
        double[] t = new double[lines.size()];
        java.util.Arrays.fill(t, Double.NaN);
        for (VocalCharter.Phrase p : c.phrases) if (Double.isNaN(t[p.line])) t[p.line] = p.lineStart;
        return t;
    }

    static void report(String what, double[] t, double[] truth) {
        int n = 0, close = 0, near = 0, far = 0;
        double sum = 0;
        for (int i = 0; i < truth.length; i++) {
            if (Double.isNaN(truth[i]) || Double.isNaN(t[i])) continue;
            double e = Math.abs(t[i] - truth[i]);
            n++;
            sum += e;
            if (e <= 0.3) close++;
            if (e <= 0.6) near++;
            if (e > 1.0) far++;
        }
        System.out.printf(Locale.US, "%-18s %2d lines: within 0.3 s %2d, within 0.6 s %2d, over 1 s %2d, mean error %.2f s%n", what, n, close, near, far, sum / n);
    }

    static VocalAnalyzer.Voice listen(File wav) throws Exception {
        byte[] b = Files.readAllBytes(wav.toPath());
        ByteBuffer bb = ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN);
        int rate = bb.getInt(24);
        int n = (b.length - 44) / 2;
        float[] x = new float[n];
        for (int i = 0; i < n; i++) x[i] = bb.getShort(44 + 2 * i) / 32768f;
        VocalAnalyzer an = new VocalAnalyzer(rate);
        for (int i = 0; i < n; i += 4096) an.feed(x, i, Math.min(4096, n - i));
        return an.finish();
    }
}
