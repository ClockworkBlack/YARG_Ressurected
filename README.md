# YARG Resurrected (Android)

An Android (arm64) build of [YARG – Yet Another Rhythm Game](https://github.com/YARC-Official/YARG),
aimed at phones like the Galaxy Z Fold 7. It replaces the archived `YARG-Android` fork.

This repo doesn't copy YARG's code. It builds on **[putudarma/YARG-DROID](https://github.com/putudarma/YARG-DROID)**
(YARG v0.15.0 stable + Android fixes: IL2CPP-safe BASS callbacks, StreamingAssets extraction,
`/storage/emulated/0/YARG/songs`, draggable touch controls). YARG-DROID only ships 32-bit (ARMv7) builds,
which can't run on 64-bit-only chips like the Snapdragon 8 Elite. This repo adds:

- a **64-bit (arm64) IL2CPP** build for modern phones,
- arm64 native libraries (BASS, STB2CSharp, sqlite3) built with the NDK,
- a controller-type prompt on Android (gamepad / CRKD guitar / other guitar),
- CI that publishes the APK as a GitHub Release.

GitHub Actions downloads YARG-DROID at a pinned commit (`UPSTREAM_REF`), applies `patches/*.patch`,
copies `overlay/` on top, builds the native libraries (`android/native/`), then builds the APK with Unity (game-ci).

`patches-yarg-dev/` keeps an earlier port of YARG's newer `dev` branch, for later.

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
