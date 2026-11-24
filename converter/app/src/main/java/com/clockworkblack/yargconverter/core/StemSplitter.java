package com.clockworkblack.yargconverter.core;

import java.io.IOException;

/**
 * Runs an instrument-separation model over a whole song.
 *
 * The model (HT-Demucs, 6 stems) only takes 7.8 seconds of stereo audio at a time, so the song
 * is cut into overlapping segments, each is separated, and neighbouring results are cross-faded
 * where they overlap. Audio is pulled from a source and pushed to a sink a segment at a time,
 * so a whole song never has to sit in memory.
 */
public final class StemSplitter {
    private StemSplitter() {}

    /** Sample rate the model works at. */
    public static final int SAMPLE_RATE = 44100;
    /** Samples per model run: 7.8 seconds. */
    public static final int SEGMENT = 343980;
    public static final int DRUMS = 0, BASS = 1, OTHER = 2, VOCALS = 3, GUITAR = 4, PIANO = 5;
    public static final String[] STEMS = {"drums", "bass", "other", "vocals", "guitar", "piano"};

    /** The neural network itself. */
    public interface Model {
        /** Number of stems the model returns. */
        int stems();

        /** Separates one segment. {@code out} is [stem][channel][sample]. */
        void run(float[] left, float[] right, float[][][] out) throws Exception;
    }

    /** Stereo audio at {@link #SAMPLE_RATE}, read front to back. */
    public interface Source {
        /** @return frames read, or 0 or less at the end */
        int read(float[] left, float[] right, int offset, int length) throws IOException;
    }

    /** Receives finished audio in order: the original mix and each stem, {@code count} frames at a time. */
    public interface Sink {
        void accept(float[] mixLeft, float[] mixRight, float[][][] stems, int count) throws IOException;
    }

    public interface Progress {
        /** @return false to stop early */
        boolean onSegment(int done, int total);
    }

    /** @return false if stopped early by {@code progress} */
    public static boolean split(Source source, long totalFrames, Model model, Sink sink, Progress progress) throws Exception {
        return split(source, totalFrames, model, SEGMENT, sink, progress);
    }

    /** As above with a custom segment length (used by tests). */
    public static boolean split(Source source, long totalFrames, Model model, int segment, Sink sink, Progress progress) throws Exception {
        int overlap = segment / 4;
        int stride = segment - overlap;
        int stems = model.stems();
        int chunks = (int) Math.max(1, (totalFrames + stride - 1) / stride);
        float[] inL = new float[segment];
        float[] inR = new float[segment];
        float[][][] out = new float[stems][2][segment];
        float[][][] tail = new float[stems][2][overlap];
        float[] fadeIn = new float[overlap];
        for (int j = 0; j < overlap; j++) fadeIn[j] = overlap > 1 ? (float) j / (overlap - 1) : 1f;

        fill(source, inL, inR, 0, segment);
        for (int i = 0; i < chunks; i++) {
            long start = (long) i * stride;
            if (i > 0) {
                System.arraycopy(inL, stride, inL, 0, overlap);
                System.arraycopy(inR, stride, inR, 0, overlap);
                fill(source, inL, inR, overlap, stride);
            }
            model.run(inL, inR, out);
            boolean last = i == chunks - 1;
            for (int s = 0; s < stems; s++) {
                for (int c = 0; c < 2; c++) {
                    float[] o = out[s][c];
                    float[] t = tail[s][c];
                    // Cross-fade from the end of the previous segment into the start of this one.
                    if (i > 0) for (int j = 0; j < overlap; j++) o[j] = t[j] + o[j] * fadeIn[j];
                    if (!last) for (int j = 0; j < overlap; j++) t[j] = o[stride + j] * (1f - fadeIn[j]);
                }
            }
            int emit = (int) Math.max(0, Math.min(last ? segment : stride, totalFrames - start));
            if (emit > 0) sink.accept(inL, inR, out, emit);
            if (progress != null && !progress.onSegment(i + 1, chunks)) return false;
        }
        return true;
    }

    private static void fill(Source source, float[] left, float[] right, int offset, int length) throws IOException {
        int got = 0;
        while (got < length) {
            int n = source.read(left, right, offset + got, length - got);
            if (n <= 0) break;
            got += n;
        }
        for (int i = offset + got; i < offset + length; i++) {
            left[i] = 0f;
            right[i] = 0f;
        }
    }
}
