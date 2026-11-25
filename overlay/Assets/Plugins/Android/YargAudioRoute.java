package in.yarg.resurrected;

import android.content.Context;
import android.media.AudioAttributes;
import android.media.AudioDeviceInfo;
import android.media.AudioManager;
import android.os.Build;

import java.util.List;

/**
 * Finds the sound outputs connected to the phone (its own speaker, a TV over HDMI, a USB sound
 * device, Bluetooth, wired headphones), so the game can play through the one the player picked
 * rather than wherever Android sends it. Android sends all sound to a USB device with a headphone
 * output, such as a USB microphone, the moment one is plugged in.
 */
public final class YargAudioRoute {
    public static final int KIND_SPEAKER = 1;
    public static final int KIND_TV = 2;
    public static final int KIND_USB = 3;
    public static final int KIND_BLUETOOTH = 4;
    public static final int KIND_HEADPHONES = 5;

    private YargAudioRoute() {
    }

    /** Output device types for each kind, best first. */
    private static int[] typesFor(int kind) {
        switch (kind) {
            case KIND_SPEAKER:
                return new int[] {AudioDeviceInfo.TYPE_BUILTIN_SPEAKER};
            case KIND_TV:
                // 29 = HDMI eARC (Android 12). A USB-C hub's HDMI port shows up as HDMI, or on some
                // phones as a dock or a digital line out.
                return new int[] {AudioDeviceInfo.TYPE_HDMI, AudioDeviceInfo.TYPE_HDMI_ARC, 29,
                        AudioDeviceInfo.TYPE_DOCK, AudioDeviceInfo.TYPE_LINE_DIGITAL, AudioDeviceInfo.TYPE_AUX_LINE};
            case KIND_USB:
                return new int[] {AudioDeviceInfo.TYPE_USB_HEADSET, AudioDeviceInfo.TYPE_USB_DEVICE, AudioDeviceInfo.TYPE_USB_ACCESSORY};
            case KIND_BLUETOOTH:
                // 27 = BLE speaker, 26 = BLE headset (Android 12)
                return new int[] {AudioDeviceInfo.TYPE_BLUETOOTH_A2DP, 27, 26};
            case KIND_HEADPHONES:
                return new int[] {AudioDeviceInfo.TYPE_WIRED_HEADPHONES, AudioDeviceInfo.TYPE_WIRED_HEADSET, AudioDeviceInfo.TYPE_LINE_ANALOG};
            default:
                return new int[0];
        }
    }

    private static AudioDeviceInfo[] outputs(Context context) {
        AudioManager am = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        if (am == null) return new AudioDeviceInfo[0];
        return am.getDevices(AudioManager.GET_DEVICES_OUTPUTS);
    }

    /** The id of the connected output of this kind, or 0 if there is none. */
    public static int find(Context context, int kind) {
        try {
            AudioDeviceInfo[] all = outputs(context);
            for (int type : typesFor(kind)) {
                for (AudioDeviceInfo d : all) {
                    if (d.isSink() && d.getType() == type) return d.getId();
                }
            }
        } catch (RuntimeException ignored) {
            // No audio service: nothing to find
        }
        return 0;
    }

    /**
     * Whether Android already sends game and music sound to this output, so there is nothing to
     * redirect. Only known from Android 13; before that this says no.
     */
    public static boolean isDefault(Context context, int deviceId) {
        if (deviceId == 0 || Build.VERSION.SDK_INT < 33) return false;
        try {
            AudioManager am = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
            AudioAttributes attributes = new AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_GAME)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                    .build();
            List<AudioDeviceInfo> routed = am.getAudioDevicesForAttributes(attributes);
            if (routed == null) return false;
            for (AudioDeviceInfo d : routed) if (d.getId() == deviceId) return true;
        } catch (RuntimeException ignored) {
            // Can't tell: redirect to be sure
        }
        return false;
    }

    /** A name for an output to show the player, like "Galaxy Z Fold (speaker)". */
    public static String name(Context context, int deviceId) {
        try {
            for (AudioDeviceInfo d : outputs(context)) {
                if (d.getId() != deviceId) continue;
                CharSequence product = d.getProductName();
                String label = product == null ? "" : product.toString().trim();
                String type = typeName(d.getType());
                return label.isEmpty() ? type : label + " (" + type + ")";
            }
        } catch (RuntimeException ignored) {
            // Fall through
        }
        return "output " + deviceId;
    }

    /** Every output, for the game's log. */
    public static String describe(Context context) {
        StringBuilder sb = new StringBuilder();
        try {
            for (AudioDeviceInfo d : outputs(context)) {
                if (sb.length() > 0) sb.append("; ");
                sb.append(d.getId()).append(' ').append(typeName(d.getType())).append(" \"").append(d.getProductName()).append('"');
            }
        } catch (RuntimeException e) {
            sb.append("(could not list: ").append(e.getMessage()).append(')');
        }
        return sb.toString();
    }

    private static String typeName(int type) {
        switch (type) {
            case AudioDeviceInfo.TYPE_BUILTIN_SPEAKER: return "speaker";
            case AudioDeviceInfo.TYPE_BUILTIN_EARPIECE: return "earpiece";
            case AudioDeviceInfo.TYPE_HDMI: return "HDMI";
            case AudioDeviceInfo.TYPE_HDMI_ARC: return "HDMI ARC";
            case 29: return "HDMI eARC";
            case AudioDeviceInfo.TYPE_DOCK: return "dock";
            case AudioDeviceInfo.TYPE_LINE_DIGITAL: return "digital out";
            case AudioDeviceInfo.TYPE_LINE_ANALOG: return "line out";
            case AudioDeviceInfo.TYPE_AUX_LINE: return "aux out";
            case AudioDeviceInfo.TYPE_USB_DEVICE: return "USB";
            case AudioDeviceInfo.TYPE_USB_HEADSET: return "USB headset";
            case AudioDeviceInfo.TYPE_USB_ACCESSORY: return "USB accessory";
            case AudioDeviceInfo.TYPE_BLUETOOTH_A2DP: return "Bluetooth";
            case AudioDeviceInfo.TYPE_BLUETOOTH_SCO: return "Bluetooth call audio";
            case 26: return "Bluetooth LE headset";
            case 27: return "Bluetooth LE speaker";
            case AudioDeviceInfo.TYPE_WIRED_HEADPHONES: return "headphones";
            case AudioDeviceInfo.TYPE_WIRED_HEADSET: return "headset";
            default: return "type " + type;
        }
    }
}
