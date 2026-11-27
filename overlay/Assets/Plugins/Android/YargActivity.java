package in.yarg.resurrected;

import android.os.Bundle;
import android.util.Log;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.View;

import com.unity3d.player.UnityPlayerActivity;

/**
 * The game's activity: Unity's own, with every movement of a controller's sticks, triggers and
 * D-pad passed on one by one.
 *
 * Why. A guitar controller's strum bar is its D-pad, and on Android a D-pad is not buttons but a
 * pair of axes ("hat"), reported in motion events. Android saves motion events up and hands them
 * over once per screen frame, as ONE event that carries the newest position and the earlier ones
 * as "history" (key events, such as the fret buttons, are never saved up). A quick strum goes
 * middle, down, middle inside a single frame: the event that arrives says "middle", and the strum
 * is only in its history. A game that reads the newest position alone never sees that strum.
 *
 * What this does. (1) Asks Android not to save up controller motion events at all, where it can
 * (Android 11 and later). (2) Where an event still arrives with history, hands each earlier
 * position to Unity as an event of its own, with the time it really happened, before the newest.
 * Nothing changes for events without history, for touches or for keys.
 */
public class YargActivity extends UnityPlayerActivity {
    private static final String TAG = "YARG";

    /** Controller motion events that arrived with earlier positions inside, and how many of those there were. */
    private static volatile int sBatches, sRecovered;
    /** Whether Android agreed to deliver controller motion events as they happen. */
    private static volatile boolean sUnbuffered;

    public static int batches() {
        return sBatches;
    }

    public static int recovered() {
        return sRecovered;
    }

    public static boolean unbuffered() {
        return sUnbuffered;
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        requestUnbufferedControllers();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) {
            requestUnbufferedControllers();
        }
    }

    /**
     * View.requestUnbufferedDispatch(int source) exists from Android 11 (API 30). Called by name so
     * the same code runs on older phones, where step (2) does the work instead.
     */
    private void requestUnbufferedControllers() {
        try {
            final View view = getWindow().getDecorView();
            view.post(new Runnable() {
                @Override
                public void run() {
                    try {
                        View.class.getMethod("requestUnbufferedDispatch", int.class)
                                .invoke(view, InputDevice.SOURCE_CLASS_JOYSTICK);
                        if (!sUnbuffered) {
                            Log.i(TAG, "Controller motion events are delivered as they happen");
                        }
                        sUnbuffered = true;
                    } catch (Throwable e) {
                        // Older Android: batches are taken apart in dispatchGenericMotionEvent
                    }
                }
            });
        } catch (Throwable e) {
            // No window yet; onWindowFocusChanged tries again
        }
    }

    @Override
    public boolean dispatchGenericMotionEvent(MotionEvent event) {
        try {
            int history = event.getHistorySize();
            if (history > 0 && event.getActionMasked() == MotionEvent.ACTION_MOVE
                    && (event.getSource() & InputDevice.SOURCE_CLASS_JOYSTICK) != 0) {
                return dispatchOneByOne(event, history);
            }
        } catch (Throwable e) {
            Log.w(TAG, "Could not take a controller event apart: " + e);
        }
        return super.dispatchGenericMotionEvent(event);
    }

    private boolean dispatchOneByOne(MotionEvent event, int history) {
        int pointers = event.getPointerCount();
        MotionEvent.PointerProperties[] properties = new MotionEvent.PointerProperties[pointers];
        MotionEvent.PointerCoords[] coords = new MotionEvent.PointerCoords[pointers];
        for (int i = 0; i < pointers; i++) {
            properties[i] = new MotionEvent.PointerProperties();
            event.getPointerProperties(i, properties[i]);
            coords[i] = new MotionEvent.PointerCoords();
        }

        boolean handled = false;
        // The earlier positions, oldest first, then the newest: history + 1 events in all
        for (int h = 0; h <= history; h++) {
            long time;
            if (h < history) {
                for (int i = 0; i < pointers; i++) {
                    event.getHistoricalPointerCoords(i, h, coords[i]);
                }
                time = event.getHistoricalEventTime(h);
            } else {
                for (int i = 0; i < pointers; i++) {
                    event.getPointerCoords(i, coords[i]);
                }
                time = event.getEventTime();
            }

            MotionEvent one = MotionEvent.obtain(event.getDownTime(), time, event.getAction(), pointers,
                    properties, coords, event.getMetaState(), event.getButtonState(), event.getXPrecision(),
                    event.getYPrecision(), event.getDeviceId(), event.getEdgeFlags(), event.getSource(),
                    event.getFlags());
            try {
                handled |= super.dispatchGenericMotionEvent(one);
            } finally {
                one.recycle();
            }
        }

        sBatches++;
        sRecovered += history;
        return handled;
    }
}
