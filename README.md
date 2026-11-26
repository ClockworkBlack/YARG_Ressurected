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
- the **phone's own mic** for vocals, plus **other phones as controllers and mics** over Wi-Fi (see below),
- a controller-type prompt on Android (gamepad / CRKD guitar / other guitar),
- a **Deep Music Mode** modifier: guitar and bass cover the whole song (see below),
- CI that publishes the APK as a GitHub Release.

GitHub Actions downloads YARG-DROID at a pinned commit (`UPSTREAM_REF`), applies `patches/*.patch`,
copies `overlay/` on top, builds the native libraries (`android/native/`), then builds the APK with Unity (game-ci).

`patches-yarg-dev/` keeps an earlier port of YARG's newer `dev` branch, for later.
`tests/RemoteServerTests` checks the phone remote server on every build (runs before the Unity build).

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
| Yellow action (Play Show / Start Setlist) | Triangle | Y | Y | Triangle |
| Blue action (Filters) | Square | X | X | Square |
| More Options (orange) | L1 or R1 | LB or RB | L or R | L1 or R1 |
| Start / pause | Options | Menu | + | Options |
| Select ("go to the song that's playing" on the main menu) | Create | View | − | Create |

While a gamepad is connected, the bar at the bottom of the screen names the button next to each
action (e.g. "Back  Circle"). Turn this off with **Settings > General > Show Controller Button Names**.

A gamepad in a profile uses that profile's menu buttons. They are set from the Controller Layout
when you add the gamepad to the profile, so after changing the layout, remove the gamepad from
its profile and add it again (or change the buttons in the profile's binding menu).

### Playing a song

When you add a gamepad to a profile, pick **Gamepad: PS5 / PS4 / Xbox / other**. Guitar and bass
use the Guitar Hero 2 layout on a DualShock / DualSense:

| Guitar | PS5 / PS4 | Xbox |
|---|---|---|
| Green / Red / Yellow / Blue | L2 / L1 / R1 / R2 | LT / LB / RB / RT |
| Orange | Cross | A |
| Strum | D-pad Up / Down | D-pad Up / Down |
| Star Power | Create | View |
| Whammy | Left stick left/right | Left stick left/right |
| Pause | Options | Menu |

Vocals: Cross / A hits percussion notes, Create / View is Star Power. In the pause menu the menu buttons above apply.
Any button can be remapped in the profile's binding menu. For a guitar controller, pick
**CRKD Guitar** or **Other guitar** instead of Gamepad.

## Menu music: pick a startup song and a playlist

The menus play songs from your library in the background. You choose what plays:

- **A song on startup.** In the music library, highlight a song, open **More Options**
  (orange / LB) and choose **Play On Startup**. It plays first every time the game starts, then
  the menu music carries on as below. Choose **Don't Play On Startup** on the same song, or set
  **Settings > Sound > Startup Song** to **None**, to turn it off.
- **Which songs play after it.** **Settings > Sound > Menu Music**: *Whole library*, *Favorites*,
  or any of your playlists. Or in the music library's **Playlists** view, highlight a playlist
  (or Favorites), open **More Options** and choose **Use As Menu Music** (and **Stop Using As
  Menu Music** to go back to the whole library). The music switches right away.
- **Order.** **Settings > Sound > Shuffle Menu Music**: on plays them in random order, off plays
  the playlist in its own order (and loops).
- **Volume** stays **Settings > Sound > Music Player Volume** (0 turns menu music off).

If the playlist is empty, deleted, or none of its songs are in the library, the whole library
plays instead.

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

## Guitar sound on your notes

Guitar Hero songs come with the guitar on its own audio track: it plays while you hit notes and
cuts out when you miss. Songs you add yourself (and everything from the converter) are one
recording, so there is nothing to cut. With **Settings > Sound > Guitar Sound On Notes** on
(the default), the game makes that track while the song loads:

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
