package com.clockworkblack.yargconverter.core;

/** Per-frame measurements of a song, produced by {@link AudioAnalyzer}. */
public final class Features {
    /** Frames per second. */
    public final double fps;
    /** Song length in seconds. */
    public final double duration;
    /** Seconds from the start of the song to the point frame 0 describes. */
    public final double frameOffset;

    /** How much new sound energy arrived in each frame (all frequencies). */
    public float[] flux;
    /** Same, for bass frequencies only (kick drum / bass guitar). */
    public float[] lowFlux;
    /** New energy that arrived as distinct pitched tones (guitar, bass, voice, keys). */
    public float[] tonal;
    /** New energy that arrived as a broad burst of noise (drums, cymbals, pick scrapes). */
    public float[] perc;
    /** Pitch of the newly arrived tone as a MIDI note number, or NaN when none stands out. */
    public float[] pitch;
    /** 0..1: how much of the newly arrived tone that single pitch explains. */
    public float[] pitchConf;
    /** 0..1: how much of the new tone is left over after that pitch (high for chords). */
    public float[] rich;
    /** Loudness of each frame. */
    public float[] rms;
    /** Where in the spectrum the new energy arrived, in semitones above 40 Hz (low = kick, high = cymbal). */
    public float[] bright;
    /** Strength of each of the 12 pitch classes per frame (frames x 12, C first). */
    public float[] chroma;
    /** Pitch classes of only the tone that just started (frames x 12): a fingerprint of the new note or chord. */
    public float[] toneChroma;

    Features(double fps, double duration, double frameOffset) {
        this.fps = fps;
        this.duration = duration;
        this.frameOffset = frameOffset;
    }

    public int frames() {
        return flux.length;
    }

    public double timeOf(double frame) {
        return frameOffset + frame / fps;
    }

    public int frameAt(double time) {
        int f = (int) Math.round((time - frameOffset) * fps);
        return Math.max(0, Math.min(frames() - 1, f));
    }

    /** Average pitch-class profile over [from, to) frames, scaled to unit length. */
    public float[] chromaBetween(int from, int to) {
        float[] out = new float[12];
        from = Math.max(0, from);
        to = Math.min(frames(), Math.max(to, from + 1));
        for (int i = from; i < to; i++) {
            for (int c = 0; c < 12; c++) out[c] += chroma[i * 12 + c];
        }
        double norm = 0;
        for (float v : out) norm += v * v;
        norm = Math.sqrt(norm);
        if (norm > 1e-9) for (int c = 0; c < 12; c++) out[c] = (float) (out[c] / norm);
        return out;
    }

    public static double cosine(float[] a, float[] b) {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.length; i++) {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na > 1e-12 && nb > 1e-12 ? dot / Math.sqrt(na * nb) : 0;
    }
}
