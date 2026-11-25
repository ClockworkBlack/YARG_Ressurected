#!/usr/bin/env bash
# YARG Resurrected - put together the Unity project for the macOS build.
#
# Same game as the Android and Windows builds (same upstream commit, same patches and overlay),
# plus the Mac pieces:
#   - YARG-DROID kept the Mac BASS, STB2CSharp and sqlite libraries; only the Discord Game SDK
#     bundle is missing, so it is taken from the official YARG v0.15.0 release source,
#   - the Mac build entry point (mac/Editor/YargMacBuild.cs).
# The Android-only editor script is left out: it needs Unity's Android module, which a
# Mac-only editor does not have.
#
# Usage:  bash mac/prepare-project.sh [project-dir]     (default: ./project)
# Works on macOS and Linux. Needs git and openssl.
# Set GIT_LFS_SKIP_SMUDGE=1 to skip the art assets (only useful for checking that patches apply).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_DIR="${1:-$REPO_ROOT/project}"
UPSTREAM_REPO="${UPSTREAM_REPO:-putudarma/YARG-DROID}"
UPSTREAM_REF="${UPSTREAM_REF:-9bebad16e63cde7022a1fb7b261b541c1fae5361}"
# Official YARG release that YARG-DROID is based on; only its Discord library is used.
NATIVES_REPO="${NATIVES_REPO:-YARC-Official/YARG}"
NATIVES_REF="${NATIVES_REF:-refs/tags/v0.15.0}"

log() { echo "[mac] $*"; }

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

log "Copying the overlay (game features) and the Mac build script"
cp -r "$REPO_ROOT"/overlay/. .
rm -rf Assets/Editor/Android Assets/Editor/Android.meta
mkdir -p Assets/Editor/Mac
cp "$REPO_ROOT"/mac/Editor/*.cs Assets/Editor/Mac/

log "Restoring the Mac Discord library from $NATIVES_REPO $NATIVES_REF"
NATIVES_TMP="$(mktemp -d)"
trap 'rm -rf "$NATIVES_TMP"' EXIT
git init -q "$NATIVES_TMP"
git -C "$NATIVES_TMP" remote add origin "https://github.com/$NATIVES_REPO"
git -C "$NATIVES_TMP" fetch -q --depth 1 --filter=blob:none origin "$NATIVES_REF"
NATIVE_FILES=(
    Assets/Plugins/DiscordGameSDK/Plugins/x86_64/discord_game_sdk.bundle
)
for f in "${NATIVE_FILES[@]}"; do
    mkdir -p "$(dirname "$f")"
    git -C "$NATIVES_TMP" show "FETCH_HEAD:$f" > "$f"
    git -C "$NATIVES_TMP" show "FETCH_HEAD:$f.meta" > "$f.meta"
    log "  $f ($(wc -c < "$f") bytes)"
done
for f in Assets/Plugins/BassNative/Mac/libbass.dylib Assets/Plugins/STB2CSharp/MacOS/STB2CSharp.dylib \
         Assets/Plugins/Sqlite3/Mac/libsqlite3.dylib; do
    [ -s "$f" ] || { echo "[mac] $f is missing from the base project" >&2; exit 1; }
done

# HIDrogen (extra controller support) ships its Mac libusb as an arm64-only file, although its
# settings mark it as x86_64. In this Intel app it can't load ("incompatible architecture"), so
# USB devices that go through libusb wouldn't work. Embed the same HIDrogen version in Packages/
# (which overrides the registry copy) with an x86_64 libusb from the libusb-package wheel on PyPI
# (libusb 1.0.30, depends only on macOS system frameworks), checked against its published SHA-256.
LIBUSB_WHEEL_URL="https://files.pythonhosted.org/packages/fa/96/9a8ee916d1503b4137a5684faacf02c3452c8c3d66959ebceb85ea54ad0b/libusb_package-1.0.30.1-py3-none-macosx_10_9_x86_64.whl"
LIBUSB_WHEEL_SHA256="7ee86d9e1092f22239be28ae45e12cd47d65f4fa19f71e83f1f1036b97295cdc"
HIDROGEN_VERSION="$(sed -n 's/.*"com\.thenathannator\.hidrogen": *"\([^"]*\)".*/\1/p' Packages/manifest.json)"
if [ -n "$HIDROGEN_VERSION" ] && [ ! -d Packages/com.thenathannator.hidrogen ]; then
    log "Embedding HIDrogen $HIDROGEN_VERSION with an x86_64 libusb"
    git clone -q --depth 1 --branch "v$HIDROGEN_VERSION" https://github.com/TheNathannator/HIDrogen "$NATIVES_TMP/hidrogen"
    cp -r "$NATIVES_TMP/hidrogen/Packages/com.thenathannator.hidrogen" Packages/
    curl -fsSL -o "$NATIVES_TMP/libusb.whl" "$LIBUSB_WHEEL_URL"
    echo "$LIBUSB_WHEEL_SHA256  $NATIVES_TMP/libusb.whl" | (sha256sum -c - 2>/dev/null || shasum -a 256 -c -) >/dev/null \
        || { echo "[mac] libusb wheel checksum mismatch" >&2; exit 1; }
    python3 -I -c 'import sys, zipfile; z = zipfile.ZipFile(sys.argv[1]); open(sys.argv[2], "wb").write(z.read("libusb_package/libusb-1.0.dylib"))' \
        "$NATIVES_TMP/libusb.whl" Packages/com.thenathannator.hidrogen/HIDrogen/Plugins/Mac/libusb-1.0.dylib
    log "  HIDrogen/Plugins/Mac/libusb-1.0.dylib replaced ($(wc -c < Packages/com.thenathannator.hidrogen/HIDrogen/Plugins/Mac/libusb-1.0.dylib) bytes)"
fi

# Self-signed certificate for the phone remote's https page (phone mics need a secure page)
log "Making the phone remote certificate"
mkdir -p Assets/Resources/YargRemote
bash "$REPO_ROOT"/android/remote/make-cert.sh Assets/Resources/YargRemote/cert.bytes

log "Done. Open $PROJECT_DIR in Unity $(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)" \
    "and use YARG > Mac > Build (or run NuGetForUnity restore first if packages are missing)."
