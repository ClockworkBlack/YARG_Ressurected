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
        return detailsOnce(title, artist);
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

    /** Timed lyrics of every artist with a song of this title and about this length: {artist, title, lyrics}. */
    public static List<String[]> candidates(String title, double seconds) {
        List<String[]> out = new java.util.ArrayList<>();
        try {
            for (LrcLib.Found f : LrcLib.candidates(title, seconds, 8)) out.add(new String[] {f.artist, f.title, f.synced});
        } catch (Exception e) {
            // Nothing to choose from
        }
        return out;
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
