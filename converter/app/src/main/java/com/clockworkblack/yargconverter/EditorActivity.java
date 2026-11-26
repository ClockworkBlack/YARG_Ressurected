package com.clockworkblack.yargconverter;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.media.MediaPlayer;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.Gravity;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.ScaleGestureDetector;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import com.clockworkblack.yargconverter.core.ChartEdit;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

/**
 * Change the notes of a song that is already in YARG's songs folder: add a note, take one away,
 * make one longer, without converting the song again. Only the guitar-style notes change; the
 * vocals, tempo and everything else in the song stay exactly as they were.
 */
public final class EditorActivity extends Activity {
    private static final int[] GRID = {1, 2, 4, 8};
    private static final String[] GRID_NAMES = {"1/4", "1/8", "1/16", "1/32"};
    private static final int[] FRET_COLORS = {0xFF2ECC40, 0xFFFF4136, 0xFFFFDC00, 0xFF0074D9, 0xFFFF851B};

    private final Handler main = new Handler(Looper.getMainLooper());

    private File songDir;
    private String songTitle = "";
    private ChartEdit edit;
    private int partIndex;
    private int difficulty = 3;
    private int gridIndex = 2;

    private MediaPlayer player;
    private HighwayView highway;
    private TextView infoLine;
    private Button partButton, gridButton, undoButton, saveButton, playButton;
    private final Button[] difficultyButtons = new Button[4];

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        showSongList();
    }

    private int dp(float v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    private Button button(String text, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setOnClickListener(l);
        return b;
    }

    private static LinearLayout.LayoutParams weighted() {
        return new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
    }

    // ---------------------------------------------------------------- picking a song

    private static final class SongEntry {
        File dir;
        String label;
        long modified;
    }

    private void showSongList() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFitsSystemWindows(true);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(20), dp(20), dp(28));
        scroll.addView(root);

        TextView heading = new TextView(this);
        heading.setText("Edit a song's notes");
        heading.setTextSize(24);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        root.addView(heading);
        TextView sub = new TextView(this);
        sub.setText("Pick a song from YARG's songs folder. You can add or take away guitar and bass notes and save, without converting the song again. Newest songs are at the top.");
        sub.setPadding(0, dp(4), 0, dp(12));
        root.addView(sub);

        File songs = new File(new File(Environment.getExternalStorageDirectory(), "YARG"), "songs");
        List<SongEntry> found = new ArrayList<>();
        findSongs(songs, 0, found);
        Collections.sort(found, (a, b) -> Long.compare(b.modified, a.modified));
        if (found.isEmpty()) {
            TextView none = new TextView(this);
            none.setText("No songs with a notes.mid file were found in Internal storage/YARG/songs. Convert a song first.");
            root.addView(none);
        }
        for (final SongEntry s : found) {
            Button b = button(s.label, v -> openSong(s.dir, s.label));
            b.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
            root.addView(b, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        }
        setContentView(scroll);
    }

    private static void findSongs(File dir, int depth, List<SongEntry> out) {
        File[] files = dir.listFiles();
        if (files == null) return;
        File mid = new File(dir, "notes.mid");
        if (mid.isFile()) {
            SongEntry e = new SongEntry();
            e.dir = dir;
            e.modified = mid.lastModified();
            e.label = songLabel(dir);
            out.add(e);
            return;
        }
        if (depth >= 4) return;
        for (File f : files) if (f.isDirectory() && !f.getName().startsWith(".")) findSongs(f, depth + 1, out);
    }

    /** "Artist - Title" from song.ini, or the folder name. */
    private static String songLabel(File dir) {
        String name = null, artist = null;
        try {
            String ini = new String(readAll(new File(dir, "song.ini")), StandardCharsets.UTF_8);
            for (String line : ini.split("\r?\n")) {
                int eq = line.indexOf('=');
                if (eq < 0) continue;
                String key = line.substring(0, eq).trim().toLowerCase(Locale.US);
                String value = line.substring(eq + 1).trim();
                if (key.equals("name")) name = value;
                else if (key.equals("artist")) artist = value;
            }
        } catch (IOException ignored) {
            // No song.ini: use the folder name
        }
        if (name == null || name.isEmpty()) return dir.getName();
        return artist == null || artist.isEmpty() ? name : artist + " - " + name;
    }

    private static byte[] readAll(File f) throws IOException {
        try (InputStream in = new FileInputStream(f)) {
            ByteArrayOutputStream out = new ByteArrayOutputStream((int) Math.max(1024, f.length()));
            byte[] buf = new byte[1 << 15];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
            return out.toByteArray();
        }
    }

    // ---------------------------------------------------------------- editing

    private void openSong(File dir, String label) {
        try {
            edit = ChartEdit.open(readAll(new File(dir, "notes.mid")));
        } catch (IOException | RuntimeException e) {
            new AlertDialog.Builder(this)
                    .setTitle("This song can't be edited")
                    .setMessage(e.getMessage() == null ? "Its notes.mid could not be read." : e.getMessage())
                    .setPositiveButton("OK", null)
                    .show();
            return;
        }
        songDir = dir;
        songTitle = label;
        partIndex = 0;
        difficulty = 3;
        releasePlayer();
        for (String audio : new String[] {"song.ogg", "song.mp3", "song.opus", "song.wav", "guitar.ogg", "guitar.mp3"}) {
            File f = new File(dir, audio);
            if (!f.isFile()) continue;
            try {
                MediaPlayer p = new MediaPlayer();
                p.setDataSource(f.getAbsolutePath());
                p.prepare();
                player = p;
                break;
            } catch (IOException | RuntimeException e) {
                player = null;
            }
        }
        if (player == null) Toast.makeText(this, "The song's music could not be played; you can still edit the notes.", Toast.LENGTH_LONG).show();
        showEditor();
    }

    private void showEditor() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setFitsSystemWindows(true);
        root.setBackgroundColor(0xFF111118);

        TextView title = new TextView(this);
        title.setText(songTitle);
        title.setTextColor(Color.WHITE);
        title.setTextSize(16);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setSingleLine(true);
        title.setPadding(dp(10), dp(6), dp(10), 0);
        root.addView(title);

        LinearLayout top = new LinearLayout(this);
        partButton = button("", v -> {
            partIndex = (partIndex + 1) % edit.parts.size();
            refresh();
        });
        top.addView(partButton, weighted());
        for (int d = 0; d < 4; d++) {
            final int level = d;
            difficultyButtons[d] = button(ChartEdit.DIFFICULTY_NAMES[d].substring(0, 1), v -> {
                difficulty = level;
                refresh();
            });
            top.addView(difficultyButtons[d], weighted());
        }
        Button more = button("More", v -> showMore());
        top.addView(more, weighted());
        root.addView(top);

        highway = new HighwayView(this);
        root.addView(highway, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f));

        infoLine = new TextView(this);
        infoLine.setTextColor(0xFFCCCCCC);
        infoLine.setTextSize(12);
        infoLine.setPadding(dp(10), dp(2), dp(10), dp(2));
        root.addView(infoLine);

        LinearLayout bottom = new LinearLayout(this);
        playButton = button("Play", v -> togglePlay());
        gridButton = button("", v -> {
            gridIndex = (gridIndex + 1) % GRID.length;
            refresh();
        });
        undoButton = button("Undo", v -> {
            Object[] where = edit.undo();
            if (where != null) {
                partIndex = edit.parts.indexOf(where[0]);
                difficulty = (Integer) where[1];
            }
            refresh();
        });
        saveButton = button("Save", v -> save(null));
        bottom.addView(playButton, weighted());
        bottom.addView(gridButton, weighted());
        bottom.addView(undoButton, weighted());
        bottom.addView(saveButton, weighted());
        root.addView(bottom);

        setContentView(root);
        refresh();
    }

    private ChartEdit.Part part() {
        return edit.parts.get(partIndex);
    }

    private void refresh() {
        if (edit == null || highway == null) return;
        ChartEdit.Part p = part();
        partButton.setText(p.name);
        for (int d = 0; d < 4; d++) {
            difficultyButtons[d].setTypeface(d == difficulty ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
            difficultyButtons[d].setAlpha(d == difficulty ? 1f : 0.55f);
        }
        gridButton.setText("Snap " + GRID_NAMES[gridIndex]);
        undoButton.setEnabled(edit.canUndo());
        saveButton.setText(edit.hasChanges() ? "Save *" : "Save");
        infoLine.setText(ChartEdit.DIFFICULTY_NAMES[difficulty] + " " + p.name.toLowerCase(Locale.US) + ": " + p.count(difficulty)
                + " notes. Tap a lane to add a note, tap a note to take it away. Hold a note and slide up to hold it longer. Drag to scroll, pinch to zoom.");
        highway.invalidate();
    }

    private void showMore() {
        final String[] items = {
                "Copy Expert notes onto " + ChartEdit.DIFFICULTY_NAMES[difficulty],
                "Copy Hard notes onto " + ChartEdit.DIFFICULTY_NAMES[difficulty],
                "Go back to the start of the song",
                "Choose another song",
        };
        new AlertDialog.Builder(this)
                .setItems(items, (dialog, which) -> {
                    if (which == 0 || which == 1) {
                        int from = which == 0 ? 3 : 2;
                        if (from == difficulty) {
                            Toast.makeText(this, "That is the level you are on.", Toast.LENGTH_SHORT).show();
                            return;
                        }
                        edit.copyDifficulty(part(), from, difficulty);
                        Toast.makeText(this, "Copied. Take away notes to make it easier; Undo puts it back.", Toast.LENGTH_LONG).show();
                        refresh();
                    } else if (which == 2) {
                        pause();
                        highway.viewTime = 0;
                        if (player != null) player.seekTo(0);
                        highway.invalidate();
                    } else {
                        leave(this::showSongListAfterEdit);
                    }
                })
                .show();
    }

    private void showSongListAfterEdit() {
        releasePlayer();
        edit = null;
        highway = null;
        showSongList();
    }

    /** Asks to save if anything changed, then does {@code then}. */
    private void leave(final Runnable then) {
        if (edit == null || !edit.hasChanges()) {
            then.run();
            return;
        }
        new AlertDialog.Builder(this)
                .setTitle("Save your changes?")
                .setMessage("The notes you changed in " + songTitle + " are not saved yet.")
                .setPositiveButton("Save", (DialogInterface d, int w) -> save(then))
                .setNegativeButton("Don't save", (DialogInterface d, int w) -> then.run())
                .setNeutralButton("Keep editing", null)
                .show();
    }

    private void save(Runnable then) {
        pause();
        File mid = new File(songDir, "notes.mid");
        File backup = new File(songDir, "notes.mid.bak");
        File temp = new File(songDir, "notes.mid.new");
        try {
            byte[] bytes = edit.save();
            if (!backup.exists()) {
                try (FileOutputStream out = new FileOutputStream(backup)) {
                    out.write(readAll(mid));
                }
            }
            try (FileOutputStream out = new FileOutputStream(temp)) {
                out.write(bytes);
                out.getFD().sync();
            }
            if (!temp.renameTo(mid)) {
                // Some storage won't rename over a file: write it in place instead
                try (FileOutputStream out = new FileOutputStream(mid)) {
                    out.write(bytes);
                }
                //noinspection ResultOfMethodCallIgnored
                temp.delete();
            }
            Toast.makeText(this, "Saved. In YARG, scan for songs again to play the new notes. The first version is kept as notes.mid.bak.", Toast.LENGTH_LONG).show();
        } catch (IOException | RuntimeException e) {
            new AlertDialog.Builder(this)
                    .setTitle("Could not save")
                    .setMessage(e.getMessage() == null ? "The song file could not be written." : e.getMessage())
                    .setPositiveButton("OK", null)
                    .show();
            return;
        }
        refresh();
        if (then != null) then.run();
    }

    @Override
    public void onBackPressed() {
        if (edit == null) {
            super.onBackPressed();
            return;
        }
        leave(this::showSongListAfterEdit);
    }

    // ---------------------------------------------------------------- music

    private double playStartPos;
    private long playStartClock;

    private void togglePlay() {
        if (player == null) {
            Toast.makeText(this, "This song's music could not be played.", Toast.LENGTH_SHORT).show();
            return;
        }
        if (player.isPlaying()) {
            pause();
        } else {
            int ms = (int) Math.max(0, Math.round(highway.viewTime * 1000));
            if (ms >= player.getDuration()) ms = 0;
            player.seekTo(ms);
            player.start();
            playStartPos = ms / 1000.0;
            playStartClock = SystemClock.uptimeMillis();
            playButton.setText("Pause");
            highway.postInvalidateOnAnimation();
        }
    }

    private void pause() {
        if (player != null && player.isPlaying()) {
            player.pause();
            if (highway != null) highway.viewTime = player.getCurrentPosition() / 1000.0;
        }
        if (playButton != null) playButton.setText("Play");
    }

    /** Where the music is, smoothly: the player's own position only moves in steps. */
    private double playTime() {
        double clock = playStartPos + (SystemClock.uptimeMillis() - playStartClock) / 1000.0;
        double reported = player.getCurrentPosition() / 1000.0;
        if (Math.abs(clock - reported) > 0.15) {
            playStartPos = reported;
            playStartClock = SystemClock.uptimeMillis();
            clock = reported;
        }
        return clock;
    }

    private void releasePlayer() {
        if (player != null) {
            try {
                player.release();
            } catch (RuntimeException ignored) {
                // Already gone
            }
            player = null;
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        pause();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        releasePlayer();
    }

    // ---------------------------------------------------------------- the highway

    private final class HighwayView extends View {
        /** The song time at the strike line, in seconds. */
        double viewTime;
        /** How many pixels one second of the song takes up. */
        float pixelsPerSecond;

        private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint line = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint text = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF rect = new RectF();
        private final ScaleGestureDetector scaler;
        private final int slop;

        private float downX, downY;
        private double downViewTime;
        private boolean moved, holding, scaling;
        private long holdTick = -1;

        private final Runnable longPress = new Runnable() {
            @Override
            public void run() {
                if (moved || scaling) return;
                int lane = laneAt(downX);
                ChartEdit.Note n = noteNear(downY, lane);
                if (n == null) return;
                holding = true;
                holdTick = n.tick;
                edit.beginDrag(part(), difficulty);
                performHapticFeedback(HapticFeedbackConstants.LONG_PRESS);
                refresh();
            }
        };

        HighwayView(Context c) {
            super(c);
            pixelsPerSecond = dp(220);
            slop = ViewConfiguration.get(c).getScaledTouchSlop();
            text.setColor(0xFFAAAAAA);
            text.setTextSize(dp(11));
            scaler = new ScaleGestureDetector(c, new ScaleGestureDetector.SimpleOnScaleGestureListener() {
                @Override
                public boolean onScale(ScaleGestureDetector d) {
                    pixelsPerSecond = Math.max(dp(60), Math.min(dp(900), pixelsPerSecond * d.getScaleFactor()));
                    invalidate();
                    return true;
                }
            });
        }

        private float strikeY() {
            return getHeight() * 0.85f;
        }

        private float laneWidth() {
            return Math.min(getWidth() * 0.9f, dp(440)) / ChartEdit.LANES;
        }

        private float left() {
            return (getWidth() - laneWidth() * ChartEdit.LANES) / 2f;
        }

        private int laneAt(float x) {
            int lane = (int) Math.floor((x - left()) / laneWidth());
            return lane >= 0 && lane < ChartEdit.LANES ? lane : -1;
        }

        private double timeAt(float y) {
            return viewTime + (strikeY() - y) / pixelsPerSecond;
        }

        private float yAt(double time) {
            return (float) (strikeY() - (time - viewTime) * pixelsPerSecond);
        }

        private long tickAt(double time) {
            return Math.max(0, Math.round(edit.timeToTickExact(Math.max(0, time))));
        }

        /** The note under a finger: within about a fingertip of where it is drawn. */
        private ChartEdit.Note noteNear(float y, int lane) {
            if (lane == -1) return null;
            if (lane < 0) lane = -1; // any fret
            double t = timeAt(y);
            double reachSeconds = dp(22) / pixelsPerSecond;
            long from = tickAt(t - reachSeconds), to = tickAt(t + reachSeconds);
            long reach = Math.max(1, (to - from) / 2);
            return edit.noteAt(part(), difficulty, (from + to) / 2, lane, reach);
        }

        @Override
        public boolean onTouchEvent(MotionEvent e) {
            if (edit == null) return false;
            scaler.onTouchEvent(e);
            if (e.getPointerCount() > 1 || scaler.isInProgress()) {
                if (!scaling) {
                    scaling = true;
                    removeCallbacks(longPress);
                }
                return true;
            }
            switch (e.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    downX = e.getX();
                    downY = e.getY();
                    downViewTime = viewTime;
                    moved = false;
                    holding = false;
                    scaling = false;
                    postDelayed(longPress, ViewConfiguration.getLongPressTimeout());
                    return true;
                case MotionEvent.ACTION_MOVE:
                    if (scaling) return true;
                    if (holding) {
                        long end = edit.snap(timeAt(e.getY()), GRID[gridIndex]);
                        edit.setLength(part(), difficulty, holdTick, end - holdTick);
                        invalidate();
                        return true;
                    }
                    if (!moved && Math.abs(e.getY() - downY) > slop) {
                        moved = true;
                        removeCallbacks(longPress);
                        pause();
                        downViewTime = viewTime;
                        downY = e.getY();
                    }
                    if (moved) {
                        viewTime = Math.max(-1, downViewTime + (e.getY() - downY) / pixelsPerSecond);
                        invalidate();
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    removeCallbacks(longPress);
                    if (holding) {
                        holding = false;
                        refresh();
                    } else if (!moved && !scaling) {
                        tap(e.getX(), e.getY());
                    }
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    removeCallbacks(longPress);
                    holding = false;
                    return true;
                default:
                    return true;
            }
        }

        private void tap(float x, float y) {
            int lane = laneAt(x);
            if (lane < 0) return;
            ChartEdit.Note n = noteNear(y, lane);
            // Beside a note on another fret: join it as a chord, at exactly its time
            if (n == null) n = noteNear(y, -2);
            long tick = n != null ? n.tick : edit.snap(timeAt(y), GRID[gridIndex]);
            edit.toggle(part(), difficulty, tick, lane);
            performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
            refresh();
        }

        @Override
        protected void onDraw(Canvas canvas) {
            if (edit == null) return;
            boolean playing = player != null && player.isPlaying();
            if (playing) viewTime = playTime();

            float w = laneWidth(), x0 = left(), strike = strikeY();
            float h = getHeight();
            fill.setColor(0xFF1E1E2A);
            canvas.drawRect(x0, 0, x0 + w * ChartEdit.LANES, h, fill);

            double bottomTime = timeAt(h), topTime = timeAt(0);
            long fromTick = tickAt(bottomTime), toTick = tickAt(topTime);
            int res = edit.resolution();

            // Beat, measure and snap lines
            long step = Math.max(1, res / GRID[gridIndex]);
            long first = (fromTick / step) * step;
            int drawn = 0;
            for (long t = first; t <= toTick && drawn < 2000; t += step, drawn++) {
                float y = yAt(edit.tickToTime(t));
                boolean beat = t % res == 0;
                boolean measure = beat && edit.isMeasureStart(t);
                line.setColor(measure ? 0xFF8888AA : beat ? 0xFF4A4A60 : 0xFF2C2C3A);
                line.setStrokeWidth(measure ? dp(2) : dp(1));
                canvas.drawLine(x0, y, x0 + w * ChartEdit.LANES, y, line);
                if (measure) {
                    double secs = edit.tickToTime(t);
                    canvas.drawText(String.format(Locale.US, "%d:%02d", (int) secs / 60, (int) secs % 60), dp(4), y - dp(2), text);
                }
            }
            line.setColor(0xFF33334A);
            line.setStrokeWidth(dp(1));
            for (int l = 0; l <= ChartEdit.LANES; l++) canvas.drawLine(x0 + l * w, 0, x0 + l * w, h, line);

            // Strike line with a ring per fret
            line.setColor(0xFFEEEEEE);
            line.setStrokeWidth(dp(3));
            canvas.drawLine(x0, strike, x0 + w * ChartEdit.LANES, strike, line);
            float r = Math.min(w * 0.36f, dp(26));
            line.setStrokeWidth(dp(2));
            for (int l = 0; l < ChartEdit.LANES; l++) {
                line.setColor(FRET_COLORS[l]);
                line.setStyle(Paint.Style.STROKE);
                canvas.drawCircle(x0 + (l + 0.5f) * w, strike, r, line);
            }
            line.setStyle(Paint.Style.FILL);

            // Notes: held part first, then the heads on top
            ChartEdit.Part p = part();
            long early = tickAt(bottomTime - 8);
            List<ChartEdit.Note> notes = edit.notes(p, difficulty, early, toTick);
            for (ChartEdit.Note n : notes) {
                if (!edit.isHeld(n)) continue;
                float yStart = yAt(edit.tickToTime(n.tick));
                float yEnd = yAt(edit.tickToTime(n.tick + n.length));
                for (int l = 0; l < ChartEdit.LANES; l++) {
                    if (!n.has(l)) continue;
                    fill.setColor((FRET_COLORS[l] & 0x00FFFFFF) | 0xC0000000);
                    float cx = x0 + (l + 0.5f) * w;
                    rect.set(cx - r * 0.28f, yEnd, cx + r * 0.28f, yStart);
                    canvas.drawRoundRect(rect, r * 0.2f, r * 0.2f, fill);
                }
            }
            for (ChartEdit.Note n : notes) {
                float y = yAt(edit.tickToTime(n.tick));
                if (y < -r || y > h + r) continue;
                boolean past = y > strike + r;
                boolean grabbed = holding && n.tick == holdTick;
                for (int l = 0; l < ChartEdit.LANES; l++) {
                    if (!n.has(l)) continue;
                    float cx = x0 + (l + 0.5f) * w;
                    fill.setColor(past ? (FRET_COLORS[l] & 0x00FFFFFF) | 0x66000000 : FRET_COLORS[l]);
                    canvas.drawCircle(cx, y, r * 0.78f, fill);
                    fill.setColor(grabbed ? Color.WHITE : 0xFF101010);
                    canvas.drawCircle(cx, y, r * (grabbed ? 0.45f : 0.32f), fill);
                }
            }

            if (playing) postInvalidateOnAnimation();
        }
    }
}
