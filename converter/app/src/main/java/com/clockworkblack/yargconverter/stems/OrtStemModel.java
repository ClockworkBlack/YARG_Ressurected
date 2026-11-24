package com.clockworkblack.yargconverter.stems;

import ai.onnxruntime.OnnxTensor;
import ai.onnxruntime.OrtEnvironment;
import ai.onnxruntime.OrtException;
import ai.onnxruntime.OrtSession;

import com.clockworkblack.yargconverter.core.StemSplitter;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.util.Collections;

/**
 * HT-Demucs (6 stems) through ONNX Runtime. The same class runs on Android and on a desktop
 * JVM, which is how the real model gets tested in GitHub Actions.
 *
 * Model contract: input "mix" float32 (1, 2, 343980), stereo 44.1 kHz in [-1, 1];
 * output "stems" float32 (1, 6, 2, 343980) in the order drums, bass, other, vocals, guitar, piano.
 */
public final class OrtStemModel implements StemSplitter.Model, AutoCloseable {
    private final OrtEnvironment env;
    private final OrtSession session;
    private final String inputName;
    private final FloatBuffer input;
    private final int segment = StemSplitter.SEGMENT;

    public OrtStemModel(String modelPath, int threads) throws OrtException {
        env = OrtEnvironment.getEnvironment();
        OrtSession.SessionOptions options = new OrtSession.SessionOptions();
        options.setOptimizationLevel(OrtSession.SessionOptions.OptLevel.ALL_OPT);
        if (threads > 0) options.setIntraOpNumThreads(threads);
        session = env.createSession(modelPath, options);
        inputName = session.getInputNames().iterator().next();
        input = ByteBuffer.allocateDirect(2 * segment * 4).order(ByteOrder.nativeOrder()).asFloatBuffer();
    }

    @Override
    public int stems() {
        return StemSplitter.STEMS.length;
    }

    @Override
    public void run(float[] left, float[] right, float[][][] out) throws OrtException {
        input.clear();
        input.put(left, 0, segment);
        input.put(right, 0, segment);
        input.rewind();
        try (OnnxTensor tensor = OnnxTensor.createTensor(env, input, new long[] {1, 2, segment});
             OrtSession.Result result = session.run(Collections.singletonMap(inputName, tensor))) {
            OnnxTensor stems = (OnnxTensor) result.get(0);
            long[] shape = stems.getInfo().getShape();
            if (shape.length != 4 || shape[1] != out.length || shape[2] != 2 || shape[3] != segment) {
                throw new IllegalStateException("Unexpected model output shape " + java.util.Arrays.toString(shape));
            }
            FloatBuffer data = stems.getFloatBuffer();
            for (int s = 0; s < out.length; s++) {
                data.get(out[s][0], 0, segment);
                data.get(out[s][1], 0, segment);
            }
        }
    }

    @Override
    public void close() throws OrtException {
        session.close();
    }
}
