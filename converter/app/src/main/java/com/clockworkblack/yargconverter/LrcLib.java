package com.clockworkblack.yargconverter;

import com.clockworkblack.yargconverter.core.SongNames;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;

/** Looks up lyrics in LRCLIB (lrclib.net), a free community database of time-synced lyrics. */
final class LrcLib {
    private LrcLib() {}

    static final class Found {
        /** Time-stamped lyrics, or null. */
        String synced;
        /** Untimed lyrics, or null. */
        String plain;
        String title = "";
        String artist = "";
        /** Length of the recording the lyrics were timed against, in seconds (0 if unknown). */
        double duration;
        boolean instrumental;
        /** Which catalogue it came from, for the user. */
        String source = "LRCLIB";
    }

    /**
     * Also ask NetEase Cloud Music when LRCLIB has no timed lyrics. Off unless the user switches
     * it on: it is an unofficial source (see {@link NetEase}).
     */
    static volatile boolean useNetEase;

    private static final String BASE = "https://lrclib.net/api/";
    private static final String AGENT = "YARGConverter/0.1 (https://github.com/ClockworkBlack/YARG_Ressurected)";

    /** @return the best match, or null when nothing was found */
    static Found find(String title, String artist, String album, double durationSec) throws IOException {
        return find(title, artist, album, durationSec, null);
    }

    /**
     * Looks a song up every sensible way until its timed lyrics turn up: as tagged, then with the
     * title tidied ("(Official Video)", "(feat. X)", "- Remastered", a track number), with only
     * the main artist, as free text, by title alone, and from the file name ("Artist - Title").
     * Each result is checked against what was asked (same title, and the same artist or the same
     * length), so a different song that shares the name is not taken.
     *
     * @param fileName the audio file's name, or null
     * @return the best match, or null when nothing was found
     */
    static Found find(String title, String artist, String album, double durationSec, String fileName) throws IOException {
        java.util.List<SongNames.Query> queries = SongNames.queries(title, artist, fileName);
        if (queries.isEmpty()) return null;
        Found best = null, bestPlain = null;
        double bestFit = 0, bestPlainFit = 0;
        IOException failure = null;
        java.util.Set<String> asked = new java.util.HashSet<>();
        try {
            // Exact lookups first: they need the right title, artist and length (within 2 seconds)
            int exact = 0;
            for (SongNames.Query q : queries) {
                if (q.artist.isEmpty() || durationSec <= 1 || exact >= 3) continue;
                exact++;
                StringBuilder url = new StringBuilder(BASE).append("get?track_name=").append(enc(q.title))
                        .append("&artist_name=").append(enc(q.artist));
                if (exact == 1 && album != null && !album.trim().isEmpty()) url.append("&album_name=").append(enc(album));
                url.append("&duration=").append(Math.round(durationSec));
                if (!asked.add(url.toString())) continue;
                try {
                    String body = get(url.toString());
                    if (body == null) continue;
                    Found f = parse(new JSONObject(body));
                    if (f.synced != null) return f;
                    if (bestPlain == null && (f.plain != null || f.instrumental)) {
                        bestPlain = f;
                        bestPlainFit = 5;
                    }
                } catch (IOException e) {
                    failure = e;
                }
            }

            int searches = 0;
            for (SongNames.Query q : queries) {
                java.util.List<String> urls = new java.util.ArrayList<>();
                if (!q.artist.isEmpty()) {
                    urls.add(BASE + "search?track_name=" + enc(q.title) + "&artist_name=" + enc(q.artist));
                    urls.add(BASE + "search?q=" + enc(q.artist + " " + q.title));
                } else {
                    urls.add(BASE + "search?track_name=" + enc(q.title));
                }
                for (String url : urls) {
                    if (!asked.add(url) || searches >= 10) continue;
                    searches++;
                    String body;
                    try {
                        body = get(url);
                    } catch (IOException e) {
                        failure = e;
                        continue;
                    }
                    if (body == null) continue;
                    JSONArray arr = new JSONArray(body);
                    for (int i = 0; i < arr.length(); i++) {
                        Found f = parse(arr.getJSONObject(i));
                        double fit = SongNames.fit(q, f.title, f.artist, durationSec, f.duration);
                        if (fit <= 0) continue;
                        if (f.synced != null && fit > bestFit) {
                            bestFit = fit;
                            best = f;
                        } else if (f.synced == null && (f.plain != null || f.instrumental) && fit > bestPlainFit) {
                            bestPlainFit = fit;
                            bestPlain = f;
                        }
                    }
                }
                // Title with the artist or the length confirmed: no need to keep asking
                if (bestFit >= 3) break;
            }
        } catch (JSONException e) {
            throw new IOException("The lyrics service sent an unexpected reply.", e);
        }
        if (best != null) return best;
        // No timed lyrics in LRCLIB: the optional second catalogue of timed lyrics
        Found netEasePlain = null;
        if (useNetEase) {
            Found other = NetEase.find(queries, durationSec);
            if (other != null && other.synced != null) return other;
            netEasePlain = other;
        }
        if (bestPlain != null && bestPlain.plain != null) return bestPlain;
        if (netEasePlain != null) return netEasePlain;
        // No timed lyrics anywhere, and no plain ones in LRCLIB: a second catalogue for the words
        // themselves (untimed; they can then be tapped in time)
        Found words = fromLyricsOvh(queries);
        if (words != null) return words;
        if (bestPlain != null) return bestPlain;
        // Nothing found and the service could not be reached at all: say so
        if (failure != null) throw failure;
        return null;
    }

    /**
     * Every timed lyric of a title, one per artist (the one nearest in length), for when the
     * artist is not known: which of them is this recording is then told by the singing.
     * Lyrics timed to a recording more than 20 seconds longer or shorter are left out.
     */
    static java.util.List<Found> candidates(String title, double durationSec, int max) throws IOException {
        java.util.Map<String, Found> byArtist = new java.util.LinkedHashMap<>();
        String clean = SongNames.cleanTitle(title);
        if (clean.isEmpty()) return new java.util.ArrayList<>();
        String body = get(BASE + "search?track_name=" + enc(clean));
        if (body == null) return new java.util.ArrayList<>();
        try {
            JSONArray arr = new JSONArray(body);
            for (int i = 0; i < arr.length(); i++) {
                Found f = parse(arr.getJSONObject(i));
                if (f.synced == null || !SongNames.key(SongNames.cleanTitle(f.title)).equals(SongNames.key(clean))) continue;
                if (durationSec > 1 && f.duration > 1 && Math.abs(f.duration - durationSec) > 20) continue;
                String k = SongNames.key(SongNames.primaryArtist(f.artist));
                Found had = byArtist.get(k);
                if (had == null || Math.abs(f.duration - durationSec) < Math.abs(had.duration - durationSec)) byArtist.put(k, f);
            }
        } catch (JSONException e) {
            throw new IOException("The lyrics service sent an unexpected reply.", e);
        }
        java.util.List<Found> out = new java.util.ArrayList<>(byArtist.values());
        out.sort((x, y) -> Double.compare(Math.abs(x.duration - durationSec), Math.abs(y.duration - durationSec)));
        return out.size() > max ? out.subList(0, max) : out;
    }

    /**
     * Lyrics.ovh (api.lyrics.ovh), a free catalogue of plain lyrics. It has no times, so what it
     * finds still has to be tapped in time; it needs both an artist and a title.
     */
    private static Found fromLyricsOvh(java.util.List<SongNames.Query> queries) {
        int tried = 0;
        for (SongNames.Query q : queries) {
            if (q.artist.isEmpty() || tried >= 3) continue;
            tried++;
            try {
                String body = get("https://api.lyrics.ovh/v1/" + path(q.artist) + "/" + path(q.title));
                if (body == null) continue;
                String text = new JSONObject(body).optString("lyrics", "").replace("\r\n", "\n").trim();
                // The service starts some lyrics with a line in French naming the song
                text = text.replaceFirst("^Paroles de la chanson [^\n]*\n", "").trim();
                if (text.length() < 40) continue;
                Found f = new Found();
                f.plain = text;
                f.title = q.title;
                f.artist = q.artist;
                f.source = "Lyrics.ovh";
                return f;
            } catch (IOException | JSONException e) {
                // This catalogue is a bonus: any trouble with it just means "not found"
            }
        }
        return null;
    }

    private static String path(String s) throws IOException {
        return URLEncoder.encode(s.trim(), "UTF-8").replace("+", "%20");
    }

    private static Found parse(JSONObject o) {
        Found f = new Found();
        f.synced = text(o, "syncedLyrics");
        f.plain = text(o, "plainLyrics");
        f.title = o.optString("trackName", "");
        f.artist = o.optString("artistName", "");
        f.duration = o.optDouble("duration", 0);
        f.instrumental = o.optBoolean("instrumental", false);
        return f;
    }

    private static String text(JSONObject o, String key) {
        if (o.isNull(key)) return null;
        String s = o.optString(key, "");
        return s.trim().isEmpty() ? null : s;
    }

    private static String enc(String s) throws IOException {
        return URLEncoder.encode(s.trim(), "UTF-8");
    }

    /** @return the response body, or null for "not found" */
    private static String get(String url) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setConnectTimeout(10000);
            c.setReadTimeout(15000);
            c.setRequestProperty("User-Agent", AGENT);
            c.setRequestProperty("Accept", "application/json");
            int code = c.getResponseCode();
            if (code == 404) return null;
            if (code != 200) throw new IOException("The lyrics service answered with error " + code + ".");
            try (InputStream in = c.getInputStream()) {
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int n;
                while ((n = in.read(buf)) > 0) {
                    out.write(buf, 0, n);
                    if (out.size() > 4 * 1024 * 1024) throw new IOException("The lyrics reply was too large.");
                }
                return out.toString("UTF-8");
            }
        } finally {
            c.disconnect();
        }
    }
}
