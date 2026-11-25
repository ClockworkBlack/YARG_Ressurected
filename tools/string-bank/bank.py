"""String bank: the file of recorded guitar (or bass) notes that ships with the game.

This module prepares single-note recordings and packs them. The game reads the result with
StringBank.Load (overlay/Assets/Script/Audio/GuitarVoice/GuitarVoiceStrings.cs).

File layout (little-endian):
    "YGSB", u16 version (1), u16 kind (0 guitar, 1 bass), u32 sample rate, u32 count
    count x 24 bytes, one per take:
        f32 pitch         MIDI note number the take really sounds at (with fraction)
        f32 gain          multiply the samples by this so every take is equally loud
        u32 frames
        u8  note          the note that was played (MIDI note number)
        u8  soft          1: played softly (for hammer-ons), 0: picked hard
        u8  string        which string, 1 = lowest (0: not known); the game balances each
                          guitar string's tone by it (StringVoicing)
        u8  take          which take of the note; for information only
        u32 data offset   from the start of the file
        u32 data bytes
    data: blocks of 2048 samples, each starting on a whole byte. Every sample is guessed from
    the ones before it and only the miss is stored, so the file is lossless at about a third of
    the size. A block is:
        u8  order << 4 | k      order 0..8: how many earlier samples the guess uses
        if order > 0:
            u8 shift, then order x 13-bit signed weights (bits packed most significant first)
            guess = (weight[0] * previous + weight[1] * the one before + ...) >> shift
        for every sample, the miss as a Rice code: (miss folded to unsigned) >> k ones, a zero,
        then the low k bits. 24 ones in a row mean "escape": the next 20 bits are the folded
        value itself.
    The samples before the start of a take count as zero.
"""
import struct
import numpy as np
from scipy import signal

BLOCK = 2048
ESCAPE = 24
ESCAPE_BITS = 20
MAX_ORDER = 8
WEIGHT_BITS = 13


# ------------------------------------------------------------------ measuring a recording

def onset_index(x, sr, frac=0.25):
    """Where the pick lets go of the string: the first sample at a quarter of the loudest.

    Recordings begin with up to 50 ms of the pick touching the string (a quiet scrape); the
    note itself has to land on the beat, so that is not where it starts."""
    peak = np.abs(x).max()
    if peak <= 0:
        return 0
    return int(np.argmax(np.abs(x) > frac * peak))


def rough_f0(x, sr, guess):
    """Autocorrelation pitch near `guess` (Hz)."""
    n = len(x)
    w = x * np.hanning(n)
    spec = np.fft.rfft(w, 1 << int(np.ceil(np.log2(2 * n))))
    ac = np.fft.irfft(np.abs(spec) ** 2)[:n]
    lo, hi = int(sr / (guess * 1.2)), int(sr / (guess / 1.2)) + 2
    k = lo + int(np.argmax(ac[lo:hi]))
    a, b, c = ac[k - 1], ac[k], ac[k + 1]
    d = a - 2 * b + c
    return sr / (k + (0.5 * (a - c) / d if d != 0 else 0.0))


def fundamental(x, sr, guess, cents=60):
    """Frequency of the strongest component within `cents` of `guess`: the note's fundamental."""
    t = np.arange(len(x)) / sr
    w = np.hanning(len(x)) * x

    def strongest(centre, span, step):
        offsets = np.arange(-span, span + step / 2, step)
        mags = [abs(np.dot(w, np.exp(-2j * np.pi * centre * 2 ** (c / 1200) * t))) for c in offsets]
        return centre * 2 ** (offsets[int(np.argmax(mags))] / 1200)

    return strongest(strongest(guess, cents, 2.0), 2.5, 0.1)


def measure_pitch(x, sr, midi_hint, t0=0.06, t1=0.9):
    """MIDI note number (with fraction) a take sounds at, over the first second of its ring.

    A plucked string starts a little sharp and settles, more so the harder it is hit, so no two
    takes are at the same pitch; each is measured and the game tunes each one separately."""
    on = onset_index(x, sr)
    seg = x[on + int(t0 * sr): on + int(t1 * sr)]
    guess = 440.0 * 2 ** ((midi_hint - 69) / 12)
    f = rough_f0(seg, sr, guess)
    if abs(1200 * np.log2(f / guess)) > 150:
        f = guess
    f = fundamental(seg, sr, f)
    return 69 + 12 * np.log2(f / 440.0)


def envelope_db(x, sr, frame=0.02):
    h = int(sr * frame)
    n = len(x) // h
    e = np.sqrt(np.mean(x[: n * h].reshape(n, h) ** 2, axis=1) + 1e-14)
    return 20 * np.log10(e)


# ------------------------------------------------------------------ preparing a recording

def resample(x, sr, rate):
    if sr == rate:
        return x
    g = np.gcd(int(sr), int(rate))
    return signal.resample_poly(x, rate // g, sr // g, window=('kaiser', 10.0))


def prepare(x, sr, note, rate, max_seconds, soft=False, string=0, take=0, loudness=0.2,
            floor_db=-55.0, lead_in=0.0025):
    """Trim, measure and level one take of one note.

    x: mono float samples at sr. Returns a dict ready for pack()."""
    x = np.asarray(x, dtype=np.float64)
    pitch = measure_pitch(x, sr, note)

    on = max(0, onset_index(x, sr) - int(lead_in * sr))
    x = resample(x[on:], sr, rate)

    # Stop where the note has died away (or at the longest length wanted)
    env = envelope_db(x, rate)
    quiet = np.where(env < env.max() + floor_db)[0]
    quiet = quiet[quiet > np.argmax(env)]
    end = len(x)
    if len(quiet):
        end = min(end, int((quiet[0] + 1) * 0.02 * rate))
    end = min(end, int(max_seconds * rate))
    x = x[:end].copy()

    # No click at either end
    n_in = max(1, int(0.001 * rate))
    x[:n_in] *= np.linspace(0.0, 1.0, n_in)
    n_out = min(len(x) // 3, int(0.15 * rate))
    x[-n_out:] *= np.linspace(1.0, 0.0, n_out) ** 2

    # Every take equally loud over its first third of a second
    a, b = int(0.01 * rate), min(len(x), int(0.33 * rate))
    rms = np.sqrt(np.mean(x[a:b] ** 2))
    peak = np.abs(x).max()
    stored = np.round(x / peak * 32000).astype(np.int16)
    gain = (loudness / rms) * (peak / 32000) * 32768

    return dict(pitch=float(pitch), gain=float(gain), samples=stored, note=int(note), soft=bool(soft),
                string=int(string), take=int(take))


# ------------------------------------------------------------------ packing

def _fold(e):
    e = e.astype(np.int64)
    return np.where(e >= 0, 2 * e, -2 * e - 1).astype(np.uint64)


def _rice_bits(u, k):
    q = (u >> np.uint64(k)).astype(np.int64)
    return int(np.sum(np.where(q >= ESCAPE, ESCAPE + ESCAPE_BITS, q + 1 + k)))


def _best_k(u):
    k0 = int(min(15, max(0, np.floor(np.log2(float(u.mean()) + 1)))))
    best = None
    for k in range(max(0, k0 - 1), min(15, k0 + 2) + 1):
        bits = _rice_bits(u, k)
        if best is None or bits < best[0]:
            best = (bits, k)
    return best


def _write_bits(parts):
    """parts: list of (values uint64 array, widths int array). Returns a uint8 array of bits."""
    widths = np.concatenate([w for _, w in parts])
    values = np.concatenate([v for v, _ in parts])
    start = np.concatenate(([0], np.cumsum(widths)[:-1]))
    bits = np.zeros(int(widths.sum()), dtype=np.uint8)
    for b in range(int(widths.max()) if len(widths) else 0):
        sel = widths > b
        shift = (widths[sel] - 1 - b).astype(np.uint64)
        bits[start[sel] + b] = ((values[sel] >> shift) & np.uint64(1)).astype(np.uint8)
    return bits


def _rice_code(u, k):
    """Rice codes of folded misses as (values, widths), ready for _write_bits."""
    q = (u >> np.uint64(k)).astype(np.int64)
    esc = q >= ESCAPE
    # ones then (unless escaping) a zero: the number (2^ones - 1) << 1, or just the ones
    ones = np.where(esc, ESCAPE, q)
    head = np.where(esc, (np.uint64(1) << np.uint64(ESCAPE)) - np.uint64(1),
                    ((np.uint64(1) << ones.astype(np.uint64)) - np.uint64(1)) << np.uint64(1))
    head_width = np.where(esc, ESCAPE, ones + 1)
    tail = np.where(esc, u, u & np.uint64((1 << k) - 1)).astype(np.uint64)
    tail_width = np.where(esc, ESCAPE_BITS, k)
    values = np.stack((head.astype(np.uint64), tail), axis=1).reshape(-1)
    widths = np.stack((head_width, tail_width), axis=1).reshape(-1).astype(np.int64)
    keep = widths > 0
    return values[keep], widths[keep]


def _weights(history_and_block, order_max):
    """Linear prediction weights for orders 1..order_max (Levinson-Durbin on the windowed block)."""
    w = history_and_block.astype(np.float64)
    w = w * np.hanning(len(w) + 2)[1:-1]
    r = np.array([np.dot(w[: len(w) - k], w[k:]) for k in range(order_max + 1)])
    out = {}
    if r[0] <= 0:
        return out
    r[0] *= 1.0000001
    a = np.zeros(order_max + 1)
    a[0] = 1.0
    err = r[0]
    for m in range(1, order_max + 1):
        acc = r[m] + np.dot(a[1:m], r[m - 1:0:-1])
        k = -acc / err
        nxt = a.copy()
        nxt[1:m] = a[1:m] + k * a[m - 1:0:-1]
        nxt[m] = k
        a = nxt
        err *= (1 - k * k)
        if err <= 0:
            break
        out[m] = -a[1:m + 1]
    return out


def _quantise(coef):
    """Weights as 13-bit integers and the shift that scales them back."""
    limit = (1 << (WEIGHT_BITS - 1)) - 1
    biggest = float(np.abs(coef).max())
    if biggest <= 0:
        return None
    shift = int(np.clip(np.floor(np.log2(limit / biggest)), 0, 15))
    q = np.round(coef * (1 << shift)).astype(np.int64)
    if np.abs(q).max() > limit:
        return None
    return q, shift


def encode(samples):
    """Losslessly pack int16 samples."""
    x = samples.astype(np.int64)
    padded = np.concatenate((np.zeros(MAX_ORDER, dtype=np.int64), x))
    fixed = {1: np.array([1]), 2: np.array([2, -1]), 3: np.array([3, -3, 1])}
    out = bytearray()
    for s in range(0, len(x), BLOCK):
        block = x[s:s + BLOCK]
        n = len(block)
        window = padded[s: s + MAX_ORDER + n]          # MAX_ORDER samples of history, then the block

        def misses(q, shift):
            guess = np.zeros(n, dtype=np.int64)
            for j in range(len(q)):
                guess += int(q[j]) * window[MAX_ORDER - 1 - j: MAX_ORDER - 1 - j + n]
            return block - (guess >> shift)

        candidates = [(0, None, 0, block)]
        for order, q in fixed.items():
            candidates.append((order, q, 0, misses(q, 0)))
        for order, coef in _weights(window, MAX_ORDER).items():
            if order in (2, 4, 6, 8):
                quantised = _quantise(coef)
                if quantised is not None:
                    candidates.append((order, quantised[0], quantised[1], misses(*quantised)))

        best = None
        for order, q, shift, e in candidates:
            u = _fold(e)
            if u.max() >= (1 << ESCAPE_BITS):
                continue
            bits, k = _best_k(u)
            bits += 8 + (8 + order * WEIGHT_BITS if order else 0)
            if best is None or bits < best[0]:
                best = (bits, order, q, shift, k, u)
        _, order, q, shift, k, u = best

        out.append((order << 4) | k)
        parts = []
        if order:
            out.append(shift)
            parts.append(((q & ((1 << WEIGHT_BITS) - 1)).astype(np.uint64),
                          np.full(order, WEIGHT_BITS, dtype=np.int64)))
        parts.append(_rice_code(u, k))
        out += np.packbits(_write_bits(parts)).tobytes()
    return bytes(out)


def decode(data, frames):
    """Slow reference decoder (mirrors the game's), for checking the packer."""
    out = np.zeros(frames + MAX_ORDER, dtype=np.int64)
    bits = np.unpackbits(np.frombuffer(data, dtype=np.uint8))
    at = 0

    def take(n):
        nonlocal at
        v = 0
        for _ in range(n):
            v = (v << 1) | int(bits[at]); at += 1
        return v

    for s in range(0, frames, BLOCK):
        at = (at + 7) // 8 * 8
        header = take(8)
        order, k = header >> 4, header & 15
        shift, q = 0, []
        if order:
            shift = take(8)
            for _ in range(order):
                v = take(WEIGHT_BITS)
                q.append(v - (1 << WEIGHT_BITS) if v >= (1 << (WEIGHT_BITS - 1)) else v)
        for i in range(s, min(frames, s + BLOCK)):
            high = 0
            while True:
                bit = bits[at]; at += 1
                if bit == 0:
                    break
                high += 1
                if high == ESCAPE:
                    break
            folded = take(ESCAPE_BITS) if high == ESCAPE else (high << k) | take(k)
            miss = (folded >> 1) ^ -(folded & 1)
            guess = 0
            for j in range(order):
                guess += q[j] * int(out[MAX_ORDER + i - 1 - j])
            out[MAX_ORDER + i] = (guess >> shift) + miss
    return out[MAX_ORDER:].astype(np.int16)


def pack(recordings, bass, rate):
    """recordings: dicts from prepare(). Returns the bank file's bytes."""
    recordings = sorted(recordings, key=lambda r: (r['note'], r['soft'], r['take']))
    blobs = [encode(r['samples']) for r in recordings]
    offset = 16 + 24 * len(recordings)
    out = bytearray(b'YGSB')
    out += struct.pack('<HHII', 1, 1 if bass else 0, rate, len(recordings))
    for r, blob in zip(recordings, blobs):
        out += struct.pack('<ffIBBBBII', r['pitch'], r['gain'], len(r['samples']), r['note'],
                           1 if r['soft'] else 0, r['string'] & 255, r['take'] & 255, offset, len(blob))
        offset += len(blob)
    for blob in blobs:
        out += blob
    return bytes(out)
