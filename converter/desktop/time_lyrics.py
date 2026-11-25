"""Times plain lyrics from a separated vocal track, or checks timed ones against it.

faster-whisper hears the words (with a time for each); the real lyrics are then matched to what
was heard, word by word, and each line starts at its first matched word. Lines with no word heard
get a time between their neighbours. Writes an .lrc file.

    python time_lyrics.py vocals.wav plain.txt out.lrc [model]

Given timed lyrics (an .lrc, as the converter finds online) instead of plain ones, their times are
kept, except a line whose words were clearly heard starting somewhere else (more than 0.8 s
away, and still in order with its neighbours): that line takes the time it was heard at. The
changes are listed.
"""
import difflib
import re
import sys

from faster_whisper import WhisperModel


def norm(w):
    w = w.lower().replace("’", "'")
    w = re.sub(r"in'$", "ing", w)
    return re.sub(r"[^a-z0-9']", "", w).strip("'")


def syllables(word):
    w = re.sub(r"[^a-z]", "", word.lower())
    return max(1, len(re.findall(r"[aeiouy]+", w)) - (1 if w.endswith("e") and not w.endswith("le") and len(w) > 3 else 0))


def stamp(t):
    cs = int(round(max(0, t) * 100))
    return "[%02d:%02d.%02d]" % (cs // 6000, (cs // 100) % 60, cs % 100)


def main():
    vocals, plain, out = sys.argv[1:4]
    size = sys.argv[4] if len(sys.argv) > 4 else "small.en"
    lines, given = [], []
    stamp_re = re.compile(r"^((?:\[\d+:\d+(?:[.:]\d+)?\])+)(.*)$")
    for raw in open(plain, encoding="utf-8").read().splitlines():
        s = raw.strip()
        t = None
        m = stamp_re.match(s)
        if m:
            first = re.match(r"\[(\d+):(\d+)(?:[.:](\d+))?\]", m.group(1))
            t = int(first.group(1)) * 60 + int(first.group(2)) + (int(first.group(3)) / 10 ** len(first.group(3)) if first.group(3) else 0)
            s = m.group(2).strip()
        if not s or re.fullmatch(r"\[[^\]]*\]", s):
            continue
        s = s.replace("—", "").strip()
        lines.append(s)
        given.append(t)
    timed = all(t is not None for t in given) and given

    model = WhisperModel(size, device="cpu", compute_type="int8")
    prompt = " ".join(lines[:12])
    import subprocess
    import numpy as np
    pcm = subprocess.run(["ffmpeg", "-v", "error", "-i", vocals, "-ac", "1", "-ar", "16000", "-f", "f32le", "-"],
                         check=True, capture_output=True).stdout
    audio = np.frombuffer(pcm, dtype=np.float32)
    hop = 160  # 10 ms at 16 kHz
    frames = len(audio) // hop
    level = np.sqrt((audio[:frames * hop].reshape(frames, hop) ** 2).mean(axis=1))
    gate = np.percentile(level, 95) * 0.06
    voiced = level > gate

    def onsets_between(a, b, quiet=0.15):
        """Times in [a, b] where the voice starts after at least `quiet` seconds of silence."""
        q = int(quiet * 100)
        out = []
        for f in range(max(q, int(a * 100)), min(frames, int(b * 100) + 1)):
            if voiced[f] and not voiced[f - q:f].any():
                out.append(f / 100.0)
        return out
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
    end_of = [None] * len(words)
    for a, b, size_ in sm.get_matching_blocks():
        for k in range(size_):
            time_of[a + k] = heard[b + k][1]
            end_of[a + k] = heard[b + k][2]
    matched = sum(t is not None for t in time_of)
    print("matched %d of %d lyric words" % (matched, len(words)), file=sys.stderr)

    # A line starts at its first matched word, moved back by the words before it that were missed
    start = [None] * len(lines)
    sure = [False] * len(lines)
    for i in range(len(lines)):
        idx = [k for k in range(len(words)) if line_of[k] == i]
        for pos, k in enumerate(idx):
            if time_of[k] is not None:
                # The recogniser often starts a word where the silence before it starts: the word
                # really begins where the voice comes in, inside the time it gives the word
                t0 = time_of[k]
                ins = onsets_between(t0, end_of[k])
                if ins:
                    t0 = ins[-1]
                start[i] = max(0.0, t0 - 0.3 * pos)
                # Sure when it is the line's first or second word and the next one was heard too
                sure[i] = pos <= 1 and k + 1 < len(words) and line_of[k + 1] == i and time_of[k + 1] is not None
                break

    import os as _os
    if _os.environ.get("TRUTH_OUT"):
        # Where each line was heard starting, for measuring other ways of timing ("" when not sure)
        with open(_os.environ["TRUTH_OUT"], "w") as tf:
            for i in range(len(lines)):
                tf.write(("%.2f" % start[i] if start[i] is not None and sure[i] else "") + "\t" + lines[i] + "\n")

    if timed:
        # Where each line should be: where it was heard (when that is sure), else as given
        want = [start[i] if start[i] is not None and sure[i] and abs(start[i] - given[i]) > 0.8 else given[i]
                for i in range(len(lines))]
        out_times = list(given)
        changed = []
        for i in range(len(lines)):
            if want[i] == given[i]:
                continue
            h = want[i]
            lo = out_times[i - 1] if i > 0 else -1
            hi = want[i + 1] if i + 1 < len(lines) else 1e9
            if lo + 0.3 < h < hi - 0.3:
                changed.append((i, given[i], h))
                out_times[i] = h
        # A correction that leaves the lines out of order is undone
        done = False
        while not done:
            done = True
            for c in list(changed):
                i = c[0]
                lo = out_times[i - 1] if i > 0 else -1
                hi = out_times[i + 1] if i + 1 < len(lines) else 1e9
                if not (lo + 0.3 < out_times[i] < hi - 0.3):
                    out_times[i] = given[i]
                    changed.remove(c)
                    done = False

        # Lines crammed together that the voice could not check: spread them between the nearest
        # lines it did check, each given room by how much there is to sing
        anchor = [i == 0 or i == len(lines) - 1 or (start[i] is not None and sure[i] and abs(start[i] - out_times[i]) <= 0.8)
                  for i in range(len(lines))]
        need = [max(0.8, 0.2 * sum(syllables(w) for w in l.split())) for l in lines]
        respaced = []
        a = 0
        for b in range(1, len(lines)):
            if not anchor[b]:
                continue
            run = range(a + 1, b)
            cramped = any(out_times[k + 1] - out_times[k] < 0.4 * need[k] for k in range(a, b))
            span = out_times[b] - out_times[a]
            total = sum(need[k] for k in range(a, b))
            if cramped and len(run) > 0 and span > 0.6 * total:
                t = out_times[a]
                for k in range(a, b - 1):
                    t += span * need[k] / total
                    respaced.append((k + 1, out_times[k + 1], t))
                    out_times[k + 1] = t
            a = b

        snapped = []
        for i in range(len(lines)):
            t = out_times[i]
            f = int(t * 100)
            if f >= frames or not voiced[max(0, f - 5):f + 5].any():
                continue
            lo = out_times[i - 1] + 0.5 if i > 0 else 0
            hi = out_times[i + 1] - 0.3 if i + 1 < len(lines) else 1e9
            before = [o for o in onsets_between(max(lo, t - 1.2), t) if o <= t]
            after = [o for o in onsets_between(t, min(hi, t + 0.25))]
            if after:
                continue
            if before and t - before[-1] > 0.12:
                snapped.append((i, t, before[-1]))
                out_times[i] = before[-1]

        with open(out + ".tmp", "w", encoding="utf-8") as f:
            for i, l in enumerate(lines):
                f.write(stamp(out_times[i]) + l + "\n")
        import os
        os.replace(out + ".tmp", out)
        for i, a_, b_ in snapped:
            print("line %d to where the voice starts %s -> %s: %s" % (i + 1, stamp(a_), stamp(b_), lines[i]), file=sys.stderr)
        for i, a_, b_ in respaced:
            print("line %d spread %s -> %s: %s" % (i + 1, stamp(a_), stamp(b_), lines[i]), file=sys.stderr)
        for i, a, b in changed:
            print("line %d moved %s -> %s: %s" % (i + 1, stamp(a), stamp(b), lines[i]), file=sys.stderr)
        print("kept %d of %d lines as given" % (len(lines) - len(changed) - len(respaced), len(lines)), file=sys.stderr)
        return
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
