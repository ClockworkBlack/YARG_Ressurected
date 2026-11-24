#!/usr/bin/env bash
# Builds every native library YARG needs for Android arm64-v8a.
#   usage: build-android-native.sh <path-to-patched-YARG-checkout> <output-dir>
# Needs: ANDROID_NDK_HOME (GitHub's ubuntu runners have it), cmake, curl, unzip.
set -euo pipefail

YARG_DIR="$(realpath "$1")"
OUT_DIR="$(realpath -m "$2")"
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
ABI=arm64-v8a
API=26

NDK="${ANDROID_NDK_HOME:-${ANDROID_NDK_LATEST_HOME:-}}"
if [ -z "$NDK" ] || [ ! -d "$NDK" ]; then
  echo "ANDROID_NDK_HOME is not set" >&2; exit 1
fi
TOOLCHAIN="$NDK/build/cmake/android.toolchain.cmake"
CMAKE_ANDROID=(-DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" -DANDROID_ABI=$ABI -DANDROID_PLATFORM=android-$API
               -DANDROID_STL=c++_static -DCMAKE_BUILD_TYPE=Release)
mkdir -p "$OUT_DIR"

fetch() { # url dest
  curl -fsSL --retry 3 -o "$2" "$1"
}

echo "::group::BASS (un4seen) Android libraries"
# BASS is free for non-commercial use: https://www.un4seen.com/bass.html#license
for pair in \
  "bass24-android.zip|https://www.un4seen.com/files/bass24-android.zip" \
  "bassmix24-android.zip|https://www.un4seen.com/files/bassmix24-android.zip" \
  "bass_fx24-android.zip|https://www.un4seen.com/files/z/0/bass_fx24-android.zip" \
  "bassopus24-android.zip|https://www.un4seen.com/files/bassopus24-android.zip"; do
  name="${pair%%|*}"; url="${pair#*|}"
  fetch "$url" "$WORK/$name"
  unzip -oq "$WORK/$name" -d "$WORK/${name%.zip}"
done
for lib in libbass.so libbassmix.so libbass_fx.so libbassopus.so; do
  found="$(find "$WORK" -path "*/$ABI/$lib" | head -n1)"
  if [ -z "$found" ]; then echo "Could not find $ABI/$lib in the BASS downloads" >&2; exit 1; fi
  cp "$found" "$OUT_DIR/"
done
echo "::endgroup::"

if [ -d "$YARG_DIR/Native/YargAudio" ]; then
  echo "::group::YargAudio"
  # Newer YARG (dev, 2026) has its own native audio library; v0.15.0 doesn't.
  sed -i 's/if(NOT WIN32 AND NOT APPLE AND NOT (UNIX/if(NOT WIN32 AND NOT APPLE AND NOT ANDROID AND NOT (UNIX/' "$YARG_DIR/Native/YargAudio/CMakeLists.txt"
  cmake -S "$YARG_DIR/Native/YargAudio" -B "$WORK/yarg_audio" "${CMAKE_ANDROID[@]}" -DYARG_AUDIO_BUILD_TESTS=OFF
  cmake --build "$WORK/yarg_audio" --parallel
  cp "$(find "$WORK/yarg_audio" -name libyarg_audio.so | head -n1)" "$OUT_DIR/"
  echo "::endgroup::"
else
  echo "No Native/YargAudio in this YARG version, skipping."
fi

echo "::group::STB2CSharp"
cp -r "$HERE/stb2csharp" "$WORK/stb2csharp"
fetch https://raw.githubusercontent.com/nothings/stb/master/stb_image.h "$WORK/stb2csharp/stb_image.h"
cmake -S "$WORK/stb2csharp" -B "$WORK/stb2csharp/build" "${CMAKE_ANDROID[@]}"
cmake --build "$WORK/stb2csharp/build" --parallel
cp "$WORK/stb2csharp/build/libSTB2CSharp.so" "$OUT_DIR/"
echo "::endgroup::"

echo "::group::SQLite"
SQLITE_ZIP=sqlite-amalgamation-3460100.zip
fetch "https://www.sqlite.org/2024/$SQLITE_ZIP" "$WORK/$SQLITE_ZIP"
unzip -oq "$WORK/$SQLITE_ZIP" -d "$WORK"
cp -r "$HERE/sqlite3" "$WORK/sqlite3src"
cp "$WORK"/sqlite-amalgamation-*/sqlite3.c "$WORK"/sqlite-amalgamation-*/sqlite3.h "$WORK/sqlite3src/"
cmake -S "$WORK/sqlite3src" -B "$WORK/sqlite3src/build" "${CMAKE_ANDROID[@]}"
cmake --build "$WORK/sqlite3src/build" --parallel
cp "$WORK/sqlite3src/build/libsqlite3.so" "$OUT_DIR/"
echo "::endgroup::"

STRIP="$(find "$NDK/toolchains/llvm/prebuilt" -name llvm-strip | head -n1)"
for so in "$OUT_DIR"/libyarg_audio.so "$OUT_DIR"/libSTB2CSharp.so "$OUT_DIR"/libsqlite3.so; do
  [ -f "$so" ] || continue
  "$STRIP" --strip-unneeded "$so"
done

echo "Built Android native libraries:"
ls -la "$OUT_DIR"
