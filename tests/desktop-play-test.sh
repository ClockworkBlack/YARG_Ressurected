#!/usr/bin/env bash
# YARG Resurrected - start a desktop build in play-test mode and check it played a song.
#
#   bash tests/desktop-play-test.sh <game> <out-dir>
#
# <game> is YARG.exe (Windows), the .app folder (Mac) or YARG.x86_64 (Linux). The game's own
# play test (overlay/Assets/Script/Testing/SmokeTest.cs) waits for the main menu, joins a phone
# over the network (the phone remote), scans tests/smoke-song, opens Song Setup, plays the song
# with a bot guitarist and a generated guitar track, and quits with 0 (pass) or 1 (fail).
# This script adds a watchdog, prints what happened, and fails on native library errors
# (a missing or wrong-architecture .dll / .so / .dylib) even if the game itself carried on.
# On Linux without a display it runs the game on a virtual screen (Xvfb), with the window in front.
set -uo pipefail

GAME="$1"
OUT="$(mkdir -p "$2" && cd "$2" && pwd)"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SONGS="$REPO_ROOT/tests/smoke-song"
WATCHDOG_SECONDS="${WATCHDOG_SECONDS:-600}"

case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) OS=windows ;;
    Darwin) OS=mac ;;
    *) OS=linux ;;
esac

if [ "$OS" = mac ]; then
    exe="$(find "$GAME/Contents/MacOS" -maxdepth 1 -type f | head -n1)"
else
    exe="$GAME"
fi
[ -f "$exe" ] || { echo "::error::game executable not found: $exe"; exit 1; }
chmod +x "$exe" 2>/dev/null || true

songs_arg="$SONGS"
out_arg="$OUT"
if [ "$OS" = windows ]; then
    # The game is a Windows program: give it Windows paths
    songs_arg="$(cygpath -w "$SONGS")"
    out_arg="$(cygpath -w "$OUT")"
fi

args=(-logFile "$out_arg/player.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720
      -yarg-smoke-test -yarg-smoke-songs "$songs_arg" -yarg-smoke-out "$out_arg")

# Linux without a display (CI): a virtual screen of our own
xvfb_pid=""
if [ "$OS" = linux ] && [ -z "${DISPLAY:-}" ] && command -v Xvfb >/dev/null; then
    for n in 99 98 97 96; do
        Xvfb ":$n" -screen 0 1280x720x24 -nolisten tcp >/dev/null 2>&1 &
        xvfb_pid=$!
        sleep 2
        if kill -0 "$xvfb_pid" 2>/dev/null; then
            export DISPLAY=":$n"
            break
        fi
        xvfb_pid=""
    done
    [ -n "$xvfb_pid" ] || { echo "::error::could not start a virtual screen (Xvfb)"; exit 1; }
    trap 'kill "$xvfb_pid" 2>/dev/null' EXIT
fi

echo "Starting $exe on $OS"
"$exe" "${args[@]}" &
pid=$!

# A virtual screen has no window manager, so nothing puts the game's window in front the way
# a desktop does, and the game takes no controller input in the background. Do what a
# desktop would: give the window the keyboard focus once it shows up.
# (Kept up for the first minutes: the game re-makes its window while it starts, which drops it.)
if [ -n "$xvfb_pid" ] && command -v xdotool >/dev/null; then
    (
        for _ in $(seq 1 180); do
            kill -0 "$pid" 2>/dev/null || break
            win="$(xdotool search --onlyvisible --name 'YARG' 2>/dev/null | head -n1)"
            [ -n "$win" ] && xdotool windowfocus "$win" 2>/dev/null
            sleep 1
        done
    ) &
fi

# Watchdog (macOS has no `timeout`)
waited=0
while kill -0 "$pid" 2>/dev/null; do
    if [ "$waited" -ge "$WATCHDOG_SECONDS" ]; then
        echo "::error::the game was still running after $WATCHDOG_SECONDS s; stopping it"
        kill "$pid" 2>/dev/null; sleep 5; kill -9 "$pid" 2>/dev/null
        pkill -f "yarg-smoke-test" 2>/dev/null || true
        break
    fi
    sleep 5
    waited=$((waited + 5))
done
wait "$pid" 2>/dev/null
code=$?
echo "The game exited with code $code after about $waited s"

log="$OUT/player.log"
echo
echo "===== Play test result ====="
if [ -f "$OUT/result.txt" ]; then cat "$OUT/result.txt"; else echo "(no result.txt: the game didn't get to the end of the test)"; fi

echo
echo "===== Game log (YARG lines and errors) ====="
if [ -f "$log" ]; then
    grep -E "YARG Smoke Test|BASS|Exception|Error|error|DllNotFound|Fallback|Screen:" "$log" | grep -v "^\s*$" | head -150
else
    echo "(no player.log)"
fi

for errors in "$HOME/YARG/logs/yarg-errors.txt" "$HOME/Documents/YARG/logs/yarg-errors.txt" \
              "${USERPROFILE:-/nonexistent}/Documents/YARG/logs/yarg-errors.txt"; do
    if [ -f "$errors" ]; then
        echo
        echo "===== $errors ====="
        cat "$errors"
        cp "$errors" "$OUT/" 2>/dev/null || true
    fi
done

fail=0
if ! grep -q "^PASS" "$OUT/result.txt" 2>/dev/null; then
    echo "::error::the play test did not pass"
    fail=1
fi
if [ -f "$log" ] && grep -E -q "DllNotFoundException|EntryPointNotFoundException|BadImageFormatException|Unable to load DLL|cannot open shared object" "$log"; then
    echo "::error::a native library failed to load:"
    grep -E "DllNotFoundException|EntryPointNotFoundException|BadImageFormatException|Unable to load DLL|cannot open shared object" "$log" | sort -u | head -20
    fail=1
fi
if [ -f "$log" ] && ! grep -q "BASS Successfully Initialized" "$log"; then
    echo "::error::the audio library (BASS) did not start"
    fail=1
fi

if [ "$fail" != 0 ] && [ -f "$log" ]; then
    echo
    echo "===== Last 120 lines of the game's log ====="
    tail -n 120 "$log"
fi

[ "$fail" = 0 ] && echo "Play test passed on $OS"
exit "$fail"
