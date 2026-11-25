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
}
