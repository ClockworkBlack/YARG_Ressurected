package com.clockworkblack.yargconverter.core;

import java.util.ArrayList;
import java.util.List;

/** Turns timed lyric lines into the phrase/lyric events YARG shows in its lyric bar. */
public final class LyricsCharter {
    private LyricsCharter() {}

    public static final class Event {
        public final long tick;
        public final String text;

        Event(long tick, String text) {
            this.tick = tick;
            this.text = text;
        }
    }

    public static List<Event> build(List<Lrc.Line> lines, TempoMap map, double duration) {
        List<Event> out = new ArrayList<>();
        long floor = 0;
        for (int i = 0; i < lines.size(); i++) {
            Lrc.Line line = lines.get(i);
            List<String> words = words(line.text);
            if (words.isEmpty()) continue;
            double start = line.time;
            if (start >= duration) break;
            double next = i + 1 < lines.size() ? lines.get(i + 1).time : duration;
            // Line-level timing only tells us when a line starts, so the words are spread over a
            // natural singing pace, squeezed if the next line arrives sooner.
            int chars = 0;
            for (String w : words) chars += w.length() + 1;
            double natural = 0.4 + chars * 0.075;
            double end = Math.min(start + natural, next - 0.12);
            if (end < start + 0.2) end = start + 0.2;

            long startTick = Math.max(floor, map.timeToTick(start));
            long endTick = Math.max(startTick + words.size() + 1, map.timeToTick(end));
            out.add(new Event(startTick, "phrase_start"));
            long span = endTick - startTick;
            long prev = startTick - 1;
            int seen = 0;
            for (String w : words) {
                long t = startTick + span * seen / chars;
                if (t <= prev) t = prev + 1;
                out.add(new Event(t, "lyric " + w));
                prev = t;
                seen += w.length() + 1;
            }
            endTick = Math.max(endTick, prev + 1);
            out.add(new Event(endTick, "phrase_end"));
            floor = endTick + 1;
        }
        return out;
    }

    /** Splits a line into words that are safe to put inside a .chart text event. */
    static List<String> words(String text) {
        List<String> out = new ArrayList<>();
        if (text == null) return out;
        for (String raw : text.trim().split("\\s+")) {
            String w = raw.replace("\"", "").replace("“", "").replace("”", "");
            // These characters are control codes in vocal charts (joins, pitch flags, hidden text).
            w = w.replaceAll("[+#^%$/§_*]+$", "");
            if (w.endsWith("-")) w = w.substring(0, w.length() - 1) + "=";
            if (!w.isEmpty()) out.add(w);
        }
        return out;
    }
}
