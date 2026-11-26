package com.clockworkblack.yargconverter;

import com.clockworkblack.yargconverter.core.Lrc;

import java.util.List;

/**
 * The app's online lookups, for the desktop runner: song details from MusicBrainz, and lyrics
 * the way the app finds them (LRCLIB, then NetEase, then untimed words from Lyrics.ovh).
 */
public final class Lookup {
    private Lookup() {}

    /** @return title, artist, album, year, genre ("" where unknown), or null when nothing matched */
    public static String[] details(String title, String artist) {
        String[] found = detailsOnce(title, artist);
        if (found != null || artist == null || artist.trim().isEmpty()) return found;
        // A title from a mangled file name ("Ascension Dont Ever Wonder" for "Ascension (Don't Ever
        // Wonder)"): with the artist known, try its first words, and keep a match only when the
        // title found starts with them
        String[] words = title.trim().split("\\s+");
        for (int n = Math.min(3, words.length - 1); n >= 1; n--) {
            String head = String.join(" ", java.util.Arrays.copyOf(words, n));
            if (head.length() < 4) continue;
            found = detailsOnce(head, artist);
            if (found != null && com.clockworkblack.yargconverter.core.SongNames.key(found[0])
                    .startsWith(com.clockworkblack.yargconverter.core.SongNames.key(head))) return found;
        }
        return null;
    }

    private static String[] detailsOnce(String title, String artist) {
        // MusicBrainz turns away more than one request a second (error 503), which happens when
        // several songs are converted at once: wait a little and ask again
        for (int attempt = 0; attempt < 4; attempt++) {
            try {
                MusicBrainz.Found f = MusicBrainz.find(title, artist);
                if (f == null) return null;
                return new String[] {f.title, f.artist, f.album, f.year, f.genre};
            } catch (java.io.IOException e) {
                try {
                    Thread.sleep(1500L + (long) (Math.random() * 2500) + 2000L * attempt);
                } catch (InterruptedException stop) {
                    return null;
                }
            } catch (Exception e) {
                return null;
            }
        }
        return null;
    }

    /** What the lyric search found. */
    public static final class Lyrics {
        /** Timed lyrics (with singer marks when the lyrics say who sings what), or null. */
        public String timed;
        /** The words without times, or null. */
        public String plain;
        public String source = "";
        public String foundTitle = "", foundArtist = "";
        /** Length of the recording the lyrics were timed to, seconds (0 if unknown). */
        public double duration;
    }

    public static Lyrics lyrics(String title, String artist, String album, double seconds, String fileName, List<String> singers) {
        LrcLib.useNetEase = System.getProperty("yarg.nonetease") == null;
        try {
            LrcLib.Found f = LrcLib.find(title, artist, album, seconds, fileName);
            if (f == null || f.instrumental) return null;
            Lyrics out = new Lyrics();
            out.source = f.source;
            out.foundTitle = f.title;
            out.foundArtist = f.artist;
            out.duration = f.duration;
            out.plain = f.plain;
            if (f.synced != null) {
                String marked = singers != null && singers.size() >= 2 ? Lrc.markFromHeaders(f.synced, f.plain, singers) : null;
                out.timed = marked != null ? marked : f.synced;
            }
            return out.timed != null || (out.plain != null && !out.plain.trim().isEmpty()) ? out : null;
        } catch (Exception e) {
            return null;
        }
    }
}
