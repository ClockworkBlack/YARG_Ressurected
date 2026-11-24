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
 * The instrument-separation model file: where it lives on the phone and how it gets there.
 *
 * The model is HT-Demucs 6-stem (Meta's Demucs v4, MIT licence) converted to ONNX by a third
 * party and hosted on Hugging Face. The download is pinned to one exact revision and checked
 * against a fixed SHA-256, so the app only ever runs the file it was tested with.
 */
final class ModelStore {
    private ModelStore() {}

    static final String FILE_NAME = "htdemucs_6s_fp16weights.onnx";
    static final String URL = "https://huggingface.co/StemSplitio/htdemucs-6s-onnx/resolve/"
            + "49df9b6989cf2150840ea65b0bef77a2e471b678/htdemucs_6s_fp16weights.onnx";
    static final long SIZE = 136428532L;
    static final String SHA256 = "7ce55792e2231c93fbf92de95f5fd5b3a5e6c89f7db690dfd693e8f1dce56869";

    interface Progress {
        /** @return false to pause the download (it resumes from the same place next time) */
        boolean onProgress(long done, long total);
    }

    static File file(Context context) {
        File base = context.getExternalFilesDir(null);
        if (base == null) base = context.getFilesDir();
        return new File(new File(base, "models"), FILE_NAME);
    }

    static boolean installed(Context context) {
        File f = file(context);
        return f.isFile() && f.length() == SIZE;
    }

    /** Downloads the model, resuming a partial download if there is one. */
    static void download(Context context, Progress progress) throws IOException {
        File target = file(context);
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
