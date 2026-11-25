import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/**
 * A made-up vocal track with known answers: lines that are sung on steady notes and lines that
 * are rapped (short syllables with a gliding pitch and a burst of noise in front, like a
 * consonant), performed by two different "voices". It is not a real singer, but unlike a real
 * singer we know exactly when every word starts and what note it is on.
 */
final class SynthVoice {
    static final class Word {
        final String text;
        final double start, end;
        /** MIDI note for a sung word, -1 for a rapped one. */
        final int midi;
        /** A second note the word moves to half way through, or -1. */
        final int midi2;

        Word(String text, double start, double end, int midi, int midi2) {
            this.text = text;
            this.start = start;
            this.end = end;
            this.midi = midi;
            this.midi2 = midi2;
        }
    }

    static final class Line {
        /** When the line is really sung, and the time stamp its lyric file gives (usually a bit early). */
        double start, stamp;
        boolean sung;
        /** 0 or 1: which of the two voices performs it. */
        int voice;
        final List<Word> words = new ArrayList<>();

        String text() {
            StringBuilder sb = new StringBuilder();
            for (Word w : words) sb.append(sb.length() > 0 ? " " : "").append(w.text);
            return sb.toString();
        }
    }

    final int sampleRate;
    final float[] audio;
    final List<Line> lines = new ArrayList<>();
    final double duration;

    private static final String[] SUNG_WORDS = {"hold", "me", "now", "forever", "tonight", "you", "and", "I", "remember", "summer", "light", "away", "love", "shine", "over", "again"};
    private static final String[] RAP_WORDS = {"yeah", "pull", "up", "in", "the", "whip", "money", "talk", "never", "stop", "run", "it", "back", "double", "time", "every", "city", "know", "my", "name"};

    /**
     * @param plan one letter per line: S sung by voice 0, s sung by voice 1, R rapped by voice 0,
     *             r rapped by voice 1, L a sung line with long held words that change note
     */
    SynthVoice(int sampleRate, String plan, long seed) {
        this.sampleRate = sampleRate;
        Random rnd = new Random(seed);
        double t = 2.0;
        int[] scale = {0, 2, 4, 5, 7, 9, 11, 12};
        for (int i = 0; i < plan.length(); i++) {
            char c = plan.charAt(i);
            Line line = new Line();
            line.sung = c == 'S' || c == 's' || c == 'L';
            line.voice = Character.isLowerCase(c) ? 1 : 0;
            line.start = t;
            line.stamp = Math.max(0, t - 0.04 - 0.2 * rnd.nextDouble());
            int count = c == 'L' ? 3 : line.sung ? 4 + rnd.nextInt(3) : 6 + rnd.nextInt(4);
            int base = line.voice == 0 ? 50 : 60;
            for (int k = 0; k < count; k++) {
                String text = line.sung ? SUNG_WORDS[rnd.nextInt(SUNG_WORDS.length)] : RAP_WORDS[rnd.nextInt(RAP_WORDS.length)];
                double length;
                int midi = -1, midi2 = -1;
                if (c == 'L') {
                    length = 1.1 + 0.1 * rnd.nextInt(3);
                    midi = base + scale[rnd.nextInt(5)];
                    midi2 = midi + (rnd.nextBoolean() ? 3 : -4);
                } else if (line.sung) {
                    // How long a word is held has little to do with how it is spelt.
                    length = 0.25 + 0.5 * rnd.nextDouble();
                    midi = base + scale[rnd.nextInt(scale.length)];
                    // Never the same note twice in a row: a word run on from the last one on the
                    // same note could not be told apart by anyone.
                    if (!line.words.isEmpty() && line.words.get(line.words.size() - 1).midi == midi) midi += 2;
                } else {
                    length = 0.09 + 0.13 * rnd.nextDouble();
                }
                line.words.add(new Word(text, t, t + length, midi, midi2));
                // One sung word in four runs straight on into the next, with no gap to hear.
                boolean runOn = line.sung && c != 'L' && rnd.nextInt(4) == 0;
                t += length + (runOn ? 0.0 : line.sung ? 0.06 : 0.04);
            }
            lines.add(line);
            t += 0.9 + 0.8 * rnd.nextDouble();
        }
        duration = t + 2.0;
        audio = new float[(int) (duration * sampleRate)];

        for (Line line : lines) {
            for (Word w : line.words) {
                if (w.midi >= 0) sing(w, line.voice, rnd);
                else rap(w, line.voice, line.words.indexOf(w), rnd);
            }
        }
        // What a separation leaves behind where nobody sings: a faint steady hum of the band.
        for (int i = 0; i < audio.length; i++) {
            double ph = 2 * Math.PI * i / sampleRate;
            audio[i] += (float) (0.004 * Math.sin(ph * 147) + 0.003 * Math.sin(ph * 392) + 0.002 * (rnd.nextDouble() - 0.5));
        }
    }

    /** Two resonances give each voice its own colour, as the shape of a mouth and throat does. */
    private static double formants(int voice, double hz) {
        double f1 = voice == 0 ? 520 : 760, f2 = voice == 0 ? 1250 : 2300;
        double a = Math.exp(-Math.pow((hz - f1) / 260.0, 2));
        double b = 0.7 * Math.exp(-Math.pow((hz - f2) / 420.0, 2));
        return 0.06 + a + b;
    }

    private void sing(Word w, int voice, Random rnd) {
        int start = (int) Math.round(w.start * sampleRate);
        int count = (int) ((w.end - w.start) * sampleRate);
        double phase = 0;
        double vibratoRate = 5.2 + rnd.nextDouble();
        for (int i = 0; i < count && start + i < audio.length; i++) {
            double pos = (double) i / count;
            double note = w.midi;
            if (w.midi2 >= 0) {
                // Hold the first note, travel for a twentieth of a second, hold the second.
                double travel = 0.05 * sampleRate / count;
                if (pos > 0.5 + travel) note = w.midi2;
                else if (pos > 0.5) note = w.midi + (w.midi2 - w.midi) * (pos - 0.5) / travel;
            }
            note += 0.25 * Math.sin(2 * Math.PI * vibratoRate * i / sampleRate);
            double hz = 440.0 * Math.pow(2, (note - 69) / 12.0);
            phase += 2 * Math.PI * hz / sampleRate;
            double env = Math.min(1.0, i / (0.015 * sampleRate)) * Math.min(1.0, (count - i) / (0.03 * sampleRate));
            double v = 0;
            for (int h = 1; h <= 12 && hz * h < 5000; h++) v += formants(voice, hz * h) / h * Math.sin(phase * h);
            audio[start + i] += (float) (0.22 * env * v);
        }
    }

    private void rap(Word w, int voice, int index, Random rnd) {
        int start = (int) Math.round(w.start * sampleRate);
        int count = (int) ((w.end - w.start) * sampleRate);
        int burst = (int) (0.02 * sampleRate);
        double phase = 0;
        // Speech melody: each syllable starts somewhere new and glides, never settling on a note.
        double from = (voice == 0 ? 47 : 57) + 5 * rnd.nextDouble();
        double to = from + (rnd.nextBoolean() ? -1 : 1) * (2.5 + 2 * rnd.nextDouble());
        for (int i = 0; i < count && start + i < audio.length; i++) {
            double env = Math.min(1.0, i / (0.006 * sampleRate)) * Math.min(1.0, (count - i) / (0.012 * sampleRate));
            if (i < burst) {
                audio[start + i] += (float) (0.12 * env * (rnd.nextDouble() * 2 - 1));
                continue;
            }
            double pos = (double) (i - burst) / Math.max(1, count - burst);
            double note = from + (to - from) * pos;
            double hz = 440.0 * Math.pow(2, (note - 69) / 12.0);
            phase += 2 * Math.PI * hz / sampleRate;
            double v = 0;
            for (int h = 1; h <= 12 && hz * h < 5000; h++) v += formants(voice, hz * h) / h * Math.sin(phase * h);
            audio[start + i] += (float) (0.2 * env * v);
        }
    }
}
