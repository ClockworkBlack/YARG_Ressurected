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
- CI that publishes the APK as a GitHub Release.

GitHub Actions downloads YARG-DROID at a pinned commit (`UPSTREAM_REF`), applies `patches/*.patch`,
copies `overlay/` on top, builds the native libraries (`android/native/`), then builds the APK with Unity (game-ci).

`patches-yarg-dev/` keeps an earlier port of YARG's newer `dev` branch, for later.
`tests/RemoteServerTests` checks the phone remote server on every build (runs before the Unity build).

> Status: **experimental.** Expect the first CI runs to need fixes from the logs.

## Gamepad (Guitar Hero 2 layout)

YARG's default gamepad layout already matches Guitar Hero 2 on a DualShock:

| Guitar | Gamepad |
|---|---|
| Green / Red / Yellow / Blue | L2 / L1 / R1 / R2 (LT / LB / RB / RT) |
| Orange | Cross / A |
| Strum | D-pad Up / Down |
| Star Power | Select / Back |
| Whammy | Left stick left/right |

When you add a controller to a profile, pick **Gamepad**. You can remap any button in the profile's binding menu.

## Phone remote: friends' phones as guitars and mics

Other phones on the same Wi-Fi (or on your hotspot) can join from their **browser**, without installing anything.

1. On the host, the main menu shows **PHONES: open https://192.168.x.x:7777**.
2. On the guest's phone, open that address. The browser warns that the connection isn't private, because the
   certificate is self-signed. Tap **Advanced → Proceed** (on iPhone, use **Safari**: *Show Details → visit this website*).
3. Type a name and tap **Join**. Turn the phone sideways.
   - **Play** tab: tap the colored **Lanes** (each tap strums, and chords work), or switch to **Frets** for frets + a strum
     pad + whammy.
   - **Menu** tab: D-pad, OK, Back and Start for moving around YARG's menus.
   - **Mic** tab: **Start mic** streams the phone's mic to the host for vocals.
4. On the host: **Profiles** → pick or make a profile → **Add Device** → **"<name> (phone)"** for the controller,
   or **"Phone mic: <name>"** for a vocals profile. The phone's profile reconnects by itself next time.

The host's own mic shows up as **"This phone's mic"**.

Notes:
- The `http://` address works too, but only for the controller. Browsers only allow the mic on `https://`.
- Each build makes a new self-signed certificate, so guests see the warning again after an update.
- Anyone on the same Wi-Fi can open the page, so use it on networks you trust.
- Wi-Fi adds a little delay. Run YARG's calibration with a phone if timing feels off.

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
3. In YARG: **Settings → Songs → Scan**.
4. Add your controller to a profile and pick **Gamepad** (or CRKD Guitar). Touch buttons: Profiles → Add Device → Virtual.

Only play songs you own (for example, charts ripped from your own Rock Band DLC) or free/official setlists.

## Licenses
YARG is LGPL-3.0, and these patches use the same license. Android groundwork by putudarma (YARG-DROID) and JordanSanchez06. BASS is free for non-commercial use only
([un4seen license](https://www.un4seen.com/bass.html#license)), so don't sell builds.
