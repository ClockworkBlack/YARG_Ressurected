package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Reads and writes timed lyrics in the common .lrc format ("[01:23.45]words"). */
public final class Lrc {
    private Lrc() {}

    public static final class Line {
        public final double time;
        public final String text;

        public Line(double time, String text) {
            this.time = time;
            this.text = text;
        }
    }

    private static final Pattern STAMP = Pattern.compile("\\[(\\d{1,3}):(\\d{1,2})(?:[.:](\\d{1,3}))?\\]");
    private static final Pattern OFFSET = Pattern.compile("\\[offset:\\s*([+-]?\\d+)\\s*\\]", Pattern.CASE_INSENSITIVE);
    private static final Pattern WORD_STAMP = Pattern.compile("<\\d{1,3}:\\d{1,2}(?:[.:]\\d{1,3})?>");

    /** True when the text carries at least one "[mm:ss]" time stamp. */
    public static boolean hasTimestamps(String text) {
        return text != null && STAMP.matcher(text).find();
    }

    /** Parses .lrc text. Lines without a time stamp are skipped; blank timed lines are kept as gaps. */
    public static List<Line> parse(String text) {
        List<Line> out = new ArrayList<>();
        if (text == null) return out;
        double offset = 0;
        Matcher om = OFFSET.matcher(text);
        if (om.find()) {
            try {
                // Per the .lrc convention a positive offset makes lyrics appear earlier.
                offset = -Integer.parseInt(om.group(1)) / 1000.0;
            } catch (NumberFormatException ignored) {
                offset = 0;
            }
        }
        for (String raw : text.split("\\r?\\n")) {
            Matcher m = STAMP.matcher(raw);
            List<Double> times = new ArrayList<>();
            int end = 0;
            while (m.find() && m.start() == end) {
                double t = Integer.parseInt(m.group(1)) * 60.0 + Integer.parseInt(m.group(2));
                String frac = m.group(3);
                if (frac != null) t += Integer.parseInt(frac) / Math.pow(10, frac.length());
                times.add(t);
                end = m.end();
            }
            if (times.isEmpty()) continue;
            String words = WORD_STAMP.matcher(raw.substring(end)).replaceAll(" ").replaceAll("\\s+", " ").trim();
            for (double t : times) out.add(new Line(Math.max(0, t + offset), words));
        }
        Collections.sort(out, new Comparator<Line>() {
            @Override
            public int compare(Line a, Line b) {
                return Double.compare(a.time, b.time);
            }
        });
        return out;
    }

    /** Splits pasted lyrics into the lines that will be shown, dropping blanks and "[Chorus]" style headers. */
    public static List<String> plainLines(String text) {
        List<String> out = new ArrayList<>();
        if (text == null) return out;
        for (String raw : text.split("\\r?\\n")) {
            String s = raw.replaceAll("\\s+", " ").trim();
            if (s.isEmpty()) continue;
            if (s.matches("^[\\[(].*[\\])]$") && s.length() < 40) continue;
            out.add(s);
        }
        return out;
    }

    public static String format(List<Line> lines) {
        StringBuilder sb = new StringBuilder();
        for (Line l : lines) {
            sb.append(stamp(l.time)).append(l.text).append('\n');
        }
        return sb.toString();
    }

    public static String stamp(double seconds) {
        int cs = (int) Math.round(Math.max(0, seconds) * 100);
        return String.format(Locale.US, "[%02d:%02d.%02d]", cs / 6000, (cs / 100) % 60, cs % 100);
    }
}
