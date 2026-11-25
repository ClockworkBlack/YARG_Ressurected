package com.clockworkblack.yargconverter;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.clockworkblack.yargconverter.core.Lrc;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * "Who sings what": for a song with two artists, shows every lyric line and lets each one be
 * given to the first artist, the second, or both. In the game each player then picks an artist
 * and sings only that artist's lines.
 *
 * The choice is kept in the lyrics themselves as a mark in front of the words ("v1:", "v2:",
 * "vb:"), so it survives being saved, synced by tapping, and converted again.
 */
public final class SingersActivity extends Activity {
    static final String EXTRA_TEXT = "text";
    static final String EXTRA_NAMES = "names";

    private static final int FIRST_COLOR = 0xFF00A8D8, SECOND_COLOR = 0xFFE87800, BOTH_COLOR = 0xFF8A8A8A;
    private static final Pattern STAMPS = Pattern.compile("^((?:\\s*\\[\\d{1,3}:\\d{1,2}(?:[.:]\\d{1,3})?\\])+)(.*)$");

    /** One line of the lyrics text. Lines that are not sung (blank, headers, tags) are kept as they are. */
    private static final class Row {
        String raw;
        String prefix = "";
        String words = "";
        int singer = Lrc.FIRST;
        boolean sung;
        TextView tag, text;
    }

    private final List<Row> rows = new ArrayList<>();
    /** Headers in the pasted lyrics name a singer, so every line has to say whose it is. */
    private boolean headersNameSingers;
    private EditText firstName, secondName;
    private TextView counts;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        String text = getIntent().getStringExtra(EXTRA_TEXT);
        String[] names = getIntent().getStringArrayExtra(EXTRA_NAMES);
        if (text == null) {
            finish();
            return;
        }
        if (names == null) names = new String[0];
        parse(text, Arrays.asList(names));
        buildUi(names);
        refresh();
    }

    private void parse(String text, List<String> names) {
        boolean timed = Lrc.hasTimestamps(text);
        int current = Lrc.UNMARKED;
        for (String raw : text.split("\\r?\\n", -1)) {
            Row row = new Row();
            row.raw = raw;
            rows.add(row);
            String rest = raw.trim();
            if (timed) {
                Matcher m = STAMPS.matcher(raw);
                if (!m.find()) continue;
                row.prefix = m.group(1).trim();
                rest = m.group(2).trim();
            } else if (rest.matches("^[\\[(].*[\\])]$") && rest.length() < 80 && (rest.length() < 40 || rest.indexOf(':') > 0)) {
                // A header such as "[Verse 2: Travis Scott]" says who sings the lines under it.
                if (rest.startsWith("[")) {
                    current = headerSinger(rest, names);
                    headersNameSingers |= current != Lrc.UNMARKED;
                }
                continue;
            }
            int[] singer = new int[1];
            String words = Lrc.stripMark(rest, singer);
            if (words.isEmpty()) continue;
            row.sung = true;
            row.words = words;
            row.singer = singer[0] != Lrc.UNMARKED ? singer[0] : current != Lrc.UNMARKED ? current : Lrc.FIRST;
        }
    }

    private static int headerSinger(String header, List<String> names) {
        // Reuses the reading of headers that pasted lyrics get: one line under the header tells us.
        List<String> marked = Lrc.plainLines(header + "\nx", names);
        if (marked.isEmpty()) return Lrc.UNMARKED;
        int[] singer = new int[1];
        Lrc.stripMark(marked.get(0), singer);
        return singer[0];
    }

    private int dp(float v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    private void buildUi(String[] names) {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(16), dp(16), dp(16), dp(12));
        root.setFitsSystemWindows(true);

        TextView heading = new TextView(this);
        heading.setText("Who sings what");
        heading.setTextSize(22);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        root.addView(heading);

        TextView help = new TextView(this);
        help.setText("Tap a line to pass it to the other artist (tap again for both). Hold a line to give it and every line under it to the same artist. In the game, each player picks an artist and sings only their lines.");
        help.setPadding(0, dp(4), 0, dp(8));
        root.addView(help);

        LinearLayout nameRow = new LinearLayout(this);
        nameRow.setOrientation(LinearLayout.HORIZONTAL);
        firstName = nameBox(names.length > 0 ? names[0] : "", "First artist", FIRST_COLOR);
        secondName = nameBox(names.length > 1 ? names[1] : "", "Second artist", SECOND_COLOR);
        nameRow.addView(firstName, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        nameRow.addView(secondName, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        root.addView(nameRow);

        LinearLayout tools = new LinearLayout(this);
        tools.setOrientation(LinearLayout.HORIZONTAL);
        tools.addView(small("Swap the two", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                for (Row r : rows) {
                    if (!r.sung) continue;
                    if (r.singer == Lrc.FIRST) r.singer = Lrc.SECOND;
                    else if (r.singer == Lrc.SECOND) r.singer = Lrc.FIRST;
                }
                refresh();
            }
        }), new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        tools.addView(small("All to the first", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                for (Row r : rows) if (r.sung) r.singer = Lrc.FIRST;
                refresh();
            }
        }), new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        root.addView(tools);

        counts = new TextView(this);
        counts.setPadding(0, dp(4), 0, dp(6));
        root.addView(counts);

        ScrollView scroll = new ScrollView(this);
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        scroll.addView(list, new ScrollView.LayoutParams(ScrollView.LayoutParams.MATCH_PARENT, ScrollView.LayoutParams.WRAP_CONTENT));
        for (int i = 0; i < rows.size(); i++) {
            final Row row = rows.get(i);
            if (!row.sung) continue;
            final int index = i;
            LinearLayout line = new LinearLayout(this);
            line.setOrientation(LinearLayout.HORIZONTAL);
            line.setGravity(Gravity.CENTER_VERTICAL);
            line.setPadding(0, dp(5), 0, dp(5));
            line.setMinimumHeight(dp(46));
            row.tag = new TextView(this);
            row.tag.setTextColor(0xFFFFFFFF);
            row.tag.setTypeface(Typeface.DEFAULT_BOLD);
            row.tag.setGravity(Gravity.CENTER);
            row.tag.setTextSize(13);
            row.tag.setPadding(dp(6), dp(4), dp(6), dp(4));
            line.addView(row.tag, new LinearLayout.LayoutParams(dp(54), LinearLayout.LayoutParams.WRAP_CONTENT));
            row.text = new TextView(this);
            row.text.setText(row.words);
            row.text.setTextSize(16);
            row.text.setPadding(dp(10), 0, 0, 0);
            line.addView(row.text, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
            line.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    row.singer = row.singer == Lrc.FIRST ? Lrc.SECOND : row.singer == Lrc.SECOND ? Lrc.BOTH : Lrc.FIRST;
                    refresh();
                }
            });
            line.setOnLongClickListener(new View.OnLongClickListener() {
                @Override
                public boolean onLongClick(View v) {
                    for (int k = index; k < rows.size(); k++) if (rows.get(k).sung) rows.get(k).singer = row.singer;
                    refresh();
                    return true;
                }
            });
            list.addView(line);
        }
        root.addView(scroll, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f));

        Button done = small("Done", new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                finishWithResult();
            }
        });
        root.addView(done);
        setContentView(root);
    }

    private EditText nameBox(String value, String hint, int color) {
        EditText e = new EditText(this);
        e.setText(value);
        e.setHint(hint);
        e.setSingleLine(true);
        e.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_WORDS);
        e.setTextColor(color);
        return e;
    }

    private Button small(String text, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setOnClickListener(l);
        return b;
    }

    private void refresh() {
        int first = 0, second = 0, both = 0;
        for (Row r : rows) {
            if (!r.sung) continue;
            int color = r.singer == Lrc.SECOND ? SECOND_COLOR : r.singer == Lrc.BOTH ? BOTH_COLOR : FIRST_COLOR;
            if (r.singer == Lrc.SECOND) second++;
            else if (r.singer == Lrc.BOTH) both++;
            else first++;
            if (r.tag == null) continue;
            r.tag.setText(r.singer == Lrc.SECOND ? "2" : r.singer == Lrc.BOTH ? "1 + 2" : "1");
            GradientDrawable bg = new GradientDrawable();
            bg.setColor(color);
            bg.setCornerRadius(dp(6));
            r.tag.setBackground(bg);
        }
        if (counts != null) {
            counts.setText("First artist: " + first + " lines.  Second artist: " + second + " lines.  Both: " + both + "."
                    + (second == 0 ? "\nWith no lines for a second artist, the song is one part for everybody." : ""));
        }
    }

    private void finishWithResult() {
        boolean two = headersNameSingers;
        for (Row r : rows) two |= r.sung && r.singer != Lrc.FIRST;
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < rows.size(); i++) {
            Row r = rows.get(i);
            if (i > 0) sb.append('\n');
            // A song left entirely to the first artist carries no marks at all.
            if (!r.sung) sb.append(r.raw);
            else sb.append(r.prefix).append(two ? Lrc.mark(r.singer) : "").append(r.words);
        }
        Intent data = new Intent();
        data.putExtra(EXTRA_TEXT, sb.toString());
        data.putExtra(EXTRA_NAMES, new String[] {firstName.getText().toString().trim(), secondName.getText().toString().trim()});
        setResult(RESULT_OK, data);
        finish();
    }
}
