package com.clockworkblack.yargconverter.core;

/** Small in-place radix-2 FFT. */
final class Fft {
    private final int n;
    private final float[] cos;
    private final float[] sin;
    private final int[] rev;

    Fft(int n) {
        if (Integer.bitCount(n) != 1) throw new IllegalArgumentException("FFT size must be a power of two");
        this.n = n;
        cos = new float[n / 2];
        sin = new float[n / 2];
        for (int i = 0; i < n / 2; i++) {
            double a = -2.0 * Math.PI * i / n;
            cos[i] = (float) Math.cos(a);
            sin[i] = (float) Math.sin(a);
        }
        rev = new int[n];
        int bits = Integer.numberOfTrailingZeros(n);
        for (int i = 0; i < n; i++) rev[i] = Integer.reverse(i) >>> (32 - bits);
    }

    void transform(float[] re, float[] im) {
        for (int i = 0; i < n; i++) {
            int j = rev[i];
            if (j > i) {
                float t = re[i]; re[i] = re[j]; re[j] = t;
                t = im[i]; im[i] = im[j]; im[j] = t;
            }
        }
        for (int size = 2; size <= n; size <<= 1) {
            int half = size >> 1;
            int step = n / size;
            for (int start = 0; start < n; start += size) {
                for (int k = 0, t = 0; k < half; k++, t += step) {
                    int a = start + k;
                    int b = a + half;
                    float xr = re[b] * cos[t] - im[b] * sin[t];
                    float xi = re[b] * sin[t] + im[b] * cos[t];
                    re[b] = re[a] - xr;
                    im[b] = im[a] - xi;
                    re[a] += xr;
                    im[a] += xi;
                }
            }
        }
    }
}
