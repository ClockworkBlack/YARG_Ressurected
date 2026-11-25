package com.clockworkblack.yargconverter.core;

import java.util.Arrays;

/**
 * Listens to a voice on its own (the separated vocals) and writes down, a hundred times a
 * second, how loud it is, which note is being sung, and the colour of the voice.
 *
 * Feed mono samples as they arrive; only a few numbers per frame are kept, so the vocals of a
 * whole song never have to sit in memory. The result is what {@link VocalCharter} turns into
 * the notes a singer follows in the game.
 */
public final class VocalAnalyzer {
    /** Lowest and highest notes looked for: about C#2 (a deep rap voice) to past C6 (a high belt). */
    private static final double MIN_HZ = 70.0, MAX_HZ = 1250.0;
    /** A frame counts as a clear tone as soon as its "how unlike itself one cycle later" score dips this low. */
    private static final float DIP_BELOW = 0.12f;
    /** ...and as some kind of tone at all up to here. Above this it is breath, consonants or noise. */
    private static final float VOICED_BELOW = 0.30f;
    /** Bands the colour of the voice is measured in. */
    public static final int BANDS = 14;
    private static final double TIMBRE_LO_HZ = 150.0, TIMBRE_HI_HZ = 5000.0;

    /** What was heard, frame by frame. */
    public static final class Voice {
        /** Frames per second (about 100). */
        public final double fps;
        /** Length of the audio in seconds. */
        public final double duration;
        /** Seconds from the start of the audio to the middle of frame 0. */
        public final double frameOffset;
        /** The note being sung, as a MIDI note number with fractions (69 = A 440 Hz); NaN when no note is heard. */
        public final float[] midi;
        /** 0..1: how clean that note is (1 = a pure steady tone). */
        public final float[] clarity;
        /** Loudness of each frame. */
        public final float[] level;
        /** Colour of the voice: frames x {@link #BANDS} band strengths, with overall loudness taken out. */
        public final float[] timbre;

        Voice(double fps, double duration, double frameOffset, float[] midi, float[] clarity, float[] level, float[] timbre) {
            this.fps = fps;
            this.duration = duration;
            this.frameOffset = frameOffset;
            this.midi = midi;
            this.clarity = clarity;
            this.level = level;
            this.timbre = timbre;
        }

        public int frames() {
            return level.length;
        }

        public double timeOf(double frame) {
            return frameOffset + frame / fps;
        }

        /** Frame nearest to a time, kept inside the audio. */
        public int frameAt(double time) {
            int f = (int) Math.round((time - frameOffset) * fps);
            return Math.max(0, Math.min(frames() - 1, f));
        }

        /**
         * The loudness above which the voice counts as "singing right now". It is set against how
         * loud this voice gets, so the faint leftovers of other instruments that a separation
         * always leaves behind stay below it.
         */
        public float gate() {
            int n = frames();
            if (n == 0) return 1e-4f;
            float[] sorted = level.clone();
            Arrays.sort(sorted);
            float loud = sorted[(int) Math.min(n - 1, Math.round(n * 0.97))];
            return Math.max(1e-4f, loud * 0.07f);
        }
    }

    private final int sampleRate;
    /** Only every Nth sample is kept (after filtering): voices need no detail above 5 kHz. */
    private final int decimate;
    private final double rate;
    private final float[] fir;
    private final float[] ring;
    private int ringPos;
    private int phase;

    private final int frameSize;
    private final int hop;
    private final int tauMin, tauMax, window;
    private final float[] frame;
    private int filled;
    private long totalSamples;

    private final double[] squares;
    private final float[] cmnd;
    private final Fft fft;
    private final float[] hann;
    private final float[] re, im;
    private final int[] bandOfBin;
    private final int fftSize;

    private float[] midi = new float[4096];
    private float[] clarity = new float[4096];
    private float[] level = new float[4096];
    private float[] timbre = new float[4096 * BANDS];
    private int count;

    public VocalAnalyzer(int sampleRate) {
        this.sampleRate = sampleRate;
        decimate = Math.max(1, (int) Math.round(sampleRate / 11025.0));
        rate = (double) sampleRate / decimate;

        // Low-pass filter in front of the thinning-out, so nothing folds back as a false tone.
        int taps = decimate == 1 ? 1 : 12 * decimate + 1;
        fir = new float[taps];
        if (taps == 1) {
            fir[0] = 1f;
        } else {
            double cutoff = 0.45 / decimate;
            double sum = 0;
            int mid = (taps - 1) / 2;
            for (int i = 0; i < taps; i++) {
                int k = i - mid;
                double sinc = k == 0 ? 2 * cutoff : Math.sin(2 * Math.PI * cutoff * k) / (Math.PI * k);
                double w = 0.5 - 0.5 * Math.cos(2 * Math.PI * i / (taps - 1));
                fir[i] = (float) (sinc * w);
                sum += fir[i];
            }
            for (int i = 0; i < taps; i++) fir[i] /= (float) sum;
        }
        ring = new float[taps * 2];

        // 46 ms of sound at a time: room for more than three cycles of the lowest note.
        frameSize = Math.max(64, (int) Math.round(rate * 0.046));
        hop = Math.max(1, (int) Math.round(rate / 100.0));
        tauMin = Math.max(2, (int) Math.floor(rate / MAX_HZ));
        tauMax = Math.min(frameSize / 2, (int) Math.ceil(rate / MIN_HZ));
        window = frameSize - tauMax;
        frame = new float[frameSize];
        squares = new double[frameSize + 1];
        cmnd = new float[tauMax + 1];

        int size = 256;
        while (size < frameSize) size <<= 1;
        fftSize = size;
        fft = new Fft(fftSize);
        hann = new float[frameSize];
        for (int i = 0; i < frameSize; i++) hann[i] = (float) (0.5 - 0.5 * Math.cos(2 * Math.PI * i / frameSize));
        re = new float[fftSize];
        im = new float[fftSize];
        // Bands evenly spaced on a pitch-like (mel) scale: narrow where voices differ most.
        int bins = fftSize / 2;
        bandOfBin = new int[bins];
        double hi = Math.min(TIMBRE_HI_HZ, rate * 0.48);
        double melLo = mel(TIMBRE_LO_HZ), melHi = mel(hi);
        for (int b = 0; b < bins; b++) {
            double hz = b * rate / fftSize;
            if (hz < TIMBRE_LO_HZ || hz >= hi) {
                bandOfBin[b] = -1;
                continue;
            }
            double pos = (mel(hz) - melLo) / (melHi - melLo) * BANDS;
            bandOfBin[b] = Math.min(BANDS - 1, (int) pos);
        }
    }

    private static double mel(double hz) {
        return 2595.0 * Math.log10(1.0 + hz / 700.0);
    }

    /** Adds more of the voice. Any block size works. */
    public void feed(float[] samples, int offset, int length) {
        int taps = fir.length;
        for (int i = 0; i < length; i++) {
            float x = samples[offset + i];
            totalSamples++;
            if (taps == 1) {
                push(x);
                continue;
            }
            ring[ringPos] = x;
            ring[ringPos + taps] = x;
            ringPos++;
            if (ringPos == taps) ringPos = 0;
            if (++phase < decimate) continue;
            phase = 0;
            // The copy in the second half of the ring makes the last "taps" samples one straight run.
            float acc = 0f;
            for (int k = 0; k < taps; k++) acc += ring[ringPos + k] * fir[k];
            push(acc);
        }
    }

    private void push(float x) {
        frame[filled++] = x;
        if (filled < frameSize) return;
        analyzeFrame();
        System.arraycopy(frame, hop, frame, 0, frameSize - hop);
        filled = frameSize - hop;
    }

    private void analyzeFrame() {
        if (count == level.length) {
            int cap = count * 2;
            midi = Arrays.copyOf(midi, cap);
            clarity = Arrays.copyOf(clarity, cap);
            level = Arrays.copyOf(level, cap);
            timbre = Arrays.copyOf(timbre, cap * BANDS);
        }

        squares[0] = 0;
        for (int i = 0; i < frameSize; i++) squares[i + 1] = squares[i] + (double) frame[i] * frame[i];
        float rms = (float) Math.sqrt(squares[frameSize] / frameSize);
        // Loudness is taken from the middle half of the frame only, so that the short gaps
        // between words (a few hundredths of a second) show up as gaps.
        int quarter = frameSize / 4;
        level[count] = (float) Math.sqrt((squares[frameSize - quarter] - squares[quarter]) / (frameSize - 2 * quarter));
        midi[count] = Float.NaN;
        clarity[count] = 0f;

        if (rms > 1e-5f) {
            // How unlike itself the sound is when slid along by each possible cycle length:
            // near zero at the true cycle length of a sung note (the "YIN" method).
            double e0 = squares[window];
            double running = 0;
            cmnd[0] = 1f;
            for (int tau = 1; tau <= tauMax; tau++) {
                double r = 0;
                for (int j = 0; j < window; j++) r += frame[j] * frame[j + tau];
                double d = e0 + (squares[tau + window] - squares[tau]) - 2 * r;
                if (d < 0) d = 0;
                running += d;
                cmnd[tau] = running > 0 ? (float) (d * tau / running) : 1f;
            }
            int best = -1;
            for (int tau = tauMin; tau < tauMax; tau++) {
                if (cmnd[tau] < DIP_BELOW) {
                    while (tau + 1 < tauMax && cmnd[tau + 1] < cmnd[tau]) tau++;
                    best = tau;
                    break;
                }
            }
            if (best < 0) {
                best = tauMin;
                for (int tau = tauMin + 1; tau < tauMax; tau++) if (cmnd[tau] < cmnd[best]) best = tau;
            }
            float dip = cmnd[best];
            if (dip < VOICED_BELOW && best > tauMin && best < tauMax - 1) {
                // The true cycle length is rarely a whole number of samples: fit a curve
                // through the dip and its neighbours to find where the bottom really is.
                double a = cmnd[best - 1], b = cmnd[best], c = cmnd[best + 1];
                double denom = a - 2 * b + c;
                double shift = denom > 1e-12 ? 0.5 * (a - c) / denom : 0;
                if (shift > 1) shift = 1;
                if (shift < -1) shift = -1;
                double hz = rate / (best + shift);
                midi[count] = (float) (69.0 + 12.0 * Math.log(hz / 440.0) / Math.log(2.0));
                clarity[count] = Math.max(0f, 1f - dip);
            }
        }

        // Colour of the voice: how the sound is spread over the bands, loudness taken out.
        for (int i = 0; i < fftSize; i++) {
            re[i] = i < frameSize ? frame[i] * hann[i] : 0f;
            im[i] = 0f;
        }
        fft.transform(re, im);
        int base = count * BANDS;
        for (int b = 0; b < BANDS; b++) timbre[base + b] = 0f;
        for (int b = 0; b < bandOfBin.length; b++) {
            int band = bandOfBin[b];
            if (band >= 0) timbre[base + band] += re[b] * re[b] + im[b] * im[b];
        }
        float mean = 0f;
        for (int b = 0; b < BANDS; b++) {
            timbre[base + b] = (float) Math.log(timbre[base + b] + 1e-12);
            mean += timbre[base + b];
        }
        mean /= BANDS;
        for (int b = 0; b < BANDS; b++) timbre[base + b] -= mean;
        count++;
    }

    /** Call once at the end. */
    public Voice finish() {
        // Flush what is left with silence so the last word is not cut short.
        int pad = filled > 0 ? frameSize - filled : 0;
        for (int i = 0; i < pad && filled > 0; i++) push(0f);

        float[] m = Arrays.copyOf(midi, count);
        float[] smoothed = m.clone();
        // A single frame that jumps an octave (the classic slip of any pitch tracker) is
        // replaced by the middle value of its neighbours.
        float[] five = new float[5];
        for (int i = 2; i < count - 2; i++) {
            if (Float.isNaN(m[i])) continue;
            int k = 0;
            for (int j = i - 2; j <= i + 2; j++) if (!Float.isNaN(m[j])) five[k++] = m[j];
            if (k < 3) continue;
            Arrays.sort(five, 0, k);
            float median = five[k / 2];
            if (Math.abs(m[i] - median) > 5f) smoothed[i] = median;
        }
        double fps = rate / hop;
        double offset = (frameSize / 2.0) / rate - (fir.length - 1) / (2.0 * sampleRate);
        return new Voice(fps, (double) totalSamples / sampleRate, offset, smoothed,
                Arrays.copyOf(clarity, count), Arrays.copyOf(level, count), Arrays.copyOf(timbre, count * BANDS));
    }
}
