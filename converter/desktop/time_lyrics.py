"""Times plain lyrics from a separated vocal track.

faster-whisper hears the words (with a time for each); the real lyrics are then matched to what
was heard, word by word, and each line starts at its first matched word. Lines with no word heard
get a time between their neighbours. Writes an .lrc file.

    python time_lyrics.py vocals.wav plain.txt out.lrc [model]
"""
import difflib
import re
import sys

from faster_whisper import WhisperModel


def norm(w):
    w = w.lower().replace("’", "'")
    w = re.sub(r"in'$", "ing", w)
    return re.sub(r"[^a-z0-9']", "", w).strip("'")


def stamp(t):
    cs = int(round(max(0, t) * 100))
    return "[%02d:%02d.%02d]" % (cs // 6000, (cs // 100) % 60, cs % 100)


def main():
    vocals, plain, out = sys.argv[1:4]
    size = sys.argv[4] if len(sys.argv) > 4 else "small.en"
    lines = []
    for raw in open(plain, encoding="utf-8").read().splitlines():
        s = raw.strip()
        if not s or re.fullmatch(r"\[[^\]]*\]", s):
            continue
        s = s.replace("—", "").strip()
        lines.append(s)

    model = WhisperModel(size, device="cpu", compute_type="int8")
    prompt = " ".join(lines[:12])
    import subprocess
    import numpy as np
    pcm = subprocess.run(["ffmpeg", "-v", "error", "-i", vocals, "-ac", "1", "-ar", "16000", "-f", "f32le", "-"],
                         check=True, capture_output=True).stdout
    audio = np.frombuffer(pcm, dtype=np.float32)
    segments, _ = model.transcribe(audio, language="en", word_timestamps=True, vad_filter=True,
                                   beam_size=5, initial_prompt=prompt, condition_on_previous_text=False)
    heard = []
    for seg in segments:
        for w in seg.words or []:
            n = norm(w.word)
            if n:
                heard.append((n, w.start, w.end))
    print("heard %d words" % len(heard), file=sys.stderr)
    with open(out + ".heard", "w") as hf:
        for h in heard: hf.write("%.2f %.2f %s\n" % (h[1], h[2], h[0]))

    # The lyrics as one list of words, each knowing its line
    words, line_of = [], []
    for i, l in enumerate(lines):
        for w in l.split():
            n = norm(w)
            if n:
                words.append(n)
                line_of.append(i)

    sm = difflib.SequenceMatcher(None, words, [h[0] for h in heard], autojunk=False)
    time_of = [None] * len(words)
    for a, b, size_ in sm.get_matching_blocks():
        for k in range(size_):
            time_of[a + k] = heard[b + k][1]
    matched = sum(t is not None for t in time_of)
    print("matched %d of %d lyric words" % (matched, len(words)), file=sys.stderr)

    # A line starts at its first matched word, moved back by the words before it that were missed
    start = [None] * len(lines)
    for i in range(len(lines)):
        idx = [k for k in range(len(words)) if line_of[k] == i]
        for pos, k in enumerate(idx):
            if time_of[k] is not None:
                start[i] = max(0.0, time_of[k] - 0.3 * pos)
                break
    # Lines nobody was heard on: between their neighbours
    known = [i for i in range(len(lines)) if start[i] is not None]
    for i in range(len(lines)):
        if start[i] is None:
            before = [j for j in known if j < i]
            after = [j for j in known if j > i]
            if before and after:
                a, b = before[-1], after[0]
                start[i] = start[a] + (start[b] - start[a]) * (i - a) / (b - a)
            elif before:
                start[i] = start[before[-1]] + 3.0 * (i - before[-1])
            elif after:
                start[i] = max(0.0, start[after[0]] - 3.0 * (after[0] - i))
    # Keep them in order
    for i in range(1, len(lines)):
        if start[i] <= start[i - 1]:
            start[i] = start[i - 1] + 0.3
    with open(out + ".tmp", "w", encoding="utf-8") as f:
        for i, l in enumerate(lines):
            f.write(stamp(start[i]) + l + "\n")
    import os
    os.replace(out + ".tmp", out)
    print("lines heard directly: %d of %d" % (len(known), len(lines)), file=sys.stderr)


if __name__ == "__main__":
    main()
