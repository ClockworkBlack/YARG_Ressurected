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
            + "official (music )?video|official audio|official lyric video|music video|full song|with lyrics|"
            // K-pop uploads: "(Color Coded Lyrics Han/Rom/Eng)", "[Eng Sub]", "(MV)", "(Dance Practice)"
            + "colou?r coded( lyrics)?[^)\\]]*|lyrics? [a-z]{2,4}(/[a-z]{2,4})+|[a-z]{3}(/[a-z]{3})+|eng(lish)? sub(s|titles?)?|"
            + "m/?v|performance video|dance practice|teaser|special video";

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
        // A bracket left open at the end: a file name cut short ("Genie (Color Coded Lyr")
        t = t.replaceFirst("\\s*[\\[(][^\\])]*$", "");
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
    /**
     * Artist and title from the name of a file without tags: {artist, or "" when the name has
     * none, title}. "Artist - Title.mp3" is the usual form. A long run of underscores is where a
     * download site dropped letters it could not keep ("Girls_Generation______Gee_MV" had 소녀시대
     * there), and it parts the artist from the title the same way. Words a video's name ends with
     * ("MV", "Official Video") are left off the title.
     */
    public static String[] fromFileName(String name) {
        String base = name == null ? "" : name.replaceFirst("\\.[A-Za-z0-9]{2,5}$", "");
        // A dash between the two, with spaces or underscores round it, of any length
        base = base.replaceAll("[ _]+[\u2013\u2014-][ _]+", " - ");
        if (!base.contains(" - ")) base = base.replaceFirst("_{3,}", " - ");
        base = base.replace('_', ' ').replaceAll("\\s+", " ").trim();
        // A TV show's upload: "[Music Bank] KBS WORLD TV 241108"
        base = base.replaceAll("(?i)\\s*[\\[(](music bank|inkigayo|m ?countdown|show champion|show! music core|music core|the show|"
                + "simply k-pop|immortal songs|sketchbook|mpd직캠|직캠|fancam|stage mix|교차편집)[^\\])]*[\\])]", "");
        base = base.replaceAll("(?i)\\s+(kbs|mbc|sbs|mnet|jtbc|arirang|kbs world)( world)?( tv)?( k-?pop)?( \\d{6})?\\s*$", "").trim();
        String artist, title;
        int dash = base.indexOf(" - ");
        java.util.regex.Matcher quoted = QUOTED.matcher(base);
        java.util.regex.Matcher kLyrics = K_LYRICS.matcher(base);
        if (dash > 0) {
            artist = base.substring(0, dash).trim();
            title = base.substring(dash + 3).trim();
            // "Came And Left Me - MINHO 민호": the side with the name in two alphabets is the artist
            if (hasAlias(title) && !hasAlias(artist)) {
                String swap = artist;
                artist = title;
                title = swap;
            }
        } else if (quoted.find() && quoted.start() > 0) {
            // "Artist 'Title' MV", "Artist 'Title' Lyrics (...) [Color Coded]": the title is what is
            // in quotes (with a feature in it kept); what follows is the uploader's
            artist = base.substring(0, quoted.start()).trim();
            title = quoted.group(1).trim();
        } else if (kLyrics.find()) {
            // "MINHO CALL BACK Lyrics (민호 CALL BACK 가사)": the title is the Latin part of the bracket
            String inside = kLyrics.group(2).replaceAll("[\\p{IsHangul}]+", " ").replaceAll("\\s+", " ").trim();
            String before = kLyrics.group(1).trim();
            if (!inside.isEmpty() && before.toLowerCase(Locale.ROOT).endsWith(inside.toLowerCase(Locale.ROOT))
                    && before.length() > inside.length()) {
                artist = before.substring(0, before.length() - inside.length()).trim();
                title = inside;
            } else {
                artist = "";
                title = before;
            }
        } else {
            artist = "";
            title = base;
        }
        artist = withoutAlias(artist);
        // A K-pop soloist with the group in brackets: "NAYEON (TWICE)", "SEULGI (RED VELVET)"
        String solo = artist.replaceFirst("^(\\p{Lu}[\\p{Lu}\\d .&-]*?)\\s*\\((\\p{Lu}[\\p{Lu}\\d .&'-]*)( of [^)]*)?\\)$", "$1");
        if (!solo.isEmpty()) artist = solo;
        return new String[] {artist, dropVideoWords(title)};
    }

    /** "Title Lyrics (한글 가사)": the way lyric videos of Korean songs are named. */
    private static final java.util.regex.Pattern K_LYRICS =
            java.util.regex.Pattern.compile("(?i)^(.*?)\\s+lyrics\\s*[(\\[]([^)\\]]*\uAC00\uC0AC)\\s*[)\\]].*$");

    /** A name followed by the same name in another alphabet: "MINHO 민호", "Girls' Generation (소녀시대)". */
    private static boolean hasAlias(String s) {
        return s.matches(".*\\p{IsLatin}.*") && !withoutAlias(s).equals(s.trim());
    }

    /**
     * Artist and title from a file's tags, put right where a video site wrote them: the channel
     * as the artist ("anthonyhamiltonVEVO", "Bad Boy Entertainment", "BigGucci Sosa") and
     * "Artist - Title" as the title. {artist, title}; tags that look right come back as they are.
     */
    public static String[] fromTags(String title, String artist, String fileName) {
        String t = title == null ? "" : title.trim(), a = artist == null ? "" : artist.trim();
        String[] named = fromFileName(fileName);
        int dash = t.indexOf(" - ");
        if (dash > 0) {
            String left = t.substring(0, dash).trim(), right = t.substring(dash + 3).trim();
            String kl = key(left), ka = key(a);
            boolean channel = a.isEmpty() || looksLikeChannel(a) || (!named[0].isEmpty() && key(named[0]).equals(kl))
                    || (kl.length() >= 3 && ka.contains(kl) && !ka.equals(kl));
            if (channel && !right.isEmpty() && !key(a).equals(key(right))) return new String[] {left, right};
        }
        if (looksLikeChannel(a) && !named[0].isEmpty()) return new String[] {named[0], t.isEmpty() ? named[1] : t};
        return new String[] {a, t};
    }

    /** A video channel or a label rather than a performer: "...VEVO", "Atlantic Records", "X - Topic". */
    public static boolean looksLikeChannel(String artist) {
        return artist != null && artist.trim().matches("(?i).*(vevo|\\brecords?|\\brecordings|\\bentertainment|\\bmusic|\\bofficial|\\btv"
                + "|\\bchannel|\\bfilms?|\\bmedia|\\bproductions?|\\blabel|\\bstudios?|- topic|\\bworldwide|\\bgroup)\\s*$");
    }

    /**
     * A title that names its artist without a dash ("The Rio Da Yung OG On The Radar Freestyle"),
     * read against artists already known (from the other songs converted with it): {artist, the
     * rest as the title}, or null when none of them is in it. The longest name found wins.
     */
    public static String[] artistInTitle(String title, java.util.Collection<String> known) {
        if (title == null || known == null) return null;
        String[] words = title.trim().split("\\s+");
        String bestArtist = null;
        int bestFrom = -1, bestTo = -1, bestLength = 0;
        for (String artist : known) {
            String k = key(artist);
            if (k.length() < 4 || k.length() <= bestLength) continue;
            for (int i = 0; i < words.length; i++) {
                StringBuilder sb = new StringBuilder();
                for (int j = i; j < words.length; j++) {
                    sb.append(key(words[j]));
                    if (sb.length() > k.length() || !k.startsWith(sb.toString())) break;
                    if (sb.length() == k.length()) {
                        bestArtist = artist.trim();
                        bestFrom = i;
                        bestTo = j;
                        bestLength = k.length();
                        break;
                    }
                }
                if (bestArtist != null && bestLength == k.length()) break;
            }
        }
        if (bestArtist == null) return null;
        String before = String.join(" ", java.util.Arrays.copyOfRange(words, 0, bestFrom)).trim();
        String after = String.join(" ", java.util.Arrays.copyOfRange(words, bestTo + 1, words.length)).trim();
        // "The <artist> ...": a leading article belongs to neither
        if (before.matches("(?i)(the|a|an)")) before = "";
        String rest = (before + " " + after).trim().replaceAll("^[-:|,]+\\s*", "").replaceAll("\\s*[-:|,]+$", "");
        return rest.isEmpty() ? null : new String[] {bestArtist, rest};
    }

    /** A title in quotes: the opening quote after a space, the closing one before a space or the end. */
    private static final java.util.regex.Pattern QUOTED =
            java.util.regex.Pattern.compile("(?<=^|\\s)['\u2018\"\u201c]([^'\u2019\"\u201d]{1,80})['\u2019\"\u201d](?=\\s|$)");

    /**
     * An artist without the same name in another alphabet beside it: "Girls' Generation (소녀시대)"
     * and "Girls' Generation 소녀시대" are "Girls' Generation". A name in one alphabet only is kept.
     */
    static String withoutAlias(String artist) {
        String a = artist.trim();
        if (!a.matches(".*\\p{IsLatin}.*")) return a;
        String before;
        do {
            before = a;
            a = a.replaceFirst("\\s*[(\\[]\\s*[^\\p{IsLatin})\\]]+[)\\]]\\s*$", "").trim();
            a = a.replaceFirst("\\s+[\\p{IsHangul}\\p{IsHan}\\p{IsHiragana}\\p{IsKatakana}]+$", "").trim();
        } while (!a.equals(before) && !a.isEmpty());
        return a.isEmpty() ? artist.trim() : a;
    }

    /** "Title Official 4K Video", "Title Lyric Video", "Title MV": a file name that lost its brackets. */
    public static String dropVideoWords(String title) {
        String t = title == null ? "" : title.trim();
        String before;
        do {
            before = t;
            t = t.replaceAll("(?i)\\s+(official|lyrics?|music|video|audio|visuali[sz]er|4k|hd|hq|mv|m/v|remaster(ed)?|explicit|clean)$", "").trim();
        } while (!t.equals(before) && t.contains(" "));
        return t.isEmpty() ? (title == null ? "" : title.trim()) : t;
    }

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
        String[] named = fromFileName(fileName);
        String fromName = named[0].isEmpty() ? named[1] : named[0] + " - " + named[1];
        for (String whole : new String[] {ct, cleanTitle(fromName)}) {
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
