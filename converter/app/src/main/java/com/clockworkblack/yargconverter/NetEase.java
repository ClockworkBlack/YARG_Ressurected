package com.clockworkblack.yargconverter;

import com.clockworkblack.yargconverter.core.Lrc;
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
import java.util.List;

/**
 * Timed lyrics from NetEase Cloud Music (music.163.com), a large Chinese music service whose
 * catalogue of time-synced lyrics covers a lot of English-language songs too.
 *
 * This is NOT an official, documented service for other apps: it is the address the service's own
 * web player uses, which many open-source players also read. It can change or stop working at any
 * time. For that reason it is off unless the user switches it on, and any trouble with it is
 * treated as "nothing found".
 */
final class NetEase {
    private NetEase() {}

    private static final String BROWSER = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36";

    /** @return timed lyrics (or, failing that, plain ones) for the best match, or null */
    static LrcLib.Found find(List<SongNames.Query> queries, double durationSec) {
        int searches = 0;
        LrcLib.Found plain = null;
        for (SongNames.Query q : queries) {
            if (searches >= 4) break;
            searches++;
            try {
                String words = (q.artist.isEmpty() ? "" : q.artist + " ") + q.title;
                String body = get("https://music.163.com/api/search/get?type=1&limit=10&offset=0&s=" + URLEncoder.encode(words, "UTF-8"));
                if (body == null) continue;
                JSONObject result = new JSONObject(body).optJSONObject("result");
                JSONArray songs = result == null ? null : result.optJSONArray("songs");
                if (songs == null) continue;

                long bestId = 0;
                double bestFit = 0;
                String bestTitle = "", bestArtist = "";
                double bestSeconds = 0;
                for (int i = 0; i < songs.length(); i++) {
                    JSONObject song = songs.getJSONObject(i);
                    String title = song.optString("name", "");
                    String artist = artistsOf(song);
                    // Milliseconds, under one of two names depending on the answer's vintage
                    double seconds = song.optDouble("duration", song.optDouble("dt", 0)) / 1000.0;
                    double fit = SongNames.fit(q, title, artist, durationSec, seconds);
                    if (fit > bestFit) {
                        bestFit = fit;
                        bestId = song.optLong("id", 0);
                        bestTitle = title;
                        bestArtist = artist;
                        bestSeconds = seconds;
                    }
                }
                if (bestId == 0) continue;

                String lyricBody = get("https://music.163.com/api/song/lyric?lv=1&kv=1&tv=-1&id=" + bestId);
                if (lyricBody == null) continue;
                JSONObject lrc = new JSONObject(lyricBody).optJSONObject("lrc");
                String text = lrc == null ? "" : tidy(lrc.optString("lyric", ""));
                if (text.trim().isEmpty()) continue;

                LrcLib.Found f = new LrcLib.Found();
                f.title = bestTitle;
                f.artist = bestArtist;
                f.duration = bestSeconds;
                f.source = "NetEase";
                if (Lrc.hasTimestamps(text) && Lrc.parse(text).size() >= 4) {
                    f.synced = text;
                    return f;
                }
                if (plain == null) {
                    f.plain = text.replaceAll("\\[[^\\]]*\\]", "").trim();
                    if (f.plain.length() >= 40) plain = f;
                }
            } catch (Exception e) {
                // Unofficial and liable to change: any trouble is simply "not found here"
            }
        }
        return plain;
    }

    private static String artistsOf(JSONObject song) throws JSONException {
        JSONArray artists = song.optJSONArray("artists");
        if (artists == null) artists = song.optJSONArray("ar");
        if (artists == null) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < artists.length(); i++) {
            String name = artists.getJSONObject(i).optString("name", "");
            if (!name.isEmpty()) sb.append(sb.length() > 0 ? " & " : "").append(name);
        }
        return sb.toString();
    }

    /**
     * Drops the credit lines these lyrics open with ("作词 : ...", "作曲 : ...", "Lyrics by: ...",
     * "Produced by: ..."), which are not words of the song.
     */
    static String tidy(String lrc) {
        StringBuilder out = new StringBuilder();
        for (String line : lrc.replace("\r\n", "\n").split("\n")) {
            String words = line.replaceAll("^(\\[[^\\]]*\\])+", "").trim();
            if (words.matches("(?i)^(作词|作曲|编曲|制作人|监制|混音|母带|和声|吉他|贝斯|鼓|录音|出品|发行|词|曲|"
                    + "lyrics? by|written by|composed by|produced by|producers?|arranged by|mixed by|mastered by|composers?|lyricists?)\\s*[:：].*")) {
                continue;
            }
            out.append(line).append('\n');
        }
        return out.toString();
    }

    private static String get(String url) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setConnectTimeout(8000);
            c.setReadTimeout(12000);
            c.setRequestProperty("User-Agent", BROWSER);
            c.setRequestProperty("Referer", "https://music.163.com/");
            c.setRequestProperty("Accept", "application/json, text/plain, */*");
            if (c.getResponseCode() != 200) return null;
            try (InputStream in = c.getInputStream()) {
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int n;
                while ((n = in.read(buf)) > 0) {
                    out.write(buf, 0, n);
                    if (out.size() > 4 * 1024 * 1024) return null;
                }
                return out.toString("UTF-8");
            }
        } finally {
            c.disconnect();
        }
    }
}
