package com.clockworkblack.yargconverter;

import android.content.Context;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;

/**
 * The model files: where they live on the phone and how they get there.
 *
 * The model is HT-Demucs 6-stem (Meta's Demucs v4, MIT licence) converted to ONNX by a third
 * party and hosted on Hugging Face. The download is pinned to one exact revision and checked
 * against a fixed SHA-256, so the app only ever runs the file it was tested with.
 */
final class ModelStore {
    private ModelStore() {}

    /** One model file: its name on the phone, the exact revision to download, and its fingerprint. */
    static final class Spec {
        final String fileName, url, sha256;
        final long size;

        Spec(String fileName, String url, long size, String sha256) {
            this.fileName = fileName;
            this.url = url;
            this.size = size;
            this.sha256 = sha256;
        }
    }

    /** Splits a song into instruments (needed for the two split modes). */
    static final Spec STEMS = new Spec("htdemucs_6s_fp16weights.onnx",
            "https://huggingface.co/StemSplitio/htdemucs-6s-onnx/resolve/49df9b6989cf2150840ea65b0bef77a2e471b678/htdemucs_6s_fp16weights.onnx",
            136428532L, "7ce55792e2231c93fbf92de95f5fd5b3a5e6c89f7db690dfd693e8f1dce56869");

    /**
     * Optional: hears the letters in the separated voice, so the lyrics can be timed word by word
     * (wav2vec2-base-960h by Meta, Apache 2.0, as ONNX from Xenova on Hugging Face). The smaller
     * quantized version of the same model was tested and was not accurate enough.
     */
    static final Spec WORDS = new Spec("wav2vec2-base-960h.onnx",
            "https://huggingface.co/Xenova/wav2vec2-base-960h/resolve/a19f851b3d42865797e410752b4c570c871e4825/onnx/model.onnx",
            377887594L, "e46614273f03ff4b87923a965e417fa72004825522cb007c9c25633b8475490d");

    static final String FILE_NAME = STEMS.fileName;
    static final long SIZE = STEMS.size;

    interface Progress {
        /** @return false to pause the download (it resumes from the same place next time) */
        boolean onProgress(long done, long total);
    }

    static File file(Context context) {
        return file(context, STEMS);
    }

    static boolean installed(Context context) {
        return installed(context, STEMS);
    }

    static void download(Context context, Progress progress) throws IOException {
        download(context, STEMS, progress);
    }

    static File file(Context context, Spec spec) {
        File base = context.getExternalFilesDir(null);
        if (base == null) base = context.getFilesDir();
        return new File(new File(base, "models"), spec.fileName);
    }

    static boolean installed(Context context, Spec spec) {
        File f = file(context, spec);
        return f.isFile() && f.length() == spec.size;
    }

    /** Removes a downloaded model (to free the space). */
    static boolean remove(Context context, Spec spec) {
        File f = file(context, spec);
        return !f.exists() || f.delete();
    }

    /** Downloads the model, resuming a partial download if there is one. */
    static void download(Context context, Spec spec, Progress progress) throws IOException {
        final String FILE_NAME = spec.fileName, URL = spec.url, SHA256 = spec.sha256;
        final long SIZE = spec.size;
        File target = file(context, spec);
        File dir = target.getParentFile();
        if (dir != null && !dir.isDirectory() && !dir.mkdirs()) throw new IOException("Could not create " + dir);
        File part = new File(dir, FILE_NAME + ".part");
        if (part.length() > SIZE && !part.delete()) throw new IOException("Could not clear an old download.");

        MessageDigest digest;
        try {
            digest = MessageDigest.getInstance("SHA-256");
        } catch (NoSuchAlgorithmException e) {
            throw new IOException("This phone cannot verify downloads.", e);
        }
        long have = part.isFile() ? part.length() : 0;
        byte[] buf = new byte[1 << 16];

        HttpURLConnection c = null;
        try {
            if (have < SIZE) {
                c = (HttpURLConnection) new URL(URL).openConnection();
                c.setConnectTimeout(15000);
                c.setReadTimeout(30000);
                c.setRequestProperty("User-Agent", "YARGConverter/0.1");
                if (have > 0) c.setRequestProperty("Range", "bytes=" + have + "-");
                int code = c.getResponseCode();
                if (code == 200) {
                    have = 0;
                } else if (code != 206) {
                    throw new IOException("The download server answered with error " + code + ".");
                }
            }
            // Hash what is already on disk, then keep hashing as the rest arrives.
            if (have > 0) {
                try (InputStream in = new FileInputStream(part)) {
                    long left = have;
                    while (left > 0) {
                        int n = in.read(buf, 0, (int) Math.min(buf.length, left));
                        if (n <= 0) throw new IOException("Could not re-read the partial download.");
                        digest.update(buf, 0, n);
                        left -= n;
                    }
                }
            }
            if (c != null) {
                try (InputStream in = c.getInputStream();
                     OutputStream out = new FileOutputStream(part, have > 0)) {
                    int n;
                    while ((n = in.read(buf)) > 0) {
                        if (have + n > SIZE) throw new IOException("The download is larger than expected.");
                        out.write(buf, 0, n);
                        digest.update(buf, 0, n);
                        have += n;
                        if (progress != null && !progress.onProgress(have, SIZE)) return;
                    }
                }
            }
        } finally {
            if (c != null) c.disconnect();
        }

        if (have != SIZE) throw new IOException("The download stopped early. Press download again to continue it.");
        StringBuilder hex = new StringBuilder();
        for (byte b : digest.digest()) hex.append(String.format("%02x", b));
        if (!hex.toString().equals(SHA256)) {
            //noinspection ResultOfMethodCallIgnored
            part.delete();
            throw new IOException("The downloaded file did not match the expected fingerprint and was discarded. Please try again.");
        }
        if (target.exists() && !target.delete()) throw new IOException("Could not replace the old model file.");
        if (!part.renameTo(target)) throw new IOException("Could not save the model file.");
    }
}
