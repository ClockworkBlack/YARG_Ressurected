package com.clockworkblack.yargconverter.core;

import java.util.Arrays;

/**
 * Streaming audio analysis. Feed mono samples as they are decoded; only a few numbers per
 * frame are kept, so a whole song never has to sit in memory.
 */
public final class AudioAnalyzer {
    private static final int BANDS = 40;
    private static final double LOW_BAND_HZ = 180.0;

    private final int sampleRate;
    private final int frameSize;
    private final int hop;
    private final float[] window;
    private final float[] buf;
    private final float[] re;
    private final float[] im;
    private final Fft fft;
    private final int[] binBand;
    private final float[] binLogHz;
    private final boolean[] binPitch;
    private final int lowBands;
    private final float[] prevBand = new float[BANDS];
    private final float[] band = new float[BANDS];
    private int filled;
    private long totalSamples;
    private boolean first = true;

    private float[] flux = new float[4096];
    private float[] lowFlux = new float[4096];
    private float[] pitch = new float[4096];
    private float[] rms = new float[4096];
    private int frames;

    public AudioAnalyzer(int sampleRate) {
        if (sampleRate < 8000) throw new IllegalArgumentException("Unsupported sample rate " + sampleRate);
        this.sampleRate = sampleRate;
        int size = 2048;
        if (sampleRate < 32000) size = 1024;
        if (sampleRate > 64000) size = 4096;
        frameSize = size;
        hop = size / 4;
        window = new float[size];
        for (int i = 0; i < size; i++) window[i] = (float) (0.5 - 0.5 * Math.cos(2 * Math.PI * i / size));
        buf = new float[size];
        re = new float[size];
        im = new float[size];
        fft = new Fft(size);

        int bins = size / 2;
        binBand = new int[bins];
        binLogHz = new float[bins];
        binPitch = new boolean[bins];
        double lo = 40.0;
        double hi = Math.min(11000.0, sampleRate / 2.0);
        double span = Math.log(hi / lo);
        int low = 0;
        for (int b = 0; b < BANDS; b++) {
            double edge = lo * Math.exp(span * (b + 1) / BANDS);
            if (edge <= LOW_BAND_HZ) low = b + 1;
        }
        lowBands = Math.max(1, low);
        for (int k = 0; k < bins; k++) {
            double hz = (double) k * sampleRate / size;
            if (hz < lo || hz >= hi) {
                binBand[k] = -1;
            } else {
                binBand[k] = Math.min(BANDS - 1, (int) (Math.log(hz / lo) / span * BANDS));
            }
            binLogHz[k] = hz > 0 ? (float) (Math.log(hz) / Math.log(2)) : 0f;
            binPitch[k] = hz >= 80.0 && hz <= 2000.0;
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

        Arrays.fill(band, 0f);
        double pitchNum = 0;
        double pitchDen = 0;
        int bins = frameSize / 2;
        for (int k = 1; k < bins; k++) {
            float p = re[k] * re[k] + im[k] * im[k];
            int b = binBand[k];
            if (b >= 0) band[b] += p;
            if (binPitch[k]) {
                pitchNum += p * binLogHz[k];
                pitchDen += p;
            }
        }
        float f = 0f;
        float lf = 0f;
        for (int b = 0; b < BANDS; b++) {
            // Log-compress so quiet and loud notes both register; 1.0 = a full-scale sine.
            float v = (float) Math.log1p(Math.sqrt(band[b]) * 400.0 / frameSize);
            if (!first) {
                float d = v - prevBand[b];
                if (d > 0) {
                    f += d;
                    if (b < lowBands) lf += d;
                }
            }
            prevBand[b] = v;
        }
        first = false;

        if (frames == flux.length) {
            int n = frames * 2;
            flux = Arrays.copyOf(flux, n);
            lowFlux = Arrays.copyOf(lowFlux, n);
            pitch = Arrays.copyOf(pitch, n);
            rms = Arrays.copyOf(rms, n);
        }
        flux[frames] = f;
        lowFlux[frames] = lf;
        float prevPitch = frames > 0 ? pitch[frames - 1] : 8.5f;
        pitch[frames] = pitchDen > 1e-9 ? (float) (pitchNum / pitchDen) : prevPitch;
        rms[frames] = (float) Math.sqrt(energy / frameSize);
        frames++;
    }

    public Features finish() {
        double fps = (double) sampleRate / hop;
        double duration = (double) totalSamples / sampleRate;
        // A sound is "seen" by the flux roughly when it reaches the middle-late part of the
        // window; measured with test clicks (see CoreTest) this is about three quarters of a frame.
        double offset = (frameSize * ONSET_POSITION) / sampleRate;
        return new Features(fps, duration, offset,
                Arrays.copyOf(flux, frames), Arrays.copyOf(lowFlux, frames),
                Arrays.copyOf(pitch, frames), Arrays.copyOf(rms, frames));
    }

    /** Where inside the analysis window a new sound registers (0 = start, 1 = end). */
    static final double ONSET_POSITION = 0.74;
}
