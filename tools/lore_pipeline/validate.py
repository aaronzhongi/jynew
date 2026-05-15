"""Post-generation check: the 3 output files vs. JSON_CONTRACT.md.

Stdlib `json` only (no deps) so it runs anywhere, anytime, even without
xai-sdk. Checks the required-key structure the Unity importer (T3A.5)
expects. Each file is independently optional (contract: a missing file is
warned-and-skipped); a present file with a malformed shape is an ERROR.

  python -m lore_pipeline.validate [--out DIR]

Exit 0 = all present files conform; 1 = a present file violates the
contract.
"""

import argparse
import json
import os
import sys

# Windows console is cp1252 by default; this prints CJK field names from the
# contract. Force UTF-8 on the std streams (see run.py for the rationale).
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

from . import config

_ERRORS = []
_WARN = []


def _err(msg):
    _ERRORS.append(msg)


def _warn(msg):
    _WARN.append(msg)


def _load(path):
    if not os.path.isfile(path):
        return None
    with open(path, "rb") as fh:
        raw = fh.read()
    if raw[:3] == b"\xef\xbb\xbf":
        _err(f"{os.path.basename(path)}: has a UTF-8 BOM "
             f"(contract: UTF-8, NO BOM).")
        raw = raw[3:]
    try:
        return json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        _err(f"{os.path.basename(path)}: not valid UTF-8 JSON ({exc}).")
        return None


def _need_str(obj, key, where):
    if key not in obj:
        _warn(f"{where}: missing '{key}' (contract: optional, but "
              f"emitting it is preferred).")
    elif not isinstance(obj[key], str):
        _err(f"{where}: '{key}' must be a string.")


def _need_list(obj, key, where):
    if key not in obj:
        _warn(f"{where}: missing '{key}'.")
        return []
    if not isinstance(obj[key], list):
        _err(f"{where}: '{key}' must be an array.")
        return []
    return obj[key]


def check_world(d):
    if d is None:
        _warn("world.json: absent (importer warns-and-skips).")
        return
    if not isinstance(d, dict):
        _err("world.json: top level must be an object.")
        return
    _need_str(d, "era", "world")
    for i, p in enumerate(_need_list(d, "polities", "world")):
        w = f"world.polities[{i}]"
        _need_str(p, "name", w)
        _need_str(p, "brief", w)
        for j, r in enumerate(_need_list(p, "relations", w)):
            _need_str(r, "target", f"{w}.relations[{j}]")
            _need_str(r, "text", f"{w}.relations[{j}]")
    for i, f in enumerate(_need_list(d, "factions", "world")):
        w = f"world.factions[{i}]"
        _need_str(f, "name", w)
        _need_str(f, "brief", w)
        _need_str(f, "homeRegion", w)
        for j, r in enumerate(_need_list(f, "relations", w)):
            _need_str(r, "target", f"{w}.relations[{j}]")
            _need_str(r, "text", f"{w}.relations[{j}]")
    for i, n in enumerate(_need_list(d, "notableFigures", "world")):
        w = f"world.notableFigures[{i}]"
        _need_str(n, "name", w)
        _need_str(n, "polity", w)
        _need_str(n, "faction", w)
        _need_str(n, "oneLine", w)


_SEX_OK = {"male", "female", "other"}


def check_bios(d):
    if d is None:
        _warn("bios.json: absent (importer warns-and-skips).")
        return
    for i, b in enumerate(_need_list(d, "bios", "bios")):
        w = f"bios.bios[{i}]"
        _need_str(b, "agentId", w)
        # sex MUST be present & non-empty (importer overwrites Sex
        # unconditionally; absent/empty would wrongly reset it).
        if not b.get("sex"):
            _err(f"{w}: 'sex' missing/empty — every bio MUST emit sex "
                 f"(Male|Female|Other).")
        elif str(b["sex"]).strip().lower() not in _SEX_OK:
            _err(f"{w}: sex='{b['sex']}' not in Male|Female|Other.")
        _need_str(b, "ageText", w)
        _need_str(b, "personality", w)
        _need_str(b, "appearance", w)
        _need_str(b, "surfaceManner", w)


def check_dossiers(d):
    if d is None:
        _warn("dossiers.json: absent (importer warns-and-skips).")
        return
    for i, ds in enumerate(_need_list(d, "dossiers", "dossiers")):
        w = f"dossiers.dossiers[{i}]"
        _need_str(ds, "agentId", w)
        for j, k in enumerate(_need_list(ds, "polityKnowledge", w)):
            _need_str(k, "subject", f"{w}.polityKnowledge[{j}]")
            _need_str(k, "text", f"{w}.polityKnowledge[{j}]")
        for j, k in enumerate(_need_list(ds, "factionKnowledge", w)):
            _need_str(k, "subject", f"{w}.factionKnowledge[{j}]")
            _need_str(k, "text", f"{w}.factionKnowledge[{j}]")
        for j, p in enumerate(_need_list(ds, "people", w)):
            pw = f"{w}.people[{j}]"
            _need_str(p, "target", pw)
            _need_str(p, "relationship", pw)
            _need_str(p, "impression", pw)
            _need_str(p, "martialNote", pw)
            _need_str(p, "sharedHistory", pw)
            _need_str(p, "theyDoNotKnow", pw)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="lore_pipeline.validate")
    ap.add_argument("--out", default=config.OUT_DIR)
    args = ap.parse_args(argv)

    print(f"Validating against JSON_CONTRACT.md in: {args.out}")
    check_world(_load(os.path.join(args.out, "world.json")))
    check_bios(_load(os.path.join(args.out, "bios.json")))
    check_dossiers(_load(os.path.join(args.out, "dossiers.json")))

    for w in _WARN:
        print(f"  WARN  {w}")
    for e in _ERRORS:
        print(f"  ERROR {e}", file=sys.stderr)

    if _ERRORS:
        print(f"\nFAIL — {len(_ERRORS)} contract violation(s), "
              f"{len(_WARN)} warning(s).")
        return 1
    print(f"\nOK — contract satisfied ({len(_WARN)} non-fatal warning(s)).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
