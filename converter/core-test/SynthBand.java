import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/**
 * A synthetic "band" recording with known answers: drums, bass, distorted rhythm guitar, a lead
 * line with vibrato, repeated verse/chorus sections and optional tempo drift. It is nowhere near
 * a real record, but unlike a real record we know exactly where every guitar note, beat and
 * downbeat is, which lets the chart engine be scored instead of eyeballed.
 */
final class SynthBand {
    static final class GuitarNote {
        final double time;
        final int midi;
        final boolean chord;
        final int measure;

        GuitarNote(double time, int midi, boolean chord, int measure) {
            this.time = time;
            this.midi = midi;
            this.chord = chord;
            this.measure = measure;
        }
    }

    final int sampleRate;
    final int beatsPerMeasure;
    float[] audio;
    final List<GuitarNote> guitar = new ArrayList<>();
    final List<Double> beats = new ArrayList<>();
    /** Start time of each measure, plus one extra entry for the end. */
    final List<Double> measures = new ArrayList<>();
    /** Section letter per measure: I intro, V verse, C chorus, O outro. */
    final List<Character> sectionOf = new ArrayList<>();
    /** First measure of each section, in order, with its letter. */
    final List<int[]> sectionStarts = new ArrayList<>();

    private final Random rnd;
    private float[] drums, bass, gtr, pad;
    private double[] beatTime;

    /** Busier arrangement: sixteenth-note hi-hats, a sung melody over the verse, a fast riff. */
    private final boolean busy;

    SynthBand(int sampleRate, double bpm, double drift, double offset, int beatsPerMeasure, long seed) {
        this(sampleRate, bpm, drift, offset, beatsPerMeasure, seed, false);
    }

    SynthBand(int sampleRate, double bpm, double drift, double offset, int beatsPerMeasure, long seed, boolean busy) {
        this.busy = busy;
        this.sampleRate = sampleRate;
        this.beatsPerMeasure = beatsPerMeasure;
        this.rnd = new Random(seed);
        String form = beatsPerMeasure == 4 ? "IIIIVVVVVVVVCCCCCCCCVVVVVVVVCCCCCCCCOO" : "IIIIVVVVVVVVCCCCCCCCVVVVVVVVCCCCCCCCOO";
        int bars = form.length();
        int totalBeats = bars * beatsPerMeasure + 2;
        beatTime = new double[totalBeats + 1];
        double t = offset;
        for (int i = 0; i <= totalBeats; i++) {
            beatTime[i] = t;
            double local = bpm * (1 + drift * Math.sin(2 * Math.PI * i / 48.0));
            t += 60.0 / local;
        }
        for (int i = 0; i < bars * beatsPerMeasure; i++) beats.add(beatTime[i]);
        for (int b = 0; b <= bars; b++) measures.add(beatTime[b * beatsPerMeasure]);
        int n = (int) ((beatTime[totalBeats] + 1.0) * sampleRate);
        audio = new float[n];
        drums = new float[n];
        bass = new float[n];
        gtr = new float[n];
        pad = new float[n];

        char prev = 0;
        int sectionBar = 0;
        for (int b = 0; b < bars; b++) {
            char c = form.charAt(b);
            if (c != prev) {
                sectionStarts.add(new int[] {b, c});
                sectionBar = 0;
                prev = c;
            }
            sectionOf.add(c);
            if (beatsPerMeasure == 4) bar44(b, sectionBar, c);
            else bar34(b, sectionBar, c);
            sectionBar++;
        }
        // Guitar goes through an amp: soft clipping adds the gritty overtones of distortion.
        for (int i = 0; i < n; i++) {
            float g = (float) Math.tanh(gtr[i] * 2.2) * 0.33f;
            audio[i] = drums[i] + bass[i] + g + pad[i] + (float) ((rnd.nextDouble() * 2 - 1) * 0.0015);
        }
        drums = bass = gtr = pad = null;
    }

    private double at(int bar, double slot16) {
        double beat = bar * beatsPerMeasure + slot16 / 4.0;
        int i = (int) Math.floor(beat);
        double f = beat - i;
        return beatTime[i] + f * (beatTime[i + 1] - beatTime[i]);
    }

    private static final int[] ROOTS = {40, 48, 43, 50}; // E, C, G, D
    private static final int[][] RIFF = {{40, 40, 43, 40, 45, 40, 47, 45}, {40, 40, 43, 40, 50, 47, 45, 43}};
    private static final int[] STRUM = {0, 2, 6, 8, 12, 14};
    // {slot, midi, length in sixteenths}
    private static final int[][][] LEAD = {
        {{0, 64, 4}, {4, 67, 2}, {6, 69, 2}, {8, 71, 4}, {12, 69, 2}, {14, 67, 2}},
        {{0, 66, 6}, {6, 64, 2}, {8, 62, 4}, {12, 64, 4}}};

    private void bar44(int bar, int sectionBar, char section) {
        double sixteenth = (at(bar, 4) - at(bar, 0)) / 4;
        int root = ROOTS[sectionBar % 4];
        if (section == 'I') {
            int[] riff = RIFF[sectionBar % 2];
            if (busy && sectionBar % 2 == 1) {
                // Second bar of the riff doubles up into sixteenths.
                for (int i = 0; i < 16; i++) guitarNote(bar, i, riff[i / 2] + (i % 2 == 1 ? 12 : 0), sixteenth * 0.9, false, 0.5f);
            } else {
                for (int i = 0; i < 8; i++) guitarNote(bar, i * 2, riff[i], sixteenth * 1.8, false, 0.5f);
            }
            if (sectionBar >= 2) for (int i = 0; i < 8; i++) hat(at(bar, i * 2));
        } else if (section == 'V') {
            kick(at(bar, 0), 1f);
            kick(at(bar, 8), 0.8f);
            if (sectionBar % 2 == 1) kick(at(bar, 10), 0.6f);
            snare(at(bar, 4));
            snare(at(bar, 12));
            for (int i = 0; i < 8; i++) {
                hat(at(bar, i * 2));
                bassNote(at(bar, i * 2), root - 12, sixteenth * 1.7);
            }
            for (int s : STRUM) guitarNote(bar, s, root, sixteenth * (s == 2 || s == 8 ? 3.6 : 1.8), true, 0.45f);
            if (busy) {
                for (int i = 0; i < 8; i++) hat(at(bar, i * 2 + 1));
                noise(drums, at(bar, 7), 0.05, 0.1f, false);
                int[] sung = {root + 24, root + 27, root + 31, root + 29};
                for (int i = 0; i < 4; i++) voice(at(bar, i * 4 + (i == 1 ? 1 : 0)), sung[(i + sectionBar) % 4], sixteenth * 3.5);
            }
        } else if (section == 'C') {
            if (sectionBar == 0) crash(at(bar, 0));
            kick(at(bar, 0), 1f);
            kick(at(bar, 6), 0.7f);
            kick(at(bar, 8), 0.85f);
            snare(at(bar, 4));
            snare(at(bar, 12));
            for (int i = 0; i < 8; i++) hat(at(bar, i * 2));
            for (int i = 0; i < 4; i++) bassNote(at(bar, i * 4), root - 12, sixteenth * 3.6);
            padChord(at(bar, 0), root + 12, sixteenth * 16);
            for (int[] nt : LEAD[sectionBar % 2]) guitarLead(bar, nt[0], nt[1], sixteenth * nt[2] * 0.95);
        } else {
            if (sectionBar == 0) {
                crash(at(bar, 0));
                kick(at(bar, 0), 1f);
                guitarNote(bar, 0, 40, sixteenth * 30, true, 0.5f);
                bassNote(at(bar, 0), 28, sixteenth * 30);
            }
        }
    }

    private void bar34(int bar, int sectionBar, char section) {
        double sixteenth = (at(bar, 4) - at(bar, 0)) / 4;
        int root = ROOTS[sectionBar % 4];
        if (section == 'O') {
            if (sectionBar == 0) {
                kick(at(bar, 0), 1f);
                guitarNote(bar, 0, 40, sixteenth * 22, true, 0.5f);
            }
            return;
        }
        // Waltz: bass and kick on one, chord strums on two and three, a melody on top.
        kick(at(bar, 0), 1f);
        bassNote(at(bar, 0), root - 12, sixteenth * 3.5);
        hat(at(bar, 4));
        hat(at(bar, 8));
        if (section != 'I') {
            snare(at(bar, 4));
            snare(at(bar, 8));
        }
        padChord(at(bar, 0), root + 12, sixteenth * 12);
        guitarNote(bar, 0, root, sixteenth * 3.6, true, 0.45f);
        guitarNote(bar, 4, root, sixteenth * 1.8, true, 0.35f);
        guitarNote(bar, 8, root, sixteenth * 1.8, true, 0.35f);
        if (section == 'C') {
            int[] mel = {64, 67, 71, 69, 67, 66};
            guitarLead(bar, 2, mel[(sectionBar * 2) % 6], sixteenth * 3.5);
            guitarLead(bar, 6, mel[(sectionBar * 2 + 1) % 6], sixteenth * 5);
        }
    }

    // ---------------------------------------------------------------- instruments

    private static double hz(double midi) {
        return 440.0 * Math.pow(2, (midi - 69) / 12.0);
    }

    private void guitarNote(int bar, int slot, int midi, double len, boolean chord, float amp) {
        double t = at(bar, slot);
        guitar.add(new GuitarNote(t, midi, chord, bar));
        string(gtr, t, midi, len, amp, 0);
        if (chord) {
            string(gtr, t + 0.004, midi + 7, len, amp * 0.85f, 0);
            string(gtr, t + 0.008, midi + 12, len, amp * 0.7f, 0);
        }
        // Pick attack.
        noise(gtr, t, 0.006, 0.12f, false);
    }

    private void guitarLead(int bar, int slot, int midi, double len) {
        double t = at(bar, slot);
        guitar.add(new GuitarNote(t, midi, false, bar));
        string(gtr, t, midi, len, 0.5f, len > 0.3 ? 0.25 : 0);
        noise(gtr, t, 0.005, 0.08f, false);
    }

    /** A plucked string: eight harmonics, quick attack, slow decay, optional vibrato (in semitones). */
    private void string(float[] out, double t, double midi, double len, float amp, double vibrato) {
        int start = (int) Math.round(t * sampleRate);
        int count = (int) (len * sampleRate);
        double f = hz(midi);
        double[] phase = new double[8];
        for (int i = 0; i < count && start + i < out.length; i++) {
            double sec = (double) i / sampleRate;
            double env = Math.min(1.0, sec / 0.004) * Math.exp(-1.6 * sec / Math.max(0.2, len));
            env *= Math.min(1.0, (count - i) / (0.012 * sampleRate));
            double fNow = f * Math.pow(2, vibrato * Math.sin(2 * Math.PI * 5.5 * sec) * Math.min(1, sec / 0.25) / 12.0);
            double v = 0;
            for (int h = 0; h < 8; h++) {
                phase[h] += 2 * Math.PI * fNow * (h + 1) / sampleRate;
                if (fNow * (h + 1) < sampleRate * 0.45) v += Math.sin(phase[h]) / Math.pow(h + 1, 0.9);
            }
            out[start + i] += (float) (amp * env * v * 0.45);
        }
    }

    /** A sung note: soft attack, vibrato, few overtones. Not part of the guitar's "right answers". */
    private void voice(double t, int midi, double len) {
        int start = (int) Math.round(t * sampleRate);
        int count = (int) (len * sampleRate);
        double f = hz(midi);
        double ph = 0;
        for (int i = 0; i < count && start + i < pad.length; i++) {
            double sec = (double) i / sampleRate;
            double env = Math.min(1.0, sec / 0.035) * Math.min(1.0, (count - i) / (0.04 * sampleRate));
            ph += 2 * Math.PI * f * Math.pow(2, 0.3 * Math.sin(2 * Math.PI * 5.2 * sec) / 12.0) / sampleRate;
            pad[start + i] += (float) (0.16 * env * (Math.sin(ph) + 0.45 * Math.sin(2 * ph) + 0.2 * Math.sin(3 * ph)));
        }
    }

    private void bassNote(double t, int midi, double len) {
        int start = (int) Math.round(t * sampleRate);
        int count = (int) (len * sampleRate);
        double f = hz(midi);
        for (int i = 0; i < count && start + i < bass.length; i++) {
            double sec = (double) i / sampleRate;
            double env = Math.min(1.0, sec / 0.006) * Math.exp(-2.0 * sec / len) * Math.min(1.0, (count - i) / (0.01 * sampleRate));
            double ph = 2 * Math.PI * f * sec;
            double v = Math.sin(ph) + 0.5 * Math.sin(2 * ph) + 0.3 * Math.sin(3 * ph) + 0.12 * Math.sin(4 * ph);
            bass[start + i] += (float) (0.22 * env * v);
        }
    }

    private void padChord(double t, int root, double len) {
        int[] tones = {root, root + (root % 12 == 4 ? 3 : 4), root + 7};
        int start = (int) Math.round(t * sampleRate);
        int count = (int) (len * sampleRate);
        for (int tone : tones) {
            double f = hz(tone);
            for (int i = 0; i < count && start + i < pad.length; i++) {
                double sec = (double) i / sampleRate;
                double env = Math.min(1.0, sec / 0.06) * Math.min(1.0, (count - i) / (0.06 * sampleRate));
                pad[start + i] += (float) (0.035 * env * (Math.sin(2 * Math.PI * f * sec) + 0.4 * Math.sin(4 * Math.PI * f * sec)));
            }
        }
    }

    private void kick(double t, float amp) {
        int start = (int) Math.round(t * sampleRate);
        int count = (int) (0.16 * sampleRate);
        double ph = 0;
        for (int i = 0; i < count && start + i < drums.length; i++) {
            double sec = (double) i / sampleRate;
            double f = 48 + 90 * Math.exp(-sec / 0.02);
            ph += 2 * Math.PI * f / sampleRate;
            drums[start + i] += (float) (0.6 * amp * Math.exp(-sec / 0.05) * Math.sin(ph));
        }
        noise(drums, t, 0.006, 0.25f * amp, false);
    }

    private void snare(double t) {
        noise(drums, t, 0.16, 0.3f, false);
        int start = (int) Math.round(t * sampleRate);
        int count = (int) (0.1 * sampleRate);
        for (int i = 0; i < count && start + i < drums.length; i++) {
            double sec = (double) i / sampleRate;
            drums[start + i] += (float) (0.25 * Math.exp(-sec / 0.03) * Math.sin(2 * Math.PI * 190 * sec));
        }
    }

    private void hat(double t) {
        noise(drums, t, 0.04, 0.09f, true);
    }

    private void crash(double t) {
        noise(drums, t, 1.2, 0.16f, true);
    }

    private void noise(float[] out, double t, double len, float amp, boolean bright) {
        int start = (int) Math.round(t * sampleRate);
        int count = (int) (len * sampleRate);
        double prev = 0;
        for (int i = 0; i < count && start + i < out.length; i++) {
            double w = rnd.nextDouble() * 2 - 1;
            double v = bright ? w - prev : w;
            prev = w;
            out[start + i] += (float) (amp * Math.exp(-4.5 * i / count) * v);
        }
    }
}
