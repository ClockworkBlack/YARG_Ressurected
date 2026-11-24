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

- **Beats**: the app measures how much new sound arrives 86 times a second, finds the tempo, then follows the beat through the song and writes a tempo map that keeps the highway locked to the music.
- **Notes**: every clear note start in the mix becomes a candidate. Expert keeps sixteenth notes, Hard eighths, Medium quarters, Easy half notes (each adapts to the song's speed), always preferring the strongest sounds and the main beats.
- **Frets**: higher-pitched moments go to higher frets, relative to the notes around them. Easy uses 3 frets, Medium 4, Hard and Expert 5.
- **Extras**: chords on the hardest hits, sustains where a note rings, Star Power every ten measures.

These are machine-made charts from the full mix. They follow the rhythm of the song well, but they are not what a human charter would write for the guitar part specifically.

## Building

GitHub Actions builds it (`.github/workflows/converter.yml`) on every change under `converter/`.
Pushes to `main` also publish the APK as a release.

The chart engine is plain Java with no Android in it (`app/src/main/java/.../core`) and has its own checks:

```
cd converter/core-test
javac -d out $(find ../app/src/main/java -path '*core*' -name '*.java') CoreTest.java
java -cp out CoreTest
```

See [PENDING.md](PENDING.md) for what is planned next.
