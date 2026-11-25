import com.clockworkblack.yargconverter.core.*;

import java.util.ArrayList;
import java.util.List;

/**
 * Lyrics timed for one cut of a song, used with another: the aligner must hear where the words
 * really are, and must leave well-timed lyrics alone.
 */
final class AlignTest {
    private AlignTest() {}

    private static int failures;

    private static void check(boolean ok, String what) {
        if (!ok) {
            failures++;
            System.out.println("  FAILED: " + what);
        }
    }

    static int run() {
        failures = 0;
        longerIntro();
        startCutOff();
        breakInTheMiddle();
        wellTimedLeftAlone();
        wrongLyricsLeftAlone();
        silentStartWithoutTheVoice();
        otherLanguages();
        lineByLine();
        wordByWord();
        System.out.println(failures == 0 ? "All lyric timing checks passed" : "FAILED: " + failures + " lyric timing check(s)");
        return failures;
    }

    private static final String PLAN = "SSRRssrrSRsrSSRrSSrr";

    private static List<Lrc.Line> lyricsOf(SynthVoice song) {
        List<Lrc.Line> lines = new ArrayList<>();
        for (SynthVoice.Line l : song.lines) lines.add(new Lrc.Line(l.stamp, l.text()));
        return lines;
    }

    /** The same audio with {@code seconds} of faint hum put in at {@code at} seconds. */
    private static float[] insert(float[] audio, int sr, double at, double seconds) {
        int where = (int) (at * sr), extra = (int) (seconds * sr);
        float[] out = new float[audio.length + extra];
        System.arraycopy(audio, 0, out, 0, where);
        java.util.Random rnd = new java.util.Random(5);
        for (int i = 0; i < extra; i++) {
            double ph = 2 * Math.PI * i / sr;
            out[where + i] = (float) (0.004 * Math.sin(ph * 147) + 0.002 * (rnd.nextDouble() - 0.5));
        }
        System.arraycopy(audio, where, out, where + extra, audio.length - where);
        return out;
    }

    /** A music video: a scene before the song starts. The lyric file knows nothing of it. */
    private static void longerIntro() {
        int sr = 22050;
        for (double extra : new double[] {3.2, 12.4, 41.0}) {
            SynthVoice song = new SynthVoice(sr, PLAN, 31);
            VocalAnalyzer.Voice voice = VocalTest.listen(insert(song.audio, sr, 0.5, extra), sr);
            LyricAligner.Result r = LyricAligner.align(lyricsOf(song), voice);
            check(r.moved(), "lyrics are moved for an intro " + extra + " s longer");
            check(Math.abs(r.shift - extra) <= 0.3, "moved by the length of the extra intro (" + r.shift + " for " + extra + ")");
            check(r.changeLine < 0, "the whole song moved as one");
            check(r.lines.size() == song.lines.size(), "every line kept");
            // And the words now land where they are sung
            int onTime = 0;
            for (int i = 0; i < r.lines.size(); i++) {
                double truth = song.lines.get(i).start + extra;
                double stamp = r.lines.get(i).time;
                if (stamp <= truth + 0.3 && stamp >= truth - 0.6) onTime++;
            }
            check(onTime == r.lines.size(), "each line starts where it is sung (" + onTime + " of " + r.lines.size() + ")");
        }
    }

    /** The audio starts part-way into the recording the lyrics were timed to. */
    private static void startCutOff() {
        int sr = 22050;
        SynthVoice song = new SynthVoice(sr, PLAN, 32);
        VocalAnalyzer.Voice voice = VocalTest.listen(song.audio, sr);
        List<Lrc.Line> late = new ArrayList<>();
        for (SynthVoice.Line l : song.lines) late.add(new Lrc.Line(l.stamp + 6.5, l.text()));
        LyricAligner.Result r = LyricAligner.align(late, voice);
        check(r.moved() && Math.abs(r.shift + 6.5) <= 0.3, "lyrics that come late are moved earlier (" + r.shift + ")");
    }

    /** A break in the middle that the album does not have. */
    private static void breakInTheMiddle() {
        int sr = 22050;
        SynthVoice song = new SynthVoice(sr, PLAN, 33);
        int half = 11;
        double at = song.lines.get(half).start - 0.5;
        float[] audio = insert(song.audio, sr, at, 9.0);
        VocalAnalyzer.Voice voice = VocalTest.listen(audio, sr);
        LyricAligner.Result r = LyricAligner.align(lyricsOf(song), voice);
        check(r.moved(), "a break in the middle is noticed");
        check(Math.abs(r.shift) < 0.01, "the part before the break is left alone (" + r.shift + ")");
        check(r.changeLine == half, "the change is at the line after the break (" + r.changeLine + ")");
        check(Math.abs(r.laterShift - 9.0) <= 0.3, "the part after it is moved by the break's length (" + r.laterShift + ")");

        // Both at once: a longer intro and a break
        VocalAnalyzer.Voice both = VocalTest.listen(insert(audio, sr, 0.5, 7.0), sr);
        r = LyricAligner.align(lyricsOf(song), both);
        check(r.moved() && Math.abs(r.shift - 7.0) <= 0.3 && r.changeLine == half && Math.abs(r.laterShift - 16.0) <= 0.3,
                "an intro and a break together (" + r.shift + ", line " + r.changeLine + ", " + r.laterShift + ")");
    }

    private static void wellTimedLeftAlone() {
        int sr = 22050;
        for (long seed = 40; seed < 46; seed++) {
            SynthVoice song = new SynthVoice(sr, PLAN, seed);
            VocalAnalyzer.Voice voice = VocalTest.listen(song.audio, sr);
            List<Lrc.Line> lines = lyricsOf(song);
            LyricAligner.Result r = LyricAligner.align(lines, voice);
            check(!r.moved() && r.lines == lines, "well-timed lyrics are left alone (seed " + seed + ": " + r.note + ")");
            // Stamps that are all a little early or late are the charter's business, not a different cut
            List<Lrc.Line> early = new ArrayList<>();
            for (Lrc.Line l : lines) early.add(new Lrc.Line(Math.max(0, l.time - 0.3), l.text));
            check(!LyricAligner.align(early, voice).moved(), "slightly early lyrics are left alone (seed " + seed + ")");
        }
    }

    /** Lyrics of some other song: there is no place where they fit, so nothing may be moved. */
    private static void wrongLyricsLeftAlone() {
        int sr = 22050;
        SynthVoice song = new SynthVoice(sr, PLAN, 50);
        VocalAnalyzer.Voice voice = VocalTest.listen(song.audio, sr);
        SynthVoice other = new SynthVoice(sr, "RrSsLLRRssSRrrSLsSRL", 51);
        LyricAligner.Result r = LyricAligner.align(lyricsOf(other), voice);
        check(!r.moved(), "lyrics of another song are not moved about (" + r.note + ")");

        List<Lrc.Line> few = new ArrayList<>();
        for (int i = 0; i < 4; i++) few.add(new Lrc.Line(song.lines.get(i).stamp + 5, song.lines.get(i).text()));
        check(!LyricAligner.align(few, voice).moved(), "too few lines to judge by");
        check(!LyricAligner.align(lyricsOf(song), null).moved(), "no voice, nothing moved");
    }

    /** Normal mode: only silence at the start of the file can be told. */
    private static void silentStartWithoutTheVoice() {
        int sr = 22050;
        SynthBand band = new SynthBand(sr, 118.0, 0.0, 0.05, 4, 7);
        List<Lrc.Line> lines = new ArrayList<>();
        for (int i = 0; i < 8; i++) lines.add(new Lrc.Line(2 + i * 3, "la la la"));

        Features plain = StemTest.analyze(band.audio, sr);
        check(!LyricAligner.alignToStart(lines, plain).moved(), "a song that starts at once is left alone");

        float[] padded = new float[band.audio.length + 5 * sr];
        System.arraycopy(band.audio, 0, padded, 5 * sr, band.audio.length);
        LyricAligner.Result r = LyricAligner.alignToStart(lines, StemTest.analyze(padded, sr));
        check(r.moved() && Math.abs(r.shift - 4.7) <= 0.35, "five seconds of silence moves the lyrics later (" + r.shift + ")");
        check(r.lines.size() == lines.size() && Math.abs(r.lines.get(0).time - (2 + r.shift)) < 1e-6, "every line moved by the same amount");

        // By hand: half a second later, and marked so the converter leaves it be
        String text = "[00:10.00]v1: Hello there\n[00:12.50]\n[00:13.00]v2: Again";
        check(!Lrc.keepsTiming(text), "plain lyrics may be re-timed");
        String moved = Lrc.shiftText(text, 0.5);
        check(Lrc.keepsTiming(moved), "lyrics moved by hand keep their times");
        List<Lrc.Line> back = Lrc.parse(moved);
        check(back.size() == 3 && Math.abs(back.get(0).time - 10.5) < 0.011 && back.get(0).singer == Lrc.FIRST
                && back.get(2).singer == Lrc.SECOND && back.get(1).text.isEmpty(), "moved by hand: times, singers and gaps kept");
        check(Math.abs(Lrc.parse(Lrc.shiftText(moved, -0.5)).get(2).time - 13.0) < 0.011, "and back again");
        check(Lrc.keepsTiming("[offset:500]\n[00:01.00]x"), "an [offset:] line also means the times are meant");
        check(Lrc.shiftText("no times here", 1).equals("no times here"), "lyrics without times are not touched");
        List<Lrc.Line> labelled = Lrc.parse("[00:12.09][Hook]\n[00:32.07]I know\n[01:02.55][Verse 2: Somebody]\n[01:05.00](Yeah yeah)");
        check(labelled.size() == 4 && labelled.get(0).text.isEmpty() && labelled.get(2).text.isEmpty(), "timed section labels are gaps, not sung words");
        check(labelled.get(3).text.equals("(Yeah yeah)"), "an ad-lib in brackets is still sung");
    }

    /** Korean, Japanese and Chinese lyrics: read, counted and cut into notes properly. */
    private static void otherLanguages() {
        try {
            String korean = "[00:12.30]\uC0AC\uB791\uD574\uC694 \uADF8\uB300\uB97C\n[00:15.00]\uB108\uB97C \uBCF4\uBA74";
            check(Lrc.decode(korean.getBytes("UTF-8")).equals(korean), "UTF-8 lyrics read as they are");
            check(Lrc.decode(("\uFEFF" + korean).getBytes("UTF-8")).equals(korean), "a byte-order mark is dropped");
            check(Lrc.decode(korean.getBytes("EUC-KR")).equals(korean), "Korean lyrics in EUC-KR");
            check(Lrc.decode(("\uFEFF" + korean).getBytes("UTF-16LE")).equals(korean), "lyrics in UTF-16");
            String japanese = "[00:01.00]\u304D\u307F\u306E\u306A\u307E\u3048\u306F\u3000\u5922\u306E\u4E2D";
            check(Lrc.decode(japanese.getBytes("Shift_JIS")).equals(japanese), "Japanese lyrics in Shift-JIS");
            String chinese = "[00:01.00]\u6211\u7231\u4F60\u4E2D\u56FD \u4EB2\u7231\u7684\u6BCD\u4EB2";
            check(Lrc.decode(chinese.getBytes("GBK")).equals(chinese), "Chinese lyrics in GBK");
            String russian = "[00:01.00]\u041F\u0440\u0438\u0432\u0435\u0442 \u043C\u043E\u0439 \u0434\u0440\u0443\u0433";
            check(Lrc.decode(russian.getBytes("windows-1251")).equals(russian), "Russian lyrics in Windows-1251");
            String french = "[00:01.00]D\u00E9j\u00E0 vu, mon c\u0153ur";
            check(Lrc.decode(french.getBytes("windows-1252")).equals(french), "Western lyrics in Windows-1252");
            check(Lrc.parse(Lrc.decode(("\uFEFF" + korean).getBytes("UTF-8"))).size() == 2, "the first line of a file with a byte-order mark is not lost");
        } catch (java.io.UnsupportedEncodingException e) {
            check(false, "encodings available: " + e.getMessage());
        }

        List<Lrc.Line> loose = Lrc.parse("[00:01.00]\u1112\u1161\u11AB cafe\u0301");
        check(loose.size() == 1 && loose.get(0).text.equals("\uD55C caf\u00E9"), "loose Korean parts and separate accents are put together");
        check(VocalCharter.syllables("\uC0AC\uB791\uD574\uC694") == 4, "a Korean word has a syllable per block");
        check(VocalCharter.syllables("love") == 1 && VocalCharter.syllables("remember") == 3, "English as before");
        check(VocalCharter.syllables("\u043C\u043E\u043B\u043E\u043A\u043E") == 3, "Russian vowels counted");
        check(VocalCharter.syllables("caf\u00E9") == 2, "accented vowels counted");

        List<String> k = VocalCharter.words("\uC0AC\uB791\uD574\uC694 \uADF8\uB300\uB97C");
        check(k.size() == 2 && k.get(0).equals("\uC0AC\uB791\uD574\uC694"), "Korean words are kept whole: " + k);
        List<String> j = VocalCharter.words("\u304D\u307F\u306E\u306A\u307E\u3048\u306F\u3000\u5922\u306E\u4E2D");
        check(j.toString().equals("[\u304D\u307F-, \u306E\u306A-, \u307E\u3048-, \u306F, \u5922\u306E\u4E2D]"), "an unspaced Japanese line is cut into joined pieces: " + j);
        List<String> c = VocalCharter.words("\u6211\u7231\u4F60\uFF0C\u4E2D\u56FD");
        check(c.toString().equals("[\u6211\u7231-, \u4F60\uFF0C\u4E2D-, \u56FD]"), "punctuation stays with the character before it: " + c);
        check(VocalCharter.words("Hello world").toString().equals("[Hello, world]"), "English as before");
    }

    /** Single lines stamped wrong, as lyric sites often have them; and lyrics that are right left alone. */
    private static void lineByLine() {
        int sr = 22050;
        int moved = 0, lines = 0;
        for (long seed = 60; seed < 66; seed++) {
            SynthVoice song = new SynthVoice(sr, PLAN, seed);
            VocalAnalyzer.Voice voice = VocalTest.listen(song.audio, sr);
            List<Lrc.Line> right = lyricsOf(song);
            LyricAligner.Result r = LyricAligner.checkLines(right, voice);
            for (int i = 0; i < right.size(); i++) {
                lines++;
                if (Math.abs(r.lines.get(i).time - right.get(i).time) > 0.15) moved++;
            }
        }
        check(moved == 0, "lyrics that are right stay where they are (" + moved + " of " + lines + " lines moved)");

        // One line stamped seconds late, crammed against the next: back to where it is sung
        int fixedLate = 0, triedLate = 0;
        for (long seed = 70; seed < 76; seed++) {
            SynthVoice song = new SynthVoice(sr, PLAN, seed);
            VocalAnalyzer.Voice voice = VocalTest.listen(song.audio, sr);
            for (int k = 3; k < song.lines.size() - 2; k += 4) {
                List<Lrc.Line> lyr = lyricsOf(song);
                double next = lyr.get(k + 1).time;
                lyr.set(k, new Lrc.Line(next - 0.45, lyr.get(k).text));
                LyricAligner.Result r = LyricAligner.checkLines(lyr, voice);
                triedLate++;
                if (Math.abs(r.lines.get(k).time - song.lines.get(k).start) <= 0.3) fixedLate++;
                check(Math.abs(r.lines.get(k + 1).time - next) < 0.15, "the line after a late one stays (seed " + seed + ", line " + k + ")");
            }
        }
        check(fixedLate >= triedLate * 0.75, "late lines crammed against the next are put back (" + fixedLate + " of " + triedLate + ")");

        // Stamped part-way into its own singing: back to where the singing began
        int fixedIn = 0, triedIn = 0;
        for (long seed = 80; seed < 84; seed++) {
            SynthVoice song = new SynthVoice(sr, PLAN, seed);
            VocalAnalyzer.Voice voice = VocalTest.listen(song.audio, sr);
            List<Lrc.Line> lyr = lyricsOf(song);
            for (int k = 1; k < lyr.size(); k += 3) lyr.set(k, new Lrc.Line(song.lines.get(k).start + 0.6, lyr.get(k).text));
            LyricAligner.Result r = LyricAligner.checkLines(lyr, voice);
            for (int k = 1; k < lyr.size(); k += 3) {
                triedIn++;
                if (Math.abs(r.lines.get(k).time - song.lines.get(k).start) <= 0.15) fixedIn++;
            }
        }
        check(fixedIn >= triedIn * 0.8, "lines stamped into their singing start where it starts (" + fixedIn + " of " + triedIn + ")");
        System.out.println("  line by line: late lines fixed " + fixedLate + "/" + triedLate + ", stamped into singing fixed " + fixedIn + "/" + triedIn);
    }

    /** What a speech model would say for these lines sung at these times: each letter likely for a moment, silence between. */
    private static float[][] heard(List<Lrc.Line> lines, double[] start, double seconds, java.util.Random rnd) {
        int frames = (int) (seconds / WordAligner.FRAME_SEC);
        float[][] e = new float[frames][WordAligner.SYMBOLS];
        for (float[] row : e) {
            java.util.Arrays.fill(row, -9f);
            row[0] = -0.01f;
        }
        for (int i = 0; i < lines.size(); i++) {
            int f = (int) Math.round(start[i] / WordAligner.FRAME_SEC);
            for (int symbol : WordAligner.symbols(lines.get(i).text)) {
                int hold = 1 + rnd.nextInt(3);
                for (int h = 0; h < hold && f < frames; h++, f++) {
                    java.util.Arrays.fill(e[f], -9f);
                    e[f][symbol] = -0.05f;
                }
                // Between letters: mostly nothing, now and then straight on
                f += rnd.nextInt(4);
            }
        }
        return e;
    }

    /** Letters matched to the voice, and the song's repeats used to catch lines that were misheard. */
    private static void wordByWord() {
        java.util.Random rnd = new java.util.Random(9);
        String[] hook = {"round and round we go", "nobody knows where", "keep it turning now", "till the morning light"};
        String[] verse = {"walking down the avenue", "nothing left to lose tonight", "all the windows shining", "a hundred miles of road"};
        List<Lrc.Line> lines = new ArrayList<>();
        List<Double> when = new ArrayList<>();
        double bar = 2.0;
        double[] hookAt = {0.1, 3.6, 7.8, 11.5};
        for (int pass = 0; pass < 3; pass++) {
            double base = 8 + pass * 48;
            for (int k = 0; k < 4; k++) {
                // Lyric files are a little off, sometimes a lot
                lines.add(new Lrc.Line(base + hookAt[k] + (k == 2 ? 3.0 : 0.2), hook[k]));
                when.add(base + hookAt[k]);
            }
            for (int k = 0; k < 4; k++) {
                lines.add(new Lrc.Line(base + 16 + 4 * k + 0.4, verse[k] + (pass == 0 ? "" : " again")));
                when.add(base + 16 + 4 * k);
            }
            for (int k = 0; k < 4; k++) {
                lines.add(new Lrc.Line(base + 32 + hookAt[k] + 0.2, hook[k] + (k == 3 ? " (oh yeah)" : "")));
                when.add(base + 32 + hookAt[k]);
            }
        }
        double[] truth = new double[when.size()];
        for (int i = 0; i < truth.length; i++) truth[i] = when.get(i);
        float[][] e = heard(lines, truth, 160, rnd);

        check(WordAligner.canAlign(lines), "English lyrics can be aligned");
        List<Lrc.Line> korean = new ArrayList<>();
        for (int i = 0; i < 8; i++) korean.add(new Lrc.Line(i * 3, "\uC0AC\uB791\uD574\uC694 \uADF8\uB300\uB97C"));
        check(!WordAligner.canAlign(korean), "lyrics in another script are left to the voice-only check");

        WordAligner.Aligned a = WordAligner.align(lines, e);
        int right = 0;
        for (int i = 0; i < truth.length; i++) if (Math.abs(a.start[i] - truth[i]) <= 0.05) right++;
        check(right == truth.length, "every line found where its letters are (" + right + " of " + truth.length + ")");

        // The whole way through, as the converter uses it
        double[] grid = new double[330];
        for (int i = 0; i < grid.length; i++) grid[i] = i * 0.5;
        TempoMap map = TempoMap.fromBeats(grid, 0, 4);
        LyricAligner.Result placed = WordAligner.place(lines, e, map, 4, 120);
        int inPlaceAll = 0;
        for (int i = 0; i < truth.length; i++) if (placed.lines.size() == truth.length && Math.abs(placed.lines.get(i).time - truth[i]) <= 0.1) inPlaceAll++;
        check(placed.moved() && inPlaceAll == truth.length && placed.leftOut == 0, "the lyrics come out where they are sung (" + inPlaceAll + " of " + truth.length + ")");

        // Lyrics of some other song: not trusted, nothing changed
        List<Lrc.Line> other = new ArrayList<>();
        String[] elsewhere = {"seven ships upon the sea", "quiet rivers running deep", "paper lanterns in the rain", "old piano out of tune"};
        for (int i = 0; i < 24; i++) other.add(new Lrc.Line(8 + 6 * i, elsewhere[i % 4] + (i % 3 == 0 ? " tonight" : "")));
        LyricAligner.Result not = WordAligner.place(other, e, map, 4, 120);
        check(!not.moved() && not.lines == other, "lyrics that do not fit the voice are left alone");

        // A verse the lyric sheet has and this recording does not: left out, and nothing else disturbed
        List<Lrc.Line> longer = new ArrayList<>(lines);
        String[] cut = {"somewhere past the river bend", "we buried all our yesterdays", "under heavy summer skies"};
        for (int k = 0; k < 3; k++) longer.add(12 + k, new Lrc.Line(51 + k, cut[k]));
        LyricAligner.Result shorter = WordAligner.place(longer, e, map, 4, 120);
        int kept = 0;
        for (int i = 0; i < truth.length; i++) {
            if (shorter.lines.size() == truth.length && shorter.lines.get(i).text.equals(lines.get(i).text) && Math.abs(shorter.lines.get(i).time - truth[i]) <= 0.1) kept++;
        }
        check(shorter.leftOut == 3 && kept == truth.length, "lines not in the recording are left out, the rest stay (" + shorter.leftOut + " left out, " + kept + " of " + truth.length + " in place)");

        // The recording has a hook the lyric sheet leaves out: the others are not pulled on to its words
        List<Lrc.Line> fewer = new ArrayList<>();
        List<Double> fewerTruth = new ArrayList<>();
        for (int i = 0; i < lines.size(); i++) {
            if (i >= 8 && i < 12) continue;
            fewer.add(lines.get(i));
            fewerTruth.add(truth[i]);
        }
        LyricAligner.Result gap = WordAligner.place(fewer, e, map, 4, 120);
        int still = 0;
        for (int i = 0; i < fewer.size(); i++) if (gap.lines.size() == fewer.size() && Math.abs(gap.lines.get(i).time - fewerTruth.get(i)) <= 0.1) still++;
        StringBuilder off = new StringBuilder();
        for (int i = 0; i < fewer.size() && gap.lines.size() == fewer.size(); i++) {
            if (Math.abs(gap.lines.get(i).time - fewerTruth.get(i)) > 0.1) off.append(String.format(java.util.Locale.US, " line %d: %.2f, sung at %.2f;", i + 1, gap.lines.get(i).time, fewerTruth.get(i)));
        }
        check(still == fewer.size(), "a hook missing from the lyrics does not pull other lines on to it (" + still + " of " + fewer.size() + ")" + off + " " + gap.repeatNotes);

        // The same with the last hook misheard: its third line lands 1.7 s early, its fourth late
        String[] keys = new String[lines.size()];
        double[] beat = new double[lines.size()];
        for (int i = 0; i < keys.length; i++) {
            keys[i] = lines.get(i).text.replaceAll("\\([^)]*\\)", "").replaceAll("[^a-z]", "");
            beat[i] = truth[i] / 0.5;
        }
        int last = keys.length - 4;
        beat[last] -= 3.3;
        beat[last + 2] -= 3.4;
        beat[last + 3] += 1.9;
        List<String> log = new ArrayList<>();
        int moved = WordAligner.repeats(keys, beat, 4, 0.5, log);
        int back = 0;
        for (int i = 0; i < keys.length; i++) if (Math.abs(beat[i] * 0.5 - truth[i]) <= 0.1) back++;
        check(moved == 3 && back == keys.length, "lines that break the repeats are put back (" + moved + " moved, " + back + " of " + keys.length + " right): " + log);

        // An outro of two lines going round eight times; after the third pass the letters were laid anyhow
        String[] round = new String[16];
        double[] laid = new double[16], sung = new double[16];
        for (int q = 0; q < 8; q++) {
            round[2 * q] = "x";
            round[2 * q + 1] = "y";
            sung[2 * q] = 400 + 16 * q;
            sung[2 * q + 1] = 410.7 + 16 * q;
        }
        double[] wrong = {0, 0, 0.3, 0, -0.2, 0.1, 14.5, 20.2, 14.8, 27.0, 18.0, 0.2, 30.4, 39.0, 28.0, 43.1};
        for (int i = 0; i < 16; i++) laid[i] = sung[i] + wrong[i];
        log.clear();
        WordAligner.repeats(round, laid, 4, 0.5, log);
        int inPlace = 0;
        for (int i = 0; i < 16; i++) if (Math.abs(laid[i] - sung[i]) <= 0.5) inPlace++;
        check(inPlace == 16, "an outro going round follows on at the pace of its first passes (" + inPlace + " of 16): " + log);

        // Nothing repeats often enough to judge by: nothing is touched
        String[] once = {"a", "b", "a", "c", "b", "d"};
        double[] t = {0, 7.3, 16.2, 23, 31.9, 40};
        double[] before = t.clone();
        check(WordAligner.repeats(once, t, 4, 0.5, null) == 0 && java.util.Arrays.equals(t, before), "two repeats are not enough to move anything");

        check(WordAligner.symbols("Don't (hey) stop").length == 11, "letters and apostrophes, asides left out");
        check(WordAligner.symbols("(la la)").length == 6, "a line that is only an aside is still sung");
    }
}
