# YARG Converter

Companion Android app for YARG Resurrected. Pick an **MP3, WAV or FLAC** and it writes a
ready-to-play song folder into `Internal storage/YARG/songs/Converted/<Artist - Title>/`:

| File | What it is |
|---|---|
| `notes.chart` | Tempo map, 5-fret guitar on Easy / Medium / Hard / Expert, Star Power, practice sections, lyrics |
| `song.ini` | Title, artist, album, year, genre, length, preview point, difficulty rating |
| `song.mp3` / `.wav` / `.ogg` / `.opus` | The audio. MP3, WAV, OGG and OPUS are copied as they are; FLAC and anything else is saved as WAV |
| `album.jpg` / `.png` | Cover art, when the audio file has one embedded |
| `lyrics.lrc` | The timed lyrics that were used (handy if you convert again) |

Everything runs on the phone. The only thing that goes online is the optional lyrics lookup.

## Using it

1. Install the APK from this repo's **Releases** (`YARG Converter build N`).
2. **Choose audio file** (or use *Share* / *Open with* from a file manager). Title and artist are filled in from the file's tags; fix them if needed.
3. Lyrics, any of:
   - **Find online** searches [LRCLIB](https://lrclib.net) for time-synced lyrics by title and artist.
   - **Open .lrc** loads a lyrics file you already have.
   - **Paste** lyrics into the box. If they have no `[mm:ss.xx]` times, press **Tap to sync**: the song plays and you tap once as each line starts.
4. **Convert and add to YARG**. The first time, Android asks for "All files access" so the app can write into the YARG folder.
5. In YARG: **Settings > Songs > Scan**.

## How the charts are made

The app listens to the whole mix about 86 times a second and works in five steps.

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

These are machine-made charts from the full mix, not from the isolated guitar track. On the
test songs (see below) they follow the guitar part closely; on a dense real recording expect
them to follow the song's rhythm and harmony rather than exactly what the guitarist plays.

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

See [PENDING.md](PENDING.md) for what is planned next.
