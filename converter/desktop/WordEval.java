import com.clockworkblack.yargconverter.core.Lrc;
import com.clockworkblack.yargconverter.core.LyricAligner;
import com.clockworkblack.yargconverter.core.TempoMap;
import com.clockworkblack.yargconverter.core.VocalCharter;
import com.clockworkblack.yargconverter.core.WordAligner;

import java.io.BufferedInputStream;
import java.io.DataInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Times lyrics again from what the speech model heard (ConvertSong --letters-out), without
 * splitting the song again: java WordEval letters.bin lyrics.lrc bpm
 * Prints one row per line with words: given time, where its letters were heard, final time, fit.
 */
public final class WordEval {
    public static void main(String[] a) throws Exception {
        float[][] letters;
        try (DataInputStream in = new DataInputStream(new BufferedInputStream(new FileInputStream(a[0]), 1 << 16))) {
            int n = in.readInt();
            letters = new float[n][WordAligner.SYMBOLS];
            for (float[] row : letters) for (int k = 0; k < row.length; k++) row[k] = in.readFloat();
        }
        List<Lrc.Line> lines = new ArrayList<>();
        for (Lrc.Line l : Lrc.parse(Lrc.decode(Files.readAllBytes(new File(a[1]).toPath())))) if (!VocalCharter.words(l.text).isEmpty()) lines.add(l);
        double bpm = Double.parseDouble(a[2]);
        double[] beats = new double[(int) (letters.length * WordAligner.FRAME_SEC * bpm / 60) + 8];
        for (int i = 0; i < beats.length; i++) beats[i] = i * 60.0 / bpm;
        TempoMap map = TempoMap.fromBeats(beats, 0, 4);
        LyricAligner.Result r = WordAligner.place(lines, letters, map, 4, bpm, a.length < 4 || !a[3].equals("plain"));
        if (r.heard == null) {
            System.out.println("NOT ALIGNED" + (r.note == null ? "" : ": " + r.note));
            return;
        }
        for (int i = 0; i < r.lines.size(); i++) {
            System.out.printf(Locale.US, "%.2f\t%.2f\t%.2f\t%.2f\t%s%n", 0.0, r.heard[i], r.lines.get(i).time, r.fit[i], WordAligner.key(r.lines.get(i).text).hashCode());
        }
        System.err.println(r.note);
    }
}
