import base64
import datetime
import hashlib
import json
import os
import pathlib
import sys

import click
import dotenv


@click.group()
def cli():
    pass


@cli.command()
def ping():
    dotenv.load_dotenv()

    api_key = os.environ.get("XAI_API_KEY")
    if not api_key:
        print("FAIL: XAI_API_KEY not set in environment")
        sys.exit(1)

    try:
        import xai_sdk
        xai_sdk.Client(api_key=api_key)
    except Exception as exc:
        print(f"FAIL: {repr(exc)}")
        sys.exit(1)

    print("OK: xai-sdk client instantiated")


_DEFAULT_IN = (
    pathlib.Path(__file__).resolve().parent.parent.parent.parent.parent
    / "jyx2" / "Assets" / "BuildSource" / "head"
)

_DEFAULT_OUT = (
    pathlib.Path(__file__).resolve().parent.parent.parent
    / "out"
)

_COST_PER_IMAGE = 0.06


def _parse_ids(ids_str: str) -> list[int]:
    ids = []
    for part in ids_str.split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            lo, hi = part.split("-", 1)
            lo, hi = int(lo.strip()), int(hi.strip())
            if lo < 0 or hi < 0:
                raise click.BadParameter(f"negative id in range: {part}")
            if lo > hi:
                raise click.BadParameter(f"invalid range: {part}")
            ids.extend(range(lo, hi + 1))
        else:
            val = int(part)
            if val < 0:
                raise click.BadParameter(f"negative id: {val}")
            ids.append(val)
    seen = set()
    deduped = []
    for i in ids:
        if i not in seen:
            seen.add(i)
            deduped.append(i)
    return deduped


@cli.command("reprocess")
@click.option("--id", "sprite_id", type=int, default=None)
@click.option("--ids", "ids_str", type=str, default=None)
@click.option("--out", "out_dir", type=click.Path(), default=None)
@click.option("--dry-run", is_flag=True, default=False)
def reprocess(
    sprite_id: int | None,
    ids_str: str | None,
    out_dir: str | None,
    dry_run: bool,
):
    if sprite_id is not None and ids_str is not None:
        print("FAIL: specify exactly one of --id or --ids, not both")
        sys.exit(1)
    if sprite_id is None and ids_str is None:
        print("FAIL: specify one of --id N or --ids RANGE")
        sys.exit(1)

    if sprite_id is not None:
        ids = [sprite_id]
    else:
        try:
            ids = _parse_ids(ids_str)
        except (ValueError, click.BadParameter) as exc:
            print(f"FAIL: invalid --ids: {exc}")
            sys.exit(1)

    out_path = pathlib.Path(out_dir) if out_dir is not None else _DEFAULT_OUT
    raw_dir = out_path / "raw"

    from imagine import postprocess

    any_failed = False
    for sid in ids:
        matches = list(raw_dir.glob(f"{sid}.*"))
        if dry_run:
            print(f"id: {sid}")
            if not matches:
                print(f"raw: MISSING")
                print(f"FAIL: id {sid}: no raw file found at {raw_dir / str(sid)}.*")
                any_failed = True
            else:
                print(f"raw: {matches[0].resolve()}")
            continue

        if not matches:
            print(f"FAIL: id {sid}: no raw file found at {raw_dir / str(sid)}.*")
            any_failed = True
            continue

        if len(matches) > 1:
            print(f"WARN: id {sid}: multiple raw files found, using {matches[0].name}")

        raw_bytes = matches[0].read_bytes()
        final_bytes = postprocess.to_final_png(raw_bytes)
        out_file = out_path / f"{sid}.png"
        out_file.write_bytes(final_bytes)
        print(f"OK: id {sid} -> {out_file}")

    sys.exit(1 if any_failed else 0)


@cli.command("edit")
@click.option("--id", "sprite_id", type=int, default=None)
@click.option("--ids", "ids_str", type=str, default=None)
@click.option("--in", "in_dir", type=click.Path(), default=None)
@click.option("--out", "out_dir", type=click.Path(), default=None)
@click.option("--dry-run", is_flag=True, default=False)
@click.option("--force", is_flag=True, default=False)
@click.option("--max", "max_batch", type=int, default=20)
@click.option("--yes-i-know", is_flag=True, default=False)
def edit(
    sprite_id: int | None,
    ids_str: str | None,
    in_dir: str | None,
    out_dir: str | None,
    dry_run: bool,
    force: bool,
    max_batch: int,
    yes_i_know: bool,
):
    if sprite_id is not None and ids_str is not None:
        print("FAIL: specify exactly one of --id or --ids, not both")
        sys.exit(1)
    if sprite_id is None and ids_str is None:
        print("FAIL: specify one of --id N or --ids RANGE")
        sys.exit(1)

    if sprite_id is not None:
        ids = [sprite_id]
    else:
        try:
            ids = _parse_ids(ids_str)
        except (ValueError, click.BadParameter) as exc:
            print(f"FAIL: invalid --ids: {exc}")
            sys.exit(1)

    if len(ids) > max_batch and not yes_i_know:
        print(
            f"FAIL: batch size {len(ids)} exceeds --max {max_batch}, "
            f"use --yes-i-know to override"
        )
        sys.exit(1)

    head_dir = pathlib.Path(in_dir) if in_dir is not None else _DEFAULT_IN
    out_path = pathlib.Path(out_dir) if out_dir is not None else _DEFAULT_OUT

    if dry_run:
        est_cost = len(ids) * _COST_PER_IMAGE
        print(f"estimated cost: {len(ids)} x ${_COST_PER_IMAGE:.2f} = ${est_cost:.2f}")
        print(f"ids: {ids}")

        from imagine import prompts

        any_missing = False
        for sid in ids:
            source_path = (head_dir / f"{sid}.png").resolve()
            if not source_path.exists():
                print(f"FAIL: source not found: {source_path}")
                any_missing = True
                continue
            prompt = prompts.prompt_for(sid)
            file_bytes = source_path.read_bytes()
            b64 = base64.b64encode(file_bytes).decode()
            data_uri = f"data:image/png;base64,{b64}"
            print(f"id: {sid}")
            print(f"source: {source_path}")
            print(f"prompt: {prompt}")
            print(f"image_uri_prefix: data:image/png;base64,{b64[:20]}... ({len(data_uri)} bytes)")

        sys.exit(1 if any_missing else 0)

    import httpx
    from imagine import cache as imagine_cache
    from imagine import client as imagine_client
    from imagine import postprocess
    from imagine import prompts

    out_path.mkdir(parents=True, exist_ok=True)
    raw_dir = out_path / "raw"
    raw_dir.mkdir(parents=True, exist_ok=True)
    cost_file = out_path / ".cost.jsonl"

    any_failed = False
    for sid in ids:
        try:
            source_path = (head_dir / f"{sid}.png").resolve()
            if not source_path.exists():
                print(f"FAIL: id {sid}: source not found: {source_path}")
                any_failed = True
                continue

            prompt = prompts.prompt_for(sid)
            file_bytes = source_path.read_bytes()
            source_hash = hashlib.sha256(file_bytes).hexdigest()[:16]
            prompt_hash = hashlib.sha256(prompt.encode()).hexdigest()[:16]

            if not force and imagine_cache.is_cached(out_path, sid, source_hash, prompt_hash):
                print(f"SKIP: id {sid} (cached)")
                continue

            response = imagine_client.edit_image(source_path, prompt)

            result_url = response.url
            resp = httpx.get(result_url, timeout=60)
            resp.raise_for_status()
            raw_bytes = resp.content

            ext = postprocess.detect_extension(raw_bytes)
            raw_file = raw_dir / f"{sid}.{ext}"
            raw_file.write_bytes(raw_bytes)

            final_bytes = postprocess.to_final_png(raw_bytes)
            out_file = out_path / f"{sid}.png"
            out_file.write_bytes(final_bytes)

            try:
                cost = response.cost_usd
            except Exception:
                cost = None

            cost_line = json.dumps({
                "ts": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "id": sid,
                "prompt_hash": prompt_hash,
                "source_hash": source_hash,
                "est_cost_usd": cost,
                "result_url": result_url,
            }) + "\n"
            with cost_file.open("a", encoding="utf-8") as f:
                f.write(cost_line)

            imagine_cache.mark_cached(out_path, sid, source_hash, prompt_hash)
            print(f"OK: id {sid} -> {out_file}")

        except Exception as exc:
            print(f"FAIL: id {sid}: {repr(exc)}")
            any_failed = True

    sys.exit(1 if any_failed else 0)


@cli.command("variants")
@click.option("--names", default=None, help="Comma-separated variant names. Default: all 8.")
@click.option("--out", "out_dir", type=click.Path(), default=None)
@click.option("--dry-run", is_flag=True, default=False)
@click.option("--force", is_flag=True, default=False)
def variants_cmd(names, out_dir, dry_run, force):
    from imagine import variants as v

    out_path = pathlib.Path(out_dir) if out_dir is not None else _DEFAULT_OUT

    if names:
        name_list = [n.strip() for n in names.split(",") if n.strip()]
        unknown = [n for n in name_list if n not in v.VARIANTS]
        if unknown:
            print(f"FAIL: unknown variant name(s): {', '.join(unknown)}", file=sys.stderr)
            sys.exit(1)
    else:
        name_list = list(v.VARIANTS.keys())

    if dry_run:
        would_run = 0
        cached_count = 0
        any_missing = False
        print(f"plan: {len(name_list)} variants")
        for name in name_list:
            source_id, _ = v.VARIANTS[name]
            source_path = out_path / f"{source_id}.png"
            if not source_path.exists():
                print(f"FAIL: {name}: source {source_path} not found")
                any_missing = True
                continue
            prompt = v.build_variant_prompt(name)
            source_hash = hashlib.sha256(source_path.read_bytes()).hexdigest()[:16]
            prompt_hash = hashlib.sha256(prompt.encode()).hexdigest()[:16]
            from imagine import cache
            if (not force) and cache.is_cached(out_path, name, source_hash, prompt_hash):
                print(f"SKIP: {name} (cached)")
                cached_count += 1
            else:
                print(f"id: {name}  source: {source_path}  prompt: {prompt[:80]}...")
                would_run += 1
        print(f"estimated cost: {would_run} would-run × ${_COST_PER_IMAGE:.2f} = ${_COST_PER_IMAGE * would_run:.2f}; {cached_count} cached")
        sys.exit(1 if any_missing else 0)

    import httpx
    from imagine import cache
    from imagine import client as imagine_client
    from imagine import postprocess

    out_path.mkdir(parents=True, exist_ok=True)
    raw_dir = out_path / "raw"
    raw_dir.mkdir(parents=True, exist_ok=True)
    cost_file = out_path / ".cost.jsonl"

    any_failed = False
    for name in name_list:
        try:
            source_id, _ = v.VARIANTS[name]
            source_path = out_path / f"{source_id}.png"
            if not source_path.exists():
                print(f"FAIL: {name}: source {source_path} not found")
                any_failed = True
                continue

            prompt = v.build_variant_prompt(name)
            source_bytes = source_path.read_bytes()
            source_hash = hashlib.sha256(source_bytes).hexdigest()[:16]
            prompt_hash = hashlib.sha256(prompt.encode()).hexdigest()[:16]

            if not force and cache.is_cached(out_path, name, source_hash, prompt_hash):
                print(f"SKIP: {name} (cached)")
                continue

            response = imagine_client.edit_image(source_path, prompt)

            result_url = response.url
            resp = httpx.get(result_url, timeout=60)
            resp.raise_for_status()
            raw_bytes = resp.content

            ext = postprocess.detect_extension(raw_bytes)
            raw_file = raw_dir / f"{name}.{ext}"
            raw_file.write_bytes(raw_bytes)

            final_bytes = postprocess.to_final_png(raw_bytes)
            out_file = out_path / f"{name}.png"
            out_file.write_bytes(final_bytes)

            try:
                cost = response.cost_usd
            except Exception:
                cost = None

            cost_line = json.dumps({
                "ts": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "id": name,
                "prompt_hash": prompt_hash,
                "source_hash": source_hash,
                "est_cost_usd": cost,
                "result_url": result_url,
            }) + "\n"
            with cost_file.open("a", encoding="utf-8") as f:
                f.write(cost_line)

            cache.mark_cached(out_path, name, source_hash, prompt_hash)
            print(f"OK: {name} -> {out_file}")

        except Exception as exc:
            print(f"FAIL: {name}: {repr(exc)}")
            any_failed = True

    sys.exit(1 if any_failed else 0)
