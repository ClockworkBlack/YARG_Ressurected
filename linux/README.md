# YARG Resurrected for Linux and Chromebook

A Linux (64-bit Intel/AMD, `x86_64`) build of the same game as the Android, Windows and Mac
versions, with everything this repo adds: guitar sound on notes, Song Setup, menu music, gamepad
layouts, venues and crowd, story mode, vocal parts, the mic test, phones as controllers and mics,
and the error log.

It runs on desktop Linux (Ubuntu, Debian, Mint, Fedora, SteamOS desktop mode…) and on
**Chromebooks with an Intel or AMD processor**, through ChromeOS's *Linux development environment*.
ARM Chromebooks can't run it, but they can run the Android version (see [ARM Chromebooks](#arm-chromebooks)).

---

## Chromebook: step by step

### 1. Check your Chromebook's processor

Open **Settings → About ChromeOS → Diagnostics** and look at **CPU**. *Intel*, *Celeron*, *Pentium*,
*Core*, *AMD* or *Ryzen* = this build works. *MediaTek*, *Snapdragon*, *Kompanio* or *ARM* = go to
[ARM Chromebooks](#arm-chromebooks) instead.

(Once Linux is on, the Terminal command `uname -m` says `x86_64` for Intel/AMD.)

### 2. Turn on Linux (one time)

1. **Settings → About ChromeOS → Developers → Linux development environment → Set up.**
   (Or search *Linux* in Settings.) School and work Chromebooks may have this turned off by the admin.
2. Pick a disk size of **at least 10 GB**, more if you'll keep songs in Linux.
3. Wait for the **Terminal** window to open. Linux is ready.

### 3. Download and install the game

1. On GitHub, open **Releases** and download the newest **YARG Resurrected for Linux and Chromebook**:
   `YARG-Resurrected-Linux-<n>.tar.gz`.
2. In the **Files** app, drag the file from **Downloads** to **Linux files**.
3. Open **Terminal** (in the launcher's *Linux apps* folder) and run, with your build number in place of `<n>`:
   ```
   tar xzf YARG-Resurrected-Linux-<n>.tar.gz
   bash YARG-Resurrected/install.sh
   ```
   The script installs the few system libraries the game needs, copies the game to
   `~/.local/share/YARG-Resurrected`, adds **YARG Resurrected** to the launcher, and makes a
   `YARG/songs` folder.
4. Start **YARG Resurrected** from the launcher (*Linux apps* folder). You can delete the `.tar.gz`
   and the `YARG-Resurrected` folder from Linux files afterwards.

**Updating:** download the newer `.tar.gz` and repeat step 3. Your settings, profiles and scores are kept.

### 4. Add songs

Either:
- **Copy songs into Linux** (fastest to load): in Files, put your song folders in
  **Linux files → YARG → songs**. In the game, **Settings → Songs → Add Folder**, open
  `/home/<your name>/YARG/songs`, choose it, then **Scan Songs**.
- **Or leave them where they are:** in Files, right-click the songs folder → **Share with Linux**.
  In the game, Add Folder and open `/mnt/chromeos/MyFiles/…` (Downloads is under
  `/mnt/chromeos/MyFiles/Downloads`).

### 5. Microphone, controllers and phones (each one time)

All of these are in **Settings → About ChromeOS → Developers → Linux development environment**:

- **Vocals with the Chromebook's mic or a USB mic:** turn on **Allow Linux to access your microphone**.
  Linux needs restarting afterwards (right-click Terminal in the shelf → **Shut down Linux**, then reopen).
- **USB gamepad, guitar or drums:** plug it in, then under **Manage USB devices** switch it on for Linux.
  ChromeOS also asks *"Connect to Linux?"* when you plug in a device. Do this again if you use a different port.
- **Bluetooth controllers** pair with ChromeOS and may not reach Linux apps. If the game doesn't see one,
  use it with a USB cable, or use the phone remote below.
- **Phones as guitars and mics:** Linux apps sit behind ChromeOS's own network, so phones can't reach
  the game until you forward its port:
  1. **Port forwarding → Add:** port **7777**, protocol **TCP**, label *YARG*. Turn it on.
  2. Find the Chromebook's address: **Settings → Network → Wi-Fi →** your network → the
     **IP address** (e.g. `192.168.1.23`).
  3. On the phone (same Wi-Fi), open **`https://<that address>:7777`** (tap past the "not private"
     warning) and join as in *Phone remote* in the main README.
  The PHONES bar inside the game shows Linux's internal address, which phones can't use, so go by the
  Wi-Fi address from step 2. If the game says it's using port 7778 or 7779, forward that number instead.

### If something's wrong

- **Black window, or very slow:** Linux GPU support may be off on your Chromebook. Make sure ChromeOS
  is up to date; on older versions turn on `chrome://flags/#crostini-gpu-support` and restart. Lower
  the resolution and quality in **Settings → Graphics**.
- **No sound:** check ChromeOS's volume and output device, then restart Linux.
- **Controller not detected:** check **Manage USB devices** (above), then restart the game.
- **Error logs:** Files → **Linux files → YARG → logs**. `yarg-errors.txt` is the one to send.

---

## ARM Chromebooks

The Linux build is for Intel/AMD only. ARM Chromebooks (MediaTek, Snapdragon) run Android apps, so
use the Android version, `YARG-Resurrected-<n>.apk` from Releases. It isn't on the Play Store, so it
goes on through Linux's Android debugging:

1. Turn on Linux as in step 2 above.
2. **Settings → About ChromeOS → Developers → Linux development environment → Develop Android apps →
   Enable ADB debugging.** ChromeOS restarts. On some older Chromebooks this needs a **powerwash
   (factory reset), which erases the Chromebook**; ChromeOS tells you first, and you can cancel.
3. In Terminal:
   ```
   sudo apt-get install -y adb
   adb connect arc
   ```
   Allow the debugging prompt that appears, then put the `.apk` in Linux files and run:
   ```
   adb install YARG-Resurrected-<n>.apk
   ```
4. Open **YARG** from the launcher and follow *Using it on the phone* in the main README
   (songs go in the Android **YARG/songs** folder).

---

## Desktop Linux

Download the `.tar.gz`, then:
```
tar xzf YARG-Resurrected-Linux-<n>.tar.gz
bash YARG-Resurrected/install.sh
```
On Debian / Ubuntu / Mint the script installs the system libraries it needs. On other distributions
it skips that step: install ALSA (libasound), OpenGL, X11, libudev and hidapi (`hidapi`, for controllers)
with your package manager if the game won't start or controllers aren't seen. Or run `YARG-Resurrected/YARG.x86_64` directly without installing.

Controllers work through the usual Linux input system. If a guitar or drum kit isn't detected, your
user may need permission for `/dev/hidraw*` (a udev rule, as for Clone Hero).

---

## How this build is made

| File | What it does |
|---|---|
| `prepare-project.sh` | Puts the Unity project together: YARG-DROID at the pinned commit, the patches, the overlay, the Linux build script, the Linux BASS and Discord libraries (from official YARG v0.15.0; YARG-DROID removed them) and the phone remote's certificate. |
| `Editor/YargLinuxBuild.cs` | The Unity build entry point (`YARG > Linux > Build`, or `YARG.Editor.Linux.YargLinuxBuild.Build` in CI). Only the Linux x86_64 libraries go in, OpenGL comes before Vulkan (Chromebooks' Linux has OpenGL), Mono scripting. |
| `install.sh` | The installer above. It's included in every download. |

**From GitHub:** **Actions → Build Linux (and Chromebook) → Run workflow** (same Unity secrets as the
other builds). It also runs on every push to `main` that changes the game, and publishes
`YARG-Resurrected-Linux-<n>.tar.gz` under Releases.

**On your own Linux machine:** `bash linux/prepare-project.sh`, then
`dotnet tool install --global NuGetForUnity.Cli && nugetforunity restore project`, open `project/` in
Unity 6000.3.5f2 (with *Linux Build Support (Mono)*), and choose **YARG → Linux → Build**.

Only play songs you own or free/official setlists. BASS is free for non-commercial use only, so don't sell builds.
