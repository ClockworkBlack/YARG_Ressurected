import com.clockworkblack.yargconverter.core.AudioAnalyzer;
import com.clockworkblack.yargconverter.core.Features;
import com.clockworkblack.yargconverter.core.GuitarCharter;
import com.clockworkblack.yargconverter.core.Lrc;
import com.clockworkblack.yargconverter.core.MelodyCharter;
import com.clockworkblack.yargconverter.core.SongWriter;
import com.clockworkblack.yargconverter.core.TempoMap;
import com.clockworkblack.yargconverter.core.VocalAnalyzer;
import com.clockworkblack.yargconverter.core.VocalCharter;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Checks for R&B mode: a guitar chart that follows the lead singer (notes where the words and
 * runs are sung, frets that climb with the tune, sustains on held notes, no chords), a vocals
 * part that follows runs note by note, and the fall-backs when there is too little singing.
 */
final class RnbTest {
    private static int failures;

    private static void check(boolean ok, String what) {
        if (!ok) {
            failures++;
            System.out.println("  FAIL: " + what);
        }
    }

    static int run() {
        failures = 0;
        followsTheSinger();
        runsInTheVocals();
        wholeSong();
        singersFromLyrics();
        namesForLookups();
        // normalModeKeepsPace() measures an experiment that is switched off (VocalCharter.PACE_FROM_SONG);
        // run it by hand when working on word timing without the separated voice.
        return failures;
    }

    public static void main(String[] args) {
        int f = run();
        System.out.println(f == 0 ? "All R&B mode checks passed" : "FAILED: " + f + " R&B mode check(s)");
        if (f > 0) System.exit(1);
    }

    private static TempoMap steadyMap(double seconds, double bpm) {
        int count = (int) (seconds * bpm / 60) + 8;
        double[] beats = new double[count];
        for (int i = 0; i < count; i++) beats[i] = i * 60.0 / bpm;
        return TempoMap.fromBeats(beats, 0, 4);
    }

    private static List<Lrc.Line> lyricFile(SynthVoice song) {
        StringBuilder sb = new StringBuilder();
        for (SynthVoice.Line l : song.lines) sb.append(Lrc.stamp(l.stamp)).append(l.text()).append('\n');
        return Lrc.parse(sb.toString());
    }

    private static VocalAnalyzer.Voice listen(float[] audio, int sampleRate) {
        VocalAnalyzer an = new VocalAnalyzer(sampleRate);
        for (int i = 0; i < audio.length; i += 3000) an.feed(audio, i, Math.min(3000, audio.length - i));
        return an.finish();
    }

    /** A sung event the chart should have a note on: a word starting, or a held word moving to its second note. */
    private static final class Event {
        final double time;
        final int midi;
        final double length;

        Event(double time, int midi, double length) {
            this.time = time;
            this.midi = midi;
            this.length = length;
        }
    }

    private static List<Event> events(SynthVoice song) {
        List<Event> out = new ArrayList<>();
        for (SynthVoice.Line l : song.lines) {
            for (SynthVoice.Word w : l.words) {
                if (w.midi < 0) continue;
                if (w.midi2 >= 0) {
                    double half = (w.start + w.end) / 2;
                    out.add(new Event(w.start, w.midi, half - w.start));
                    out.add(new Event(half, w.midi2, w.end - half));
                } else {
                    out.add(new Event(w.start, w.midi, w.end - w.start));
                }
            }
        }
        return out;
    }

    private static int nearest(List<Event> events, double t, double within) {
        int best = -1;
        double bestGap = within;
        for (int i = 0; i < events.size(); i++) {
            double gap = Math.abs(events.get(i).time - t);
            if (gap <= bestGap) {
                bestGap = gap;
                best = i;
            }
        }
        return best;
    }

    /** The lead-vocal chart puts its notes on the singing and its frets on the shape of the tune. */
    private static void followsTheSinger() {
        System.out.println("R&B mode: a guitar chart that follows the singer");
        int sr = 44100;
        double bpm = 96;
        SynthVoice song = new SynthVoice(sr, "SSLSSLSSLL", 41);
        VocalAnalyzer.Voice voice = listen(song.audio, sr);
        Features vocals = StemTest.analyze(song.audio, sr);
        TempoMap map = steadyMap(song.duration, bpm);
        GuitarCharter.Chart chart = MelodyCharter.build(vocals, voice, map, bpm, null);
        List<Event> events = events(song);

        List<GuitarCharter.Note> expert = chart.notes.get(GuitarCharter.EXPERT);
        // Expert's grid is a sixteenth at 96 BPM: about 156 ms, so a note can be up to ~80 ms from where it was sung
        double within = 0.09;
        int onSinging = 0, chords = 0;
        int[] noteOf = new int[events.size()];
        java.util.Arrays.fill(noteOf, -1);
        for (int i = 0; i < expert.size(); i++) {
            GuitarCharter.Note n = expert.get(i);
            if (n.lane2 >= 0) chords++;
            int e = nearest(events, map.tickToTime(n.tick), within);
            if (e >= 0) {
                onSinging++;
                if (noteOf[e] < 0) noteOf[e] = i;
            }
        }
        int covered = 0, runs = 0, runsCovered = 0;
        for (int e = 0; e < events.size(); e++) {
            if (noteOf[e] >= 0) covered++;
        }
        for (SynthVoice.Line l : song.lines) {
            for (SynthVoice.Word w : l.words) {
                if (w.midi2 < 0) continue;
                runs++;
                int e = nearest(events, (w.start + w.end) / 2, 0.001);
                if (e >= 0 && noteOf[e] >= 0) runsCovered++;
            }
        }

        // Where the tune goes up, the frets go up; where it goes down, they go down
        int moves = 0, agree = 0;
        for (int e = 1; e < events.size(); e++) {
            int a = noteOf[e - 1], b = noteOf[e];
            if (a < 0 || b < 0 || a == b) continue;
            int step = events.get(e).midi - events.get(e - 1).midi;
            if (Math.abs(step) < 2 || events.get(e).time - events.get(e - 1).time > 2.5) continue;
            moves++;
            int lanes = expert.get(b).lane - expert.get(a).lane;
            if (lanes != 0 && Integer.signum(lanes) == Integer.signum(step)) agree++;
        }

        // A note held for half a second or more, with a breath after it, is a sustain. (The first
        // half of a run leads straight into the next note, so it is not held.)
        int long_ = 0, held = 0;
        for (int e = 0; e < events.size(); e++) {
            double end = events.get(e).time + events.get(e).length;
            boolean breathAfter = e + 1 >= events.size() || events.get(e + 1).time - end >= 0.05;
            if (events.get(e).length < 0.5 || noteOf[e] < 0 || !breathAfter) continue;
            long_++;
            if (expert.get(noteOf[e]).sustain > 0) held++;
        }

        System.out.printf(Locale.US, "  %d Expert notes, %d on the singing; %d of %d sung notes charted (%d of %d run notes); frets follow the tune on %d of %d moves; %d of %d held notes sustained%n",
                expert.size(), onSinging, covered, events.size(), runsCovered, runs, agree, moves, held, long_);
        check(chords == 0, "a voice sings one note at a time: no chords (" + chords + ")");
        check(onSinging >= 0.85 * expert.size(), "notes come where the voice sings");
        check(covered >= 0.8 * events.size(), "most sung notes get a guitar note");
        check(runsCovered >= 0.6 * runs, "notes inside held words (runs) are charted");
        check(moves >= 10 && agree >= 0.75 * moves, "frets climb and fall with the tune");
        check(long_ > 0 && held >= 0.6 * long_, "held notes are sustains");
        for (int d = GuitarCharter.EASY; d < GuitarCharter.EXPERT; d++) {
            check(chart.notes.get(d).size() <= chart.notes.get(d + 1).size(), "difficulty " + d + " has no more notes than the next");
        }
        check(chart.notes.get(GuitarCharter.EASY).size() > 0, "Easy has notes");
    }

    /**
     * A sung tone at a sequence of notes, with no gap between them (one held word moving through
     * a run), as a sum of harmonics with a soft start and end.
     */
    private static void sing(float[] out, int sr, double start, double noteSec, int[] notes) {
        double phase = 0;
        int a = (int) (start * sr);
        int n = (int) (noteSec * notes.length * sr);
        for (int i = 0; i < n && a + i < out.length; i++) {
            int k = Math.min(notes.length - 1, (int) (i / (noteSec * sr)));
            double hz = 440.0 * Math.pow(2, (notes[k] - 69) / 12.0);
            phase += 2 * Math.PI * hz / sr;
            double env = Math.min(1.0, Math.min(i, n - i) / (0.02 * sr));
            double v = 0;
            for (int h = 1; h <= 8; h++) v += Math.sin(h * phase) / (h * 1.3);
            out[a + i] += (float) (0.12 * env * v);
        }
    }

    /** R&B mode follows a word through a run of several notes, where the usual mode stops early. */
    private static void runsInTheVocals() {
        System.out.println("R&B mode: runs in the vocals part");
        int sr = 44100;
        // Eight lines of three words. Long words run through five notes; short ones move once.
        int[][] runShapes = {{60, 62, 64, 62, 60}, {64, 62, 60, 59, 57}, {57, 60, 62, 64, 67}, {67, 65, 64, 62, 60}};
        double t = 1.0;
        StringBuilder lrc = new StringBuilder();
        List<double[]> plan = new ArrayList<>();
        int changes = 0;
        for (int line = 0; line < 8; line++) {
            lrc.append(Lrc.stamp(Math.max(0, t - 0.05)));
            double lineStart = t;
            for (int w = 0; w < 3; w++) {
                boolean longWord = w != 1;
                int[] notes = longWord ? runShapes[(line + w) % runShapes.length] : new int[] {62, 65};
                double noteSec = longWord ? 0.3 : 0.2;
                plan.add(new double[] {t, noteSec, notes.length, line * 3 + w});
                changes += notes.length - 1;
                lrc.append(w == 0 ? "" : " ").append(longWord ? "ooh" : "yeah");
                t += noteSec * notes.length + 0.08;
            }
            lrc.append('\n');
            t += 1.2;
        }
        double duration = t + 1.0;
        float[] audio = new float[(int) (duration * sr)];
        int i = 0;
        for (double[] p : plan) {
            int line = (int) p[3] / 3, w = (int) p[3] % 3;
            boolean longWord = w != 1;
            sing(audio, sr, p[0], p[1], longWord ? runShapes[(line + w) % runShapes.length] : new int[] {62, 65});
            i++;
        }
        VocalAnalyzer.Voice voice = listen(audio, sr);
        TempoMap map = steadyMap(duration, 90);
        List<Lrc.Line> lyrics = Lrc.parse(lrc.toString());
        VocalCharter.Chart usual = VocalCharter.build(lyrics, map, duration, voice);
        VocalCharter.Chart rnb = VocalCharter.build(lyrics, map, duration, voice, true);
        int[] slides = new int[2];
        int[] words = new int[2];
        VocalCharter.Chart[] charts = {usual, rnb};
        for (int c = 0; c < 2; c++) {
            for (VocalCharter.Phrase p : charts[c].phrases) {
                for (VocalCharter.Note n : p.notes) {
                    if (n.slide) slides[c]++;
                    else words[c]++;
                }
            }
        }
        System.out.printf(Locale.US, "  %d note changes sung; usual: %d words, %d joined-on notes; R&B mode: %d words, %d joined-on notes%n",
                changes, words[0], slides[0], words[1], slides[1]);
        check(words[0] == words[1], "the same words either way");
        check(rnb.sungLines == usual.sungLines && rnb.sungLines >= 6, "the lines count as sung");
        check(slides[1] >= slides[0] + changes / 5, "R&B mode follows clearly more of the runs");
        check(slides[1] >= 0.7 * changes, "R&B mode follows most note changes of the runs");
        check(slides[1] <= changes + 4, "R&B mode does not invent notes");
    }

    /**
     * Normal mode has no separated voice to listen to. The words must still keep pace with the
     * song: timed from the lyric file's own stamps and moved onto the sounds of the song.
     */
    private static void normalModeKeepsPace() {
        System.out.println("Normal mode: words keep pace with the song");
        int[] seeds = {51, 52, 53};
        String[] plans = {"SSSSSSSSSS", "RRRRRRRRRR", "SSRRSSRRSS"};
        for (int t = 0; t < plans.length; t++) {
            SynthVoice song = new SynthVoice(44100, plans[t], seeds[t]);
            // Lines that follow straight on, as in a real lyric file: each stamp is where the line starts
            StringBuilder sb = new StringBuilder();
            List<Double> starts = new ArrayList<>();
            for (SynthVoice.Line l : song.lines) {
                sb.append(Lrc.stamp(l.stamp)).append(l.text()).append('\n');
                for (SynthVoice.Word w : l.words) starts.add(w.start);
            }
            List<Lrc.Line> lyrics = Lrc.parse(sb.toString());
            // What the full song gives: a sound starting on every word, and as many again elsewhere (the band)
            List<Double> all = new ArrayList<>(starts);
            java.util.Random rnd = new java.util.Random(seeds[t]);
            for (int i = 0; i < starts.size(); i++) all.add(rnd.nextDouble() * song.duration);
            java.util.Collections.sort(all);
            double[] onsets = new double[all.size()];
            for (int i = 0; i < onsets.length; i++) onsets[i] = all.get(i);

            TempoMap map = steadyMap(song.duration, 100);
            int[] close = new int[2];
            int[] lastInTime = new int[2];
            int words = 0, lines = 0;
            for (int mode = 0; mode < 2; mode++) {
                VocalCharter.Chart chart = VocalCharter.build(lyrics, map, song.duration, null, false, mode == 0 ? null : onsets);
                words = 0;
                lines = 0;
                int at = 0;
                for (int i = 0; i < song.lines.size(); i++) {
                    List<VocalCharter.Note> notes = new ArrayList<>();
                    for (VocalCharter.Phrase p : chart.phrases) if (p.line == i) notes.addAll(p.notes);
                    SynthVoice.Line line = song.lines.get(i);
                    lines++;
                    for (int k = 0; k < line.words.size() && k < notes.size(); k++) {
                        words++;
                        double placed = map.tickToTime(notes.get(k).tick);
                        if (Math.abs(placed - line.words.get(k).start) <= 0.15) close[mode]++;
                        if (k == line.words.size() - 1 && Math.abs(placed - line.words.get(k).start) <= 0.35) lastInTime[mode]++;
                    }
                }
            }
            System.out.printf(Locale.US, "  %s: %d words; within 0.15 s of where they are sung: %d by pace alone, %d moved onto the song; last word of a line in time on %d of %d lines%n",
                    plans[t], words, close[0], close[1], lastInTime[1], lines);
            check(close[1] >= close[0], "moving words onto the song's sounds does not make them worse");
            check(close[1] >= 0.5 * words, "at least half the words land where they are sung");
            check(lastInTime[1] >= 0.6 * lines, "a line's last word is still in time, so the words have not run ahead or behind");
        }
    }

    /** Who sings what, read from the lyrics themselves: section headers of the plain text carried over to the timed text. */
    private static void singersFromLyrics() {
        System.out.println("Singers from the lyrics' own headers");
        String plain = "[Intro]\nYeah, yeah\n\n[Verse 1: Lead One]\nFirst line of mine\nSecond line of mine\n\n"
                + "[Chorus: Lead One & Guest Two]\nSing it all together\n\n[Verse 2: Guest Two]\nNow the guest comes in\nGuest keeps going on\n\n"
                + "[Chorus: Lead One & Guest Two]\nSing it all together\n\n[Outro: Lead One]\nFirst line of mine\n";
        String synced = "[00:01.00]Yeah, yeah\n[00:05.00]First line of mine\n[00:08.00]Second line of mine\n[00:11.00]Sing it all together\n"
                + "[00:15.00]Now the guest comes in\n[00:18.00]Guest keeps going on!\n[00:21.00]\n[00:22.00]Sing it all together\n[00:26.00]First line of mine\n";

        List<String> names = Lrc.findSingers("A Song", plain, "Lead One");
        check(names.size() == 2 && names.get(0).equals("Lead One") && names.get(1).equals("Guest Two"), "the guest is found in the headers when the artist names only one: " + names);
        check(Lrc.findSingers("A Song", plain, "Lead One", "Lead One feat. Somebody Else").get(1).equals("Somebody Else"), "an artist spelling that names two wins over the headers");
        check(Lrc.findSingers("A Song (feat. Guest Two)", null, "Lead One").size() == 2, "a feature in the title counts");
        check(Lrc.findSingers("A Song", "[Verse 1]\nwords\n[Chorus]\nwords", "Lead One").size() == 1, "headers that name nobody add nobody");

        String marked = Lrc.markFromHeaders(synced, plain, names);
        check(marked != null, "the timed lyrics get marks");
        if (marked != null) {
            List<Lrc.Line> lines = Lrc.parse(marked);
            int[] want = {Lrc.FIRST, Lrc.FIRST, Lrc.FIRST, Lrc.BOTH, Lrc.SECOND, Lrc.SECOND, -99, Lrc.BOTH, Lrc.FIRST};
            boolean right = lines.size() == want.length;
            for (int i = 0; right && i < want.length; i++) {
                if (want[i] != -99 && lines.get(i).singer != want[i]) right = false;
            }
            check(right, "each line goes to the singer its header names, the repeated chorus and the repeated line included:\n" + marked);
            check(Math.abs(lines.get(4).time - 15.0) < 0.01, "the times are kept");
        }
        check(Lrc.markFromHeaders(synced, "[Verse 1]\nFirst line of mine\n[Chorus]\nSing it all together", names) == null, "plain lyrics that name nobody change nothing");
        check(Lrc.markFromHeaders(synced, "[Verse 1: Lead One]\nCompletely other words\n[Verse 2: Guest Two]\nNothing alike at all", names) == null, "lyrics of a different song are not trusted");
        check(Lrc.markFromHeaders(synced, plain, names.subList(0, 1)) == null, "one artist: nothing to split");
    }

    /** Tidying titles and artists so a lookup finds the song, and telling the right result from a namesake. */
    private static void namesForLookups() {
        System.out.println("Names for looking a song up");
        String[][] titles = {
                {"Just Be A Man About It (Official Video)", "Just Be A Man About It"},
                {"03 - No Pole [Official Audio]", "No Pole"},
                {"After Party (feat. Offset)", "After Party"},
                {"Superstition - Remastered 2011", "Superstition"},
                {"07. What's Going On.mp3", "What's Going On"},
                {"Lovin_You_(Lyrics)", "Lovin You"},
                {"Sittin' On The Dock Of The Bay (Live) [HD]", "Sittin' On The Dock Of The Bay"},
                {"(Sittin' On) The Dock of the Bay", "(Sittin' On) The Dock of the Bay"},
                {"No Scrubs ft. Someone Else", "No Scrubs"},
                {"1999", "1999"},
        };
        for (String[] t : titles) {
            String got = com.clockworkblack.yargconverter.core.SongNames.cleanTitle(t[0]);
            check(got.equals(t[1]), "\"" + t[0] + "\" is looked up as \"" + t[1] + "\" (got \"" + got + "\")");
        }
        check(com.clockworkblack.yargconverter.core.SongNames.primaryArtist("Don Toliver feat. Travis Scott & Offset").equals("Don Toliver"), "the main artist of three");

        List<com.clockworkblack.yargconverter.core.SongNames.Query> qs =
                com.clockworkblack.yargconverter.core.SongNames.queries("Toni Braxton - Just Be A Man About It (Official Video)", "", "Toni Braxton - Just Be A Man About It (Official Video).mp3");
        boolean split = false, titleOnly = false;
        for (com.clockworkblack.yargconverter.core.SongNames.Query q : qs) {
            if (q.title.equals("Just Be A Man About It") && q.artist.equals("Toni Braxton")) split = true;
            if (q.artist.isEmpty()) titleOnly = true;
        }
        check(split, "an untagged \"Artist - Title\" file is asked for by title and artist");
        check(titleOnly && qs.size() >= 3 && qs.size() <= 8, "several ways of asking, a title-only one among them (" + qs.size() + ")");

        com.clockworkblack.yargconverter.core.SongNames.Query q = qs.get(0);
        for (com.clockworkblack.yargconverter.core.SongNames.Query each : qs) if (each.title.equals("Just Be A Man About It") && each.artist.equals("Toni Braxton")) q = each;
        check(com.clockworkblack.yargconverter.core.SongNames.fit(q, "Just Be a Man About It", "Toni Braxton", 289, 289) >= 4, "the same song fits");
        check(com.clockworkblack.yargconverter.core.SongNames.fit(q, "Just Be A Man About It", "Toni Braxton feat. Dr. Dre", 289, 0) >= 3, "... with a guest credited too");
        check(com.clockworkblack.yargconverter.core.SongNames.fit(q, "Just Be A Man About It", "Some Cover Band", 289, 200) == 0, "a namesake by someone else, of another length, does not");
        check(com.clockworkblack.yargconverter.core.SongNames.fit(q, "Another Song Entirely", "Toni Braxton", 289, 289) == 0, "another song by the same artist does not");
        com.clockworkblack.yargconverter.core.SongNames.Query bare = qs.get(qs.size() - 1);
        check(bare.artist.isEmpty() && bare.title.equals("Just Be A Man About It"), "the last resort is the title alone: \"" + bare.title + "\"");
        check(com.clockworkblack.yargconverter.core.SongNames.fit(bare, "Just Be A Man About It", "Anyone", 289, 288) > 0
                && com.clockworkblack.yargconverter.core.SongNames.fit(bare, "Just Be A Man About It", "Anyone", 289, 200) == 0, "by title alone, the length has to agree");

        // More than two artists: the main one, and the guests together
        List<String> three = Lrc.artists("Lead One feat. Guest Two & Guest Three", "A Song");
        check(three.size() == 2 && three.get(0).equals("Lead One") && three.get(1).equals("Guest Two & Guest Three"), "three artists become the lead and the guests together: " + three);
        check(Lrc.allArtists("A, B & C feat. D", null).size() == 4, "every artist is found");
        String plain = "[Verse 1: Lead One]\nLine one here\n[Verse 2: Guest Two]\nLine two here\n[Verse 3: Guest Three]\nLine three here\n[Outro: Lead One & Guest Three]\nLine four here\n";
        String synced = "[00:01.00]Line one here\n[00:05.00]Line two here\n[00:09.00]Line three here\n[00:13.00]Line four here\n";
        String marked = Lrc.markFromHeaders(synced, plain, three);
        check(marked != null, "three singers' headers are used");
        if (marked != null) {
            List<Lrc.Line> lines = Lrc.parse(marked);
            check(lines.get(0).singer == Lrc.FIRST && lines.get(1).singer == Lrc.SECOND && lines.get(2).singer == Lrc.SECOND && lines.get(3).singer == Lrc.BOTH,
                    "either guest's lines go to the guests' part, a line shared with the lead to both:\n" + marked);
        }
        List<String> fromHeaders = Lrc.findSingers("A Song", plain, "Lead One");
        check(fromHeaders.size() == 2 && fromHeaders.get(1).equals("Guest Two & Guest Three"), "guests named only in the lyrics' headers are found together: " + fromHeaders);
    }

    /** Through the whole converter: R&B mode is used when there is singing, and explained when it cannot be. */
    private static void wholeSong() {
        System.out.println("R&B mode through the whole converter");
        SynthBand band = new SynthBand(44100, 96.0, 0.0, 0.3, 4, 7);
        AudioAnalyzer an = new AudioAnalyzer(band.sampleRate);
        for (int i = 0; i < band.audio.length; i += 4096) an.feed(band.audio, i, Math.min(4096, band.audio.length - i));
        Features mix = an.finish();
        SynthVoice song = new SynthVoice(44100, "SSLSSLSSLSSLSSLSS", 47);

        SongWriter.Stems stems = new SongWriter.Stems();
        stems.guitar = StemTest.analyze(band.guitarTrack, band.sampleRate);
        stems.melodic = stems.guitar;
        stems.bass = StemTest.analyze(band.bassTrack, band.sampleRate, true);
        stems.guitarEnergy = StemTest.energy(band.guitarTrack);
        stems.melodicEnergy = stems.guitarEnergy;
        stems.bassEnergy = StemTest.energy(band.bassTrack);
        stems.mixEnergy = StemTest.energy(band.audio);
        stems.voice = listen(song.audio, 44100);
        stems.vocals = StemTest.analyze(song.audio, 44100);
        stems.rnb = true;
        SongWriter.Result r = SongWriter.analyze(mix, lyricFile(song), null, stems);
        check(r.rnb && r.rnbNote == null, "the guitar chart follows the singer (" + r.guitarSource + ")");
        check(r.guitarSource.contains("vocal"), "the summary says it was made from the voice");
        check(r.bassChart != null, "the bass chart still follows the bass");
        check(r.noteCount(GuitarCharter.EXPERT) > 0, "the guitar chart has notes");

        // Without the measured vocals the instruments are charted, and the user is told why
        stems.vocals = null;
        SongWriter.Result none = SongWriter.analyze(mix, lyricFile(song), null, stems);
        check(!none.rnb && none.rnbNote != null, "without the vocals the usual chart is kept, with a note why");

        // An instrumental: nothing to follow
        float[] silence = new float[song.audio.length];
        stems.voice = listen(silence, 44100);
        stems.vocals = StemTest.analyze(silence, 44100);
        SongWriter.Result instrumental = SongWriter.analyze(mix, new ArrayList<Lrc.Line>(), null, stems);
        check(!instrumental.rnb && instrumental.rnbNote != null && instrumental.noteCount(GuitarCharter.EXPERT) > 0,
                "an instrumental keeps the chart from the instruments");
        System.out.println("  with singing: " + r.guitarSource + "; without: " + none.rnbNote + "; instrumental: " + instrumental.rnbNote);
    }
}
