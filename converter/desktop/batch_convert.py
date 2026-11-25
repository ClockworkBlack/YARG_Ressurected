"""Converts a folder of songs into YARG song folders, the way the converter app does, and more:

  - details from the file's tags and name, the rest from MusicBrainz;
  - lyrics from LRCLIB / NetEase (timed) or Lyrics.ovh (words only, then timed from the voice);
  - no lyrics anywhere: written from the separated voice with Whisper (large-v3, song hints);
  - every song's lyrics timed word by word against the voice and checked against its repeats.

    python batch_convert.py <songs folder> <output folder> [--mode split|rnb|normal] [--notes more|standard|fewer]
                            [--gpu] [--jobs N]

An optional songs.csv in the songs folder gives, per file: file,mode,notes,title,artist,album,year,genre
(any column may be empty; mode and notes default to the command line's).
Writes <output>/Converted/<Artist - Title>/ for each song, and <output>/report.csv.
"""
import argparse, csv, json, os, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
AUDIO = (".mp3", ".wav", ".ogg", ".opus", ".flac", ".m4a", ".aac", ".wma", ".webm")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("songs"); ap.add_argument("out")
    ap.add_argument("--mode", default="split"); ap.add_argument("--notes", default="more")
    ap.add_argument("--gpu", action="store_true")
    ap.add_argument("--classpath", default=os.environ.get("YARG_CP", ""))
    ap.add_argument("--stems-model", default=os.environ.get("YARG_STEMS", "htdemucs_6s.onnx"))
    ap.add_argument("--words-model", default=os.environ.get("YARG_WORDS", "wav2vec2-base-960h.onnx"))
    ap.add_argument("--whisper", default="large-v3")
    args = ap.parse_args()

    plan = {}
    csv_path = os.path.join(args.songs, "songs.csv")
    if os.path.exists(csv_path):
        with open(csv_path, encoding="utf-8-sig") as f:
            for row in csv.DictReader(f):
                plan[row.get("file", "").strip()] = {k: (v or "").strip() for k, v in row.items()}

    files = sorted(f for f in os.listdir(args.songs) if f.lower().endswith(AUDIO))
    out_songs = os.path.join(args.out, "Converted")
    work = os.path.join(args.out, ".work")
    os.makedirs(out_songs, exist_ok=True); os.makedirs(work, exist_ok=True)
    report = []
    whisper = None
    for n, name in enumerate(files, 1):
        p = plan.get(name, {})
        mode = p.get("mode") or args.mode
        notes = p.get("notes") or args.notes
        base = os.path.join(work, "%03d" % n)
        for ext in (".vocals.wav", ".lrc", ".lrc.needed", ".json", ".log"):
            if os.path.exists(base + ext): os.remove(base + ext)
        cmd = ["java", "-Xmx6g"] + (["-Dyarg.cuda=1"] if args.gpu else []) + ["-cp", args.classpath, "ConvertSong",
               "--audio", os.path.join(args.songs, name), "--out", out_songs, "--mode", mode, "--notes", notes,
               "--model", args.stems_model, "--words-model", args.words_model, "--find-details", "--find-lyrics",
               "--two-singers", "--summary", base + ".json"]
        for key in ("title", "artist", "album", "year", "genre"):
            if p.get(key): cmd += ["--" + key, p[key]]
        if mode != "normal":
            cmd += ["--vocals-out", base + ".vocals.wav", "--wait-for-lyrics", base + ".lrc"]
        print("[%d/%d] %s (%s, %s notes)" % (n, len(files), name, mode, notes), flush=True)
        started = time.time()
        with open(base + ".log", "w") as log:
            proc = subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT)
            wrote = False
            while proc.poll() is None:
                if not wrote and os.path.exists(base + ".lrc.needed"):
                    # No lyrics anywhere: write them from the voice
                    song = "a song"
                    for line in open(base + ".log", encoding="utf-8", errors="replace"):
                        if line.startswith("Song: "): song = line[6:].strip()
                    print("    no lyrics online: writing them from the voice", flush=True)
                    env = dict(os.environ, NOVAD="1", HINT=song, DEVICE="cuda" if args.gpu else "cpu")
                    r = subprocess.run([sys.executable, os.path.join(HERE, "transcribe_lyrics.py"), base + ".vocals.wav",
                                        base + ".lrc.tmp", args.whisper, "auto"], env=env, capture_output=True, text=True)
                    if r.returncode != 0 or not os.path.exists(base + ".lrc.tmp"):
                        print("    speech to text failed: " + r.stderr[-300:], flush=True)
                        open(base + ".lrc.tmp", "w").close()
                    os.replace(base + ".lrc.tmp", base + ".lrc")
                    wrote = True
                time.sleep(1)
        took = time.time() - started
        if proc.returncode == 0 and os.path.exists(base + ".json"):
            s = json.load(open(base + ".json", encoding="utf-8"))
            s.update(file=name, mode=mode, notes=notes, seconds=round(took))
            print("    done in %d s: %s, %d vocal lines (%s)" % (took, s["folder"], s["vocalLines"], s["lyrics"]), flush=True)
        else:
            tail = open(base + ".log", encoding="utf-8", errors="replace").read()[-400:]
            s = dict(file=name, mode=mode, notes=notes, folder="", error=tail.strip().splitlines()[-1] if tail.strip() else "failed")
            print("    FAILED: " + s["error"], flush=True)
        report.append(s)
        if os.path.exists(base + ".vocals.wav"): os.remove(base + ".vocals.wav")

    cols = ["file", "folder", "title", "artist", "mode", "notes", "lyrics", "vocalLines", "guitarExpert", "bassExpert", "seconds", "timing", "error"]
    with open(os.path.join(args.out, "report.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore"); w.writeheader(); w.writerows(report)
    ok = sum(1 for r in report if r.get("folder"))
    print("Converted %d of %d songs into %s" % (ok, len(report), out_songs))


if __name__ == "__main__":
    main()
