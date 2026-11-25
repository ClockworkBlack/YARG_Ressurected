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
import java.util.Locale;

/**
 * Fills in a song's details from MusicBrainz (musicbrainz.org), the open music encyclopedia, when
 * the audio file's own tags leave them out: the artist, the album it first came out on, the year
 * and a genre. Only the song's title and artist (or its file name) are sent.
 *
 * MusicBrainz asks for at most one request a second from each app, so the requests are spaced.
 */
final class MusicBrainz {
    private MusicBrainz() {}

    static final class Found {
        String title = "";
        String artist = "";
        String album = "";
        String year = "";
        String genre = "";
    }

    private static final String BASE = "https://musicbrainz.org/ws/2/";
    private static final String AGENT = "YARGConverter/0.1 (https://github.com/ClockworkBlack/YARG_Resurrected)";
    /** How sure MusicBrainz must be that a recording is the one asked for (0 to 100). */
    private static final int MIN_SCORE = 85;
    private static long lastRequest;

    /**
     * @param title  the song's title (required)
     * @param artist its artist, or empty when not known
     * @return what was found, or null when nothing matched well enough
     */
    static Found find(String title, String artist) throws IOException {
        if (title == null || title.trim().isEmpty()) return null;
        StringBuilder q = new StringBuilder("recording:\"").append(escape(title)).append('"');
        if (artist != null && !artist.trim().isEmpty()) q.append(" AND artist:\"").append(escape(artist)).append('"');
        String body = get(BASE + "recording?fmt=json&limit=10&query=" + URLEncoder.encode(q.toString(), "UTF-8"));
        if (body == null) return null;
        try {
            JSONArray recordings = new JSONObject(body).optJSONArray("recordings");
            if (recordings == null) return null;
            JSONObject best = null;
            for (int i = 0; i < recordings.length(); i++) {
                JSONObject r = recordings.getJSONObject(i);
                if (r.optInt("score", 0) < MIN_SCORE) continue;
                // Prefer the recording that came out first: the original, not a remaster or a cover
                if (best == null || firstYear(r) < firstYear(best)) best = r;
            }
            if (best == null) return null;

            Found f = new Found();
            f.title = best.optString("title", "");
            f.artist = artistOf(best);
            int year = firstYear(best);
            if (year < 9999) f.year = String.valueOf(year);
            f.album = albumOf(best);
            f.genre = topTag(best.optJSONArray("tags"));
            if (f.genre.isEmpty()) f.genre = genreOfArtist(best);
            return f;
        } catch (JSONException e) {
            throw new IOException("MusicBrainz sent an unexpected reply.", e);
        }
    }

    /** The artists as credited, e.g. "Don Toliver feat. Travis Scott". */
    private static String artistOf(JSONObject recording) throws JSONException {
        JSONArray credit = recording.optJSONArray("artist-credit");
        if (credit == null) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < credit.length(); i++) {
            JSONObject c = credit.getJSONObject(i);
            sb.append(c.optString("name", c.has("artist") ? c.getJSONObject("artist").optString("name", "") : ""));
            sb.append(c.optString("joinphrase", ""));
        }
        return sb.toString().trim();
    }

    /** The year of the earliest release the recording is on, or 9999 when none says. */
    private static int firstYear(JSONObject recording) throws JSONException {
        int best = year(recording.optString("first-release-date", ""));
        JSONArray releases = recording.optJSONArray("releases");
        if (releases != null) {
            for (int i = 0; i < releases.length(); i++) best = Math.min(best, year(releases.getJSONObject(i).optString("date", "")));
        }
        return best;
    }

    private static int year(String date) {
        if (date == null || date.length() < 4) return 9999;
        try {
            int y = Integer.parseInt(date.substring(0, 4));
            return y > 1800 ? y : 9999;
        } catch (NumberFormatException e) {
            return 9999;
        }
    }

    /** The earliest album the recording is on (not a single or a compilation, when there is one). */
    private static String albumOf(JSONObject recording) throws JSONException {
        JSONArray releases = recording.optJSONArray("releases");
        if (releases == null) return "";
        String album = "", any = "";
        int albumYear = 99999, anyYear = 99999;
        for (int i = 0; i < releases.length(); i++) {
            JSONObject r = releases.getJSONObject(i);
            int y = year(r.optString("date", ""));
            JSONObject group = r.optJSONObject("release-group");
            String type = group == null ? "" : group.optString("primary-type", "");
            boolean compilation = false;
            JSONArray secondary = group == null ? null : group.optJSONArray("secondary-types");
            if (secondary != null) {
                for (int k = 0; k < secondary.length(); k++) compilation |= "Compilation".equalsIgnoreCase(secondary.optString(k));
            }
            String title = r.optString("title", "");
            if ("Album".equalsIgnoreCase(type) && !compilation && y < albumYear) {
                album = title;
                albumYear = y;
            }
            if (y < anyYear) {
                any = title;
                anyYear = y;
            }
        }
        return !album.isEmpty() ? album : any;
    }

    /** A recording seldom has genres of its own; the artist usually does. */
    private static String genreOfArtist(JSONObject recording) throws JSONException, IOException {
        JSONArray credit = recording.optJSONArray("artist-credit");
        if (credit == null || credit.length() == 0) return "";
        JSONObject artist = credit.getJSONObject(0).optJSONObject("artist");
        String id = artist == null ? "" : artist.optString("id", "");
        if (id.isEmpty()) return "";
        String body = get(BASE + "artist/" + URLEncoder.encode(id, "UTF-8") + "?fmt=json&inc=genres+tags");
        if (body == null) return "";
        JSONObject a = new JSONObject(body);
        String genre = topTag(a.optJSONArray("genres"));
        return genre.isEmpty() ? topTag(a.optJSONArray("tags")) : genre;
    }

    /** The genre or tag most people agreed on, written as a genre is: "Contemporary R&B". */
    private static String topTag(JSONArray tags) throws JSONException {
        if (tags == null) return "";
        String best = "";
        int bestCount = 0;
        for (int i = 0; i < tags.length(); i++) {
            JSONObject t = tags.getJSONObject(i);
            int count = t.optInt("count", 0);
            String name = t.optString("name", "").trim();
            if (count > bestCount && !name.isEmpty() && name.length() <= 40) {
                best = name;
                bestCount = count;
            }
        }
        return titleCase(best);
    }

    static String titleCase(String s) {
        StringBuilder sb = new StringBuilder();
        boolean start = true;
        for (String word : s.split(" ")) {
            if (!start) sb.append(' ');
            start = false;
            String w = word.toLowerCase(Locale.US);
            if (w.equals("r&b") || w.equals("rnb") || w.equals("edm") || w.equals("uk")) sb.append(w.toUpperCase(Locale.US));
            else if (!w.isEmpty()) sb.append(Character.toUpperCase(w.charAt(0))).append(w.substring(1));
        }
        return sb.toString();
    }

    /** Lucene's special characters, so a title like "Ain't No Mountain (High Enough)" searches as written. */
    private static String escape(String s) {
        return s.trim().replaceAll("([+\\-!(){}\\[\\]^\"~*?:\\\\/]|&&|\\|\\|)", "\\\\$1");
    }

    /** @return the response body, or null for "not found" */
    private static synchronized String get(String url) throws IOException {
        long wait = lastRequest + 1100 - System.currentTimeMillis();
        if (wait > 0) {
            try {
                Thread.sleep(wait);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IOException("Interrupted");
            }
        }
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setConnectTimeout(10000);
            c.setReadTimeout(15000);
            c.setRequestProperty("User-Agent", AGENT);
            c.setRequestProperty("Accept", "application/json");
            int code = c.getResponseCode();
            if (code == 404) return null;
            if (code != 200) throw new IOException("MusicBrainz answered with error " + code + ".");
            try (InputStream in = c.getInputStream()) {
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int n;
                while ((n = in.read(buf)) > 0) {
                    out.write(buf, 0, n);
                    if (out.size() > 4 * 1024 * 1024) throw new IOException("The MusicBrainz reply was too large.");
                }
                return out.toString("UTF-8");
            }
        } finally {
            lastRequest = System.currentTimeMillis();
            c.disconnect();
        }
    }
}
