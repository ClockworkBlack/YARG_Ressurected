# Pending items

Saved for later, roughly in the order they would help most.

## Asked for, deferred
- [ ] **Bass charts** (follow the low frequencies) on all four difficulties.
- [ ] **Drum charts** (kick / snare / cymbals from the percussive hits) on all four difficulties.

## Would improve what is there
- [ ] **Play-test on real songs** in YARG on the Z Fold 7. The engine is now scored against synthetic band recordings with known answers, but no real record has been through it yet. Things to listen for: are there too many or too few notes (try the Fewer / More setting), do frets change where the music changes, do drum hits pull the chart away from the guitar.
- [ ] Confirm in-game that YARG's lyric bar shows `.chart` lyrics the way this app writes them; if not, switch lyrics to a `notes.mid` vocals track.
- [ ] Split the song into stems (guitar / bass / drums / vocals) so each instrument is charted from its own part instead of the full mix. This is the single biggest remaining step for chart quality. Needs a separation model on the phone, or a helper on the Chromebook.
- [ ] Forced strums / hammer-ons and tap sections; open notes.
- [ ] Solo sections and smarter Star Power placement (on the catchiest phrases rather than every ten measures).
- [ ] Named practice sections (Intro, Verse, Chorus) from the repeat structure instead of "Part 1, 2, 3".
- [ ] Songs that change time signature part-way, and 6/8 or 5/4.
- [ ] Shrink FLAC conversions by saving OPUS instead of WAV.
- [ ] Word-by-word lyric timing (use `<mm:ss.xx>` word stamps when an .lrc has them).
- [ ] Automatic lyrics by speech recognition for songs LRCLIB does not have.
- [ ] Convert several files in one go.
- [ ] A setting for the output folder, and a "trigger YARG scan" shortcut if the game can support one.

## Done
- [x] Charts follow pitch: same sound same fret, higher sounds higher frets, chords vs single notes.
- [x] Repeated measures and sections get the same chart.
- [x] Tempo drift, 3/4 time, beats aligned to note starts.
- [x] Sustains; Hard / Medium / Easy derived from Expert.
- [x] Note-density setting (Fewer / Standard / More).
