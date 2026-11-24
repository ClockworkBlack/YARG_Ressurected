# YARG Resurrected (Android)

An Android (arm64) build of [YARG – Yet Another Rhythm Game](https://github.com/YARC-Official/YARG),
aimed at phones like the Galaxy Z Fold 7. It replaces the archived `YARG-Android` fork.

This repo doesn't copy YARG's code. It holds **patches on top of an exact upstream YARG commit**, so it
can follow new YARG releases. GitHub Actions does the rest:

1. downloads YARG at the pinned commit (`UPSTREAM_REF` in the workflow),
2. applies `patches/*.patch` and copies `overlay/` on top,
3. builds the Android native libraries with the NDK (`android/native/`),
4. builds the APK with Unity (game-ci) and publishes it as a GitHub **Release** you can download on your phone.

> Status: **experimental first port.** It hasn't been built yet, so expect the first CI runs to fail
> and need fixes from the logs.

## What the port changes

| Area | Change |
|---|---|
| Audio | BASS, BASSmix, BASS_FX, BASSOPUS Android libs + YargAudio built for arm64 with the NDK; Android `dlopen` paths |
| Images / scores DB | `STB2CSharp` and `sqlite3` rebuilt for arm64 |
| StreamingAssets | Unpacked from the APK on first launch (YARG reads them as regular files) |
| Songs | Asks for "All files access" and adds `/storage/emulated/0/YARG/Songs` as a song folder |
| Video backgrounds | Turned off (libVLC isn't bundled) |
| Desktop-only plugins | Kept out of the APK automatically |
| Controllers | When you add a controller, Android asks what it is, like Windows does |
| **New: GH2-style gamepad layout** | Lets you play with a normal gamepad, like Guitar Hero 2 on a controller |

### Gamepad: Guitar Hero 2 style (shoulder frets)

| Guitar | Gamepad |
|---|---|
| Green | L2 / LT |
| Red | L1 / LB |
| Yellow | R1 / RB |
| Blue | R2 / RT |
| Orange | Cross / A |
| Strum | D-pad Down/Right, or Square / Circle (GH2 had no strum on a pad, but YARG needs one) |
| Star Power | Select / Back or D-pad Up |
| Whammy | Left stick left/right |

YARG's normal "Gamepad as 5-fret guitar" layout (face buttons as frets) is still available. You can remap
any button in the profile's binding menu.

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
2. Put songs (Clone Hero / YARG / Rock Band format folders) in `Internal storage/YARG/Songs`.
3. In YARG: **Settings → Songs → Scan**.
4. Add your controller to a profile and pick **Gamepad – Guitar Hero 2 style** (or CRKD Guitar).

Only play songs you own (for example, charts ripped from your own Rock Band DLC) or free/official setlists.

## Licenses
YARG is LGPL-3.0, and these patches use the same license. BASS is free for non-commercial use only
([un4seen license](https://www.un4seen.com/bass.html#license)), so don't sell builds.
