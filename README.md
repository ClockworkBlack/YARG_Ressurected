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
- a controller-type prompt on Android (gamepad / CRKD guitar / other guitar),
- CI that publishes the APK as a GitHub Release.

GitHub Actions downloads YARG-DROID at a pinned commit (`UPSTREAM_REF`), applies `patches/*.patch`,
copies `overlay/` on top, builds the native libraries (`android/native/`), then builds the APK with Unity (game-ci).

`patches-yarg-dev/` keeps an earlier port of YARG's newer `dev` branch, for later.
`tests/RemoteServerTests` checks the phone remote server on every build (runs before the Unity build).
`tests/GameReaderCheck` runs a song written by the converter through YARG's own song scanner and chart loader.

> Status: **experimental.** Expect the first CI runs to need fixes from the logs.

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
on the phone's screen. They now only appear when the guitar player has no controller. Change it
in **Settings > General > Touch Buttons** (Automatic / Always show / Never show).

Vocals: Cross / A hits percussion notes, Create / View is Star Power. In the pause menu the menu buttons above apply.
Any button can be remapped in the profile's binding menu. For a guitar controller, pick
**CRKD Guitar** or **Other guitar** instead of Gamepad.

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
- **Choosing one:** **Settings > General > Venue**. Random by default; or random among street and
  club venues only, random among high-class rooms only, one venue in particular, or None for the
  game's own background. A song that comes with its own background still uses that.
- **Your own pictures or videos:** each venue has a folder in `Internal storage/YARG/venues/`
  (made the first time a song is played). Put a `.jpg`, `.png`, `.mp4` or `.webm` in a venue's
  folder and it is used as that venue's backdrop instead of the drawn stage, with the crowd in
  front of it. Several files in a folder are picked from at random.
- **The crowd** is seen from behind, three rows deep, with mixed skin tones, hair and clothes and
  a strong Black presence; dressed up in the high-class rooms. Groups of neighbors dance simplified
  versions of party dances from the mid-2000s on (Lean wit It, Crank That, Walk It Out, the Jerk,
  the Dougie, Cat Daddy, the Nae Nae, the Whip, the Dab, Hit the Quan, the Milly Rock, the Woah),
  change every sixteen beats, and now and then the whole room swag surfs or puts its hands up.
  Turn it off with **Settings > General > Dancing Crowd**.

Everything here is drawn by code from simple shapes, so it is cartoon-like rather than realistic.

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

## Vocals: sing or rap, pick an artist, sing-off

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

**Hearing yourself.** Your voice is played back to you (**Settings > Sound > Vocal Monitoring
Volume**) only while headphones are connected, wired, USB or Bluetooth. Through the phone's own
speaker the microphone would hear it again and squeal, so without headphones it is silent.

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

Not tried on a phone yet: all of this builds and the song files are checked against the game's
own reader, but the sing-off board, the artist names, the level hints and the two-microphone
recording have not been seen running.

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
3. **It plays the chart on a modelled electric guitar** (or bass, for a bass player): plucked
   strings through an amplifier and speaker cabinet, with hammer-ons softer than picked notes,
   vibrato on long notes and strummed chords. Three sounds: Clean, Rock, Heavy.

The result is added to the song as its guitar (or bass) track, so the game treats it like the
real thing: it sounds exactly on the notes whatever your audio delay (including Bluetooth),
drops out when you miss and comes back on the next hit, bends with the whammy bar on sustains,
gets the Star Power reverb, follows the Guitar / Bass volume sliders, and works in practice mode
at any speed. A wrong strum still makes the game's own "clunk".

- Only for five-fret guitar, bass and rhythm, and only when the song has no separate audio for
  that instrument. Songs with a real guitar track are left alone.
- With several guitarists on one instrument, the hardest difficulty in play is the one you hear.
- "Mute On Miss" must not be Off for the drop-out on misses (it is on by default).
- Loading takes a few seconds longer. Songs over 20 minutes are skipped.
- Every build's release includes `guitar-sound-demo.wav`: the three guitar sounds and the bass,
  recorded by the automated tests.

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
