// YARG Resurrected - plays a singer's voice back to them ("Vocal Monitoring").
//
// The game's sound library plays microphones back itself, but only the ones it records from. The
// phone's own mic, a USB mic or a headset opened through Android (YargMicCapture), and phones
// singing over Wi-Fi, are heard by the game without passing through it, so they were silent. This
// is their playback: one small player per microphone, on the output the game's sound is on
// (headphones, USB audio, Bluetooth), asked for the shortest delay Android can give.
//
// Sound is written without ever waiting: if the player is full, the newest sound is dropped
// rather than the delay growing.
package in.yarg.resurrected;

import android.media.AudioAttributes;
import android.media.AudioDeviceInfo;
import android.media.AudioFormat;
import android.media.AudioTrack;
import android.os.Build;

public final class YargVoiceMonitor {
    private final AudioTrack track;
    private final int sampleRate;
    /** Sound written before playing starts (and again after running dry): a cushion for Wi-Fi. */
    private final int primeSamples;
    private volatile float gain;
    private short[] scratch = new short[0];
    private boolean playing;
    private int primed;
    private int underruns;

    private YargVoiceMonitor(AudioTrack track, int sampleRate, int primeSamples) {
        this.track = track;
        this.sampleRate = sampleRate;
        this.primeSamples = primeSamples;
    }

    /**
     * @param sampleRate the microphone's rate
     * @param cushionMilliseconds sound gathered before playing starts: 0 for a microphone on this
     *        phone, more for one whose sound arrives over Wi-Fi in uneven bursts
     * @return the player, or null when Android could not make one
     */
    public static YargVoiceMonitor open(int sampleRate, int cushionMilliseconds) {
        try {
            int min = AudioTrack.getMinBufferSize(sampleRate, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT);
            if (min <= 0) return null;
            int prime = sampleRate * Math.max(0, cushionMilliseconds) / 1000;
            // Room for the cushion and a little more; no more, or the voice falls behind
            int bytes = Math.max(min, (prime + sampleRate / 25) * 2);
            AudioTrack.Builder builder = new AudioTrack.Builder()
                    .setAudioAttributes(new AudioAttributes.Builder()
                            // The same use as the game's own sound, so Android sends it to the same place
                            .setUsage(AudioAttributes.USAGE_GAME)
                            .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                            .build())
                    .setAudioFormat(new AudioFormat.Builder()
                            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                            .setSampleRate(sampleRate)
                            .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                            .build())
                    .setTransferMode(AudioTrack.MODE_STREAM)
                    .setBufferSizeInBytes(bytes);
            if (Build.VERSION.SDK_INT >= 26) {
                builder.setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY);
            }
            AudioTrack track = builder.build();
            if (track.getState() != AudioTrack.STATE_INITIALIZED) {
                track.release();
                return null;
            }
            return new YargVoiceMonitor(track, sampleRate, prime);
        } catch (Throwable t) {
            return null;
        }
    }

    public int getSampleRate() {
        return sampleRate;
    }

    /** How loud the voice is played: 0 is silent, 1 as recorded; above 1 louder, softly limited. */
    public synchronized void setGain(float value) {
        float g = Math.max(0f, Math.min(4f, value));
        if (g == gain) return;
        gain = g;
        if (g == 0f && playing) {
            // Silent: stop, and forget what was waiting, so turning it up again starts fresh
            try {
                track.pause();
                track.flush();
            } catch (Throwable ignored) {
            }
            playing = false;
            primed = 0;
        }
    }

    /** Plays {@code count} 16-bit samples (from the microphone's own thread). */
    public synchronized void write(short[] samples, int count) {
        float g = gain;
        if (g <= 0f || count <= 0) return;
        if (scratch.length < count) scratch = new short[count];
        for (int i = 0; i < count; i++) scratch[i] = shape(samples[i], g);
        put(count);
    }

    /** Plays {@code length} bytes of 16-bit little-endian sound (as phones send it over Wi-Fi). */
    public synchronized void writeBytes(byte[] data, int offset, int length) {
        float g = gain;
        int count = length / 2;
        if (g <= 0f || count <= 0) return;
        if (scratch.length < count) scratch = new short[count];
        for (int i = 0; i < count; i++) {
            int at = offset + i * 2;
            scratch[i] = shape((short) ((data[at] & 0xff) | (data[at + 1] << 8)), g);
        }
        put(count);
    }

    private void put(int count) {
        try {
            if (playing && Build.VERSION.SDK_INT >= 24) {
                // Ran dry (a Wi-Fi gap): gather a cushion again before going on, rather than crackle
                int now = track.getUnderrunCount();
                if (now != underruns && primeSamples > 0) {
                    track.pause();
                    playing = false;
                    primed = 0;
                }
                underruns = now;
            }
            int wrote = track.write(scratch, 0, count, AudioTrack.WRITE_NON_BLOCKING);
            if (!playing) {
                primed += Math.max(0, wrote);
                if (primed >= primeSamples) {
                    track.play();
                    playing = true;
                    if (Build.VERSION.SDK_INT >= 24) underruns = track.getUnderrunCount();
                }
            }
        } catch (Throwable ignored) {
            // Never worth stopping the microphone over
        }
    }

    /** One sample made {@code g} times louder; loud peaks are rounded off instead of clipping hard. */
    private static short shape(short sample, float g) {
        float x = sample * g / 32768f;
        float a = Math.abs(x);
        if (a > 0.8f) {
            float over = (a - 0.8f) / 0.2f;
            a = 0.8f + 0.2f * (float) Math.tanh(over);
            x = x < 0 ? -a : a;
        }
        return (short) Math.round(x * 32767f);
    }

    /**
     * The kind of output the voice is played on (an AudioDeviceInfo type), or -1 when Android
     * does not say. Used to warn that Bluetooth adds a delay.
     */
    public int getRoutedType() {
        try {
            AudioDeviceInfo routed = track.getRoutedDevice();
            return routed == null ? -1 : routed.getType();
        } catch (Throwable t) {
            return -1;
        }
    }

    /** Whether the voice is going to a Bluetooth output, which adds a delay of its own. */
    public boolean isBluetooth() {
        int type = getRoutedType();
        return type == AudioDeviceInfo.TYPE_BLUETOOTH_A2DP || type == AudioDeviceInfo.TYPE_BLUETOOTH_SCO
                || type == 26 /* TYPE_BLE_HEADSET */ || type == 27 /* TYPE_BLE_SPEAKER */ || type == 30 /* TYPE_BLE_BROADCAST */;
    }

    public synchronized void close() {
        try {
            track.pause();
            track.flush();
        } catch (Throwable ignored) {
        }
        try {
            track.release();
        } catch (Throwable ignored) {
        }
        playing = false;
    }
}
