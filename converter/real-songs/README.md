# Real-song check

Converts real recordings the way the app does (separation model included) and checks each
result against what that song is known to produce. It exists because the synthetic test songs
only contain what we thought to put in them: every fix listed below came from a real recording
doing something the synthetic ones did not.

## What is in this folder, and what is not

- `expected.tsv`: one line per recording, with its SHA-256, the tempo it must be charted at,
  what the guitar chart must follow, and the least it must cover.
- `check.sh`: runs the check on a folder of audio files.
- **Not the recordings.** They are commercial releases and cannot be put in the repository.
  You need your own copies. Files are matched by SHA-256, so names do not matter, but it has to
  be the same file: another rip of the same song has another hash and is reported as "not in
  expected.tsv" (with its numbers, so it can be added).

Because the recordings are not here, **the automated build cannot run this check**. It runs
the synthetic songs (`core-test/`, including the trap beat built after these recordings), and
only compiles this check. Run it by hand after changing anything in how charts are made.

## Running it

```
converter/real-songs/check.sh <folder with the audio files> <htdemucs_6s.onnx> <onnxruntime.jar> [folder for the song folders]
```

Needs `ffmpeg` and a JDK. The model file and ONNX Runtime jar are the ones the `model` job in
`.github/workflows/converter.yml` downloads (the URL and checksum are at the top of that file).
The separated instruments are cached in `<folder>/.cache`, so only the first run of a song is
slow (about half the song's length); after that a song takes some 20 seconds. With a fourth
argument, each song's `notes.chart` and `song.ini` are written there (add the audio as
`song.ogg` to play it).

It ends with `All N real songs passed` or lists what failed.

## What is checked

For each recording, against its line in `expected.tsv`:

| Check | How it is measured |
|---|---|
| Tempo | within 2% of the expected BPM |
| What the guitar follows | `vocals` (a song carried by the vocals), `guitar` or `keys` |
| Guitar covers what it follows | share of vocal (or guitar) note starts with a chart note within 70 ms |
| Deep Music Mode guitar covers the instruments | the same, for the guitar + keys + other instruments |
| Bass covers the bass hits | share of bass / 808 hits with a bass note within 70 ms |
| Bass notes are on time | of those, the share within 35 ms |
| Deep Music Mode bass covers the drums | share of drum hits with a note within 70 ms |

The note starts are found by the check itself, not by the charting code: a start is where a
separated instrument suddenly gets louder. For the bass that is a jump of 2.4 dB within 25 ms,
or a click together with a jump of 1.5 dB, which also finds an 808 that is hit again while it
is still ringing (a cruder "3 dB louder" test misses those, and hid a bug for a while). The
bass level is the highest point of the wave over 30 ms, not its average power: 30 ms is barely
one cycle of a low 808, and the average wobbles enough from one window to the next to look
like hits inside a held note. Hits within 120 ms count once, so of a double hit this check
sees the first; the synthetic trap beat checks both. The
floors in `expected.tsv` are 5 points under what was measured when the line was written, so
they catch a regression, not a wobble.

These are not grades of how good a chart feels to play. Nobody has ground truth for a
commercial recording; what is checked is that the chart is where the sound is.

## Adding a recording

Put the file in your folder and run the check. It prints, for a file it does not know:

```
  row: <sha256>	<tempo>	<guitar from>	<floors...>
```

Check the tempo by ear or against the song's loop length (a loop of 2.53 s is one measure at
95 BPM), and that it is the tempo you would count (clap or snare on two and four), then add that line to `expected.tsv` followed by the artist, the title and a note on
what the recording is there for.

## The recordings, and what each one taught

| Recording | What it showed |
|---|---|
| Rio Da Yung Og, *Milwaukee Bucks* | 808s in dotted eighths made 95 BPM read as 126.7. A soft piano intro was half missing. |
| Veeze, *guillermoandrade* | A song with no guitar at all: the guitar chart had nothing to follow, hence following the vocals. Its 808 is hit again while it rings; those hits were once removed as "phantom notes". Read as 109.9 BPM until the tempo was checked against the loop. |
| Mokenstef, *It Happens* | A looping synth / flute line under the vocals: in Deep Music Mode guitar, about 90% of its clear notes. Read as 138 BPM for 69. |
| Bun B, *Get Throwed* | A "rock-leaning" song whose recording has almost no guitar in it (0.3% of the sound). Read as 124 BPM for 62. A played bass line: 71% of its hits were charted, 81% once notes strong enough on their own were kept. |
| Rio Da Yung Og, *Douche Bag* | Real guitar in the intro, a break and the ending: the guitar chart follows the instruments where they lead. Its beat grid wandered (notes 23 ms from their hits, typically), which led to the steady grid: 60% of bass notes within 35 ms before, 98% after. |
| Rio Da Yung OG & Frank Beats, *Punk Rock Guitar Session* | Read as 123.5 BPM for a 96.5 BPM loop, at no simple ratio: the tempo is now also tried from the loop length. Bass hits charted went from 43% to 99% with the 1.5 dB rule. |
| Rio Da Yung Og & Veeze, *Superstar* | An 808 hit every dotted eighth while still ringing, each hit only 2-3 dB louder: 31% of them were charted under the 4 dB rule, 96% now. Read as 190 BPM for 95: the tempo is now counted by the backbeat. Its bass frets moved the way the 808's pitch does in 15% of the changes; 92% with the pitch read from the waveform. |
| Rio Da Yung OG & RMC Mike, *Haw Thorne* | 808 double hits a 32nd note apart, closer than the chart's finest step was: bass notes may now sit on 32nd positions, and of the 213 double hits the converter finds in the 808, 186 are charted as two notes on time (none before). |
| FdnSleepy & Frank Beats ft. Rio Da Yung Og, *F Your Sister* | An 808 that is 92% of the sound. |

## Which fixes only this check guards

The synthetic trap beat fails when these are taken out: the dotted-808 tempo check, the tempo
count by the backbeat, the 1.5 dB re-hit rule, drums in Deep Music Mode bass, the loop in Deep
Music Mode guitar, guitar following the vocals, guitar following the instruments where they
lead, 32nd-note positions for the bass, the bass pitch read from the waveform, and the steady
grid.

Only the real recordings fail when these are taken out: the tempo from the loop length (Punk
Rock Guitar Session, guillermoandrade), and bass notes kept on their own strength (Get Throwed:
70% of its bass hits without, under its floor of 76%). Not guarded by either yet: the song's
tuning for bass note names, holding bass notes by loudness, treating an unsure re-hit as the
same note, and the 3 dB rule for a hit within 0.1 s of the last (see PENDING.md).
