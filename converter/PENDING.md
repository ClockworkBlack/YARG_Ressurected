# Pending items

Saved for later, roughly in the order they would help most.

## Asked for, deferred
- [ ] **Drum charts** from the separated drum stem (kick / snare / toms / cymbals) on all four difficulties.

## Follow-ups to instrument separation
- [ ] **Time it on the Z Fold 7.** On a 4-core build machine separation takes 0.45 to 0.75 times the song's length; the phone has not been measured.
- [ ] **Save the separated instruments as audio** (guitar / bass / drums / vocals files in the song folder) so YARG can drop the guitar when you miss notes and set per-instrument volume. Needs an on-phone Opus encoder so each song does not grow by hundreds of MB.
- [ ] Keep converting with the screen off or the app in the background (foreground service with a notification).
- [ ] Use the separated vocals to time pasted lyrics automatically, word by word.
- [ ] Keys chart from the piano stem.
- [ ] A faster, lighter model as a "quick" option (Spleeter 4-stem, about 79 MB).
- [ ] Let the user pick which instrument the "guitar" chart follows when a song has no guitar (the automatic vocals choice below covers rap and other voice-led songs).
- [ ] Play-test vocal-led charts (rap, R&B) in YARG; adjust the 35% cut-off if songs with a quiet guitar get switched, or rap songs with loud synths don't.

## Would improve what is there
- [ ] Regular guitar in songs carried by the vocals covers a beat's loop only where the instruments lead (about 68% of the loop's clear notes on "It Happens"); Deep Music Mode guitar covers about 90%. If the regular guitar should carry the loop too, make it an option.
- [ ] Flute / pipe / synth lines that glide between notes: their notes are found from the change in sound (spectral flux); a pitch-based legato detector was tried and added nothing measurable, so it was left out.
- [ ] Play-test Deep Music Mode on band songs (it is written for every song now; tuned on rap songs).
- [ ] Trap hi-hat rolls: the bass takes about every other hat; a setting could take all of them on Expert, or none.
- [ ] Bass on played (not 808) bass lines: about two thirds to four fifths of the hits are charted ("It Happens" 65%, "Get Throwed" 81%), against 74-100% on 808s. Notes that begin with a smooth change of pitch and no attack are the ones missed; adding a note at every such change was tried and put a third more notes into 808 songs (an 808 drops in pitch after every hit), with their frets no longer following the note.
- [ ] "Get Throwed" in Deep Music Mode: its busy bass leaves room for half of the drum hits (50%, from 60% before bass notes could sit on 32nd positions).
- [ ] Bass frets on two songs still follow the pitch only partly ("Haw Thorne": the fret moves the right way in 63% of the changes; "Get Throwed" 53%).
- [ ] The tempo count by the backbeat (half / twice) is only applied to songs carried by the vocals. A rap song with a clap on every beat and a tempo under 80 BPM would be counted at twice its tempo; none of the nine test songs is like that.
- [ ] Not guarded by a check: the 3 dB rule for a hit within 0.1 s of the last. Without it "Haw Thorne" gets 19 more bass notes and its frets follow the pitch in 53% of the changes instead of 63%.
- [ ] Bass on rock songs: the 808 rules cost a few real notes on a rock bass ("Sax, Rock, and Roll": 74% -> 69% of bass notes charted, while frets moving the right way went 61% -> 82%). Smooth note changes (walking lines, hammer-ons) need a surer pitch reading to count; a better tell between a slide and a misread would win them back.
- [ ] **Play-test on real songs** in YARG on the Z Fold 7. The engine is now scored against synthetic band recordings with known answers, but only one real record has been through it (in the automated build, with nobody listening). Things to listen for: are there too many or too few notes (try the Fewer / More setting), do frets change where the music changes, do drum hits pull the chart away from the guitar.
- [ ] Confirm in-game that YARG's lyric bar shows `.chart` lyrics the way this app writes them; if not, switch lyrics to a `notes.mid` vocals track.
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
- [x] Instrument separation on the phone (HT-Demucs 6-stem); guitar charted from the guitar stem.
- [x] Bass charts on all four difficulties, from the bass stem.
- [x] Songs carried by the vocals: guitar chart from the instruments and vocals together, single notes only.
- [x] In those songs, measures without vocals (piano intro, breaks) charted from the instruments with a lower note threshold.
- [x] Deep Music Mode parts (co-op guitar / rhythm slots, played by the game's Deep Music Mode modifier): guitar adds vocals and the beat's melody, bass adds the drum hits.
- [x] Tempo: dotted/triplet look-alike tempos (3/4, 4/3, 2/3, 3/2) checked by how the song repeats measure by measure.
- [x] Bass: song tuning, hits on a still-ringing 808 kept (1.5 dB rule) and held on their fret, settled pitch after a click, held notes by loudness, leakage left out. (An earlier "no phantom notes inside long 808s" rule with a 4 dB cut-off was wrong: it removed real hits.)
- [x] Tempo from the song's loop length; one steady tempo for music made on a grid.
- [x] Real-song check (real-songs/): nine rap and R&B recordings with expected tempo, coverage and timing.
- [x] Tempo counted by the backbeat: four songs that read in double time are charted at 95, 79, 69 and 62 BPM (were 190, 158, 138 and 124), with the same notes.
- [x] Bass pitch from the waveform: frets follow an 808's notes.
- [x] Bass notes on 32nd positions on a steady grid: both hits of an 808 double hit.
- [x] Bass notes strong enough on their own, and confirmed re-hits, kept whatever the other repeats of the measure have.
