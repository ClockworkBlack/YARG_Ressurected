# Converting on a computer

`ConvertSong.java` runs the phone app's conversion on a desktop JVM: the same chart code, the same
separation model and the same song folder. Audio is read with ffmpeg instead of Android's decoder,
and `notes.mid` is read back by the game's rules (`core-test/MidiCheck`) before it is kept.

    # once: the ONNX runtime jar and the model (see .github/workflows/converter.yml for versions)
    javac -cp onnxruntime.jar -d out $(find app/src/main/java -path '*core*' -name '*.java') \
        app/src/main/java/com/clockworkblack/yargconverter/stems/OrtStemModel.java \
        core-test/MidiCheck.java desktop/ConvertSong.java
    java -Xmx5g -cp out:onnxruntime.jar ConvertSong --audio song.mp3 --out songs --mode split --notes more \
        --title "Title" --artist "Artist" --album "Album" --year 1996 --genre "R&B" \
        --model htdemucs_6s.onnx --lyrics lyrics.lrc

Add `--words-model wav2vec2-base-960h.onnx` (the `onnx/model.onnx` of Xenova/wav2vec2-base-960h on
Hugging Face) to time the lyrics word by word against the voice and check them against the song's
repeats: the most accurate way there is here, and it needs no times in the lyrics at all.

`--mode` is normal, split or rnb; `--notes` fewer, standard or more; `--two-singers` guesses who sings
which line from their voices. The folder appears in `songs/Artist - Title/`; copy it into
`Internal storage/YARG/songs/` on the phone.

**Finding lyrics as the app does.** The app's lookup (LRCLIB, then NetEase when its box is ticked,
then untimed words from Lyrics.ovh) can be run here too; NetEase often has timed lyrics LRCLIB lacks.

**Checking found lyrics against the voice.** Given a timed `.lrc` instead of plain words,
`time_lyrics.py` keeps its times except where the voice clearly disagrees: a line heard starting
more than 0.8 s away takes the heard time, and a run of lines crammed together (less room than it
takes to sing them) is spread out between the nearest lines the voice confirmed. Every change is
listed.

**Lyrics with no times anywhere online.** `--vocals-out vocals.wav --wait-for-lyrics timed.lrc` writes
the separated voice and waits for a timed lyrics file. `time_lyrics.py` makes one: it recognises the
words in the voice with faster-whisper (`pip install faster-whisper`) and matches them to the plain
lyrics, line by line:

    python time_lyrics.py vocals.wav plain.txt timed.lrc small.en

Lines it could not hear are placed between their neighbours, so check repeated sections (hooks,
outros) by ear; `timed.lrc.heard` lists every word it heard with its time.

**Checking lyric timing on a song.** `--letters-out song.letters` keeps what the speech model heard,
and `--timing-report report.tsv` writes each line's heard time, final time and fit. `WordEval
song.letters lyrics.lrc <bpm> [plain]` then times the lyrics again in a second or two, without
splitting the song again: the way every change to `core/WordAligner` was measured on the test songs.
