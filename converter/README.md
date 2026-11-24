# YARG Converter

Companion Android app for YARG Resurrected. Pick an **MP3, WAV or FLAC** and it writes a
ready-to-play song folder into `Internal storage/YARG/songs/Converted/<Artist - Title>/`:

| File | What it is |
|---|---|
| `notes.chart` | Tempo map, 5-fret guitar and bass on Easy / Medium / Hard / Expert, Star Power, practice sections, lyrics |
| `song.ini` | Title, artist, album, year, genre, length, preview point, difficulty rating |
| `song.mp3` / `.wav` / `.ogg` / `.opus` | The audio. MP3, WAV, OGG and OPUS are copied as they are; FLAC and anything else is saved as WAV |
| `album.jpg` / `.png` | Cover art, when the audio file has one embedded |
| `lyrics.lrc` | The timed lyrics that were used (handy if you convert again) |

Everything runs on the phone. The only things that go online are the optional lyrics lookup and
the one-time download of the instrument-separation model.

## Using it

1. Install the APK from this repo's **Releases** (`YARG Converter build N`).
2. **Choose audio file** (or use *Share* / *Open with* from a file manager). Title and artist are filled in from the file's tags; fix them if needed.
3. Lyrics, any of:
   - **Find online** searches [LRCLIB](https://lrclib.net) for time-synced lyrics by title and artist.
   - **Open .lrc** loads a lyrics file you already have.
   - **Paste** lyrics into the box. If they have no `[mm:ss.xx]` times, press **Tap to sync**: the song plays and you tap once as each line starts.
4. **Split into instruments** (optional, recommended). Press *Download the separation model*
   once (136 MB, Wi-Fi recommended; it resumes if interrupted). With the box ticked, the app
   first separates the song into guitar, bass, drums, vocals, piano and "other", makes the
   guitar chart from the guitar alone, and adds a bass chart. This takes roughly as long as
   the song itself, so keep the screen open.
5. **Convert and add to YARG**. The first time, Android asks for "All files access" so the app can write into the YARG folder.
6. In YARG: **Settings > Songs > Scan**.

## How the charts are made

With **Split into instruments** on, a neural network (HT-Demucs) first separates the recording
into six instruments. Tempo and beats are still taken from the full mix, where the drums are;
the guitar chart is made from the separated guitar (or, in a song with little guitar, from
guitar, keys and other pitched instruments together), and the bass chart from the separated
bass. Without it, everything below is done on the full mix and there is no bass chart.

The app listens about 86 times a second and works in five steps.

1. **Beats and measures.** It finds the overall tempo, then follows the local tempo every two
   seconds so songs that speed up or slow down stay locked. Each beat is nudged onto the nearest
   note start, bass hits and chord changes say which beat is the "one", and it tells 4/4 from 3/4.
2. **Note starts.** Every moment where new sound arrives is split into *pitched* sound (guitar,
   bass, voice, keys) and *noisy bursts* (drums). Pitched starts count fully; bare drum hits
   count less, so the chart leans towards the instruments rather than the drum kit.
3. **What each note is.** For each pitched start it reads the pitch of the new sound and takes a
   "fingerprint" of it. Starts with the same fingerprint are the same note or chord played
   again.
4. **Frets.** Within each four-measure phrase the distinct sounds are lined up from lowest to
   highest and spread over the frets. The same sound always gets the same fret, higher sounds
   get higher frets, low thick sounds become chords, and drum hits between strums repeat the
   fret before them. Notes that keep ringing become sustains.
5. **Repeats and difficulties.** Measures that repeat in the music (same rhythm, same notes) are
   pooled, so a riff played eight times gets one pattern decided by all eight hearings, and a
   verse that comes round again gets the same chart. Expert is written first; Hard, Medium and
   Easy are thinned-out versions of it (eighths, quarters, half notes, adapting to the song's
   speed) on 5, 4 and 3 frets.

The **How many notes** choice (Fewer / Standard / More) moves the bar for how strong a sound
has to be to become a note.

These are machine-made charts. Made from the full mix, expect them to follow the song's rhythm
and harmony rather than exactly what the guitarist plays; made from the separated guitar they
follow the guitar itself, as far as the separation got it right.

### The separation model

- **HT-Demucs 6-stem** (Demucs v4 by Meta AI, MIT licence): drums, bass, other, vocals, guitar, piano.
- The app uses an ONNX conversion published by a third party on Hugging Face
  (`StemSplitio/htdemucs-6s-onnx`, file `htdemucs_6s_fp16weights.onnx`), not a file from Meta
  directly. The download is pinned to one exact revision and verified against a fixed SHA-256,
  so the app only runs the file that the automated build tests.
- It runs on the phone through ONNX Runtime (Microsoft, MIT licence). It needs about 1 GB of
  memory while it runs. If it fails for any reason the app falls back to full-mix charts.
- Only measurements of the separated instruments are kept. The separated audio is not saved.

## How it is checked

`core-test/` builds synthetic "band" recordings (drums, bass, distorted rhythm guitar, lead
with vibrato, verses and choruses that repeat, tempo drift, a waltz, a busy mix with singing)
where every guitar note, beat and downbeat is known, and scores the charts against them.
Typical scores on the 4/4 songs:

| | First version | Now |
|---|---|---|
| Same note gets the same fret | 21-29% | 92-100% |
| Fret moves the way the pitch moves | 52-65% right, 24-31% wrong | 77-84% right, 0-2% wrong |
| Repeated sections get the same chart | 27-68% | 94-100% |
| Chords charted as chords | 20-24% | 98-100% |
| Waltz: measures start on the "one" | 23% (assumed 4/4) | 100% |
| Beat grid, worst error | 9-26 ms | 16-23 ms (about the same) |

"Right" is below 100% mostly because five frets cannot give every pitch its own position;
neighbouring pitches share a fret rather than going the wrong way.

For separation, three things are checked on every build:

1. **The plumbing**, with a stand-in model: cutting a song into overlapping segments and joining
   the results gives back every sample exactly once, aligned with the original.
2. **Charting from perfectly separated instruments** (the test song's true guitar and bass
   tracks): the guitar chart then contains guitar notes only (0% stray notes, against 10-11%
   from the full mix), and the bass chart follows the bass (98-100% of notes charted, frets move
   the right way 93-96% of the time, 0% the wrong way).
3. **The real model** (`model-test/`), downloaded and run through the app's own code on a
   desktop JVM in GitHub Actions:
   - on the synthetic song, drums and bass come out in the right stems (94% and 109% of their
     level) and the six stems add back up to the mix (-22 dB difference). The synthetic
     "guitar" is not recognised as a guitar by the model, so this song cannot grade guitar
     separation;
   - on a real recording ("Sax, Rock, and Roll" by Kevin MacLeod, CC BY-SA 3.0, which the
     composer lists at 128 BPM with kit, bass, electric guitar and horns): tempo found
     128.0 BPM in 4/4; the guitar stem carries 57% of the separated sound, drums 30%, bass 10%;
     the guitar chart is made from the separated guitar; separation took 0.73 times the song's
     length on a 4-core build machine. There are no "right answers" for this song, so this shows
     the whole chain working on real music, not how good the chart feels to play. The resulting
     song folder is attached to each build as the `sample-song` artifact.

## Building

GitHub Actions builds it (`.github/workflows/converter.yml`) on every change under `converter/`.
Pushes to `main` also publish the APK as a release.

The chart engine is plain Java with no Android in it (`app/src/main/java/.../core`) and has its own checks:

```
cd converter/core-test
javac -d out $(find ../app/src/main/java -path '*core*' -name '*.java') *.java
java -cp out CoreTest      # pass/fail checks
java -cp out ChartEval     # score table for every test song
```

The real-model checks need the model file and the ONNX Runtime jar; see the `model` job in
`.github/workflows/converter.yml` for the exact commands.

See [PENDING.md](PENDING.md) for what is planned next.
