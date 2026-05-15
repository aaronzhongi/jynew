"""Split shediao.txt into its 40 回 (chapters).

The edition's headers look like `第1回 風雪驚變` on their own line
(Arabic digits; a fallback also accepts 第十回 Chinese numerals in case
the user supplies a different edition). Everything before the first 回
header (the 小說簡介 preamble + download notices) is discarded.

CRITICAL (Plan §3.3): `chapters_up_to_cutoff()` is the STRUCTURAL
enforcement of CUTOFF_HUI — it physically returns only 回1..CUTOFF_HUI.
Per-character extraction is fed exclusively from this slice, so even a
prompt-injection or a model that "knows" the rest of the saga cannot
pull post-cutoff text — that text is never in the call at all.
"""

import re
from dataclasses import dataclass
from typing import List

from . import config

# `第` + (Arabic 1-3 digits | Chinese numerals) + `回`, at line start,
# optionally followed by whitespace + a title on the same line.
_HUI_HEADER = re.compile(
    r"^第\s*([0-9]{1,3}|[一二三四五六七八九十百零]{1,6})\s*回"
    r"[ \t　]*(.*?)\s*$",
    re.MULTILINE,
)

_CN_DIGIT = {"零": 0, "一": 1, "二": 2, "三": 3, "四": 4,
             "五": 5, "六": 6, "七": 7, "八": 8, "九": 9}


@dataclass(frozen=True)
class Chapter:
    hui: int        # 1-based 回 number
    title: str      # e.g. "風雪驚變" (繁體, as in source)
    text: str       # full chapter body INCLUDING its header line


def _cn_to_int(s: str) -> int:
    """十=10, 十一=11, 二十=20, 二十三=23, 四十=40 (covers 1..40)."""
    if s.isdigit():
        return int(s)
    if s == "十":
        return 10
    total, has_ten = 0, "十" in s
    if has_ten:
        left, _, right = s.partition("十")
        tens = _CN_DIGIT.get(left, 1) if left else 1
        ones = _CN_DIGIT.get(right, 0) if right else 0
        total = tens * 10 + ones
    else:
        for ch in s:
            total = total * 10 + _CN_DIGIT.get(ch, 0)
    return total


def read_novel(path: str = None) -> str:
    """Read the novel as UTF-8, tolerating a BOM. Never converts 繁→简
    (Plan §10 Q7 — that is the model's job, per-call)."""
    path = path or config.NOVEL_PATH
    with open(path, "r", encoding="utf-8-sig", errors="strict") as fh:
        return fh.read()


def split_chapters(novel_text: str) -> List[Chapter]:
    """Return all chapters in order. Asserts EXPECTED_HUI_COUNT (40);
    on mismatch logs a warning and continues (a different edition still
    works, downstream just sees a different count)."""
    matches = list(_HUI_HEADER.finditer(novel_text))
    if not matches:
        raise RuntimeError(
            "No 回 chapter headers found. The novel format is unexpected; "
            "inspect the first ~3 KB and adjust chunker._HUI_HEADER. "
            f"(novel path: {config.NOVEL_PATH})"
        )

    chapters: List[Chapter] = []
    for i, m in enumerate(matches):
        start = m.start()
        end = matches[i + 1].start() if i + 1 < len(matches) else len(novel_text)
        hui = _cn_to_int(m.group(1))
        title = (m.group(2) or "").strip()
        chapters.append(Chapter(hui=hui, title=title,
                                text=novel_text[start:end].strip()))

    # De-dupe defensively: some web-scraped editions repeat 第1回. Keep the
    # first occurrence of each 回 number, in ascending order.
    seen, deduped = set(), []
    for ch in sorted(chapters, key=lambda c: (c.hui, )):
        if ch.hui in seen:
            continue
        seen.add(ch.hui)
        deduped.append(ch)

    if len(deduped) != config.EXPECTED_HUI_COUNT:
        print(f"[chunker] WARNING: found {len(deduped)} 回, expected "
              f"{config.EXPECTED_HUI_COUNT}. Continuing with what was found "
              f"(edition may differ). 回 numbers: "
              f"{[c.hui for c in deduped][:50]}")
    return deduped


def chapters_up_to_cutoff(chapters: List[Chapter],
                          cutoff: int = None) -> List[Chapter]:
    """STRUCTURAL anti-omniscience guard (Plan §3.3). Physically returns
    only 回 <= cutoff. ALL per-character extraction MUST pull from this —
    never from the full list — so post-cutoff text is never in any call."""
    cutoff = config.CUTOFF_HUI if cutoff is None else cutoff
    return [c for c in chapters if c.hui <= cutoff]
