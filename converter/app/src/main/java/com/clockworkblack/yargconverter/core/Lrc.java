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
            // Letters written as a base plus a separate accent, and Korean written as loose
            // parts, are put together: the game draws each character on its own
            words = java.text.Normalizer.normalize(words, java.text.Normalizer.Form.NFC);
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
        if (artist == null) return false;
        // A part that stands for several guests ("B & C") is named when any one of them is
        for (String one : artist.split("\\s+&\\s+")) {
            String a = one.trim().toLowerCase(Locale.US);
            if (a.length() >= 2 && haystack.contains(a)) return true;
        }
        return false;
    }

    /**
     * The singers of a song, from how its artist is written: "Don Toliver feat. Travis Scott",
     * "A & B", "A, B", "A x B", and a "(feat. B)" in the title. At most two are returned, the
     * main artist first; one name (or none) means there is nobody to split the lines between.
     */
    public static List<String> artists(String artist, String title) {
        List<String> all = allArtists(artist, title);
        if (all.size() <= 2) return all;
        // The game has two parts to give out: the main artist, and the guests together
        List<String> out = new ArrayList<>();
        out.add(all.get(0));
        StringBuilder guests = new StringBuilder();
        for (int i = 1; i < all.size(); i++) guests.append(i > 1 ? " & " : "").append(all.get(i));
        out.add(guests.toString());
        return out;
    }

    /** Every artist named, however many, the main artist first. */
    public static List<String> allArtists(String artist, String title) {
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
        }
        return out;
    }

    /**
     * Names found after the colon in section headers of plain lyrics, as lyric sites write them:
     * "[Verse 2: Travis Scott]", "[Chorus: Don Toliver & Travis Scott]". In order of first
     * appearance, without repeats.
     */
    public static List<String> headerSingers(String plain) {
        List<String> out = new ArrayList<>();
        if (plain == null) return out;
        for (String raw : plain.split("\\r?\\n")) {
            String s = raw.trim();
            if (!s.startsWith("[") || !s.endsWith("]") || s.length() > 80) continue;
            int colon = s.indexOf(':');
            if (colon < 0) continue;
            for (String name : allArtists(s.substring(colon + 1, s.length() - 1), null)) {
                boolean seen = false;
                for (String o : out) if (o.equalsIgnoreCase(name)) seen = true;
                if (!seen && name.length() >= 2 && name.length() <= 40) out.add(name);
            }
        }
        return out;
    }

    /**
     * The two singers of a song from everything that names them: how the artist is written (the
     * file's tags, the lyric service's entry, the encyclopedia's credit) and, failing that, the
     * names in the lyrics' own section headers. The first of {@code artistSpellings} that names
     * two wins; otherwise the main artist plus the first other name in the headers.
     */
    public static List<String> findSingers(String title, String plainLyrics, String... artistSpellings) {
        List<String> main = new ArrayList<>();
        for (String spelling : artistSpellings) {
            List<String> found = artists(spelling, title);
            if (found.size() >= 2) return found;
            if (main.isEmpty()) main = found;
        }
        List<String> out = new ArrayList<>(main);
        if (out.size() >= 2) return out;
        StringBuilder guests = new StringBuilder();
        for (String name : headerSingers(plainLyrics)) {
            boolean seen = false;
            for (String o : out) {
                String a = o.toLowerCase(Locale.US), b = name.toLowerCase(Locale.US);
                if (a.equals(b) || a.contains(b) || b.contains(a)) seen = true;
            }
            if (out.isEmpty()) out.add(name);
            else if (!seen) guests.append(guests.length() > 0 ? " & " : "").append(name);
        }
        if (guests.length() > 0) out.add(guests.toString());
        return out;
    }

    private static String key(String words) {
        return words.toLowerCase(Locale.US).replaceAll("[^\\p{L}\\p{Nd}]+", "");
    }

    /**
     * Carries the singers named in plain lyrics' section headers over to timed lyrics of the same
     * song. Timed lyrics hardly ever say who sings; the plain text of the same entry often does
     * ("[Verse 2: Travis Scott]"). Lines are matched by their words, in order, so a chorus that
     * comes round again is matched to the right time round.
     *
     * @return the timed lyrics with marks, or null when the plain lyrics name nobody (or too few
     *         lines could be matched to trust it)
     */
    public static String markFromHeaders(String synced, String plain, List<String> artists) {
        if (synced == null || plain == null || artists == null || artists.size() < 2) return null;
        List<String> marked = plainLines(plain, artists);
        List<String> keys = new ArrayList<>();
        List<Integer> who = new ArrayList<>();
        boolean any = false;
        for (String l : marked) {
            int[] singer = new int[1];
            String words = stripMark(l, singer);
            keys.add(key(words));
            who.add(singer[0]);
            any |= singer[0] != UNMARKED;
        }
        if (!any) return null;

        List<Line> lines = parse(synced);
        List<Line> out = new ArrayList<>();
        int at = 0, matched = 0, withWords = 0, firsts = 0, seconds = 0;
        for (Line l : lines) {
            if (l.text.isEmpty() || l.singer != UNMARKED) {
                out.add(l);
                continue;
            }
            withWords++;
            String k = key(l.text);
            int found = -1;
            // The same line a little further on: lyric files differ by a dropped or doubled line
            for (int j = at; j < Math.min(keys.size(), at + 6) && found < 0; j++) {
                if (!k.isEmpty() && (keys.get(j).equals(k) || keys.get(j).startsWith(k) || k.startsWith(keys.get(j)) && keys.get(j).length() >= 6)) found = j;
            }
            if (found < 0) {
                out.add(l);
                continue;
            }
            at = found + 1;
            matched++;
            int singer = who.get(found);
            if (singer == FIRST) firsts++;
            if (singer == SECOND) seconds++;
            out.add(new Line(l.time, l.text, singer));
        }
        // Only worth using when the two texts really are the same song, and both singers appear
        if (withWords == 0 || matched < 0.6 * withWords || seconds == 0 || firsts + seconds < 2) return null;
        // Lines under a header that names nobody ("[Chorus]") belong to the main artist
        List<Line> filled = new ArrayList<>();
        for (Line l : out) filled.add(l.text.isEmpty() || l.singer != UNMARKED ? l : new Line(l.time, l.text, FIRST));
        return format(filled);
    }

    public static String format(List<Line> lines) {
        StringBuilder sb = new StringBuilder();
        for (Line l : lines) {
            sb.append(stamp(l.time)).append(l.text.isEmpty() ? "" : mark(l.singer)).append(l.text).append('\n');
        }
        return sb.toString();
    }

    /** A line of its own in lyrics text saying the times were set by hand and must be left alone. */
    public static final String KEEP_TIMES = "[timing:manual]";

    /**
     * True when the lyrics' times were set on purpose (moved by hand in the app, or carrying an
     * "[offset:]" line), so the converter must not line them up with the voice by itself.
     */
    public static boolean keepsTiming(String text) {
        if (text == null) return false;
        return text.toLowerCase(Locale.US).contains(KEEP_TIMES) || OFFSET.matcher(text).find();
    }

    /**
     * The same timed lyrics with every line moved by {@code seconds} (more than 0 is later), marked
     * so the converter keeps these times. Text without time stamps is returned as it is.
     */
    public static String shiftText(String text, double seconds) {
        List<Line> lines = parse(text);
        if (lines.isEmpty()) return text;
        List<Line> moved = new ArrayList<>(lines.size());
        for (Line l : lines) moved.add(new Line(Math.max(0, l.time + seconds), l.text, l.singer));
        return KEEP_TIMES + "\n" + format(moved);
    }

    /**
     * Reads the bytes of a lyrics file as text. Most are UTF-8, but Korean, Japanese, Chinese and
     * Russian files are often in the older encodings of their countries, or in UTF-16; read as
     * UTF-8 those turn into boxes and question marks.
     */
    public static String decode(byte[] bytes) {
        if (bytes == null || bytes.length == 0) return "";
        int n = bytes.length;
        if (n >= 3 && (bytes[0] & 0xFF) == 0xEF && (bytes[1] & 0xFF) == 0xBB && (bytes[2] & 0xFF) == 0xBF) {
            return new String(bytes, 3, n - 3, java.nio.charset.StandardCharsets.UTF_8);
        }
        if (n >= 2 && (bytes[0] & 0xFF) == 0xFF && (bytes[1] & 0xFF) == 0xFE) {
            return new String(bytes, 2, n - 2, java.nio.charset.StandardCharsets.UTF_16LE);
        }
        if (n >= 2 && (bytes[0] & 0xFF) == 0xFE && (bytes[1] & 0xFF) == 0xFF) {
            return new String(bytes, 2, n - 2, java.nio.charset.StandardCharsets.UTF_16BE);
        }
        String utf8 = strict(bytes, "UTF-8");
        if (utf8 != null) return utf8;

        // Not UTF-8: the encoding whose reading has the most letters of the script it is for
        String[] names = {"EUC-KR", "Shift_JIS", "GB18030", "Big5", "windows-1251"};
        String best = null;
        double bestScore = 0.6;
        for (String name : names) {
            String text = strict(bytes, name);
            if (text == null) continue;
            int other = 0, home = 0, plain = 0;
            for (int i = 0; i < text.length(); i++) {
                char c = text.charAt(i);
                if (c < 0x80) {
                    if (Character.isLetter(c)) plain++;
                    continue;
                }
                other++;
                Character.UnicodeScript script = Character.UnicodeScript.of(c);
                boolean fits;
                switch (name) {
                    case "EUC-KR": fits = script == Character.UnicodeScript.HANGUL; break;
                    case "Shift_JIS": fits = script == Character.UnicodeScript.HIRAGANA || script == Character.UnicodeScript.KATAKANA; break;
                    case "windows-1251": fits = script == Character.UnicodeScript.CYRILLIC; break;
                    default: fits = script == Character.UnicodeScript.HAN; break;
                }
                if (fits) home++;
            }
            if (other == 0) continue;
            // A few accented letters in English, French or Spanish words also read as "valid"
            // Chinese; real lyrics in these languages are mostly not plain letters
            if (other * 3 < plain) continue;
            // Japanese is part kana, part Chinese characters: a third kana is plenty
            double score = (double) home / other * ("Shift_JIS".equals(name) ? 2.5 : 1.0);
            if (score > bestScore) {
                bestScore = score;
                best = text;
            }
        }
        if (best != null) return best;
        try {
            return new String(bytes, "windows-1252");
        } catch (java.io.UnsupportedEncodingException e) {
            return new String(bytes, java.nio.charset.StandardCharsets.ISO_8859_1);
        }
    }

    /** The bytes read in the named encoding, or null when they are not valid in it. */
    private static String strict(byte[] bytes, String charset) {
        try {
            return java.nio.charset.Charset.forName(charset).newDecoder()
                    .onMalformedInput(java.nio.charset.CodingErrorAction.REPORT)
                    .onUnmappableCharacter(java.nio.charset.CodingErrorAction.REPORT)
                    .decode(java.nio.ByteBuffer.wrap(bytes)).toString();
        } catch (java.nio.charset.CharacterCodingException | IllegalArgumentException e) {
            return null;
        }
    }

    public static String stamp(double seconds) {
        int cs = (int) Math.round(Math.max(0, seconds) * 100);
        return String.format(Locale.US, "[%02d:%02d.%02d]", cs / 6000, (cs / 100) % 60, cs % 100);
    }
}
