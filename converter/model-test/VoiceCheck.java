import com.clockworkblack.yargconverter.core.VocalAnalyzer;
import com.clockworkblack.yargconverter.core.VocalCharter;

import java.io.BufferedInputStream;
import java.io.DataInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Measures how a real voice delivers its words, for tuning the sung-or-rapped decision on actual
 * recordings. Takes vocals on their own (an a cappella), cuts them into stretches of sound
 * between pauses, and prints the measurements of each stretch and how it would be judged.
 *
 *   java -cp out VoiceCheck vocals.f32 "label"     (mono 32-bit float, 44.1 kHz)
 */
public final class VoiceCheck {
    public static void main(String[] args) throws Exception {
        File raw = new File(args[0]);
        int total = (int) (raw.length() / 4);
        float[] x = new float[total];
        try (DataInputStream in = new DataInputStream(new BufferedInputStream(new FileInputStream(raw), 1 << 20))) {
            byte[] bytes = new byte[4 * 8192];
            int at = 0;
            while (at < total) {
                int n = Math.min(8192, total - at);
                in.readFully(bytes, 0, n * 4);
                ByteBuffer.wrap(bytes, 0, n * 4).order(ByteOrder.LITTLE_ENDIAN).asFloatBuffer().get(x, at, n);
                at += n;
            }
        }
        VocalAnalyzer an = new VocalAnalyzer(44100);
        an.feed(x, 0, x.length);
        VocalAnalyzer.Voice v = an.finish();
        float gate = v.gate();

        // Stretches: sound with no pause of 0.5 s or more inside, at least 1.5 s long.
        List<double[]> stretches = new ArrayList<>();
        int start = -1, quiet = 0, pause = (int) (0.5 * v.fps);
        for (int f = 0; f <= v.frames(); f++) {
            boolean on = f < v.frames() && v.level[f] > gate;
            if (on) {
                if (start < 0) start = f;
                quiet = 0;
            } else if (start >= 0 && ++quiet >= pause) {
                int end = f - quiet;
                if ((end - start) / v.fps >= 1.5) stretches.add(new double[] {v.timeOf(start), v.timeOf(end)});
                start = -1;
            }
        }
        int sung = 0;
        System.out.printf(Locale.US, "VOICE %s: %.0f s, %d stretches%n", args[1], v.duration, stretches.size());
        for (double[] s : stretches) {
            VocalCharter.Delivery d = VocalCharter.measure(v, s[0], s[1]);
            boolean isSung = VocalCharter.judgeSung(d);
            if (isSung) sung++;
            System.out.printf(Locale.US, "  %6.1f-%6.1f %s %s%n", s[0], s[1], isSung ? "SUNG " : "rap  ", d);
        }
        System.out.printf(Locale.US, "VOICE-SUMMARY %s: %d of %d stretches judged sung%n", args[1], sung, stretches.size());
    }
}
