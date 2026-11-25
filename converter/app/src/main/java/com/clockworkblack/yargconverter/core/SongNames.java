package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Tidies the names a song comes with, for looking it up online. Tags and file names carry a lot
 * that a lyrics catalogue does not: "(Official Video)", "[Lyrics]", "- Remastered 2011",
 * "(feat. Someone)", a track number in front, underscores for spaces. A lookup with those in it
 * finds nothing, so each is tried without them, and results are checked against the cleaned names.
 */
public final class SongNames {
    private SongNames() {}

    private static final String JUNK = "official|video|audio|lyrics?|lyric video|visuali[sz]er|hd|hq|4k|explicit|clean|dirty|"
            + "remaster(ed)?( \\d{4})?|\\d{4} remaster(ed)?|radio edit|single version|album version|original mix|"
            + "bonus track|deluxe( edition)?|mono|stereo|live|prod\\.? by [^)\\]]*|prod\\.? [^)\\]]*|"
            + "official (music )?video|official audio|official lyric video|music video|full song|with lyrics";

    /** A title without the extras: brackets of junk and features, "- Remastered", a track number, a file ending. */
    public static String cleanTitle(String title) {
        if (title == null) return "";
        String t = title.replace('_', ' ').trim();
        t = t.replaceFirst("(?i)\\.(mp3|wav|flac|ogg|opus|m4a|aac|wma)$", "");
        // "01 - Title", "01. Title", "1-03 Title"
        t = t.replaceFirst("^\\s*(\\d{1,2}[-.]\\d{1,2}|\\d{1,3})\\s*[-.)]\\s+", "");
        t = t.replaceFirst("^\\s*\\d{2}\\s+(?=\\p{L})", "");
        // Brackets that hold a feature or junk go; brackets that are part of the name stay
        t = t.replaceAll("(?i)\\s*[\\[(]\\s*(feat\\.?|ft\\.?|featuring|with)\\s+[^\\])]*[\\])]", "");
        t = t.replaceAll("(?i)\\s*[\\[(]\\s*(" + JUNK + ")(\\s*[-,/|]?\\s*(" + JUNK + "))*\\s*[\\])]", "");
        t = t.replaceAll("(?i)\\s+(feat\\.?|ft\\.?|featuring)\\s+.*$", "");
        t = t.replaceAll("(?i)\\s+-\\s+(" + JUNK + ")(\\s+(" + JUNK + "))*\\s*$", "");
        return t.replaceAll("\\s+", " ").trim();
    }

    /** The first artist named: "Don Toliver feat. Travis Scott" gives "Don Toliver". */
    public static String primaryArtist(String artist) {
        List<String> all = Lrc.allArtists(artist, null);
        return all.isEmpty() ? (artist == null ? "" : artist.trim()) : all.get(0);
    }

    /** Letters and digits only, lower case, accents folded: for comparing names. */
    public static String key(String s) {
        if (s == null) return "";
        String n = java.text.Normalizer.normalize(s, java.text.Normalizer.Form.NFD).replaceAll("\\p{M}+", "");
        return n.toLowerCase(Locale.US).replace("&", "and").replaceAll("[^\\p{L}\\p{Nd}]+", "");
    }

    /** Whether two names are the same once tidied, or one is the other with a little more on it. */
    public static boolean same(String a, String b) {
        String x = key(a), y = key(b);
        if (x.isEmpty() || y.isEmpty()) return false;
        if (x.equals(y)) return true;
        String shorter = x.length() <= y.length() ? x : y, longer = x.length() <= y.length() ? y : x;
        return shorter.length() >= 4 && longer.contains(shorter) && shorter.length() * 2 >= longer.length();
    }

    /** One way of asking for a song: a title and an artist (either may be empty). */
    public static final class Query {
        public final String title, artist;

        Query(String title, String artist) {
            this.title = title;
            this.artist = artist;
        }

        @Override
        public boolean equals(Object o) {
            return o instanceof Query && ((Query) o).title.equalsIgnoreCase(title) && ((Query) o).artist.equalsIgnoreCase(artist);
        }

        @Override
        public int hashCode() {
            return (title + "\u0001" + artist).toLowerCase(Locale.US).hashCode();
        }
    }

    /**
     * Every sensible way to ask for this song, best first: as tagged, tidied, with only the main
     * artist, by title alone, and from the file name ("Artist - Title.mp3", either way round).
     */
    public static List<Query> queries(String title, String artist, String fileName) {
        Set<Query> out = new LinkedHashSet<>();
        List<Query> bare = new ArrayList<>();
        String t = title == null ? "" : title.trim();
        String a = artist == null ? "" : artist.trim();
        String ct = cleanTitle(t);
        String pa = primaryArtist(a);
        if (!t.isEmpty()) out.add(new Query(t, a));
        if (!ct.isEmpty()) {
            out.add(new Query(ct, a));
            out.add(new Query(ct, pa));
        }
        // A title that is really "Artist - Title" (common when a file has no tags)
        for (String whole : new String[] {ct, cleanTitle(fileName)}) {
            if (whole == null || whole.isEmpty()) continue;
            int dash = whole.indexOf(" - ");
            if (dash > 0) {
                String left = whole.substring(0, dash).trim(), right = cleanTitle(whole.substring(dash + 3));
                if (!right.isEmpty()) {
                    out.add(new Query(right, primaryArtist(left)));
                    bare.add(new Query(right, ""));
                }
                if (!left.isEmpty()) out.add(new Query(cleanTitle(left), primaryArtist(right)));
            } else if (!whole.equalsIgnoreCase(ct)) {
                out.add(new Query(whole, pa));
            }
        }
        // By title alone comes last: it is the least certain
        if (!ct.isEmpty()) out.add(new Query(ct, ""));
        out.addAll(bare);
        return new ArrayList<>(out);
    }

    /**
     * How well a catalogue entry fits what was asked for; 0 means "not this song". The title has
     * to match. After that a matching artist and a matching length each make it more certain.
     *
     * @param seconds length of the user's file, 0 when unknown
     */
    public static double fit(Query asked, String foundTitle, String foundArtist, double seconds, double foundSeconds) {
        if (!same(cleanTitle(asked.title), cleanTitle(foundTitle))) return 0;
        double score = 1;
        boolean artistKnown = !asked.artist.isEmpty();
        boolean artistFits = artistKnown && (same(asked.artist, foundArtist) || same(primaryArtist(asked.artist), primaryArtist(foundArtist)));
        boolean lengthKnown = seconds > 1 && foundSeconds > 1;
        double off = Math.abs(seconds - foundSeconds);
        if (artistFits) score += 2;
        if (lengthKnown && off <= 3) score += 2;
        else if (lengthKnown && off <= 10) score += 1;
        // A different artist AND a different length: another song with the same name
        if (artistKnown && !artistFits && !(lengthKnown && off <= 3)) return 0;
        // No artist to go by: the length has to agree
        if (!artistKnown && lengthKnown && off > 10) return 0;
        if (lengthKnown && off > 30) score -= 1;
        return Math.max(0.1, score);
    }
}
