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
2. **Choose audio file** (or use *Share* / *Open with* from a file manager). Title, artist,
   album, year and genre are filled in from the file's tags. With **Fill in missing details
   online** ticked (the default), whatever the tags leave out is looked up on
   [MusicBrainz](https://musicbrainz.org) by title and artist (or the file name) and put in the
   empty boxes only; nothing you typed or the file had is replaced. Check them before converting.
   The genre also helps the game's story mode suggest songs for each gig. The same happens for
   every song in a list (below). **Find details online** (under the boxes, and on each listed
   song's Edit page) looks the song up again from whatever is in the boxes, or the file name when
   they are empty: it fills the empty boxes and, where MusicBrainz has something different from
   what is there, asks before replacing it.

   How a song is found: a file without tags is read as "Artist - Title" (a long run of
   underscores, where a download site dropped letters it could not keep, also parts the two;
   "MV" and "Official Video" at the end are left off). A title mangled by its file name
   ("Ascension Dont Ever Wonder") is tried again by its first words, and kept only when the
   title found starts with them. The album shown is the first official studio album or EP the
   song came out on, looked for among all the artist's recordings of it, whatever name they are
   credited by: *Maxwell's Urban Hang Suite* rather than a 1996 radio sampler, and the *Gee* EP
   (credited to 소녀시대) rather than a later live album.
3. Lyrics, any of:
   - **Find online** searches [LRCLIB](https://lrclib.net) for time-synced lyrics by title and artist.
   - **Open .lrc** loads a lyrics file you already have.
   - **Paste** lyrics into the box. If they have no `[mm:ss.xx]` times, press **Tap to sync**: the song plays and you tap once as each line starts.
   - **Two artists? Choose who sings what** (optional) opens the list of lines. Tap a line to
     pass it to the other artist, tap again for both; hold a line to give it and everything
     under it to the same artist. The names come from the Artist box ("A feat. B", "A & B")
     and can be changed there. Pasted lyrics with headers such as `[Verse 2: Travis Scott]`
     are filled in for you.
4. **How to convert**: pick one.
   - **Normal**: quick, about a minute, and needs no download. The guitar chart is made from
     the whole song as it is. There is no bass chart, and with lyrics the words are spread at
     speaking pace (any sound in time counts; the notes are not read).
   - **Split into instruments**: the best charts. Press *Download the separation model* once
     (136 MB, Wi-Fi recommended; it resumes if interrupted). The app first separates the song
     into guitar, bass, drums, vocals, piano and "other", makes the guitar chart from the guitar
     alone, adds a bass chart, and listens to the voice on its own to place every word where it
     is sung and find its note. Expect a few minutes per song (not yet timed on a phone).
   - **R&B mode**: the voice comes first. Like the split, but the guitar chart follows the lead
     singer instead of the band: the notes come where the words and runs are sung, the frets
     climb and fall with the tune, a held note is a sustain for as long as it is held, and
     there are no chords. The vocals part follows runs and riffs note by note. The bass chart
     still follows the bass. Made for R&B, soul and vocal-led pop. On a song with little
     singing in it the guitar chart follows the band as usual, and the summary says so.

   With **Two artists: ... guess it from their voices** also ticked (split modes only), lyrics
   that don't say who sings a line are split between the two artists by the sound of their
   voices. It is a guess: the lyrics box shows the result afterwards, so you can correct it with
   *Choose who sings what* and convert again.

   The split uses one thread for each of the phone's fast cores and listens to the separated
   instruments side by side, so it finishes sooner than it used to with the same result.
5. **Convert and add to YARG**. The first time, Android asks for "All files access" so the app can write into the YARG folder.
   Once it has started you can leave the app, switch to something else or turn the screen off:
   the conversion keeps going, with its progress in a notification, and a second notification
   tells you when the song is ready. (Allow notifications when Android asks, to see them.)
   Open the app again at any time to see where it is.
6. Add the song to the game (next section).

## Lyrics that start too early or too late

Lyric files are timed against one recording of a song, usually the album version. If your audio
is another cut (a music video with a scene or a few seconds of nothing before the song, a longer
intro, a break in the middle) the words would come up while nobody is singing yet.

- **Split and R&B mode** hear the voice on its own, so they line the lyrics up with it: the
  pattern of sung lines and pauses is slid along the voice until the two agree. One later part of
  the song may be moved by a different amount than the start (a break the album does not have).
  Lyrics that already fit, and lyrics that fit nowhere (the wrong song), are left as they are.
- **Normal mode** cannot hear the voice. It only moves the lyrics later by silence at the very
  start of the file (two seconds or more).
- **By hand:** **Words earlier** and **Words later**, under the lyrics (and on a batch song's
  **Edit** page), move every line half a second per press. Lyrics moved this way are used exactly
  as they are; so are lyrics carrying an `[offset:]` line.

- **Line by line (Split and R&B):** lyric sites get most lines right but not all. NetEase in
  particular often stamps a line seconds late, crammed against the next, or part-way into its
  own singing. Each line is checked against where the voice comes in after a silence:
  - two lines crammed together: the wrong one moves to the nearest place the voice comes in,
    picking whichever move leaves the lines around it with the song's usual room;
  - a line stamped while its singing is already going: back to where that singing began, up to
    1 second.

  Measured on a real song with NetEase lyrics, against line starts checked by speech
  recognition: average error 1.20 s before, 0.92 s after; no line got worse, and lyrics that
  were already right came through unchanged. Lines sung straight on from the one before (no
  breath between) cannot be told apart by the voice alone and keep their times.

The summary says when lyrics were moved and by how much, and the `lyrics.lrc` saved with the song
has the times that were used.

## Lyrics in other languages

Korean, Japanese, Chinese, Russian and other lyrics work like English ones. `.lrc` files in the
older encodings (EUC-KR, Shift-JIS, GBK, Big5, Windows-1251) and in UTF-16 are recognised when
opened. Chinese and Japanese lines, written without spaces, are cut into two-character pieces so
each gets a note. The game needs build 37 or newer to draw these characters (older builds show
boxes).

## Word timing (optional download)

Under the ways of converting, once the separation model is installed, **Download word timing
(378 MB, optional)** adds a speech model (wav2vec2-base-960h by Meta, Apache 2.0, as ONNX from
Hugging Face, pinned to one revision and checked against a fixed fingerprint). With it, in Split
and R&B mode, the lyrics are matched to the separated voice letter by letter, checked against the
lyric file and the song's repeats, and lines the recording does not have are left out. English,
Korean and Japanese kana are followed; other scripts fall back to the voice-only check.

It adds about a minute per song and needs a phone with 4 GB of memory or more. If it cannot run
(not enough memory), the song is still converted with the voice-only check and the summary says
so. The same button removes it again to free the space.

## What we learned about lyric timing

From converting real songs and checking every line by ear and by speech recognition:

1. **Section labels are not lyrics.** NetEase writes `[00:12.09][Hook]`; such lines are gaps.
2. **Timed lyrics online are mostly right, never all right.** Keep the file's times and fix only
   lines the voice clearly contradicts; re-timing whole runs of lines moved good lines too.
3. **A line starts where the voice comes in after a silence,** not where a word recogniser says
   (it often counts the silence before a word as part of it).
4. **The fix must never make right lyrics worse.** Every rule is measured on lyrics already
   checked to be right as well as on wrong ones (`core-test/LineEval`, `AlignTest`). A longer
   reach-back (1.6 s) fixed more NetEase lines but pulled correct hook lines into the line before.
5. **A speech recogniser's word times are not a yardstick.** Its word starts stretch back over
   the held note before them; lines it "confirmed" were up to 1.5 s early. The song itself is the
   yardstick: a hook's lines must come back a whole number of bars later, every time.
6. **Forced alignment is the accurate method.** With the words known, a speech model
   (wav2vec2) only has to say where each letter is (`core/WordAligner`, `stems/OrtWordModel`).
   On the test song its hook lines repeat within 0.05 s in 22 of 24 cases; lyric sites' times and
   the recogniser's were off by 0.5 to 3 s on a third of them.
7. **Repeats catch what was misheard.** Over ad-libs and a fading outro the letters can be laid
   many ways; lines that break the pattern most passes keep are put where the pattern says, and
   an outro going round follows on at the pace of its first passes.
8. **The small (quantized, 95 MB) speech model is not good enough:** it agreed with the full
   one (378 MB) on only 20 of 36 lines. The app offers the full one as an optional download
   (below); without it, the voice-only check above is used. The desktop runner uses the same
   file (`--words-model`).
9. **Checked on nine real songs, not one.** Besides the first song (R&B, NetEase lyrics), eight
   Creative Commons songs by Josh Woodward with independently timed lyrics from LRCLIB, three
   of them remixed versions. Before any of the lyric file's times were used, the aligner put 86%
   of 257 lines within 0.6 s of the file and 95% within 1 s. Of the 14 lines further off, the
   letters showed the file wrong on 2, a speech recogniser could judge 2 more and sided with
   the aligner on both, and on none was the aligner shown wrong; the rest could not be judged.
   Lyrics given with no times at all came out the same as with times on those songs.
10. **What the other songs broke, and the guards they led to:**
    - a hook missing from the lyric sheet pulled lines 15 to 30 s on to the same words: each
      line is now looked for within 10 s of its time in the lyric file;
    - a verse in the lyric sheet that the recording lacks pushed real lines out of place: runs
      of two or more lines whose letters fit nowhere are left out and the rest laid again (a
      single badly heard line is never left out: two songs showed that would drop real lines);
    - the wrong song's lyrics: rejected when the letters fit badly overall (right lyrics average
      -0.3 to -1.7, wrong ones -2.7 and below; the line is drawn at -2.3);
    - the repeat rule moved a last line that was held back on purpose (heard clearly, and the
      lyric file agreed): lines the file confirms never move, and clearly heard lines move only
      by whole repeats;
    - the repeat rule preferred the longest period, then the one most passes kept, and each was
      wrong somewhere: now the period most passes keep, among periods long enough to sing the
      block in;
    - a line's first letter latched on to a matching letter seconds early when unlisted singing
      came before it: a stray letter or two is skipped.
11. **A good lyric file is believed where the voice is unclear.** When the file agrees with the
    voice on 70% of the lines or more, the few lines that differ keep the file's time unless the
    voice was heard clearly there. A file that agrees on fewer lines (NetEase on the first
    song: 37%) is not trusted, and the voice and the song's repeats decide.
12. **Korean and Japanese are read by their sound.** Hangul is written out in Latin letters as
    it is pronounced (Revised Romanization) and kana in Hepburn, so the English speech model can
    follow them; accents are dropped. Chinese characters are still left to the voice-only check.
13. **Right lyrics are told from wrong ones by order, not by a fixed fit.** Korean read by sound,
    or a group singing in unison, fits worse even when right (Gee: -2.79) than the wrong song's
    lyrics on another recording (-2.25). What does not change: right lyrics fit clearly better in
    their own order than shuffled (+0.49 to +3.75 on 11 songs), wrong ones never do (-0.12 to
    -0.01).
14. **A lyric file's crammed lines are its own mistake.** Even a lyric file believed on unclear
    lines does not get a line back where it would leave less than half the time the line takes
    to sing; echoes in brackets do not count towards that time.
15. **Still not covered:** one singer-songwriter and one R&B group is a
    narrow sample (no rap, no heavy backing vocals, no live recordings); nothing here was
    checked by ear.
16. **Words decide the rest.** On a computer, `desktop/time_lyrics.py` hears the words and gets
   much closer (average 0.23 s on the same song); the phone has no speech recogniser for files.

## Finding the lyrics

**Find online** (and the lookup for every song in a list) asks LRCLIB for the song several ways
until its timed lyrics turn up: as tagged; with the title tidied (no "(Official Video)",
"[Lyrics]", "(feat. X)", "- Remastered 2011", track number or file ending); with only the main
artist; as free text; by title alone; and from the file name ("Artist - Title.mp3", either way
round). Each result has to have the same title, and the same artist or the same length, so a
different song that shares the name is not taken. When no timed lyrics exist, the words alone
are looked for (in LRCLIB, then in Lyrics.ovh) so they can be timed with **Tap to sync**; the
status line says where they came from.

**Also search NetEase Cloud Music** (a box under the lyrics, off by default) adds a second
catalogue of *timed* lyrics, asked only when LRCLIB has none. It is unofficial: the address its
own web player uses, not a service offered to other apps, so it may stop working at any time.
Only the title and artist are sent. If nothing is found, check the Title and
Artist boxes: they are what is searched for.

## Two artists, worked out for you

The converter looks for a second singer by itself, in this order:

1. **Who the singers are.** From how the artist is written in the file's tags ("A feat. B",
   "A & B"), a "(feat. B)" in the title, how the lyric service and MusicBrainz credit the
   song, and the names in the lyrics' own section headers.
2. **Who sings which line, from the lyrics.** When the lyrics found online carry headers such
   as `[Verse 2: Travis Scott]`, those are carried over to the timed lines: each line goes to
   the singer its header names, "both" where it names both.
3. **Fallback: their voices.** When there are two singers but the lyrics don't say who sings
   what, and the song is converted with **Split** or **R&B mode**, the lines are split by the
   sound of the two voices. This now happens by itself; the "guess it from their voices" box
   is only needed to try it on a song where no second singer is named.

When two artists are named, the voice guess knows there certainly are two singers, so it now
splits the song even when their voices are fairly alike (it used to give up and leave one part).
The voice guess happens while the song is converted, so the "who sings what" screen shows every
line for the first artist until then. After a list is converted, its summary says how each
song's lines came out ("Coi Leray 40 lines, H.E.R. 38"), or that the voices could not be told
apart.

**More than two artists.** The game has two parts to hand out, so a song with three or more
names becomes the main artist against the guests together ("B & C"): a line under any guest's
header goes to the guests' part, and a line shared with the main artist to both.

The app says which of these happened, and the result is always shown as marks in the lyrics, so
it can be checked and corrected with **Choose who sings what** (or the **Singers** button in a
list). Normal mode cannot listen to voices: there, only the lyrics' own headers can split a song.

## Several songs at once

Under **Several songs at once**, press **Choose files (up to 33)** and pick as many songs as you
like in the file picker (long-press the first, then tap the others). They are listed with the
title and artist from each file's tags, or from a file name like `Artist - Title.mp3`; take any
out with **Remove**. Each song also has a button with its way of converting: it starts as the
one chosen in step 3, and tapping it goes **Normal > Split > R&B** and round again, so ballads
can go through R&B mode and the rest Normal or Split in the same list (Split and R&B need the
separation model). With **Find lyrics online for each song** ticked (the default), each song
is looked up on LRCLIB by its title, artist and length, and gets a vocals part when timed lyrics
are found there.

**Convert all** converts them one after another, each its own way, with the number of notes
chosen in step 3, in the background with a notification ("Song 7 of 33: ..."). A song that fails does not
stop the rest. At the end a summary lists which songs were converted (and whether lyrics were
found) and which were not, and why; those stay in the list to try again. Then scan songs in YARG
once for all of them.

**Two artists in the list.** Each song's **Singers** button finds its lyrics online (once) and
opens the same "who sings what" screen as for a single song, with the two names from its artist
("A feat. B", "A & B"). Mark the lines and go back: the button shows **Singers ✓**, and that song
is converted with those lyrics and those marks. A song you don't mark is still split between two
artists by the sound of their voices when it is set to Split or R&B and "Two artists: ... guess
it from their voices" is ticked.

**Everything the single-song steps offer, per song.** Each song's **Edit** button opens its own
page: title, artist, album, year and genre; how many notes (or "as in step 3"); and its lyrics,
which can be found online there and then, opened from a `.lrc` file, pasted, timed with **Tap to
sync** (it plays that song), and marked with **Who sings what**. A song's row shows when its
lyrics are ready or still need times. Lyrics without times give that song no vocals, and the
summary says so.

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
   Most songs today are recorded to a click and never change speed. When the beats found lie on
   one straight line (or, where the tracker's beats are rough, the rhythm repeats at one length
   in every part of the song), the whole chart gets that one tempo instead of the tracker's
   small wobbles, so every measure is the same length and a riff lines up with itself.
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

   A riff that loops (the same one, two or four measures played over and over, under a whole
   verse or the whole song) is charted the same every time: once it has come round at least
   four times in a row it is locked, and the other hearings follow the pattern the most hearings
   agree on. A bass line is locked only when it keeps coming back unchanged. A measure whose
   motion and harmony match the one two or four measures before is charted as that one.

The **How many notes** choice (Fewer / Standard / More) moves the bar for how strong a sound
has to be to become a note.

These are machine-made charts. Made from the full mix, expect them to follow the song's rhythm
and harmony rather than exactly what the guitarist plays; made from the separated guitar they
follow the guitar itself, as far as the separation got it right.

### What we learned about the charts

Measured on real songs from their separated instruments ("units like the one before": the
share of 1-, 2- or 4-measure units charted 75%+ the same as the unit before, on the bar lines):

1. **A loop needs the same tempo all the way.** Usher's *Seduction* (128 BPM, one guitar loop
   from the first second to the last): 2 of 142 measures charted like the one before at first;
   pooling measures by their motion as well as their sound, then locking the loop, gave 102;
   the steady tempo, 110.
2. **The beat tracker's wobble was the problem in *Gee*.** Girls' Generation's *Gee* is exactly
   100 BPM; the tracker's beats wandered between 94 and 107, so measures drifted off the bar
   lines and only 17 were seen to repeat. With one steady tempo (found from the rhythm's repeat
   length, 99.99 BPM), 45 repeat.
3. **Bass loops are locked more carefully than guitar ones.** Maxwell's *Ascension*: a 2-measure
   bass line charted like the one before 19 times in 50 at first, 28 with the loop lock. A
   stricter rule for bass kept the test songs' bass charts right.
4. **Still not covered:** *Gee*'s four-measure synth cycles change a little each time round
   (the same chords match only about half the time), so they are charted as heard rather than
   as one repeated part. Forcing them together would chart changes that are there as if they
   were not.

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
