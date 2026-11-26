import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.SongWriter;

import java.util.Arrays;
import java.util.List;
import java.util.Locale;

/**
 * Guards what was learned converting real rap and R&B songs, on a synthetic beat with known
 * answers (TrapSong), so that later changes to the converter keep every fix:
 * dotted-808 tempo, hits on a still-ringing 808 charted and kept on their fret, a sustained 808
 * as one held note, guitar following the vocals (and the instruments where they lead), the soft
 * loop in Deep Music Mode guitar, and drums in Deep Music Mode bass.
 */
final class TrapTest {
    private static int failures;

    static int run() {
        failures = 0;
        TrapSong song = new TrapSong(95.0, 16, 11);
        System.out.println("Trap / R&B beat, 95 BPM: dotted-eighth 808s, long pumping 808s, claps, a soft legato loop, vocals");
        int sr = song.sampleRate;
        SongWriter.Stems s = StemTest.stems(new float[song.audio.length], song.loop, song.vocals, song.drums, song.bass, song.audio, sr);
        Features mix = StemTest.analyze(song.audio, sr);
        SongWriter.Result r = SongWriter.analyze(mix, null, null, s);

        System.out.printf(Locale.US, "  share of the sound: 808 %.0f%%, vocals %.0f%%, loop %.1f%%, drums %.1f%%%n",
                100 * s.bassEnergy / s.mixEnergy, 100 * s.vocalsEnergy / s.mixEnergy, 100 * s.melodicEnergy / s.mixEnergy, 100 * s.drumsEnergy / s.mixEnergy);
        // 1. Tempo: the 808's dotted eighths must not pass for the beat (95 would read as 126.7).
        System.out.printf(Locale.US, "  tempo %.1f BPM, %d beats to the measure%n", r.bpm, r.beatsPerMeasure);
        check(Math.abs(r.bpm / song.bpm - 1) < 0.02, "tempo is the beat's, not the dotted 808's (" + r.bpm + ")");

        // 2. Guitar follows the vocals (the loop is too quiet to lead), as single notes.
        check(r.guitarSource.contains("vocals"), "guitar follows the vocals (" + r.guitarSource + ")");
        double sung = covered(r.chart, r, song.sung);
        System.out.printf(Locale.US, "  regular guitar: %d Expert notes, sung notes covered %.0f%%%n", r.chart.notes.get(GuitarCharter.EXPERT).size(), 100 * sung);
        check(sung >= 0.8, "regular guitar covers the sung notes (" + sung + ")");

        // 3. Deep Music Mode guitar has the soft loop, which glides between notes with no attacks.
        check(r.deepChart != null, "Deep Music Mode guitar is written");
        if (r.deepChart != null) {
            double loop = covered(r.deepChart, r, song.loopNotes);
            System.out.printf(Locale.US, "  Deep Music Mode guitar: %d Expert notes, loop notes covered %.0f%%, sung notes %.0f%%%n",
                    r.deepChart.notes.get(GuitarCharter.EXPERT).size(), 100 * loop, 100 * covered(r.deepChart, r, song.sung));
            check(loop >= 0.8, "Deep Music Mode guitar covers the legato loop (" + loop + ")");
            double plainLoop = covered(r.chart, r, song.loopNotes);
            System.out.printf(Locale.US, "  regular guitar covers %.0f%% of the loop notes (vocals carry it; the loop only where nobody sings)%n", 100 * plainLoop);
            check(loop > plainLoop, "Deep Music Mode guitar adds the loop that the regular guitar leaves to the vocals");
        }
        // Where the loop is turned up as loud as the vocals (a guitar break over the rap), the
        // regular guitar follows it there.
        {
            double brk = covered(r.chart, r, song.breakNotes);
            System.out.printf(Locale.US, "  regular guitar where the loop is as loud as the vocals: %.0f%% of its notes%n", 100 * brk);
            check(brk >= 0.8, "regular guitar follows the instruments where they lead (" + brk + ")");
        }
        // 4. Bass: every 808 charted, nothing inside a long pumping 808, long 808s held.
        check(r.bassChart != null, "a bass chart is written");
        if (r.bassChart != null) {
            List<GuitarCharter.Note> bass = r.bassChart.notes.get(GuitarCharter.EXPERT);
            double hits = covered(r.bassChart, r, song.bassNotes);
            int phantom = 0, held = 0;
            StringBuilder lengths = new StringBuilder();
            for (double[] note : song.longBass) {
                for (GuitarCharter.Note b : bass) {
                    double t = r.tempo.tickToTime(b.tick);
                    if (t > note[0] + 0.15 && t < note[0] + note[1] - 0.05) phantom++;
                    if (Math.abs(t - note[0]) < 0.07) {
                        double len = r.tempo.tickToTime(b.tick + b.sustain) - t;
                        lengths.append(String.format(Locale.US, "%.2f/%.2fs ", len, note[1]));
                        if (len > 0.7 * note[1]) held++;
                    }
                }
            }
            // Hits on a still-ringing 808 are notes too, and the same note again: same fret.
            double rehits = covered(r.bassChart, r, song.ringingHits);
            int sameFret = 0, pairs = 0;
            int lastFret = -1, lastMeasure = -1;
            for (int i = 0; i < song.ringingHits.size(); i++) {
                int fret = -1;
                for (GuitarCharter.Note b : bass) if (Math.abs(r.tempo.tickToTime(b.tick) - song.ringingHits.get(i)) < 0.07) fret = b.lane;
                if (fret >= 0 && lastFret >= 0 && lastMeasure == song.ringingMeasure.get(i)) {
                    pairs++;
                    if (fret == lastFret) sameFret++;
                }
                lastFret = fret;
                lastMeasure = song.ringingMeasure.get(i);
            }
            double steadyFret = pairs == 0 ? 0 : (double) sameFret / pairs;
            System.out.println("  sustained 808s held (charted/real): " + lengths);
            System.out.printf(Locale.US, "  bass: %d Expert notes for %d 808 notes; 808s covered %.0f%%; hits on a ringing 808 covered %.0f%%, on the same fret as the hit before %.0f%%;%n"
                            + "        %d notes inside %d sustained 808s, %d of them held%n",
                    bass.size(), song.bassNotes.size(), 100 * hits, 100 * rehits, 100 * steadyFret, phantom, song.longBass.size(), held);
            check(rehits >= 0.9, "hits on a still-ringing 808 are charted (" + rehits + ")");
            check(steadyFret >= 0.9, "the same 808 note hit again stays on its fret (" + steadyFret + ")");
            check(hits >= 0.9, "bass covers the 808 notes (" + hits + ")");
            // The same 808 note gets the same fret (the song is tuned between two note names).
            java.util.Map<Integer, java.util.Map<Integer, Integer>> frets = new java.util.HashMap<>();
            for (int i = 0; i < song.bassNotes.size(); i++) {
                double t = song.bassNotes.get(i);
                for (GuitarCharter.Note b : bass) {
                    if (Math.abs(r.tempo.tickToTime(b.tick) - t) < 0.07) {
                        frets.computeIfAbsent(song.bassPitch.get(i), k -> new java.util.HashMap<>()).merge(b.lane, 1, Integer::sum);
                        break;
                    }
                }
            }
            int same = 0, all = 0;
            for (java.util.Map<Integer, Integer> m : frets.values()) {
                int most = 0, sum = 0;
                for (int c : m.values()) {
                    most = Math.max(most, c);
                    sum += c;
                }
                same += most;
                all += sum;
            }
            double consistent = all == 0 ? 0 : (double) same / all;
            // Reported, not yet checked: a distorted 808 that drops onto its note reads as its
            // fourth, fifth or octave too often for this to be reliable (see PENDING.md).
            System.out.printf(Locale.US, "  same 808 note -> same fret: %.0f%% (known weak spot)%n", 100 * consistent);
            check(phantom <= song.longBass.size() / 4, "a sustained 808 is one note (" + phantom + " notes inside)");
            check(held >= song.longBass.size() * 0.75, "sustained 808s are held for most of their length (" + held + " of " + song.longBass.size() + ")");
        }

        // 5. Deep Music Mode bass adds the claps; the regular bass does not.
        check(r.deepBassChart != null, "Deep Music Mode bass is written");
        if (r.deepBassChart != null && r.bassChart != null) {
            double deep = covered(r.deepBassChart, r, song.claps), plain = covered(r.bassChart, r, song.claps);
            System.out.printf(Locale.US, "  claps covered: Deep Music Mode bass %.0f%%, regular bass %.0f%%%n", 100 * deep, 100 * plain);
            check(deep >= 0.8, "Deep Music Mode bass covers the claps (" + deep + ")");
            check(r.drumNotesInBass > 0 && r.bassChart.notes.get(GuitarCharter.EXPERT).size() < r.deepBassChart.notes.get(GuitarCharter.EXPERT).size(),
                    "regular bass stays the bass alone");
        }
        // 6. The beat on its own (808s in dotted eighths, faint drums): the case that made the
        // tempo finder lock onto the dotted 808 at 4/3 of the real tempo.
        TrapSong beat = new TrapSong(95.0, 16, 12, true);
        SongWriter.Result br = SongWriter.analyze(StemTest.analyze(beat.audio, sr), null, null);
        System.out.printf(Locale.US, "  the beat alone: tempo %.1f BPM%n", br.bpm);
        check(Math.abs(br.bpm / beat.bpm - 1) < 0.02, "the beat alone: tempo is the beat's, not the dotted 808's (" + br.bpm + ")");
        return failures;
    }

    private static double covered(GuitarCharter.Chart chart, SongWriter.Result r, List<Double> times) {
        List<GuitarCharter.Note> notes = chart.notes.get(GuitarCharter.EXPERT);
        double[] at = new double[notes.size()];
        for (int i = 0; i < at.length; i++) at[i] = r.tempo.tickToTime(notes.get(i).tick);
        return near(at, times);
    }

    private static double near(double[] at, List<Double> times) {
        Arrays.sort(at);
        int hit = 0;
        for (double t : times) {
            int k = Arrays.binarySearch(at, t);
            if (k < 0) k = -k - 1;
            if ((k < at.length && Math.abs(at[k] - t) < 0.07) || (k > 0 && Math.abs(at[k - 1] - t) < 0.07)) hit++;
        }
        return times.isEmpty() ? 0 : (double) hit / times.size();
    }

    private static void check(boolean ok, String what) {
        if (!ok) {
            failures++;
            System.out.println("  FAIL: " + what);
        }
    }

    public static void main(String[] args) {
        int f = run();
        System.out.println(f == 0 ? "All trap checks passed" : "FAILED: " + f + " trap check(s)");
        if (f > 0) System.exit(1);
    }
}
