// YARG Resurrected - asks Android for its fastest Bluetooth setting for game controllers.
package in.yarg.resurrected;

import android.app.Activity;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.util.Log;
import android.view.InputDevice;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * A Bluetooth controller and the phone agree how often they talk. For a Bluetooth Low Energy
 * controller the phone normally settles on a relaxed pace (every 30 to 50 thousandths of a second)
 * to save power; an app may ask for the fast one (every 11 to 15). That request is all this does:
 * it opens its own connection to each paired Low Energy controller that is in use as a game
 * controller, and asks for high priority. Both connections share one radio link, so the
 * controller's buttons arrive at the fast pace too.
 *
 * A controller on classic Bluetooth (not Low Energy) sets its own pace; an app cannot change it.
 * This says which kind each controller is, so the game can tell the player.
 *
 * Needs the "Nearby devices" permission (BLUETOOTH_CONNECT) on Android 12 and later; the game asks
 * for it. Without it nothing is done.
 */
public final class YargBluetoothBoost {
    private static final String TAG = "YARG";

    private static final Map<String, BluetoothGatt> sLinks = new HashMap<>();
    private static final Map<String, String> sState = new HashMap<>();
    private static volatile String sStatus = "Bluetooth controllers: not looked at yet";

    private YargBluetoothBoost() {}

    /** What was found and done, in words, one controller a line. */
    public static String status() {
        return sStatus;
    }

    /** Whether the game may look at paired Bluetooth devices. */
    public static boolean allowed(Activity activity) {
        try {
            if (Build.VERSION.SDK_INT < 31) return true;
            return activity.checkSelfPermission("android.permission.BLUETOOTH_CONNECT") == PackageManager.PERMISSION_GRANTED;
        } catch (Throwable t) {
            return false;
        }
    }

    /** The names of the game controllers plugged in or paired right now (not the phone's own buttons). */
    private static Set<String> controllerNames() {
        Set<String> names = new HashSet<>();
        for (int id : InputDevice.getDeviceIds()) {
            InputDevice device = InputDevice.getDevice(id);
            if (device == null || device.isVirtual()) continue;
            int sources = device.getSources();
            boolean pad = (sources & InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD
                    || (sources & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK;
            if (pad && device.getName() != null) names.add(device.getName());
        }
        return names;
    }

    /**
     * Asks for the fast pace for every paired Low Energy device that is a game controller right
     * now. Safe to call again and again (before each song): a controller already asked for is left.
     */
    public static synchronized String apply(final Activity activity) {
        try {
            if (!allowed(activity)) {
                sStatus = "Bluetooth controllers: the game is not allowed to see them (Nearby devices permission)";
                return sStatus;
            }

            BluetoothManager manager = (BluetoothManager) activity.getSystemService(Context.BLUETOOTH_SERVICE);
            BluetoothAdapter adapter = manager == null ? null : manager.getAdapter();
            if (adapter == null || !adapter.isEnabled()) {
                sStatus = "Bluetooth controllers: Bluetooth is off";
                return sStatus;
            }

            Set<String> inUse = controllerNames();
            List<String> lines = new ArrayList<>();
            Set<String> seen = new HashSet<>();
            for (final BluetoothDevice device : adapter.getBondedDevices()) {
                final String name = device.getName();
                if (name == null || !inUse.contains(name)) continue;
                final String address = device.getAddress();
                seen.add(address);

                int type = device.getType();
                if (type == BluetoothDevice.DEVICE_TYPE_LE) {
                    if (!sLinks.containsKey(address)) {
                        sState.put(address, "asking for the fast connection");
                        BluetoothGatt gatt = device.connectGatt(activity, false, new BluetoothGattCallback() {
                            @Override
                            public void onConnectionStateChange(BluetoothGatt g, int status, int newState) {
                                try {
                                    if (newState == BluetoothProfile.STATE_CONNECTED) {
                                        boolean asked = g.requestConnectionPriority(BluetoothGatt.CONNECTION_PRIORITY_HIGH);
                                        sState.put(address, asked ? "fast connection asked for" : "the phone refused the fast connection");
                                        Log.i(TAG, "Bluetooth boost: " + name + ": high priority asked: " + asked);
                                    } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                                        sState.put(address, "disconnected");
                                        synchronized (YargBluetoothBoost.class) {
                                            BluetoothGatt old = sLinks.remove(address);
                                            if (old != null) old.close();
                                        }
                                    }
                                } catch (Throwable t) {
                                    sState.put(address, "could not ask (" + t.getClass().getSimpleName() + ")");
                                }
                            }
                        }, BluetoothDevice.TRANSPORT_LE);
                        if (gatt != null) sLinks.put(address, gatt);
                        else sState.put(address, "could not open a connection");
                    }
                    lines.add(name + ": Bluetooth Low Energy, " + sState.get(address));
                } else if (type == BluetoothDevice.DEVICE_TYPE_DUAL) {
                    lines.add(name + ": classic and Low Energy Bluetooth; left alone (its pace is its own)");
                } else {
                    lines.add(name + ": classic Bluetooth; it sets its own pace, the game cannot change it");
                }
            }

            // A controller that is no longer in use: let its extra connection go
            List<String> gone = new ArrayList<>();
            for (String address : sLinks.keySet()) {
                if (!seen.contains(address)) gone.add(address);
            }
            for (String address : gone) {
                BluetoothGatt old = sLinks.remove(address);
                if (old != null) old.close();
                sState.remove(address);
            }

            sStatus = lines.isEmpty()
                    ? "Bluetooth controllers: none of the controllers in use is a paired Bluetooth device (wired, a USB receiver, or named differently)"
                    : "Bluetooth controllers: " + String.join("; ", lines);
            return sStatus;
        } catch (Throwable t) {
            Log.w(TAG, "Bluetooth boost failed", t);
            sStatus = "Bluetooth controllers: could not be looked at (" + t.getClass().getSimpleName() + ")";
            return sStatus;
        }
    }

    /** Lets every extra connection go (the controllers stay connected as controllers). */
    public static synchronized void release() {
        for (BluetoothGatt gatt : sLinks.values()) {
            try {
                gatt.close();
            } catch (Throwable ignored) {
            }
        }
        sLinks.clear();
        sState.clear();
    }
}
