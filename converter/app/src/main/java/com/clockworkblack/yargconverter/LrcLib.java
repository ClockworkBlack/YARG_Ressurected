package com.clockworkblack.yargconverter;

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
    }

    private static final String BASE = "https://lrclib.net/api/";
    private static final String AGENT = "YARGConverter/0.1 (https://github.com/ClockworkBlack/YARG_Ressurected)";

    /** @return the best match, or null when nothing was found */
    static Found find(String title, String artist, String album, double durationSec) throws IOException {
        if (title == null || title.trim().isEmpty()) return null;
        Found best = null;
        try {
            // Exact lookup first: needs the right title, artist and length (within 2 seconds).
            if (artist != null && !artist.trim().isEmpty() && durationSec > 1) {
                StringBuilder q = new StringBuilder(BASE).append("get?track_name=").append(enc(title))
                        .append("&artist_name=").append(enc(artist));
                if (album != null && !album.trim().isEmpty()) q.append("&album_name=").append(enc(album));
                q.append("&duration=").append(Math.round(durationSec));
                String body = get(q.toString());
                if (body != null) {
                    Found f = parse(new JSONObject(body));
                    if (f.synced != null) return f;
                    best = f;
                }
            }
            StringBuilder q = new StringBuilder(BASE).append("search?track_name=").append(enc(title));
            if (artist != null && !artist.trim().isEmpty()) q.append("&artist_name=").append(enc(artist));
            String body = get(q.toString());
            if (body != null) {
                JSONArray arr = new JSONArray(body);
                Found bestSynced = null;
                double bestDiff = Double.MAX_VALUE;
                for (int i = 0; i < arr.length(); i++) {
                    Found f = parse(arr.getJSONObject(i));
                    if (f.synced != null) {
                        double diff = durationSec > 1 && f.duration > 0 ? Math.abs(f.duration - durationSec) : i;
                        if (diff < bestDiff) {
                            bestDiff = diff;
                            bestSynced = f;
                        }
                    } else if (best == null && f.plain != null) {
                        best = f;
                    }
                }
                if (bestSynced != null) return bestSynced;
            }
        } catch (JSONException e) {
            throw new IOException("The lyrics service sent an unexpected reply.", e);
        }
        return best != null && (best.plain != null || best.instrumental) ? best : null;
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
