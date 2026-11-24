package com.clockworkblack.yargconverter.core;

import java.util.Arrays;

/**
 * Streaming audio analysis. Feed mono samples as they are decoded; only a few numbers per
 * frame are kept, so a whole song never has to sit in memory.
 *
 * For every frame (about 86 per second) it measures:
 *  - how much new sound arrived, split into pitched tones and noisy bursts (drums);
 *  - the pitch of the tone that just started, and whether more than one note started (a chord);
 *  - which of the 12 pitch classes are sounding (the harmony), and the loudness.
 */
public final class AudioAnalyzer {
    private static final double LOW_BAND_HZ = 180.0;
    private static final double TONAL_LO_HZ = 150.0, TONAL_HI_HZ = 3000.0;
    private static final double PITCH_LO_HZ = 65.0, PITCH_HI_HZ = 3200.0;
    /** Lowest pitch considered: D2, the low string of a drop-D guitar. */
    private static final double F0_MIN_HZ = 73.42;
    private static final int F0_STEPS = 24 * 4;
    private static final int MAX_HARMONIC = 8;
    private static final int MAX_PEAKS = 32;
    /** Pitch is measured on every other frame, against the spectrum this many measurements back. */
    private static final int RISE_FRAMES = 3;

    private final int sampleRate;
    private final int frameSize;
    private final int hop;
    private final int bins;
    private final double binHz;
    private final float gain;
    private final float[] window;
    private final float[] buf;
    private final float[] re;
    private final float[] im;
    private final Fft fft;

    // Log-spaced bands (a semitone wide where the FFT allows) for onset detection.
    private final int[] binBand;
    private final int bandCount;
    private final boolean[] bandLow;
    private final boolean[] bandTonal;
    private final float[] bandSemis;
    private final float[] band;
    private final float[] prevBand;
    private final float[] rise;
    private final float[] scratch = new float[32];
    private boolean first = true;

    // Pitch needs finer frequency detail than rhythm does (low guitar and bass notes are only a
    // few Hz apart), so it uses a window twice as long, taken every other frame.
    private final int longSize;
    private final float[] longBuf;
    private final float[] longWindow;
    private final float[] longRe;
    private final float[] longIm;
    private final Fft longFft;
    private final float longGain;
    private final float[][] history;
    private int historyCount;
    private int longFrames;
    private final float[] logMag;
    private final int pitchLoBin, pitchHiBin;
    private final float[] newTone;
    private final float[] cleanTone;
    private final double[] peakHz = new double[MAX_PEAKS];
    private final double[] peakAmp = new double[MAX_PEAKS];
    private final boolean[] peakUsed = new boolean[MAX_PEAKS];
    private final double[] f0Hz = new double[F0_STEPS];
    private final double[] f0Score = new double[F0_STEPS];
    private final double[] harmonicWeight = new double[MAX_HARMONIC + 1];
    private final int[] binPitchClass;

    private int filled;
    private long totalSamples;

    private float[] flux = new float[4096];
    private float[] lowFlux = new float[4096];
    private float[] tonal = new float[4096];
    private float[] perc = new float[4096];
    private float[] pitch = new float[4096];
    private float[] pitchConf = new float[4096];
    private float[] rich = new float[4096];
    private float[] rms = new float[4096];
    private float[] bright = new float[4096];
    private float[] chroma = new float[4096 * 12];
    private float[] toneChroma = new float[4096 * 12];
    private int frames;

    public AudioAnalyzer(int sampleRate) {
        if (sampleRate < 8000) throw new IllegalArgumentException("Unsupported sample rate " + sampleRate);
        this.sampleRate = sampleRate;
        int size = 2048;
        if (sampleRate < 32000) size = 1024;
        if (sampleRate > 64000) size = 4096;
        frameSize = size;
        hop = size / 4;
        bins = size / 2;
        binHz = (double) sampleRate / size;
        // Scale so a full-volume sine reads about 100 before the log.
        gain = 400f / size;
        window = new float[size];
        for (int i = 0; i < size; i++) window[i] = (float) (0.5 - 0.5 * Math.cos(2 * Math.PI * i / size));
        buf = new float[size];
        re = new float[size];
        im = new float[size];
        fft = new Fft(size);
        longSize = size * 2;
        longBuf = new float[longSize];
        longWindow = new float[longSize];
        for (int i = 0; i < longSize; i++) longWindow[i] = (float) (0.5 - 0.5 * Math.cos(2 * Math.PI * i / longSize));
        longRe = new float[longSize];
        longIm = new float[longSize];
        longFft = new Fft(longSize);
        longGain = 400f / longSize;
        logMag = new float[size];
        history = new float[RISE_FRAMES][size];
        newTone = new float[size];
        cleanTone = new float[size];

        // Bands: one per semitone from 40 Hz to 11 kHz, merged where FFT bins are wider than that.
        binBand = new int[bins];
        double top = Math.min(11000.0, sampleRate / 2.0);
        int count = 0;
        int lastSemi = Integer.MIN_VALUE;
        double[] centre = new double[bins];
        for (int k = 0; k < bins; k++) {
            double hz = k * binHz;
            if (hz < 40.0 || hz >= top) {
                binBand[k] = -1;
                continue;
            }
            int semi = (int) Math.floor(12.0 * Math.log(hz / 40.0) / Math.log(2));
            if (semi != lastSemi) {
                lastSemi = semi;
                centre[count] = hz;
                count++;
            }
            binBand[k] = count - 1;
        }
        bandCount = count;
        bandLow = new boolean[count];
        bandTonal = new boolean[count];
        bandSemis = new float[count];
        for (int b = 0; b < count; b++) {
            bandSemis[b] = (float) (12.0 * Math.log(centre[b] / 40.0) / Math.log(2));
            bandLow[b] = centre[b] < LOW_BAND_HZ;
            bandTonal[b] = centre[b] >= TONAL_LO_HZ && centre[b] <= TONAL_HI_HZ;
        }
        band = new float[count];
        prevBand = new float[count];
        rise = new float[count];

        pitchLoBin = Math.max(2, (int) Math.ceil(PITCH_LO_HZ / (binHz / 2)));
        pitchHiBin = Math.min(size - 2, (int) Math.floor(PITCH_HI_HZ / (binHz / 2)));
        for (int c = 0; c < F0_STEPS; c++) f0Hz[c] = F0_MIN_HZ * Math.pow(2, c / 24.0);
        for (int h = 1; h <= MAX_HARMONIC; h++) harmonicWeight[h] = Math.pow(0.84, h - 1);

        binPitchClass = new int[bins];
        for (int k = 0; k < bins; k++) {
            double hz = k * binHz;
            if (hz < 250.0 || hz > 4000.0) {
                binPitchClass[k] = -1;
            } else {
                int midi = (int) Math.round(69 + 12 * Math.log(hz / 440.0) / Math.log(2));
                binPitchClass[k] = ((midi % 12) + 12) % 12;
            }
        }
    }

    public int sampleRate() {
        return sampleRate;
    }

    public void feed(float[] mono, int off, int len) {
        totalSamples += len;
        while (len > 0) {
            int n = Math.min(len, frameSize - filled);
            System.arraycopy(mono, off, buf, filled, n);
            filled += n;
            off += n;
            len -= n;
            if (filled == frameSize) {
                processFrame();
                System.arraycopy(buf, hop, buf, 0, frameSize - hop);
                filled = frameSize - hop;
            }
        }
    }

    private void processFrame() {
        double energy = 0;
        for (int i = 0; i < frameSize; i++) {
            float s = buf[i];
            energy += s * s;
            re[i] = s * window[i];
            im[i] = 0f;
        }
        fft.transform(re, im);

        ensureCapacity();
        Arrays.fill(band, 0f);
        int chromaAt = frames * 12;
        for (int c = 0; c < 12; c++) chroma[chromaAt + c] = 0f;
        for (int k = 1; k < bins; k++) {
            float p = re[k] * re[k] + im[k] * im[k];
            float m = (float) Math.sqrt(p);
            int b = binBand[k];
            if (b >= 0) band[b] += p;
            int pc = binPitchClass[k];
            if (pc >= 0) chroma[chromaAt + pc] += m;
        }
        for (int c = 0; c < 12; c++) chroma[chromaAt + c] = (float) Math.log1p(chroma[chromaAt + c] * gain);

        // New energy per band. Comparing against the loudest neighbouring band of the previous
        // frame keeps a wobbling (vibrato) note from looking like a stream of new notes.
        float total = 0f, low = 0f, tone = 0f, burst = 0f, weighted = 0f;
        for (int b = 0; b < bandCount; b++) band[b] = (float) Math.log1p(Math.sqrt(band[b]) * gain);
        if (!first) {
            for (int b = 0; b < bandCount; b++) {
                float ref = prevBand[b];
                if (b > 0) ref = Math.max(ref, prevBand[b - 1]);
                if (b + 1 < bandCount) ref = Math.max(ref, prevBand[b + 1]);
                rise[b] = Math.max(0f, band[b] - ref);
            }
            // A drum hit lifts every band at once; a pitched note lifts a few bands and leaves
            // their neighbours alone. Whatever stands above the local median is "tonal".
            for (int b = 0; b < bandCount; b++) {
                float d = rise[b];
                total += d;
                weighted += d * bandSemis[b];
                if (bandLow[b]) low += d;
                if (d <= 0f) continue;
                float med = median(rise, b - 5, b + 5, bandCount);
                float t = Math.max(0f, d - med);
                if (bandTonal[b]) tone += t;
                burst += d - t;
            }
        }
        System.arraycopy(band, 0, prevBand, 0, bandCount);
        first = false;

        flux[frames] = total;
        lowFlux[frames] = low;
        tonal[frames] = tone;
        perc[frames] = burst;
        bright[frames] = total > 1e-6f ? weighted / total : (frames > 0 ? bright[frames - 1] : 60f);
        rms[frames] = (float) Math.sqrt(energy / frameSize);

        // Keep the long window filled with the newest samples.
        if (frames == 0) {
            System.arraycopy(buf, 0, longBuf, longSize - frameSize, frameSize);
        } else {
            System.arraycopy(longBuf, hop, longBuf, 0, longSize - hop);
            System.arraycopy(buf, frameSize - hop, longBuf, longSize - hop, hop);
        }
        if (frames % 2 == 0) {
            for (int i = 0; i < longSize; i++) {
                longRe[i] = longBuf[i] * longWindow[i];
                longIm[i] = 0f;
            }
            longFft.transform(longRe, longIm);
            for (int k = 1; k < frameSize; k++) {
                logMag[k] = (float) Math.log1p(Math.sqrt(longRe[k] * longRe[k] + longIm[k] * longIm[k]) * longGain);
            }
            estimateNewPitch();
            System.arraycopy(logMag, 0, history[longFrames % RISE_FRAMES], 0, frameSize);
            if (historyCount < RISE_FRAMES) historyCount++;
            longFrames++;
        } else {
            pitch[frames] = pitch[frames - 1];
            pitchConf[frames] = pitchConf[frames - 1];
            rich[frames] = rich[frames - 1];
            System.arraycopy(toneChroma, (frames - 1) * 12, toneChroma, frames * 12, 12);
        }
        frames++;
    }

    private float median(float[] x, int from, int to, int limit) {
        from = Math.max(0, from);
        to = Math.min(limit - 1, to);
        int n = to - from + 1;
        System.arraycopy(x, from, scratch, 0, n);
        // Insertion sort: the window is tiny.
        for (int i = 1; i < n; i++) {
            float v = scratch[i];
            int j = i - 1;
            while (j >= 0 && scratch[j] > v) {
                scratch[j + 1] = scratch[j];
                j--;
            }
            scratch[j + 1] = v;
        }
        return scratch[n / 2];
    }

    /**
     * Pitch of whatever started sounding in the last few frames: take only the part of the
     * spectrum that rose, find its peaks, and look for the note whose harmonics explain them best.
     */
    private void estimateNewPitch() {
        pitch[frames] = Float.NaN;
        pitchConf[frames] = 0f;
        rich[frames] = 0f;
        int toneAt = frames * 12;
        for (int c = 0; c < 12; c++) toneChroma[toneAt + c] = 0f;
        if (historyCount < RISE_FRAMES) return;
        float[] old = history[longFrames % RISE_FRAMES];
        double binHz = this.binHz / 2;
        for (int k = pitchLoBin - 1; k <= pitchHiBin + 1; k++) newTone[k] = Math.max(0f, logMag[k] - old[k]);
        float max = 0f;
        for (int k = pitchLoBin; k <= pitchHiBin; k++) {
            float v = newTone[k];
            float c = v > 0f ? Math.max(0f, v - median(newTone, k - 12, k + 12, pitchHiBin + 2)) : 0f;
            cleanTone[k] = c;
            if (c > max) max = c;
        }
        cleanTone[pitchLoBin - 1] = 0f;
        cleanTone[pitchHiBin + 1] = 0f;
        if (max < 0.15f) return;

        int count = 0;
        double totalAmp = 0;
        for (int k = pitchLoBin; k <= pitchHiBin; k++) {
            float v = cleanTone[k];
            if (v < 0.12f * max || v < cleanTone[k - 1] || v <= cleanTone[k + 1]) continue;
            double pos = k;
            float a = logMag[k - 1], b = logMag[k], c = logMag[k + 1];
            if (b >= a && b >= c) {
                double denom = a - 2 * b + c;
                if (Math.abs(denom) > 1e-9) pos = k + Math.max(-0.5, Math.min(0.5, 0.5 * (a - c) / denom));
            }
            int slot = count;
            if (count == MAX_PEAKS) {
                slot = 0;
                for (int i = 1; i < MAX_PEAKS; i++) if (peakAmp[i] < peakAmp[slot]) slot = i;
                if (peakAmp[slot] >= v) continue;
                totalAmp -= peakAmp[slot];
            } else {
                count++;
            }
            peakHz[slot] = pos * binHz;
            peakAmp[slot] = v;
            totalAmp += v;
        }
        if (count < 2 || totalAmp <= 0) return;

        // Fingerprint of the new tone: which of the 12 pitch classes its partials fall on.
        // Each partial is shared between its two nearest semitones so that a slightly sharp or
        // flat note does not flip from one class to the next.
        for (int i = 0; i < count; i++) {
            if (peakHz[i] < 120.0) continue;
            double midi = 69 + 12 * Math.log(peakHz[i] / 440.0) / Math.log(2);
            int lo = (int) Math.floor(midi);
            float frac = (float) (midi - lo);
            toneChroma[toneAt + Math.floorMod(lo, 12)] += (float) peakAmp[i] * (1f - frac);
            toneChroma[toneAt + Math.floorMod(lo + 1, 12)] += (float) peakAmp[i] * frac;
        }

        Arrays.fill(peakUsed, false);
        double[] found = bestF0(count, true);
        if (found == null) return;
        pitch[frames] = (float) (69 + 12 * Math.log(found[0] / 440.0) / Math.log(2));
        pitchConf[frames] = (float) Math.min(1.0, found[2] / totalAmp);
        // A second, different note hiding in what the first could not explain means a chord.
        double[] second = bestF0(count, false);
        rich[frames] = second == null ? 0f : (float) Math.min(1.0, second[1] / found[1]);
    }

    /**
     * @return {frequency, weighted score, amplitude explained} of the best-fitting note among
     *         the peaks not yet used, or null. With {@code claim} set, the peaks it explains
     *         are marked as used.
     */
    private double[] bestF0(int count, boolean claim) {
        double bestScore = 0;
        int best = -1;
        for (int c = 0; c < F0_STEPS; c++) {
            double s = harmonicScore(c, count);
            f0Score[c] = s;
            if (s > bestScore) {
                bestScore = s;
                best = c;
            }
        }
        if (best < 0) return null;
        // A chord's fifth and octave fit the peaks almost as well as its root does. When the
        // note a fifth or an octave below scores nearly as high, that lower note is the root
        // (down to the guitar's low E; below that it is the bass doubling the chord).
        final int lowestRoot = 4;
        for (int pass = 0; pass < 2; pass++) {
            int root = -1;
            for (int down : new int[] {14, 24}) {
                for (int c = best - down - 1; c <= best - down + 1; c++) {
                    if (c < lowestRoot || f0Score[c] < 0.85 * bestScore) continue;
                    if (root < 0 || f0Score[c] > f0Score[root]) root = c;
                }
                if (root >= 0) break;
            }
            if (root < 0) break;
            best = root;
        }
        bestScore = f0Score[best];
        double binHz = this.binHz / 2;
        double f0 = f0Hz[best];
        double num = 0, den = 0, explained = 0;
        for (int i = 0; i < count; i++) {
            if (peakUsed[i]) continue;
            int h = (int) Math.round(peakHz[i] / f0);
            if (h < 1 || h > MAX_HARMONIC) continue;
            double tol = Math.max(h * f0 * 0.021, binHz * 0.30);
            if (Math.abs(peakHz[i] - h * f0) >= tol) continue;
            double w = peakAmp[i] * harmonicWeight[h];
            num += w * peakHz[i] / h;
            den += w;
            explained += peakAmp[i];
            if (claim) peakUsed[i] = true;
        }
        if (den <= 0) return null;
        return new double[] {num / den, bestScore, explained};
    }

    /** How well the harmonics of pitch candidate {@code c} line up with the unused peaks. */
    private double harmonicScore(int c, int count) {
        double binHz = this.binHz / 2;
        double f0 = f0Hz[c];
        double s = 0;
        boolean hasLow = false;
        for (int i = 0; i < count; i++) {
            if (peakUsed[i]) continue;
            int h = (int) Math.round(peakHz[i] / f0);
            if (h < 1 || h > MAX_HARMONIC) continue;
            double tol = Math.max(h * f0 * 0.021, binHz * 0.30);
            double dev = Math.abs(peakHz[i] - h * f0);
            if (dev >= tol) continue;
            s += peakAmp[i] * harmonicWeight[h] * (1 - 0.5 * dev / tol);
            if (h <= 2) hasLow = true;
        }
        // A real note shows its fundamental or first overtone, not only high harmonics.
        if (!hasLow) s *= 0.5;
        // When a melody and a bass note start together, the ear follows the melody.
        return s * (1.0 + 0.12 * c / 24.0);
    }

    private void ensureCapacity() {
        if (frames < flux.length) return;
        int n = frames * 2;
        flux = Arrays.copyOf(flux, n);
        lowFlux = Arrays.copyOf(lowFlux, n);
        tonal = Arrays.copyOf(tonal, n);
        perc = Arrays.copyOf(perc, n);
        pitch = Arrays.copyOf(pitch, n);
        pitchConf = Arrays.copyOf(pitchConf, n);
        rich = Arrays.copyOf(rich, n);
        rms = Arrays.copyOf(rms, n);
        bright = Arrays.copyOf(bright, n);
        chroma = Arrays.copyOf(chroma, n * 12);
        toneChroma = Arrays.copyOf(toneChroma, n * 12);
    }

    public Features finish() {
        double fps = (double) sampleRate / hop;
        double duration = (double) totalSamples / sampleRate;
        // A sound registers when it is roughly two thirds of the way into the window;
        // measured with test clicks and tones (see CoreTest).
        Features f = new Features(fps, duration, (frameSize * ONSET_POSITION) / sampleRate);
        f.flux = Arrays.copyOf(flux, frames);
        f.lowFlux = Arrays.copyOf(lowFlux, frames);
        f.tonal = Arrays.copyOf(tonal, frames);
        f.perc = Arrays.copyOf(perc, frames);
        f.pitch = Arrays.copyOf(pitch, frames);
        f.pitchConf = Arrays.copyOf(pitchConf, frames);
        f.rich = Arrays.copyOf(rich, frames);
        f.rms = Arrays.copyOf(rms, frames);
        f.bright = Arrays.copyOf(bright, frames);
        f.chroma = Arrays.copyOf(chroma, frames * 12);
        f.toneChroma = Arrays.copyOf(toneChroma, frames * 12);
        return f;
    }

    /** Where inside the analysis window a new sound registers (0 = start, 1 = end). */
    static final double ONSET_POSITION = 0.67;
}
