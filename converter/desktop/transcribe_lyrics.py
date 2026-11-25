"""Writes lyrics for a song that has none: speech-to-text on the separated voice.

    NOVAD=1 python transcribe_lyrics.py vocals.wav out.lrc large-v3-turbo [en|ko|...|auto]

Needs faster-whisper and num2words (pip). Then convert with --lyrics out.lrc and --words-model:
the word aligner times the written lines against the voice like any other lyrics.

Measured with lyrics_wer.py on songs whose real lyrics are known (share of words wrong):
  Overthinking (rap/R&B)  large-v3-turbo 29%, medium 33%, small 39%; phone word model 82%
  Body Knockin' (R&B)     large-v3-turbo 29%
  Gee (Korean/English)    about 60% of characters wrong
NOVAD=1 (no silence filter) heard more words and scored better on every English song; the words it
makes up in silence are dropped by checking the separated voice under each word.
"""
import re, subprocess, sys
import numpy as np
from faster_whisper import WhisperModel

vocals, out = sys.argv[1], sys.argv[2]
VAD = __import__("os").environ.get("NOVAD") is None
size = sys.argv[3] if len(sys.argv) > 3 else "medium"
lang = sys.argv[4] if len(sys.argv) > 4 and sys.argv[4] != "auto" else None
pcm = subprocess.run(["ffmpeg", "-v", "error", "-i", vocals, "-ac", "1", "-ar", "16000", "-f", "f32le", "-"], check=True, capture_output=True).stdout
audio = np.frombuffer(pcm, dtype=np.float32)
model = WhisperModel(size, device="cpu", compute_type="int8")
segments, info = model.transcribe(audio, language=lang, word_timestamps=True, vad_filter=VAD, beam_size=5,
                                  condition_on_previous_text=False, no_speech_threshold=0.5)
words = []
for s in segments:
    for w in s.words or []:
        t = w.word.strip()
        if t:
            words.append((w.start, w.end, t, w.probability))
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
print("language %s, %d words heard, %d where the voice is" % (info.language, len(words), len(kept)), file=sys.stderr)
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
            w = num2words(int(digits), lang=(info.language or "en")) if not digits.startswith("0") else " ".join(num2words(int(c)) for c in digits)
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
        if (gap > 0.45 or len(cur) >= 12 or (re.search(r"[.!?]$", last) and len(cur) >= 2)
                or (last.endswith(",") and len(cur) >= 7)):
            lines.append(cur); cur = []
    cur.append(w)
if cur: lines.append(cur)

def stamp(t):
    cs = int(round(max(0, t) * 100)); return "[%02d:%02d.%02d]" % (cs // 6000, (cs // 100) % 60, cs % 100)

with open(out, "w", encoding="utf-8") as f:
    for line in lines:
        text = " ".join(w[2] for w in line)
        text = re.sub(r"\s+([,.!?])", r"\1", text).strip().rstrip(",")
        text = text[0].upper() + text[1:] if text else text
        f.write(stamp(line[0][0]) + text + "\n")
print("%d lines" % len(lines), file=sys.stderr)
