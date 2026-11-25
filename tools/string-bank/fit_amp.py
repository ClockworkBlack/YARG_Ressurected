#!/usr/bin/env python3
"""Fits the amplifier's tone-and-cabinet filter chains (AmpSettings in GuitarVoiceAmp.cs).

The amplifier model's valve stages are set by design; what cannot be set by reasoning is the
balance of low, middle and high that a real amp, speaker and microphone leave behind. This
measures it from recordings and prints the filter chains to paste into GuitarVoiceAmp.cs:

  Rock, Heavy  the bank's direct guitar notes are played through the model (without its
               cabinet) and compared, overtone by overtone, with recordings of real amplified
               guitars playing the same notes: "Overdrive Guitar" and "Distortion Guitar" in
               the FluidR3 sound bank (MIT licence; Ubuntu package fluid-soundfont-gm).
  Clean        FreePats publishes the same guitar takes played through a clean amplifier
               (github.com/freepats/electric-guitar-FSBS-clean, CC0). Before against after
               gives what that amplifier does to the tone.

    python3 tools/string-bank/fit_amp.py \
        --guitar <clone of freepats/electric-guitar-FSBS-direct> \
        --clean  <clone of freepats/electric-guitar-FSBS-clean> \
        --soundfont /usr/share/sounds/sf2/FluidR3_GM.sf2

Needs numpy, scipy and soundfile. Takes a few minutes. The valve-stage settings below must be
kept the same as in GuitarVoiceAmp.cs (the automated tests do not check this file).
"""
import argparse
import os
import re
import struct
import sys
from fractions import Fraction

import numpy as np
import soundfile as sf
from scipy import optimize, signal

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bank  # noqa: E402

SR = 44100
OVERSAMPLE = 4
CENTRES = [70, 85, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500,
           3150, 4000, 5000, 6300, 8000, 10000]
GRID = np.geomspace(70, 11000, 60)
# How a guitar speaker falls away above 5 kHz (dB against 5 kHz), from the recordings sampled
# fast enough to show it
TOP = (np.array([5000, 6300, 8000, 10000, 12500]), np.array([0.0, -4.0, -9.0, -17.0, -28.0]))

# ---------------------------------------------------------------- the amplifier, as in the game
# (filters: 'hp'/'lp' two-pole with Q, 'pk' peak, 'ls' low shelf, 'hp1'/'lp1' one-pole)
HEAVY = dict(
    input=[('hp', 70, 0.707)],
    stages=[
        dict(pre=[('hp1', 120), ('ls', 500, 0.6, -9.0)], gain=10.0, bias=0.12, up=1.4, down=0.8, post=[('lp1', 9000)]),
        dict(pre=[('hp1', 80)], gain=5.0, bias=0.10, up=1.3, down=0.8, post=[('lp1', 7500)]),
        dict(pre=[('hp1', 180)], gain=4.0, bias=0.08, up=1.2, down=0.9, post=[('lp1', 6500)]),
    ],
    tone=[('hp1', 40)], power=dict(gain=1.6, sag=0.35, sharpness=1.0))
ROCK = dict(
    input=[('hp', 70, 0.707)],
    stages=[
        dict(pre=[('hp1', 150), ('ls', 600, 0.6, -8.0)], gain=5.0, bias=0.15, up=1.3, down=0.75, post=[('lp1', 10000)]),
        dict(pre=[('hp1', 80)], gain=3.0, bias=0.10, up=1.2, down=0.8, post=[('lp1', 8000)]),
    ],
    tone=[('hp1', 40)], power=dict(gain=2.0, sag=0.45, sharpness=1.0))
CLEAN = dict(
    input=[('hp', 60, 0.707)],
    stages=[dict(pre=[('hp1', 50)], gain=0.9, bias=0.10, up=0.9, down=0.6, post=[('lp1', 12000)])],
    tone=[('hp1', 40)], power=dict(gain=0.9, sag=0.3, sharpness=0.8))


def make(spec, sr):
    kind, hz = spec[0], spec[1]
    if kind == 'lp1':
        k = 1 - np.exp(-2 * np.pi * hz / sr)
        return np.array([k]), np.array([1, k - 1])
    if kind == 'hp1':
        p = np.exp(-2 * np.pi * hz / sr)
        return np.array([(1 + p) / 2, -(1 + p) / 2]), np.array([1, -p])
    w = 2 * np.pi * hz / sr
    al, c = np.sin(w) / (2 * spec[2]), np.cos(w)
    if kind == 'lp':
        b, a = [(1 - c) / 2, 1 - c, (1 - c) / 2], [1 + al, -2 * c, 1 - al]
    elif kind == 'hp':
        b, a = [(1 + c) / 2, -(1 + c), (1 + c) / 2], [1 + al, -2 * c, 1 - al]
    elif kind == 'pk':
        A = 10 ** (spec[3] / 40)
        b, a = [1 + al * A, -2 * c, 1 - al * A], [1 + al / A, -2 * c, 1 - al / A]
    elif kind == 'ls':
        A = 10 ** (spec[3] / 40)
        s = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) - (A - 1) * c + s), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - s)]
        a = [(A + 1) + (A - 1) * c + s, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - s]
    else:
        raise ValueError(kind)
    return np.array(b) / a[0], np.array(a) / a[0]


def chain(x, specs, sr):
    for spec in specs:
        b, a = make(spec, sr)
        x = signal.lfilter(b, a, x)
    return x


def response_db(specs, freqs):
    h = np.ones(len(freqs), dtype=complex)
    for spec in specs:
        b, a = make(spec, SR)
        h *= signal.freqz(b, a, worN=freqs, fs=SR)[1]
    return 20 * np.log10(np.abs(h) + 1e-12)


def valve(x, bias, up, down):
    u = x + bias
    rest = np.tanh(up * bias) / up if bias >= 0 else np.tanh(down * bias) / down
    return np.where(u >= 0, np.tanh(up * u) / up, np.tanh(down * u) / down) - rest


def amplifier(di, p):
    """The amp without its cabinet: di is the bare guitar at 44.1 kHz."""
    x = chain(di, p['input'], SR)
    x = signal.resample_poly(x, OVERSAMPLE, 1, window=('kaiser', 9.0))
    high = SR * OVERSAMPLE
    for stage in p['stages']:
        x = chain(x, stage['pre'], high)
        x = -valve(stage['gain'] * x, stage['bias'], stage['up'], stage['down'])
        x = chain(x, stage['post'], high)
    x = chain(x, p['tone'], high) * p['power']['gain']
    # the supply giving way under load
    attack, release = 1 - np.exp(-1 / (high * 0.008)), 1 - np.exp(-1 / (high * 0.12))
    load, level = np.abs(x), 0.0
    follow = np.empty_like(load)
    for i in range(len(load)):
        level += (attack if load[i] > level else release) * (load[i] - level)
        follow[i] = level
    x = x / (1 + p['power']['sag'] * follow)
    x = np.tanh(p['power']['sharpness'] * x) / p['power']['sharpness']
    return signal.resample_poly(x, 1, OVERSAMPLE, window=('kaiser', 9.0))


# ---------------------------------------------------------------- recordings
def to_44k(x, sr):
    g = np.gcd(SR, int(sr))
    return x if sr == SR else signal.resample_poly(x, SR // g, int(sr) // g)


def onset(x, frac=0.05):
    return int(np.argmax(np.abs(x) > frac * np.abs(x).max()))


def trimmed(x):
    return x[max(0, onset(x) - int(0.002 * SR)):]


def pitch_of(x, note):
    """Autocorrelation pitch (Hz) of a note known to be near MIDI note `note`."""
    start = onset(x)
    seg = x[start + int(0.1 * SR): start + int(0.6 * SR)]
    n = len(seg)
    spec = np.fft.rfft(seg * np.hanning(n), 1 << int(np.ceil(np.log2(2 * n))))
    ac = np.fft.irfft(np.abs(spec) ** 2)[:n]
    ac = ac / ac[0]
    guess = 440 * 2 ** ((note - 69) / 12)
    lo, hi = int(SR / (guess * 1.12)), int(SR / (guess / 1.12)) + 1
    part = ac[lo:hi]
    k = next((i for i in range(1, len(part) - 1)
              if part[i] > part[i - 1] and part[i] >= part[i + 1] and part[i] > 0.85 * part.max()), int(np.argmax(part))) + lo
    a, b, c = ac[k - 1], ac[k], ac[k + 1]
    d = a - 2 * b + c
    return SR / (k + (0.5 * (a - c) / d if d != 0 else 0.0))


NOTE = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}


def note_of(name, octave):
    return 12 * (int(octave) + 1) + NOTE[name[0]] + (1 if '#' in name else 0)


def direct_takes(folder, take, seconds):
    """The bank's guitar as the game plays it: (note, Hz, samples at 44.1 kHz, 0.2 RMS at the start)."""
    folder = os.path.join(folder, 'samples', 'bridge')
    out = []
    for fn in sorted(os.listdir(folder)):
        m = re.match(r'^([A-G]#?)(\d)_s\d_(\d\d)\.wav$', fn)
        if not m or int(m.group(3)) != take or note_of(m.group(1), m.group(2)) < 40:
            continue
        note = note_of(m.group(1), m.group(2))
        x, sr = sf.read(os.path.join(folder, fn), always_2d=True)
        x = x.mean(axis=1)
        pitch = bank.measure_pitch(x, sr, note)
        start = max(0, bank.onset_index(x, sr) - int(0.0025 * sr))
        x = to_44k(x[start: start + int(seconds * sr)], sr)
        rms = np.sqrt(np.mean(x[int(0.01 * SR): int(0.33 * SR)] ** 2))
        out.append((note, 440 * 2 ** ((pitch - 69) / 12), x * 0.2 / rms))
    return sorted(out, key=lambda t: t[0])


def clean_takes(folder):
    """FreePats' clean-amp versions of the first take of each note (left channel)."""
    folder = os.path.join(folder, 'samples', 'bridge')
    out = []
    for fn in sorted(os.listdir(folder)):
        m = re.match(r'^([A-G]#?)(\d)_s\d_01\.flac$', fn)
        if not m or note_of(m.group(1), m.group(2)) < 40:
            continue
        note = note_of(m.group(1), m.group(2))
        x, sr = sf.read(os.path.join(folder, fn), always_2d=True)
        x = trimmed(to_44k(x[:, 0], sr))
        out.append((note, pitch_of(x, note), x[: 6 * SR]))
    return sorted(out, key=lambda t: t[0])


def soundfont_notes(path, preset):
    """The recordings behind one General MIDI preset of a SoundFont: (note, Hz, samples)."""
    with open(path, 'rb') as f:
        data = f.read()
    chunks, at = {}, 12
    while at < len(data):
        name, size = struct.unpack_from('<4sI', data, at)
        if name == b'LIST':
            sub = at + 12
            while sub < at + 8 + size:
                sid, ssize = struct.unpack_from('<4sI', data, sub)
                chunks[sid.decode()] = (sub + 8, ssize)
                sub += 8 + ssize + (ssize & 1)
        at += 8 + size + (size & 1)

    def table(name, fmt):
        offset, size = chunks[name]
        n = struct.calcsize(fmt)
        return [struct.unpack_from(fmt, data, offset + i) for i in range(0, size - n + 1, n)]

    phdr, pbag, pgen = table('phdr', '<20sHHHIII'), table('pbag', '<HH'), table('pgen', '<HH')
    inst, ibag, igen = table('inst', '<20sH'), table('ibag', '<HH'), table('igen', '<HH')
    shdr = table('shdr', '<20sIIIIIBbHH')
    which = next(i for i, p in enumerate(phdr[:-1]) if p[1] == preset and p[2] == 0)
    instruments = {amount for bag in range(phdr[which][3], phdr[which + 1][3])
                   for op, amount in pgen[pbag[bag][0]: pbag[bag + 1][0]] if op == 41}
    out, seen = [], set()
    for instrument in sorted(instruments):
        for bag in range(inst[instrument][1], inst[instrument + 1][1]):
            zone = dict(igen[ibag[bag][0]: ibag[bag + 1][0]])
            if 53 not in zone or zone[53] in seen:      # 53: which sample, 58: the note it plays
                continue
            seen.add(zone[53])
            _, start, end, _, _, rate, key, _, _, _ = shdr[zone[53]]
            note = zone.get(58, key)
            offset = chunks['smpl'][0]
            x = np.frombuffer(data, dtype='<i2', count=end - start, offset=offset + 2 * start) / 32768.0
            x = trimmed(to_44k(x, rate))
            out.append((note, pitch_of(x, note), x))
    return sorted(out, key=lambda t: t[0])


# ---------------------------------------------------------------- measuring and fitting
def overtones(x, f0, fmax=11000.0):
    """Level (dB) of each overtone of f0, over the first second of the note."""
    start = onset(x)
    seg = x[start + int(0.08 * SR): start + int(1.0 * SR)]
    n = 1 << 15
    power, count = np.zeros(n // 2 + 1), 0
    for s in range(0, max(1, len(seg) - n + 1), n // 4):
        w = seg[s:s + n]
        w = np.pad(w, (0, n - len(w)))
        power += np.abs(np.fft.rfft(w * np.hanning(n))) ** 2
        count += 1
        if len(seg) < n:
            break
    power /= count
    step = SR / n
    freqs, levels, k = [], [], 1
    while k * f0 < fmax:
        lo, hi = int(k * f0 * 0.985 / step), int(k * f0 * 1.03 / step) + 2   # strings run sharp
        j = lo + int(np.argmax(power[lo:hi]))
        freqs.append(j * step)
        levels.append(10 * np.log10(power[max(0, j - 3): j + 4].sum() + 1e-30))
        k += 1
    levels = np.array(levels)
    return np.array(freqs), levels - 10 * np.log10(np.sum(10 ** (levels / 10)))


def nearest_take(takes, hz):
    """The direct take nearest in pitch, played at exactly hz (as the game would)."""
    note, f, x = min(takes, key=lambda t: abs(np.log2(t[1] / hz)))
    ratio = Fraction(f / hz).limit_denominator(2000)
    return signal.resample_poly(x, ratio.numerator, ratio.denominator)


def differences(references, takes, preset, seconds=3.0):
    """For every overtone of every reference note: (Hz, real level - model level, weight)."""
    points = []
    for note, hz, real in references:
        model = amplifier(nearest_take(takes, hz)[: int(seconds * SR)], preset)
        fr, lr = overtones(real, hz)
        _, lm = overtones(model, pitch_of(model, 69 + 12 * np.log2(hz / 440)))
        for k in range(min(len(lr), len(lm))):
            points.append((fr[k], lr[k] - lm[k], np.clip((lr[k] + 45) / 30, 0.05, 1.0)))
    return np.array(points)


def smooth(points):
    """Weighted mean of the differences in overlapping third-octave windows."""
    f, d, w = points[:, 0], points[:, 1], points[:, 2]
    out = []
    for c in CENTRES:
        m = (f >= c / 2 ** (1 / 3)) & (f < c * 2 ** (1 / 3))
        out.append(np.sum(d[m] * w[m]) / np.sum(w[m]) if m.sum() >= 2 else np.nan)
    out = np.array(out)
    ok = ~np.isnan(out)
    out[~ok] = np.interp(np.log(np.array(CENTRES)[~ok]), np.log(np.array(CENTRES)[ok]), out[ok])
    return out


def fit(curve, top, ridge):
    """A high-pass, six broad peaks or dips and two low-passes whose response follows `curve`."""
    target = np.interp(np.log(GRID), np.log(CENTRES), curve)
    if top is not None:
        high = GRID > 5000
        target[high] = np.interp(np.log(5000), np.log(CENTRES), curve) + np.interp(np.log(GRID[high]), np.log(top[0]), top[1])
    centres = (120, 280, 650, 1400, 2700, 4000)
    n = len(centres)

    def design(p):
        specs = [('hp', p[0], p[1])]
        specs += [('pk', centres[i] * 2 ** p[2 + 3 * i], max(0.3, p[3 + 3 * i]), p[4 + 3 * i]) for i in range(n)]
        return specs + [('lp', p[2 + 3 * n], p[3 + 3 * n]), ('lp', p[4 + 3 * n], p[5 + 3 * n])]

    weight = np.where(GRID < 6500, 1.0, 0.6)

    def miss(p):
        d = response_db(design(p), GRID) - target
        d = d - np.average(d, weights=weight)
        # (the ridge keeps neighbouring peaks from fighting each other)
        return np.concatenate((d * weight, ridge * np.array([p[4 + 3 * i] for i in range(n)])))

    start = [80, 0.9] + [0.0, 1.0, 0.0] * n + [5600, 0.8, 7000, 0.7]
    lowest = [60, 0.6] + [-0.4, 0.5, -14.0] * n + [4300, 0.5, 5000, 0.5]
    highest = [120, 1.4] + [0.4, 2.2, 10.0] * n + [7500, 1.1, 9500, 0.9]
    best = optimize.least_squares(miss, start, bounds=(lowest, highest), max_nfev=600)
    return [(s[0], round(s[1]), round(s[2], 2)) + ((round(s[3], 1),) if len(s) > 3 else ()) for s in design(best.x)]


def show(name, specs):
    print(f'{name}:')
    names = {'hp': 'HighPass', 'lp': 'LowPass', 'pk': 'Peak'}
    for s in specs:
        print(f'    Filter.{names[s[0]]}({", ".join(str(v) for v in s[1:])}, RATE),')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--guitar', required=True, help='folder of the FreePats FSBS direct recordings')
    parser.add_argument('--clean', required=True, help='folder of the FreePats FSBS clean recordings')
    parser.add_argument('--soundfont', required=True, help='FluidR3_GM.sf2')
    args = parser.parse_args()

    # Rock and Heavy: every take the game plays against the real amp recordings
    takes = [direct_takes(args.guitar, take, 3.2) for take in (1, 2, 3, 4)]
    for name, preset, number, lift in (('Heavy', HEAVY, 30, 0.0), ('Rock', ROCK, 29, 5.0)):
        references = soundfont_notes(args.soundfont, number)
        curve = smooth(np.concatenate([differences(references, t, preset) for t in takes]))
        if lift:
            # More body than the (thin) reference: lift everything below 800 Hz, tapering out
            # by 2 kHz, and even out that one recording's own bumps down there
            c = np.array(CENTRES, dtype=float)
            curve = curve + np.where(c <= 800, lift, lift * np.clip(1 - np.log2(c / 800) / np.log2(2000 / 800), 0, 1))
            low = c <= 1000
            curve[low] = 0.5 * curve[low] + 0.5 * np.mean(curve[low])
        show(name, fit(curve, TOP, 0.15))

    # Clean: the same takes before and after FreePats' clean amplifier
    show('Clean', fit(smooth(differences(clean_takes(args.clean), direct_takes(args.guitar, 1, 4.0), CLEAN)), None, 0.12))


if __name__ == '__main__':
    main()
