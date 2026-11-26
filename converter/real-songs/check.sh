#!/usr/bin/env bash
# Converts every audio file in a folder the way the app does and checks each against
# expected.tsv (matched by the file's SHA-256, so file names do not matter).
#
#   converter/real-songs/check.sh <folder with the audio files> <htdemucs_6s.onnx> <onnxruntime.jar> [folder for the song folders]
#
# Needs ffmpeg and a JDK. The recordings are not in the repository: put your own copies in a
# folder and point this at it. The separated instruments are cached in <folder>/.cache, so only
# the first run of a song takes long (about half the song's length).
set -uo pipefail
audio="$1"; model="$2"; ort="$3"; out="${4:-}"
here="$(cd "$(dirname "$0")" && pwd)"; conv="$(dirname "$here")"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
javac -nowarn -cp "$ort" -d "$work/classes" \
  $(find "$conv/app/src/main/java" -path '*core*' -name '*.java') \
  "$conv/app/src/main/java/com/clockworkblack/yargconverter/stems/OrtStemModel.java" \
  "$conv"/core-test/*.java "$conv"/model-test/*.java || exit 2
failed=0; checked=0; seen=" "
for f in "$audio"/*; do
  case "${f,,}" in *.mp3|*.wav|*.flac|*.ogg|*.opus|*.m4a|*.aac) ;; *) continue ;; esac
  sha="$(sha256sum "$f" | cut -d' ' -f1)"
  case "$seen" in *" $sha "*) continue ;; esac   # the same recording twice under two names
  seen="$seen$sha "
  name="$(grep -P "^$sha\t" "$here/expected.tsv" | cut -f9,10 | sed 's/\t/ - /' || true)"
  echo "== ${name:-$(basename "$f")}"
  ffmpeg -loglevel error -y -i "$f" -map 0:a:0 -f f32le -ac 2 -ar 44100 "$work/song.f32" || { echo "  could not decode"; failed=$((failed+1)); continue; }
  folder=(); [ -n "$out" ] && folder=("$out/${name:-$sha}")
  java -Xmx6g -cp "$work/classes:$ort" RealSongRegression "$model" "$here/expected.tsv" "$sha" "$work/song.f32" "$audio/.cache" "${folder[@]}" 2>&1 | grep -v '^Picked up'
  [ "${PIPESTATUS[0]}" -ne 0 ] && failed=$((failed+1))
  checked=$((checked+1))
done
echo
if [ "$failed" -eq 0 ]; then echo "All $checked real songs passed"; else echo "FAILED: $failed of $checked real songs"; exit 1; fi
