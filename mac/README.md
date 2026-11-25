# YARG Resurrected for Mac

A macOS build of the same game as the Android and Windows versions: YARG v0.15.0 (through
[YARG-DROID](https://github.com/putudarma/YARG-DROID)) with everything this repo adds:
guitar sound on notes, Song Setup, menu music playlists, PS5 / Xbox / Switch gamepad layouts,
venues and crowd, vocal parts and sing-off, phones as guitars and mics, and the error log.

It's built from the same `patches/`, `patches-core/` and `overlay/` as the other versions, so new
features land everywhere. This folder only holds what is different for the Mac.

## What's in this folder

| File | What it does |
|---|---|
| `prepare-project.sh` | Puts the Unity project together: YARG-DROID at the pinned commit, the patches, the overlay, the Mac build script, the Discord library and the phone remote's certificate. |
| `Editor/YargMacBuild.cs` | The Unity build entry point (`YARG > Mac > Build` in the editor, `YARG.Editor.Mac.YargMacBuild.Build` in CI). It also adds the microphone line to the app's Info.plist, which macOS needs before it lets vocals use the mic. |

YARG-DROID kept the Mac BASS, STB2CSharp and sqlite libraries. Only the Discord Game SDK bundle
was gone, so `prepare-project.sh` takes it from the official YARG **v0.15.0** source.

How it differs from the other builds:
- **Intel (x64) app.** On Apple Silicon (M1 and later) it runs through **Rosetta 2**, which macOS
  offers to install the first time. Two reasons: the project's sqlite library is Intel-only, and
  native Apple Silicon code has to be code-signed to run, which a build made on GitHub's Linux
  machines isn't.
- **Mono** instead of IL2CPP (IL2CPP for macOS can only be built on a Mac).
- **Not signed or notarized**, so macOS blocks it until you clear the download flag (below).
- Its own name and bundle id (`in.yarg.resurrected`), so settings, profiles and the song cache are kept apart from official YARG
  (`~/Library/Application Support/<company>/YARG Resurrected`).

## Getting a build

**From GitHub:** **Actions → Build Mac → Run workflow** (same Unity secrets as the other builds).
It also runs on every push to `main` that changes the game. Open **Releases** and download
`YARG-Resurrected-Mac-<n>.zip`.

**On your own Mac:**
1. Install Git (with Git LFS) and Unity Hub, then the Unity version in `ProjectSettings/ProjectVersion.txt`
   (currently **6000.3.5f2**) with *Mac Build Support (Mono)*.
2. In Terminal, from this repo: `bash mac/prepare-project.sh` (makes `project/`).
3. `dotnet tool install --global NuGetForUnity.Cli` then `nugetforunity restore project`.
4. Open `project/` in Unity and choose **YARG → Mac → Build**. The app is in `project/build/StandaloneOSX`.
   A build made on a Mac can be switched to Apple Silicon or Universal in *Build Profiles*, but the
   sqlite library would need an arm64 copy first.

## Installing and playing

1. Unzip, and move **YARG Resurrected.app** to **Applications**.
2. The app isn't signed, so macOS says it "is damaged" or "can't be opened". In **Terminal**, run once:
   ```
   xattr -cr "/Applications/YARG Resurrected.app"
   ```
   Then open it normally. (Or: try to open it, then **System Settings → Privacy & Security → Open Anyway**.)
3. **Songs:** **Settings → Songs → Add Folder**, pick your songs folder, then **Scan Songs**.
4. **Vocals:** allow the microphone when macOS asks. If you said no, turn it on in
   **System Settings → Privacy & Security → Microphone**.
5. **Phones as controllers / mics:** allow incoming connections when macOS asks, and keep the phones
   on the same Wi-Fi as the Mac. Then follow *Phone remote* in the main README. (*Start game network* is Android only.)
6. **Controllers:** a PS5 DualSense or an Xbox pad over USB or Bluetooth. Pick the layout in
   **Settings → General → Controller Layout**.
7. **Error logs:** `~/Documents/YARG/logs` (`yarg-errors.txt` is the one to send).

Only play songs you own or free/official setlists. BASS is free for non-commercial use only, so don't sell builds.
