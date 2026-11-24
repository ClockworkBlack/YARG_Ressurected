package com.clockworkblack.yargconverter.core;

import java.util.Arrays;

/**
 * Guesses which of two singers performs each line of a song from the sound of their voices.
 *
 * For every line it takes the colour of the voice (how the sound is spread over the frequency
 * bands, see {@link VocalAnalyzer}) and how high the voice sits, sorts the lines into the two
 * groups that differ most, and then smooths the result: singers trade verses far more often
 * than single lines, so a lone line that sounds a little like the other singer stays put.
 * Whoever sings the first line is called the first singer.
 *
 * It is a guess. Two voices that sound alike, heavy effects, or one artist who both sings and
 * raps can fool it, which is why the app lets the answer be corrected by hand.
 */
public final class VoiceSplitter {
    private VoiceSplitter() {}

    /**
     * How far apart the two groups of lines have to be, measured in units of how much the lines
     * within a group vary, before the guess is offered at all. One voice on its own, however
     * varied, comes out around 3 to 6 when cut in half; two distinct voices land above that.
     */
    static final double MIN_SEPARATION = 6.5;
    /**
     * How much one semitone of difference in where the voice sits counts, against the colour
     * bands. Next to nothing: the same artist raps low and sings high, and must stay one person.
     */
    static final double PITCH_WEIGHT = 0.02;
    /** Frames at least this clean (see {@link VocalAnalyzer.Voice#clarity}) are used to describe a voice. */
    static final float CLEAR_ENOUGH = 0.6f;
    /** How far apart the voices came out the last time a guess was tried, even if it was turned down (for the log). */
    public static volatile double lastSeparation;
    /** A second singer with less than this share of the lines is taken for noise. */
    static final double MIN_SHARE = 0.12;

    public static final class Guess {
        /** {@link VocalCharter#PART_FIRST} or {@link VocalCharter#PART_SECOND} for every lyric line (lines without notes get the first). */
        public final int[] partOfLine;
        /** How clearly the two voices differ: the distance between them over the spread within each. */
        public final double separation;
        public final int firstLines, secondLines;

        Guess(int[] partOfLine, double separation, int firstLines, int secondLines) {
            this.partOfLine = partOfLine;
            this.separation = separation;
            this.firstLines = firstLines;
            this.secondLines = secondLines;
        }
    }

    /**
     * @param lineCount number of lyric lines the chart was built from
     * @return the guess, or null when the voices cannot be told apart (or there are too few lines)
     */
    public static Guess guess(VocalCharter.Chart chart, VocalAnalyzer.Voice voice, int lineCount) {
        lastSeparation = 0;
        if (chart == null || voice == null || voice.frames() < 50) return null;
        final int dims = VocalAnalyzer.BANDS + 1;
        float gate = voice.gate();

        // One description per line: average colour of the voice, and its middle note.
        int[] lineIndex = new int[chart.phrases.size()];
        double[][] feature = new double[chart.phrases.size()][];
        double[] weight = new double[chart.phrases.size()];
        int n = 0;
        int previous = -1;
        float[] notes = new float[1024];
        for (VocalCharter.Phrase p : chart.phrases) {
            if (p.line == previous) continue;
            previous = p.line;
            int a = voice.frameAt(p.lineStart), b = Math.min(voice.frames(), voice.frameAt(p.lineEnd) + 1);
            double[] f = new double[dims];
            int frames = 0, voiced = 0;
            if (notes.length < b - a) notes = new float[b - a];
            for (int i = a; i < b; i++) {
                // Only the clear, voiced part of the sound (the vowels) is compared: breaths,
                // consonants and the hiss of rap delivery say little about whose voice it is.
                if (voice.level[i] <= gate || Float.isNaN(voice.midi[i]) || voice.clarity[i] < CLEAR_ENOUGH) continue;
                frames++;
                for (int d = 0; d < VocalAnalyzer.BANDS; d++) f[d] += voice.timbre[i * VocalAnalyzer.BANDS + d];
                notes[voiced++] = voice.midi[i];
            }
            if (frames < 8) continue;
            for (int d = 0; d < VocalAnalyzer.BANDS; d++) f[d] /= frames;
            if (voiced >= 3) {
                Arrays.sort(notes, 0, voiced);
                f[dims - 1] = notes[voiced / 2];
            } else {
                f[dims - 1] = Double.NaN;
            }
            lineIndex[n] = p.line;
            feature[n] = f;
            weight[n] = frames;
            n++;
        }
        if (n < 6) return null;

        // Lines with no clear note take the middle of the song's.
        double[] known = new double[n];
        int k = 0;
        for (int i = 0; i < n; i++) if (!Double.isNaN(feature[i][dims - 1])) known[k++] = feature[i][dims - 1];
        Arrays.sort(known, 0, k);
        double middle = k > 0 ? known[k / 2] : 60;
        for (int i = 0; i < n; i++) if (Double.isNaN(feature[i][dims - 1])) feature[i][dims - 1] = middle;

        // The colour bands are already on one scale (log loudness with the average taken out).
        // Where the voice sits counts for less: a singer's low verse and high chorus must not
        // look like two people.
        double[] centreOfAll = new double[dims];
        for (int i = 0; i < n; i++) {
            feature[i][dims - 1] *= PITCH_WEIGHT;
            for (int d = 0; d < dims; d++) centreOfAll[d] += feature[i][d] / n;
        }
        for (int i = 0; i < n; i++) for (int d = 0; d < dims; d++) feature[i][d] -= centreOfAll[d];

        // Two groups, starting from the two lines that differ most.
        int a0 = 0;
        double far = -1;
        double[] origin = new double[dims];
        for (int i = 0; i < n; i++) {
            double d = distance(feature[i], origin);
            if (d > far) {
                far = d;
                a0 = i;
            }
        }
        int b0 = 0;
        far = -1;
        for (int i = 0; i < n; i++) {
            double d = distance(feature[i], feature[a0]);
            if (d > far) {
                far = d;
                b0 = i;
            }
        }
        double[][] centre = {feature[a0].clone(), feature[b0].clone()};
        int[] group = new int[n];
        for (int round = 0; round < 40; round++) {
            boolean moved = false;
            for (int i = 0; i < n; i++) {
                int g = distance(feature[i], centre[0]) <= distance(feature[i], centre[1]) ? 0 : 1;
                if (g != group[i] || round == 0) moved = true;
                group[i] = g;
            }
            if (!moved) break;
            for (int g = 0; g < 2; g++) {
                double[] sum = new double[dims];
                int members = 0;
                for (int i = 0; i < n; i++) {
                    if (group[i] != g) continue;
                    members++;
                    for (int d = 0; d < dims; d++) sum[d] += feature[i][d];
                }
                if (members == 0) return null;
                for (int d = 0; d < dims; d++) centre[g][d] = sum[d] / members;
            }
        }

        // How clearly apart are they? Look along the line joining the two groups: the gap between
        // them against the spread inside each.
        double[] axis = new double[dims];
        double axisLength = distance(centre[0], centre[1]);
        if (axisLength < 1e-9) return null;
        for (int d = 0; d < dims; d++) axis[d] = (centre[1][d] - centre[0][d]) / axisLength;
        double[] lean = new double[n];
        double[] mean = new double[2], spread = new double[2];
        int[] members = new int[2];
        double meanLean = 0;
        for (int i = 0; i < n; i++) {
            // Positive leans towards group 1; zero is half way between the groups.
            double along = 0;
            for (int d = 0; d < dims; d++) along += (feature[i][d] - (centre[0][d] + centre[1][d]) / 2) * axis[d];
            lean[i] = along;
            meanLean += Math.abs(along);
            mean[group[i]] += along;
            members[group[i]]++;
        }
        meanLean /= n;
        for (int g = 0; g < 2; g++) mean[g] /= Math.max(1, members[g]);
        for (int i = 0; i < n; i++) spread[group[i]] += (lean[i] - mean[group[i]]) * (lean[i] - mean[group[i]]);
        double within = Math.sqrt((spread[0] / Math.max(1, members[0]) + spread[1] / Math.max(1, members[1])) / 2);
        double separation = (mean[1] - mean[0]) / (within + 1e-6);
        lastSeparation = separation;
        if (separation < MIN_SEPARATION) return null;

        // Smooth: the cheapest path through the lines when changing singer has a price.
        double switchCost = meanLean;
        double[][] cost = new double[n][2];
        int[][] from = new int[n][2];
        for (int i = 0; i < n; i++) {
            for (int g = 0; g < 2; g++) {
                double here = g == 0 ? Math.max(0, lean[i]) : Math.max(0, -lean[i]);
                if (i == 0) {
                    cost[i][g] = here;
                    continue;
                }
                double stay = cost[i - 1][g], change = cost[i - 1][1 - g] + switchCost;
                from[i][g] = stay <= change ? g : 1 - g;
                cost[i][g] = here + Math.min(stay, change);
            }
        }
        int g = cost[n - 1][0] <= cost[n - 1][1] ? 0 : 1;
        for (int i = n - 1; i >= 0; i--) {
            group[i] = g;
            g = from[i][g];
        }

        // Whoever sings the first line is taken to be the first artist (the app can swap them).
        double[] share = new double[2];
        int[] lines = new int[2];
        for (int i = 0; i < n; i++) {
            share[group[i]] += weight[i];
            lines[group[i]]++;
        }
        int main = group[0];
        if (Math.min(share[0], share[1]) < MIN_SHARE * (share[0] + share[1])) return null;

        int[] part = new int[Math.max(lineCount, 1)];
        Arrays.fill(part, VocalCharter.PART_FIRST);
        for (int i = 0; i < n; i++) {
            if (lineIndex[i] < part.length) part[lineIndex[i]] = group[i] == main ? VocalCharter.PART_FIRST : VocalCharter.PART_SECOND;
        }
        return new Guess(part, separation, lines[main], lines[1 - main]);
    }

    private static double distance(double[] a, double[] b) {
        double sum = 0;
        for (int i = 0; i < a.length; i++) sum += (a[i] - b[i]) * (a[i] - b[i]);
        return Math.sqrt(sum);
    }
}
