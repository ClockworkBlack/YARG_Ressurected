# YARG Converter

Companion Android app for YARG Resurrected. Pick an **MP3, WAV or FLAC** and it writes a
ready-to-play song folder into `Internal storage/YARG/songs/Converted/<Artist - Title>/`:

| File | What it is |
|---|---|
| `notes.mid` | Tempo map, 5-fret guitar and bass on Easy / Medium / Hard / Expert, Star Power, practice sections, and a **vocals part** made from the lyrics: a note under every word, for one singer or two |
| `song.ini` | Title, artist, album, year, genre, length, preview point, difficulty rating |
| `song.mp3` / `.wav` / `.ogg` / `.opus` | The audio. MP3, WAV, OGG and OPUS are copied as they are; FLAC and anything else is saved as WAV |
| `album.jpg` / `.png` | Cover art, when the audio file has one embedded |
| `lyrics.lrc` | The timed lyrics that were used, with who sings each line (handy if you convert again) |
| `vocal_parts.txt` | Only for a song with two singers: their names, shown in the game where a player picks whose lines to sing |

Older versions of the app wrote `notes.chart`. Converting a song again replaces it with
`notes.mid`; songs you don't convert again keep working as they are, but have no vocals to sing.

Everything runs on the phone. The only things that go online are the optional lyrics lookup and
the one-time download of the instrument-separation model.

## Using it

1. Install the APK from this repo's **Releases** (`YARG Converter build N`).
2. **Choose audio file** (or use *Share* / *Open with* from a file manager). Title and artist are filled in from the file's tags; fix them if needed.
3. Lyrics, any of:
   - **Find online** searches [LRCLIB](https://lrclib.net) for time-synced lyrics by title and artist.
   - **Open .lrc** loads a lyrics file you already have.
   - **Paste** lyrics into the box. If they have no `[mm:ss.xx]` times, press **Tap to sync**: the song plays and you tap once as each line starts.
   - **Two artists? Choose who sings what** (optional) opens the list of lines. Tap a line to
     pass it to the other artist, tap again for both; hold a line to give it and everything
     under it to the same artist. The names come from the Artist box ("A feat. B", "A & B")
     and can be changed there. Pasted lyrics with headers such as `[Verse 2: Travis Scott]`
     are filled in for you.
4. **Split into instruments** (optional, recommended). Press *Download the separation model*
   once (136 MB, Wi-Fi recommended; it resumes if interrupted). With the box ticked, the app
   first separates the song into guitar, bass, drums, vocals, piano and "other", makes the
   guitar chart from the guitar alone, adds a bass chart, and listens to the voice on its own
   to place every word where it is sung and find its note. Expect it to take a few minutes per
   song (not yet timed on a phone).
   With **Two artists: ... guess it from their voices** also ticked, lyrics that don't say who
   sings a line are split between the two artists by the sound of their voices. It is a guess:
   the lyrics box shows the result afterwards, so you can correct it with *Choose who sings
   what* and convert again.
5. **Convert and add to YARG**. The first time, Android asks for "All files access" so the app can write into the YARG folder.
   Once it has started you can leave the app, switch to something else or turn the screen off:
   the conversion keeps going, with its progress in a notification, and a second notification
   tells you when the song is ready. (Allow notifications when Android asks, to see them.)
   Open the app again at any time to see where it is.
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
6. To sing: the profile needs a microphone (**Add Device** on the profile). Pick **Vocals** as
   the instrument to sing the whole song, or **Harmonies** on a two-artist song to choose one
   artist's lines. The level you pick sets how exact you have to be (see the game's README).

**Doing it from a computer, or moving songs to another phone**

Copy the whole song folder (the one with `notes.mid`, `song.ini` and the audio in it) into
`Internal storage/YARG/songs/` on the phone, keeping the folder together, then scan as above.
A folder with only some of the files won't show up.

**If the song doesn't show up**

- Scan again after the converter says **Done**; a scan that ran before then won't have it.
- Check the folder in a file manager: it must contain `notes.mid`, `song.ini` and a `song.*`
  audio file. If it is empty, the conversion was cancelled: convert again.
- If a scan ends with a **"Some Songs Could Not Be Scanned"** message, it lists the songs it
  couldn't read and why.
- If YARG lost All files access (Android can take it away after an update or reinstall), give it
  back as in step 1 above and scan again.
- Converting the same song again replaces the old folder; scan again to see the new chart.

## Changing a song's notes

A converted chart can be fixed by hand without converting the song again: press **Edit a
converted song's notes** at the bottom of the converter's screen and pick the song.

- The buttons at the top choose the instrument (tap to switch between guitar and bass) and the
  level (**E**asy, **M**edium, **H**ard, E**x**pert).
- **Tap** an empty spot in a lane to add a note there. Tapping beside a note on another fret
  makes it a chord. **Tap a note** to take it away.
- **Hold a note** until the phone buzzes, then slide up to hold it longer (slide back down to
  shorten it).
- **Drag** up and down to move through the song, **pinch** to zoom. **Play** plays the music
  from where you are, with the notes scrolling to the white line like in the game.
- **Snap** sets where new notes land: on the beat (1/4), half beats (1/8), quarter beats (1/16)
  or eighths of a beat (1/32).
- **Undo** takes back the last change, as many times as you like.
- **More** can copy all of Expert (or Hard) onto the level you are on, squeezed onto the frets
  that level uses, so you can make an easier level by taking notes away.
- **Save**, then in YARG scan for songs again (as after a conversion) to play the new notes.
  The first save keeps the original file as `notes.mid.bak` in the song's folder; to go back to
  it, delete `notes.mid` and rename `notes.mid.bak` to `notes.mid`.

Only the guitar, bass (and rhythm, co-op or keys, if a song has them) notes change. Vocals,
lyrics, tempo, star power and sections are saved exactly as they were, so it also works on
songs from other charting tools that use `notes.mid`. Converting the same song again replaces
the folder, edits included.

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

### How the vocals are made

Lyrics give the words and when each line starts. What turns them into something to sing
depends on whether the song was split into instruments.

**With the split**, the app listens to the voice on its own a hundred times a second: how loud
it is and which note it is on.

1. **Where the line really is.** A lyric file's time stamp is close, not exact, so the start of
   each line slides to where that burst of singing begins, and the line ends at the last sound
   before a pause.
2. **Where each word is.** Every point where a new sound starts (the voice comes in after a
   silence, gets suddenly louder, or jumps to another note) is a likely start of a word, and a
   word should last about as long as its syllables suggest. The words are placed to fit both as
   well as possible over the whole line.
3. **Sung or rapped.** Singing holds notes; rap and speech never settle on one. A line that
   spends a fair share of its time on held notes is **sung**: each word gets the note it is
   sung on, and a long word that moves to other notes gets a joined-on note for each. Anything
   else is **rapped or spoken**: its words become "talkies", where any sound in time with the
   words counts and the note does not matter.
4. Lines longer than nine words are shown a few words at a time, and every sixth phrase or so
   earns Star Power.

**Without the split** there is nothing to listen to, so every line is a talkie, spread from its
time stamp at a natural speaking pace. Good for rapping along; for real notes, use the split.

**Two singers.** Lines marked for the first artist, the second, or both (see *Choose who sings
what*) are written as two parts, so in the game each player picks an artist and sings only that
artist's lines; the whole song is still there as one part for anyone who wants all of it.
The optional guess from the voices compares the colour of the voice line by line, sorts the
lines into the two groups that differ most, and smooths the result (singers trade verses more
often than single lines). It only offers a split when the two groups are clearly apart, and it
can be fooled by voices that sound alike or by one artist who both sings and raps.

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

The vocals have the same kind of check with a made-up singer (`SynthVoice`): lines sung on
known notes, rapped lines with gliding pitch, long words that change note, words that run into
each other, two different "voices", time stamps that come early. On those:

| | Result |
|---|---|
| Which note a steady voice is on | within 0.05 of a semitone, from D2 to C6 |
| Sung and rapped lines told apart | every line (on real recordings: every line of one sung song; see below for rap) |
| Sung words on the right note / starting within 0.08 s | 100% / 100% |
| Rapped words starting within 0.08 s | 100% |
| A long word moving to a second note is found, with the right note | 12 of 12 |
| Two voices trading verses told apart | 51 of 53 lines; one voice alone is left as one |

A made-up singer is far cleaner than a real one, so these numbers show the method works, not
how well it does on a record. One real recording goes through the whole chain on every build
(below): "Swansong" by Josh Woodward, one man singing over a guitar, with the time-synced
lyrics the app itself finds on LRCLIB. On it, all 28 sung lines come out as sung (the lyrics
file's 29th line is the note "[Chorus x 2]", which is now skipped), the voice is on a clear note
90% of the time it sounds, the notes stay in one key and move by steps the way a tune does, and
the one singer is not split into two. Nobody has checked the result by ear or played it yet.

Rap is measured on every build too, on real recordings from Wikimedia Commons: three short
rap clips, and five hip hop songs from the Free Music Archive put through the same instrument
split as in the app. That showed something the made-up singer did not: rap separated from its
beat often holds a few notes (scores of 0.16 to 0.29 on the "held notes" measure, against 0.38
and up for nearly all sung lines and 0.00 for the made-up rapper). So the decision now works
in three bands. Under 0.15 a line is rapped and over 0.30 it is sung, whatever else happens.
In between, the line follows the same words elsewhere in the song (a chorus that comes round
again), or else what most of the song clearly is, and a song only counts as a sung one with a
clear majority, so the rapped verse of a song with a sung hook stays rapped. In those five
songs a handful of lines scored 0.38 to 0.64 and count as sung; they are probably sung hooks,
but nobody has listened to check.

Every song the checks write is also read back from `notes.mid` by a reader that follows the
game's own rules (how it pairs notes, finds phrases, attaches lyrics, and decides a song has
vocals at all), and compared note for note with what was meant to be written.

For separation, three things are checked on every build:

1. **The plumbing**, with a stand-in model: cutting a song into overlapping segments and joining
   the results gives back every sample exactly once, aligned with the original.
2. **Charting from perfectly separated instruments** (the test song's true guitar and bass
   tracks): the guitar chart then contains guitar notes only (0% stray notes, against 10-11%
   from the full mix), and the bass chart follows the bass (every bass note charted, frets
   move the right way 93-96% of the time, 0% the wrong way).
3. **The real model** (`model-test/`), downloaded and run through the app's own code on a
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
     song folder is attached to each build as the `sample-song` artifact;
   - on a real recording with singing ("Swansong" by Josh Woodward, CC BY 3.0, 4 min 21 s):
     separated in 0.71 times the song's length, the voice carries 46% of the separated sound,
     and the lyrics (30 timed lines from LRCLIB) become 36 phrases with 214 words to sing. This
     song folder is in the `sample-song` artifact too, vocals included.

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
