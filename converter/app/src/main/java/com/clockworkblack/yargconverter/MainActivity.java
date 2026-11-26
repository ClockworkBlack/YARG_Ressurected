package com.clockworkblack.yargconverter;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.graphics.Typeface;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.provider.OpenableColumns;
import android.provider.Settings;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.TextView;

import com.clockworkblack.yargconverter.core.AudioAnalyzer;
import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.Lrc;
import com.clockworkblack.yargconverter.core.SongMeta;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.StemSplitter;
import com.clockworkblack.yargconverter.stems.OrtStemModel;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/** Pick a song, sort out its lyrics, and write a ready-to-play folder into YARG's songs folder. */
public final class MainActivity extends Activity {
    private static final int REQ_AUDIO = 1;
    private static final int REQ_LRC = 2;
    private static final int REQ_TAP = 3;
    private static final int REQ_PERMISSION = 4;

    /** Folder inside Internal storage/YARG/songs that converted songs go into. */
    private static final String SUBFOLDER = "Converted";

    private final Handler main = new Handler(Looper.getMainLooper());

    private Uri audioUri;
    private String audioName = "";
    private double audioSeconds;
    private boolean busy;

    private TextView fileLabel;
    private EditText titleBox, artistBox, albumBox, yearBox, genreBox, lyricsBox;
    private TextView lyricsStatus, log;
    private Button pickButton, findButton, lrcButton, tapButton, convertButton;
    private ProgressBar progress;
    private RadioGroup densityGroup;
    private CheckBox stemsCheck;
    private TextView modelStatus;
    private Button modelButton;
    private static final int DENSITY_FEWER = 1, DENSITY_STANDARD = 2, DENSITY_MORE = 3;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        buildUi();
        handleIncoming(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        handleIncoming(intent);
    }

    private void handleIncoming(Intent intent) {
        if (intent == null || busy) return;
        Uri uri = null;
        if (Intent.ACTION_VIEW.equals(intent.getAction())) {
            uri = intent.getData();
        } else if (Intent.ACTION_SEND.equals(intent.getAction())) {
            Object extra = intent.getParcelableExtra(Intent.EXTRA_STREAM);
            if (extra instanceof Uri) uri = (Uri) extra;
        }
        if (uri != null) setAudio(uri);
    }

    // ---------------------------------------------------------------- layout

    private int dp(float v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    private void buildUi() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setFitsSystemWindows(true);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(20), dp(20), dp(28));
        scroll.addView(root, new ScrollView.LayoutParams(ScrollView.LayoutParams.MATCH_PARENT, ScrollView.LayoutParams.WRAP_CONTENT));

        TextView heading = new TextView(this);
        heading.setText("YARG Converter");
        heading.setTextSize(26);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        root.addView(heading);

        TextView sub = new TextView(this);
        sub.setText("Turns an MP3, WAV or FLAC into a guitar chart (Easy to Expert) with lyrics, and puts it in YARG's songs folder.");
        sub.setPadding(0, dp(4), 0, dp(16));
        root.addView(sub);

        root.addView(section("1. Song"));
        pickButton = button("Choose audio file", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                pickAudio();
            }
        });
        root.addView(pickButton);
        fileLabel = new TextView(this);
        fileLabel.setText("No file chosen yet.");
        fileLabel.setPadding(0, dp(4), 0, dp(8));
        root.addView(fileLabel);

        titleBox = field(root, "Title");
        artistBox = field(root, "Artist");
        albumBox = field(root, "Album (optional)");
        LinearLayout row = row();
        yearBox = new EditText(this);
        yearBox.setHint("Year");
        yearBox.setSingleLine(true);
        yearBox.setInputType(InputType.TYPE_CLASS_NUMBER);
        row.addView(yearBox, weighted());
        genreBox = new EditText(this);
        genreBox.setHint("Genre");
        genreBox.setSingleLine(true);
        row.addView(genreBox, weighted());
        root.addView(row);

        root.addView(section("2. Lyrics (optional)"));
        LinearLayout lyricButtons = row();
        findButton = button("Find online", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                findLyrics();
            }
        });
        lrcButton = button("Open .lrc", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                pickLrc();
            }
        });
        tapButton = button("Tap to sync", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                startTapSync();
            }
        });
        lyricButtons.addView(findButton, weighted());
        lyricButtons.addView(lrcButton, weighted());
        lyricButtons.addView(tapButton, weighted());
        root.addView(lyricButtons);

        lyricsBox = new EditText(this);
        lyricsBox.setHint("Or paste lyrics here.\n\nLines with times like [01:23.45] are used as they are. Plain lines need \"Tap to sync\" so the app knows when each line is sung.");
        lyricsBox.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        lyricsBox.setGravity(Gravity.TOP | Gravity.START);
        lyricsBox.setMinLines(5);
        lyricsBox.setMaxLines(12);
        lyricsBox.setTextSize(14);
        root.addView(lyricsBox, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        lyricsStatus = new TextView(this);
        lyricsStatus.setPadding(0, dp(4), 0, dp(4));
        root.addView(lyricsStatus);

        root.addView(section("3. Convert"));
        stemsCheck = new CheckBox(this);
        stemsCheck.setText("Split the song into instruments first. The guitar chart is then made from the guitar alone, and a bass chart is added. Takes a few minutes per song.");
        root.addView(stemsCheck);
        modelStatus = new TextView(this);
        modelStatus.setPadding(dp(32), 0, 0, dp(4));
        root.addView(modelStatus);
        modelButton = button("Download the separation model (136 MB)", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                downloadModel();
            }
        });
        root.addView(modelButton);
        refreshModelUi(true);

        TextView densityLabel = new TextView(this);
        densityLabel.setPadding(0, dp(10), 0, 0);
        densityLabel.setText("How many notes");
        root.addView(densityLabel);
        densityGroup = new RadioGroup(this);
        densityGroup.setOrientation(RadioGroup.HORIZONTAL);
        String[] names = {"Fewer", "Standard", "More"};
        int[] ids = {DENSITY_FEWER, DENSITY_STANDARD, DENSITY_MORE};
        for (int i = 0; i < names.length; i++) {
            RadioButton rb = new RadioButton(this);
            rb.setId(ids[i]);
            rb.setText(names[i]);
            densityGroup.addView(rb, new RadioGroup.LayoutParams(0, RadioGroup.LayoutParams.WRAP_CONTENT, 1f));
        }
        densityGroup.check(DENSITY_STANDARD);
        root.addView(densityGroup, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        convertButton = button("Convert and add to YARG", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                onConvertPressed();
            }
        });
        root.addView(convertButton);
        progress = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        progress.setMax(1000);
        progress.setVisibility(View.GONE);
        root.addView(progress, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        log = new TextView(this);
        log.setPadding(0, dp(10), 0, 0);
        log.setTextIsSelectable(true);
        root.addView(log);

        setContentView(scroll);
    }

    private TextView section(String text) {
        TextView t = new TextView(this);
        t.setText(text);
        t.setTextSize(18);
        t.setTypeface(Typeface.DEFAULT_BOLD);
        t.setPadding(0, dp(18), 0, dp(6));
        return t;
    }

    private Button button(String text, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setOnClickListener(l);
        return b;
    }

    private LinearLayout row() {
        LinearLayout r = new LinearLayout(this);
        r.setOrientation(LinearLayout.HORIZONTAL);
        return r;
    }

    private LinearLayout.LayoutParams weighted() {
        return new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
    }

    private EditText field(LinearLayout parent, String hint) {
        EditText e = new EditText(this);
        e.setHint(hint);
        e.setSingleLine(true);
        e.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_WORDS);
        parent.addView(e, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        return e;
    }

    private void setBusy(boolean value) {
        busy = value;
        pickButton.setEnabled(!value);
        findButton.setEnabled(!value);
        lrcButton.setEnabled(!value);
        tapButton.setEnabled(!value);
        convertButton.setEnabled(!value);
        modelButton.setEnabled(!value);
        stemsCheck.setEnabled(!value && ModelStore.installed(this));
        if (value) {
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        } else {
            getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        }
    }

    /** Shows whether the separation model is on the phone, and offers the download if not. */
    private void refreshModelUi(boolean tickIfInstalled) {
        boolean installed = ModelStore.installed(this);
        stemsCheck.setEnabled(installed && !busy);
        if (!installed) stemsCheck.setChecked(false);
        else if (tickIfInstalled) stemsCheck.setChecked(true);
        modelButton.setVisibility(installed ? View.GONE : View.VISIBLE);
        modelStatus.setText(installed
                ? "Separation model installed (HT-Demucs, 6 instruments)."
                : "Needs a one-time 136 MB download from Hugging Face (HT-Demucs). Wi-Fi recommended.");
    }

    private void downloadModel() {
        if (busy) return;
        setBusy(true);
        progress.setIndeterminate(false);
        progress.setProgress(0);
        progress.setVisibility(View.VISIBLE);
        modelStatus.setText("Starting download…");
        new Thread(new Runnable() {
            @Override
            public void run() {
                String error = null;
                try {
                    final long[] last = {0};
                    ModelStore.download(MainActivity.this, new ModelStore.Progress() {
                        @Override
                        public boolean onProgress(final long done, final long total) {
                            long now = System.currentTimeMillis();
                            if (now - last[0] < 400 && done < total) return true;
                            last[0] = now;
                            main.post(new Runnable() {
                                @Override
                                public void run() {
                                    progress.setProgress((int) (done * 1000 / total));
                                    modelStatus.setText(String.format(Locale.US, "Downloading: %d of %d MB", done >> 20, total >> 20));
                                }
                            });
                            return true;
                        }
                    });
                } catch (IOException e) {
                    error = e.getMessage() == null ? "No connection." : e.getMessage();
                } catch (RuntimeException e) {
                    error = String.valueOf(e);
                }
                final String err = error;
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        setBusy(false);
                        progress.setVisibility(View.GONE);
                        refreshModelUi(true);
                        if (err != null) {
                            modelStatus.setText("Download did not finish: " + err);
                        }
                    }
                });
            }
        }, "model-download").start();
    }

    private void message(String title, String text) {
        new AlertDialog.Builder(this).setTitle(title).setMessage(text).setPositiveButton("OK", null).show();
    }

    // ---------------------------------------------------------------- choosing the song

    private void pickAudio() {
        Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        i.addCategory(Intent.CATEGORY_OPENABLE);
        i.setType("audio/*");
        i.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {"audio/*", "application/ogg", "application/x-flac"});
        try {
            startActivityForResult(i, REQ_AUDIO);
        } catch (RuntimeException e) {
            message("No file picker", "This phone has no file picker app available.");
        }
    }

    private void pickLrc() {
        Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        i.addCategory(Intent.CATEGORY_OPENABLE);
        i.setType("*/*");
        try {
            startActivityForResult(i, REQ_LRC);
        } catch (RuntimeException e) {
            message("No file picker", "This phone has no file picker app available.");
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (resultCode != RESULT_OK || data == null) return;
        if (requestCode == REQ_AUDIO && data.getData() != null) {
            setAudio(data.getData());
        } else if (requestCode == REQ_LRC && data.getData() != null) {
            try {
                String text = new String(readAll(data.getData(), 2 * 1024 * 1024), StandardCharsets.UTF_8);
                if (text.startsWith("﻿")) text = text.substring(1);
                lyricsBox.setText(text);
                describeLyrics("Loaded from file.");
            } catch (IOException e) {
                message("Could not read file", String.valueOf(e.getMessage()));
            }
        } else if (requestCode == REQ_TAP) {
            String lrc = data.getStringExtra(TapSyncActivity.EXTRA_RESULT);
            if (lrc != null && !lrc.isEmpty()) {
                lyricsBox.setText(lrc);
                describeLyrics("Synced by tapping.");
            }
        }
    }

    private byte[] readAll(Uri uri, int limit) throws IOException {
        try (InputStream in = getContentResolver().openInputStream(uri)) {
            if (in == null) throw new IOException("The file could not be opened.");
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) > 0) {
                out.write(buf, 0, n);
                if (out.size() > limit) throw new IOException("The file is too large to be a lyrics file.");
            }
            return out.toByteArray();
        }
    }

    private void setAudio(final Uri uri) {
        audioUri = uri;
        audioName = displayName(uri);
        audioSeconds = 0;
        fileLabel.setText(audioName + "\nReading song details…");
        log.setText("");
        new Thread(new Runnable() {
            @Override
            public void run() {
                final String[] tags = new String[5];
                double seconds = 0;
                MediaMetadataRetriever r = new MediaMetadataRetriever();
                try {
                    r.setDataSource(MainActivity.this, uri);
                    tags[0] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_TITLE);
                    tags[1] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ARTIST);
                    if (tags[1] == null) tags[1] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ALBUMARTIST);
                    tags[2] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ALBUM);
                    tags[3] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_YEAR);
                    if (tags[3] == null) tags[3] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DATE);
                    tags[4] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_GENRE);
                    String d = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION);
                    if (d != null) seconds = Long.parseLong(d) / 1000.0;
                } catch (RuntimeException e) {
                    // Tags are optional; the name of the file is used instead.
                } finally {
                    try {
                        r.release();
                    } catch (Exception ignored) {
                        // Nothing useful to do if releasing fails.
                    }
                }
                final double secondsFinal = seconds;
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        if (!uri.equals(audioUri)) return;
                        audioSeconds = secondsFinal;
                        String base = audioName.replaceFirst("\\.[A-Za-z0-9]{2,5}$", "");
                        String title = clean(tags[0]);
                        String artist = clean(tags[1]);
                        if (title.isEmpty()) {
                            // "Artist - Title.mp3" is the most common way untagged files are named.
                            int dash = base.indexOf(" - ");
                            if (dash > 0 && artist.isEmpty()) {
                                artist = base.substring(0, dash).trim();
                                title = base.substring(dash + 3).trim();
                            } else {
                                title = base.trim();
                            }
                        }
                        titleBox.setText(title);
                        artistBox.setText(artist);
                        albumBox.setText(clean(tags[2]));
                        String year = clean(tags[3]).replaceAll("[^0-9]", "");
                        yearBox.setText(year.length() >= 4 ? year.substring(0, 4) : "");
                        genreBox.setText(clean(tags[4]));
                        String len = secondsFinal > 0 ? String.format(Locale.US, " (%d:%02d)", (int) secondsFinal / 60, (int) secondsFinal % 60) : "";
                        fileLabel.setText(audioName + len);
                    }
                });
            }
        }).start();
    }

    private static String clean(String s) {
        return s == null ? "" : s.trim();
    }

    private String displayName(Uri uri) {
        String name = null;
        try (Cursor c = getContentResolver().query(uri, new String[] {OpenableColumns.DISPLAY_NAME}, null, null, null)) {
            if (c != null && c.moveToFirst()) name = c.getString(0);
        } catch (RuntimeException e) {
            // Some apps share files without a name; fall back to the link itself.
        }
        if (name == null || name.isEmpty()) name = uri.getLastPathSegment();
        return name == null ? "song" : name;
    }

    // ---------------------------------------------------------------- lyrics

    private void describeLyrics(String prefix) {
        String text = lyricsBox.getText().toString();
        if (text.trim().isEmpty()) {
            lyricsStatus.setText(prefix);
        } else if (Lrc.hasTimestamps(text)) {
            lyricsStatus.setText(prefix + " " + Lrc.parse(text).size() + " timed lines, ready to use.");
        } else {
            lyricsStatus.setText(prefix + " " + Lrc.plainLines(text).size() + " lines without times. Use \"Tap to sync\" to time them.");
        }
    }

    private void findLyrics() {
        final String title = titleBox.getText().toString().trim();
        final String artist = artistBox.getText().toString().trim();
        final String album = albumBox.getText().toString().trim();
        if (title.isEmpty()) {
            message("Title needed", "Fill in the song title (and ideally the artist) first.");
            return;
        }
        final double seconds = audioSeconds;
        lyricsStatus.setText("Searching lrclib.net…");
        findButton.setEnabled(false);
        new Thread(new Runnable() {
            @Override
            public void run() {
                LrcLib.Found found = null;
                String error = null;
                try {
                    found = LrcLib.find(title, artist, album, seconds);
                } catch (IOException e) {
                    error = e.getMessage() == null ? "No connection." : e.getMessage();
                }
                final LrcLib.Found f = found;
                final String err = error;
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        findButton.setEnabled(!busy);
                        if (err != null) {
                            lyricsStatus.setText("Lookup failed: " + err);
                        } else if (f == null) {
                            lyricsStatus.setText("No lyrics found for that title and artist. Check the spelling, or paste them below.");
                        } else if (f.synced != null) {
                            lyricsBox.setText(f.synced);
                            String note = "Found timed lyrics for \"" + f.title + "\" by " + f.artist + ".";
                            if (seconds > 1 && f.duration > 0 && Math.abs(f.duration - seconds) > 3) {
                                note += String.format(Locale.US, " Their recording is %d s %s than your file, so the timing may drift.",
                                        Math.round(Math.abs(f.duration - seconds)), f.duration > seconds ? "longer" : "shorter");
                            }
                            describeLyrics(note);
                        } else if (f.plain != null) {
                            lyricsBox.setText(f.plain);
                            describeLyrics("Found lyrics without times.");
                        } else {
                            lyricsStatus.setText("This song is listed as instrumental (no lyrics).");
                        }
                    }
                });
            }
        }).start();
    }

    private void startTapSync() {
        if (audioUri == null) {
            message("Choose a song first", "Tap to sync plays the song, so pick the audio file first.");
            return;
        }
        String text = lyricsBox.getText().toString();
        ArrayList<String> lines = new ArrayList<>();
        if (Lrc.hasTimestamps(text)) {
            for (Lrc.Line l : Lrc.parse(text)) if (!l.text.isEmpty()) lines.add(l.text);
        } else {
            lines.addAll(Lrc.plainLines(text));
        }
        if (lines.isEmpty()) {
            message("Lyrics needed", "Paste or find the lyrics first, one sung line per line.");
            return;
        }
        Intent i = new Intent(this, TapSyncActivity.class);
        i.setData(audioUri);
        i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        i.putStringArrayListExtra(TapSyncActivity.EXTRA_LINES, lines);
        startActivityForResult(i, REQ_TAP);
    }

    // ---------------------------------------------------------------- converting

    private boolean hasStorageAccess() {
        if (Build.VERSION.SDK_INT >= 30) return Environment.isExternalStorageManager();
        return checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED;
    }

    private void requestStorageAccess() {
        if (Build.VERSION.SDK_INT >= 30) {
            new AlertDialog.Builder(this)
                    .setTitle("Allow access to the YARG folder")
                    .setMessage("To place songs in Internal storage/YARG/songs, turn on \"Allow access to manage all files\" for YARG Converter on the next screen, then come back and press Convert again.")
                    .setPositiveButton("Open settings", new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialog, int which) {
                            try {
                                startActivity(new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                                        Uri.parse("package:" + getPackageName())));
                            } catch (RuntimeException e) {
                                try {
                                    startActivity(new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION));
                                } catch (RuntimeException e2) {
                                    message("Could not open settings", "Open Settings > Apps > YARG Converter and allow access to all files.");
                                }
                            }
                        }
                    })
                    .setNegativeButton("Cancel", null)
                    .show();
        } else {
            requestPermissions(new String[] {Manifest.permission.WRITE_EXTERNAL_STORAGE}, REQ_PERMISSION);
        }
    }

    private void onConvertPressed() {
        if (busy) return;
        if (audioUri == null) {
            message("Choose a song first", "Pick an MP3, WAV or FLAC file to convert.");
            return;
        }
        if (!hasStorageAccess()) {
            requestStorageAccess();
            return;
        }
        final String text = lyricsBox.getText().toString();
        if (!text.trim().isEmpty() && !Lrc.hasTimestamps(text)) {
            new AlertDialog.Builder(this)
                    .setTitle("Lyrics have no times yet")
                    .setMessage("The app needs to know when each line is sung. Tap along with the song once to time them, or convert without lyrics.")
                    .setPositiveButton("Tap to sync", new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialog, int which) {
                            startTapSync();
                        }
                    })
                    .setNegativeButton("Convert without lyrics", new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialog, int which) {
                            convert(new ArrayList<Lrc.Line>(), "");
                        }
                    })
                    .setNeutralButton("Cancel", null)
                    .show();
            return;
        }
        convert(Lrc.parse(text), text);
    }

    private void convert(final List<Lrc.Line> lyrics, final String lrcText) {
        final SongMeta meta = new SongMeta();
        meta.title = titleBox.getText().toString();
        meta.artist = artistBox.getText().toString();
        meta.album = albumBox.getText().toString();
        meta.year = yearBox.getText().toString();
        meta.genre = genreBox.getText().toString();
        final Uri uri = audioUri;
        final String name = audioName;
        final GuitarCharter.Options options = new GuitarCharter.Options();
        int choice = densityGroup.getCheckedRadioButtonId();
        // "Fewer" keeps only the stronger sounds; "More" lets weaker ones through.
        options.threshold = choice == DENSITY_FEWER ? 1.5 : choice == DENSITY_MORE ? 0.65 : 1.0;
        final boolean useStems = stemsCheck.isChecked() && ModelStore.installed(this);

        setBusy(true);
        progress.setIndeterminate(false);
        progress.setProgress(0);
        progress.setVisibility(View.VISIBLE);
        log.setText("Listening to the song…");

        new Thread(new Runnable() {
            @Override
            public void run() {
                String summary;
                boolean ok = false;
                try {
                    summary = runConversion(uri, name, meta, lyrics, lrcText, options, useStems);
                    ok = true;
                } catch (IOException e) {
                    summary = "Conversion failed: " + e.getMessage();
                } catch (RuntimeException | OutOfMemoryError e) {
                    summary = "Conversion failed unexpectedly: " + e;
                }
                final String text = summary;
                final boolean success = ok;
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        setBusy(false);
                        progress.setVisibility(View.GONE);
                        log.setText(text);
                        if (!success) message("Conversion failed", text);
                    }
                });
            }
        }, "convert").start();
    }

    private void status(final String text) {
        main.post(new Runnable() {
            @Override
            public void run() {
                log.setText(text);
            }
        });
    }

    private String runConversion(Uri uri, String name, SongMeta meta, List<Lrc.Line> lyrics, String lrcText, GuitarCharter.Options options, boolean useStems) throws IOException {
        File songs = new File(new File(Environment.getExternalStorageDirectory(), "YARG"), "songs");
        File dir = new File(new File(songs, SUBFOLDER), meta.folderName());
        if (!dir.isDirectory() && !dir.mkdirs()) {
            throw new IOException("Could not create " + dir + ". Check that file access is allowed.");
        }

        // YARG plays MP3, WAV, OGG and OPUS directly; anything else (FLAC, M4A...) is saved as WAV.
        String ext = "";
        int dot = name.lastIndexOf('.');
        if (dot >= 0) ext = name.substring(dot + 1).toLowerCase(Locale.US);
        boolean copyAsIs = ext.equals("mp3") || ext.equals("wav") || ext.equals("ogg") || ext.equals("opus");
        String audioFile = copyAsIs ? "song." + ext : "song.wav";
        for (String old : new String[] {"song.mp3", "song.wav", "song.ogg", "song.opus"}) {
            File f = new File(dir, old);
            if (f.exists() && !f.delete()) throw new IOException("Could not replace " + f);
        }

        // When splitting into instruments, the decoded audio is also parked in a temporary
        // file as 44.1 kHz stereo, which is what the separation model reads.
        final File pcmFile = new File(getCacheDir(), "mix44k.pcm");
        final long[] pcmFrames = {0};
        OutputStream pcmOut = null;
        AudioDecoder.StereoSink stereo = null;
        if (useStems) {
            final OutputStream out = new BufferedOutputStream(new FileOutputStream(pcmFile), 1 << 16);
            pcmOut = out;
            final byte[] bytes = new byte[8192 * 4];
            stereo = new AudioDecoder.StereoSink() {
                @Override
                public void write(float[] left, float[] right, int count) throws IOException {
                    int at = 0;
                    for (int i = 0; i < count; i++) {
                        int l = Math.max(-32768, Math.min(32767, Math.round(left[i] * 32767f)));
                        int r = Math.max(-32768, Math.min(32767, Math.round(right[i] * 32767f)));
                        bytes[at++] = (byte) l;
                        bytes[at++] = (byte) (l >> 8);
                        bytes[at++] = (byte) r;
                        bytes[at++] = (byte) (r >> 8);
                        if (at == bytes.length) {
                            out.write(bytes, 0, at);
                            at = 0;
                        }
                    }
                    if (at > 0) out.write(bytes, 0, at);
                    pcmFrames[0] += count;
                }
            };
        }

        Features features;
        SongWriter.Stems stems = null;
        String stemNote = null;
        long separationMs = 0;
        try {
            try {
                features = AudioDecoder.decode(this, uri, copyAsIs ? null : new File(dir, audioFile), stereo, new AudioDecoder.Progress() {
                    @Override
                    public void onProgress(final float fraction) {
                        main.post(new Runnable() {
                            @Override
                            public void run() {
                                if (fraction < 0) {
                                    progress.setIndeterminate(true);
                                } else {
                                    progress.setIndeterminate(false);
                                    progress.setProgress(Math.round(fraction * 1000));
                                }
                            }
                        });
                    }
                });
            } finally {
                if (pcmOut != null) pcmOut.close();
            }
            if (features.duration < 5) throw new IOException("The audio is too short to chart.");

            if (useStems) {
                long started = System.currentTimeMillis();
                try {
                    stems = separate(pcmFile, pcmFrames[0]);
                    separationMs = System.currentTimeMillis() - started;
                } catch (Exception | LinkageError | OutOfMemoryError e) {
                    // The charts can still be made the old way, from the full mix.
                    String why = e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage();
                    stemNote = "Splitting into instruments did not work on this song (" + why + "), so the charts were made from the full mix.";
                }
            }
        } finally {
            //noinspection ResultOfMethodCallIgnored
            pcmFile.delete();
        }

        status("Finding the beat and writing the charts…");
        SongWriter.Result result = SongWriter.analyze(features, lyrics, options, stems);
        if (result.noteCount(GuitarCharter.EXPERT) == 0) {
            throw new IOException("No notes could be found in this audio. Is it silent, or speech only?");
        }
        SongWriter.writeFolder(dir, meta, result, audioFile);

        if (copyAsIs) {
            status("Copying the audio…");
            try (InputStream in = getContentResolver().openInputStream(uri);
                 OutputStream out = new FileOutputStream(new File(dir, audioFile))) {
                if (in == null) throw new IOException("The audio file could not be opened.");
                byte[] buf = new byte[1 << 16];
                int n;
                while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
            }
        }

        File lrcFile = new File(dir, "lyrics.lrc");
        if (result.lyricLines > 0) {
            try (OutputStream out = new FileOutputStream(lrcFile)) {
                out.write(lrcText.getBytes(StandardCharsets.UTF_8));
            }
        } else if (lrcFile.exists()) {
            //noinspection ResultOfMethodCallIgnored
            lrcFile.delete();
        }
        saveAlbumArt(uri, dir);

        StringBuilder sb = new StringBuilder();
        sb.append("Done. \"").append(meta.titleOrDefault()).append("\" is in\nInternal storage/YARG/songs/")
                .append(SUBFOLDER).append('/').append(dir.getName()).append("\n\n");
        sb.append(String.format(Locale.US, "Tempo: about %.0f BPM, %d beats to the measure\n", result.bpm, result.beatsPerMeasure));
        if (stemNote != null) sb.append(stemNote).append("\n");
        if (stems != null) {
            sb.append(String.format(Locale.US, "Split into instruments in %d min %02d s\n", separationMs / 60000, (separationMs / 1000) % 60));
        }
        sb.append("Guitar chart made from ").append(result.guitarSource).append("\n");
        if (result.instrumentalMeasures > 0) {
            sb.append(result.instrumentalMeasures).append(result.instrumentalMeasures == 1 ? " measure" : " measures")
                    .append(" without vocals (intro, breaks) charted from the instruments\n");
        }
        if (result.drumNotesInBass > 0) {
            sb.append("Bass also covers the drums: ").append(result.drumNotesInBass).append(" drum hits added on Expert\n");
        }
        sb.append(String.format(Locale.US, "Guitar notes: Easy %d, Medium %d, Hard %d, Expert %d\n",
                result.noteCount(GuitarCharter.EASY), result.noteCount(GuitarCharter.MEDIUM),
                result.noteCount(GuitarCharter.HARD), result.noteCount(GuitarCharter.EXPERT)));
        if (result.bassChart != null) {
            sb.append(String.format(Locale.US, "Bass notes: Easy %d, Medium %d, Hard %d, Expert %d\n",
                    result.bassChart.notes.get(GuitarCharter.EASY).size(), result.bassChart.notes.get(GuitarCharter.MEDIUM).size(),
                    result.bassChart.notes.get(GuitarCharter.HARD).size(), result.bassChart.notes.get(GuitarCharter.EXPERT).size()));
        } else if (stems != null) {
            sb.append("No bass chart: the song has little or no separate bass.\n");
        }
        sb.append("Recognised ").append(result.chart.soundGroups).append(" different notes and chords, and ")
                .append(result.chart.repeatedMeasures).append(" measures that repeat\n");
        sb.append(result.lyricLines > 0 ? "Lyrics: " + result.lyricLines + " lines\n" : "Lyrics: none\n");
        if (!copyAsIs) sb.append("Audio saved as WAV so YARG can play it.\n");
        sb.append("\nTo play it in YARG:\n")
                .append("1. Open YARG (allow All files access if it asks).\n")
                .append("2. Settings > Songs > Scan Songs (or in the music library: More Options > Scan Songs).\n")
                .append("3. In the music library, Search for \"").append(meta.titleOrDefault())
                .append("\", or sort by Charter: this one is under \"").append(meta.charter).append("\".");
        return sb.toString();
    }

    /**
     * Runs the separation model over the parked audio and measures the guitar, the pitched
     * instruments together, the vocals, the drums and the bass as they come out. Only the measurements are kept; the separated audio itself is not stored.
     */
    private SongWriter.Stems separate(File pcmFile, long frames) throws Exception {
        final AudioAnalyzer guitar = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer melodic = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer vocals = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer drums = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer bass = new AudioAnalyzer(StemSplitter.SAMPLE_RATE, true);
        final double[] energy = new double[6];
        final float[] g = new float[StemSplitter.SEGMENT];
        final float[] m = new float[StemSplitter.SEGMENT];
        final float[] v = new float[StemSplitter.SEGMENT];
        final float[] dr = new float[StemSplitter.SEGMENT];
        final float[] b = new float[StemSplitter.SEGMENT];
        int cores = Runtime.getRuntime().availableProcessors();
        int threads = Math.max(2, Math.min(6, cores - 2));
        final long started = System.currentTimeMillis();
        status("Loading the separation model…");
        main.post(new Runnable() {
            @Override
            public void run() {
                progress.setIndeterminate(false);
                progress.setProgress(0);
            }
        });

        try (OrtStemModel model = new OrtStemModel(ModelStore.file(this).getAbsolutePath(), threads);
             InputStream in = new BufferedInputStream(new FileInputStream(pcmFile), 1 << 16)) {
            final byte[] bytes = new byte[1 << 16];
            StemSplitter.Source source = new StemSplitter.Source() {
                @Override
                public int read(float[] left, float[] right, int offset, int length) throws IOException {
                    int want = Math.min(length, bytes.length / 4) * 4;
                    int got = 0;
                    while (got < want) {
                        int n = in.read(bytes, got, want - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    int count = got / 4;
                    for (int i = 0; i < count; i++) {
                        int at = i * 4;
                        left[offset + i] = (short) ((bytes[at] & 0xFF) | (bytes[at + 1] << 8)) / 32768f;
                        right[offset + i] = (short) ((bytes[at + 2] & 0xFF) | (bytes[at + 3] << 8)) / 32768f;
                    }
                    return count;
                }
            };
            StemSplitter.Sink sink = new StemSplitter.Sink() {
                @Override
                public void accept(float[] mixLeft, float[] mixRight, float[][][] stems, int count) {
                    float[][] gt = stems[StemSplitter.GUITAR], ot = stems[StemSplitter.OTHER];
                    float[][] pn = stems[StemSplitter.PIANO], bs = stems[StemSplitter.BASS];
                    float[][] vo = stems[StemSplitter.VOCALS], ds = stems[StemSplitter.DRUMS];
                    for (int i = 0; i < count; i++) {
                        float gv = (gt[0][i] + gt[1][i]) * 0.5f;
                        float mv = gv + (ot[0][i] + ot[1][i] + pn[0][i] + pn[1][i]) * 0.5f;
                        float vv = (vo[0][i] + vo[1][i]) * 0.5f;
                        float dv = (ds[0][i] + ds[1][i]) * 0.5f;
                        float bv = (bs[0][i] + bs[1][i]) * 0.5f;
                        float xv = (mixLeft[i] + mixRight[i]) * 0.5f;
                        g[i] = gv;
                        m[i] = mv;
                        v[i] = vv;
                        dr[i] = dv;
                        b[i] = bv;
                        energy[0] += gv * gv;
                        energy[1] += mv * mv;
                        energy[2] += bv * bv;
                        energy[3] += xv * xv;
                        energy[4] += vv * vv;
                        energy[5] += dv * dv;
                    }
                    guitar.feed(g, 0, count);
                    melodic.feed(m, 0, count);
                    vocals.feed(v, 0, count);
                    drums.feed(dr, 0, count);
                    bass.feed(b, 0, count);
                }
            };
            StemSplitter.Progress report = new StemSplitter.Progress() {
                @Override
                public boolean onSegment(final int done, final int total) {
                    long elapsed = System.currentTimeMillis() - started;
                    long left = done > 0 ? elapsed * (total - done) / done : 0;
                    final String text = String.format(Locale.US, "Splitting into instruments: part %d of %d%s. Keep this screen open.",
                            done, total, done < total ? String.format(Locale.US, " (about %d min %02d s left)", left / 60000, (left / 1000) % 60) : "");
                    main.post(new Runnable() {
                        @Override
                        public void run() {
                            progress.setProgress(done * 1000 / total);
                            log.setText(text);
                        }
                    });
                    return true;
                }
            };
            status("Splitting into instruments… Keep this screen open.");
            StemSplitter.split(source, frames, model, sink, report);
        }
        SongWriter.Stems out = new SongWriter.Stems();
        out.guitar = guitar.finish();
        out.melodic = melodic.finish();
        out.vocals = vocals.finish();
        out.drums = drums.finish();
        out.bass = bass.finish();
        out.guitarEnergy = energy[0];
        out.melodicEnergy = energy[1];
        out.bassEnergy = energy[2];
        out.mixEnergy = energy[3];
        out.vocalsEnergy = energy[4];
        out.drumsEnergy = energy[5];
        return out;
    }

    /** Saves the picture embedded in the audio file, if any, so YARG shows it as album art. */
    private void saveAlbumArt(Uri uri, File dir) {
        MediaMetadataRetriever r = new MediaMetadataRetriever();
        try {
            r.setDataSource(this, uri);
            byte[] art = r.getEmbeddedPicture();
            if (art == null || art.length < 16) return;
            boolean png = (art[0] & 0xFF) == 0x89 && art[1] == 'P';
            boolean jpg = (art[0] & 0xFF) == 0xFF && (art[1] & 0xFF) == 0xD8;
            if (!png && !jpg) return;
            try (OutputStream out = new FileOutputStream(new File(dir, png ? "album.png" : "album.jpg"))) {
                out.write(art);
            }
        } catch (RuntimeException | IOException e) {
            // Album art is a nice-to-have; the song works without it.
        } finally {
            try {
                r.release();
            } catch (Exception ignored) {
                // Nothing useful to do if releasing fails.
            }
        }
    }
}
