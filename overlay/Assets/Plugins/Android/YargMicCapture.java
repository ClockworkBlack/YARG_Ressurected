// YARG Resurrected - records from one chosen microphone, so two players can sing on one phone.
//
// The game's sound library only ever records from "the" microphone, which on Android means
// whichever one the system prefers at the moment: plug in a USB mic and the phone's own mic is no
// longer heard. Android itself can do better: an app may open several recorders and ask for each
// to be fed by a particular input (the built-in mic, a USB mic, a wired headset). This class is
// one such recorder. The game opens one per singer and reads the sound from it.
//
// A background thread keeps reading from Android into a small ring of recent sound, so nothing
// backs up while the game is busy; the game takes it out in fixed-size chunks from its own thread.
package in.yarg.resurrected;

import android.content.Context;
import android.media.AudioDeviceInfo;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.os.Build;

import java.util.ArrayList;
import java.util.List;

public final class YargMicCapture {
    /** Kinds of input the game offers. */
    public static final int KIND_BUILT_IN = 0;
    public static final int KIND_USB = 1;
    public static final int KIND_WIRED = 2;

    private static volatile String lastError = "";

    private final AudioRecord record;
    private final int sampleRate;
    private final int chunk;
    private final short[] ring;
    private int writeAt;
    private int filled;
    // A running count of the samples recorded, and where in that count the chunk last handed
    // out began. Sound is sometimes thrown away (see pump() and flush()); with these the game
    // can tell how much, and still knows exactly when each sound it does get was made.
    private long written;
    private long lastTakeFirst = -1;
    private final Object lock = new Object();
    private volatile boolean running = true;
    private volatile boolean dead;
    private final Thread thread;
    private final int wantedKind;
    /** The voice played back to the singer ("Vocal Monitoring"), made when it is first turned up. */
    private volatile YargVoiceMonitor monitor;
    private volatile float monitorGain;
    private volatile int monitorOutput;
    private Context appContext;

    private YargMicCapture(AudioRecord record, int sampleRate, int chunkSamples, int wantedKind) {
        this.record = record;
        this.sampleRate = sampleRate;
        this.chunk = chunkSamples;
        this.wantedKind = wantedKind;
        // One second of sound at most is ever kept.
        this.ring = new short[sampleRate];
        thread = new Thread(new Runnable() {
            @Override
            public void run() {
                pump();
            }
        }, "YargMicCapture");
        thread.setDaemon(true);
    }

    public static String getLastError() {
        return lastError;
    }

    private static int kindOf(AudioDeviceInfo device) {
        switch (device.getType()) {
            case AudioDeviceInfo.TYPE_BUILTIN_MIC:
                return KIND_BUILT_IN;
            case AudioDeviceInfo.TYPE_USB_DEVICE:
            case AudioDeviceInfo.TYPE_USB_HEADSET:
            case AudioDeviceInfo.TYPE_USB_ACCESSORY:
                return KIND_USB;
            case AudioDeviceInfo.TYPE_WIRED_HEADSET:
                return KIND_WIRED;
            default:
                return -1;
        }
    }

    private static String nameOf(AudioDeviceInfo device) {
        CharSequence name = device.getProductName();
        String s = name == null ? "" : name.toString().replace('|', ' ').replace('\n', ' ').trim();
        return s.isEmpty() ? "USB" : s;
    }

    /**
     * The microphones that can be recorded from right now, one text line each: "kind|name", where
     * kind is one of the KIND_ numbers. The phone's own mic is listed once, however many
     * capsules it has.
     */
    public static String[] listInputs(Context context) {
        List<String> out = new ArrayList<String>();
        try {
            AudioManager manager = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
            boolean builtIn = false, wired = false;
            for (AudioDeviceInfo device : manager.getDevices(AudioManager.GET_DEVICES_INPUTS)) {
                int kind = kindOf(device);
                if (kind == KIND_BUILT_IN) {
                    if (!builtIn) out.add(0, KIND_BUILT_IN + "|");
                    builtIn = true;
                } else if (kind == KIND_WIRED) {
                    if (!wired) out.add(KIND_WIRED + "|");
                    wired = true;
                } else if (kind == KIND_USB) {
                    String line = KIND_USB + "|" + nameOf(device);
                    if (!out.contains(line)) out.add(line);
                }
            }
        } catch (Throwable t) {
            lastError = String.valueOf(t);
        }
        return out.toArray(new String[0]);
    }

    private static AudioDeviceInfo find(Context context, int kind, String name) {
        AudioManager manager = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        AudioDeviceInfo first = null;
        int matches = 0;
        for (AudioDeviceInfo device : manager.getDevices(AudioManager.GET_DEVICES_INPUTS)) {
            if (kindOf(device) != kind) continue;
            if (kind != KIND_USB) return device;
            if (nameOf(device).equals(name)) return device;
            if (first == null) first = device;
            matches++;
        }
        // The only USB mic there is will do, even if it is not the one saved with the profile.
        return matches >= 1 && (name == null || name.isEmpty() || matches == 1) ? first : null;
    }

    /**
     * Starts recording from the microphone of the given kind (and, for USB, name).
     *
     * @param chunkMilliseconds how much sound each call to {@link #take} returns
     * @return the recorder, or null if it could not be started ({@link #getLastError} says why)
     */
    public static YargMicCapture open(Context context, int kind, String name, int chunkMilliseconds) {
        lastError = "";
        try {
            AudioDeviceInfo device = find(context, kind, name);
            if (device == null) {
                lastError = "That microphone is not connected.";
                return null;
            }

            // Voice recognition is the input meant for analysis: no echo canceller, no automatic
            // gain riding the level up and down, and a known loudness on every phone.
            int[] sources = {MediaRecorder.AudioSource.VOICE_RECOGNITION, MediaRecorder.AudioSource.MIC};
            int[] rates = {48000, 44100, 16000};
            for (int source : sources) {
                for (int rate : rates) {
                    AudioRecord record = create(source, rate);
                    if (record == null) continue;
                    record.setPreferredDevice(device);
                    try {
                        record.startRecording();
                    } catch (IllegalStateException e) {
                        record.release();
                        continue;
                    }
                    if (record.getRecordingState() != AudioRecord.RECORDSTATE_RECORDING) {
                        record.release();
                        continue;
                    }
                    int chunkSamples = Math.max(64, rate * chunkMilliseconds / 1000);
                    YargMicCapture capture = new YargMicCapture(record, rate, chunkSamples, kind);
                    capture.appContext = context.getApplicationContext();
                    capture.thread.start();
                    return capture;
                }
            }
            lastError = "Android would not start recording from that microphone.";
        } catch (SecurityException e) {
            lastError = "Microphone permission is needed.";
        } catch (Throwable t) {
            lastError = String.valueOf(t);
        }
        return null;
    }

    private static AudioRecord create(int source, int rate) {
        try {
            int min = AudioRecord.getMinBufferSize(rate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
            if (min <= 0) return null;
            // Room for a quarter of a second, so a busy moment loses nothing.
            int bytes = Math.max(min * 2, rate / 2);
            AudioRecord record = new AudioRecord.Builder()
                    .setAudioSource(source)
                    .setAudioFormat(new AudioFormat.Builder()
                            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                            .setSampleRate(rate)
                            .setChannelMask(AudioFormat.CHANNEL_IN_MONO)
                            .build())
                    .setBufferSizeInBytes(bytes)
                    .build();
            if (record.getState() != AudioRecord.STATE_INITIALIZED) {
                record.release();
                return null;
            }
            return record;
        } catch (Throwable t) {
            return null;
        }
    }

    private void pump() {
        // A hundredth of a second at a time keeps the delay to the game small.
        short[] block = new short[Math.max(32, sampleRate / 100)];
        while (running) {
            int got;
            try {
                got = record.read(block, 0, block.length);
            } catch (Throwable t) {
                got = -1;
            }
            if (got < 0) {
                // The microphone went away (unplugged, or Android took it back).
                dead = true;
                break;
            }
            if (got == 0) continue;
            // Played back straight from here, so the voice is not held up by the game
            YargVoiceMonitor m = monitor;
            if (m != null) m.write(block, got);
            synchronized (lock) {
                for (int i = 0; i < got; i++) {
                    ring[writeAt] = block[i];
                    writeAt++;
                    if (writeAt == ring.length) writeAt = 0;
                }
                filled = Math.min(ring.length, filled + got);
                written += got;
                // If the game has stopped taking sound, keep only the newest bit: singing must
                // never be judged on something sung a moment ago.
                int most = chunk * 4;
                if (filled > most) filled = chunk;
            }
        }
    }

    public int getSampleRate() {
        return sampleRate;
    }

    /** Samples in each chunk {@link #take} returns. */
    public int getChunkSamples() {
        return chunk;
    }

    /** How many chunks of sound are waiting to be taken. */
    public int available() {
        synchronized (lock) {
            return filled / chunk;
        }
    }

    /** The oldest waiting chunk of sound (16-bit samples). Only call when {@link #available} is above zero. */
    public short[] take() {
        short[] out = new short[chunk];
        synchronized (lock) {
            if (filled < chunk) return out;
            lastTakeFirst = written - filled;
            int from = writeAt - filled;
            if (from < 0) from += ring.length;
            for (int i = 0; i < chunk; i++) {
                out[i] = ring[from];
                from++;
                if (from == ring.length) from = 0;
            }
            filled -= chunk;
        }
        return out;
    }

    /**
     * Where the chunk last returned by {@link #take} began, counted in samples since recording
     * started (-1 before the first chunk). A jump from the end of one chunk to the start of the
     * next is sound that was thrown away in between.
     */
    public long getLastTakeFirst() {
        synchronized (lock) {
            return lastTakeFirst;
        }
    }

    /** Forgets the sound that is waiting (at the start of a song). */
    public void flush() {
        synchronized (lock) {
            filled = 0;
        }
    }

    /** True once the microphone has gone away; the recorder then has to be opened again. */
    public boolean isDead() {
        return dead;
    }

    /**
     * Whether Android is really feeding this recorder from the kind of microphone asked for.
     * Some phones can only listen to one microphone at a time and quietly give every recorder the
     * same one; this is how the game finds out. True when Android does not say.
     */
    public boolean isRoutedAsAsked() {
        try {
            AudioDeviceInfo routed = record.getRoutedDevice();
            return routed == null || kindOf(routed) == wantedKind;
        } catch (Throwable t) {
            return true;
        }
    }

    /**
     * How loud the singer hears their own voice: 0 is off. The player is made the first time it
     * is turned up, on the output the game's sound is on.
     */
    public void setMonitorLevel(float gain) {
        monitorGain = gain;
        YargVoiceMonitor m = monitor;
        if (m == null && gain > 0f) {
            m = YargVoiceMonitor.open(sampleRate, 0);
            if (m != null) m.setOutput(appContext, monitorOutput);
            monitor = m;
        }
        if (m != null) m.setGain(gain);
    }

    /** Plays the voice back on this output device (0: where Android chooses), as the game's sound. */
    public void setMonitorOutput(int deviceId) {
        monitorOutput = deviceId;
        YargVoiceMonitor m = monitor;
        if (m != null) m.setOutput(appContext, deviceId);
    }

    /** How many times the voice played back squealed and was turned down, since last asked. */
    public int takeMonitorSqueals() {
        YargVoiceMonitor m = monitor;
        return m == null ? 0 : m.takeSqueals();
    }

    /** Whether the voice is played back over Bluetooth (which adds a delay of its own). */
    public boolean isMonitorOnBluetooth() {
        YargVoiceMonitor m = monitor;
        return m != null && monitorGain > 0f && m.isBluetooth();
    }

    public void close() {
        running = false;
        YargVoiceMonitor m = monitor;
        monitor = null;
        if (m != null) m.close();
        try {
            record.stop();
        } catch (Throwable ignored) {
        }
        try {
            thread.join(300);
        } catch (InterruptedException ignored) {
            Thread.currentThread().interrupt();
        }
        try {
            record.release();
        } catch (Throwable ignored) {
        }
    }

    /** Android version check for the game: choosing an input needs Android 6. */
    public static boolean isSupported() {
        return Build.VERSION.SDK_INT >= 23;
    }
}
