# YARG Resurrected (Android)

An Android (arm64) build of [YARG – Yet Another Rhythm Game](https://github.com/YARC-Official/YARG),
aimed at phones like the Galaxy Z Fold 7. It replaces the archived `YARG-Android` fork.

This repo doesn't copy YARG's code. It builds on the `android-touch` branch of
**[putudarma/YARG-DROID](https://github.com/putudarma/YARG-DROID)** (YARG v0.15.0 stable + Android fixes: IL2CPP-safe
BASS callbacks, `/storage/emulated/0/YARG/songs`, touch controls, the all-files-access prompt). YARG-DROID only ships
32-bit (ARMv7) builds, which can't run on 64-bit-only chips like the Snapdragon 8 Elite. This repo adds:

- a **64-bit (arm64) IL2CPP** build for modern phones,
- arm64 native libraries (BASS, STB2CSharp, sqlite3) built with the NDK,
- **working menu text**: YARG-DROID's extractor read the APK through JNI into a C# array that Java never filled,
  so language files never unpacked and menus showed raw keys like `MENU.MAIN.OPTIONS.QUICKPLAY`,
- the **phone's own mic** for vocals, a **USB mic and the built-in mic at the same time**, plus **other phones as
  controllers and mics** over Wi-Fi (see below),
- **vocals as a game of their own**: pick an artist's lines on two-singer songs, and a **sing-off** mode,
- **Vocal Challenge**: sing the whole song, one artist's part on any song with two or three artists, or trade lines with
  another singer; lyrics that are readable while singing; tidy song names in the menus (see below),
- **fairer singing scores** and a **Challenge Mode** (now shown as **Strict Judging**) for vocals that makes you really sing the song (see below),
- a controller-type prompt on Android (gamepad / CRKD guitar / other guitar),
- a **Deep Music Mode** modifier: guitar and bass cover the whole song (see below),
- CI that publishes the APK as a GitHub Release.

GitHub Actions downloads YARG-DROID at a pinned commit (`UPSTREAM_REF`), applies `patches/*.patch`,
copies `overlay/` on top, builds the native libraries (`android/native/`), then builds the APK with Unity (game-ci).

**Windows, Mac, Linux and Chromebook:** the same game also builds for Windows x64 (**Actions → Build Windows**, see [windows/README.md](windows/README.md)), macOS (**Actions → Build Mac**, see [mac/README.md](mac/README.md)) and Linux x86_64, which Intel/AMD Chromebooks run too (**Actions → Build Linux (and Chromebook)**, see [linux/README.md](linux/README.md) for step-by-step Chromebook instructions).
They are built from the same `patches/`, `patches-core/` and `overlay/` as the phone version, so to keep them level with it,
run those three workflows on the same branch whenever the Android one is run. Each is play-tested on a machine of its own
system before it is published: the game starts, a phone joins over the network, Song Setup opens, and a bot plays a test
song with the guitar sound generated and the microphone listening (`tests/desktop-play-test.sh`).

`patches-yarg-dev/` keeps an earlier port of YARG's newer `dev` branch, for later.
`tests/RemoteServerTests` checks the phone remote server on every build (runs before the Unity build).
`tests/RemotePageTests` runs the phone page in a real browser (touch, a fake microphone) against that server.
`tests/GameReaderCheck` runs a song written by the converter through YARG's own song scanner and chart loader.
`tests/VocalsTests` checks singing: pitch detection, microphone timing and scoring, with the game's own engine.

> Status: **experimental.** Expect the first CI runs to need fixes from the logs.

**Website:** `site/` is a download page and player guide, published to GitHub Pages by
`.github/workflows/pages.yml` (one time: **Settings → Pages → Source: GitHub Actions**). Its download
buttons always point at the newest game and Converter builds in Releases.

## Gamepad (PS5 controller layout, others supported)

The game is set up for a **PS5 DualSense** (PS4 works the same) and uses the usual PlayStation
buttons. Xbox, Switch and generic Android pads work too: pick yours in
**Settings > General > Controller Layout**.

### Menus

Any connected gamepad moves around the menus straight away, before it's in a profile.

| Menu action | PS5 / PS4 (default) | Xbox / generic | Nintendo Switch | PlayStation, Japan style |
|---|---|---|---|---|
| Move | D-pad or left stick (hold to scroll) | same | same | same |
| Confirm / select (green) | Cross | A | A (right) | Circle |
| Back (red) | Circle | B | B (bottom) | Cross |
| Yellow action (Start Setlist; Play A Show if enabled) | Triangle | Y | Y | Triangle |
| Blue action (Filters) | Square | X | X | Square |
| More Options (orange) | L1 or R1 | LB or RB | L or R | L1 or R1 |
| Start / pause | Options | Menu | + | Options |
| Select ("go to the song that's playing" on the main menu) | Create | View | − | Create |

While a gamepad is connected, every button prompt shows the controller's own button in the
action's color: a green disc with a cross for Confirm, a red one with a circle for Back, then
triangle, square and L1 (A / B / X / Y / LB with the Xbox or Nintendo layout). The same icons are
used in the bar at the bottom, on the music library's Play button, on the "Choose Your Fate"
rows and in the binding menu. Turn this off with **Settings > General > Show Controller Buttons**.

Lists in pop-ups (Add Device, "Which kind of controller is this?") work with the controller too:
D-pad or stick moves the highlight and scrolls the list, Cross picks, Circle closes. On Android
the Add Device list leaves out the phone's own touch screens, touchpads and sensors.

**Play A Show** (build a show and vote for the next song on a "Choose Your Fate" screen) is off
by default. Turn it on with **Settings > Songs > Enable Play A Show**.

A gamepad in a profile uses that profile's menu buttons. They are set from the Controller Layout
when you add the gamepad to the profile, so after changing the layout, remove the gamepad from
its profile and add it again (or change the buttons in the profile's binding menu).

### Playing a song

Add the gamepad to a profile in **Profiles > Add Device**. PlayStation and Xbox controllers are
set up straight away; anything else is asked about once (pick **Gamepad** for a pad, or the guitar
it is). The controller keeps working in the menus the whole time, including while that question
is on screen.

Guitar and bass on a gamepad have two layouts, chosen in **Settings > General > Gamepad Guitar
Layout**. Changing it re-does the guitar buttons of PlayStation and Xbox controllers already in
a profile.

**Guitar Hero 2 (default).** The same as playing Guitar Hero 2 on a PlayStation controller:

| Guitar | PS5 / PS4 | Xbox |
|---|---|---|
| Green / Red / Yellow / Blue | L2 / L1 / R1 / R2 | LT / LB / RB / RT |
| Orange | Cross | A |
| Strum | D-pad Up / Down | D-pad Up / Down |
| Star Power | Create | View |
| Whammy | Left stick left/right | Left stick left/right |
| Pause | Options | Menu |

Easy uses green, red and yellow only: L2, L1, R1.

**Face buttons.** The frets are the same buttons as the colors in the menus:

| Guitar | PS5 / PS4 | Xbox |
|---|---|---|
| Green | Cross | A |
| Red | Circle | B |
| Yellow | Triangle | Y |
| Blue | Square | X |
| Orange | L1 | LB |
| Strum | D-pad Up / Down | D-pad Up / Down |
| Star Power | R1 (or Create) | RB (or View) |
| Whammy | R2 | RT |
| Pause | Options | Menu |

With this layout Easy is Cross, Circle, Triangle.

**No strumming.** On a gamepad, pressing the fret button plays the note: no strum needed. Hold
the buttons of a chord together, and press again for a repeated note. This is **Settings >
General > No Strumming On Gamepads** (on by default). It gives the profile the *All Taps*
modifier and the *Casual* engine preset, both of which you can see and change on the screen
before a song. Turn the setting off to play with strums (D-pad up / down) like on a guitar.

**Which button is which fret.** During a song, each fret on the highway has the controller button
that plays it shown just under it, in the fret's color: L2, L1, R1, R2 and Cross with the Guitar
Hero 2 layout, or whatever the profile has bound. It follows **Settings > General > Show
Controller Buttons** and only appears for a player with a gamepad.

**Difficulty and what the notes look like.** New profiles start on **Easy**: colored notes on
three frets, like Guitar Hero and Rock Band. **Beginner** is different on purpose: every note is a
rainbow bar across the whole track, meaning "strum, any fret". Pick the difficulty on the screen
before a song. The note style is per profile: **Profiles > (profile) > Theme**, where
*Rectangular* is the Rock Band look and *Circular* the Guitar Hero look.

**Touch buttons.** The big colored squares and strum bar are the on-screen controls for playing
on the phone's screen. They only appear when nobody playing has a controller: with several
players, one guitar, gamepad or phone remote among them keeps them off the screen (they used to
be judged by the first guitar player alone, and could come up over a multiplayer song). When they
are shown they play for the guitar player without a controller. Change it in **Settings >
General > Touch Buttons** (Automatic / Always show / Never show).

Vocals: Cross / A hits percussion notes, Create / View is Star Power. In the pause menu the menu buttons above apply.
Any button can be remapped in the profile's binding menu. For a guitar controller, pick
**CRKD Guitar** or **Other guitar** instead of Gamepad.

### Guitar controllers (CRKD Gibson Les Paul and others)

A guitar controller is harder on the player than a gamepad: every note has to be strummed in
time, where a gamepad (with No Strumming) only needs the button. Three things make it fair:

- **Every strum reaches the game.** A guitar's strum bar is its D-pad, and Android does not
  treat a D-pad as buttons: its movements are saved up and handed over once per screen frame,
  as one event holding the newest position, with the earlier ones tucked inside. A quick strum
  (down and back to the middle within one frame) arrived as "in the middle": no strum. Fret
  buttons are never saved up, which is why a gamepad felt fine. The game now asks Android to
  deliver controller movements as they happen (Android 11 and later) and, where one still
  arrives with earlier positions inside, passes each of them on with its own time. The game's
  log says after every song how many there were ("Controller input this song: ...").
- **Forgiving timing** (**Settings > General > Forgiving Timing On Guitars**, on by default). A
  profile that plays on a guitar controller gets the **Relaxed** engine: a note counts when you
  strum up to a tenth of a second early or late (the Default engine: 70 ms), you have 80 ms
  between the strum and the fret instead of 50, and touching a fret early costs nothing. Each
  profile is switched once and shows "Relaxed" as its engine; pick another in Profiles if you
  want it stricter. Turning the setting off puts those profiles back on Default.
- **Strum bar bounce** (**Settings > General > Strum Bar Bounce Filter**: Off / Light / Strong,
  Light by default). Let go of a strum bar and it springs back; if it swings past the middle it
  strums a second time, which the game counts as a wrong strum (streak gone, guitar cut out).
  A second strum within 20 ms of the first, or within 10 ms of letting go in the other
  direction, is left out (Strong: 35 and 20 ms). Fast up-and-down strumming is slower than
  that, and is checked to pass untouched at 20 strokes a second.

Not measured on a real guitar here: these come from how Android and the game handle input, and
from tests of the code. If strums still go missing, the log line above says whether Android was
saving them up.

### Automatic timing for each controller

Every controller is late by its own amount. A Bluetooth guitar is later than a wired one (CRKD's
own guide rates Bluetooth as its slowest connection, and on Android Bluetooth is how the guitar
connects), a TV is later than the phone's screen. When the delay is more than the game allows for,
a strum made exactly on the note misses, and it feels as if you have to strum before the note
arrives.

The game now works the delay out by itself (**Settings > General > Automatic Timing For Each
Controller**, on by default):

- While you play it measures how early or late your notes land: the ones you hit, and strums that
  found no note but had exactly one note nearby that the frets you were holding fit.
- After the first 8 notes, and then every 16, it takes the middle of those measurements and, if it
  is more than 12 thousandths of a second off, moves your profile's **Input Calibration** three
  quarters of the way. A message says so when the change is 15 ms or more.
- The value is kept for each controller a profile has used, so a guitar and a gamepad each keep
  their own.

**Play to what you see and hear. Do not strum early to make up for the delay**: that hides the
delay from the measurement. The first few notes of the first song may miss; after that they
should land.

It is not used for bots, replays, practice, songs played at another speed, or phones joined over
Wi-Fi (which have their own measured delay). A profile's Input Calibration can still be set by
hand in Profiles; automatic timing carries on from whatever it is.

### Fast Bluetooth for controllers, and how often a controller really reports

A Bluetooth controller and the phone agree how often they talk. For a **Bluetooth Low Energy**
controller the phone normally settles on a relaxed pace to save power, and an app may ask for the
fast one. The game now asks (**Settings > General > Fast Bluetooth For Controllers**, on by
default, Android only). The first time, on the screen before a song, it asks for the **Nearby
devices** permission, which Android requires for looking at paired Bluetooth devices.

This cannot help a controller on **classic Bluetooth**: that kind sets its own pace and nothing on
the phone can change it. Which kind yours is, the game can only find out on the phone. Press green
on your profile in **Profiles**: the **Connection** row says "Bluetooth Low Energy, fast connection
asked for" or "classic Bluetooth; it sets its own pace", and, after you have played a song,
how often the controller really reports ("reports about every 11 ms"). That number is measured
from the controller's own reports while something on it moves (the whammy bar, the tilt sensor).

For the lowest delay, CRKD's guide ranks a USB cable first, its USB receiver second and Bluetooth
last. Whether the cable or the receiver works as a guitar on Android has not been tried here.

### Smoother play: the volume normalizer no longer runs all song long

With **Settings > Sound > Normalize Audio** on (the default; an advanced setting), the game reads through the whole
song in the background to measure how loud it is. That reader never stopped: after the end of the
song's audio it went on reading silence at full speed, using a whole processor core for as long as
the song was on screen. On a phone that means heat, a slower game and stutter. It now stops at the
end of the audio, runs at low priority, and reports to the game once for every second of song
instead of ten times.

### Finding songs, rap mode, explanations (build 42)

- **Browse by Artist / Genre / Album / Year / Charter**: in the song list open **More Options**
  (L1 / orange fret) and pick one. A list of every artist (or genre...) in your library comes up,
  with how many songs each has; pick one to see just those songs. No typing needed.
- **Rap Mode** (on the screen before a song, for a singer): **Whole song**, **<Artist>'s verses**
  (on a song with each artist marked, such as converted two-artist songs), or **Trade every 4 / 8
  / 16 bars**, which splits the song evenly between two singers, a block of bars each, scored head
  to head like a sing-off.
- **Light blue explanations**: on the screen before a song, the option under the cursor (each
  modifier such as All Taps, each level, Rap Mode, Sing-Off...) is explained in a light blue box
  on the right. The controller profile list says what the profile's engine (Casual, Precision...)
  does, and can change the engine and the instrument type.
- **Left / right** on that screen changes the song speed, 5% at a time.
- **Gibson Les Paul (CRKD) guitar in the menus**: green fret confirms, red goes back, strum moves
  up and down, yellow / blue / orange are the other menu buttons, as on the PS5 controller
  (Cross, Circle, Triangle, Square, L1). R2 / L2 song skipping only listens to gamepads, so the
  guitar's whammy bar doesn't skip songs.

### Controller extras (build 41)

- **Profiles screen on a controller**: up/down moves through the profiles, **Cross** opens a list
  for the lit-up profile (Connect, Add Device, Remove Device, note speed, lefty flip, move, more
  settings, Disconnect), **Triangle** adds a profile.
- **Everything in the profile menus with a controller or a guitar** (strum to move, green to
  pick, red to go back): **Rename** puts the name together a letter at a time from a list;
  **Buttons** shows what each button does while playing and in the menus, and changes one by
  picking it and pressing the button that should do it (do nothing for 8 seconds to leave it);
  the **Engine** row now goes through your own engine presets as well as the built-in ones;
  **Triangle / yellow** offers a new player profile or a new bot. The touch screen's own
  settings page (sliders such as press points) can be left with **red** instead of trapping a
  controller there.
- **Add a controller, a mic or another player after picking a song**: the screen where you choose
  instrument and difficulty has **Add Controller or Mic** and **Add Another Player**.
- **R2 / L2** skip to the next / previous menu song wherever the music player shows (main menu,
  settings, profiles). The menu song keeps its place when you go into the song list or a dialog
  and carries on when you come back, instead of starting a new one.
- **Square** in the song list adds the song under the cursor to **Favorites** (again to remove).
  The first time, the menu music switches to your Favorites (change it in Settings > Sound > Menu
  Music Source). **Filters** moved into **More Options**.

## Venues and crowd

Songs are played in a venue, with a crowd in front of the stage dancing in time with the music.

- **The venues** are stages drawn in the spirit of places that matter to Hip Hop and R&B history.
  They are tributes, not copies: no logos or likenesses, and each one says what it is inspired by
  when the song starts.
  - *East:* a Harlem amateur-night theater (the Apollo), a Bronx park jam, a Midtown arena
    (Madison Square Garden), a U Street theater (the Howard, Washington D.C.), a North Philly soul
    hall, an uptown concert hall (Carnegie Hall), a capital arts center (the Kennedy Center).
  - *South:* an Atlanta block party, a Crescent City blues house (House of Blues New Orleans) and
    festival night (Essence), Houston slab Sunday, a Beale Street juke (Memphis), a Miami beach stage.
  - *Midwest:* the Pontiac ballroom (the Crofoot, Michigan), a Motor City grand theater (the Fox,
    Detroit), the South Side Regal (Chicago), a Minneapolis star-wall club (First Avenue).
  - *West:* an Inglewood arena (the Forum), a Compton lot party, an Oakland deco theater, a Las
    Vegas residency room.
  - *International:* a London stadium night and a Bucharest open-air show (Michael Jackson's 1988
    Wembley and 1992 Bucharest concerts), Tokyo Dome, a lakeside jazz festival (Montreux), a
    Johannesburg festival field.
- **Choosing one:** on the instrument and difficulty screen (Quickplay and the rest), the
  **VENUE** bar above SONG SETUP shows where the next song is played and whether the crowd is on;
  tap it to pick, or to switch the crowd. The same choice is in **Settings > General > Venue**. Random by default; or random among street and
  club venues only, random among high-class rooms only, one venue in particular, or None for the
  game's own background. A song that comes with its own background still uses that.
- **Your own pictures or videos:** each venue has a folder in `Internal storage/YARG/venues/`
  (made the first time a song is played). Put a `.jpg`, `.png`, `.mp4` or `.webm` in a venue's
  folder and it is used as that venue's backdrop instead of the drawn stage, with the crowd in
  front of it. Several files in a folder are picked from at random.
- **The crowd** is seen from behind, three rows deep, with mixed skin tones, hair and clothes and
  a strong Black presence; dressed up in the high-class rooms. Groups of neighbors dance simplified
  versions of party dances from the mid-2000s on (Lean wit It, Crank That, Walk It Out, the Jerk,
  the Dougie, Cat Daddy, the Nae Nae, the Whip, the Dab, Hit the Quan, the Milly Rock, the Woah,
  Shmoney, the Griddy, Hit Dem Folks, the Wobble, the Stanky Leg, the Running Man, the Shimmy, the
  Shoot, the Tootsie Slide, the Renegade), plus nodding, fist pumps and jumping. Each group
  changes dance every sixteen beats on its own bar, blending into the new move, and now and then
  the whole room swag surfs, jumps or puts its hands up. In slow songs people sway and hold their
  phones up as lights. Every part of the body moves on a spring, so the head and forearms follow
  the body a moment later and swing through, instead of snapping from pose to pose.
  Turn it off with **Settings > General > Dancing Crowd**.

Everything here is drawn by code from simple shapes, so it is cartoon-like rather than realistic.

Up to build 36 none of this could be seen on a phone: the note highways were drawn on a layer that
is see-through on a computer but solid black on phones, and it covered the venue, the crowd and
any background picture or video. Build 37 fixes that layer.

## Menu music: pick a startup song and a playlist

The menus play songs from your library in the background. You choose what plays:

- **Startup Songs.** Each time the game starts, the songs in the **Startup Songs** playlist play
  first, in order, then the menu music carries on as below. It starts with **"No Pole" by Don
  Toliver** as soon as that song is in your library (convert it or add its folder to
  `YARG/songs`, then scan).
  - **Add a song:** in the music library, highlight it, open **More Options** (L1) and choose
    **Add To Startup Songs**. It goes to the end of the list.
  - **Change the order:** music library > **Playlists** > **Startup Songs**, highlight a song and
    press **Left / Right** on the D-pad to move it up or down.
  - **Remove a song:** highlight it and choose **Remove From Startup Songs** in More Options.
    Removing No Pole sticks: it isn't added back.
  - **Turn them off:** **Settings > Sound > Play Startup Songs**.
- **Which songs play after it.** **Settings > Sound > Menu Music**: *Whole library*, *Favorites*,
  or any of your playlists. Or in the music library's **Playlists** view, highlight a playlist
  (or Favorites), open **More Options** and choose **Use As Menu Music** (and **Stop Using As
  Menu Music** to go back to the whole library). The music switches right away.
- **Order.** **Settings > Sound > Shuffle Menu Music**: on plays them in random order, off plays
  the playlist in its own order (and loops).
- **Volume** stays **Settings > Sound > Music Player Volume** (0 turns menu music off).

If the playlist is empty, deleted, or none of its songs are in the library, the whole library
plays instead.

## Lyrics fill as they are sung

The lyric bar at the top no longer turns a whole word blue the moment it starts. The blue fills
through the letters as the word is sung: over the length of its note in the vocals part, so a
held note fills slowly, a quick word quickly, and the letter the fill is passing through is part
blue, part white. Charts without a vocals part fill each word until the next one starts.

## Lyrics and names in any language

The game's own fonts cover Latin letters and Japanese. For everything else (Korean, Chinese, Thai,
Arabic, Hebrew, Hindi and more) the fonts already on the phone are used, so lyrics, song titles and
artist names in those languages are drawn instead of empty boxes. Arabic and Indian scripts are
shown letter by letter, not joined up as they should be.

## Vocal Challenge, clearer lyrics, tidy names

### Lyrics you can read while singing

The words above the notes used to fly past in small grey italics. Now:

- Every word is **bold and upright**.
- **Settings > Graphics > Lyrics When Singing** (under the Gameplay heading) has three choices. **Automatic** (the default)
  scrolls the words as slowly as the song allows (down to 55% of the old speed) and, on a song
  whose words come too fast to read while moving (most rap), shows **still lines** instead: the
  whole line stands still and each word lights up as it is sung, like karaoke. **Scrolling** and
  **Still lines** force one or the other.

### Vocal Challenge (was Rap Mode, Sing-Off and Whose Lines)

A singer's screen before a song has one row, **Vocal Challenge**, with everything in one list:

- **Whole song**: every verse, hook and ad-lib.
- **<Artist>'s part**, one row for each artist of the song. On a song that has a part for each
  artist (a Rock Band harmony chart, or a converted song with `vocal_parts.txt`) these are the
  artist's real lines. On **any other song credited to two or three artists** ("A & B", "A ft. B",
  "Title (feat. B)") the row says **(verses dealt out)**: the song doesn't say who has which lines,
  so its verses are cut at pauses in the words (or every 16 bars) and dealt out between the artists
  in turn. That gives each artist a fair share in sensible pieces; it is **not** the exact lines
  they have on the record, and such a run leaves no high score. A song by one artist has no artist
  rows: the whole song is theirs.
- **Artist against artist** (On/Off): when two singers are different artists, every line is marked
  out of 100 and the winner is called at the end.
- **Trade every line / 2 lines / 4 lines / 4 bars / 8 bars / 16 bars**: two singers share the song
  in turns, each in their own color, every exchange a round with a winner.
- **Strict judging** (On/Off): the modifier that used to be called Challenge Mode (you have to
  really sing it). It is still under Modifiers too, as **Strict Judging**.

The settings **Sing-Off** and **Sing-Off Turn Length** are now called **Vocal Challenge: Head To
Head** and **Vocal Challenge: Turn Length**; the list above sets them for you.

### Tidy song and artist names

Songs made from a downloaded video often carry the video's name. The menus now show, sort and
search by a tidied name (the song's own files are not changed):

| The song says | The menus show |
| --- | --- |
| `Rio_Da_Yung_Og__Veeze_-_Superstar_Official_Visualizer` by Unknown Artist | **Superstar** by **Rio Da Yung Og & Veeze** |
| `Gee (Official Video) [HD]` | **Gee** |
| `Veeze - guillermoandrade [Official Music Video]` by Veeze | **guillermoandrade** |
| `Get Throwed (Lyrics) Ft. Pimp C` | **Get Throwed feat. Pimp C** |

Only what a video site adds is taken off: "(Official Video)", "[Lyrics]", "(Audio)", "(Prod. by
...)", underscores, "Artist - " in front of the title. "(Live)", "(Remastered)", "(Radio Edit)" and
the like stay, since they tell two versions apart. This tidies what the song already says; it does
not look names up online, so a song whose files name the wrong artist still shows the wrong artist
(fix its `song.ini`, or convert it again with YARG Converter, which does look names up).

### Renaming a profile and changing its settings with a controller or guitar

On **Profiles**, move to a profile and press **green** (Cross on a gamepad). The button bar now
calls this **Rename / Settings**, and the list that opens starts with **Rename** (pick letters
with up/down and green, then Save) and **Profile Settings** (highway length, calibration, theme,
colors, camera...), followed by instrument type, engine, note speed, lefty flip, buttons and
devices.

## Vocals: sing or rap, pick an artist, sing-off

> The rows described below (Whose Lines, Rap Mode, Sing-Off) are now one row, **Vocal Challenge**:
> see the section above. What they do is unchanged.

Any song with a vocals part can be sung: Rock Band style charts, and every song made with
[YARG Converter](converter/README.md) from lyrics (convert it again with the newest converter to
get one; songs converted before only have a lyric bar).

**Getting set up**

1. **Profiles** > pick or make a profile > set its **Game Mode** to **Vocals** (new profiles
   start as Five Fret Guitar, and those are never offered Vocals) > **Add Device** > choose a
   microphone (below).
2. Pick a song, then on the screen before it choose **Vocals** as the instrument and a level.
3. Sing. Notes scroll along the top with the words under them; your needle shows where your
   voice is. Words in italics over a shaded block, with no note line to follow, are **rapped or
   spoken**: there, any sound in time with the words counts and the note does not matter.

**Levels: how exact you have to be**

The level sets two things: how close to the note counts, and how much of each line you have to
sing right for the line to count. The screen where you pick the level says both for each one.

| Level | The note | Of each line |
|---|---|---|
| Beginner | any note counts | 23% |
| Easy | within 1.7 semitones | 33% |
| Medium | within 1.4 semitones | 40% |
| Hard | within 1.1 semitones | 45% |
| Expert | within 0.8 semitones | 58% |

(A semitone is one key on a piano. Octaves never matter: sing it high or low.) Being closer than
the limit earns more of the line: full marks inside 60% of it, less towards the edge. Harder
levels are also worth more points per line. These numbers are YARG's own and can be changed
under **Settings > Presets**.

**Two artists on one song**

Songs with a part for each artist (two-singer songs from the converter, Rock Band harmonies)
offer a second instrument, **Harmonies**. Choose it and a **Whose Lines** row appears: pick the
artist. Each player then gets only that artist's lines to sing, in their own color, with the
other artist's words on the second line. Lines marked for both go to both. All singers have to
choose the same instrument, Vocals or Harmonies. **Vocals** is always the whole song as one part.

**Spoken and rapped lines have to be spoken.** Words with no note (rap, speech, and every word
of a song converted in the converter's Normal mode) used to count for any sound at all, so one
long "aaah" cleared a song. From Medium up, such a line now also counts syllables: every fresh
start of your voice (after a tiny silence, a jump in loudness, or a clear move in pitch). Roughly
half as many starts as the line has words, up to three a word, is full credit; a drone or a hum
gets a fraction, and so does babbling as fast as you can. The game cannot check the words
themselves, only the rhythm of saying them. Sung lines are judged on the note, as before, and
Beginner and Easy are unchanged. For notes to sing, convert songs with **Split** or **R&B mode**.

**Mic test.** On the Profiles screen, **MIC TEST** shows what the game hears from each active
profile's microphone: how loud (it has to pass the white line, which is **Settings > Sound >
Microphone Sensitivity**), the note you are on, a dot that follows your voice up and down, and
the syllables it would count. **Hear myself** plays the microphone back; use headphones.

**No vocals to pick?** Then the song has no vocals part: a message says so when the instrument
screen opens. Songs from the converter only get one when they are converted with lyrics.

**Hearing yourself.** Every microphone is played back on the output the game's sound is on:
the phone's own mic, a USB mic, a headset mic, and phones singing over Wi-Fi. **Settings > Sound >
Vocal Monitoring Volume** (also in the pause menu's quick settings) sets how loud: 0 is off, 70%
(the default) about one and a half times as recorded, 100% three times, with loud notes rounded
off instead of crackling. It plays only while headphones or another output are connected (wired,
USB or Bluetooth): through the phone's own speaker the microphone would hear it again and
squeal, so without them it is silent.

**Karaoke** (Settings > Sound, under Vocal Monitoring Volume; off by default) plays the singers
through the phone speaker or the TV as well, like a karaoke machine. Keep the microphones away
from the speaker and pointed away from it, and start with Vocal Monitoring low. A squeal guard
listens to what is played back: a howl (a pure tone held at full loudness, which singing never
is) is halved within a second, Vocal Monitoring is turned down, and a message says so. It was
checked on real vocals and full mixes at 70% and 100% (no false alarms) and on howls from 300 Hz
to 4.5 kHz (all caught in under a second). It works with headphones too.

The phone's own mics are played back straight from the recorder, with the shortest delay Android
offers. Phones over Wi-Fi arrive about a tenth of a second later (the network, and a small
cushion against its uneven bursts): fine for hearing the singers in the room, a little late in a
singer's own ear. Bluetooth headphones add a delay of their own, and the game says so once;
wired or USB headphones keep the voice in time. With a Bluetooth speaker in the room, keep the
phone mics away from it, or turn the level down, so they do not pick it up again.

**Sing-off**

With two or more singers, the screen before a song has a **Sing-Off** row (also **Settings >
General > Vocals**). Switch it on and the vocals become a battle:

- On **Vocals**, the lines are dealt out in turns, two each by default (**Sing-Off Turn Length**:
  one, two or four). Your lines come in your color; the board under the lyrics says whose turn
  it is and counts down to the next singer. Each exchange is a round with a winner.
- On **Harmonies**, nobody is dealt anything: each player sings the artist they picked, and the
  two artists' singers face off.
- Every line is marked out of 100 for how much of it was sung right (a harder level earns up to
  a tenth more, an easier one less). The board shows each side's average and a bar that leans
  towards whoever is ahead, calls each round, and names the winner at the end.
- More than two singers play as two teams: players alternate between the sides in the order they
  joined.
- A sing-off with turns leaves no high score (each singer has half the song). It is off in
  practice and replays.

**Two microphones on one phone**

"This phone's mic" is whichever microphone Android prefers at the moment, which is a USB
microphone as soon as one is plugged in. So for two singers, **Add Device** also lists each
microphone on its own:

- **Built-in mic**: the phone's own microphone, even while a USB mic is plugged in
- **USB mic: (name)**: a microphone on USB or a USB-C adapter (a controller with a mic on a USB
  cable shows up here too)
- **Headset mic (plug)**: a wired headset's microphone

Give one profile **USB mic** and the other **Built-in mic**, and both are recorded at once. If
this phone can only listen to one microphone at a time, a message says so when the song starts;
then a second phone as a mic (next section) is the way. Hearing yourself through the speakers is
only offered for "This phone's mic".

**Sound and USB mics.** A USB microphone with a headphone jack (a Fifine AM8, for one) is also a
sound card, and Android sends all sound to it the moment it is plugged in. The game doesn't let
it: on **Automatic** the sound goes to Bluetooth, wired headphones, the TV or the phone speaker
instead (whichever is connected, in that order). To hear the game through headphones plugged
into the microphone, pick **USB Device** in **Settings > Sound > Play Sound Through**; the other
choices there are **Phone Speaker**, **TV (HDMI)** (through a USB-C hub's HDMI port),
**Bluetooth** and **Wired Headphones**. The microphone keeps working. Your own voice is never
played back while the game plays out loud on the phone speaker or a TV (the mic would hear it
again). A message says where the sound went, or that what
you picked isn't connected (the sound then stays where Android puts it until it is plugged in).
Playing somewhere other than Android's choice adds a few hundredths of a second of delay, so run
**Settings > Calibration** once more for that setup.

Not tried on a phone yet: all of this builds and the song files are checked against the game's
own reader, but the sing-off board, the artist names, the level hints and the two-microphone
recording have not been seen running.

## Singing: fairer scores, and Challenge Mode

### What changed for every singer

- **Your pitch is read better.** The game now finds your pitch 50 times a second (it was 25) with a
  newer method. In the automated tests it is within half a cent of the true note on average, where
  the original was 4 cents flat. Breath, "shhh" and room noise are never mistaken for a note, and
  they can no longer hold a note you have stopped singing. It is used by "This phone's mic", the
  Built-in / USB / Headset mics and phone mics over Wi-Fi alike, and by the Mic Test.
  (**Settings > Sound > Improved Singing Detection**; turn it off to get the original back.)
- **You are judged by when you sang, not by when the sound reached the game.** A microphone is
  always a little late, and the original scoring held every sound against the notes playing when
  the sound *arrived*. A trained singer could only be credited about three quarters of a phrase
  that way; now it is 98%. This also covers phone mics over Wi-Fi: each packet of sound is
  counted, so jitter and lost packets no longer shift your timing.
  If you only score when you sing ahead of the notes (a Bluetooth microphone will do this), raise
  **Settings > Sound > Microphone Delay**.
- **Each phrase tells you how much of it you sang**: "AWESOME! 84%". 95% or more is "PERFECT!",
  and three in a row are counted ("3x PERFECT!").
- **Changing the key on the Song Setup screen now moves the notes you have to sing too.** Before,
  the song moved and the scoring did not, so nothing you sang could match.
- **Faster song speeds are scored properly** (at 150% speed a perfect singer used to be credited
  two thirds of a phrase at most).
- **Spoken lines count syllables from your voice alone.** With breath and consonants no longer
  taken for the voice, the gaps between syllables are heard as they are (see "Spoken and rapped
  lines" above).
- Small fixes: no "miss" sound for every tambourine note you let go by; restarting a section in
  practice no longer counts a held note twice.

One thing to know: because the game no longer loses a quarter of what you sing, **normal vocals are
easier to pass than before**. That is what Challenge Mode is for.

### Challenge Mode

Turn it on where you pick your level: **Modifiers > Challenge Mode**. It stays on for that profile
until you turn it off, and the level list then shows what each level asks for ("Challenge: within
40 cents, 80% of the line, every note"). With several singers, the first singer's modifiers apply
to all of them, as with the other vocal modifiers. A phone that joins as a singer has a
**Challenge mode** checkbox on its join screen. It works with Harmonies and in a sing-off.

Normal vocals only ask you to hit part of each line: on Expert, 58% of it, and you can leave out
any notes you like. Challenge Mode asks for the song:

| | In tune within (full credit / some credit) | Of each line | Notes you may leave out |
|---|---|---|---|
| Easy | 90 / 150 cents | 60% | 2 per line |
| Medium | 72 / 120 cents | 70% | 1 per line |
| Hard | 54 / 90 cents | 75% | 1 per line |
| Expert | 40 / 70 cents | 80% | none |
| *Expert without it* | *48 / 80 cents* | *58%* | *any* |

(100 cents is one semitone, the step from one piano key to the next. For scale: studies of
singing count a note as accurate within 50 cents, and trained listeners start to notice a note
held about 25 cents off. [1](https://livelab.mcmaster.ca/app/uploads/2021/07/DemorestPfordresher_2015_MPSpecial.pdf)
[2](https://www.voicescience.org/articles/how-to-sing-in-tune/))

- **Every note counts.** A note is "left out" if less than 30% of it was sung in tune. One note
  too many and the line is lost, however well the rest went. (Notes shorter than about a tenth
  of a second are too quick to judge alone; they still count towards the line.)
- **The start and end of a note are forgiven** (a tenth of a second each): the time a consonant
  takes, finding the pitch, leaving a moment early to breathe. Each edge is scored by the best you
  reach inside it, so sliding up into a note, or letting it sag as you let go, costs nothing. What
  is forgiven is limited by what you sang, though: one stray moment on pitch is not a whole note.
- **Only a voice counts.** Breath and noise never hold a note, and spoken parts need you to speak
  every word. (The syllable check for spoken lines, which normally starts at Medium, is also on
  for Easy in Challenge Mode.)
- **Any octave is fine**, as always: low voices can sing high songs.
- **The original singer steps back.** On songs with a separate vocal track, the recorded singer
  drops to 35% volume (**Settings > Sound > Lead Singer Volume In Challenge Mode**), so you carry
  the tune, and the mic cannot simply hear the right notes from the speaker.
- **The screen is honest.** The line meter fills with what you really sang: it turns green when
  that is enough and red when the line is lost. The needle sits where your voice is instead of
  snapping to the note. The percentage on the results screen is what you sang, not 100% for every
  line you passed.
- **Same points per line** as without it, so scores compare, and stars are harder to earn.

What it cannot do is check the words. Humming the tune note for note, in time and in tune, passes.

For the real thing, **wear headphones, or sing into a second phone** (see the phone remote below).
A phone's loudspeaker is a finger's width from its mic, so with the song playing out loud the mic
hears the band as well as you. Hold the mic close either way.

### How the numbers were chosen

By having made-up singers of known skill sing made-up songs (pop, fast and slow) through the real
pitch detector and the real scoring. Lines cleared, from the automated tests:

| Singer | Expert | Easy Challenge | Medium Challenge | Hard Challenge | Expert Challenge |
|---|---|---|---|---|---|
| Trained (within 8 cents, on time) | 100% | 100% | 100% | 100% | 100% |
| Good amateur (within 22 cents, a wrong note now and then) | 98% | 98% | 98% | 98% | 88% |
| Average karaoke singer (within 42 cents, some wrong and missed notes) | 40% | 88% | 60% | 34% | 6% |
| Trained, but sings only 70% of each line | 100% | 100% | 66% | 10% | 0% |
| Trained, but leaves out the highest note of each line | 98% | 96% | 88% | 86% | 8% |
| Hums one note | 2% | 4% | 2% | 2% | 0% |
| Makes noise | 0% | 0% | 0% | 0% | 0% |

("Expert" is Expert without Challenge Mode, with the new microphone timing. Easy Challenge is
wider on pitch than plain Expert, which is why the karaoke singer does better there.)

A trained singer still clears Expert Challenge with a microphone 30 ms earlier or later than the
game assumes, and over Wi-Fi with jitter and lost packets. These are models, not people: real
voices and real rooms are messier, and none of this has been tried on a phone yet. If a level
feels wrong, the numbers are in one table (`GetChallengeLevel` in
`patches-core/0003-vocals-challenge-mode.patch`), and `tests/VocalsTests` prints these tables for
any numbers you try (`-- readme`, `-- grid`, `-- robust`). The same tests run on every build.

## Phone remote: friends' phones as guitars and mics

Other phones on the same Wi-Fi (or on your hotspot) can join from their **browser**, without installing anything.

0. No shared Wi-Fi? On the host, tap the **PHONES** bar on the main menu, then **Start game network**. YARG starts a
   private Wi-Fi network (no internet) and shows a QR code to join it, and a second one to open the page.
   Android asks for the "Nearby devices" permission the first time (Location on Android 12 and older).
1. On the host, the main menu shows a **PHONES** bar with the address; tap it for QR codes.
2. On the guest's phone, open that address. The browser warns that the connection isn't private, because the
   certificate is self-signed. Tap **Advanced → Proceed** (on iPhone, use **Safari**: *Show Details → visit this website*).
3. Type a name, pick **Guitar**, **Bass** or **Vocals** and a difficulty, and tap **Join**. The host adds a player for
   that phone automatically (between songs; a phone that joins mid-song is added when the song ends).
   Pick **Controller only** to have the host set the phone up by hand in Profiles. Turn the phone sideways.
   - **Play** tab: tap the colored **Lanes** (each tap strums, and chords work), or switch to **Frets** for frets + a strum
     pad + whammy.
   - **Menu** tab: D-pad, OK, Back and Start for moving around YARG's menus.
   - **Mic** tab: **Start mic** streams the phone's mic to the host for vocals.
4. On the host: **Profiles** → pick or make a profile → **Add Device** → **"<name> (phone)"** for the controller,
   or **"Phone mic: <name>"** for a vocals profile. The phone's profile reconnects by itself next time.

The host's own mic shows up as **"This phone's mic"**.

How timing is kept tight:
- Each phone syncs its clock with the host and stamps every press with the moment the screen was touched.
- The host plays each press a fixed few milliseconds after that moment (a bit more than the connection's usual
  delay, measured between songs), so Wi-Fi jitter doesn't reach the game, and moves that player's input
  calibration by the same amount, so presses are judged at the moment of the touch.
- While phones are connected, Android's Wi-Fi power saving is turned off for the app.
- The game runs at the screen's refresh rate (120 Hz on a Z Fold 7) when the FPS cap is left at 60.

Notes:
- The `http://` address works too, but only for the controller. Browsers only allow the mic on `https://`.
- Each build makes a new self-signed certificate, so guests see the warning again after an update.
- Anyone on the same Wi-Fi can open the page, so use it on networks you trust.
- Wi-Fi adds a little delay. Run YARG's calibration with a phone if timing feels off.
- A phone that drops off the Wi-Fi joins again by itself as the same player, mid-song too.
- If a guest has the page open twice (say, the QR code scanned again), the newest tab plays. The other
  says so at the top; tap there, or go back to that tab, to play from it instead.

## Deep Music Mode: guitar and bass cover the whole song

For playing without a drum kit or a singer, songs made with the YARG Converter carry a second
guitar part and a second bass part that cover everything:

- **Guitar** adds the vocals and the beat's melody (a chime, a keys loop) to the guitar notes.
  In a rap song that means the rapped syllables plus the chime or piano loop under them.
- **Bass** adds the drum hits between its notes: a kick plays the bass note that is sounding,
  a snare, clap or hat the fret next to it.

To play them, turn on **Deep Music Mode** in the **Modifiers** list when choosing your
difficulty (it is offered on guitar and bass). Each player chooses for themselves, so one person
can play the normal guitar while another plays Deep Music Mode bass. Songs without these parts
(anything not made by the converter) play as normal with the modifier on.

The parts sit in the song's co-op guitar and rhythm slots, so the song list also shows those
instruments for converted songs: picking **Co-op Guitar** or **Rhythm** plays the Deep Music
Mode guitar or bass directly. Convert a song again with the latest converter to add the parts
to songs converted earlier.

## Deep Music Mode on every song

*(Branch `real-instrument-tracks`.)* Songs from the converter (split into instruments) carry
Deep Music Mode's whole-song parts. Every other song now gets them built when it loads, from
the charts it has: **guitar** adds the **keys** part's notes and the **sung melody** (lead
vocals, one note per sung word, the fret following the tune) where the guitar leaves room;
**bass** adds the **drum** hits between its notes (a kick on the fret of the bass note that is
ringing, a snare, tom or cymbal on the fret next to it). Notes only go where there is room (a
beat apart on Easy, down to a tenth of a second on Expert), and a held note lets go before an
added one. A song with only guitar and bass charts has nothing to add from, and plays as before.

Deep Music Mode is offered for **guitar, bass and co-op guitar**. On co-op guitar it plays the
song's own co-op part when it has one (converted songs: that already is the whole-song part),
or the part built as above. It is not offered on rhythm or keys.

## Co-op guitar on every song

*(Branch `real-instrument-tracks`.)* Every song with a guitar part can be played as **co-op
guitar**, so a second guitarist always has the choice. Converted songs that were split into
instruments have a co-op part of their own (Deep Music Mode's whole-song guitar). Songs without
one (converted without splitting, converted before Deep Music Mode, and most other songs) now
offer co-op guitar anyway, with the guitar part's notes.

## Real instrument tracks: what you play cuts out when you miss

*(Branch `real-instrument-tracks`.)* Guitar Hero songs have each instrument on its own audio
track: the guitar plays while you hit notes and cuts out when you miss. Songs from the converter
can now be made the same way. Convert with **TRACKS** on (Colab notebook) or `--tracks`
(`batch_convert.py`, `ConvertSong`) and the song folder holds `guitar.ogg`, `bass.ogg`,
`drums.ogg`, `vocals.ogg` and `keys.ogg` (the piano and every other tuned sound) in place of the
one recording, plus `yarg-tracks.txt`. The notebook keeps these songs apart: with TRACKS on they
go to `My Drive/YARG/output-tracks`, and its zips unzip into `YARG/songs/Converted Tracks`.

In the game it is a modifier, **Track Mode**, on the instrument screen before a song (with the
other modifiers, for guitar and bass). On, the tracks your part plays cut out when you miss.
Off, the song plays as one recording that never cuts out, so the same songs work both ways.

`yarg-tracks.txt`:

```
guitar=guitar,vocals,keys
bass=bass
coop=guitar,vocals,keys
rhythm=bass,drums
```

It says which tracks each part plays, because the converter's charts do not always follow the
instrument they are named after: in a song carried by the voice the guitar chart follows the
vocals and the beat's melody, and Deep Music Mode's bass (`rhythm`) plays the drum hits too
(`coop` is Deep Music Mode's guitar). When you miss, those tracks cut out together, and they come
back on your next hit. With two players on tracks they share (a singer and a guitarist on the
vocals), a miss takes the track down by that player's share instead of silencing it.

- "Mute On Miss" (Settings > Sound) must not be Off; its default works.
- Track Mode only changes guitar and bass parts (and Deep Music Mode's). A drummer or singer on
  a track song still has their own track cut out on a miss, as in any multitrack song.
- A song with tracks is about three to four times as big (7 MB for a two-minute song).
- "Guitar Sound On Notes" leaves these songs alone: they have the real thing.
- Songs converted without tracks, and every other song, play as before.
- The phone's converter app cannot write tracks yet (it has no audio encoder); use the notebook.
- Checked here: the five tracks played together are the original song (the difference is 22 dB
  under it, the encoder's), and the Linux build loads and starts such a song. Hearing a track
  cut out on a miss has not been checked on a phone.

## Guitar sound on your notes

Guitar Hero songs come with the guitar on its own audio track: it plays while you hit notes and
cuts out when you miss. Songs you add yourself (and everything from the converter) are one
recording, so there is nothing to cut. This is off unless you turn it on: set **Settings > Sound >
Guitar Sound On Notes** to Clean, Rock or Heavy, and the game makes that track while the song loads:

1. **It listens to the song.** It works out the recording's tuning (many songs are not at
   exactly A = 440 Hz), its scale, and which note the music is on at each moment, separately
   for the guitar and the bass register.
2. **It gives every note of your chart a pitch**: the note the song is on, in an octave chosen so
   the line goes up when the frets go up and repeats when they repeat. Two- and three-fret notes
   become power chords on the harmony's root.
3. **It plays the chart on a real electric guitar** (or bass, for a bass player). The notes are
   recordings of a real guitar and a real bass, taken straight from the instruments, every note
   several times over. They are played the way a hand plays them: the same note twice in a row is
   a different take each time, hammer-ons use softly played takes with no hard pick attack and
   run on from the note before, a picked note stops the one before it as the pick lands, and
   chords are strummed across the strings. Every take is balanced first so that the note itself,
   not its overtones, carries the sound: a thin take through an amplifier is what makes a
   guitar sound like a beep.
4. **It puts that guitar through an amplifier**: valve stages that overdrive the way valves do,
   a power stage, then a real speaker cabinet (measurements of a Marshall 4x12 with Greenback
   speakers, and of a 4x12 with a Vintage 30, each with a microphone in front) and a little room.
   Three sounds: **Clean**, **Rock** (overdrive) and **Heavy** (high gain).

The result is added to the song as its guitar (or bass) track, so the game treats it like the
real thing: it sounds exactly on the notes whatever your audio delay (including Bluetooth),
drops out when you miss and comes back on the next hit, bends with the whammy bar on sustains,
gets the Star Power reverb, follows the Guitar / Bass volume sliders, and works in practice mode
at any speed. A wrong strum still makes the game's own "clunk".

- **Hear a sound before you choose it**: picking Clean, Rock or Heavy in the setting plays a
  short riff with it (single notes, a power chord and a held note).
- **It plays with the song's dynamics.** The guitar follows the song's loudness, a little over
  half as far as the song moves (never more than 10 dB down or 3 dB up, and smoothly): quiet in
  a quiet intro, digging in for the chorus. At one level all the way through it sat on top of
  the record instead of in it.
- Only for five-fret guitar, bass and rhythm, and only when the song has no separate audio for
  that instrument. Songs with a real guitar track are left alone.
- With several guitarists on one instrument, the hardest difficulty in play is the one you hear.
- "Mute On Miss" must not be Off for the drop-out on misses (it is on by default).
- Loading takes a few seconds longer. Songs over 20 minutes are skipped.
- Every build's release includes `guitar-sound-demo.wav`: the three guitar sounds and the bass,
  recorded by the automated tests.
- The recorded notes are two files in the game's `StreamingAssets/guitar-voice` folder (about
  6 MB). If they cannot be read, a modelled string stands in so there is still a guitar; the
  log says so.

The guitar is FreePats' *Electric Guitar FSBS (direct)*, a Fender recorded from its output jack;
the bass is Karoryfer Samples' *Growlybass*, a Squier Jazz Bass played hard; the speaker cabinets
were measured by Jester Dyne Productions. All are public domain (CC0). How the note files and
the cabinets are made from them, and how the sound was tuned, is in
[tools/string-bank](tools/string-bank/README.md).

## Song Setup: speed, key, a section of the song, freestyle

After picking a song, the instrument / difficulty screen shows a **SONG SETUP** bar at the
bottom. Tap it to open a screen where you can change things while listening to the song:

- **Speed**, 50% to 200%. This is the game's own song speed (the same number as the speed box).
- **Key**, up to 12 semitones down or up, without changing the speed. The generated guitar and
  freestyle notes move with it.
- **Section.** A bar shows the whole song. Drag its two yellow ends to choose where to start and
  stop; tap anywhere on the bar to hear the song from there; or use *Start here* / *End here*
  while it plays. The preview loops the chosen stretch at the chosen speed and key. A song
  started with a section chosen is played in practice mode on that stretch, on repeat.
- **Freestyle guitar** (stays on until you turn it off or close the game). Every strum plays a
  real guitar note at once, chart or no chart, in the song's own scale and tuning:
  - the five frets are the five notes of the song's pentatonic scale, green lowest; a strum
    with no fret is the note just under green;
  - strum down for the low octave, strum up for the octave above;
  - two frets make a power chord, three or more a fuller one;
  - change frets while a note rings to hammer on or pull off; let go of all frets to stop it;
  - the whammy bar bends the note down.
  With freestyle on, your instrument gets no generated track (you are the guitar), and wrong
  strums do not clunk. The chart still scrolls and scores as usual; follow it or ignore it.
  Works for five-fret guitar, bass and rhythm.

The settings belong to the song you opened the screen for. Picking a different song starts
again from normal speed, original key and the whole song.

## Error logs on the phone

The game keeps a copy of its log next to the songs, in **Internal storage/YARG/logs**, so it can be
opened or sent from any file manager:

- `yarg-errors.txt`: only the errors, from every run, with the build and phone they happened on.
  **This is the file to send when something goes wrong.**
- `yarg-log-<date>_<time>.txt`: everything the game logged in one run. The newest 10 are kept.

It records errors the game caught, exceptions nothing caught (with where they happened), and low-memory
warnings. A song that fails to start now says why in a dialog and goes back to the menu, instead of
stopping on an empty screen. Nothing is uploaded: the files stay on the phone. A crash inside a native
library (the app closing with no message) is not recorded.

## Setup (one time)

### 1. Unity license (needs a computer once, e.g. a Chromebook's Linux)
1. Install **Unity Hub**, sign in, then go to **Preferences → Licenses → Add → Get a free personal license**.
2. Open the license file `Unity_lic.ulf`:
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux / Chromebook: `~/.local/share/unity3d/Unity/Unity_lic.ulf`

### 2. GitHub secrets
**Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Value |
|---|---|
| `UNITY_LICENSE` | The entire contents of `Unity_lic.ulf` |
| `UNITY_EMAIL` | Your Unity account email |
| `UNITY_PASSWORD` | Your Unity account password (if you sign in with Google, set one first at id.unity.com) |

Optional, so new builds install over old ones without uninstalling first:
`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS`.
Without them, each build gets a different debug signature, so you have to uninstall the old app to update.

### 3. Build
**Actions → Build Android APK → Run workflow**. When it finishes, open **Releases** on your phone,
download the `.apk` and install it.

## Using it on the phone
1. Open the app and allow **All files access** when Android asks.
2. Put songs (Clone Hero / YARG / Rock Band format folders) in `Internal storage/YARG/songs`.
   Songs from the [YARG Converter](converter/README.md#adding-a-converted-song-to-yarg) are put there for you.
3. In YARG: **Settings → Songs → Scan Songs** (or in the music library: **More Options → Scan Songs**).
4. Add your controller to a profile and pick **Gamepad: PS5 / PS4 / Xbox / other** (or CRKD Guitar). Touch buttons: Profiles → Add Device → Virtual.

Only play songs you own (for example, charts ripped from your own Rock Band DLC) or free/official setlists.

## Licenses
YARG is LGPL-3.0, and these patches use the same license. Android groundwork by putudarma (YARG-DROID) and JordanSanchez06. BASS is free for non-commercial use only
([un4seen license](https://www.un4seen.com/bass.html#license)), so don't sell builds.

The recorded guitar and bass notes (`overlay/Assets/StreamingAssets/guitar-voice`) are made from
public-domain (CC0 1.0) recordings: [Electric Guitar FSBS (direct)](https://freepats.zenvoid.org/ElectricGuitar/clean-electric-guitar.html)
from the FreePats project, and [Growlybass](https://shop.karoryfer.com/pages/free-growlybass) by Karoryfer Samples.
The speaker cabinets the guitar amplifier plays through (`GuitarVoiceCabinets.cs`) are from the
public-domain (CC0 1.0) *Emerald Pack* and *Brutal Pack* cabinet measurements by
[Jester Dyne Productions](https://www.jester-dyne-productions.com/) (Bastian Karschewski).
Thank you to all three.
