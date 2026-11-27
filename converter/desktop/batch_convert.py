"""Converts a folder of songs into YARG song folders, the way the converter app does, and more:

  - details from the file's tags and name, the rest from MusicBrainz;
  - lyrics from LRCLIB / NetEase (timed) or Lyrics.ovh (words only, then timed from the voice);
  - no lyrics anywhere: written from the separated voice with Whisper (large-v3, song hints);
  - every song's lyrics timed word by word against the voice and checked against its repeats.

    python batch_convert.py <songs folder> <output folder> [--mode split|rnb|normal] [--notes more|standard|fewer]
                            [--gpu] [--jobs N|auto] [--keep FOLDER] [--redo-all]

--keep FOLDER keeps every finished song there as soon as it is done, with a record of what is
done (state.json); run again with the same --keep and only the songs not finished last time are
converted: new files, ones that failed or found no lyrics, and ones whose file or songs.csv row
changed. --redo "charlene, minho" also redoes the songs whose file names contain those words;
--redo-all converts everything again.

--jobs converts several songs at once (auto: as many as the machine's memory and processors allow,
at most 4). Speech-to-text runs for one song at a time, so the GPU never holds more than one copy.

An optional songs.csv in the songs folder gives, per file: file,mode,notes,title,artist,album,year,genre,start,end
(any column may be empty; mode and notes default to the command line's; start and end, in seconds,
cut a music video's intro or outro off). The file column is matched in any letter case, with or
without the extension.
Writes <output>/Converted/<Artist - Title>/ for each song, and <output>/report.csv (--save-to is another
name for --keep).
"""
import argparse, csv, json, os, re, shutil, subprocess, sys, threading, time
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
    ap.add_argument("--keep", "--save-to", dest="keep", help="a folder (on Google Drive, say) that keeps every finished song and the record of "
                    "what is done, so a later run picks up where this one stopped")
    ap.add_argument("--redo-all", action="store_true", help="convert every song again, finished or not")
    ap.add_argument("--redo", default="", help="songs to convert again even if finished: parts of their file names, "
                    "separated by commas (any letter case; spaces and underscores alike)")
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
    keep_songs = os.path.join(args.keep, "Converted") if args.keep else None
    if keep_songs: os.makedirs(keep_songs, exist_ok=True)
    state_path = os.path.join(args.keep or args.out, "state.json")
    state = {}
    if os.path.exists(state_path):
        try:
            state = json.load(open(state_path, encoding="utf-8"))
        except ValueError:
            state = {}
    songs_dir = keep_songs or out_songs

    def settings(name):
        p = plan.get(name.lower()) or plan.get(os.path.splitext(name)[0].lower()) or {}
        mode = p.get("mode") or args.mode
        notes = p.get("notes") or args.notes
        given = {k: p.get(k, "") for k in ("title", "artist", "album", "year", "genre", "start", "end")}
        return p, mode, notes, dict(given, mode=mode, notes=notes, size=os.path.getsize(os.path.join(args.songs, name)))

    def finished(name):
        """Why a song needs converting again, or None when its last conversion is complete."""
        before = state.get(name.lower())
        if args.redo_all: return "all songs redone"
        squash = lambda t: re.sub(r"[\s_]+", " ", t).strip().lower()
        if any(w and w in squash(name) for w in (squash(x) for x in args.redo.split(","))): return "asked to redo"
        if not before: return "new"
        if before.get("settings") != settings(name)[3]: return "file or settings changed"
        row = before.get("row", {})
        if row.get("error") or not row.get("folder"): return "failed last time"
        if not os.path.isdir(os.path.join(songs_dir, row["folder"])): return "folder missing"
        if before.get("settings", {}).get("mode") != "normal" and row.get("lyrics") == "none" and not before.get("noWords"):
            return "no lyrics last time"
        return None

    report_path = os.path.join(args.keep or args.out, "report.csv")
    if not state and not args.redo_all and os.path.exists(report_path):
        # A run from before there was a record: its report says what was finished
        with open(report_path, encoding="utf-8-sig") as f:
            rows = {(r.get("file") or "").lower(): r for r in csv.DictReader(f)}
        for name in files:
            r = rows.get(name.lower())
            if r and r.get("folder") and os.path.isdir(os.path.join(songs_dir, r["folder"])):
                state[name.lower()] = dict(settings=settings(name)[3], row={k: v for k, v in r.items() if k})
        if state: print("Picked up %d songs from an earlier run's report" % len(state), flush=True)

    todo, report = [], {}
    for name in files:
        why = finished(name)
        if why is None:
            report[name] = state[name.lower()]["row"]
        else:
            todo.append((name, why))
    skipped = len(files) - len(todo)
    if skipped:
        print("%d songs already done (kept as they are), %d to convert" % (skipped, len(todo)), flush=True)

    # Artists already known (other files' names, songs.csv, earlier results): a title that names
    # one without a dash ("The Rio Da Yung OG On The Radar Freestyle") is read by them
    known = set()
    for f in files:
        stem = re.sub(r"[ _]+[\u2013\u2014-][ _]+", " - ", os.path.splitext(f)[0]).replace("_", " ")
        if " - " in stem: known.add(stem.split(" - ")[0].strip())
    for p in plan.values():
        if p.get("artist"): known.add(p["artist"])
    for st in state.values():
        a = (st.get("row") or {}).get("artist", "")
        if a and a.lower() != "unknown artist": known.add(a)
    known_path = os.path.join(work, "known_artists.txt")
    with open(known_path, "w", encoding="utf-8") as f:
        f.write("\n".join(sorted(a for a in known if len(a) >= 3)))
    logs_dir = os.path.join(args.keep or args.out, "logs")

    jobs = auto_jobs() if args.jobs == "auto" else max(1, int(args.jobs))
    gpu_mb = gpu_memory_mb() if args.gpu else 0
    if gpu_mb and args.jobs == "auto":
        # The graphics card is shared: speech to text keeps WHISPER_GPU_MB, each song SONG_GPU_MB
        jobs = int(max(1, min(jobs, (gpu_mb - WHISPER_GPU_MB) // SONG_GPU_MB)))
    heap = heap_for(jobs)
    print("%d songs, %d at a time" % (len(todo), jobs), flush=True)
    whisper_lock = threading.Lock()
    print_lock = threading.Lock()
    state_lock = threading.Lock()
    total = len(todo)
    whisper = {"device": "cuda" if args.gpu else "cpu"}
    jobs_now = [jobs]
    if gpu_mb:
        print("Graphics card: %d MB; %d MB for each song, %d MB kept for speech to text"
              % (gpu_mb, gpu_share(gpu_mb, jobs), WHISPER_GPU_MB), flush=True)

    def say(text):
        with print_lock:
            print(text, flush=True)

    def save_state():
        tmp = state_path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f: json.dump(state, f, ensure_ascii=False, indent=1)
        os.replace(tmp, state_path)
        write_report(args.keep or args.out, [report[n] for n in files if n in report])

    oom_waits = {}

    def speech_to_text(tag, name, base, song):
        """Writes lyrics from the voice; on the GPU when it can, else on the processor. True when it worked."""
        for attempt in range(2):
            env = dict(os.environ, NOVAD="1", DEVICE=whisper["device"])
            if whisper["device"] == "cuda":
                libs = cuda_libraries()
                if libs: env["LD_LIBRARY_PATH"] = ":".join(libs + [os.environ.get("LD_LIBRARY_PATH", "")]).rstrip(":")
            if song: env["HINT"] = song
            r = subprocess.run([sys.executable, os.path.join(HERE, "transcribe_lyrics.py"), base + ".vocals.wav",
                                base + ".lrc.tmp", args.whisper, "auto"], env=env, capture_output=True, text=True)
            if r.returncode == 0 and os.path.exists(base + ".lrc.tmp"):
                return True
            err = r.stderr or ""
            if whisper["device"] == "cuda" and re.search(r"out of memory|CUBLAS_STATUS_ALLOC_FAILED|CUDNN_STATUS_ALLOC", err, re.I):
                # The card is full of other songs for now: wait for them to finish with it and
                # ask again, rather than move every later song to the (much slower) processor
                waited = oom_waits.get(name, 0)
                if waited < 6:
                    oom_waits[name] = waited + 1
                    say("%s the graphics card is full for now; speech to text waits 30 s and tries again" % tag)
                    time.sleep(30)
                    return speech_to_text(tag, name, base, song)
                say("%s the graphics card stayed full; this song's speech to text runs on the processor" % tag)
                env = dict(env, DEVICE="cpu")
                r = subprocess.run([sys.executable, os.path.join(HERE, "transcribe_lyrics.py"), base + ".vocals.wav",
                                    base + ".lrc.tmp", args.whisper, "auto"], env=env, capture_output=True, text=True)
                if r.returncode == 0 and os.path.exists(base + ".lrc.tmp"):
                    return True
                err = r.stderr or ""
            gpu_trouble = re.search(r"libcublas|libcudnn|CUDA|cuda|cuBLAS|GPU", err)
            if whisper["device"] == "cuda" and gpu_trouble and attempt == 0:
                # The GPU's libraries are missing or broken: the processor is slower but works
                whisper["device"] = "cpu"
                say("%s speech to text cannot use the GPU (%s); using the processor from now on (slower)"
                    % (tag, (r.stderr or "").strip().splitlines()[-1][:160] if r.stderr.strip() else "error"))
                continue
            say("%s speech to text failed: %s" % (tag, (r.stderr or "")[-300:]))
            return False
        return False

    def convert(n, name, why):
        p, mode, notes, sig = settings(name)
        tag = "[%d/%d]" % (n, total)
        base = os.path.join(work, "%03d" % n)
        for ext in (".vocals.wav", ".lrc", ".lrc.tmp", ".lrc.needed", ".json", ".log"):
            if os.path.exists(base + ext): os.remove(base + ext)
        before = state.get(name.lower(), {}).get("row", {}).get("folder")
        gpu_cap = ["-Dyarg.gpuMemMb=%d" % gpu_share(gpu_mb, jobs_now[0])] if gpu_mb else []
        cmd = ["java", "-Xmx%dm" % heap] + (["-Dyarg.cuda=1"] if args.gpu else []) + gpu_cap + ["-cp", args.classpath, "ConvertSong",
               "--audio", os.path.join(args.songs, name), "--out", out_songs, "--mode", mode, "--notes", notes,
               "--model", args.stems_model, "--words-model", args.words_model, "--find-details", "--find-lyrics",
               "--two-singers", "--summary", base + ".json", "--known-artists", known_path, "--threads", str(max(1, (os.cpu_count() or 2) // jobs))]
        for key in ("title", "artist", "album", "year", "genre", "start", "end"):
            if p.get(key): cmd += ["--" + key, p[key]]
        if mode != "normal":
            cmd += ["--vocals-out", base + ".vocals.wav", "--wait-for-lyrics", base + ".lrc"]
        say("%s %s (%s, %s notes)%s" % (tag, name, mode, notes, "" if why == "new" else ": " + why))
        started = time.time()
        stt = None
        with open(base + ".log", "w") as log:
            proc = subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT, env=JAVA_ENV)
            wrote = False
            while proc.poll() is None:
                if not wrote and os.path.exists(base + ".lrc.needed"):
                    # No lyrics anywhere: write them from the voice
                    song = None
                    for line in open(base + ".log", encoding="utf-8", errors="replace"):
                        if line.startswith("Hint: "): song = line[6:].strip()
                    with whisper_lock:
                        say("%s no lyrics online for %s: writing them from the voice" % (tag, name))
                        stt = speech_to_text(tag, name, base, song)
                    if not stt: open(base + ".lrc.tmp", "w").close()
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
            s = dict(file=name, mode=mode, notes=notes, folder="", error=error_line(base + ".log"))
            # The whole log kept where the songs are, to see what went wrong
            try:
                os.makedirs(logs_dir, exist_ok=True)
                shutil.copy(base + ".log", os.path.join(logs_dir, re.sub(r'[\\/:*?"<>|]', "_", name) + ".log"))
                s["error"] += " (full log in logs/)"
            except OSError:
                pass
            say("%s FAILED %s: %s" % (tag, name, s["error"]))
        if os.path.exists(base + ".vocals.wav"): os.remove(base + ".vocals.wav")
        with state_lock:
            if s.get("folder") and keep_songs:
                # Kept at once, so a run cut short loses only the songs still in progress
                dest = os.path.join(keep_songs, s["folder"])
                if os.path.isdir(dest): shutil.rmtree(dest)
                shutil.copytree(os.path.join(out_songs, s["folder"]), dest)
            if s.get("folder") and before and before != s["folder"]:
                # The song's folder has a new name (its details were found this time): the old one goes
                for d in (out_songs, keep_songs):
                    if d and os.path.isdir(os.path.join(d, before)): shutil.rmtree(os.path.join(d, before))
            report[name] = s
            # Speech to text that worked and heard no words: an instrumental, not to be tried again
            state[name.lower()] = dict(settings=sig, row=s, noWords=bool(stt) and s.get("lyrics") == "none")
            save_state()
        return s

    started_all = time.time()
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        list(pool.map(lambda a: convert(*a), [(n, name, why) for n, (name, why) in enumerate(todo, 1)]))
    failed = [name for name, _ in todo if report.get(name, {}).get("error")]
    if failed and jobs > 1:
        # Most failures with several songs at once are the GPU or memory running short: alone, they fit
        print("Trying the %d that failed again, one at a time" % len(failed), flush=True)
        total = len(failed)
        heap = heap_for(1)
        jobs_now[0] = 1
        for n, name in enumerate(failed, 1):
            convert(n, name, "failed with other songs running; again on its own")
    print("All done in %d min %02d s" % divmod(int(time.time() - started_all), 60), flush=True)
    rows = [report[n] for n in files if n in report]
    write_report(args.out, rows)
    if args.keep: write_report(args.keep, rows)
    ok = sum(1 for r in rows if r.get("folder"))
    print("Converted %d of %d songs into %s%s" % (ok, len(rows), songs_dir, " (%d of them earlier)" % skipped if skipped else ""))


def error_line(log_path):
    """The line of a failed conversion's log that says what went wrong."""
    try:
        lines = [l.strip() for l in open(log_path, encoding="utf-8", errors="replace") if l.strip()]
    except OSError:
        return "failed"
    # The converter's own check of the chart says what it found
    found = [l.split("notes.mid problem:", 1)[1].strip() for l in lines if "notes.mid problem:" in l]
    if found:
        return ("notes.mid: " + "; ".join(found[:3]) + ("; and %d more" % (len(found) - 3) if len(found) > 3 else ""))[:400]
    for l in lines:
        if ("Exception" in l or "Error" in l) and not l.startswith("at ") and not l.startswith("Picked up"):
            return l[:300]
    return lines[-1][:300] if lines else "failed"


COLS = ["file", "folder", "title", "artist", "mode", "notes", "bpm", "tempo", "guitarFrom", "lyrics", "vocalLines",
        "guitarExpert", "bassExpert", "deepGuitarExpert", "deepBassExpert", "seconds", "timing", "error"]


def write_report(folder, rows):
    tmp = os.path.join(folder, "report.csv.tmp")
    with open(tmp, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=COLS, extrasaction="ignore"); w.writeheader(); w.writerows(rows)
    os.replace(tmp, os.path.join(folder, "report.csv"))


def cuda_libraries():
    """Folders of the CUDA libraries pip installs (nvidia-cublas-cu12, nvidia-cudnn-cu12), for speech to text."""
    import glob, site
    roots = list(getattr(site, "getsitepackages", lambda: [])()) + [site.getusersitepackages()] + sys.path
    found = []
    for root in roots:
        for d in sorted(glob.glob(os.path.join(root, "nvidia", "*", "lib"))):
            if d not in found: found.append(d)
    return found


def memory_mb():
    try:
        for line in open("/proc/meminfo"):
            if line.startswith("MemAvailable:"):
                return int(line.split()[1]) // 1024
    except OSError:
        pass
    return 8000


# Graphics memory (MB): Whisper large-v3 in half precision while it writes lyrics, and one song
# while its instruments are split and its words heard (the split model's working memory)
WHISPER_GPU_MB = 5500
SONG_GPU_MB = 3500


def gpu_memory_mb():
    """The graphics card's memory in MB, or 0 when there is none to ask."""
    try:
        out = subprocess.run(["nvidia-smi", "--query-gpu=memory.total", "--format=csv,noheader,nounits"],
                             capture_output=True, text=True, timeout=20).stdout.strip().splitlines()
        return int(out[0]) if out else 0
    except (OSError, ValueError, subprocess.SubprocessError):
        return 0


def gpu_share(gpu_mb, jobs):
    """Graphics memory one song may take, with speech to text's share kept free."""
    return int(max(2500, (gpu_mb - WHISPER_GPU_MB) // max(1, jobs)))


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
