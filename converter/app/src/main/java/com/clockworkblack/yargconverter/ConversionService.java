package com.clockworkblack.yargconverter;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import android.os.SystemClock;

/**
 * Keeps a conversion going while the app is in the background or the screen is off.
 *
 * The conversion itself runs on its own thread (started by MainActivity). While it runs, this
 * foreground service keeps the app's process alive and holds a partial wake lock so the processor
 * doesn't sleep, and shows a notification with the progress. When the conversion ends the service
 * stops and a "Done" (or "failed") notification is left that opens the app.
 *
 * The state of the conversion is kept here in static fields, so a MainActivity that was closed and
 * reopened in the meantime can pick it up again (see {@link Listener}).
 */
public final class ConversionService extends Service {
    private static final String CHANNEL_PROGRESS = "convert";
    private static final String CHANNEL_DONE = "convert_done";
    private static final int ID_PROGRESS = 1;
    private static final int ID_DONE = 2;

    /** Told about every change, on the main thread. MainActivity sets it while it's on screen. */
    public interface Listener {
        void onConversionChanged();
    }

    private static final Handler MAIN = new Handler(Looper.getMainLooper());

    // ------------------------------------------------------------------ the conversion's state

    private static volatile boolean running;
    private static volatile String songTitle = "";
    private static volatile String text = "";
    /** "Song 3 of 12: Title" while a list of songs is converted, put before the text; empty otherwise. */
    private static volatile String batchLabel = "";
    /** 0 to 1000, or -1 while the length of the work isn't known yet. */
    private static volatile int progress = -1;
    private static volatile String result;
    private static volatile boolean succeeded;
    /** True once the result has been shown in the app, so it isn't shown twice. */
    private static volatile boolean resultSeen = true;
    /** Lyrics the conversion wants shown back in the app (singers it worked out), or null. */
    private static volatile String markedLyrics;

    private static Listener listener;
    private static ConversionService instance;
    private static Context appContext;
    private static long lastNotified;

    public static boolean isRunning() {
        return running;
    }

    public static String text() {
        String label = batchLabel;
        return label.isEmpty() ? text : label + "\n" + text;
    }

    /** For a list of songs: which one is being converted. Empty for a single song. */
    public static void setBatchLabel(String value) {
        batchLabel = value == null ? "" : value;
        changed(true);
    }

    public static int progress() {
        return progress;
    }

    /** The summary of the last conversion, or null if there's none that hasn't been seen yet. */
    public static String unseenResult() {
        return resultSeen ? null : result;
    }

    public static boolean succeeded() {
        return succeeded;
    }

    /** Hands the app lyrics with the singers the conversion worked out, to show when it is next on screen. */
    public static void setMarkedLyrics(String value) {
        markedLyrics = value;
    }

    /** Those lyrics, once. */
    public static String takeMarkedLyrics() {
        String value = markedLyrics;
        markedLyrics = null;
        return value;
    }

    public static void markResultSeen() {
        resultSeen = true;
        if (appContext != null) {
            NotificationManager nm = appContext.getSystemService(NotificationManager.class);
            if (nm != null) nm.cancel(ID_DONE);
        }
    }

    /** Must be called on the main thread. */
    public static void setListener(Listener value) {
        listener = value;
    }

    // ------------------------------------------------------------------ called by the conversion

    /** Starts the service for a new conversion. Call from the app while it's on screen. */
    public static void begin(Context context, String title) {
        appContext = context.getApplicationContext();
        songTitle = title == null ? "" : title;
        text = "Getting started…";
        progress = -1;
        result = null;
        resultSeen = true;
        markedLyrics = null;
        batchLabel = "";
        running = true;

        NotificationManager nm = appContext.getSystemService(NotificationManager.class);
        if (nm != null) nm.cancel(ID_DONE);

        Intent intent = new Intent(appContext, ConversionService.class);
        try {
            if (Build.VERSION.SDK_INT >= 26) {
                appContext.startForegroundService(intent);
            } else {
                appContext.startService(intent);
            }
        } catch (RuntimeException e) {
            // Not allowed to start (shouldn't happen while the app is open). The conversion still
            // runs; it just won't survive the app going to the background as reliably.
        }
        changed(true);
    }

    public static void setText(String value) {
        text = value;
        changed(true);
    }

    public static void setProgress(int value) {
        progress = value;
        changed(false);
    }

    public static void setTextAndProgress(String value, int progressValue) {
        text = value;
        progress = progressValue;
        changed(true);
    }

    /** The conversion is over. Shows the result in the app if it's open, or as a notification. */
    public static void finish(boolean ok, String summary) {
        succeeded = ok;
        result = summary;
        resultSeen = false;
        running = false;
        MAIN.post(new Runnable() {
            @Override
            public void run() {
                if (instance != null) {
                    instance.stopForegroundNow();
                    instance.stopSelf();
                }
                postDoneNotification();
                if (listener != null) listener.onConversionChanged();
            }
        });
    }

    private static void changed(final boolean important) {
        MAIN.post(new Runnable() {
            @Override
            public void run() {
                if (listener != null) listener.onConversionChanged();

                // Notifications are rate-limited by Android: only update a few times a second,
                // unless the text itself changed
                long now = SystemClock.elapsedRealtime();
                if (instance != null && running && (important || now - lastNotified > 700)) {
                    lastNotified = now;
                    NotificationManager nm = instance.getSystemService(NotificationManager.class);
                    if (nm != null) nm.notify(ID_PROGRESS, instance.buildProgress());
                }
            }
        });
    }

    // ------------------------------------------------------------------ the service

    private PowerManager.WakeLock wakeLock;

    @Override
    public void onCreate() {
        super.onCreate();
        instance = this;
        createChannels(this);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        Notification notification = buildProgress();
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(ID_PROGRESS, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);
        } else {
            startForeground(ID_PROGRESS, notification);
        }

        if (wakeLock == null) {
            PowerManager pm = getSystemService(PowerManager.class);
            if (pm != null) {
                wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "YARGConverter:convert");
                wakeLock.setReferenceCounted(false);
                // Long enough for the slowest phone to split a long song; released when done
                wakeLock.acquire(3 * 60 * 60 * 1000L);
            }
        }

        // The conversion may have ended before the service got going
        if (!running) {
            stopForegroundNow();
            stopSelf();
        }
        return START_NOT_STICKY;
    }

    private void stopForegroundNow() {
        if (Build.VERSION.SDK_INT >= 24) {
            stopForeground(STOP_FOREGROUND_REMOVE);
        } else {
            stopForeground(true);
        }
    }

    @Override
    public void onDestroy() {
        if (wakeLock != null && wakeLock.isHeld()) {
            wakeLock.release();
        }
        wakeLock = null;
        if (instance == this) instance = null;
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    // ------------------------------------------------------------------ notifications

    private static void createChannels(Context context) {
        if (Build.VERSION.SDK_INT < 26) return;
        NotificationManager nm = context.getSystemService(NotificationManager.class);
        if (nm == null) return;

        NotificationChannel progressChannel = new NotificationChannel(CHANNEL_PROGRESS,
                "Converting", NotificationManager.IMPORTANCE_LOW);
        progressChannel.setDescription("Shows the progress of a song being converted.");
        progressChannel.setShowBadge(false);
        nm.createNotificationChannel(progressChannel);

        NotificationChannel doneChannel = new NotificationChannel(CHANNEL_DONE,
                "Finished songs", NotificationManager.IMPORTANCE_DEFAULT);
        doneChannel.setDescription("Tells you when a song is ready to play in YARG.");
        nm.createNotificationChannel(doneChannel);
    }

    private static PendingIntent openApp(Context context) {
        Intent open = new Intent(context, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        return PendingIntent.getActivity(context, 0, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }

    private static Notification.Builder builder(Context context, String channel) {
        if (Build.VERSION.SDK_INT >= 26) {
            return new Notification.Builder(context, channel);
        }
        //noinspection deprecation
        return new Notification.Builder(context);
    }

    private Notification buildProgress() {
        String title = songTitle.isEmpty() ? "Converting a song" : "Converting \"" + songTitle + "\"";
        Notification.Builder b = builder(this, CHANNEL_PROGRESS)
                .setSmallIcon(R.drawable.ic_launcher)
                .setContentTitle(title)
                .setContentText(text())
                .setStyle(new Notification.BigTextStyle().bigText(text()))
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setContentIntent(openApp(this));
        int p = progress;
        b.setProgress(1000, Math.max(0, p), p < 0);
        if (Build.VERSION.SDK_INT >= 31) {
            b.setForegroundServiceBehavior(Notification.FOREGROUND_SERVICE_IMMEDIATE);
        }
        return b.build();
    }

    private static void postDoneNotification() {
        Context context = appContext;
        if (context == null) return;
        createChannels(context);
        NotificationManager nm = context.getSystemService(NotificationManager.class);
        if (nm == null) return;
        nm.cancel(ID_PROGRESS);

        String name = songTitle.isEmpty() ? "The song" : "\"" + songTitle + "\"";
        String title = succeeded ? name + " is ready" : "Conversion failed";
        String body = succeeded
                ? "Open YARG and scan for songs (Settings > Songs > Scan Songs) to play it."
                : String.valueOf(result);
        Notification n = builder(context, CHANNEL_DONE)
                .setSmallIcon(R.drawable.ic_launcher)
                .setContentTitle(title)
                .setContentText(body)
                .setStyle(new Notification.BigTextStyle().bigText(body))
                .setAutoCancel(true)
                .setContentIntent(openApp(context))
                .build();
        try {
            nm.notify(ID_DONE, n);
        } catch (SecurityException e) {
            // Notifications not allowed: the result is still shown when the app is opened
        }
    }
}
