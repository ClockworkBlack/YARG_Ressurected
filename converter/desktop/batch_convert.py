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
Writes <output>/Converted/<Artist - Title>/ for each song, and <output>/report.csv. With --save-to <folder>,
each song is also copied to <folder>/Converted/ the moment it is done and <folder>/report.csv is kept up to
date; songs already done there are skipped, so running again after an interruption carries on.
"""
import argparse, csv, json, os, shutil, subprocess, sys, threading, time
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
    ap.add_argument("--save-to", default="",
                    help="copy each song here (into Converted/) as soon as it is done, keep report.csv there up to date, "
                         "and skip songs already done there")
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
    # Saving as it goes: what an earlier run already saved is kept and skipped, so a run that was
    # cut off (a Colab session that disconnected) picks up where it stopped
    saved_songs = os.path.join(args.save_to, "Converted") if args.save_to else ""
    saved_report = os.path.join(args.save_to, "report.csv") if args.save_to else ""
    earlier = {}
    if saved_report and os.path.exists(saved_report):
        with open(saved_report, encoding="utf-8") as f:
            for row in csv.DictReader(f):
                if row.get("file"): earlier[row["file"]] = row
    skipped = [f for f in files if (earlier.get(f) or {}).get("folder") and not (earlier[f].get("error") or "").strip()
               and os.path.isdir(os.path.join(saved_songs, earlier[f]["folder"]))]
    if skipped:
        print("%d songs already done in %s (delete a song's folder there to convert it again)" % (len(skipped), saved_songs), flush=True)
    files = [f for f in files if f not in skipped]
    out_songs = os.path.join(args.out, "Converted")
    work = os.path.join(args.out, ".work")
    os.makedirs(out_songs, exist_ok=True); os.makedirs(work, exist_ok=True)

    jobs = auto_jobs() if args.jobs == "auto" else max(1, int(args.jobs))
    heap = heap_for(jobs)
    print("%d songs, %d at a time" % (len(files), jobs), flush=True)
    whisper_lock = threading.Lock()
    print_lock = threading.Lock()
    save_lock = threading.Lock()
    done_rows = dict(earlier)
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
            say("%s done in %d s: %s, %.0f BPM, guitar from %s, %d vocal lines (%s)%s" % (
                tag, took, s["folder"], s.get("bpm", 0), s.get("guitarFrom", "?"), s["vocalLines"], s["lyrics"],
                ", Deep Music Mode guitar %d / bass %d notes" % (s["deepGuitarExpert"], s["deepBassExpert"]) if s.get("deepGuitarExpert") or s.get("deepBassExpert") else ""))
        else:
            tail = open(base + ".log", encoding="utf-8", errors="replace").read()[-400:]
            s = dict(file=name, mode=mode, notes=notes, folder="", error=tail.strip().splitlines()[-1] if tail.strip() else "failed")
            say("%s FAILED %s: %s" % (tag, name, s["error"]))
        if os.path.exists(base + ".vocals.wav"): os.remove(base + ".vocals.wav")
        if saved_songs:
            with save_lock:
                try:
                    if s.get("folder"):
                        target = os.path.join(saved_songs, s["folder"])
                        if os.path.isdir(target): shutil.rmtree(target)
                        shutil.copytree(os.path.join(out_songs, s["folder"]), target)
                    done_rows[name] = s
                    write_report(saved_report, list(done_rows.values()))
                    if s.get("folder"): say("%s saved to %s" % (tag, target))
                except OSError as e:
                    say("%s could not save to %s: %s" % (tag, saved_songs, e))
        return s

    started_all = time.time()
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        report = list(pool.map(lambda a: convert(*a), [(n, name) for n, name in enumerate(files, 1)]))
    print("All done in %d min %02d s" % divmod(int(time.time() - started_all), 60), flush=True)

    write_report(os.path.join(args.out, "report.csv"), report)
    ok = sum(1 for r in report if r.get("folder"))
    print("Converted %d of %d songs into %s" % (ok, len(report), out_songs))
    if saved_songs:
        print("All %d songs done so far are in %s" % (sum(1 for r in done_rows.values() if r.get("folder") and not (r.get("error") or "").strip()),
                                                     saved_songs))


REPORT_COLUMNS = ["file", "folder", "title", "artist", "mode", "notes", "bpm", "tempo", "guitarFrom", "lyrics", "vocalLines",
                  "guitarExpert", "bassExpert", "deepGuitarExpert", "deepBassExpert", "seconds", "timing", "error"]


def write_report(path, rows):
    # Written whole to a new file and then moved into place, so a cut-off run never leaves half a report
    with open(path + ".tmp", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=REPORT_COLUMNS, extrasaction="ignore"); w.writeheader(); w.writerows(rows)
    os.replace(path + ".tmp", path)


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
