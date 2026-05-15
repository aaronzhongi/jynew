"""CLI entrypoint for the offline lore pipeline.

  python -m lore_pipeline.run [--dry-run] [--only world|bios|dossiers]
                              [--novel PATH] [--out DIR]

--dry-run        preview prompts + token cost. NO API key / xai-sdk needed.
--only X         run just one of world|bios|dossiers (default: all three).
--novel PATH     override the novel path (default config.NOVEL_PATH).
--out DIR        override the output dir (default config.OUT_DIR).

Writes world.json / bios.json / dossiers.json (UTF-8, no BOM) into OUT_DIR.
Re-running is idempotent (same novel + same config => same JSON shape;
low temperature). The Unity importer (T3A.5) then PATCHES bios and
re-writes WorldCodex/Dossiers — it never clobbers hand-authored Phase 1/2
bio fields.
"""

import argparse
import json
import os
import sys

from . import chunker, config
from .xai_client import XaiClient, XaiError
from . import extract


def _write_json(out_dir: str, name: str, obj: dict) -> str:
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name)
    # UTF-8, NO BOM, Unicode preserved (contract: Simplified Chinese, no BOM).
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(obj, fh, ensure_ascii=False, indent=2)
        fh.write("\n")
    return path


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        prog="lore_pipeline.run",
        description="Offline 射雕 lore extraction for AI Tavern Phase 3.",
    )
    ap.add_argument("--dry-run", action="store_true",
                    help="preview prompts + token cost; no API key needed.")
    ap.add_argument("--only", choices=["world", "bios", "dossiers"],
                    default=None, help="run just one stage (default: all).")
    ap.add_argument("--novel", default=config.NOVEL_PATH,
                    help=f"novel path (default: {config.NOVEL_PATH}).")
    ap.add_argument("--out", default=config.OUT_DIR,
                    help=f"output dir (default: {config.OUT_DIR}).")
    args = ap.parse_args(argv)

    print("=" * 72)
    print("AI Tavern lore pipeline (T3A.6)  —  "
          f"{'DRY-RUN (no tokens spent)' if args.dry_run else 'LIVE'}")
    print(f"  novel : {args.novel}")
    print(f"  out   : {args.out}")
    print(f"  model : {config.MODEL}  (verify id at https://docs.x.ai/)")
    print(f"  cutoff: 回{config.CUTOFF_HUI} (冤家聚头) — enforced "
          f"structurally + by prompt")
    print(f"  stage : {args.only or 'all (world, bios, dossiers)'}")
    print("=" * 72)

    # --- read + segment the novel (no API; works in dry-run) -----------
    try:
        novel_text = chunker.read_novel(args.novel)
    except FileNotFoundError:
        print(f"\nERROR: novel not found at '{args.novel}'.\n"
              f"Pass --novel PATH or set JYNEW_NOVEL_PATH. The file is "
              f"《射雕英雄傳》 shediao.txt (繁體, ~2.7 MB, 40 回).",
              file=sys.stderr)
        return 2
    except OSError as exc:
        print(f"\nERROR reading novel: {exc}", file=sys.stderr)
        return 2

    chapters = chunker.split_chapters(novel_text)
    in_scope = chunker.chapters_up_to_cutoff(chapters)
    print(f"\nSegmented {len(chapters)} 回; "
          f"{len(in_scope)} 回 within cutoff "
          f"(回{in_scope[0].hui}–回{in_scope[-1].hui}: "
          f"〈{in_scope[0].title}〉 … 〈{in_scope[-1].title}〉).")

    client = XaiClient(dry=args.dry_run)

    # --- run the map-reduce --------------------------------------------
    try:
        results = extract.run_pipeline(client, chapters, only=args.only)
    except XaiError as exc:
        # Clean, actionable, no stack trace (constraint).
        print(f"\nERROR: {exc}", file=sys.stderr)
        return 3

    # --- emit ----------------------------------------------------------
    written = []
    if not args.dry_run:
        name_map = {"world": "world.json",
                    "bios": "bios.json",
                    "dossiers": "dossiers.json"}
        for key, fname in name_map.items():
            if key in results:
                written.append(_write_json(args.out, fname, results[key]))

    # --- summary -------------------------------------------------------
    print("\n" + "=" * 72)
    print("SUMMARY")
    print(f"  cost : {client.cost_summary()}")
    if args.dry_run:
        print("  files: (dry-run — no files written). Re-run without "
              "--dry-run, with XAI_API_KEY set, to generate them.")
        print("\n  Rough one-time LIVE cost (Plan §3.2): ~2M input tokens "
              "for the per-character map (回1–10 × roster × extract), "
              "~0.5M for the reduces. One-time offline spend.")
    else:
        for p in written:
            print(f"  wrote: {p}")
        print("\n  Next: in Unity ▸ AI Tavern ▸ Import Lore JSON… ▸ pick "
              f"'{args.out}'. Then run validate.py to check the contract:")
        print(f"    python -m lore_pipeline.validate --out \"{args.out}\"")
    print("=" * 72)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
