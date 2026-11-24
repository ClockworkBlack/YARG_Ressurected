package com.clockworkblack.yargconverter;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Typeface;
import android.media.MediaPlayer;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import com.clockworkblack.yargconverter.core.Lrc;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Plays the song and lets the user tap once as each lyric line starts. The taps become the
 * line times, which is how pasted (untimed) lyrics get synced to the music.
 */
public final class TapSyncActivity extends Activity {
    static final String EXTRA_LINES = "lines";
    static final String EXTRA_RESULT = "lrc";

    /** People tap slightly after they hear a line start; this pulls each time back a touch. */
    private static final double REACTION_SECONDS = 0.12;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private MediaPlayer player;
    private List<String> lines = new ArrayList<>();
    private double[] times;
    private int next;

    private TextView clock, previous, current, upcoming, counter;
    private Button tapButton, playButton, doneButton;

    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            if (player != null) {
                int ms = safePosition();
                clock.setText(String.format(Locale.US, "%d:%02d", ms / 60000, (ms / 1000) % 60));
                playButton.setText(player.isPlaying() ? "Pause" : "Play");
            }
            handler.postDelayed(this, 200);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        Uri uri = getIntent().getData();
        ArrayList<String> extra = getIntent().getStringArrayListExtra(EXTRA_LINES);
        if (uri == null || extra == null || extra.isEmpty()) {
            finish();
            return;
        }
        lines = extra;
        times = new double[lines.size()];
        buildUi();
        try {
            player = MediaPlayer.create(this, uri);
        } catch (RuntimeException e) {
            player = null;
        }
        if (player == null) {
            Toast.makeText(this, "This song could not be played for syncing.", Toast.LENGTH_LONG).show();
            finish();
            return;
        }
        show();
        handler.post(tick);
    }

    private int dp(float v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    private void buildUi() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(20), dp(20), dp(20));
        root.setFitsSystemWindows(true);

        TextView help = new TextView(this);
        help.setText("Press Play, then tap the big button at the moment each highlighted line starts being sung.");
        root.addView(help);

        LinearLayout top = new LinearLayout(this);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setPadding(0, dp(8), 0, dp(8));
        clock = new TextView(this);
        clock.setTextSize(20);
        clock.setText("0:00");
        top.addView(clock, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        counter = new TextView(this);
        counter.setTextSize(16);
        counter.setGravity(Gravity.END);
        top.addView(counter, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        root.addView(top);

        previous = new TextView(this);
        previous.setAlpha(0.5f);
        previous.setMaxLines(2);
        root.addView(previous);
        current = new TextView(this);
        current.setTextSize(22);
        current.setTypeface(Typeface.DEFAULT_BOLD);
        current.setPadding(0, dp(10), 0, dp(10));
        root.addView(current);
        upcoming = new TextView(this);
        upcoming.setAlpha(0.7f);
        upcoming.setMaxLines(4);
        root.addView(upcoming);

        tapButton = new Button(this);
        tapButton.setText("TAP");
        tapButton.setTextSize(30);
        tapButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                tap();
            }
        });
        LinearLayout.LayoutParams big = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f);
        big.topMargin = dp(12);
        big.bottomMargin = dp(12);
        root.addView(tapButton, big);

        LinearLayout controls = new LinearLayout(this);
        controls.setOrientation(LinearLayout.HORIZONTAL);
        playButton = small("Play", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (player == null) return;
                if (player.isPlaying()) player.pause();
                else player.start();
            }
        });
        controls.addView(playButton, cell());
        controls.addView(small("Undo line", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                undo();
            }
        }), cell());
        controls.addView(small("Back 5 s", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (player != null) player.seekTo(Math.max(0, safePosition() - 5000));
            }
        }), cell());
        controls.addView(small("Start over", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                next = 0;
                if (player != null) player.seekTo(0);
                show();
            }
        }), cell());
        root.addView(controls);

        doneButton = small("Use these timings", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                finishWithResult();
            }
        });
        root.addView(doneButton);
        setContentView(root);
    }

    private Button small(String text, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setOnClickListener(l);
        return b;
    }

    private LinearLayout.LayoutParams cell() {
        return new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
    }

    private int safePosition() {
        try {
            return player == null ? 0 : player.getCurrentPosition();
        } catch (IllegalStateException e) {
            return 0;
        }
    }

    private void tap() {
        if (player == null || next >= lines.size()) return;
        if (!player.isPlaying()) {
            player.start();
            return;
        }
        double t = Math.max(0, safePosition() / 1000.0 - REACTION_SECONDS);
        // Lines must stay in order, each at least a moment after the one before.
        if (next > 0) t = Math.max(t, times[next - 1] + 0.2);
        times[next++] = t;
        show();
    }

    private void undo() {
        if (next == 0) return;
        next--;
        if (player != null) player.seekTo((int) Math.max(0, times[next] * 1000 - 4000));
        show();
    }

    private void show() {
        int n = lines.size();
        counter.setText(Math.min(next, n) + " of " + n + " lines");
        previous.setText(next > 0 ? lines.get(next - 1) : "");
        if (next < n) {
            current.setText(lines.get(next));
            StringBuilder sb = new StringBuilder();
            for (int i = next + 1; i < Math.min(n, next + 4); i++) sb.append(lines.get(i)).append('\n');
            upcoming.setText(sb.toString().trim());
            tapButton.setEnabled(true);
        } else {
            current.setText("All lines timed.");
            upcoming.setText("Press \"Use these timings\" below.");
            tapButton.setEnabled(false);
        }
        doneButton.setEnabled(next > 0);
    }

    private void finishWithResult() {
        List<Lrc.Line> out = new ArrayList<>();
        for (int i = 0; i < next; i++) out.add(new Lrc.Line(times[i], lines.get(i)));
        Intent data = new Intent();
        data.putExtra(EXTRA_RESULT, Lrc.format(out));
        setResult(RESULT_OK, data);
        finish();
    }

    @Override
    protected void onPause() {
        super.onPause();
        try {
            if (player != null && player.isPlaying()) player.pause();
        } catch (IllegalStateException ignored) {
            // The player is already gone.
        }
    }

    @Override
    protected void onDestroy() {
        handler.removeCallbacks(tick);
        if (player != null) {
            player.release();
            player = null;
        }
        super.onDestroy();
    }
}
