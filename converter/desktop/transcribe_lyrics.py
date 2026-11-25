"""Writes lyrics for a song that has none: speech-to-text on the separated voice.

    NOVAD=1 HINT="Title, a genre song by Artist" python transcribe_lyrics.py vocals.wav out.lrc [large-v3] [en|ko|...|auto]

Needs faster-whisper and num2words (pip). Then convert with --lyrics out.lrc and --words-model:
the word aligner times the written lines against the voice like any other lyrics.

Measured with lyrics_wer.py on two songs whose real lyrics are known (share of words wrong):
                               Overthinking   Body Knockin'
  large-v3, with HINT              27%            20%      <- the default
  large-v3, plain                  27%            24%
  large-v3-turbo, with HINT        29%            28%      (4 times faster on a CPU)
  large-v3-turbo, plain            29%            29%
  medium / small                   33% / 39%
  the phone's word-timing model    82%
Tried and switched off (REPEATS=1 turns it on): rewording near-identical lines to the wording most
of them share. It made both songs worse with both models (up to 8 points): hooks vary on purpose.
NOVAD=1 (no silence filter) heard more words and scored better; the words the model makes up in
silence are dropped by checking the separated voice under each word.
"""
import os, re, subprocess, sys
import numpy as np
from faster_whisper import WhisperModel

vocals, out = sys.argv[1], sys.argv[2]
VAD = __import__("os").environ.get("NOVAD") is None
size = sys.argv[3] if len(sys.argv) > 3 else "large-v3"
lang = sys.argv[4] if len(sys.argv) > 4 and sys.argv[4] != "auto" else None
pcm = subprocess.run(["ffmpeg", "-v", "error", "-i", vocals, "-ac", "1", "-ar", "16000", "-f", "f32le", "-"], check=True, capture_output=True).stdout
audio = np.frombuffer(pcm, dtype=np.float32)
DEVICE = __import__("os").environ.get("DEVICE", "cpu")
import json as _json
cache = out + ".heard.json"
cached = _json.load(open(cache)) if os.environ.get("REUSE") and os.path.exists(cache) else None
model = None if cached else WhisperModel(size, device=DEVICE, compute_type="float16" if DEVICE == "cuda" else "int8")
import os
HINT = os.environ.get("HINT")  # e.g. "Overthinking by Coi Leray and H.E.R." : names and words to expect
if HINT and not HINT.rstrip().endswith((".", "!", "?")):
    # A prompt ending in a full stop keeps the model writing punctuation
    HINT = HINT.rstrip() + "."
if cached:
    words = [tuple(w) for w in cached["words"]]
    language = cached["language"]
else:
    segments, info = model.transcribe(audio, language=lang, initial_prompt=HINT, word_timestamps=True, vad_filter=VAD, beam_size=5,
                                      condition_on_previous_text=False, no_speech_threshold=0.5)
    words = []
    for s in segments:
        for w in s.words or []:
            t = w.word.strip()
            if t:
                words.append((w.start, w.end, t, w.probability))
    language = info.language
    _json.dump({"language": language, "words": words}, open(cache, "w"))
# Words where the separated voice is silent are made up (the model fills silence with phrases
# like "thanks for watching"): each word must have voice under at least a third of it
hop = 160
frames = len(audio) // hop
level = np.sqrt((audio[:frames * hop].reshape(frames, hop) ** 2).mean(axis=1))
gate = np.percentile(level, 95) * 0.06
voiced = level > gate
kept = []
for w in words:
    a, b = int(w[0] * 100), max(int(w[0] * 100) + 1, int(w[1] * 100))
    if a < frames and voiced[a:min(b, frames)].mean() >= 0.33:
        kept.append(w)
print("language %s, %d words heard, %d where the voice is" % (language, len(words), len(kept)), file=sys.stderr)
words = kept

# Numbers as words, as they are sung ("40,000" is "forty thousand"): a lyric needs letters
try:
    from num2words import num2words
except ImportError:
    num2words = None

def spoken(t):
    if num2words is None:
        return t
    def one(m):
        digits, tail = m.group(1).replace(",", ""), m.group(2)
        try:
            w = num2words(int(digits), lang=(language or "en")) if not digits.startswith("0") else " ".join(num2words(int(c)) for c in digits)
        except Exception:
            return m.group(0)
        return w.replace("-", " ") + (tail or "")
    return re.sub(r"(\d[\d,]*)(s|%|'s)?", lambda m: one(m) if m.group(2) != "%" else one(m)[:-1] + " percent", t)

# The model can split one number into pieces ("100" and ",000s"): put them back together first
joined = []
for w in words:
    if joined and re.match(r"^[,.]?\d", w[2]) and re.search(r"\d,?$", joined[-1][2]):
        a, b, t, p = joined[-1]
        joined[-1] = (a, w[1], t + w[2], min(p, w[3]))
    else:
        joined.append(w)
words = [(a, b, spoken(t), p) for a, b, t, p in joined]

# A lyric line ends at a pause or a sentence end; a long one at a comma; none runs past twelve words
lines, cur = [], []
for i, w in enumerate(words):
    if cur:
        gap = w[0] - cur[-1][1]
        last = cur[-1][2]
        # Without punctuation the model still capitalises each new sentence ("...my phone I'm on
        # my missed call shit You the type..."): a capital starts a new line, unless it is "I",
        # or part of a name (the word before or after is capitalised too)
        nxt = words[i + 1][2] if i + 1 < len(words) else ""
        starts = (w[2][:1].isupper() and not re.match(r"^I('|$)", w[2]) and not last[:1].isupper()
                  and not nxt[:1].isupper() and len(cur) >= 3)
        if (gap > 0.45 or len(cur) >= 12 or (re.search(r"[.!?]$", last) and len(cur) >= 2)
                or (last.endswith(",") and len(cur) >= 7) or starts):
            lines.append(cur); cur = []
    cur.append(w)
if cur: lines.append(cur)

# The song's own repeats: a hook is sung several times and heard a little differently each time.
# Lines that are nearly the same are one line; each takes the wording most of them agree on.
import difflib
def norm(ws):
    return [re.sub(r"[^a-z0-9']", "", w[2].lower()) for w in ws]
def same(a, b):
    return difflib.SequenceMatcher(None, a, b, autojunk=False).ratio()
fixed = 0
if os.environ.get("REPEATS") is not None:  # off: it made every test song worse (see the notes at the top)
    keys = [norm(l) for l in lines]
    done = [False] * len(lines)
    for i in range(len(lines)):
        if done[i] or len(keys[i]) < 3: continue
        group = [j for j in range(len(lines)) if not done[j] and len(keys[j]) >= 3 and same(keys[i], keys[j]) >= 0.7]
        if len(group) < 3: continue
        # the wording closest to all the others
        best = max(group, key=lambda j: sum(same(keys[j], keys[k]) for k in group))
        for j in group:
            done[j] = True
            if j != best and keys[j] != keys[best] and abs(len(keys[j]) - len(keys[best])) <= 2:
                a, b = lines[j][0][0], lines[j][-1][1]
                n = len(lines[best])
                # the agreed words, spread over this line's own time
                lines[j] = [(a + (b - a) * k / n, a + (b - a) * (k + 1) / n, lines[best][k][2], 1.0) for k in range(n)]
                fixed += 1
print("%d lines reworded from the song's repeats" % fixed, file=sys.stderr)

def stamp(t):
    cs = int(round(max(0, t) * 100)); return "[%02d:%02d.%02d]" % (cs // 6000, (cs // 100) % 60, cs % 100)

with open(out, "w", encoding="utf-8") as f:
    for line in lines:
        text = " ".join(w[2] for w in line)
        text = re.sub(r"\s+([,.!?])", r"\1", text).strip().rstrip(",")
        text = text[0].upper() + text[1:] if text else text
        f.write(stamp(line[0][0]) + text + "\n")
print("%d lines" % len(lines), file=sys.stderr)
