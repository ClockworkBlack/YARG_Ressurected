package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Reads and writes timed lyrics in the common .lrc format ("[01:23.45]words").
 *
 * A line can also say who sings it, for songs with two artists: "[01:23.45]v1: words" is the
 * first artist, "v2:" the second and "vb:" both together. Lines without a mark belong to the
 * first artist.
 */
public final class Lrc {
    private Lrc() {}

    /** Who sings a line. */
    public static final int UNMARKED = 0, FIRST = 1, SECOND = 2, BOTH = 3;

    public static final class Line {
        public final double time;
        public final String text;
        /** {@link #UNMARKED}, {@link #FIRST}, {@link #SECOND} or {@link #BOTH}. */
        public final int singer;

        public Line(double time, String text) {
            this(time, text, UNMARKED);
        }

        public Line(double time, String text, int singer) {
            this.time = time;
            this.text = text;
            this.singer = singer;
        }
    }

    private static final Pattern STAMP = Pattern.compile("\\[(\\d{1,3}):(\\d{1,2})(?:[.:](\\d{1,3}))?\\]");
    private static final Pattern OFFSET = Pattern.compile("\\[offset:\\s*([+-]?\\d+)\\s*\\]", Pattern.CASE_INSENSITIVE);
    private static final Pattern WORD_STAMP = Pattern.compile("<\\d{1,3}:\\d{1,2}(?:[.:]\\d{1,3})?>");
    /** "v1:", "v2:", "vb:" (ours), and the older karaoke marks "M:", "F:", "D:" (male, female, duet). */
    private static final Pattern MARK = Pattern.compile("^(?:([vV])\\s?([12bB]|12)|([MFD]))\\s?:\\s*");

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
            int[] singer = new int[1];
            words = stripMark(words, singer);
            for (double t : times) out.add(new Line(Math.max(0, t + offset), words, singer[0]));
        }
        Collections.sort(out, new Comparator<Line>() {
            @Override
            public int compare(Line a, Line b) {
                return Double.compare(a.time, b.time);
            }
        });
        return out;
    }

    /** Takes a "v1:" style mark off the front of a line. {@code singer[0]} receives who it names. */
    public static String stripMark(String words, int[] singer) {
        singer[0] = UNMARKED;
        if (words == null) return "";
        Matcher m = MARK.matcher(words);
        if (!m.find()) return words;
        String which = m.group(2) != null ? m.group(2).toLowerCase(Locale.US) : m.group(3);
        if (which.equals("1") || which.equals("M")) singer[0] = FIRST;
        else if (which.equals("2") || which.equals("F")) singer[0] = SECOND;
        else singer[0] = BOTH;
        return words.substring(m.end()).trim();
    }

    /** The mark written in front of a line for a singer ("" for unmarked). */
    public static String mark(int singer) {
        return singer == FIRST ? "v1: " : singer == SECOND ? "v2: " : singer == BOTH ? "vb: " : "";
    }

    /** Splits pasted lyrics into the lines that will be shown, dropping blanks and "[Chorus]" style headers. */
    public static List<String> plainLines(String text) {
        return plainLines(text, null);
    }

    /**
     * As above, and when the names of the song's artists are given, a header that names one
     * ("[Verse 2: Travis Scott]", as lyric sites write them) marks the lines under it as theirs.
     */
    public static List<String> plainLines(String text, List<String> artists) {
        List<String> out = new ArrayList<>();
        if (text == null) return out;
        int current = UNMARKED;
        for (String raw : text.split("\\r?\\n")) {
            String s = raw.replaceAll("\\s+", " ").trim();
            if (s.isEmpty()) continue;
            if (s.matches("^[\\[(].*[\\])]$") && s.length() < 80 && (s.length() < 40 || s.indexOf(':') > 0)) {
                if (artists != null && s.startsWith("[")) current = singerNamed(s, artists);
                continue;
            }
            int[] own = new int[1];
            String words = stripMark(s, own);
            if (words.isEmpty()) continue;
            int singer = own[0] != UNMARKED ? own[0] : current;
            out.add(mark(singer) + words);
        }
        return out;
    }

    /** Which of the two artists a section header names: one, the other, both, or neither. */
    static int singerNamed(String header, List<String> artists) {
        String h = header.toLowerCase(Locale.US);
        int colon = h.indexOf(':');
        if (colon < 0) return UNMARKED;
        h = h.substring(colon + 1);
        boolean first = artists.size() > 0 && names(h, artists.get(0));
        boolean second = artists.size() > 1 && names(h, artists.get(1));
        return first && second ? BOTH : first ? FIRST : second ? SECOND : UNMARKED;
    }

    private static boolean names(String haystack, String artist) {
        String a = artist == null ? "" : artist.trim().toLowerCase(Locale.US);
        return a.length() >= 2 && haystack.contains(a);
    }

    /**
     * The singers of a song, from how its artist is written: "Don Toliver feat. Travis Scott",
     * "A & B", "A, B", "A x B", and a "(feat. B)" in the title. At most two are returned, the
     * main artist first; one name (or none) means there is nobody to split the lines between.
     */
    public static List<String> artists(String artist, String title) {
        List<String> out = new ArrayList<>();
        String all = artist == null ? "" : artist;
        if (title != null) {
            Matcher m = Pattern.compile("[\\[(]\\s*(?:feat\\.?|ft\\.?|featuring|with)\\s+([^\\])]+)[\\])]", Pattern.CASE_INSENSITIVE).matcher(title);
            if (m.find()) all = all + " feat. " + m.group(1);
        }
        String[] parts = all.split("(?i)\\s*(?:,|&|;|/|\\+|\\bfeat\\.?(?=\\s)|\\bft\\.?(?=\\s)|\\bfeaturing\\b|\\bwith\\b|\\bvs\\.?(?=\\s)|\\sx\\s|\\sand\\s)\\s*");
        for (String p : parts) {
            String name = p.replaceAll("[()\\[\\]]", " ").replaceAll("\\s+", " ").trim();
            if (name.isEmpty()) continue;
            boolean seen = false;
            for (String o : out) if (o.equalsIgnoreCase(name)) seen = true;
            if (!seen) out.add(name);
            if (out.size() == 2) break;
        }
        return out;
    }

    public static String format(List<Line> lines) {
        StringBuilder sb = new StringBuilder();
        for (Line l : lines) {
            sb.append(stamp(l.time)).append(l.text.isEmpty() ? "" : mark(l.singer)).append(l.text).append('\n');
        }
        return sb.toString();
    }

    public static String stamp(double seconds) {
        int cs = (int) Math.round(Math.max(0, seconds) * 100);
        return String.format(Locale.US, "[%02d:%02d.%02d]", cs / 6000, (cs / 100) % 60, cs % 100);
    }
}
