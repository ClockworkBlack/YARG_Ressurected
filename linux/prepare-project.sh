#!/usr/bin/env bash
# YARG Resurrected - put together the Unity project for the Linux (x86_64) build, which is also
# the one Chromebooks run (Linux development environment).
#
# Same game as the Android, Windows and Mac builds (same upstream commit, same patches and
# overlay), plus the Linux pieces:
#   - BASS / BASS FX / BASS Mix / BASS Opus and the Discord Game SDK for Linux, taken from the
#     official YARG v0.15.0 release source (YARG-DROID removed them; it kept the Linux sqlite,
#     STB2CSharp and libusb libraries),
#   - the Linux build entry point (linux/Editor/YargLinuxBuild.cs).
# The Android-only editor script is left out: it needs Unity's Android module, which a
# Linux-only editor does not have.
#
# Usage:  bash linux/prepare-project.sh [project-dir]     (default: ./project)
# Works on Linux and macOS. Needs git and openssl.
# Set GIT_LFS_SKIP_SMUDGE=1 to skip the art assets (only useful for checking that patches apply).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_DIR="${1:-$REPO_ROOT/project}"
UPSTREAM_REPO="${UPSTREAM_REPO:-putudarma/YARG-DROID}"
UPSTREAM_REF="${UPSTREAM_REF:-9bebad16e63cde7022a1fb7b261b541c1fae5361}"
# Official YARG release that YARG-DROID is based on; only its Linux libraries are used.
NATIVES_REPO="${NATIVES_REPO:-YARC-Official/YARG}"
NATIVES_REF="${NATIVES_REF:-refs/tags/v0.15.0}"

log() { echo "[linux] $*"; }

if [ ! -d "$PROJECT_DIR/.git" ]; then
    log "Getting $UPSTREAM_REPO at $UPSTREAM_REF into $PROJECT_DIR"
    git init -q "$PROJECT_DIR"
    git -C "$PROJECT_DIR" remote add origin "https://github.com/$UPSTREAM_REPO"
    GIT_LFS_SKIP_SMUDGE=1 git -C "$PROJECT_DIR" fetch -q --depth 1 origin "$UPSTREAM_REF"
    GIT_LFS_SKIP_SMUDGE=1 git -C "$PROJECT_DIR" checkout -q FETCH_HEAD
    GIT_LFS_SKIP_SMUDGE=1 git -C "$PROJECT_DIR" submodule update -q --init --recursive --depth 1
    if [ "${GIT_LFS_SKIP_SMUDGE:-0}" != "1" ]; then
        log "Pulling LFS assets (art, fonts)"
        git -C "$PROJECT_DIR" lfs install --local
        git -C "$PROJECT_DIR" lfs pull
    fi
else
    log "$PROJECT_DIR already exists; reusing it (delete it to start clean)"
fi

cd "$PROJECT_DIR"

if [ -f .yarg-resurrected-prepared ]; then
    log "Patches were already applied here; skipping to the native libraries"
else
    for p in "$REPO_ROOT"/patches/*.patch; do
        log "Applying $(basename "$p")"
        git apply --whitespace=nowarn "$p"
    done
    for p in "$REPO_ROOT"/patches-core/*.patch; do
        log "Applying $(basename "$p") to YARG.Core"
        git -C YARG.Core apply --whitespace=nowarn "$p"
    done
    touch .yarg-resurrected-prepared
fi

log "Copying the overlay (game features) and the Linux build script"
cp -r "$REPO_ROOT"/overlay/. .
rm -rf Assets/Editor/Android Assets/Editor/Android.meta
mkdir -p Assets/Editor/Linux
cp "$REPO_ROOT"/linux/Editor/*.cs Assets/Editor/Linux/

log "Restoring the Linux native libraries from $NATIVES_REPO $NATIVES_REF"
NATIVES_TMP="$(mktemp -d)"
trap 'rm -rf "$NATIVES_TMP"' EXIT
git init -q "$NATIVES_TMP"
git -C "$NATIVES_TMP" remote add origin "https://github.com/$NATIVES_REPO"
git -C "$NATIVES_TMP" fetch -q --depth 1 --filter=blob:none origin "$NATIVES_REF"
NATIVE_FILES=(
    Assets/Plugins/BassNative/Linux/x86_64/libbass.so
    Assets/Plugins/BassNative/Linux/x86_64/libbass_fx.so
    Assets/Plugins/BassNative/Linux/x86_64/libbassmix.so
    Assets/Plugins/BassNative/Linux/x86_64/libbassopus.so
    Assets/Plugins/DiscordGameSDK/Plugins/x86_64/discord_game_sdk.so
)
for f in "${NATIVE_FILES[@]}"; do
    mkdir -p "$(dirname "$f")"
    git -C "$NATIVES_TMP" show "FETCH_HEAD:$f" > "$f"
    git -C "$NATIVES_TMP" show "FETCH_HEAD:$f.meta" > "$f.meta"
    log "  $f ($(wc -c < "$f") bytes)"
done
for f in Assets/Plugins/STB2CSharp/Linux/x86_64/STB2CSharp.so Assets/Plugins/Sqlite3/Linux/x86_64/libsqlite3.so; do
    [ -s "$f" ] || { echo "[linux] $f is missing from the base project" >&2; exit 1; }
done

# Minis (MIDI controller support) crashes the game at startup when MIDI can't start, which is
# the case on Linux without an ALSA sequencer (/dev/snd/seq), Chromebooks' Linux included: the
# failed backend stays hooked to the input system and keeps calling RtMidi on a broken handle
# (SIGSEGV in rtmidi_get_port_count). Embed the same Minis commit in Packages/ (which overrides
# the git copy) with linux/minis-no-midi-crash.patch, so MIDI is just switched off instead.
MINIS_REF="$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["dependencies"]["jp.keijiro.minis"]["hash"])' Packages/packages-lock.json)"
if [ -n "$MINIS_REF" ] && [ ! -d Packages/jp.keijiro.minis ]; then
    log "Embedding Minis $MINIS_REF with the no-MIDI crash fix"
    git init -q "$NATIVES_TMP/minis"
    git -C "$NATIVES_TMP/minis" remote add origin https://github.com/TheNathannator/Minis
    git -C "$NATIVES_TMP/minis" fetch -q --depth 1 origin "$MINIS_REF"
    git -C "$NATIVES_TMP/minis" checkout -q FETCH_HEAD
    cp -r "$NATIVES_TMP/minis/Packages/jp.keijiro.minis" Packages/
    git apply --whitespace=nowarn --directory=Packages/jp.keijiro.minis "$REPO_ROOT/linux/minis-no-midi-crash.patch"
fi

# Self-signed certificate for the phone remote's https page (phone mics need a secure page)
log "Making the phone remote certificate"
mkdir -p Assets/Resources/YargRemote
bash "$REPO_ROOT"/android/remote/make-cert.sh Assets/Resources/YargRemote/cert.bytes

log "Done. Open $PROJECT_DIR in Unity $(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)" \
    "and use YARG > Linux > Build (or run NuGetForUnity restore first if packages are missing)."
