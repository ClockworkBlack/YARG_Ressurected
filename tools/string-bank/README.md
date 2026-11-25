# String banks: the recorded guitar and bass notes

The game's "Guitar Sound On Notes" plays real recorded notes through a modelled amplifier.
The notes ship as two files, `overlay/Assets/StreamingAssets/guitar-voice/guitar.bank` and
`bass.bank` (about 4 MB and 2 MB). This folder holds the tool that makes them.

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

## How the amplifier was tuned

The bank is only the bare guitar signal. The sound comes from the amplifier model in
`GuitarVoiceAmp.cs`: valve stages, a power stage and a tone-and-speaker filter chain. The
filter chains were fitted by measurement, not by ear, with `fit_amp.py` in this folder:

- **Rock** and **Heavy**: the bank's notes were played through the model and compared, overtone
  by overtone, with recordings of real amplified guitars playing the same notes (the
  "Overdrive Guitar" and "Distortion Guitar" recordings in the FluidR3 sound bank, MIT
  licence, used for measuring only). The chain was fitted so the model's balance of low,
  middle and high lands on theirs; Rock was then given a little more body than its rather
  thin reference.
- **Clean**: FreePats also publishes the same guitar takes played through a clean amplifier
  (*Electric Guitar FSBS (clean)*, CC0). Comparing each take before and after gives what that
  amplifier does to the tone, and the Clean chain was fitted to it.

To run it again (it prints the three filter chains exactly as they stand in
`GuitarVoiceAmp.cs`, in under a minute):

```
git clone --depth 1 https://github.com/freepats/electric-guitar-FSBS-clean
sudo apt-get install fluid-soundfont-gm
python3 tools/string-bank/fit_amp.py \
    --guitar electric-guitar-FSBS-direct \
    --clean electric-guitar-FSBS-clean \
    --soundfont /usr/share/sounds/sf2/FluidR3_GM.sf2
```

The valve-stage settings at the top of `fit_amp.py` are a copy of the ones in
`GuitarVoiceAmp.cs`; change them in both places.

For scale: in those recordings of real distorted amps, 20 to 80% of a note's energy sits
between 2 and 5 kHz, where a guitar speaker peaks (least on the lowest string, most in the
middle of the neck). The game's earlier generated guitar had about 1% there on the lower
strings, which is why it sounded like a beep. The automated tests hold the model to the real
range.
