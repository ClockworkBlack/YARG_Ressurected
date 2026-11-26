package com.clockworkblack.yargconverter;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.content.res.ColorStateList;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
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
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;
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
import com.clockworkblack.yargconverter.core.VocalAnalyzer;
import com.clockworkblack.yargconverter.core.VocalCharter;
import com.clockworkblack.yargconverter.core.Resampler;
import com.clockworkblack.yargconverter.core.WordAligner;
import com.clockworkblack.yargconverter.stems.OrtStemModel;
import com.clockworkblack.yargconverter.stems.OrtWordModel;

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
public final class MainActivity extends Activity implements ConversionService.Listener {
    private static final int REQ_AUDIO = 1;
    private static final int REQ_LRC = 2;
    private static final int REQ_TAP = 3;
    private static final int REQ_PERMISSION = 4;
    private static final int REQ_NOTIFICATIONS = 5;
    private static final int REQ_SINGERS = 6;
    private static final int REQ_BATCH = 7;
    private static final int REQ_BATCH_SINGERS = 8;
    private static final int REQ_BATCH_LRC = 9;
    private static final int REQ_BATCH_TAP = 10;
    /** The song in the list being edited, and its dialog's lyrics box, while the dialog is open. */
    private Queued editing;
    private EditText editingLyrics;
    private TextView editingStatus;
    /** The song in the list whose singers are being chosen. */
    private Queued singersFor;

    /** Most songs converted in one go. */
    static final int BATCH_MAX = 33;

    /** One song waiting in the list. */
    private static final class Queued {
        Uri uri;
        String fileName = "";
        SongMeta meta = new SongMeta();
        double seconds;
        boolean read;
        /** How MusicBrainz credits its artists, when looked up. */
        String credit = "";
        /** How this song is converted: MODE_NORMAL, MODE_SPLIT or MODE_RNB. */
        int mode = MODE_NORMAL;
        /** Lyrics found for it (with who sings each line, once marked), or null to look them up when it is converted. */
        String lrcText;
        /** The lines have been marked by hand with the Singers button. */
        boolean singersMarked;
        /** "How many notes" for this song alone (0 = as chosen in step 3). */
        int density;
    }

    private static String modeName(int mode) {
        return mode == MODE_RNB ? "R&B" : mode == MODE_SPLIT ? "Split" : "Normal";
    }

    /** The next way of converting for a song in the list: Normal, Split, R&B, and round again. */
    private int nextMode(int mode) {
        if (!ModelStore.installed(this)) return MODE_NORMAL;
        return mode == MODE_NORMAL ? MODE_SPLIT : mode == MODE_SPLIT ? MODE_RNB : MODE_NORMAL;
    }

    private final List<Queued> batch = new ArrayList<>();
    private LinearLayout batchList;
    private TextView batchStatus;
    private CheckBox batchLyricsCheck;
    private Button batchPickButton, batchConvertButton, batchClearButton;

    /** Folder inside Internal storage/YARG/songs that converted songs go into. */
    private static final String SUBFOLDER = "Converted";

    private final Handler main = new Handler(Looper.getMainLooper());
    /** The app, not this screen: the conversion keeps using it after the screen is closed. */
    private Context app;

    private Uri audioUri;
    private String audioName = "";
    private double audioSeconds;
    private boolean busy;

    private TextView fileLabel;
    private CheckBox onlineDetailsCheck;
    /** Read on other threads: whether to fill missing details from MusicBrainz. */
    private volatile boolean onlineDetails = true;
    private EditText titleBox, artistBox, albumBox, yearBox, genreBox, lyricsBox;
    private TextView lyricsStatus, log;
    private Button pickButton, findButton, lrcButton, tapButton, convertButton;
    private ProgressBar progress;
    private RadioGroup densityGroup;
    private CheckBox guessCheck;
    private RadioGroup modeGroup;
    private RadioButton modeNormal, modeSplit, modeRnb;
    private static final int MODE_NORMAL = 11, MODE_SPLIT = 12, MODE_RNB = 13;

    // Colours of the screen. Set by hand so every option reads clearly whatever the phone's theme.
    private static final int INK = 0xFF0F1318, CARD = 0xFF1B222B, CARD_ON = 0xFF2B2410, LINE = 0xFF3A4654;
    private static final int TEXT = 0xFFF4F6F8, SOFT = 0xFFC3CCD6, FAINT = 0xFF7D8894, GOLD = 0xFFFFC53D;

    /** A copy of the chosen audio in the app's own storage, and the file it is a copy of. */
    private volatile File audioCopy;
    private volatile Uri audioCopyOf;
    private Button singersButton;
    private TextView singersStatus;
    /** The two artists' names as typed on the "Who sings what" screen; null until that has been used. */
    private String[] singerNames;
    /** How MusicBrainz credits the chosen song's artists ("A feat. B"), when it was looked up. */
    private volatile String creditArtist = "";
    /** The last song's analysis, for the list's summary (how the singers were split). */
    private volatile SongWriter.Result lastResult;
    private TextView modelStatus;
    private Button modelButton;
    private TextView wordsStatus;
    private Button wordsButton;
    private static final int DENSITY_FEWER = 1, DENSITY_STANDARD = 2, DENSITY_MORE = 3;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        app = getApplicationContext();
        buildUi();
        handleIncoming(getIntent());
    }

    @Override
    protected void onResume() {
        super.onResume();
        ConversionService.setListener(this);
        onConversionChanged();
    }

    @Override
    protected void onPause() {
        ConversionService.setListener(null);
        super.onPause();
    }

    /** Shows a conversion that runs (or finished) in the background, including one started before
     * this screen was last closed. */
    @Override
    public void onConversionChanged() {
        if (log == null) return;
        if (ConversionService.isRunning()) {
            if (!busy) setBusy(true);
            progress.setVisibility(View.VISIBLE);
            int p = ConversionService.progress();
            if (p < 0) {
                progress.setIndeterminate(true);
            } else {
                progress.setIndeterminate(false);
                progress.setProgress(p);
            }
            log.setText(ConversionService.text());
            return;
        }

        String result = ConversionService.unseenResult();
        if (result != null) {
            ConversionService.markResultSeen();
            setBusy(false);
            progress.setVisibility(View.GONE);
            log.setText(result);
            // Singers worked out from the voices go back into the lyrics box, to be checked.
            String marked = ConversionService.takeMarkedLyrics();
            if (marked != null && ConversionService.succeeded()) {
                lyricsBox.setText(marked);
                describeLyrics("Singers guessed from their voices.");
            }
            if (!ConversionService.succeeded()) message("Conversion failed", result);
        }
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
        sub.setText("Turns an MP3, WAV or FLAC into guitar and bass charts (Easy to Expert) and a vocals part to sing or rap along to, and puts it in YARG's songs folder.");
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
        onlineDetailsCheck = new CheckBox(this);
        onlineDetailsCheck.setText("Fill in missing details online. Title, artist, album, year and genre come from the file's own tags; whatever they leave out is looked up on MusicBrainz (only the title and artist are sent). The genre also helps YARG's story mode pick songs for each gig.");
        onlineDetailsCheck.setChecked(true);
        onlineDetailsCheck.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(CompoundButton b, boolean checked) {
                onlineDetails = checked;
            }
        });
        root.addView(onlineDetailsCheck);

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
        root.addView(button("Find details online", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (busy) return;
                findDetails(titleBox, artistBox, albumBox, yearBox, genreBox, audioName, fileLabel, new Runnable() {
                    @Override
                    public void run() {
                        // A featured artist found: the names for "who sings what"
                        List<String> names = Lrc.artists(artistBox.getText().toString(), titleBox.getText().toString());
                        if (names.size() >= 2) singerNames = names.toArray(new String[0]);
                    }
                });
            }
        }));

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
        // For words that come up too early or too late in the game
        LinearLayout nudgeButtons = row();
        nudgeButtons.addView(button("Words earlier", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                String note = nudgeLyrics(lyricsBox, -NUDGE_SEC);
                if (note != null) describeLyrics(note);
            }
        }), gapped());
        nudgeButtons.addView(button("Words later", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                String note = nudgeLyrics(lyricsBox, NUDGE_SEC);
                if (note != null) describeLyrics(note);
            }
        }), weighted());
        root.addView(nudgeButtons);

        final android.content.SharedPreferences prefs = getSharedPreferences("converter", MODE_PRIVATE);
        CheckBox netEaseCheck = new CheckBox(this);
        netEaseCheck.setText("Also search NetEase Cloud Music for timed lyrics when LRCLIB has none. Unofficial: it is not a service offered to other apps, and it may stop working at any time. Only the title and artist are sent.");
        LrcLib.useNetEase = prefs.getBoolean("netease", false);
        netEaseCheck.setChecked(LrcLib.useNetEase);
        netEaseCheck.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(CompoundButton b, boolean checked) {
                LrcLib.useNetEase = checked;
                prefs.edit().putBoolean("netease", checked).apply();
            }
        });
        root.addView(netEaseCheck);
        singersButton = button("Two artists? Choose who sings what", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                openSingers();
            }
        });
        root.addView(singersButton);
        singersStatus = new TextView(this);
        singersStatus.setPadding(0, dp(2), 0, dp(4));
        singersStatus.setText("With lyrics, the song gets a vocals part: every player with a microphone can sing or rap it. Mark the lines of two artists and each player picks whose lines to do.");
        root.addView(singersStatus);

        root.addView(section("3. Convert"));
        TextView modeLabel = new TextView(this);
        modeLabel.setText("How to convert");
        modeLabel.setPadding(0, 0, 0, dp(4));
        root.addView(modeLabel);
        modeGroup = new RadioGroup(this);
        modeGroup.setOrientation(RadioGroup.VERTICAL);
        modeNormal = mode(MODE_NORMAL, "Normal", "Quick: about a minute. Charts the guitar from the whole song as it is. No download needed. No bass chart; with lyrics, the words are spread at speaking pace.");
        modeSplit = mode(MODE_SPLIT, "Split into instruments", "Best charts. Separates the song first, so the guitar chart comes from the guitar alone, a bass chart is added, and every word is placed where it is sung. Takes a few minutes.");
        modeRnb = mode(MODE_RNB, "R&B mode", "The voice comes first. Like the split, but the guitar chart follows the lead singer's melody and runs instead of the band. For R&B, soul and vocal-led pop.");
        modeGroup.check(MODE_NORMAL);
        root.addView(modeGroup, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        modeStatusHolder(root);
        guessCheck = new CheckBox(this);
        guessCheck.setText("Two artists: where the lyrics don't say who sings a line, guess it from their voices. For the two split modes. It is a guess; check it afterwards with \"Choose who sings what\".");
        root.addView(guessCheck);
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

        root.addView(section("Several songs at once"));
        batchStatus = new TextView(this);
        batchStatus.setText("Pick up to " + BATCH_MAX + " files and convert them all in one go, one after another, with the mode and number of notes chosen above. Each song's title and artist come from the file's tags (or its name).");
        root.addView(batchStatus);
        batchPickButton = button("Choose files (up to " + BATCH_MAX + ")", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                pickBatch();
            }
        });
        root.addView(batchPickButton);
        batchList = new LinearLayout(this);
        batchList.setOrientation(LinearLayout.VERTICAL);
        root.addView(batchList);
        batchLyricsCheck = new CheckBox(this);
        batchLyricsCheck.setText("Find lyrics online for each song (LRCLIB), so each one gets a vocals part to sing. Songs with no timed lyrics there are converted without vocals.");
        batchLyricsCheck.setChecked(true);
        root.addView(batchLyricsCheck);
        LinearLayout batchRow = row();
        batchConvertButton = button("Convert all", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                onBatchPressed();
            }
        });
        batchClearButton = button("Clear list", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (busy) return;
                batch.clear();
                showBatch();
            }
        });
        batchRow.addView(batchConvertButton, weighted());
        batchRow.addView(batchClearButton, weighted());
        root.addView(batchRow);
        showBatch();

        progress = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        progress.setMax(1000);
        progress.setVisibility(View.GONE);
        root.addView(progress, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        log = new TextView(this);
        log.setPadding(0, dp(10), 0, 0);
        log.setTextIsSelectable(true);
        root.addView(log);

        root.addView(section("Change a song's notes"));
        TextView editInfo = new TextView(this);
        editInfo.setText("Add or take away notes in a song that is already converted, without converting it again.");
        editInfo.setPadding(0, 0, 0, dp(4));
        root.addView(editInfo);
        root.addView(button("Edit a converted song's notes", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (!hasStorageAccess()) {
                    requestStorageAccess();
                    return;
                }
                startActivity(new Intent(MainActivity.this, EditorActivity.class));
            }
        }));

        scroll.setBackgroundColor(INK);
        getWindow().setStatusBarColor(INK);
        getWindow().setNavigationBarColor(INK);
        heading.setTextColor(GOLD);
        style(root);
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

    /** One way of converting: a name in bold with what it does underneath, as a card that lights up when chosen. */
    private RadioButton mode(int id, String name, String what) {
        RadioButton rb = new RadioButton(this);
        rb.setId(id);
        android.text.SpannableString text = new android.text.SpannableString(name + "\n" + what);
        text.setSpan(new android.text.style.StyleSpan(Typeface.BOLD), 0, name.length(), 0);
        text.setSpan(new android.text.style.RelativeSizeSpan(1.15f), 0, name.length(), 0);
        rb.setText(text);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.setMargins(0, dp(4), 0, dp(4));
        modeGroup.addView(rb, lp);
        return rb;
    }

    /** The line about the separation model and its download button, under the ways of converting. */
    private void modeStatusHolder(LinearLayout root) {
        modelStatus = new TextView(this);
        modelStatus.setPadding(0, dp(2), 0, dp(4));
        root.addView(modelStatus);
        modelButton = button("Download the separation model (136 MB)", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                downloadModel(ModelStore.STEMS, modelStatus);
            }
        });
        root.addView(modelButton);
        wordsStatus = new TextView(this);
        wordsStatus.setPadding(0, dp(6), 0, dp(4));
        root.addView(wordsStatus);
        wordsButton = button("Download word timing (378 MB, optional)", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (ModelStore.installed(MainActivity.this, ModelStore.WORDS)) {
                    new AlertDialog.Builder(MainActivity.this)
                            .setTitle("Remove word timing?")
                            .setMessage("Frees 378 MB. Lyrics are then timed from where the voice starts and stops only. You can download it again at any time.")
                            .setPositiveButton("Remove", new DialogInterface.OnClickListener() {
                                @Override
                                public void onClick(DialogInterface d, int which) {
                                    ModelStore.remove(MainActivity.this, ModelStore.WORDS);
                                    refreshModelUi(false);
                                }
                            })
                            .setNegativeButton("Keep", null)
                            .show();
                } else {
                    downloadModel(ModelStore.WORDS, wordsStatus);
                }
            }
        });
        root.addView(wordsButton);
    }

    private GradientDrawable box(int fill, int stroke) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(fill);
        d.setCornerRadius(dp(10));
        if (stroke != 0) d.setStroke(dp(1.5f), stroke);
        return d;
    }

    /**
     * Gives every control on the screen the same clear look: light text on a dark page, options
     * as cards with a gold edge when chosen, gold buttons, and greyed-out things that still read.
     */
    private void style(View v) {
        int[][] states = {{-android.R.attr.state_enabled}, {android.R.attr.state_checked}, {}};
        if (v instanceof CompoundButton) {
            CompoundButton c = (CompoundButton) v;
            c.setTextColor(new ColorStateList(states, new int[] {FAINT, TEXT, TEXT}));
            c.setButtonTintList(new ColorStateList(states, new int[] {FAINT, GOLD, SOFT}));
            StateListDrawable bg = new StateListDrawable();
            bg.addState(new int[] {android.R.attr.state_checked}, box(CARD_ON, GOLD));
            bg.addState(new int[] {}, box(CARD, LINE));
            c.setBackground(bg);
            c.setPadding(dp(10), dp(10), dp(12), dp(10));
            c.setLineSpacing(dp(2), 1f);
        } else if (v instanceof Button) {
            Button b = (Button) v;
            StateListDrawable bg = new StateListDrawable();
            bg.addState(new int[] {-android.R.attr.state_enabled}, box(CARD, LINE));
            bg.addState(new int[] {android.R.attr.state_pressed}, box(0xFFE0A91F, 0));
            bg.addState(new int[] {}, box(GOLD, 0));
            b.setBackground(bg);
            b.setTextColor(new ColorStateList(new int[][] {{-android.R.attr.state_enabled}, {}}, new int[] {FAINT, 0xFF14110A}));
            b.setTypeface(Typeface.DEFAULT_BOLD);
            b.setMinHeight(dp(48));
            ViewGroup.LayoutParams lp = b.getLayoutParams();
            if (lp instanceof LinearLayout.LayoutParams && ((LinearLayout.LayoutParams) lp).topMargin == 0) {
                ((LinearLayout.LayoutParams) lp).topMargin = dp(4);
                ((LinearLayout.LayoutParams) lp).bottomMargin = dp(4);
            }
        } else if (v instanceof EditText) {
            EditText e = (EditText) v;
            e.setTextColor(TEXT);
            e.setHintTextColor(FAINT);
            e.setBackground(box(CARD, LINE));
            e.setPadding(dp(12), dp(10), dp(12), dp(10));
            ViewGroup.LayoutParams lp = e.getLayoutParams();
            if (lp instanceof LinearLayout.LayoutParams) {
                ((LinearLayout.LayoutParams) lp).topMargin = dp(4);
                ((LinearLayout.LayoutParams) lp).bottomMargin = dp(4);
                ((LinearLayout.LayoutParams) lp).rightMargin = dp(4);
            }
        } else if (v instanceof TextView) {
            TextView t = (TextView) v;
            // Headings are bold already: keep those bright, the explanations a little softer
            if (t.getCurrentTextColor() != GOLD) t.setTextColor(t.getTypeface() != null && t.getTypeface().isBold() ? TEXT : SOFT);
        } else if (v instanceof ProgressBar) {
            ((ProgressBar) v).setProgressTintList(ColorStateList.valueOf(GOLD));
            ((ProgressBar) v).setIndeterminateTintList(ColorStateList.valueOf(GOLD));
        }
        if (v instanceof ViewGroup) {
            ViewGroup g = (ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) style(g.getChildAt(i));
        }
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

    /** An equal share of a row, with a small gap on the right. */
    private LinearLayout.LayoutParams gapped() {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        lp.setMargins(0, 0, dp(6), 0);
        return lp;
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
        if (batchPickButton != null) {
            batchPickButton.setEnabled(!value);
            batchConvertButton.setEnabled(!value && !batch.isEmpty());
            batchClearButton.setEnabled(!value && !batch.isEmpty());
            for (int i = 0; i < batchList.getChildCount(); i++) batchList.getChildAt(i).setEnabled(!value);
        }
        modelButton.setEnabled(!value);
        if (wordsButton != null) wordsButton.setEnabled(!value);
        singersButton.setEnabled(!value);
        boolean model = ModelStore.installed(this);
        modeNormal.setEnabled(!value);
        modeSplit.setEnabled(!value && model);
        modeRnb.setEnabled(!value && model);
        guessCheck.setEnabled(!value && model);
        if (value) {
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        } else {
            getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        }
    }

    /** Shows whether the separation model is on the phone, and offers the download if not. */
    private void refreshModelUi(boolean tickIfInstalled) {
        boolean installed = ModelStore.installed(this);
        modeSplit.setEnabled(installed && !busy);
        modeRnb.setEnabled(installed && !busy);
        guessCheck.setEnabled(installed && !busy);
        if (!installed) {
            guessCheck.setChecked(false);
            modeGroup.check(MODE_NORMAL);
        } else if (tickIfInstalled && modeGroup.getCheckedRadioButtonId() == MODE_NORMAL) {
            // Just downloaded (or already there when the app opens): the split is what it is for
            modeGroup.check(MODE_SPLIT);
        }
        modelButton.setVisibility(installed ? View.GONE : View.VISIBLE);
        modelStatus.setText(installed
                ? "Separation model installed (HT-Demucs, 6 instruments)."
                : "The two split modes need a one-time 136 MB download from Hugging Face (HT-Demucs). Wi-Fi recommended. Normal works without it.");
        if (wordsStatus != null) {
            boolean words = ModelStore.installed(this, ModelStore.WORDS);
            // Only of use with the split modes, which hear the voice on its own
            wordsStatus.setVisibility(installed ? View.VISIBLE : View.GONE);
            wordsButton.setVisibility(installed ? View.VISIBLE : View.GONE);
            wordsButton.setText(words ? "Remove word timing (frees 378 MB)" : "Download word timing (378 MB, optional)");
            wordsStatus.setText(words
                    ? "Word timing installed: in the split modes, lyrics are matched to the voice word by word (English, Korean, Japanese kana). Adds about a minute per song."
                    : "Optional: times lyrics word by word against the voice, much more accurately than from where the voice starts and stops. One-time 378 MB download (wav2vec2). Needs a phone with 4 GB of memory or more; Wi-Fi recommended.");
        }
    }

    private void downloadModel(final ModelStore.Spec spec, final TextView modelStatus) {
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
                    ModelStore.download(MainActivity.this, spec, new ModelStore.Progress() {
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
        if (requestCode == REQ_BATCH) {
            addToBatch(data);
        } else if (requestCode == REQ_BATCH_LRC && data.getData() != null && editingLyrics != null) {
            try {
                String text = Lrc.decode(readAll(data.getData(), 2 * 1024 * 1024));
                editingLyrics.setText(text);
                if (editingStatus != null) describeEditLyrics(editingStatus, text);
            } catch (IOException e) {
                message("Could not read file", String.valueOf(e.getMessage()));
            }
        } else if (requestCode == REQ_BATCH_TAP && editingLyrics != null) {
            String lrc = data.getStringExtra(TapSyncActivity.EXTRA_RESULT);
            if (lrc != null && !lrc.isEmpty()) {
                editingLyrics.setText(lrc);
                if (editingStatus != null) describeEditLyrics(editingStatus, lrc);
            }
        } else if (requestCode == REQ_BATCH_SINGERS) {
            Queued q = singersFor;
            singersFor = null;
            String text = data.getStringExtra(SingersActivity.EXTRA_TEXT);
            String[] names = data.getStringArrayExtra(SingersActivity.EXTRA_NAMES);
            if (q != null && batch.contains(q) && text != null) {
                q.lrcText = text;
                q.singersMarked = true;
                if (names != null) q.meta.singers = names;
                // Opened from the Edit dialog: show the marks there too
                if (editingLyrics != null) {
                    editingLyrics.setText(text);
                    if (editingStatus != null) describeEditLyrics(editingStatus, text);
                }
                showBatch();
            }
        } else if (requestCode == REQ_AUDIO && data.getData() != null) {
            setAudio(data.getData());
        } else if (requestCode == REQ_LRC && data.getData() != null) {
            try {
                String text = Lrc.decode(readAll(data.getData(), 2 * 1024 * 1024));
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
        } else if (requestCode == REQ_SINGERS) {
            String text = data.getStringExtra(SingersActivity.EXTRA_TEXT);
            String[] names = data.getStringArrayExtra(SingersActivity.EXTRA_NAMES);
            if (names != null) singerNames = names;
            if (text != null) {
                lyricsBox.setText(text);
                describeLyrics("Singers chosen.");
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
        // A new song: its singers are worked out afresh
        singerNames = null;
        audioSeconds = 0;
        fileLabel.setText(audioName + "\nReading song details…");
        log.setText("");
        new Thread(new Runnable() {
            @Override
            public void run() {
                final String[] tags = new String[5];
                double seconds = 0;
                try {
                    // While the permission to read it is fresh
                    localCopy(uri);
                } catch (IOException | RuntimeException e) {
                    // Tried again, with a message, when the conversion starts
                }
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
                            String[] named = com.clockworkblack.yargconverter.core.SongNames.fromFileName(base);
                            if (artist.isEmpty()) artist = named[0];
                            title = named[1];
                        }
                        titleBox.setText(title);
                        artistBox.setText(artist);
                        albumBox.setText(clean(tags[2]));
                        String year = clean(tags[3]).replaceAll("[^0-9]", "");
                        yearBox.setText(year.length() >= 4 ? year.substring(0, 4) : "");
                        genreBox.setText(clean(tags[4]));
                        String len = secondsFinal > 0 ? String.format(Locale.US, " (%d:%02d)", (int) secondsFinal / 60, (int) secondsFinal % 60) : "";
                        fileLabel.setText(audioName + len);
                        fillFromOnline(uri, audioName + len);
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

    /** How far one press of "Words earlier" or "Words later" moves the lyrics, in seconds. */
    private static final double NUDGE_SEC = 0.5;

    /**
     * Moves every timed line in a lyrics box earlier or later, for a song whose words come up at
     * the wrong moment in the game. Lyrics moved this way keep their times: the converter no
     * longer lines them up with the voice by itself.
     *
     * @return what to tell the user, or null when a message was shown instead
     */
    private String nudgeLyrics(EditText box, double seconds) {
        String text = box.getText().toString();
        if (!Lrc.hasTimestamps(text)) {
            message("Timed lyrics needed", "Find, open or tap in the lyrics first: only lines with times like [01:23.45] can be moved.");
            return null;
        }
        box.setText(Lrc.shiftText(text, seconds));
        return String.format(Locale.US, "All lines moved %.1f seconds %s. These times are now used as they are; convert the song again to hear the difference.",
                Math.abs(seconds), seconds > 0 ? "later" : "earlier");
    }

    private void describeLyrics(String prefix) {
        String text = lyricsBox.getText().toString();
        if (text.trim().isEmpty()) {
            lyricsStatus.setText(prefix);
        } else if (Lrc.hasTimestamps(text)) {
            lyricsStatus.setText(prefix + " " + Lrc.parse(text).size() + " timed lines, ready to use.");
        } else {
            lyricsStatus.setText(prefix + " " + Lrc.plainLines(text).size() + " lines without times. Use \"Tap to sync\" to time them.");
        }
        describeSingers(text);
    }

    /** The two artists of the song: as typed on the "Who sings what" screen, else read from the artist and title. */
    private String[] singers() {
        String[] names = singerNames;
        if (names == null) {
            names = Lrc.artists(artistBox.getText().toString(), titleBox.getText().toString()).toArray(new String[0]);
        }
        String first = names.length > 0 ? names[0].trim() : "";
        String second = names.length > 1 ? names[1].trim() : "";
        return new String[] {first.isEmpty() ? "Singer 1" : first, second.isEmpty() ? "Singer 2" : second};
    }

    private void describeSingers(String text) {
        int first = 0, second = 0, both = 0;
        List<Lrc.Line> lines = new ArrayList<>();
        if (Lrc.hasTimestamps(text)) {
            lines = Lrc.parse(text);
        } else {
            int[] singer = new int[1];
            for (String l : Lrc.plainLines(text, java.util.Arrays.asList(singers()))) lines.add(new Lrc.Line(0, Lrc.stripMark(l, singer), singer[0]));
        }
        for (Lrc.Line l : lines) {
            if (l.text.isEmpty()) continue;
            if (l.singer == Lrc.SECOND) second++;
            else if (l.singer == Lrc.BOTH) both++;
            else first++;
        }
        if (first + second + both == 0) {
            singersStatus.setText("With lyrics, the song gets a vocals part: every player with a microphone can sing or rap it. Mark the lines of two artists and each player picks whose lines to do.");
        } else if (second == 0) {
            singersStatus.setText("One vocals part for everybody (no lines marked for a second artist).");
        } else {
            String[] names = singers();
            singersStatus.setText(names[0] + ": " + first + " lines.  " + names[1] + ": " + second + " lines." + (both > 0 ? "  Both: " + both + "." : "")
                    + "  In the game, pick Harmonies as the instrument to choose an artist.");
        }
    }

    private void openSingers() {
        String text = lyricsBox.getText().toString();
        if (text.trim().isEmpty()) {
            message("Lyrics needed", "Find or paste the lyrics first, then choose who sings each line.");
            return;
        }
        Intent i = new Intent(this, SingersActivity.class);
        i.putExtra(SingersActivity.EXTRA_TEXT, text);
        i.putExtra(SingersActivity.EXTRA_NAMES, singers());
        startActivityForResult(i, REQ_SINGERS);
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
        final String fileForLookup = audioName;
        lyricsStatus.setText("Searching lrclib.net…");
        findButton.setEnabled(false);
        new Thread(new Runnable() {
            @Override
            public void run() {
                LrcLib.Found found = null;
                String error = null;
                try {
                    found = LrcLib.find(title, artist, album, seconds, fileForLookup);
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
                            // Who sings what, from the lyrics themselves when they say
                            List<String> names = findSingers(title, artist, creditArtist, f);
                            if (singerNames == null && names.size() >= 2) singerNames = names.toArray(new String[0]);
                            String lyricsText = markedLyrics(f, java.util.Arrays.asList(singers()));
                            lyricsBox.setText(lyricsText);
                            String note = "Found timed lyrics for \"" + f.title + "\" by " + f.artist + " (" + f.source + ").";
                            if (hasMarks(lyricsText)) {
                                note += " The lyrics say who sings what (" + singers()[0] + " / " + singers()[1] + "): check it with \"Choose who sings what\".";
                            } else if (names.size() >= 2) {
                                note += " Two artists (" + names.get(0) + " / " + names.get(1) + "), but the lyrics don't say who sings what: with a split mode their voices decide, or mark the lines yourself.";
                            }
                            if (seconds > 1 && f.duration > 0 && Math.abs(f.duration - seconds) > 3) {
                                note += String.format(Locale.US, " Their recording is %d s %s than your file, so the timing may drift.",
                                        Math.round(Math.abs(f.duration - seconds)), f.duration > seconds ? "longer" : "shorter");
                            }
                            describeLyrics(note);
                        } else if (f.plain != null) {
                            lyricsBox.setText(f.plain);
                            describeLyrics("Found the words on " + f.source + ", but no times for them anywhere. Press \"Tap to sync\" to time them.");
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
            for (Lrc.Line l : Lrc.parse(text)) if (!l.text.isEmpty()) lines.add(Lrc.mark(l.singer) + l.text);
        } else {
            lines.addAll(Lrc.plainLines(text, java.util.Arrays.asList(singers())));
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
                    .setMessage("To place songs in Internal storage/YARG/songs, turn on \"Allow access to manage all files\" for YARG Converter on the next screen, then come back and try again.")
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
        int mode = modeGroup.getCheckedRadioButtonId();
        final boolean useStems = mode != MODE_NORMAL && ModelStore.installed(this);
        meta.singers = singers();
        final boolean guessSingers = voicesDecide(useStems, guessCheck.isChecked(), singerNames != null ? singerNames
                : Lrc.artists(meta.artist, meta.title).toArray(new String[0]));
        final boolean rnb = useStems && mode == MODE_RNB;

        setBusy(true);
        progress.setIndeterminate(false);
        progress.setProgress(0);
        progress.setVisibility(View.VISIBLE);
        log.setText("Listening to the song…");

        // Keeps going with the app in the background or the screen off, with a notification
        askForNotifications();
        ConversionService.begin(this, meta.titleOrDefault());
        ConversionService.setTextAndProgress("Listening to the song… You can leave the app; it keeps going.", 0);

        new Thread(new Runnable() {
            @Override
            public void run() {
                String summary;
                boolean ok = false;
                try {
                    summary = runConversion(uri, name, meta, lyrics, lrcText, options, useStems, guessSingers, rnb);
                    ok = true;
                } catch (IOException e) {
                    summary = "Conversion failed: " + e.getMessage();
                } catch (RuntimeException | OutOfMemoryError e) {
                    summary = "Conversion failed unexpectedly: " + e;
                }
                // Shown here if the app is open, otherwise as a notification and when it's next opened
                ConversionService.finish(ok, summary);
            }
        }, "convert").start();
    }

    /**
     * Copies the chosen audio into the app's own storage, once. The permission to read a file
     * picked or shared from another app only lasts while this screen is alive, and a conversion
     * carries on long after that.
     */
    private synchronized File localCopy(Uri uri) throws IOException {
        File copy = audioCopy;
        if (copy != null && uri.equals(audioCopyOf) && copy.isFile() && copy.length() > 0) return copy;
        File target = new File(app.getCacheDir(), "chosen-audio");
        audioCopy = null;
        audioCopyOf = null;
        try (InputStream in = app.getContentResolver().openInputStream(uri);
             OutputStream out = new FileOutputStream(target)) {
            if (in == null) throw new IOException("The audio file could not be opened. Choose it again.");
            byte[] buf = new byte[1 << 16];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        } catch (SecurityException e) {
            throw new IOException("Android no longer lets the app read that file. Tap \"Choose audio file\" and pick it again.");
        }
        audioCopy = target;
        audioCopyOf = uri;
        return target;
    }

    // ---------------------------------------------------------------- who sings what, automatically

    /**
     * The song's two singers from everything that names them: the artist as tagged, as the lyric
     * service has it, as MusicBrainz credits it, a "(feat. B)" in the title, and the names in the
     * lyrics' own section headers. One name (or none) when nothing names a second.
     */
    private static List<String> findSingers(String title, String artist, String credit, LrcLib.Found f) {
        return Lrc.findSingers(title, f == null ? null : f.plain, artist, credit, f == null ? "" : f.artist,
                f == null ? "" : f.title.matches("(?i).*[\\[(]\\s*(feat|ft|featuring|with)\\b.*") ? artist + " " + f.title : "");
    }

    /**
     * The timed lyrics, with each line given to the singer the plain lyrics' headers name when
     * they do ("[Verse 2: Travis Scott]"). Otherwise the timed lyrics as found: with two singers
     * and a split, the voices then decide (see {@link #voicesDecide}).
     */
    private static String markedLyrics(LrcLib.Found f, List<String> names) {
        String marked = Lrc.markFromHeaders(f.synced, f.plain, names);
        return marked != null ? marked : f.synced;
    }

    /** Whether any line of timed lyrics says who sings it. */
    private static boolean hasMarks(String lrcText) {
        for (Lrc.Line l : Lrc.parse(lrcText)) if (l.singer != Lrc.UNMARKED) return true;
        return false;
    }

    /**
     * The fallback: a song with two named singers whose lyrics do not say who sings what is split
     * by the sound of their voices, whenever the song is separated into instruments. With one
     * named singer that only happens when the "guess it from their voices" box is ticked.
     */
    private static boolean voicesDecide(boolean useStems, boolean boxTicked, String[] names) {
        boolean two = names != null && names.length >= 2 && !blank(names[0]) && !blank(names[1])
                && !names[0].startsWith("Singer ") && !names[1].startsWith("Singer ");
        return useStems && (boxTicked || two);
    }

    // ---------------------------------------------------------------- details from MusicBrainz

    /**
     * "Find details online": looks the song up on MusicBrainz from what is in the boxes (or the
     * file name), fills the empty boxes, and when what it found differs from what is already
     * there, asks before replacing it. Used by the single-song screen and each song's Edit page.
     */
    private void findDetails(final EditText title, final EditText artist, final EditText album, final EditText year,
                             final EditText genre, final String fileName, final TextView status, final Runnable after) {
        String t = title.getText().toString().trim();
        final String a = artist.getText().toString().trim();
        if (t.isEmpty() && fileName != null) {
            // Nothing typed: the file name, "Artist - Title" or just the title
            t = com.clockworkblack.yargconverter.core.SongNames.cleanTitle(
                    com.clockworkblack.yargconverter.core.SongNames.fromFileName(fileName)[1]);
        }
        if (t.isEmpty()) {
            status.setText("Type the title (and the artist if you know it) first.");
            return;
        }
        final String askTitle = t;
        status.setText("Looking the song up on MusicBrainz…");
        new Thread(new Runnable() {
            @Override
            public void run() {
                MusicBrainz.Found f = null;
                String error = null;
                try {
                    f = MusicBrainz.find(askTitle, a);
                    // With the artist the search may be too narrow (a typo, a nickname): try the title alone
                    if (f == null && !a.isEmpty()) f = MusicBrainz.find(askTitle, "");
                } catch (IOException | RuntimeException e) {
                    error = e.getMessage();
                }
                final MusicBrainz.Found found = f;
                final String why = error;
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        if (found == null) {
                            status.setText(why != null ? "Could not reach MusicBrainz: " + why
                                    : "No match found on MusicBrainz. Check the spelling of the title and artist.");
                            return;
                        }
                        EditText[] boxes = {title, artist, album, year, genre};
                        String[] values = {found.title, found.artist, found.album, found.year, found.genre};
                        String[] names = {"title", "artist", "album", "year", "genre"};
                        final List<Integer> different = new ArrayList<>();
                        List<String> filled = new ArrayList<>();
                        for (int i = 0; i < boxes.length; i++) {
                            if (blank(values[i])) continue;
                            String now = boxes[i].getText().toString().trim();
                            if (now.isEmpty()) {
                                boxes[i].setText(values[i]);
                                filled.add(names[i]);
                            } else if (!now.equalsIgnoreCase(values[i].trim())) {
                                different.add(i);
                            }
                        }
                        String found1 = "Found: " + found.artist + " - " + found.title
                                + (blank(found.album) ? "" : ", " + found.album) + (blank(found.year) ? "" : " (" + found.year + ")")
                                + (blank(found.genre) ? "" : ", " + found.genre) + ".";
                        status.setText(found1 + (filled.isEmpty() ? "" : " Filled in: " + android.text.TextUtils.join(", ", filled) + "."));
                        if (after != null) after.run();
                        if (different.isEmpty()) return;
                        StringBuilder sb = new StringBuilder("MusicBrainz has these differently. Use its versions?\n");
                        for (int i : different) {
                            sb.append("\n").append(names[i]).append(": \"").append(boxes[i].getText().toString().trim())
                                    .append("\"  ->  \"").append(values[i]).append('"');
                        }
                        final EditText[] b = boxes;
                        final String[] v = values;
                        new AlertDialog.Builder(MainActivity.this)
                                .setTitle("Replace details?")
                                .setMessage(sb.toString())
                                .setPositiveButton("Use these", new DialogInterface.OnClickListener() {
                                    @Override
                                    public void onClick(DialogInterface d, int which) {
                                        for (int i : different) b[i].setText(v[i]);
                                        if (after != null) after.run();
                                    }
                                })
                                .setNegativeButton("Keep mine", null)
                                .show();
                    }
                });
            }
        }, "find-details").start();
    }


    /** True when a box is empty, so an online detail may go in it. */
    private static boolean blank(String s) {
        return s == null || s.trim().isEmpty();
    }

    /**
     * Looks the chosen song up on MusicBrainz when its tags left something out, and puts what is
     * found into the boxes that are still empty. Nothing typed or read from the file is replaced.
     */
    private void fillFromOnline(final Uri uri, final String label) {
        final String title = titleBox.getText().toString();
        final String artist = artistBox.getText().toString();
        creditArtist = "";
        boolean missing = blank(artist) || blank(albumBox.getText().toString()) || blank(yearBox.getText().toString())
                || blank(genreBox.getText().toString()) || Lrc.artists(artist, title).size() < 2;
        if (!onlineDetails || !missing || blank(title)) return;
        fileLabel.setText(label + "\nLooking up missing details…");
        new Thread(new Runnable() {
            @Override
            public void run() {
                MusicBrainz.Found found = null;
                String error = null;
                try {
                    found = MusicBrainz.find(title, artist);
                } catch (IOException | RuntimeException e) {
                    error = e.getMessage();
                }
                final MusicBrainz.Found f = found;
                final String why = error;
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        if (!uri.equals(audioUri)) return;
                        if (f == null) {
                            fileLabel.setText(label + (why != null ? "\nCould not look up details online." : "\nNo match found online; fill in anything missing by hand."));
                            return;
                        }
                        List<String> filled = new ArrayList<>();
                        creditArtist = f.artist;
                        // A featured artist the tags left out: the names for "who sings what"
                        List<String> credited = Lrc.artists(f.artist, f.title);
                        if (singerNames == null && credited.size() >= 2 && Lrc.artists(artistBox.getText().toString(), title).size() < 2) {
                            singerNames = credited.toArray(new String[0]);
                            filled.add("featured artist (" + credited.get(1) + ")");
                        }
                        if (blank(artistBox.getText().toString()) && !blank(f.artist)) {
                            artistBox.setText(f.artist);
                            filled.add("artist");
                        } else if (sameNameOtherSpelling(artistBox.getText().toString(), f.artist)) {
                            // "Girls Generation" from a file name: written as the record has it
                            artistBox.setText(f.artist);
                            filled.add("artist's spelling");
                        }
                        if (blank(albumBox.getText().toString()) && !blank(f.album)) {
                            albumBox.setText(f.album);
                            filled.add("album");
                        }
                        if (blank(yearBox.getText().toString()) && !blank(f.year)) {
                            yearBox.setText(f.year);
                            filled.add("year");
                        }
                        if (blank(genreBox.getText().toString()) && !blank(f.genre)) {
                            genreBox.setText(f.genre);
                            filled.add("genre");
                        }
                        fileLabel.setText(label + (filled.isEmpty() ? "" : "\nFilled in from MusicBrainz: " + android.text.TextUtils.join(", ", filled) + ". Check them."));
                    }
                });
            }
        }, "details").start();
    }

    /** The same for a song in the list (on its own thread, so it waits for the answer). */
    private void fillFromOnline(Queued q) {
        SongMeta m = q.meta;
        if (!onlineDetails || blank(m.title)) return;
        if (!(blank(m.artist) || blank(m.album) || blank(m.year) || blank(m.genre) || m.singers.length < 2)) return;
        try {
            MusicBrainz.Found f = MusicBrainz.find(m.title, m.artist);
            if (f == null) return;
            q.credit = f.artist;
            List<String> credited = Lrc.artists(f.artist, f.title);
            if (m.singers.length < 2 && credited.size() >= 2) m.singers = credited.toArray(new String[0]);
            if ((blank(m.artist) && !blank(f.artist)) || sameNameOtherSpelling(m.artist, f.artist)) {
                m.artist = f.artist;
                m.singers = Lrc.artists(m.artist, m.title).toArray(new String[0]);
            }
            if (blank(m.album)) m.album = f.album;
            if (blank(m.year)) m.year = f.year;
            if (blank(m.genre)) m.genre = f.genre;
        } catch (IOException | RuntimeException e) {
            // The file's own details are used
        }
    }

    /** The same name written another way ("Girls Generation", "Girls' Generation"). */
    private static boolean sameNameOtherSpelling(String ours, String found) {
        if (blank(ours) || blank(found) || ours.trim().equals(found.trim())) return false;
        String k = com.clockworkblack.yargconverter.core.SongNames.key(ours);
        return !k.isEmpty() && k.equals(com.clockworkblack.yargconverter.core.SongNames.key(found));
    }

    // ---------------------------------------------------------------- several songs

    private void pickBatch() {
        if (busy) return;
        Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        i.addCategory(Intent.CATEGORY_OPENABLE);
        i.setType("audio/*");
        i.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {"audio/*", "application/ogg", "application/x-flac"});
        i.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        try {
            startActivityForResult(i, REQ_BATCH);
        } catch (RuntimeException e) {
            message("No file picker", "This phone has no file picker app available.");
        }
    }

    private void addToBatch(Intent data) {
        List<Uri> picked = new ArrayList<>();
        if (data.getClipData() != null) {
            for (int i = 0; i < data.getClipData().getItemCount(); i++) picked.add(data.getClipData().getItemAt(i).getUri());
        } else if (data.getData() != null) {
            picked.add(data.getData());
        }
        int left = 0;
        final List<Queued> added = new ArrayList<>();
        for (Uri uri : picked) {
            boolean already = false;
            for (Queued q : batch) already |= q.uri.equals(uri);
            if (already || uri == null) continue;
            if (batch.size() >= BATCH_MAX) {
                left++;
                continue;
            }
            // Kept readable after this screen closes: the songs are read one at a time, much later
            try {
                getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
            } catch (RuntimeException e) {
                // Not every file picker offers it; the song is still read while the app is open
            }
            Queued q = new Queued();
            q.uri = uri;
            // Starts as the way chosen above; tap its button in the list to change it for this song
            q.mode = modeGroup.getCheckedRadioButtonId();
            q.fileName = displayName(uri);
            q.meta.title = q.fileName.replaceFirst("\\.[A-Za-z0-9]{2,5}$", "");
            batch.add(q);
            added.add(q);
        }
        if (left > 0) {
            message("List is full", left + (left == 1 ? " file was" : " files were") + " left out: at most " + BATCH_MAX + " songs go in one list.");
        }
        showBatch();
        // Title, artist and the rest from each file's tags, off the main thread
        new Thread(new Runnable() {
            @Override
            public void run() {
                for (final Queued q : added) {
                    readTags(q);
                    main.post(new Runnable() {
                        @Override
                        public void run() {
                            showBatch();
                        }
                    });
                }
            }
        }, "batch-tags").start();
    }

    /** Fills in a queued song's details from its tags, falling back to "Artist - Title" in the file name. */
    private void readTags(Queued q) {
        String[] tags = new String[5];
        MediaMetadataRetriever r = new MediaMetadataRetriever();
        try {
            r.setDataSource(app, q.uri);
            tags[0] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_TITLE);
            tags[1] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ARTIST);
            if (tags[1] == null) tags[1] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ALBUMARTIST);
            tags[2] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ALBUM);
            tags[3] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_YEAR);
            if (tags[3] == null) tags[3] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DATE);
            tags[4] = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_GENRE);
            String d = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION);
            if (d != null) q.seconds = Long.parseLong(d) / 1000.0;
        } catch (RuntimeException e) {
            // Tags are optional; the file name is used instead
        } finally {
            try {
                r.release();
            } catch (Exception ignored) {
                // Nothing useful to do if releasing fails
            }
        }
        String base = q.fileName.replaceFirst("\\.[A-Za-z0-9]{2,5}$", "");
        String title = clean(tags[0]);
        String artist = clean(tags[1]);
        if (title.isEmpty()) {
            String[] named = com.clockworkblack.yargconverter.core.SongNames.fromFileName(base);
            if (artist.isEmpty()) artist = named[0];
            title = named[1];
        }
        SongMeta m = new SongMeta();
        m.title = title;
        m.artist = artist;
        m.album = clean(tags[2]);
        String year = clean(tags[3]).replaceAll("[^0-9]", "");
        m.year = year.length() >= 4 ? year.substring(0, 4) : "";
        m.genre = clean(tags[4]);
        m.singers = Lrc.artists(artist, title).toArray(new String[0]);
        q.meta = m;
        fillFromOnline(q);
        q.read = true;
    }

    /** Redraws the list: one row a song, with a button to take it out. */
    private void showBatch() {
        batchList.removeAllViews();
        for (int i = 0; i < batch.size(); i++) {
            final Queued q = batch.get(i);
            // The song on one line, its buttons on the line under it, so they fit a phone held upright
            LinearLayout r = new LinearLayout(this);
            r.setOrientation(LinearLayout.VERTICAL);
            r.setPadding(0, dp(4), 0, dp(8));
            LinearLayout buttons = row();
            TextView t = new TextView(this);
            String who = q.meta.artist == null || q.meta.artist.isEmpty() ? "" : q.meta.artist + " - ";
            String len = q.seconds > 0 ? String.format(Locale.US, "  (%d:%02d)", (int) q.seconds / 60, (int) q.seconds % 60) : "";
            String extra = "";
            if (q.lrcText != null) extra += Lrc.hasTimestamps(q.lrcText) ? "  · lyrics ready" : "  · lyrics need times";
            if (q.density != 0) extra += q.density == DENSITY_FEWER ? "  · fewer notes" : q.density == DENSITY_MORE ? "  · more notes" : "  · standard notes";
            t.setText((i + 1) + ". " + who + q.meta.titleOrDefault() + len + (q.read ? "" : "  (reading…)") + extra);
            t.setPadding(0, dp(6), 0, dp(2));
            r.addView(t);
            r.addView(buttons);
            final Button way = button(modeName(q.mode), new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (busy) return;
                    if (!ModelStore.installed(MainActivity.this)) {
                        message("Normal only for now", "Split and R&B mode need the separation model. Download it in step 3 first.");
                        return;
                    }
                    q.mode = nextMode(q.mode);
                    ((Button) v).setText(modeName(q.mode));
                }
            });
            buttons.addView(way, gapped());
            Button singersButton = button(q.singersMarked ? "Singers ✓" : "Singers", new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (busy) return;
                    chooseSingers(q);
                }
            });
            buttons.addView(singersButton, gapped());
            Button edit = button("Edit", new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (busy) return;
                    editSong(q);
                }
            });
            buttons.addView(edit, gapped());
            Button remove = button("Remove", new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (busy) return;
                    batch.remove(q);
                    showBatch();
                }
            });
            buttons.addView(remove, gapped());
            batchList.addView(r);
            style(r);
        }
        batchConvertButton.setText(batch.isEmpty() ? "Convert all" : "Convert all " + batch.size());
        batchConvertButton.setEnabled(!busy && !batch.isEmpty());
        batchClearButton.setEnabled(!busy && !batch.isEmpty());
    }

    /** The way a song was really converted: Split and R&B fall back to Normal without the model. */
    private static int useStemsNote(Queued q, boolean model) {
        return model ? q.mode : MODE_NORMAL;
    }

    /**
     * Opens "who sings what" for one song of the list. Its lyrics are looked up first (once), so
     * the lines can be marked before the list is converted.
     */
    private void chooseSingers(final Queued q) {
        if (q.lrcText != null) {
            openBatchSingers(q);
            return;
        }
        batchStatus.setText("Looking up the lyrics of \"" + q.meta.titleOrDefault() + "\"…");
        new Thread(new Runnable() {
            @Override
            public void run() {
                if (!q.read) readTags(q);
                String text = null;
                String why = "No timed lyrics were found online for this song.";
                try {
                    LrcLib.Found found = LrcLib.find(q.meta.title, q.meta.artist, q.meta.album, q.seconds, q.fileName);
                    if (found != null && found.synced != null) {
                        List<String> names = findSingers(q.meta.title, q.meta.artist, q.credit, found);
                        if (names.size() >= 2) q.meta.singers = names.toArray(new String[0]);
                        text = markedLyrics(found, names);
                    }
                } catch (IOException e) {
                    why = "The lyrics could not be looked up: " + e.getMessage();
                }
                final String lyrics = text;
                final String reason = why;
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        batchStatus.setText("");
                        if (lyrics == null) {
                            message("No lyrics to mark", reason + " Convert this song on its own to paste or tap in its lyrics.");
                            return;
                        }
                        q.lrcText = lyrics;
                        openBatchSingers(q);
                    }
                });
            }
        }, "batch-lyrics").start();
    }

    private void openBatchSingers(Queued q) {
        String[] names = q.meta.singers;
        String first = names.length > 0 && !blank(names[0]) ? names[0].trim() : "Singer 1";
        String second = names.length > 1 && !blank(names[1]) ? names[1].trim() : "Singer 2";
        singersFor = q;
        Intent i = new Intent(this, SingersActivity.class);
        i.putExtra(SingersActivity.EXTRA_TEXT, q.lrcText);
        i.putExtra(SingersActivity.EXTRA_NAMES, new String[] {first, second});
        startActivityForResult(i, REQ_BATCH_SINGERS);
    }

    // ---------------------------------------------------------------- editing one song of the list

    /**
     * Everything the single-song screen offers, for one song of the list: its details, how many
     * notes, its lyrics (found online, opened from a .lrc file, pasted, or tapped in time), and
     * who sings what.
     */
    private void editSong(final Queued q) {
        if (!q.read) {
            message("One moment", "The song's details are still being read. Try again in a second.");
            return;
        }
        ScrollView scroll = new ScrollView(this);
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(18), dp(8), dp(18), dp(8));
        // The screen's own dark colours, so its light text reads whatever the dialog's theme is
        scroll.setBackgroundColor(INK);
        scroll.addView(box);

        final EditText title = field(box, "Title");
        title.setText(q.meta.title);
        final EditText artist = field(box, "Artist (e.g. \"A feat. B\")");
        artist.setText(q.meta.artist);
        final EditText album = field(box, "Album (optional)");
        album.setText(q.meta.album);
        LinearLayout r = row();
        final EditText year = new EditText(this);
        year.setHint("Year");
        year.setSingleLine(true);
        year.setInputType(InputType.TYPE_CLASS_NUMBER);
        year.setText(q.meta.year);
        r.addView(year, weighted());
        final EditText genre = new EditText(this);
        genre.setHint("Genre");
        genre.setSingleLine(true);
        genre.setText(q.meta.genre);
        r.addView(genre, weighted());
        box.addView(r);
        final TextView detailsStatus = new TextView(this);
        box.addView(button("Find details online", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                findDetails(title, artist, album, year, genre, q.fileName, detailsStatus, null);
            }
        }));
        detailsStatus.setPadding(0, dp(2), 0, dp(4));
        box.addView(detailsStatus);

        // One button that steps through the choices: four side by side do not fit a phone
        final int[] density = {q.density};
        final String[] densityNames = {"As in step 3", "Fewer", "Standard", "More"};
        final Button notesButton = button("Notes: " + densityNames[density[0]], null);
        notesButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                density[0] = (density[0] + 1) % 4;
                notesButton.setText("Notes: " + densityNames[density[0]] + "   (tap to change)");
            }
        });
        notesButton.setText("Notes: " + densityNames[density[0]] + "   (tap to change)");
        box.addView(notesButton);

        TextView lyricsLabel = new TextView(this);
        lyricsLabel.setText("Lyrics");
        lyricsLabel.setPadding(0, dp(10), 0, 0);
        box.addView(lyricsLabel);
        final EditText lyrics = new EditText(this);
        lyrics.setHint("Empty: found online when converting. Or paste them here, open a .lrc file, or find them now.");
        lyrics.setMinLines(4);
        lyrics.setMaxLines(10);
        lyrics.setGravity(Gravity.TOP);
        lyrics.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        lyrics.setText(q.lrcText == null ? "" : q.lrcText);
        box.addView(lyrics, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        final TextView status = new TextView(this);
        status.setPadding(0, dp(2), 0, dp(4));
        box.addView(status);
        describeEditLyrics(status, lyrics.getText().toString());

        LinearLayout lr1 = row();
        lr1.addView(button("Find online", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                findForEdit(q, title.getText().toString(), artist.getText().toString(), album.getText().toString(), lyrics, status);
            }
        }), gapped());
        lr1.addView(button("Open .lrc", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                startEditActivity(q, lyrics, status, REQ_BATCH_LRC);
            }
        }), gapped());
        box.addView(lr1);
        LinearLayout lr2 = row();
        lr2.addView(button("Tap to sync", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                startEditActivity(q, lyrics, status, REQ_BATCH_TAP);
            }
        }), gapped());
        lr2.addView(button("Who sings what", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                q.meta.artist = artist.getText().toString().trim();
                q.meta.singers = Lrc.artists(q.meta.artist, title.getText().toString()).toArray(new String[0]);
                String text = lyrics.getText().toString();
                if (text.trim().isEmpty()) {
                    message("Lyrics needed", "Find, open or paste the lyrics first.");
                    return;
                }
                q.lrcText = text;
                editingLyrics = lyrics;
                editingStatus = status;
                openBatchSingers(q);
            }
        }), gapped());
        box.addView(lr2);
        LinearLayout lr3 = row();
        lr3.addView(button("Words earlier", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                String note = nudgeLyrics(lyrics, -NUDGE_SEC);
                if (note != null) status.setText(note);
            }
        }), gapped());
        lr3.addView(button("Words later", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                String note = nudgeLyrics(lyrics, NUDGE_SEC);
                if (note != null) status.setText(note);
            }
        }), gapped());
        box.addView(lr3);
        style(box);

        new AlertDialog.Builder(this)
                .setTitle("Edit song " + (batch.indexOf(q) + 1))
                .setView(scroll)
                .setPositiveButton("Save", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface d, int which) {
                        String oldArtist = q.meta.artist;
                        q.meta.title = title.getText().toString().trim();
                        q.meta.artist = artist.getText().toString().trim();
                        q.meta.album = album.getText().toString().trim();
                        q.meta.year = year.getText().toString().trim();
                        q.meta.genre = genre.getText().toString().trim();
                        if (!q.singersMarked || !q.meta.artist.equals(oldArtist)) {
                            q.meta.singers = Lrc.artists(q.meta.artist, q.meta.title).toArray(new String[0]);
                        }
                        q.density = density[0];
                        String text = lyrics.getText().toString();
                        if (!text.equals(q.lrcText == null ? "" : q.lrcText)) {
                            q.lrcText = text.trim().isEmpty() ? null : text;
                            q.singersMarked = q.lrcText != null && hasMarks(q.lrcText);
                        }
                        editing = null;
                        editingLyrics = null;
                        editingStatus = null;
                        showBatch();
                    }
                })
                .setNegativeButton("Cancel", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface d, int which) {
                        editing = null;
                        editingLyrics = null;
                        editingStatus = null;
                    }
                })
                .show();
    }

    private void describeEditLyrics(TextView status, String text) {
        if (text.trim().isEmpty()) {
            status.setText("No lyrics yet: they are looked up online when the list is converted.");
        } else if (!Lrc.hasTimestamps(text)) {
            status.setText(Lrc.plainLines(text).size() + " lines without times. Press \"Tap to sync\" to time them; without times the song gets no vocals.");
        } else {
            int lines = 0;
            for (Lrc.Line l : Lrc.parse(text)) if (!l.text.isEmpty()) lines++;
            status.setText(lines + " timed lines" + (hasMarks(text) ? ", with who sings what." : "."));
        }
    }

    private void findForEdit(final Queued q, final String title, final String artist, final String album, final EditText lyrics, final TextView status) {
        status.setText("Searching…");
        new Thread(new Runnable() {
            @Override
            public void run() {
                LrcLib.Found f = null;
                String error = null;
                try {
                    f = LrcLib.find(title, artist, album, q.seconds, q.fileName);
                } catch (IOException e) {
                    error = e.getMessage();
                }
                final LrcLib.Found found = f;
                final String why = error;
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        if (found == null) {
                            status.setText(why != null ? "Lookup failed: " + why : "Nothing found. Check the title and artist, or paste the lyrics.");
                            return;
                        }
                        if (found.synced != null) {
                            List<String> names = findSingers(title, artist, q.credit, found);
                            lyrics.setText(markedLyrics(found, names));
                        } else if (found.plain != null) {
                            lyrics.setText(found.plain);
                        } else {
                            status.setText("Listed as instrumental (no lyrics).");
                            return;
                        }
                        describeEditLyrics(status, lyrics.getText().toString());
                        status.setText(status.getText() + " From " + found.source + ".");
                    }
                });
            }
        }, "edit-lyrics").start();
    }

    /** Opens a .lrc file or the tap-in-time screen for the song being edited; the result goes back into its box. */
    private void startEditActivity(Queued q, EditText lyrics, TextView status, int request) {
        editing = q;
        editingLyrics = lyrics;
        editingStatus = status;
        if (request == REQ_BATCH_LRC) {
            Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(Intent.CATEGORY_OPENABLE);
            i.setType("*/*");
            try {
                startActivityForResult(i, REQ_BATCH_LRC);
            } catch (RuntimeException e) {
                message("No file picker", "This phone has no file picker app available.");
            }
            return;
        }
        String text = lyrics.getText().toString();
        ArrayList<String> lines = new ArrayList<>();
        List<String> names = java.util.Arrays.asList(q.meta.singers);
        if (Lrc.hasTimestamps(text)) {
            for (Lrc.Line l : Lrc.parse(text)) if (!l.text.isEmpty()) lines.add(Lrc.mark(l.singer) + l.text);
        } else {
            lines.addAll(Lrc.plainLines(text, names));
        }
        if (lines.isEmpty()) {
            message("Lyrics needed", "Paste or find the lyrics first, one sung line per line.");
            return;
        }
        Intent i = new Intent(this, TapSyncActivity.class);
        i.setData(q.uri);
        i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        i.putStringArrayListExtra(TapSyncActivity.EXTRA_LINES, lines);
        startActivityForResult(i, REQ_BATCH_TAP);
    }

    private void onBatchPressed() {
        if (busy || batch.isEmpty()) return;
        if (!hasStorageAccess()) {
            requestStorageAccess();
            return;
        }
        final List<Queued> songs = new ArrayList<>(batch);
        final GuitarCharter.Options options = new GuitarCharter.Options();
        int choice = densityGroup.getCheckedRadioButtonId();
        options.threshold = choice == DENSITY_FEWER ? 1.5 : choice == DENSITY_MORE ? 0.65 : 1.0;
        final boolean model = ModelStore.installed(this);
        final boolean guessWanted = guessCheck.isChecked();
        final boolean findLyrics = batchLyricsCheck.isChecked();

        setBusy(true);
        progress.setIndeterminate(false);
        progress.setProgress(0);
        progress.setVisibility(View.VISIBLE);
        log.setText("Converting " + songs.size() + " songs…");
        askForNotifications();
        ConversionService.begin(this, songs.size() + " songs");

        new Thread(new Runnable() {
            @Override
            public void run() {
                StringBuilder done = new StringBuilder();
                StringBuilder failed = new StringBuilder();
                final List<Queued> notDone = new ArrayList<>();
                int ok = 0;
                for (int i = 0; i < songs.size(); i++) {
                    Queued q = songs.get(i);
                    if (!q.read) readTags(q);
                    String name = q.meta.titleOrDefault();
                    ConversionService.setBatchLabel("Song " + (i + 1) + " of " + songs.size() + ": " + name);
                    try {
                        List<Lrc.Line> lyrics = new ArrayList<>();
                        String lrcText = "";
                        String lyricsNote = "no lyrics";
                        if (q.lrcText != null && !Lrc.hasTimestamps(q.lrcText)) {
                            // Words without times cannot be placed: the song gets no vocals
                            lyricsNote = "its lyrics have no times, so no vocals (tap them in with Edit > Tap to sync)";
                        } else if (q.lrcText != null) {
                            // Found, opened, tapped or marked already, with Edit or the Singers button
                            lrcText = q.lrcText;
                            lyrics = Lrc.parse(lrcText);
                            lyricsNote = q.singersMarked ? "with lyrics, singers marked" : "with lyrics";
                        } else if (findLyrics) {
                            ConversionService.setTextAndProgress("Looking for lyrics…", -1);
                            try {
                                LrcLib.Found found = LrcLib.find(q.meta.title, q.meta.artist, q.meta.album, q.seconds, q.fileName);
                                if (found != null && found.synced != null) {
                                    List<String> names = findSingers(q.meta.title, q.meta.artist, q.credit, found);
                                    if (names.size() >= 2) q.meta.singers = names.toArray(new String[0]);
                                    lrcText = markedLyrics(found, names);
                                    lyrics = Lrc.parse(lrcText);
                                    lyricsNote = hasMarks(lrcText) ? "with lyrics, singers from the lyrics"
                                            : names.size() < 2 ? "with lyrics"
                                            : q.mode != MODE_NORMAL && model ? "with lyrics, singers by their voices"
                                            : "with lyrics; " + names.get(0) + " and " + names.get(1) + " not told apart: that needs Split or R&B, or the Singers button";
                                } else {
                                    lyricsNote = "no timed lyrics found";
                                }
                            } catch (IOException e) {
                                lyricsNote = "lyrics lookup failed";
                            }
                        }
                        // Each song its own way: Normal, Split or R&B, as set in the list
                        boolean useStems = q.mode != MODE_NORMAL && model;
                        boolean rnb = useStems && q.mode == MODE_RNB;
                        boolean guessSingers = voicesDecide(useStems, guessWanted, q.meta.singers);
                        GuitarCharter.Options own = options.copy();
                        if (q.density != 0) {
                            own.threshold = q.density == DENSITY_FEWER ? 1.5 : q.density == DENSITY_MORE ? 0.65 : 1.0;
                        }
                        lastResult = null;
                        runConversion(q.uri, q.fileName, q.meta, lyrics, lrcText, own, useStems, guessSingers, rnb);
                        ok++;
                        // How the singers came out, now that the song has been listened to
                        SongWriter.Result res = lastResult;
                        if (res != null && res.lyricLines > 0 && q.meta.singers.length >= 2) {
                            if (res.vocals.twoParts()) {
                                lyricsNote += String.format(Locale.US, "; %s %d lines, %s %d%s", q.meta.singerName(0),
                                        res.vocals.partLines[VocalCharter.PART_FIRST], q.meta.singerName(1),
                                        res.vocals.partLines[VocalCharter.PART_SECOND],
                                        res.vocals.partLines[VocalCharter.PART_BOTH] > 0 ? ", both " + res.vocals.partLines[VocalCharter.PART_BOTH] : "");
                                if (res.singerGuess != null) lyricsNote += " (guessed from their voices: check it in the song's lyrics.lrc or convert it alone)";
                            } else if (res.singersGuessTried) {
                                lyricsNote += "; the two voices could not be told apart, so one part for everybody";
                            }
                        }
                        if (res != null && res.lyricsMoved) lyricsNote += "; lyrics re-timed to fit this audio";
                        done.append("  ").append(i + 1).append(". ").append(name).append(" (").append(modeName(useStemsNote(q, model)))
                                .append(", ").append(lyricsNote).append(")\n");
                    } catch (IOException e) {
                        notDone.add(q);
                        failed.append("  ").append(i + 1).append(". ").append(name).append(": ").append(e.getMessage()).append('\n');
                    } catch (RuntimeException | OutOfMemoryError e) {
                        notDone.add(q);
                        failed.append("  ").append(i + 1).append(". ").append(name).append(": ").append(e).append('\n');
                    }
                }
                StringBuilder sb = new StringBuilder();
                sb.append("Done: ").append(ok).append(" of ").append(songs.size()).append(" songs converted into\nInternal storage/YARG/songs/")
                        .append(SUBFOLDER).append("\n\n");
                if (done.length() > 0) sb.append("Converted:\n").append(done);
                if (failed.length() > 0) sb.append("\nNot converted:\n").append(failed);
                sb.append("\nTo play them: open YARG, then Settings > Songs > Scan Songs.");
                main.post(new Runnable() {
                    @Override
                    public void run() {
                        // Converted songs leave the list; any that failed stay, to try again
                        batch.removeAll(songs);
                        batch.addAll(0, notDone);
                        showBatch();
                    }
                });
                ConversionService.setBatchLabel("");
                ConversionService.finish(ok > 0, sb.toString());
            }
        }, "convert-batch").start();
    }

    private void askForNotifications() {
        if (Build.VERSION.SDK_INT >= 33
                && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[] {Manifest.permission.POST_NOTIFICATIONS}, REQ_NOTIFICATIONS);
        }
    }

    private void status(final String text) {
        ConversionService.setText(text);
    }

    private String runConversion(Uri uri, String name, SongMeta meta, List<Lrc.Line> lyrics, String lrcText, GuitarCharter.Options options,
                                 boolean useStems, boolean guessSingers, boolean rnb) throws IOException {
        // Everything below reads the app's own copy: Android can take back the right to read the
        // original at any moment (it did, with "Permission Denial", once the screen had been left)
        uri = Uri.fromFile(localCopy(uri));
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
        final File pcmFile = new File(app.getCacheDir(), "mix44k.pcm");
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
                features = AudioDecoder.decode(app, uri, copyAsIs ? null : new File(dir, audioFile), stereo, new AudioDecoder.Progress() {
                    @Override
                    public void onProgress(final float fraction) {
                        ConversionService.setProgress(fraction < 0 ? -1 : Math.round(fraction * 1000));
                    }
                });
            } finally {
                if (pcmOut != null) pcmOut.close();
            }
            if (features.duration < 5) throw new IOException("The audio is too short to chart.");

            if (useStems) {
                long started = System.currentTimeMillis();
                try {
                    stems = separate(pcmFile, pcmFrames[0], rnb, lyrics != null && !lyrics.isEmpty());
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
        if (stems != null) {
            stems.guessSingers = guessSingers;
            stems.rnb = rnb;
            String[] named = meta.singers;
            boolean two = named != null && named.length >= 2 && !blank(named[0]) && !blank(named[1])
                    && !named[0].startsWith("Singer ") && !named[1].startsWith("Singer ");
            stems.singerNames = two ? named : null;
        }
        // Lyrics timed for another cut of the song are lined up with this audio, unless their
        // times were set by hand ("Words earlier" / "Words later", or an [offset:] line)
        boolean timesSetByHand = Lrc.keepsTiming(lrcText);
        SongWriter.Result result = SongWriter.analyze(features, lyrics, options, stems, timesSetByHand);
        lastResult = result;
        if (result.singerGuess != null) {
            // Saved with the song and shown back in the app, where the guess can be corrected.
            lrcText = Lrc.format(result.lyricsOut);
            ConversionService.setMarkedLyrics(lrcText);
        } else if (result.lyricsMoved) {
            // Saved with the song as they were used
            lrcText = Lrc.format(result.lyricsOut);
        }
        if (result.noteCount(GuitarCharter.EXPERT) == 0) {
            throw new IOException("No notes could be found in this audio. Is it silent, or speech only?");
        }
        SongWriter.writeFolder(dir, meta, result, audioFile);

        if (copyAsIs) {
            status("Copying the audio…");
            try (InputStream in = app.getContentResolver().openInputStream(uri);
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
        if (result.rnbNote != null) sb.append(result.rnbNote).append("\n");
        if (result.instrumentalMeasures > 0) {
            sb.append(result.instrumentalMeasures).append(result.instrumentalMeasures == 1 ? " measure" : " measures")
                    .append(" where the instruments lead (intro, breaks) charted from the instruments\n");
        }
        if (result.deepChart != null || result.deepBassChart != null) {
            sb.append("Deep Music Mode charts included (turn on the Deep Music Mode modifier in YARG): guitar +")
                    .append(result.deepGuitarNotesAdded).append(" notes from the vocals and other instruments, bass +")
                    .append(result.drumNotesInBass).append(" drum hits on Expert\n");
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
        describeVocals(sb, result, meta, stems != null && stems.voice != null);
        if (wordTimingProblem != null) {
            sb.append("  Word timing could not run on this phone (").append(wordTimingProblem).append("), so the lyrics were timed from where the voice starts and stops.\n");
            wordTimingProblem = null;
        }
        if (!copyAsIs) sb.append("Audio saved as WAV so YARG can play it.\n");
        sb.append("\nTo play it in YARG:\n")
                .append("1. Open YARG (allow All files access if it asks).\n")
                .append("2. Settings > Songs > Scan Songs (or in the music library: More Options > Scan Songs).\n")
                .append("3. In the music library, Search for \"").append(meta.titleOrDefault())
                .append("\", or sort by Charter: this one is under \"").append(meta.charter).append("\".");
        if (result.lyricLines > 0) {
            sb.append("\n4. To sing: give a profile a microphone, then pick Vocals as its instrument")
                    .append(result.vocals.twoParts() ? " (or Harmonies, to choose one artist's lines)." : ".");
        }
        return sb.toString();
    }

    /** What the vocals part turned out like, in words. */
    private static void describeVocals(StringBuilder sb, SongWriter.Result result, SongMeta meta, boolean listened) {
        VocalCharter.Chart v = result.vocals;
        if (result.lyricLines == 0) {
            sb.append("Vocals: none (no lyrics)\n");
            return;
        }
        sb.append("Vocals: ").append(v.lines).append(" lines, ").append(v.sungLines).append(" sung (the note counts), ")
                .append(v.spokenLines).append(" rapped or spoken (any sound in time counts)\n");
        if (result.lyricTimingNote != null) {
            sb.append("  ").append(result.lyricTimingNote).append(" If the words now come too early or too late, use \"Words earlier\" or \"Words later\" under the lyrics and convert again.\n");
        }
        if (!listened) {
            sb.append("  The words were spread at speaking pace. Tick \"Split the song into instruments\" to place them where they are sung and get the real notes.\n");
        } else if (!v.heard) {
            sb.append("  The voice could not be heard where the lyrics say it is, so the words were spread at speaking pace. Check the lyric timings.\n");
        }
        if (v.twoParts()) {
            sb.append("  ").append(meta.singerName(0)).append(": ").append(v.partLines[VocalCharter.PART_FIRST]).append(" lines, ")
                    .append(meta.singerName(1)).append(": ").append(v.partLines[VocalCharter.PART_SECOND]).append(" lines");
            if (v.partLines[VocalCharter.PART_BOTH] > 0) sb.append(", both: ").append(v.partLines[VocalCharter.PART_BOTH]);
            sb.append(result.singerGuess != null ? " (guessed from their voices: check with \"Choose who sings what\" and convert again if it is wrong)\n" : "\n");
        } else if (result.singersGuessTried) {
            sb.append("  The two voices could not be told apart. Mark the lines yourself with \"Choose who sings what\".\n");
        }
    }

    /**
     * Runs the separation model over the parked audio and measures the guitar, the pitched
     * instruments together, the vocals, the drums, the bass and the voice as they come out. Only
     * the measurements are kept; the separated audio itself is not stored.
     */
    private SongWriter.Stems separate(File pcmFile, long frames, boolean rnb, boolean timeWords) throws Exception {
        // With word timing installed and lyrics to time, the voice is also kept at 16 kHz for it
        final boolean words = timeWords && ModelStore.installed(app, ModelStore.WORDS);
        final Resampler to16k = words ? new Resampler(StemSplitter.SAMPLE_RATE, WordAligner.SAMPLE_RATE) : null;
        final float[][] voice16k = {words ? new float[(int) Math.min(Integer.MAX_VALUE - 8, frames * WordAligner.SAMPLE_RATE / StemSplitter.SAMPLE_RATE + 4096)] : null};
        final int[] voice16kLength = {0};
        final float[] rsL = words ? new float[to16k.capacityFor(StemSplitter.SEGMENT)] : null;
        final float[] rsR = words ? new float[to16k.capacityFor(StemSplitter.SEGMENT)] : null;
        final AudioAnalyzer guitar = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer melodic = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        // The vocals measured like an instrument: R&B mode charts the guitar from them, and songs
        // the voice carries chart guitar from them too
        final AudioAnalyzer vocals = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer drums = new AudioAnalyzer(StemSplitter.SAMPLE_RATE);
        final AudioAnalyzer bass = new AudioAnalyzer(StemSplitter.SAMPLE_RATE, true);
        final VocalAnalyzer voice = new VocalAnalyzer(StemSplitter.SAMPLE_RATE);
        final float[] vox = new float[StemSplitter.SEGMENT];
        final double[] energy = new double[6];
        final float[] g = new float[StemSplitter.SEGMENT];
        final float[] m = new float[StemSplitter.SEGMENT];
        final float[] v = new float[StemSplitter.SEGMENT];
        final float[] dr = new float[StemSplitter.SEGMENT];
        final float[] b = new float[StemSplitter.SEGMENT];
        int threads = modelThreads();
        // The four or five listeners each take a separated instrument; they run side by side on
        // cores the model is not using at that moment, instead of one after another
        final java.util.concurrent.ExecutorService listeners = java.util.concurrent.Executors.newFixedThreadPool(5);
        final long started = System.currentTimeMillis();
        ConversionService.setTextAndProgress("Loading the separation model…", 0);

        try (OrtStemModel model = new OrtStemModel(ModelStore.file(app).getAbsolutePath(), threads);
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
                    float[][] vc = stems[StemSplitter.VOCALS], ds = stems[StemSplitter.DRUMS];
                    for (int i = 0; i < count; i++) {
                        vox[i] = (vc[0][i] + vc[1][i]) * 0.5f;
                        float gv = (gt[0][i] + gt[1][i]) * 0.5f;
                        float mv = gv + (ot[0][i] + ot[1][i] + pn[0][i] + pn[1][i]) * 0.5f;
                        float vv = vox[i];
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
                    final int n = count;
                    List<java.util.concurrent.Callable<Void>> jobs = new ArrayList<>();
                    jobs.add(listen(new Runnable() { @Override public void run() { guitar.feed(g, 0, n); } }));
                    jobs.add(listen(new Runnable() { @Override public void run() { melodic.feed(m, 0, n); } }));
                    jobs.add(listen(new Runnable() { @Override public void run() { bass.feed(b, 0, n); } }));
                    jobs.add(listen(new Runnable() { @Override public void run() { drums.feed(dr, 0, n); } }));
                    jobs.add(listen(new Runnable() { @Override public void run() {
                        voice.feed(vox, 0, n);
                        vocals.feed(vox, 0, n);
                        if (to16k != null) {
                            int made = to16k.process(vox, vox, n, rsL, rsR);
                            if (voice16kLength[0] + made > voice16k[0].length) {
                                voice16k[0] = java.util.Arrays.copyOf(voice16k[0], voice16kLength[0] + made + 16000);
                            }
                            System.arraycopy(rsL, 0, voice16k[0], voice16kLength[0], made);
                            voice16kLength[0] += made;
                        }
                    } }));
                    try {
                        // Waits for all of them, and passes on anything one of them threw
                        for (java.util.concurrent.Future<Void> done : listeners.invokeAll(jobs)) done.get();
                    } catch (InterruptedException e) {
                        Thread.currentThread().interrupt();
                        throw new IllegalStateException("Interrupted while listening to the instruments");
                    } catch (java.util.concurrent.ExecutionException e) {
                        throw new IllegalStateException(e.getCause());
                    }
                }
            };
            StemSplitter.Progress report = new StemSplitter.Progress() {
                @Override
                public boolean onSegment(final int done, final int total) {
                    long elapsed = System.currentTimeMillis() - started;
                    long left = done > 0 ? elapsed * (total - done) / done : 0;
                    final String text = String.format(Locale.US, "Splitting into instruments: part %d of %d%s. You can leave the app; it keeps going.",
                            done, total, done < total ? String.format(Locale.US, " (about %d min %02d s left)", left / 60000, (left / 1000) % 60) : "");
                    ConversionService.setTextAndProgress(text, done * 1000 / total);
                    return true;
                }
            };
            status("Splitting into instruments… You can leave the app; it keeps going.");
            StemSplitter.split(source, frames, model, sink, report);
        } finally {
            listeners.shutdown();
        }
        SongWriter.Stems out = new SongWriter.Stems();
        out.guitar = guitar.finish();
        out.melodic = melodic.finish();
        out.vocals = vocals.finish();
        out.drums = drums.finish();
        out.bass = bass.finish();
        out.voice = voice.finish();
        out.guitarEnergy = energy[0];
        out.melodicEnergy = energy[1];
        out.bassEnergy = energy[2];
        out.mixEnergy = energy[3];
        out.vocalsEnergy = energy[4];
        out.drumsEnergy = energy[5];
        if (words && voice16kLength[0] > 0) {
            // The separation model is closed by now, so its memory is free for this one
            ConversionService.setTextAndProgress("Timing the lyrics word by word…", 0);
            status("Timing the lyrics word by word… You can leave the app; it keeps going.");
            try (OrtWordModel model = new OrtWordModel(ModelStore.file(app, ModelStore.WORDS).getAbsolutePath(), modelThreads())) {
                out.letters = model.letters(voice16k[0], voice16kLength[0], new OrtWordModel.Progress() {
                    @Override
                    public void onProgress(int done, int total) {
                        ConversionService.setTextAndProgress(String.format(Locale.US, "Timing the lyrics word by word: part %d of %d", done, total), done * 1000 / total);
                    }
                });
            } catch (Exception | LinkageError | OutOfMemoryError e) {
                // Not enough memory on this phone, or the model would not load: lyrics are then
                // timed from the voice's entries alone, as without the download
                out.letters = null;
                wordTimingProblem = e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage();
            }
            voice16k[0] = null;
        }
        return out;
    }

    /** Why word timing could not be used on the last song, or null. */
    private volatile String wordTimingProblem;

    private static java.util.concurrent.Callable<Void> listen(final Runnable work) {
        return new java.util.concurrent.Callable<Void>() {
            @Override
            public Void call() {
                work.run();
                return null;
            }
        };
    }

    /**
     * How many threads the separation model gets: one for each of the phone's fast cores. Phones
     * mix fast and slow cores, and a model spread over both runs at the pace of the slow ones, so
     * the slow ones are left out. A core counts as fast when its top speed is at least 70% of the
     * fastest core's. When the phone will not say, the old rule is used (all but two, at most 6).
     */
    static int modelThreads() {
        int cores = Runtime.getRuntime().availableProcessors();
        int fallback = Math.max(2, Math.min(6, cores - 2));
        try {
            long[] top = new long[cores];
            long best = 0;
            for (int i = 0; i < cores; i++) {
                File f = new File("/sys/devices/system/cpu/cpu" + i + "/cpufreq/cpuinfo_max_freq");
                try (java.io.BufferedReader r = new java.io.BufferedReader(new java.io.FileReader(f))) {
                    top[i] = Long.parseLong(r.readLine().trim());
                }
                best = Math.max(best, top[i]);
            }
            int fast = 0;
            for (long t : top) if (t >= 0.7 * best) fast++;
            // Leave one fast core for the rest of the phone when all of them are fast
            if (fast == cores) fast = cores - 1;
            return Math.max(2, Math.min(8, fast));
        } catch (IOException | RuntimeException e) {
            return fallback;
        }
    }

    /** Saves the picture embedded in the audio file, if any, so YARG shows it as album art. */
    private void saveAlbumArt(Uri uri, File dir) {
        MediaMetadataRetriever r = new MediaMetadataRetriever();
        try {
            r.setDataSource(app, uri);
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
