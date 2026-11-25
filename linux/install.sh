#!/usr/bin/env bash
# YARG Resurrected - install on a Chromebook (Linux development environment) or any
# Debian / Ubuntu based Linux.
#
#   tar xzf YARG-Resurrected-Linux-<n>.tar.gz
#   bash YARG-Resurrected/install.sh
#
# What it does:
#   1. checks this is a 64-bit Intel/AMD machine (the game doesn't run on ARM Linux),
#   2. installs the few system libraries the game needs (asks for your password via sudo;
#      on a Chromebook there is no password, it just works),
#   3. copies the game to ~/.local/share/YARG-Resurrected,
#   4. adds "YARG Resurrected" to the app launcher (on a Chromebook: the ChromeOS launcher,
#      in the "Linux apps" folder),
#   5. makes ~/YARG/songs for your songs.
# Run it again to update to a newer build: your settings, profiles and scores are kept.
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="$HOME/.local/share/YARG-Resurrected"
DESKTOP="$HOME/.local/share/applications/yarg-resurrected.desktop"

say() { printf '\n\033[1m%s\033[0m\n' "$*"; }

if [ ! -f "$SRC/YARG.x86_64" ]; then
    echo "YARG.x86_64 isn't next to this script. Unpack the whole download first:" >&2
    echo "  tar xzf YARG-Resurrected-Linux-<n>.tar.gz && bash YARG-Resurrected/install.sh" >&2
    exit 1
fi

arch="$(uname -m)"
if [ "$arch" != "x86_64" ]; then
    say "This computer's processor is $arch, but this build is for Intel/AMD (x86_64)."
    echo "On an ARM Chromebook, install the Android version (the .apk) instead."
    echo "See 'ARM Chromebooks' in linux/README.md."
    exit 1
fi

if command -v apt-get >/dev/null 2>&1; then
    say "Installing system libraries (sound, graphics, controllers)"
    sudo apt-get update -qq || true
    # Names differ between Debian versions (e.g. libasound2 vs libasound2t64), so try each one
    for pkg in libasound2t64 libasound2 libasound2-plugins libpulse0 libgl1 libegl1 libglu1-mesa \
               libx11-6 libxcursor1 libxrandr2 libxi6 libxinerama1 libxkbcommon0 \
               libudev1 libusb-1.0-0 ca-certificates; do
        if apt-cache show "$pkg" >/dev/null 2>&1; then
            sudo apt-get install -y -qq "$pkg" >/dev/null 2>&1 || echo "  (couldn't install $pkg, skipping)"
        fi
    done
    # The game plays sound through ALSA. On systems where PipeWire is the sound server (newer
    # Chromebook Linux, Ubuntu 22.10+), ALSA needs PipeWire's plugin or the game starts silent.
    if pgrep -x pipewire >/dev/null 2>&1 && apt-cache show pipewire-alsa >/dev/null 2>&1; then
        sudo apt-get install -y -qq pipewire-alsa >/dev/null 2>&1 || echo "  (couldn't install pipewire-alsa)"
    fi
else
    say "No apt-get here, so system libraries were not installed."
    echo "Install ALSA (libasound), OpenGL, X11 and libudev with your package manager if the game won't start."
fi

say "Copying the game to $DEST"
mkdir -p "$DEST"
if [ "$SRC" != "$DEST" ]; then
    # Replace the old game files; settings live elsewhere (~/.config/unity3d) and are kept
    rm -rf "$DEST"/YARG_Data
    cp -a "$SRC"/. "$DEST"/
fi
chmod +x "$DEST/YARG.x86_64"

say "Adding YARG Resurrected to the app launcher"
mkdir -p "$(dirname "$DESKTOP")"
icon="$DEST/icon.png"
[ -f "$icon" ] || icon="applications-games"
cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=YARG Resurrected
Comment=Rhythm game: guitar, bass, drums and vocals
Exec="$DEST/YARG.x86_64"
Path=$DEST
Icon=$icon
Terminal=false
Categories=Game;Music;
EOF
chmod +x "$DESKTOP"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$(dirname "$DESKTOP")" >/dev/null 2>&1 || true

mkdir -p "$HOME/YARG/songs"

say "Done."
cat <<EOF
- Start it from the launcher ("YARG Resurrected", in the Linux apps folder on a Chromebook),
  or run:  "$DEST/YARG.x86_64"
- Put songs in ~/YARG/songs (on a Chromebook: Files app > Linux files > YARG > songs), then in
  the game: Settings > Songs > Add Folder > choose $HOME/YARG/songs > Scan Songs.
- Error logs: ~/YARG/logs (yarg-errors.txt is the one to send).
EOF
