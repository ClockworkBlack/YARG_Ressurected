package com.clockworkblack.yargconverter.stems;

import ai.onnxruntime.OnnxTensor;
import ai.onnxruntime.OrtEnvironment;
import ai.onnxruntime.OrtException;
import ai.onnxruntime.OrtSession;

import com.clockworkblack.yargconverter.core.WordAligner;

import java.nio.FloatBuffer;
import java.util.Collections;

/**
 * The speech model that hears letters in a voice (wav2vec2-base-960h as an ONNX file): input
 * "input_values" float32 (1, samples) at 16 kHz, output "logits" float32 (1, frames, 32), one
 * frame every 320 samples. Used by {@link WordAligner} to find where each lyric line is sung.
 */
public final class OrtWordModel implements AutoCloseable {
    private final OrtEnvironment env;
    private final OrtSession session;
    private final String inputName;

    /** Seconds heard at a time, and extra on each side so the edges are heard in context. */
    private static final int CHUNK = 20 * WordAligner.SAMPLE_RATE, PAD = 2 * WordAligner.SAMPLE_RATE;
    private static final int HOP = 320;

    public interface Progress {
        void onProgress(int done, int total);
    }

    public OrtWordModel(String modelPath, int threads) throws OrtException {
        env = OrtEnvironment.getEnvironment();
        OrtSession.SessionOptions options = new OrtSession.SessionOptions();
        options.setOptimizationLevel(OrtSession.SessionOptions.OptLevel.ALL_OPT);
        if (threads > 0) options.setIntraOpNumThreads(threads);
        // On a computer with an NVIDIA graphics card and the GPU build of the runtime (the desktop
        // runner, e.g. on Colab): run there. Never set on a phone.
        Gpu.add(options);
        session = env.createSession(modelPath, options);
        inputName = session.getInputNames().iterator().next();
    }

    /**
     * @param audio the voice on its own, mono, at {@link WordAligner#SAMPLE_RATE}
     * @return for every 20 ms, the log likelihood of each of the model's 32 symbols
     */
    public float[][] letters(float[] audio, int length, Progress progress) throws OrtException {
        int frames = length / HOP;
        float[][] out = new float[frames][];
        int chunks = (length + CHUNK - 1) / CHUNK;
        for (int c = 0; c < chunks; c++) {
            int pos = c * CHUNK;
            int a = Math.max(0, pos - PAD), b = Math.min(length, pos + CHUNK + PAD);
            int n = b - a;
            if (n < HOP * 4) break;
            // The model expects each stretch scaled to zero mean and unit spread
            double mean = 0;
            for (int i = a; i < b; i++) mean += audio[i];
            mean /= n;
            double var = 0;
            for (int i = a; i < b; i++) var += (audio[i] - mean) * (audio[i] - mean);
            float scale = (float) (1.0 / (Math.sqrt(var / n) + 1e-7));
            float[] x = new float[n];
            for (int i = 0; i < n; i++) x[i] = (float) (audio[a + i] - mean) * scale;

            try (OnnxTensor tensor = OnnxTensor.createTensor(env, FloatBuffer.wrap(x), new long[] {1, n});
                 OrtSession.Result result = session.run(Collections.singletonMap(inputName, tensor))) {
                OnnxTensor logits = (OnnxTensor) result.get(0);
                long[] shape = logits.getInfo().getShape();
                if (shape.length != 3 || shape[2] != WordAligner.SYMBOLS) {
                    throw new IllegalStateException("Unexpected model output shape " + java.util.Arrays.toString(shape));
                }
                int got = (int) shape[1];
                FloatBuffer data = logits.getFloatBuffer();
                float[] row = new float[WordAligner.SYMBOLS];
                int from = (pos - a) / HOP;
                int until = from + (Math.min(length, pos + CHUNK) - pos) / HOP;
                for (int f = 0; f < got; f++) {
                    data.get(row);
                    int at = pos / HOP + (f - from);
                    if (f < from || f >= until || at >= frames) continue;
                    // Scores to log likelihoods
                    float max = Float.NEGATIVE_INFINITY;
                    for (float v : row) max = Math.max(max, v);
                    double sum = 0;
                    for (float v : row) sum += Math.exp(v - max);
                    float norm = max + (float) Math.log(sum);
                    float[] lp = new float[WordAligner.SYMBOLS];
                    for (int k = 0; k < lp.length; k++) lp[k] = row[k] - norm;
                    out[at] = lp;
                }
            }
            if (progress != null) progress.onProgress(c + 1, chunks);
        }
        // The last frames of the file, which the model is a little short of: silence
        float[] quiet = new float[WordAligner.SYMBOLS];
        java.util.Arrays.fill(quiet, -20f);
        quiet[0] = 0f;
        for (int f = 0; f < frames; f++) if (out[f] == null) out[f] = quiet;
        return out;
    }

    @Override
    public void close() throws OrtException {
        session.close();
    }
}
