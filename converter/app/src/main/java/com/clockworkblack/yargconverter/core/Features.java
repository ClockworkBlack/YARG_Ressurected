package com.clockworkblack.yargconverter.core;

/** Per-frame measurements of a song, produced by {@link AudioAnalyzer}. */
public final class Features {
    /** Frames per second. */
    public final double fps;
    /** Song length in seconds. */
    public final double duration;
    /** How much new sound energy arrived in each frame (all frequencies). */
    public final float[] flux;
    /** Same, for bass frequencies only (kick drum / bass guitar). */
    public final float[] lowFlux;
    /** Rough pitch height of each frame, in octaves (log2 of Hz). */
    public final float[] pitch;
    /** Loudness of each frame. */
    public final float[] rms;
    /** Seconds from the start of the song to the centre of frame 0. */
    public final double frameOffset;

    Features(double fps, double duration, double frameOffset, float[] flux, float[] lowFlux, float[] pitch, float[] rms) {
        this.fps = fps;
        this.duration = duration;
        this.frameOffset = frameOffset;
        this.flux = flux;
        this.lowFlux = lowFlux;
        this.pitch = pitch;
        this.rms = rms;
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
}
