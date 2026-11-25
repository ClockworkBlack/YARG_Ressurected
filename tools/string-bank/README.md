# String banks: the recorded guitar and bass notes

The game's "Guitar Sound On Notes" plays real recorded notes through a modelled amplifier and
a measured speaker cabinet. The notes ship as two files,
`overlay/Assets/StreamingAssets/guitar-voice/guitar.bank` and `bass.bank` (about 4 MB and 2 MB).
This folder holds the tools that make them and the cabinets, and the notes on how the sound
was tuned.

## Where the recordings come from

Both sets are recordings taken straight from the instrument's output (no amplifier, no
effects), and both are **public domain: Creative Commons CC0 1.0**.

| Bank | Source | Instrument | Version used |
|---|---|---|---|
| `guitar.bank` | [FreePats: Electric Guitar FSBS (direct)](https://freepats.zenvoid.org/ElectricGuitar/clean-electric-guitar.html), repository [freepats/electric-guitar-FSBS-direct](https://github.com/freepats/electric-guitar-FSBS-direct) | A Fender electric guitar, bridge pickup | 2022-09-11 (commit `4c1d546`) |
| `bass.bank` | [Karoryfer Samples: Growlybass](https://shop.karoryfer.com/pages/free-growlybass), repository [sfzinstruments/karoryfer.growlybass](https://github.com/sfzinstruments/karoryfer.growlybass) | A Squier Jazz Bass with round-wound strings, played hard with the fingers | v1.002 (commit `4f48326`) |

CC0 asks for nothing, but the people who made these deserve the credit: Roberto Gordo Saez and
the FreePats contributors, and Karoryfer Lecolds.

## What goes into a bank

Every recorded note keeps several takes, because no two picks of a real string sound alike:

- **guitar**: 18 notes from the open low E up to the C sharp three octaves above (never more
  than four semitones apart). For each: one long take picked hard (up to 8 seconds on the low
  strings, for held notes), three more hard takes of 2 seconds, and, where the source has
  them, two softly played takes for hammer-ons and pull-offs. 94 takes, 22.05 kHz.
- **bass**: 13 notes from the open low E up, two or three semitones apart. For each: one long
  take (6 seconds), two more of 2 seconds, and one softly played take. 52 takes, 16 kHz.

For each take the tool:

1. finds where the pick lets go of the string (the recordings begin with up to 50 ms of the
   pick touching it) and starts 2.5 ms before that, so notes land on the beat;
2. measures the pitch the take really sounds at. A string is never dead on the note (a hard
   pick is sharper than a soft one, and this bass's low E sits a quarter of a semitone high),
   so the game tunes every take separately;
3. cuts it where the note has died away, or at the lengths above, and fades the end;
4. makes every take equally loud over its first third of a second;
5. stores it as 16-bit samples, packed without loss to about 40% of that size (each sample
   is predicted from up to eight before it and only the miss is stored; see `bank.py`).

The file layout is described at the top of `bank.py`. The game reads it with `StringBank.Load`
in `overlay/Assets/Script/Audio/GuitarVoice/GuitarVoiceStrings.cs`, and the automated tests
(`tests/RemoteServerTests`) check both files on every build.

## Rebuilding

```
git clone --depth 1 https://github.com/freepats/electric-guitar-FSBS-direct
git clone --depth 1 https://github.com/sfzinstruments/karoryfer.growlybass
pip install numpy scipy soundfile
python3 tools/string-bank/build_banks.py \
    --guitar electric-guitar-FSBS-direct \
    --bass karoryfer.growlybass \
    --out overlay/Assets/StreamingAssets/guitar-voice
dotnet run --project tests/RemoteServerTests -c Release -- <cert> <demo folder>
```

With numpy 2.5 and scipy 1.18 the result is the same two files byte for byte (other versions
may round a sample differently here and there). To use other recordings, add a function like
`guitar()` or `bass()` in `build_banks.py`: it only has to hand `bank.prepare` one note at a
time with the note that was played.

## The speaker cabinets

The guitar amplifier plays through measurements of real speaker cabinets: what came out of a
microphone in front of the cabinet when a single click went in. Both are from **Jester Dyne
Productions** (Bastian Karschewski), who gave them to the public domain (**CC0 1.0**, stated in
the handbook inside each pack):

| In the game | Pack | Measurement | What it is |
|---|---|---|---|
| `Cabinets.Greenback` (Clean, Rock) | [Jester's Emerald Pack 1.0](https://www.jester-dyne-productions.com/) | 1, "Nacho Guacamole" | Marshall 1960AX 4x12 (1998), Celestion Greenback G12M 25 W, Shure SM57, dry room |
| `Cabinets.Vintage30` (Heavy) | [Jester's Brutal Pack 1.0](https://www.jester-dyne-productions.com/) | 1, "Cookie Monster" | Oversized 4x12, Celestion Vintage 30, Shure SM57 |

`make_cabinets.py` takes the first 46 ms of each (2048 samples at 44.1 kHz; what follows is the
recording room, more than 30 dB down), fades the end out, sets it to unit energy and writes it
as 16-bit numbers into `overlay/Assets/Script/Audio/GuitarVoice/GuitarVoiceCabinets.cs`. To make
that file again, download and unpack the two packs, then:

```
python3 tools/string-bank/make_cabinets.py \
    --emerald "Emerald Pack 1.0/Impulses/44.1kHz" \
    --brutal "Jesters_Brutal_Pack_1.0/Impulses/44.1kHz" \
    --out overlay/Assets/Script/Audio/GuitarVoice/GuitarVoiceCabinets.cs
```

The bass amp keeps a speaker drawn with a few filters (a bass cabinet is far plainer).

## How the sound was tuned

By measuring, because the first versions sounded like a beep with a buzz on it, and the reason
turned out to be measurable. A note sounds like a guitar when the note itself (its fundamental)
is among its strongest parts and the speaker's "bite" between 2 and 5 kHz sits on top of that.
The earlier sound had it the other way round: the fundamental 9 to 15 dB *below* the strongest
overtone, and in the middle of the neck 55 to 70% of the energy between 2 and 5 kHz. Three
things caused it, and each has its own fix:

1. **The recordings.** The guitar was recorded from its bridge pickup, which sits where a string
   hardly moves at its fundamental, so on the thin strings the overtones are several times
   stronger than the note. The four takes of each note were also picked very differently: on
   one and the same note the fundamental's share of the first eight overtones runs from 3% to
   19% (the open A) or from 10% to 39% (the C sharp on the B string), and the open B and high E
   strings have takes with over half their energy in the fifth overtone and above. `StringVoicing` (in `GuitarVoiceStrings.cs`) measures every take once,
   when the bank is read, and balances it: some top back on the dull wound strings (10, 6 and
   3 dB above 700 Hz on the low E, A and D), the top turned down on a take with more than a
   quarter of its energy in the fifth overtone and up (at most 10 dB), and the fundamental
   turned up until it carries a third of the first eight overtones (at most 12 dB, less towards
   the lowest notes, none at the low E). Each take is then as loud as it was.
2. **The speaker.** A speaker drawn with a handful of filters had been fitted to recordings from
   a General MIDI sound bank, which are thin themselves. The measured cabinets above have 3 to
   7 dB more level around 100 to 200 Hz than in the middle, and a broad rise at 2 to 4 kHz
   rather than a sharp peak.
3. **The amplifier.** It held back everything below 500 to 600 Hz before the valves (by 8 to
   9 dB), which keeps chords clear but takes the body out of single notes. It now holds back
   only what is below 400 Hz (by 6 dB on Rock, 8 on Heavy), and Rock is driven half as hard, so
   that a note still dies away as it does on recordings of overdriven amps.

For comparison while tuning, FreePats' own distorted version of the same takes (*Electric
Guitar FSBS (dist2)*, CC0) was measured the same way: in the middle of the neck its fundamental
is level with the strongest overtone or up to 8 dB below it.

What comes out now, and what the automated tests hold it to (single notes up the neck, four
takes each, Rock and Heavy): the fundamental within 3.5 dB of the strongest overtone on every
note (the tests allow 8), and 18% (Rock) and 25% (Heavy) of the energy between 2 and 5 kHz on
average, 34% and 37% on the brightest note (the tests allow 55%).

The amplifier's settings are in `AmpSettings` in `GuitarVoiceAmp.cs`. There is no fitting
script any more: change a setting, run the tests, and read the balance they print.
