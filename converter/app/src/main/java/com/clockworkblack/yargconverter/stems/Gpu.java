package com.clockworkblack.yargconverter.stems;

import ai.onnxruntime.OrtSession;

/**
 * Runs a model on an NVIDIA graphics card (the desktop runner, e.g. on Colab; never on a phone).
 *
 * By default the runtime takes graphics memory in ever bigger steps and never gives it back, so
 * with several songs at once (and speech to text beside them) the card runs out. Here it takes
 * only what is asked for, and no more than -Dyarg.gpuMemMb when that is set.
 */
final class Gpu {
    private Gpu() {}

    /** Adds the graphics card to the options when -Dyarg.cuda is set. */
    static void add(OrtSession.SessionOptions options) {
        if (System.getProperty("yarg.cuda") == null) return;
        try {
            // Looked up by name: the phone's runtime has no graphics card options at all
            Class<?> cls = Class.forName("ai.onnxruntime.providers.OrtCUDAProviderOptions");
            Object cuda = cls.getConstructor(int.class).newInstance(0);
            java.lang.reflect.Method add = cls.getMethod("add", String.class, String.class);
            add.invoke(cuda, "arena_extend_strategy", "kSameAsRequested");
            String limit = System.getProperty("yarg.gpuMemMb");
            if (limit != null && limit.matches("\\d+")) {
                add.invoke(cuda, "gpu_mem_limit", String.valueOf(Long.parseLong(limit) * 1024L * 1024L));
            }
            OrtSession.SessionOptions.class.getMethod("addCUDA", cls).invoke(options, cuda);
        } catch (Throwable withOptions) {
            try {
                options.addCUDA(0);
            } catch (Throwable e) {
                System.err.println("No GPU for the model (" + e.getMessage() + "): running on the processor");
            }
        }
    }
}
