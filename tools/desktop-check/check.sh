#!/usr/bin/env bash
# YARG Resurrected - a fast check of the game's code on a Linux machine, without Unity.
#
#   bash tools/desktop-check/check.sh            compile for Linux, Windows, Mac and Android
#   bash tools/desktop-check/check.sh --run      ...then run the Linux build's play test
#
# What it does:
#   1. gets the game's source the way the builds do (YARG-DROID at the pinned commit, this
#      repo's patches and overlay; no art assets),
#   2. downloads the newest published Linux build (for the libraries the game is compiled
#      against: Unity's, the plug-ins', and the game's own data files),
#   3. compiles the game's own code (YARG.Core and everything under Assets, 900 files) with
#      the .NET SDK's C# compiler, once for each system's set of #if symbols. A compile error
#      here is a compile error in the real build, found in a minute instead of half an hour;
#   4. with --run: puts the freshly compiled Linux code into a copy of that Linux build and
#      runs tests/desktop-play-test.sh on a virtual screen with a dummy sound card.
#
# What it does not do: it is not the Unity build. Scenes, prefabs and bundled resources come
# from the downloaded build, so changes to those are not seen here, and Windows and Mac are
# only compiled, not run. The GitHub workflows (Build Windows / Mac / Linux) stay the real test.
#
# Needs: git, curl, python3, the .NET 8 SDK. For --run also:
#   sudo apt-get install -y xvfb xdotool libgl1-mesa-dri pulseaudio libasound2-plugins libhidapi-hidraw0
# Work files go in tools/desktop-check/work (not committed). Delete it to start afresh.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="${DESKTOP_CHECK_WORK:-$REPO_ROOT/tools/desktop-check/work}"
GITHUB_REPO="${GITHUB_REPO:-ClockworkBlack/YARG_Resurrected}"
UPSTREAM_REPO="${UPSTREAM_REPO:-putudarma/YARG-DROID}"
UPSTREAM_REF="${UPSTREAM_REF:-9bebad16e63cde7022a1fb7b261b541c1fae5361}"
RUN=0
[ "${1:-}" = "--run" ] && RUN=1

log() { echo "[desktop-check] $*"; }
die() { echo "[desktop-check] $*" >&2; exit 1; }

CSC_DLL="$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll /usr/share/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | tail -n1)"
[ -n "$CSC_DLL" ] || die "the .NET SDK's C# compiler (csc.dll) was not found; install the .NET 8 SDK"
mkdir -p "$WORK"

# ---------------------------------------------------------------- 1. the game's source
if [ ! -d "$WORK/upstream/.git" ]; then
    log "Getting $UPSTREAM_REPO at $UPSTREAM_REF (no art assets)"
    git init -q "$WORK/upstream"
    git -C "$WORK/upstream" remote add origin "https://github.com/$UPSTREAM_REPO"
    GIT_LFS_SKIP_SMUDGE=1 git -C "$WORK/upstream" fetch -q --depth 1 origin "$UPSTREAM_REF" || die "could not fetch the game's source"
    GIT_LFS_SKIP_SMUDGE=1 git -C "$WORK/upstream" checkout -q FETCH_HEAD 2>/dev/null
    GIT_LFS_SKIP_SMUDGE=1 git -C "$WORK/upstream" submodule update -q --init --recursive --depth 1 2>/dev/null
fi

log "Applying this repo's patches and overlay to a fresh copy"
rm -rf "$WORK/project"
cp -r "$WORK/upstream" "$WORK/project"
for p in "$REPO_ROOT"/patches/*.patch; do
    git -C "$WORK/project" apply --whitespace=nowarn "$p" || die "patch does not apply: $(basename "$p")"
done
for p in "$REPO_ROOT"/patches-core/*.patch; do
    git -C "$WORK/project/YARG.Core" apply --whitespace=nowarn "$p" || die "core patch does not apply: $(basename "$p")"
done
cp -r "$REPO_ROOT"/overlay/. "$WORK/project/"

# ---------------------------------------------------------------- 2. a published Linux build
if [ ! -x "$WORK/linux-build/YARG.x86_64" ]; then
    url="${LINUX_BUILD_URL:-}"
    if [ -z "$url" ]; then
        url="$(curl -fsSL "https://api.github.com/repos/$GITHUB_REPO/releases?per_page=40" | python3 -I -c '
import json, sys
for release in json.load(sys.stdin):
    if release["tag_name"].startswith("linux-build-"):
        for asset in release["assets"]:
            if asset["name"].endswith(".tar.gz"):
                print(asset["browser_download_url"]); sys.exit(0)
')"
    fi
    [ -n "$url" ] || die "no published Linux build found; set LINUX_BUILD_URL to a YARG-Resurrected-Linux-<n>.tar.gz"
    log "Downloading $url"
    rm -rf "$WORK/linux-build.tmp"; mkdir -p "$WORK/linux-build.tmp"
    curl -fsSL -o "$WORK/linux-build.tmp/game.tar.gz" "$url" || die "download failed"
    tar xzf "$WORK/linux-build.tmp/game.tar.gz" -C "$WORK/linux-build.tmp" || die "could not unpack the Linux build"
    rm -rf "$WORK/linux-build"
    mv "$WORK/linux-build.tmp/YARG-Resurrected" "$WORK/linux-build"
    rm -rf "$WORK/linux-build.tmp"
    chmod +x "$WORK/linux-build/YARG.x86_64"
fi
MANAGED="$WORK/linux-build/YARG_Data/Managed"
[ -f "$MANAGED/UnityEngine.CoreModule.dll" ] || die "the Linux build in $WORK/linux-build has no libraries"

# ---------------------------------------------------------------- 3. compile
# Unity's own symbols for a player build, apart from the one that names the system
common="ENABLE_MONO;NET_4_6;NET_UNITY_4_8;CSHARP_7_3_OR_NEWER;ZSTRING_TEXTMESHPRO_SUPPORT;UNITY_64;UNITY_6000;UNITY_6000_3"
common="$common;UNITY_5_3_OR_NEWER;UNITY_5_4_OR_NEWER;UNITY_5_5_OR_NEWER;UNITY_5_6_OR_NEWER"
for y in 2017 2018 2019 2020 2021 2022 2023; do for m in 1 2 3 4; do common="$common;UNITY_${y}_${m}_OR_NEWER"; done; done
for m in 0 1 2 3; do common="$common;UNITY_6000_${m}_OR_NEWER"; done

find "$WORK/project/YARG.Core/YARG.Core" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | sed 's/.*/"&"/' > "$WORK/core-files.txt"
# The game's assembly: everything under Assets except editor code and Assets/Plugins
find "$WORK/project/Assets" -name '*.cs' -not -path '*/Editor/*' -not -path "$WORK/project/Assets/Plugins/*" | sed 's/.*/"&"/' > "$WORK/game-files.txt"

compile() {   # compile <name> <system symbols>   -> $WORK/out-<name>/*.dll
    local name="$1" symbols="$2" out="$WORK/out-$1" refs=() dll
    rm -rf "$out"; mkdir -p "$out"

    # YARG.Core: no engine references, nullable on (as its csc.rsp says)
    for dll in "$MANAGED"/*.dll; do
        case "$(basename "$dll")" in
            YARG.Core.Package.dll|Assembly-CSharp.dll|Assembly-CSharp-firstpass.dll|UnityEngine*|Unity.*) ;;
            *) refs+=("-r:$dll") ;;
        esac
    done
    dotnet "$CSC_DLL" -noconfig -nostdlib -nologo -langversion:9.0 -unsafe -nullable:enable -target:library \
        -optimize+ -debug- -warn:0 -nowarn:CS0436,CS1701,CS1702,CS8632 "-define:$symbols;$common" "${refs[@]}" \
        "-out:$out/YARG.Core.Package.dll" "@$WORK/core-files.txt" > "$out/core.log" 2>&1 || {
        grep "error CS" "$out/core.log" | sed "s#$WORK/project/##" | sort -u | head -40
        return 1
    }

    refs=("-r:$out/YARG.Core.Package.dll")
    for dll in "$MANAGED"/*.dll; do
        case "$(basename "$dll")" in
            YARG.Core.Package.dll|Assembly-CSharp.dll) ;;
            *) refs+=("-r:$dll") ;;
        esac
    done
    dotnet "$CSC_DLL" -noconfig -nostdlib -nologo -langversion:9.0 -unsafe -target:library \
        -optimize+ -debug- -warn:0 -nowarn:CS0436,CS1701,CS1702 "-define:$symbols;$common" "${refs[@]}" \
        "-out:$out/Assembly-CSharp.dll" "@$WORK/game-files.txt" > "$out/game.log" 2>&1 || {
        grep "error CS" "$out/game.log" | sed "s#$WORK/project/##" | sort -u | head -60
        return 1
    }
}

failed=0
for target in "linux:UNITY_STANDALONE;UNITY_STANDALONE_LINUX" "windows:UNITY_STANDALONE;UNITY_STANDALONE_WIN" \
              "mac:UNITY_STANDALONE;UNITY_STANDALONE_OSX" "android:UNITY_ANDROID"; do
    name="${target%%:*}"
    if compile "$name" "${target#*:}"; then
        log "compiles for $name ($(wc -l < "$WORK/game-files.txt") game files, $(wc -l < "$WORK/core-files.txt") core files)"
    else
        log "DOES NOT COMPILE for $name (errors above)"
        failed=1
    fi
done
[ "$failed" = 0 ] || exit 1
[ "$RUN" = 1 ] || { log "Done. (Add --run to play-test the Linux build with this code.)"; exit 0; }

# ---------------------------------------------------------------- 4. run the Linux build with this code
for tool in Xvfb xdotool pulseaudio pactl; do
    command -v "$tool" >/dev/null || die "--run needs $tool (see the apt-get line at the top of this file)"
done

log "Putting the compiled code into a copy of the Linux build"
GAME="$WORK/game"
rm -rf "$GAME"; cp -r "$WORK/linux-build" "$GAME"
DATA="$GAME/YARG_Data"
cp "$WORK/out-linux/Assembly-CSharp.dll" "$WORK/out-linux/YARG.Core.Package.dll" "$DATA/Managed/"
# Data files that are plain files in this checkout (the art is git-lfs, which was skipped)
( cd "$WORK/project/Assets/StreamingAssets" && find . -type f -not -name '*.meta' -print0 | while IFS= read -r -d '' f; do
      if head -c 40 "$f" | grep -q "^version https://git-lfs"; then continue; fi
      mkdir -p "$DATA/StreamingAssets/$(dirname "$f")"; cp "$f" "$DATA/StreamingAssets/$f"
  done )
# Unity lists the game's start-up methods when it builds; bring that list in line with the source
python3 -I - "$DATA/RuntimeInitializeOnLoads.json" "$WORK/project/Assets" "$DATA/Managed/Assembly-CSharp.dll" <<'PY'
import json, os, re, sys
path, assets = sys.argv[1], sys.argv[2]
compiled = open(sys.argv[3], "rb").read()
data = json.load(open(path))
# (made by Unity's build for the job system; the locally compiled code does not have it)
data["root"] = [e for e in data["root"]
                if not (e["assemblyName"] == "Assembly-CSharp" and e["className"].startswith("__JobReflectionRegistrationOutput__"))]
known = {(e.get("nameSpace") or "", e["className"], e["methodName"]) for e in data["root"] if e["assemblyName"] == "Assembly-CSharp"}
load_types = {"AfterSceneLoad": 0, "BeforeSceneLoad": 1, "AfterAssembliesLoaded": 2, "BeforeSplashScreen": 3, "SubsystemRegistration": 4}
attribute = re.compile(r"\[RuntimeInitializeOnLoadMethod(?:\(RuntimeInitializeLoadType\.(\w+)\))?\]\s*(?:#if[^\n]*\n\s*)?(?:private|public|internal)?\s*static\s+\w+\s+(\w+)\s*\(")
for folder, _, files in os.walk(assets):
    if "/Editor" in folder or "/Plugins" in folder:
        continue
    for name in files:
        if not name.endswith(".cs"):
            continue
        text = open(os.path.join(folder, name), encoding="utf-8", errors="replace").read()
        if "RuntimeInitializeOnLoadMethod" not in text:
            continue
        namespace = re.search(r"^\s*namespace\s+([\w.]+)", text, re.M)
        for match in attribute.finditer(text):
            before = text[:match.start()]
            classes = re.findall(r"\b(?:class|struct)\s+(\w+)", before)
            if not classes:
                continue
            key = (namespace.group(1) if namespace else "", classes[-1], match.group(2))
            # (only what is in the Linux code: phone-only classes are not compiled into it)
            if key[1].encode() not in compiled or key[2].encode() not in compiled:
                continue
            if key not in known:
                known.add(key)
                data["root"].append({"assemblyName": "Assembly-CSharp", "nameSpace": key[0], "className": key[1],
                                     "methodName": key[2], "loadTypes": load_types.get(match.group(1) or "AfterSceneLoad", 0),
                                     "isUnityClass": False})
                print("[desktop-check] start-up method added to the list:", ".".join(key))
json.dump(data, open(path, "w"))
PY

# A dummy sound card: the song only moves on while its audio plays, and the play test's
# microphone listens to it
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/tmp/xdg-runtime-$(id -u)}"
mkdir -p "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
if ! pulseaudio --check 2>/dev/null; then
    pulseaudio -D --exit-idle-time=-1 2>/dev/null || true
    sleep 1
    pactl load-module module-null-sink sink_name=yargcheck >/dev/null 2>&1 || true
    pactl set-default-sink yargcheck >/dev/null 2>&1 || true
    pactl set-default-source yargcheck.monitor >/dev/null 2>&1 || true
fi
if [ ! -f "$HOME/.asoundrc" ]; then
    printf 'pcm.!default { type pulse }\nctl.!default { type pulse }\n' > "$HOME/.asoundrc"
    log "Wrote ~/.asoundrc so the game's sound goes to PulseAudio"
fi

OUT="$WORK/play-test"
rm -rf "$OUT"
log "Running the play test (a few minutes on software graphics)"
( unset DISPLAY; LIBGL_ALWAYS_SOFTWARE=1 bash "$REPO_ROOT/tests/desktop-play-test.sh" "$GAME/YARG.x86_64" "$OUT" ) > "$WORK/play-test.log" 2>&1
code=$?
if [ -f "$OUT/result.txt" ]; then cat "$OUT/result.txt"; else tail -n 40 "$WORK/play-test.log"; fi
log "Screenshots and the game's log: $OUT   (full output: $WORK/play-test.log)"
exit "$code"
