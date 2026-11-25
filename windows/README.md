# YARG Resurrected for Windows

A Windows (64-bit) build of the same game as the Android APK: YARG v0.15.0 (through
[YARG-DROID](https://github.com/putudarma/YARG-DROID)) with everything this repo adds:

- **Guitar sound on your notes** for songs without a guitar track
- **Song Setup**: speed, key, a section of the song, freestyle guitar
- **Menu music**: startup song, playlists, shuffle
- **Gamepad layouts**: PS5 / PS4 (default), Xbox, Switch, Japanese PlayStation, with button names in the help bar
- **Phones as guitars and mics** over Wi-Fi (the PHONES bar on the main menu)
- Song start errors shown in a dialog, and an error log you can find (see below)

The game is built from the same `patches/`, `patches-core/` and `overlay/` as the Android version,
so every new feature lands in both. This folder only holds what is different for Windows.

## What's in this folder

| File | What it does |
|---|---|
| `prepare-project.sh` | Puts the Unity project together: YARG-DROID at the pinned commit, the patches, the overlay, the Windows build script, the Windows DLLs and the phone remote's certificate. |
| `Editor/YargWindowsBuild.cs` | The Unity build entry point (`YARG > Windows > Build` in the editor, `YARG.Editor.Windows.YargWindowsBuild.Build` in CI). |

Why the DLLs: YARG-DROID removed the desktop libraries (BASS, BASS FX / Mix / Opus,
STB2CSharp, Discord Game SDK) to keep the APK small. `prepare-project.sh` takes the Windows x64
copies, with their Unity `.meta` files, from the official YARG **v0.15.0** source, the release
YARG-DROID is built on. The build script then puts only Windows x64 libraries in the game folder.

Other differences from the Android build:
- **Mono** instead of IL2CPP (a Windows IL2CPP build can only be made on a Windows machine; Mono plays the same).
- The Android-only editor script (`overlay/Assets/Editor/Android`) is left out, because a
  Windows-only Unity install has no Android module.
- Product name **YARG Resurrected**, so its settings, profiles and song cache are kept apart from an
  official YARG install (`%USERPROFILE%\AppData\LocalLow\<company>\YARG Resurrected`).
- The game keeps running when the window is in the background, so menu music and connected phones don't stop.

## Getting a build

**From GitHub (no Unity needed):** **Actions → Build Windows → Run workflow**. It uses the same
Unity secrets as the Android build (see the main README's Setup). When it finishes, open
**Releases**, download `YARG-Resurrected-Windows-<n>.zip`, unzip it anywhere and run `YARG.exe`.
It also runs by itself on every push to `main` that changes the game.

**On your own PC:**
1. Install **Git for Windows** (with Git LFS) and Unity Hub, then the Unity version in the project's
   `ProjectSettings/ProjectVersion.txt` (currently **6000.3.5f2**) with *Windows Build Support (Mono)*.
2. In Git Bash, from this repo: `bash windows/prepare-project.sh` (makes `project/`).
3. Restore NuGet packages: `dotnet tool install --global NuGetForUnity.Cli` then `nugetforunity restore project`
   (or open the project and use *NuGet → Restore Packages*).
4. Open `project/` in Unity and choose **YARG → Windows → Build**. The game is in `project/build/StandaloneWindows64`.

## Playing on Windows

- **Songs:** **Settings → Songs → Add Folder**, pick your Clone Hero / YARG / Rock Band songs folder, then **Scan Songs**.
- **Controllers:** a PS5 DualSense works over USB or Bluetooth; Xbox pads are XInput and ask which kind of
  controller they are when added to a profile, as on official YARG. Pick your layout in
  **Settings → General → Controller Layout**.
- **Phones as controllers / mics:** the PC and the phones must be on the same network. The first time,
  **Windows Firewall** asks whether YARG may use the network: allow **Private networks**, or phones
  can't connect. Then follow *Phone remote* in the main README. (*Start game network*, the phone hotspot,
  is Android only: on a PC use your normal Wi-Fi, or turn on Windows' **Mobile hotspot** and join the phones to it.)
- **Error logs:** `Documents\YARG\logs`, the same files as on the phone (`yarg-errors.txt` is the one to send).

Only play songs you own or free/official setlists. BASS is free for non-commercial use only, so don't sell builds.
