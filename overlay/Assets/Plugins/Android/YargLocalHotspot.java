// YARG Resurrected - starts a private "game network" other phones can join.
//
// Android doesn't let apps switch on the normal (internet-sharing) hotspot, but it does let an
// app start a local-only hotspot: a Wi-Fi network with a name and password chosen by Android and
// no internet access. That is all the phone remote needs.
package in.yarg.resurrected;

import android.content.Context;
import android.net.wifi.WifiConfiguration;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;

public final class YargLocalHotspot {
    public static final int OFF = 0;
    public static final int STARTING = 1;
    public static final int ON = 2;
    public static final int FAILED = 3;

    private static volatile int state = OFF;
    private static volatile String ssid = "";
    private static volatile String password = "";
    private static volatile String error = "";
    private static WifiManager.LocalOnlyHotspotReservation reservation;

    private YargLocalHotspot() {
    }

    public static int getState() {
        return state;
    }

    public static String getSsid() {
        return ssid;
    }

    public static String getPassword() {
        return password;
    }

    public static String getError() {
        return error;
    }

    public static void start(Context context) {
        if (state == STARTING || state == ON) {
            return;
        }
        state = STARTING;
        error = "";

        final Context app = context.getApplicationContext();
        new Handler(Looper.getMainLooper()).post(new Runnable() {
            @Override
            public void run() {
                try {
                    WifiManager wifi = (WifiManager) app.getSystemService(Context.WIFI_SERVICE);
                    wifi.startLocalOnlyHotspot(new WifiManager.LocalOnlyHotspotCallback() {
                        @Override
                        public void onStarted(WifiManager.LocalOnlyHotspotReservation started) {
                            reservation = started;
                            try {
                                readCredentials(started);
                                state = ON;
                            } catch (Throwable t) {
                                error = "Couldn't read the network name: " + t;
                                state = FAILED;
                            }
                        }

                        @Override
                        public void onStopped() {
                            reservation = null;
                            state = OFF;
                        }

                        @Override
                        public void onFailed(int reason) {
                            reservation = null;
                            switch (reason) {
                                case ERROR_NO_CHANNEL:
                                    error = "No free Wi-Fi channel. Try again, or turn Wi-Fi off first.";
                                    break;
                                case ERROR_TETHERING_DISALLOWED:
                                    error = "Hotspots are blocked on this phone.";
                                    break;
                                case ERROR_INCOMPATIBLE_MODE:
                                    error = "Turn off the regular hotspot first.";
                                    break;
                                default:
                                    error = "Android couldn't start the network (code " + reason + ").";
                                    break;
                            }
                            state = FAILED;
                        }
                    }, new Handler(Looper.getMainLooper()));
                } catch (SecurityException e) {
                    error = "Permission needed: allow Nearby devices / Location, and turn Location on.";
                    state = FAILED;
                } catch (Throwable t) {
                    error = String.valueOf(t.getMessage() != null ? t.getMessage() : t);
                    state = FAILED;
                }
            }
        });
    }

    @SuppressWarnings("deprecation")
    private static void readCredentials(WifiManager.LocalOnlyHotspotReservation started) {
        if (Build.VERSION.SDK_INT >= 30) {
            android.net.wifi.SoftApConfiguration config = started.getSoftApConfiguration();
            String name = config.getSsid();
            ssid = name != null ? name : "";
            String pass = config.getPassphrase();
            password = pass != null ? pass : "";
        } else {
            WifiConfiguration config = started.getWifiConfiguration();
            ssid = config != null && config.SSID != null ? config.SSID : "";
            password = config != null && config.preSharedKey != null ? config.preSharedKey : "";
        }

        // Some versions wrap the name in quotes.
        if (ssid.length() >= 2 && ssid.startsWith("\"") && ssid.endsWith("\"")) {
            ssid = ssid.substring(1, ssid.length() - 1);
        }
    }

    public static void stop() {
        new Handler(Looper.getMainLooper()).post(new Runnable() {
            @Override
            public void run() {
                try {
                    if (reservation != null) {
                        reservation.close();
                    }
                } catch (Throwable ignored) {
                }
                reservation = null;
                state = OFF;
            }
        });
    }
}
