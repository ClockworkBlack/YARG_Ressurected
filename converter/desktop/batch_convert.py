"""Converts a folder of songs into YARG song folders, the way the converter app does, and more:

  - details from the file's tags and name, the rest from MusicBrainz;
  - lyrics from LRCLIB / NetEase (timed) or Lyrics.ovh (words only, then timed from the voice);
  - no lyrics anywhere: written from the separated voice with Whisper (large-v3, song hints);
  - every song's lyrics timed word by word against the voice and checked against its repeats.

    python batch_convert.py <songs folder> <output folder> [--mode split|rnb|normal] [--notes more|standard|fewer]
                            [--gpu] [--jobs N|auto]

--jobs converts several songs at once (auto: as many as the machine's memory and processors allow,
at most 4). Speech-to-text runs for one song at a time, so the GPU never holds more than one copy.

An optional songs.csv in the songs folder gives, per file: file,mode,notes,title,artist,album,year,genre,start,end
(any column may be empty; mode and notes default to the command line's; start and end, in seconds,
cut a music video's intro or outro off). The file column is matched in any letter case, with or
without the extension.
Writes <output>/Converted/<Artist - Title>/ for each song, and <output>/report.csv.
"""
import argparse, csv, json, os, subprocess, sys, threading, time
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
# Java writes file names in the locale's encoding: under a plain "C" or "POSIX" locale only ASCII,
# so "Girls’ Generation" or a Korean title would not fit. UTF-8 for the converter, always.
JAVA_ENV = dict(os.environ)
if "UTF-8" not in (os.environ.get("LC_ALL") or os.environ.get("LC_CTYPE") or os.environ.get("LANG") or "").upper().replace("UTF8", "UTF-8"):
    JAVA_ENV["LC_ALL"] = "C.UTF-8"
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
    ap.add_argument("--jobs", default="auto")
    args = ap.parse_args()

    plan = {}
    csv_path = os.path.join(args.songs, "songs.csv")
    if os.path.exists(csv_path):
        with open(csv_path, encoding="utf-8-sig") as f:
            for row in csv.DictReader(f):
                # Matched by file name, in any letter case and with or without its extension
                name = (row.get("file") or "").strip().lower()
                if name: plan[name] = plan[os.path.splitext(name)[0]] = {k: (v or "").strip() for k, v in row.items() if k}

    files = sorted(f for f in os.listdir(args.songs) if f.lower().endswith(AUDIO))
    out_songs = os.path.join(args.out, "Converted")
    work = os.path.join(args.out, ".work")
    os.makedirs(out_songs, exist_ok=True); os.makedirs(work, exist_ok=True)

    jobs = auto_jobs() if args.jobs == "auto" else max(1, int(args.jobs))
    heap = heap_for(jobs)
    print("%d songs, %d at a time" % (len(files), jobs), flush=True)
    whisper_lock = threading.Lock()
    print_lock = threading.Lock()
    total = len(files)

    def say(text):
        with print_lock:
            print(text, flush=True)

    def convert(n, name):
        p = plan.get(name.lower()) or plan.get(os.path.splitext(name)[0].lower()) or {}
        mode = p.get("mode") or args.mode
        notes = p.get("notes") or args.notes
        tag = "[%d/%d]" % (n, total)
        base = os.path.join(work, "%03d" % n)
        for ext in (".vocals.wav", ".lrc", ".lrc.needed", ".json", ".log"):
            if os.path.exists(base + ext): os.remove(base + ext)
        cmd = ["java", "-Xmx%dm" % heap] + (["-Dyarg.cuda=1"] if args.gpu else []) + ["-cp", args.classpath, "ConvertSong",
               "--audio", os.path.join(args.songs, name), "--out", out_songs, "--mode", mode, "--notes", notes,
               "--model", args.stems_model, "--words-model", args.words_model, "--find-details", "--find-lyrics",
               "--two-singers", "--summary", base + ".json", "--threads", str(max(1, (os.cpu_count() or 2) // jobs))]
        for key in ("title", "artist", "album", "year", "genre", "start", "end"):
            if p.get(key): cmd += ["--" + key, p[key]]
        if mode != "normal":
            cmd += ["--vocals-out", base + ".vocals.wav", "--wait-for-lyrics", base + ".lrc"]
        say("%s %s (%s, %s notes)" % (tag, name, mode, notes))
        started = time.time()
        with open(base + ".log", "w") as log:
            proc = subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT, env=JAVA_ENV)
            wrote = False
            while proc.poll() is None:
                if not wrote and os.path.exists(base + ".lrc.needed"):
                    # No lyrics anywhere: write them from the voice
                    song = None
                    for line in open(base + ".log", encoding="utf-8", errors="replace"):
                        if line.startswith("Hint: "): song = line[6:].strip()
                    env = dict(os.environ, NOVAD="1", DEVICE="cuda" if args.gpu else "cpu")
                    if song: env["HINT"] = song
                    with whisper_lock:
                        say("%s no lyrics online for %s: writing them from the voice" % (tag, name))
                        r = subprocess.run([sys.executable, os.path.join(HERE, "transcribe_lyrics.py"), base + ".vocals.wav",
                                            base + ".lrc.tmp", args.whisper, "auto"], env=env, capture_output=True, text=True)
                    if r.returncode != 0 or not os.path.exists(base + ".lrc.tmp"):
                        say("%s speech to text failed: %s" % (tag, r.stderr[-300:]))
                        open(base + ".lrc.tmp", "w").close()
                    os.replace(base + ".lrc.tmp", base + ".lrc")
                    wrote = True
                time.sleep(1)
        took = time.time() - started
        if proc.returncode == 0 and os.path.exists(base + ".json"):
            s = json.load(open(base + ".json", encoding="utf-8"))
            s.update(file=name, mode=mode, notes=notes, seconds=round(took))
            say("%s done in %d s: %s, %d vocal lines (%s)" % (tag, took, s["folder"], s["vocalLines"], s["lyrics"]))
        else:
            tail = open(base + ".log", encoding="utf-8", errors="replace").read()[-400:]
            s = dict(file=name, mode=mode, notes=notes, folder="", error=tail.strip().splitlines()[-1] if tail.strip() else "failed")
            say("%s FAILED %s: %s" % (tag, name, s["error"]))
        if os.path.exists(base + ".vocals.wav"): os.remove(base + ".vocals.wav")
        return s

    started_all = time.time()
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        report = list(pool.map(lambda a: convert(*a), [(n, name) for n, name in enumerate(files, 1)]))
    print("All done in %d min %02d s" % divmod(int(time.time() - started_all), 60), flush=True)

    cols = ["file", "folder", "title", "artist", "mode", "notes", "lyrics", "vocalLines", "guitarExpert", "bassExpert", "seconds", "timing", "error"]
    with open(os.path.join(args.out, "report.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore"); w.writeheader(); w.writerows(report)
    ok = sum(1 for r in report if r.get("folder"))
    print("Converted %d of %d songs into %s" % (ok, len(report), out_songs))


def memory_mb():
    try:
        for line in open("/proc/meminfo"):
            if line.startswith("MemAvailable:"):
                return int(line.split()[1]) // 1024
    except OSError:
        pass
    return 8000


def auto_jobs():
    # Each song needs about 5 GB while it is split and charted, and about 3 processor cores
    by_memory = max(1, (memory_mb() - 4000) // 5000)
    by_cores = max(1, (os.cpu_count() or 2) // 3)
    return int(min(4, by_memory, by_cores))


def heap_for(jobs):
    # Java's memory per song, leaving room for the models and speech-to-text
    return int(max(3000, min(6000, (memory_mb() - 4000) // max(1, jobs))))


if __name__ == "__main__":
    main()
