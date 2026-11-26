import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/**
 * A synthetic trap / R&B beat with known answers, for the lessons learned on real rap songs:
 * 808s in dotted eighths (3-3-2), which make the beat look a third faster than it is; long 808
 * notes that pump while they ring; claps and hi-hats; a rapped / sung vocal line; and a soft
 * flute-like loop that glides from note to note without attacks, quieter than the vocals.
 * Like the real songs it is built after, the 808 dominates and the drums are faint (which is
 * what lets the dotted 808 pass for the beat), the 808 has a kick click on each note (its pitch
 * only settles after it drops from above onto its note), it pumps and buzzes while it rings,
 * and the whole song is tuned 30 cents flat, with a slight wobble. With {@code beatOnly}, just the 808s and the faint drums: the case that fooled the
 * tempo finder.
 */
final class TrapSong {
    final int sampleRate = 44100;
    final double bpm;
    final float[] audio, drums, bass, loop, vocals;
    /** Start of each loop note, each 808 note (with its MIDI note), each clap, each sung note. */
    final List<Double> loopNotes = new ArrayList<>(), bassNotes = new ArrayList<>(), claps = new ArrayList<>(), sung = new ArrayList<>();
    final List<Integer> bassPitch = new ArrayList<>();
    /** Long 808 notes: start and how long they ring. */
    final List<double[]> longBass = new ArrayList<>();
    final int measures;

    TrapSong(double bpm, int measures, long seed) {
        this(bpm, measures, seed, false);
    }

    TrapSong(double bpm, int measures, long seed, boolean beatOnly) {
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
            loop[i] = (float) (0.05 * fade * (Math.sin(ph) + 0.25 * Math.sin(2 * ph) + 0.08 * Math.sin(3 * ph)));
        }
        for (int e = 1; e < measures * 8; e++) {
            int[] now = arps[(e / 8) % arps.length], before = arps[((e - 1) / 8) % arps.length];
            if (now[e % 4] != before[(e - 1) % 4]) loopNotes.add(offset + e * beat / 2);
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
                for (int k = 0; k < at.length; k++) note808(bar + at[k] * six, root, (k == 4 ? 4 : 3) * six * 0.95, false);
            } else if (m % 2 == 0) {
                int[] at = {0, 3, 6, 8, 11, 14};
                for (int s : at) note808(bar + s * six, root, (s == 6 || s == 14 ? 2 : 3) * six * 0.95, false);
            } else {
                // Dotted lead-in, then one long note for the rest of the measure.
                note808(bar, root, 3 * six * 0.95, false);
                note808(bar + 3 * six, root, 3 * six * 0.95, false);
                note808(bar + 6 * six, root, 10 * six * 0.95, true);
            }
            for (int b = 1; b < 4; b += 2) clap(bar + b * beat);
            for (int s = 0; s < 16; s += 2) hat(bar + s * six, rnd);
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

    private void note808(double t, int midi, double len, boolean pump) {
        bassNotes.add(t);
        bassPitch.add(midi);
        if (pump) longBass.add(new double[] {t, len});
        int start = (int) Math.round(t * sampleRate), count = (int) (len * sampleRate);
        double f = hz(midi), ph = 0;
        for (int i = 0; i < count && start + i < bass.length; i++) {
            double sec = (double) i / sampleRate;
            double env = Math.min(1, sec / 0.004) * (pump ? 1 : Math.exp(-1.5 * sec / len)) * Math.min(1, (count - i) / (0.02 * sampleRate));
            // A pumping, buzzing tone: the level swells some 2 dB a few times a second (as the
            // real 808s measured), and the distortion's upper harmonics flutter.
            double buzz = 0;
            if (pump) {
                env *= 1 + 0.12 * Math.sin(2 * Math.PI * 4.3 * sec);
                buzz = 0.5 + 0.5 * Math.sin(2 * Math.PI * 7.1 * sec);
            }
            // An 808 drops from an octave above onto its note in the first ~80 ms, and wobbles a
            // little (8 cents) after that.
            double drop = Math.pow(2, Math.exp(-sec / 0.025) + 0.08 / 12 * Math.sin(2 * Math.PI * 3.1 * sec));
            ph += 2 * Math.PI * f * drop / sampleRate;
            double v = Math.tanh((2.5 + 3 * buzz) * Math.sin(ph)) * 0.8 + 0.2 * Math.sin(2 * ph);
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
