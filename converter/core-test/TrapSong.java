import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/**
 * A synthetic trap / R&B beat with known answers, for the lessons learned on real rap songs:
 * 808s in dotted eighths (3-3-2), which make the beat look a third faster than it is; an 808
 * that is hit again while its last note still rings at full strength (each hit only 3 dB
 * louder: real notes, which an earlier rule threw away); one long sustained 808 with the slight
 * flutter real ones have (one note, to be held); claps and hi-hats; a rapped / sung vocal line;
 * and a soft flute-like loop that glides from note to note without attacks, quieter than the
 * vocals. Like the real songs it is built after, the 808 dominates and the drums are faint
 * (which is what lets the dotted 808 pass for the beat), every 808 hit has a kick click and
 * drops from above onto its note, the whole song is tuned 30 cents flat, and every other
 * measure ends in an 808 double hit a 32nd note apart. With {@code beatOnly}, just the 808s and the faint drums: the case that fooled the
 * tempo finder.
 */
final class TrapSong {
    final int sampleRate = 44100;
    final double bpm;
    final float[] audio, drums, bass, loop, vocals;
    /** Start of each loop note, each 808 note (with its MIDI note), each clap, each sung note. */
    final List<Double> loopNotes = new ArrayList<>(), bassNotes = new ArrayList<>(), claps = new ArrayList<>(), sung = new ArrayList<>();
    final List<Integer> bassPitch = new ArrayList<>();
    /** Measures where the loop is turned up as loud as the vocals (a guitar break over the rap). */
    static final int BREAK_FROM = 10, BREAK_TO = 12;
    /** Start of each loop note in the louder measures. */
    final List<Double> breakNotes = new ArrayList<>();
    /** Sustained 808 notes (not hit again while they ring): start and how long they ring. */
    final List<double[]> longBass = new ArrayList<>();
    /** 808 double hits a 32nd note apart: the time of each hit of each pair. */
    final List<Double> doubleHits = new ArrayList<>();
    /** 808 hits on a note that is still ringing, and the measure each is in. */
    final List<Double> ringingHits = new ArrayList<>();
    final List<Integer> ringingMeasure = new ArrayList<>();
    final int measures;

    TrapSong(double bpm, int measures, long seed) {
        this(bpm, measures, seed, false);
    }

    TrapSong(double bpm, int measures, long seed, boolean beatOnly) {
        this(bpm, measures, seed, beatOnly, false);
    }

    /**
     * @param busyHats hi-hats on every sixteenth instead of every eighth: the busy top line
     *     that makes a slow rap beat read at twice its tempo
     */
    TrapSong(double bpm, int measures, long seed, boolean beatOnly, boolean busyHats) {
        this.bpm = bpm;
        this.measures = measures;
        Random rnd = new Random(seed);
        double beat = 60.0 / bpm, six = beat / 4;
        double offset = 0.5;
        int n = (int) ((offset + measures * 4 * beat + 2) * sampleRate);
        drums = new float[n];
        bass = new float[n];
        loop = new float[n];
        vocals = new float[n];
        audio = new float[n];
        // The loop: an arpeggio in eighth notes, four notes per half measure, played as one
        // continuous tone that slides to each new pitch (no attack, no dip in level).
        int[][] arps = {{69, 76, 73, 76}, {66, 73, 69, 73}, {62, 69, 66, 69}, {64, 71, 68, 71}};
        double ph = 0, f0 = hz(69);
        int loopStart = (int) (offset * sampleRate), loopEnd = (int) ((offset + measures * 4 * beat) * sampleRate);
        for (int i = loopStart; i < loopEnd; i++) {
            double t = (double) i / sampleRate - offset;
            int eighth = (int) Math.floor(t / (beat / 2));
            int bar = eighth / 8;
            int[] arp = arps[bar % arps.length];
            double target = hz(arp[eighth % 4]);
            f0 += (target - f0) * 0.0003; // a glide of some 75 ms, like a flute slurring
            ph += 2 * Math.PI * f0 / sampleRate;
            double fade = Math.min(1, (i - loopStart) / (0.05 * sampleRate)) * Math.min(1, (loopEnd - i) / (0.05 * sampleRate));
            double up = bar >= BREAK_FROM && bar < BREAK_TO ? 2.5 : 1;
            loop[i] = (float) (0.05 * up * fade * (Math.sin(ph) + 0.25 * Math.sin(2 * ph) + 0.08 * Math.sin(3 * ph)));
        }
        for (int e = 1; e < measures * 8; e++) {
            int[] now = arps[(e / 8) % arps.length], before = arps[((e - 1) / 8) % arps.length];
            if (now[e % 4] != before[(e - 1) % 4]) {
                loopNotes.add(offset + e * beat / 2);
                if (e / 8 >= BREAK_FROM && e / 8 < BREAK_TO) breakNotes.add(offset + e * beat / 2);
            }
        }
        int[] roots = {45, 38, 42, 40};
        for (int m = 0; m < measures; m++) {
            double bar = offset + m * 4 * beat;
            int root = roots[m % roots.length];
            // 808: dotted eighths (3-3-2 sixteenths, twice a measure); every other measure one
            // long note instead, which pumps while it rings.
            if (beatOnly) {
                // The common trap 3-3-3-3-4: dotted eighths across the measure.
                int[] at = {0, 3, 6, 9, 12};
                for (int k = 0; k < at.length; k++) note808(bar + at[k] * six, root, (k == 4 ? 4 : 3) * six * 0.95, 0);
            } else if (m % 2 == 0) {
                int[] at = {0, 3, 6, 8, 11, 14};
                for (int s : at) note808(bar + s * six, root, (s == 6 ? 2 : s == 14 ? 1.5 : 3) * six * 0.95, 0);
                // A double hit: one more a 32nd note before the next measure's first hit.
                if (m + 1 < measures) {
                    note808(bar + 15.5 * six, root, 0.5 * six * 0.9, 0);
                    doubleHits.add(bar + 15.5 * six);
                    doubleHits.add(bar + 16 * six);
                }
            } else if (m % 4 == 1) {
                // The same pattern on an 808 that keeps ringing: each hit lands on the last
                // note at 70% of its strength, so the sound only gets 3 dB louder.
                int[] at = {0, 3, 6, 8, 11, 14, 16};
                for (int k = 0; k + 1 < at.length; k++) {
                    note808(bar + at[k] * six, root, (at[k + 1] - at[k]) * six, 2);
                    if (k > 0) {
                        ringingHits.add(bar + at[k] * six);
                        ringingMeasure.add(m);
                    }
                }
            } else {
                // Dotted lead-in, then one long note for the rest of the measure.
                note808(bar, root, 3 * six * 0.95, 0);
                note808(bar + 3 * six, root, 3 * six * 0.95, 0);
                note808(bar + 6 * six, root, 10 * six * 0.95, 1);
            }
            for (int b = 1; b < 4; b += 2) clap(bar + b * beat);
            for (int s = 0; s < 16; s += busyHats ? 1 : 2) hat(bar + s * six, rnd);
            // Vocals: a sung / rapped line over the second half of the song, louder than the loop.
            if (m >= 2) {
                // On offbeat sixteenths, so the loop's eighth notes are not hidden behind vocal notes.
                int[] line = {69, 71, 73, 71, 69, 66, 69, 64};
                int[] at = {1, 3, 7, 9, 11, 15};
                for (int k = 0; k < at.length; k++) voice(bar + at[k] * six, line[(k + m) % 8], six * 1.6);
            }
        }
        if (beatOnly) {
            java.util.Arrays.fill(loop, 0f);
            java.util.Arrays.fill(vocals, 0f);
        }
        for (int i = 0; i < n; i++) audio[i] = drums[i] + bass[i] + loop[i] + vocals[i] + (float) ((rnd.nextDouble() * 2 - 1) * 0.0015);
    }

    /** The song is tuned this many semitones flat of A = 440 Hz. */
    static final double DETUNE = 0.3;

    private static double hz(int midi) {
        return 440 * Math.pow(2, (midi - DETUNE - 69) / 12.0);
    }

    /**
     * @param kind 0: a short note that fades; 1: a long sustained note with a slight flutter;
     *     2: a note that rings at nearly full strength right up to the next hit
     */
    private void note808(double t, int midi, double len, int kind) {
        bassNotes.add(t);
        bassPitch.add(midi);
        if (kind == 1) longBass.add(new double[] {t, len});
        int start = (int) Math.round(t * sampleRate), count = (int) (len * sampleRate);
        double f = hz(midi), ph = 0;
        for (int i = 0; i < count && start + i < bass.length; i++) {
            double sec = (double) i / sampleRate;
            double env = Math.min(1, sec / 0.004);
            if (kind == 0) env *= Math.exp(-1.5 * sec / len) * Math.min(1, (count - i) / (0.02 * sampleRate));
            // The slight flutter of a real 808 (ripples of about 1 dB, nine a second).
            if (kind == 1) env *= (1 + 0.06 * Math.sin(2 * Math.PI * 9 * sec)) * Math.min(1, (count - i) / (0.02 * sampleRate));
            if (kind == 2) env *= Math.exp(-0.36 * sec / len);
            // An 808 drops from an octave above onto its note in the first ~80 ms.
            double drop = Math.pow(2, Math.exp(-sec / 0.025));
            ph += 2 * Math.PI * f * drop / sampleRate;
            double v = Math.tanh(2.5 * Math.sin(ph)) * 0.8 + 0.2 * Math.sin(2 * ph);
            bass[start + i] += (float) (0.3 * env * v);
        }
        // The kick on top of every 808: a click and a fast drop in pitch, mixed into the 808.
        Random click = new Random(start);
        for (int i = 0; i < (int) (0.03 * sampleRate) && start + i < bass.length; i++) {
            double sec = (double) i / sampleRate;
            bass[start + i] += (float) (0.35 * Math.exp(-sec / 0.006) * ((click.nextDouble() * 2 - 1) * 0.6 + Math.sin(2 * Math.PI * (60 + 400 * Math.exp(-sec / 0.004)) * sec)));
        }
    }

    private void clap(double t) {
        claps.add(t);
        int start = (int) Math.round(t * sampleRate), count = (int) (0.15 * sampleRate);
        Random r = new Random(start);
        for (int i = 0; i < count && start + i < drums.length; i++) {
            double sec = (double) i / sampleRate;
            drums[start + i] += (float) (0.3 * Math.exp(-sec / 0.04) * (r.nextDouble() * 2 - 1));
        }
    }

    private void hat(double t, Random rnd) {
        int start = (int) Math.round(t * sampleRate), count = (int) (0.03 * sampleRate);
        double prev = 0;
        for (int i = 0; i < count && start + i < drums.length; i++) {
            double w = rnd.nextDouble() * 2 - 1, hp = w - prev;
            prev = w;
            drums[start + i] += (float) (0.012 * Math.exp(-(double) i / sampleRate / 0.008) * hp);
        }
    }

    private void voice(double t, int midi, double len) {
        sung.add(t);
        int start = (int) Math.round(t * sampleRate), count = (int) (len * sampleRate);
        double f = hz(midi), ph = 0;
        for (int i = 0; i < count && start + i < vocals.length; i++) {
            double sec = (double) i / sampleRate;
            double env = Math.min(1, sec / 0.02) * Math.min(1, (count - i) / (0.03 * sampleRate));
            ph += 2 * Math.PI * f * Math.pow(2, 0.25 * Math.sin(2 * Math.PI * 5.5 * sec) / 12) / sampleRate;
            vocals[start + i] += (float) (0.14 * env * (Math.sin(ph) + 0.5 * Math.sin(2 * ph) + 0.25 * Math.sin(3 * ph)));
        }
    }
}
