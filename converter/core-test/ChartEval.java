import com.clockworkblack.yargconverter.core.AudioAnalyzer;
import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.SongWriter;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/** Scores the charts made for a {@link SynthBand} song against what was really played. */
final class ChartEval {
    double tempoRatio;
    double beatWorstMs, beatAvgMs;
    double downbeatRate;
    double coverage;        // guitar notes that got an Expert note
    double offGuitar;       // Expert notes with no guitar note under them
    double samePitchSameFret;
    double rightDirection, wrongDirection;
    double repeatMatch;     // identical notes between repeats of a section
    double chordAsChord, singleAsSingle;
    double subset;          // lower-difficulty notes that also exist in Expert
    int[] counts = new int[4];
    int tempoMarkers;
    double seconds;

    static ChartEval run(SynthBand song) {
        AudioAnalyzer an = new AudioAnalyzer(song.sampleRate);
        for (int i = 0; i < song.audio.length; i += 4096) an.feed(song.audio, i, Math.min(4096, song.audio.length - i));
        Features f = an.finish();
        SongWriter.Result r = SongWriter.analyze(f, null);
        return score(song, r);
    }

    static ChartEval score(SynthBand song, SongWriter.Result r) {
        ChartEval e = new ChartEval();
        e.seconds = r.duration;
        e.tempoMarkers = r.tempo.tick.length;
        for (int d = 0; d < 4; d++) e.counts[d] = r.noteCount(d);

        // Tempo and beat grid.
        double trueBpm = 60.0 * (song.beats.size() - 1) / (song.beats.get(song.beats.size() - 1) - song.beats.get(0));
        e.tempoRatio = r.bpm / trueBpm;
        double worst = 0, sum = 0;
        int judged = 0;
        for (int i = 0; i < song.beats.size(); i++) {
            // Nothing marks the beat while the last chord rings out, so the outro is not judged.
            if (song.sectionOf.get(i / song.beatsPerMeasure) == 'O') continue;
            double t = song.beats.get(i);
            double err = r.tempo.tickToTime(r.tempo.snap(t, 96)) - t;
            worst = Math.max(worst, Math.abs(err));
            sum += err;
            judged++;
        }
        e.beatWorstMs = worst * 1000;
        e.beatAvgMs = sum / Math.max(1, judged) * 1000;

        // Downbeats: does each real measure start coincide with a chart measure start?
        List<Double> chartMeasures = new ArrayList<>();
        for (int m = 0; ; m++) {
            double t = r.tempo.tickToTime(r.tempo.measureStart(m));
            if (t > r.duration) break;
            chartMeasures.add(t);
        }
        int hit = 0, total = 0;
        for (int m = 1; m < song.measures.size() - 3; m++) {
            total++;
            if (near(chartMeasures, song.measures.get(m)) < 0.045) hit++;
        }
        e.downbeatRate = total > 0 ? (double) hit / total : 0;

        // Expert notes against the guitar part.
        List<GuitarCharter.Note> expert = r.chart.notes.get(GuitarCharter.EXPERT);
        double[] noteTime = new double[expert.size()];
        for (int i = 0; i < noteTime.length; i++) noteTime[i] = r.tempo.tickToTime(expert.get(i).tick);
        int[] match = new int[song.guitar.size()];
        boolean[] used = new boolean[expert.size()];
        int covered = 0;
        for (int g = 0; g < match.length; g++) {
            match[g] = -1;
            double best = 0.045;
            for (int i = 0; i < noteTime.length; i++) {
                double d = Math.abs(noteTime[i] - song.guitar.get(g).time);
                if (d < best) {
                    best = d;
                    match[g] = i;
                }
            }
            if (match[g] >= 0) {
                covered++;
                used[match[g]] = true;
            }
        }
        e.coverage = (double) covered / Math.max(1, match.length);
        int off = 0;
        for (boolean u : used) if (!u) off++;
        e.offGuitar = (double) off / Math.max(1, used.length);

        // Does the chart go up when the music goes up, and repeat a fret when the note repeats?
        int same = 0, sameOk = 0, moved = 0, right = 0, wrong = 0;
        int chords = 0, chordOk = 0, singles = 0, singleOk = 0;
        for (int g = 0; g < match.length; g++) {
            if (match[g] < 0) continue;
            SynthBand.GuitarNote gn = song.guitar.get(g);
            GuitarCharter.Note cn = expert.get(match[g]);
            if (gn.chord) {
                chords++;
                if (cn.lane2 >= 0) chordOk++;
            } else {
                singles++;
                if (cn.lane2 < 0) singleOk++;
            }
            if (g == 0 || match[g - 1] < 0 || match[g - 1] == match[g]) continue;
            SynthBand.GuitarNote gp = song.guitar.get(g - 1);
            if (song.sectionOf.get(gp.measure) != song.sectionOf.get(gn.measure)) continue;
            GuitarCharter.Note cp = expert.get(match[g - 1]);
            int dp = gn.midi - gp.midi;
            int dl = low(cn) - low(cp);
            if (dp == 0) {
                same++;
                if (dl == 0) sameOk++;
            } else {
                moved++;
                if (Integer.signum(dp) == Integer.signum(dl)) right++;
                else if (dl != 0) wrong++;
            }
        }
        e.samePitchSameFret = same > 0 ? (double) sameOk / same : 0;
        e.rightDirection = moved > 0 ? (double) right / moved : 0;
        e.wrongDirection = moved > 0 ? (double) wrong / moved : 0;
        e.chordAsChord = chords > 0 ? (double) chordOk / chords : 0;
        e.singleAsSingle = singles > 0 ? (double) singleOk / singles : 0;

        // Repeated sections should produce the same chart.
        int common = 0, union = 0;
        for (int a = 0; a < song.sectionStarts.size(); a++) {
            for (int b = a + 1; b < song.sectionStarts.size(); b++) {
                if (song.sectionStarts.get(a)[1] != song.sectionStarts.get(b)[1]) continue;
                int bars = (a + 1 < song.sectionStarts.size() ? song.sectionStarts.get(a + 1)[0] : song.sectionOf.size()) - song.sectionStarts.get(a)[0];
                Set<String> sa = sectionNotes(song, r, expert, song.sectionStarts.get(a)[0], bars);
                Set<String> sb = sectionNotes(song, r, expert, song.sectionStarts.get(b)[0], bars);
                Set<String> all = new HashSet<>(sa);
                all.addAll(sb);
                sa.retainAll(sb);
                common += sa.size();
                union += all.size();
            }
        }
        e.repeatMatch = union > 0 ? (double) common / union : 0;

        Set<Long> expertTicks = new HashSet<>();
        for (GuitarCharter.Note n : expert) expertTicks.add(n.tick);
        int lower = 0, inExpert = 0;
        for (int d = 0; d < 3; d++) {
            for (GuitarCharter.Note n : r.chart.notes.get(d)) {
                lower++;
                if (expertTicks.contains(n.tick)) inExpert++;
            }
        }
        e.subset = lower > 0 ? (double) inExpert / lower : 0;
        return e;
    }

    private static int low(GuitarCharter.Note n) {
        return n.lane2 >= 0 ? Math.min(n.lane, n.lane2) : n.lane;
    }

    private static double near(List<Double> sorted, double t) {
        double best = 1e9;
        for (double s : sorted) best = Math.min(best, Math.abs(s - t));
        return best;
    }

    private static Set<String> sectionNotes(SynthBand song, SongWriter.Result r, List<GuitarCharter.Note> notes, int firstBar, int bars) {
        long a = r.tempo.snap(song.measures.get(firstBar), 48);
        long b = r.tempo.snap(song.measures.get(firstBar + bars), 48);
        Set<String> out = new HashSet<>();
        for (GuitarCharter.Note n : notes) {
            if (n.tick >= a && n.tick < b) out.add((n.tick - a) + ":" + n.lane + ":" + n.lane2);
        }
        return out;
    }

    String report() {
        return String.format(Locale.US,
                "  tempo x%.3f (%d markers), beats worst %.0f ms avg %+.0f ms, downbeats %.0f%%%n"
                        + "  guitar notes charted %.0f%%, Expert notes off the guitar %.0f%%%n"
                        + "  same note -> same fret %.0f%%, moves the right way %.0f%% (wrong way %.0f%%)%n"
                        + "  repeats identical %.0f%%, chords as chords %.0f%%, singles as singles %.0f%%%n"
                        + "  notes E/M/H/X %d/%d/%d/%d (%.1f per second on Expert), lower levels drawn from Expert %.0f%%",
                tempoRatio, tempoMarkers, beatWorstMs, beatAvgMs, downbeatRate * 100,
                coverage * 100, offGuitar * 100,
                samePitchSameFret * 100, rightDirection * 100, wrongDirection * 100,
                repeatMatch * 100, chordAsChord * 100, singleAsSingle * 100,
                counts[0], counts[1], counts[2], counts[3], counts[3] / Math.max(1, seconds), subset * 100);
    }

    public static void main(String[] args) {
        Object[][] cases = {
            {"Steady 118 BPM", 44100, 118.0, 0.0, 0.40, 4},
            {"Drifting 104 BPM (+-3%)", 44100, 104.0, 0.03, 0.25, 4},
            {"Fast 142 BPM, 48 kHz, slight drift", 48000, 142.0, 0.015, 0.10, 4},
            {"Waltz 126 BPM (3/4)", 44100, 126.0, 0.0, 0.30, 3},
            {"Busy mix 96 BPM (singing, fast riff, 16th hats)", 44100, 96.0, 0.02, 0.50, 4},
            {"Busy mix 128 BPM", 44100, 128.0, 0.01, 0.20, 4},
        };
        for (Object[] c : cases) {
            boolean busy = ((String) c[0]).startsWith("Busy");
            SynthBand song = new SynthBand((Integer) c[1], (Double) c[2], (Double) c[3], (Double) c[4], (Integer) c[5], 7, busy);
            long t0 = System.currentTimeMillis();
            ChartEval e = run(song);
            System.out.println(c[0] + "  [" + (System.currentTimeMillis() - t0) + " ms]");
            System.out.println(e.report());
        }
    }
}
