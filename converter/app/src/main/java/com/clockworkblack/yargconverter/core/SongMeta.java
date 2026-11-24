package com.clockworkblack.yargconverter.core;

/** Song details shown in YARG's library. */
public final class SongMeta {
    public String title = "";
    public String artist = "";
    public String album = "";
    public String genre = "";
    public String year = "";
    public String charter = "YARG Converter (auto)";

    public String titleOrDefault() {
        return title.trim().isEmpty() ? "Unknown Song" : title.trim();
    }

    public String artistOrDefault() {
        return artist.trim().isEmpty() ? "Unknown Artist" : artist.trim();
    }

    /** Folder name for this song, safe for Android storage. */
    public String folderName() {
        String s = (artistOrDefault() + " - " + titleOrDefault()).replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", "_").trim();
        while (s.endsWith(".")) s = s.substring(0, s.length() - 1).trim();
        if (s.length() > 90) s = s.substring(0, 90).trim();
        return s.isEmpty() ? "Converted Song" : s;
    }
}
