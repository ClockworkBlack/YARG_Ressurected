#!/usr/bin/env python3
"""Writes GuitarVoiceCabinets.cs: the measured speaker cabinets the game's amplifier plays through.

A speaker cabinet with a microphone in front of it is captured as its answer to a single click
(an "impulse response"). Playing the amplifier's output through that answer gives the sound of
that very cabinet. The two used here were measured by Jester Dyne Productions (Bastian
Karschewski) and are public domain (CC0); see README.md in this folder for where to get them.

    python3 tools/string-bank/make_cabinets.py \
        --emerald "Emerald Pack 1.0/Impulses/44.1kHz" \
        --brutal "Jesters_Brutal_Pack_1.0/Impulses/44.1kHz" \
        --out overlay/Assets/Script/Audio/GuitarVoice/GuitarVoiceCabinets.cs

Each response is cut to its first 46 ms (the rest is the recording room, more than 30 dB down),
faded out over its last quarter, set to unit energy and stored as 16-bit numbers.
"""
import argparse
import base64
import os
import textwrap

import numpy as np
import soundfile as sf

RATE = 44100
LENGTH = 2048
FADE = LENGTH // 4

# name in the game, pack, file, what it is
CABINETS = [
    ('Greenback', 'emerald', '1_Nacho_Guacamole_44.wav',
     'A Marshall 1960AX cabinet (4x12, built 1998) with Celestion Greenback G12M 25 W speakers,\n'
     'Shure SM57 microphone, dry room. From "Jester\'s Emerald Pack" (1: Nacho Guacamole).'),
    ('Vintage30', 'brutal', '1_Cookie_Monster_44.wav',
     'An oversized 4x12 cabinet with a Celestion Vintage 30 speaker, Shure SM57 microphone.\n'
     'From "Jester\'s Brutal Pack" (1: Cookie Monster).'),
]


def prepare(path):
    """-> (16-bit samples, what to multiply them by)"""
    data, rate = sf.read(path, always_2d=True)
    if rate != RATE:
        raise SystemExit('%s: expected %d Hz, found %d' % (path, RATE, rate))
    h = data[:LENGTH, 0].astype(np.float64)
    if len(h) < LENGTH:
        h = np.concatenate([h, np.zeros(LENGTH - len(h))])
    h[-FADE:] *= 0.5 * (1 + np.cos(np.pi * np.arange(FADE) / FADE))
    h /= np.sqrt(np.sum(h * h))
    peak = np.abs(h).max()
    return np.round(h / peak * 32767).astype('<i2'), peak / 32767


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('--emerald', required=True, help='folder with the Emerald Pack 44.1 kHz files')
    parser.add_argument('--brutal', required=True, help='folder with the Brutal Pack 44.1 kHz files')
    parser.add_argument('--out', required=True, help='the C# file to write')
    args = parser.parse_args()
    folders = {'emerald': args.emerald, 'brutal': args.brutal}

    lines = [
        '// Guitar voice, part 2c: the speaker cabinets.',
        '//',
        '// WRITTEN BY tools/string-bank/make_cabinets.py. Do not edit by hand: run the tool again.',
        '//',
        '// A guitar speaker is the biggest part of an electric guitar\'s sound, and far too irregular',
        '// to describe with a few tone controls. So these are measurements of real ones: how a',
        '// cabinet, with a microphone in front of it, answers a single click. Playing the amplifier',
        '// through that answer (GuitarVoiceAmp) puts it through that cabinet.',
        '//',
        '// Measured by Jester Dyne Productions (Bastian Karschewski) and given to the public domain',
        '// (Creative Commons CC0 1.0). The first %d samples (%d ms at %d Hz) of each, as 16-bit'
        % (LENGTH, round(1000 * LENGTH / RATE), RATE),
        '// numbers packed in base64; see tools/string-bank/README.md.',
        '',
        'using System;',
        '',
        'namespace YARG.Audio.GuitarVoice',
        '{',
        '    internal static class Cabinets',
        '    {',
        '        /// <summary>Samples in each response (at %d Hz).</summary>' % RATE,
        '        public const int LENGTH = %d;' % LENGTH,
        '',
    ]
    packed = []
    for name, pack, filename, about in CABINETS:
        samples, scale = prepare(os.path.join(folders[pack], filename))
        packed.append((name, samples, scale))
        lines.append('        /// <summary>')
        for row in about.split('\n'):
            lines.append('        /// ' + row)
        lines.append('        /// </summary>')
        lines.append('        public static readonly float[] %s = Unpack(%s_SCALE, %s_DATA);'
                     % (name, name.upper(), name.upper()))
        lines.append('')

    lines += [
        '        private static float[] Unpack(double scale, string packed)',
        '        {',
        '            var bytes = Convert.FromBase64String(packed);',
        '            var response = new float[bytes.Length / 2];',
        '            for (int i = 0; i < response.Length; i++)',
        '            {',
        '                short sample = unchecked((short) (bytes[2 * i] | bytes[2 * i + 1] << 8));',
        '                response[i] = (float) (sample * scale);',
        '            }',
        '            return response;',
        '        }',
    ]
    for name, samples, scale in packed:
        text = base64.b64encode(samples.tobytes()).decode('ascii')
        rows = textwrap.wrap(text, 100)
        lines.append('')
        lines.append('        private const double %s_SCALE = %s;' % (name.upper(), repr(float(scale))))
        lines.append('        private const string %s_DATA =' % name.upper())
        for i, row in enumerate(rows):
            lines.append('            "%s"%s' % (row, ';' if i == len(rows) - 1 else ' +'))
    lines += ['    }', '}', '']

    with open(args.out, 'w', newline='\n') as f:
        f.write('\n'.join(lines))
    print('wrote', args.out, 'with', ', '.join(name for name, _, _ in packed))


if __name__ == '__main__':
    main()
