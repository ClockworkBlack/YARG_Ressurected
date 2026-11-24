package com.clockworkblack.yargconverter;

import android.content.Context;
import android.media.MediaCodec;
import android.media.MediaExtractor;
import android.media.MediaFormat;
import android.net.Uri;

import com.clockworkblack.yargconverter.core.AudioAnalyzer;
import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.Resampler;
import com.clockworkblack.yargconverter.core.StemSplitter;

import java.io.BufferedOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.io.RandomAccessFile;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/**
 * Decodes any audio file Android can play (MP3, WAV, FLAC, ...) and measures it as it goes.
 * Optionally also writes the decoded sound to a 16-bit WAV file, for formats YARG cannot read.
 */
final class AudioDecoder {
    private AudioDecoder() {}

    interface Progress {
        /** @param fraction 0..1, or -1 when the length is unknown */
        void onProgress(float fraction);
    }

    /** Receives the decoded audio as stereo at 44.1 kHz, the format the separation model wants. */
    interface StereoSink {
        void write(float[] left, float[] right, int count) throws IOException;
    }

    // android.media.AudioFormat encodings (written out so they work on every Android version).
    private static final int PCM_16 = 2, PCM_8 = 3, PCM_FLOAT = 4, PCM_24_PACKED = 21, PCM_32 = 22;

    static Features decode(Context context, Uri uri, File wavOut, StereoSink stereo, Progress progress) throws IOException {
        MediaExtractor extractor = new MediaExtractor();
        MediaCodec codec = null;
        WavWriter wav = null;
        try {
            extractor.setDataSource(context, uri, null);
            int track = -1;
            MediaFormat format = null;
            for (int i = 0; i < extractor.getTrackCount(); i++) {
                MediaFormat f = extractor.getTrackFormat(i);
                String mime = f.getString(MediaFormat.KEY_MIME);
                if (mime != null && mime.startsWith("audio/")) {
                    track = i;
                    format = f;
                    break;
                }
            }
            if (track < 0 || format == null) throw new IOException("No audio found in this file.");
            extractor.selectTrack(track);
            long durationUs = format.containsKey(MediaFormat.KEY_DURATION) ? format.getLong(MediaFormat.KEY_DURATION) : -1;
            int sampleRate = format.getInteger(MediaFormat.KEY_SAMPLE_RATE);
            int channels = format.getInteger(MediaFormat.KEY_CHANNEL_COUNT);
            int encoding = PCM_16;

            codec = MediaCodec.createDecoderByType(format.getString(MediaFormat.KEY_MIME));
            codec.configure(format, null, null, 0);
            codec.start();

            AudioAnalyzer analyzer = null;
            Resampler resampler = null;
            float[] left = new float[8192];
            float[] right = new float[8192];
            float[] outLeft = null;
            float[] outRight = null;
            float[] mono = new float[8192];
            short[] pcm = new short[8192];
            MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
            boolean inputDone = false;
            boolean outputDone = false;
            long lastReport = 0;
            int idle = 0;

            while (!outputDone) {
                if (!inputDone) {
                    int in = codec.dequeueInputBuffer(10000);
                    if (in >= 0) {
                        ByteBuffer buf = codec.getInputBuffer(in);
                        int size = buf == null ? -1 : extractor.readSampleData(buf, 0);
                        if (size < 0) {
                            codec.queueInputBuffer(in, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                            inputDone = true;
                        } else {
                            codec.queueInputBuffer(in, 0, size, extractor.getSampleTime(), 0);
                            extractor.advance();
                        }
                    }
                }
                int out = codec.dequeueOutputBuffer(info, 10000);
                if (out == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                    MediaFormat of = codec.getOutputFormat();
                    if (of.containsKey(MediaFormat.KEY_SAMPLE_RATE)) sampleRate = of.getInteger(MediaFormat.KEY_SAMPLE_RATE);
                    if (of.containsKey(MediaFormat.KEY_CHANNEL_COUNT)) channels = of.getInteger(MediaFormat.KEY_CHANNEL_COUNT);
                    if (of.containsKey("pcm-encoding")) encoding = of.getInteger("pcm-encoding");
                    idle = 0;
                } else if (out >= 0) {
                    idle = 0;
                    ByteBuffer buf = codec.getOutputBuffer(out);
                    if (buf != null && info.size > 0) {
                        if (analyzer == null) {
                            analyzer = new AudioAnalyzer(sampleRate);
                            if (wavOut != null) wav = new WavWriter(wavOut, sampleRate, channels);
                            if (stereo != null) {
                                resampler = new Resampler(sampleRate, StemSplitter.SAMPLE_RATE);
                                outLeft = new float[resampler.capacityFor(left.length)];
                                outRight = new float[resampler.capacityFor(left.length)];
                            }
                        }
                        buf.position(info.offset);
                        buf.limit(info.offset + info.size);
                        buf.order(ByteOrder.nativeOrder());
                        int bytesPer = bytesPerSample(encoding);
                        int frames = info.size / (bytesPer * channels);
                        int done = 0;
                        while (done < frames) {
                            int n = Math.min(frames - done, Math.min(mono.length, pcm.length / channels));
                            for (int i = 0; i < n; i++) {
                                float sum = 0f;
                                for (int c = 0; c < channels; c++) {
                                    float s = readSample(buf, encoding);
                                    sum += s;
                                    if (c == 0) {
                                        left[i] = s;
                                        right[i] = s;
                                    } else if (c == 1) {
                                        right[i] = s;
                                    }
                                    if (wav != null) {
                                        int v = Math.round(s * 32767f);
                                        pcm[i * channels + c] = (short) Math.max(-32768, Math.min(32767, v));
                                    }
                                }
                                mono[i] = sum / channels;
                            }
                            analyzer.feed(mono, 0, n);
                            if (resampler != null) {
                                int made = resampler.process(left, right, n, outLeft, outRight);
                                if (made > 0) stereo.write(outLeft, outRight, made);
                            }
                            if (wav != null) wav.write(pcm, n * channels);
                            done += n;
                        }
                    }
                    codec.releaseOutputBuffer(out, false);
                    if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) outputDone = true;
                    long now = System.currentTimeMillis();
                    if (progress != null && now - lastReport > 150) {
                        lastReport = now;
                        progress.onProgress(durationUs > 0 ? Math.min(1f, (float) info.presentationTimeUs / durationUs) : -1f);
                    }
                } else if (inputDone && ++idle > 500) {
                    // The decoder never signalled the end; stop instead of waiting forever.
                    outputDone = true;
                }
            }
            if (analyzer == null) throw new IOException("This file could not be decoded.");
            if (wav != null) {
                wav.finish();
                wav = null;
            }
            return analyzer.finish();
        } catch (IllegalStateException | IllegalArgumentException e) {
            throw new IOException("This file could not be decoded (" + e.getMessage() + ").", e);
        } finally {
            if (wav != null) wav.abort();
            if (codec != null) {
                try {
                    codec.stop();
                } catch (RuntimeException ignored) {
                    // Already stopped or failed; nothing more to do.
                }
                codec.release();
            }
            extractor.release();
        }
    }

    private static int bytesPerSample(int encoding) {
        switch (encoding) {
            case PCM_8: return 1;
            case PCM_FLOAT: return 4;
            case PCM_24_PACKED: return 3;
            case PCM_32: return 4;
            default: return 2;
        }
    }

    private static float readSample(ByteBuffer buf, int encoding) {
        switch (encoding) {
            case PCM_8:
                return ((buf.get() & 0xFF) - 128) / 128f;
            case PCM_FLOAT:
                return buf.getFloat();
            case PCM_24_PACKED: {
                int a = buf.get() & 0xFF;
                int b = buf.get() & 0xFF;
                int c = buf.get();
                return ((c << 16) | (b << 8) | a) / 8388608f;
            }
            case PCM_32:
                return buf.getInt() / 2147483648f;
            default:
                return buf.getShort() / 32768f;
        }
    }

    /** Minimal 16-bit PCM WAV writer; the header is filled in once the length is known. */
    private static final class WavWriter {
        private final File file;
        private final OutputStream out;
        private final int sampleRate;
        private final int channels;
        private final byte[] bytes = new byte[16384];
        private long dataBytes;

        WavWriter(File file, int sampleRate, int channels) throws IOException {
            this.file = file;
            this.sampleRate = sampleRate;
            this.channels = channels;
            out = new BufferedOutputStream(new FileOutputStream(file), 1 << 16);
            out.write(new byte[44]);
        }

        void write(short[] samples, int count) throws IOException {
            int n = 0;
            for (int i = 0; i < count; i++) {
                bytes[n++] = (byte) samples[i];
                bytes[n++] = (byte) (samples[i] >> 8);
                if (n == bytes.length) {
                    out.write(bytes, 0, n);
                    n = 0;
                }
            }
            if (n > 0) out.write(bytes, 0, n);
            dataBytes += count * 2L;
        }

        void finish() throws IOException {
            out.close();
            ByteBuffer h = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN);
            h.put(new byte[] {'R', 'I', 'F', 'F'}).putInt((int) (36 + dataBytes));
            h.put(new byte[] {'W', 'A', 'V', 'E', 'f', 'm', 't', ' '}).putInt(16);
            h.putShort((short) 1).putShort((short) channels).putInt(sampleRate);
            h.putInt(sampleRate * channels * 2).putShort((short) (channels * 2)).putShort((short) 16);
            h.put(new byte[] {'d', 'a', 't', 'a'}).putInt((int) dataBytes);
            try (RandomAccessFile raf = new RandomAccessFile(file, "rw")) {
                raf.seek(0);
                raf.write(h.array());
            }
        }

        void abort() {
            try {
                out.close();
            } catch (IOException ignored) {
                // Best effort: the half-written file is deleted next.
            }
            //noinspection ResultOfMethodCallIgnored
            file.delete();
        }
    }
}
