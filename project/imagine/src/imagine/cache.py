import datetime
import json
import os
import pathlib


def _cache_path(out_dir: pathlib.Path) -> pathlib.Path:
    return out_dir / ".cache.json"


def _load(cache_file: pathlib.Path) -> dict:
    if not cache_file.exists():
        return {}
    return json.loads(cache_file.read_text(encoding="utf-8"))


def is_cached(out_dir: pathlib.Path, id: int | str, source_hash: str, prompt_hash: str) -> bool:
    data = _load(_cache_path(out_dir))
    entry = data.get(str(id))
    if entry is None:
        return False
    return entry.get("source_hash") == source_hash and entry.get("prompt_hash") == prompt_hash


def mark_cached(out_dir: pathlib.Path, id: int | str, source_hash: str, prompt_hash: str) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    cache_file = _cache_path(out_dir)
    data = _load(cache_file)
    data[str(id)] = {
        "source_hash": source_hash,
        "prompt_hash": prompt_hash,
        "ts": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    }
    tmp = cache_file.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(data, indent=2), encoding="utf-8")
    tmp.replace(cache_file)
