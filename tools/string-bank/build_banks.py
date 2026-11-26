#!/usr/bin/env python3
"""Builds the game's two string banks from the original recordings.

    python3 tools/string-bank/build_banks.py \
        --guitar <clone of github.com/freepats/electric-guitar-FSBS-direct> \
        --bass   <clone of github.com/sfzinstruments/karoryfer.growlybass> \
        --out    overlay/Assets/StreamingAssets/guitar-voice

Needs numpy, scipy and soundfile. See README.md in this folder for where the recordings come
from and their licences (both are public domain, CC0).

Each note keeps one long take (for held notes) and a few shorter ones (so that the same note
played twice in a row is never the same recording), plus softly played takes for hammer-ons.
"""
import argparse
import os
import re
import sys

import numpy as np
import soundfile as sf

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bank  # noqa: E402

NOTE = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}


def note_number(name, octave):
    """'C#', 4 -> 61 (middle C is C4 = 60)."""
    number = NOTE[name[0].upper()]
    for mark in name[1:]:
        number += 1 if mark == '#' else -1 if mark == 'b' else 0
    return 12 * (octave + 1) + number


def read_mono(path):
    x, sr = sf.read(path, always_2d=True)
    return x.mean(axis=1), sr


def guitar(folder, rate=22050):
    """FreePats "Electric Guitar FSBS (direct)": a Fender recorded from its output jack.

    Files: samples/bridge/<note><octave>_s<string>[_soft]_<take>.wav, 48 kHz 24-bit, each note
    left to ring out (up to 40 seconds). Four takes picked hard; most notes also four soft."""
    folder = os.path.join(folder, 'samples', 'bridge')
    pattern = re.compile(r'^([A-G]#?)(\d)_s(\d)(_soft)?_(\d\d)\.wav$')
    takes = []
    for fn in sorted(os.listdir(folder)):
        m = pattern.match(fn)
        if not m:
            continue
        note = note_number(m.group(1), int(m.group(2)))
        takes.append((note, m.group(4) is not None, int(m.group(5)), int(m.group(3)), fn))

    recordings = []
    for note, soft, take, string, fn in takes:
        if note < 40:
            continue  # the low string tuned down to C: below anything the game plays
        if soft:
            if take > 2:
                continue
            seconds = 1.7
        elif take == 1:
            # The long take: lower strings ring longer
            seconds = 8.0 if note <= 52 else 6.0 if note <= 64 else 4.5 if note <= 76 else 3.5
        else:
            seconds = 2.1
        x, sr = read_mono(os.path.join(folder, fn))
        recordings.append(bank.prepare(x, sr, note, rate, seconds, soft=soft, string=string, take=take))
        describe(fn, recordings[-1], rate)
    return recordings, rate


def bass(folder, rate=16000):
    """Karoryfer Samples "Growlybass": a Squier Jazz Bass played hard, recorded direct.

    Files: sustain/<note><octave>_<pp|p|f|ff>_rr<take>.wav, 44.1 kHz 24-bit, six seconds each,
    every third note. The names are an octave above the real pitch ("e2" is the open low E)."""
    folder = os.path.join(folder, 'sustain')
    pattern = re.compile(r'^([a-g]b?)(\d)_(pp|p|f|ff)_rr(\d)\.wav$')
    recordings = []
    for fn in sorted(os.listdir(folder)):
        m = pattern.match(fn)
        if not m:
            continue
        note = note_number(m.group(1), int(m.group(2))) - 12
        layer, take = m.group(3), int(m.group(4))
        if note < 28:
            continue  # the low string tuned down: below anything the game plays
        if layer == 'f':
            if take > 3:
                continue
            soft, seconds = False, (6.0 if take == 1 else 2.1)
        elif layer == 'p':
            if take > 1:
                continue
            soft, seconds = True, 1.7
        else:
            continue
        x, sr = read_mono(os.path.join(folder, fn))
        recordings.append(bank.prepare(x, sr, note, rate, seconds, soft=soft, take=take))
        describe(fn, recordings[-1], rate)
    return recordings, rate


def describe(fn, r, rate):
    print(f'  {fn:<24} note {r["note"]:3d} {"soft" if r["soft"] else "hard"} take {r["take"]}  '
          f'sounds at {r["pitch"]:7.3f} ({100 * (r["pitch"] - r["note"]):+5.1f} cents)  '
          f'{len(r["samples"]) / rate:5.2f} s  gain {r["gain"]:.3f}')


def write(path, recordings, is_bass, rate):
    data = bank.pack(recordings, is_bass, rate)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(data)
    raw = sum(len(r['samples']) * 2 for r in recordings)
    seconds = sum(len(r['samples']) for r in recordings) / rate
    notes = len(set(r['note'] for r in recordings))
    print(f'{path}: {notes} notes, {len(recordings)} takes, {seconds:.0f} s at {rate} Hz, '
          f'{raw / 1e6:.1f} MB of samples packed into {len(data) / 1e6:.2f} MB')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--guitar', help='folder of the FreePats FSBS direct recordings')
    parser.add_argument('--bass', help='folder of the Karoryfer Growlybass recordings')
    parser.add_argument('--out', required=True, help='folder to write guitar.bank and bass.bank to')
    args = parser.parse_args()
    if args.guitar:
        recordings, rate = guitar(args.guitar)
        write(os.path.join(args.out, 'guitar.bank'), recordings, False, rate)
    if args.bass:
        recordings, rate = bass(args.bass)
        write(os.path.join(args.out, 'bass.bank'), recordings, True, rate)


if __name__ == '__main__':
    main()
