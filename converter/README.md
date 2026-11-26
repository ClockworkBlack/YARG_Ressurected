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
   guitar chart from the guitar alone, and adds a bass chart. Expect it to take a few minutes per
   song (not yet timed on a phone), so keep the screen open.
5. **Convert and add to YARG**. The first time, Android asks for "All files access" so the app can write into the YARG folder.
6. Add the song to the game (next section).

## Adding a converted song to YARG

The converter already puts the song where YARG looks for songs, so there is nothing to copy or
unzip. YARG just has to scan for it once.

**Once per install of YARG Resurrected**

1. Open YARG and allow **All files access** when Android asks (or later: Android **Settings >
   Apps > YARG > Permissions > Files and media > Allow management of all files**). Without it the
   game can't see `Internal storage/YARG/songs`, where the converter writes.
2. Check the folder is in the game's list: **Settings > Songs**. `/storage/emulated/0/YARG/songs`
   should be listed under song folders (YARG Resurrected adds it by itself). If it isn't, press
   **Add Folder** and pick `Internal storage > YARG > songs`. Don't add the `Converted` folder on
   its own: subfolders are scanned anyway, and adding it twice shows every song twice.

**After every conversion**

1. Wait for the converter's **Done** message. It shows where the song was saved:
   `Internal storage/YARG/songs/Converted/<Artist - Title>/`.
2. Open YARG (if it was already open, it won't notice the new song by itself).
3. Scan, either way:
   - **Settings > Songs > Scan Songs**, or
   - in the music library (**Quickplay**), open **More Options** (orange button / LB on a
     gamepad) and choose **Scan Songs**.
4. Find the song: in the music library press **Search** and type the title, or sort by
   **Charter** and look under **YARG Converter (auto)**, where all converted songs are grouped.
5. Pick a profile and difficulty and play. Guitar and bass charts are in the song on
   Easy / Medium / Hard / Expert; vocals only if lyrics were added.

**Doing it from a computer, or moving songs to another phone**

Copy the whole song folder (the one with `notes.chart`, `song.ini` and the audio in it) into
`Internal storage/YARG/songs/` on the phone, keeping the folder together, then scan as above.
A folder with only some of the files won't show up.

**If the song doesn't show up**

- Scan again after the converter says **Done**; a scan that ran before then won't have it.
- Check the folder in a file manager: it must contain `notes.chart`, `song.ini` and a `song.*`
  audio file. If it is empty, the conversion was cancelled: convert again.
- If a scan ends with a **"Some Songs Could Not Be Scanned"** message, it lists the songs it
  couldn't read and why.
- If YARG lost All files access (Android can take it away after an update or reinstall), give it
  back as in step 1 above and scan again.
- Converting the same song again replaces the old folder; scan again to see the new chart.

## How the charts are made

With **Split into instruments** on, a neural network (HT-Demucs) first separates the recording
into six instruments. Tempo and beats are still taken from the full mix, where the drums are;
the guitar chart is made from the separated guitar (or, in a song with little guitar, from
guitar, keys and other pitched instruments together), and the bass chart from the separated
bass. Without it, everything below is done on the full mix and there is no bass chart.

**Songs carried by the vocals** (rap over a beat, for example) have little or nothing for a
guitar chart to follow once the drums and bass are taken out. When the pitched instruments
are under 35% of the pitched instruments and vocals together, the **guitar chart follows the
vocals**, as single notes (a voice sings one note at a time, and its breathy sound would
otherwise turn into chords). In measures without vocals (intro, breaks, outro) it follows the
instruments instead, with half the usual bar for how strong a note must be, since an intro
is usually softer than the rest of the song. Songs with a real guitar part don't reach the
cut-off and are charted as before.

**Deep Music Mode parts.** Every song split into instruments also gets a second guitar and bass
part that cover the whole song, for playing with no drum or vocal chart. They are written as
the co-op guitar (`DoubleGuitar`) and rhythm (`DoubleRhythm`) parts, which the game's **Deep
Music Mode** modifier plays in place of guitar and bass:

- **Guitar** adds the vocals and the other instruments' melody (a chime, a keys loop), charted
  separately and merged in wherever they don't fall right next to a guitar note. In a song
  carried by the vocals that is the vocals plus the beat's melody all the way through.
- **Bass** adds the drum hits between its notes. A hit takes the fret of the bass note that is
  sounding, and a bright hit (snare, clap, hat) the fret next to it; a held note ends where a
  drum hit comes in.

On two rap tracks, Deep Music Mode guitar covers 85-92% of the chime/keys notes (normal guitar:
64-71%) and 84-87% of the rapped syllables, at about 4-5 notes a second on Expert; Deep Music
Mode bass covers 81% of the main drum hits on one, and every other trap hi-hat on the other.
The Done message gives how many notes each part adds.

**Bass charts** (from the separated bass) read each note's name allowing for the song's own
tuning, which is often not A = 440 Hz. A long note (a trap 808, a held synth bass) pumps and
buzzes while it rings; a start on the same note only counts as a new note when the sound gets
clearly louder again (4 dB) or the note had already faded, and a change of note without a new
attack needs a sure pitch reading, since a distorted 808 often reads as its own fifth or octave.
A note that starts with a click has its pitch read a little later, once its tone settles.
Notes are held for as long as they stay loud (until 15 dB down). Sounds far below the song's
usual bass level (-24 dB) are other instruments leaking into the bass and are left out.

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

For separation, four things are checked on every build:

1. **The plumbing**, with a stand-in model: cutting a song into overlapping segments and joining
   the results gives back every sample exactly once, aligned with the original.
2. **Charting from perfectly separated instruments** (the test song's true guitar and bass
   tracks): the guitar chart then contains guitar notes only (0% stray notes, against 10-11%
   from the full mix), and the bass chart follows the bass (every bass note charted, frets
   move the right way 93-96% of the time, 0% the wrong way).
3. **A song carried by the vocals**: the busy test song's drums, bass and singing with only a
   trace of guitar. The guitar chart must follow the vocals, with no chords on any difficulty,
   and chart at least 80% of the sung notes (it charts all of them); the unsung intro, choruses
   and outro must follow the instruments instead. The full band songs, which have singing too,
   must still chart guitar from the guitar.
4. **The real model** (`model-test/`), downloaded and run through the app's own code on a
   desktop JVM in GitHub Actions:
   - on the synthetic song, drums and bass come out in the right stems (94% and 109% of their
     level) and the six stems add back up to the mix (-22 dB difference). The synthetic
     "guitar" is not recognised as a guitar by the model, so this song cannot grade guitar
     separation;
   - on a real recording ("Sax, Rock, and Roll" by Kevin MacLeod, CC BY-SA 3.0, which the
     composer lists at 128 BPM with kit, bass, electric guitar and horns): tempo found
     128.0 BPM in 4/4; the guitar stem carries 57% of the separated sound, drums 30%, bass 10%;
     the guitar chart is made from the separated guitar; separation took between 0.45 and 0.75
     times the song's length on a 4-core build machine, depending on the run. There are no "right answers" for this song, so this shows
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
