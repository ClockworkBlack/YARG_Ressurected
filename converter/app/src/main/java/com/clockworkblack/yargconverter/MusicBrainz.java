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
        Found found = findOnce(title, artist);
        if (found != null || title == null || artist == null || artist.trim().isEmpty()) return found;
        // A title from a mangled file name ("Ascension Dont Ever Wonder" for "Ascension (Don't Ever
        // Wonder)"): with the artist known, try its first words, and keep a match only when the
        // title found starts with them
        String[] words = com.clockworkblack.yargconverter.core.SongNames.cleanTitle(title).trim().split("\\s+");
        for (int n = Math.min(3, words.length - 1); n >= 1; n--) {
            String head = String.join(" ", java.util.Arrays.copyOf(words, n));
            if (head.length() < 4) continue;
            found = findOnce(head, artist);
            if (found != null && com.clockworkblack.yargconverter.core.SongNames.key(found.title)
                    .startsWith(com.clockworkblack.yargconverter.core.SongNames.key(head))) return found;
        }
        return null;
    }

    private static Found findOnce(String title, String artist) throws IOException {
        if (title == null || title.trim().isEmpty()) return null;
        // Without "(Official Video)", "(feat. X)" and the like, and with the main artist only
        title = com.clockworkblack.yargconverter.core.SongNames.cleanTitle(title);
        String asCredited = artist;
        artist = com.clockworkblack.yargconverter.core.SongNames.primaryArtist(artist);
        if (title.isEmpty()) return null;
        StringBuilder q = new StringBuilder("recording:\"").append(escape(title)).append('"');
        if (artist != null && !artist.trim().isEmpty()) q.append(" AND artist:\"").append(escape(artist)).append('"');
        String body = get(BASE + "recording?fmt=json&limit=10&query=" + URLEncoder.encode(q.toString(), "UTF-8"));
        if (body == null) return null;
        try {
            JSONArray recordings = new JSONObject(body).optJSONArray("recordings");
            if (recordings == null) return null;
            JSONObject best = null;
            boolean byArtist = artist != null && !artist.trim().isEmpty();
            for (int i = 0; i < recordings.length(); i++) {
                JSONObject r = recordings.getJSONObject(i);
                if (r.optInt("score", 0) < MIN_SCORE) continue;
                // Title alone matches loosely ("Round 11" finds "JAPONESQUE (Round 1-1)"): the
                // title found must be the one asked for
                String found = r.optString("title", "");
                if (!byArtist && !base(found).equals(base(title))
                        && !com.clockworkblack.yargconverter.core.SongNames.key(found).equals(com.clockworkblack.yargconverter.core.SongNames.key(title))) continue;
                if (best == null) {
                    best = r;
                } else if (byArtist) {
                    // The artist is known: of their recordings, the one that came out first is the
                    // original, not a remaster or a live take
                    if (firstYear(r) < firstYear(best)) best = r;
                } else {
                    // Title alone: many songs share a name, so the best-known one (on the most
                    // releases) is the likeliest, not the oldest
                    if (releases(r) > releases(best)) best = r;
                }
            }
            if (best == null) return null;

            Found f = new Found();
            f.title = best.optString("title", "");
            f.artist = artistOf(best);
            int year = firstYear(best);
            if (year < 9999) f.year = String.valueOf(year);
            f.album = albumOf(best);
            if (byArtist) {
                // The same song is often filed as several recordings: the album version, a promo
                // sampler, compilations. The studio album it is on is the album to show, from
                // whichever of them has it.
                String song = base(f.title);
                JSONObject[] studio = studioAlbum(recordings, song);
                // The artist may be credited by another name on the original (소녀시대 for Girls'
                // Generation): their recordings of the song under every name
                JSONArray all = byArtistId(best, f.title);
                JSONObject[] other = all == null ? null : studioAlbum(all, song);
                if (other != null && (studio == null || date(other[1]).compareTo(date(studio[1])) < 0)) studio = other;
                if (studio != null) {
                    f.album = studio[1].optString("title", f.album);
                    // Its full title ("Ascension (Don't Ever Wonder)"), unless what is in brackets
                    // only names a version ("(Music Video)", "(Live)", "[Japanese ver.]")
                    String full = studio[0].optString("title", f.title);
                    if (!full.toLowerCase(Locale.ROOT).matches(".*[(\\[][^)\\]]*\\b(ver|version|mix|remix|live|video|edit|instrumental|acoustic|demo|remaster(ed)?|mono|stereo|uncut|clean|explicit)\\b.*")) f.title = full;
                    int y = year(studio[1].optString("date", ""));
                    if (y < 9999 && y <= year) f.year = String.valueOf(y);
                }
            }
            f.genre = topTag(best.optJSONArray("tags"));
            if (f.genre.isEmpty()) f.genre = genreOfArtist(best);
            return f;
        } catch (JSONException e) {
            throw new IOException("MusicBrainz sent an unexpected reply.", e);
        }
    }

    private static int releases(JSONObject recording) {
        JSONArray r = recording.optJSONArray("releases");
        return r == null ? 0 : r.length();
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

    /** A title without what is in brackets: "Ascension (Don't Ever Wonder)" is "ascension". */
    private static String base(String title) {
        return com.clockworkblack.yargconverter.core.SongNames.key(title.replaceAll("\\s*[(\\[][^)\\]]*[)\\]]", ""));
    }

    /**
     * Of the recordings of one song (same title without brackets), the official studio album
     * release that came out first: {recording, release}, or null when none is on one.
     */
    private static JSONObject[] studioAlbum(JSONArray recordings, String song) throws JSONException {
        if (song.isEmpty()) return null;
        JSONObject[] best = null;
        String bestDate = "";
        for (int i = 0; i < recordings.length(); i++) {
            JSONObject r = recordings.getJSONObject(i);
            if (r.optInt("score", 0) < 60 || !base(r.optString("title", "")).equals(song)) continue;
            JSONArray releases = r.optJSONArray("releases");
            if (releases == null) continue;
            for (int k = 0; k < releases.length(); k++) {
                JSONObject rel = releases.getJSONObject(k);
                JSONObject group = rel.optJSONObject("release-group");
                String type = group == null ? "" : group.optString("primary-type", "");
                if (!"Album".equalsIgnoreCase(type) && !"EP".equalsIgnoreCase(type)) continue;
                JSONArray secondary = group.optJSONArray("secondary-types");
                if (secondary != null && secondary.length() > 0) continue; // compilation, live, soundtrack, ...
                if (!"Official".equalsIgnoreCase(rel.optString("status", ""))) continue;
                String date = date(rel);
                if (best == null || date.compareTo(bestDate) < 0) {
                    best = new JSONObject[] {r, rel};
                    bestDate = date;
                }
            }
        }
        return best;
    }

    /** A release's date for sorting; undated ones last. */
    private static String date(JSONObject release) {
        String d = release.optString("date", "");
        return d.length() < 4 ? "9999" : d;
    }

    /** The recordings of a song by the artist of {@code recording}, whatever name they are credited by. */
    private static JSONArray byArtistId(JSONObject recording, String title) throws JSONException, IOException {
        JSONArray credit = recording.optJSONArray("artist-credit");
        if (credit == null || credit.length() == 0) return null;
        JSONObject artist = credit.getJSONObject(0).optJSONObject("artist");
        String id = artist == null ? "" : artist.optString("id", "");
        if (!id.matches("[0-9a-f-]{36}")) return null;
        String q = "recording:\"" + escape(title.replaceAll("\\s*[(\\[][^)\\]]*[)\\]]", "")) + "\" AND arid:" + id;
        // Only a better album name rides on this: when MusicBrainz is busy, ask once more, then
        // do without it
        for (int attempt = 0; attempt < 2; attempt++) {
            try {
                String body = get(BASE + "recording?fmt=json&limit=25&query=" + URLEncoder.encode(q, "UTF-8"));
                return body == null ? null : new JSONObject(body).optJSONArray("recordings");
            } catch (IOException e) {
                if (attempt == 0) {
                    try {
                        Thread.sleep(2500);
                    } catch (InterruptedException stop) {
                        Thread.currentThread().interrupt();
                        return null;
                    }
                }
            }
        }
        return null;
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
                compilation = secondary.length() > 0; // compilation, live, DJ mix, ...
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
