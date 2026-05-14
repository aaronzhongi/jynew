# project/imagine — Realistic Portrait Upgrade Pipeline

A Python batch tool that converts the jynew Unity project's anime-style character
portraits into a more photorealistic look, using xAI's Grok Imagine image-edit API.

## Goal

Re-render the **123** head portraits at
[jyx2/Assets/BuildSource/head/](../../jyx2/Assets/BuildSource/head/) into a
realistic photographic style while preserving each character's identity,
pose, clothing, hairstyle, age, and gender. Output drops into
[project/imagine/out/](./out/) — never overwrite the originals.

**Source inventory (revised 2026-05-13 after user audit):**

The numbered set `0.png`–`114.png` (115 files) is NOT all head portraits.
The user confirmed actual head IDs are:
- **0–75** (76 ids) — head portraits
- **109, 111, 112** (3 ids) — head portraits
- 76–108, 110, 113, 114 — NOT head portraits (items/weapons/scenery
  or similar misc graphics that happen to share the `head/` directory).
  Do NOT run `imagine edit` on these — wasted billing.

Total numbered head portraits: **79**.

Plus 8 variants:
- 6 "old" age variants: `2old.png`, `17old.png`, `47old.png`, `56old.png`,
  `59old.png`, `63old.png` — older versions of the corresponding base id
- 1 "new" makeover variant: `25new.png` — same character as `25.png`, but
  with more elaborate makeup and a different/fancier hairstyle. NOT a
  younger version — same age, same face.
- 1 "a" variant: `27a.png` — feminized appearance of character 27 (Jin Yong
  trope: martial art with feminizing side-effects, à la Dongfang Bubai / 葵花宝典).
  Post-transformation look of the same identity in `27.png`.

**Grand total photoreal portraits to ship: 87** (79 numbered heads + 8 variants).

**Variant handling strategy (NOT yet implemented — for Phase 3+):**
ALL 8 variants follow the same pattern: feed the already-generated photoreal
`N.png` (from Phase 2's pipeline) as the source image, then apply a
prompt-suffix modifier describing the transformation. Never feed the cartoon
variant source (`2old.png` etc.) directly to the API — variants should
converge on a single realistic identity rather than being independently
re-imagined from sketch.

Per-variant prompt suffixes:
- `Nold.png` (×6, `AGE_UP`): "...add roughly 30 years of age — graying or
  white hair, weathered skin, deeper eye lines, slight stoop — but keep the
  same facial structure, eye shape, and distinguishing features"
- `25new.png` (×1, `MAKEOVER`): "...with more elaborate makeup, more
  intricate hair styling (ornate hairpins, polished court look), but same
  facial structure, same age, same identity. NOT a younger version."
- `27a.png` (×1, `FEMINIZE`): "...feminized appearance — softer features,
  longer styled hair, ancient-Chinese women's wuxia attire, but keep the
  same facial identity, eye shape, and distinguishing features. (Reflects
  27's in-novel transformation from a martial art with feminizing
  side-effects.)"

The current pipeline assumes one source per id — variant support needs a new
code path that takes a different `source_id` from the `target_id`. Probably
a `variants.py` config like `{"2old": ("2", AGE_UP_PROMPT), "25new": ("25",
AGE_DOWN_PROMPT), "27a": ("27", FEMINIZE_PROMPT)}` consumed by a new CLI
subcommand or a `--variants` flag.

## Pipeline

Image-to-image via xAI's **official Python SDK** (`xai-sdk`). Each source PNG
is sent as a base64 data URI; a per-character or shared prompt describes the
desired style transfer.

- **SDK**: `xai-sdk` (`pip install xai-sdk`). Do **not** use the `openai`
  package or raw `httpx` — the user has called this out specifically.
- **Method**: `client.image.sample(prompt=..., model="grok-imagine-image-quality", image_url=...)`
- **Model**: `grok-imagine-image-quality`
- **Auth**: SDK reads `XAI_API_KEY` from the environment. Load from `.env`
  via `python-dotenv`; never commit the key.
- **Input image**: base64-encoded data URI (`data:image/png;base64,...`) built
  from the source PNG. The endpoint also accepts public URLs but we won't
  host the sources publicly.
- **Response**: result object exposes `.url` for the generated image — fetch
  the bytes from that URL with `httpx` (allowed for the result download; the
  *call* still goes through the SDK).
- **Billing**: edits charge for both the input and the output image — keep
  this in mind when iterating on prompts; cache by `(source_hash, prompt_hash)`.
- **Docs**: https://docs.x.ai/docs/guides/image-generations

## Constraints to preserve

These matter for the game to remain coherent — every batch run should respect
them:

- **Identity**: face shape, eye shape, distinctive features (scars, beards,
  hair color) must remain recognizable as the same character.
- **Pose & framing**: head-and-shoulders bust shot, same crop as the source.
- **Costume era**: ancient-Chinese / wuxia clothing — no anachronistic outfits.
- **Background**: source PNGs have transparent backgrounds. Grok Imagine
  outputs are JPEG/PNG without alpha — the pipeline must run a background
  removal pass (e.g. `rembg`) and re-encode as transparent PNG.
- **Dimensions**: source is 384×384. Final output must be resized back to
  384×384 to be drop-in compatible with the Unity sprite atlas.

## Layout (planned)

```
project/imagine/
├─ CLAUDE.md              ← you are here
├─ .gitignore             ← ignores out/, .env, __pycache__, *.pyc
├─ pyproject.toml         ← deps: xai-sdk, pillow, rembg, python-dotenv, click, httpx (result download only)
├─ .env.example           ← XAI_API_KEY=...
├─ src/imagine/
│  ├─ __init__.py
│  ├─ cli.py              ← `imagine run --in DIR --out DIR [--ids 0-10]`
│  ├─ client.py           ← xAI HTTP client (edits endpoint)
│  ├─ prompts.py          ← shared style prompt + per-id overrides
│  ├─ postprocess.py      ← bg removal + resize to 384×384
│  └─ cache.py            ← skip already-done (source_hash, prompt_hash)
└─ out/                   ← generated PNGs, gitignored
```

## CLI shape (planned)

```
imagine run --in ../../jyx2/Assets/BuildSource/head --out ./out
imagine run --ids 0,5,12-20 --prompt-file prompts/wuxia.md
imagine diff 42                # side-by-side source vs. output for review
```

Default behavior: resumable (skip outputs that already exist), single-image
concurrency until quality is dialed in, then optional `--concurrency N`.

## Prompting strategy

Two layers:

1. **Shared style prompt** — locks the photoreal look: "photorealistic cinematic
   portrait of a {character} in ancient Chinese wuxia attire, soft natural
   lighting, neutral background, head-and-shoulders bust, 35mm photograph,
   shallow depth of field, no anime, no illustration".
2. **Per-ID override** (optional) — name, gender, age, distinctive traits
   pulled from the jynew character data if available, applied as a JSON map
   keyed by sprite id.

The character roster lives in the Unity project's data tables under
[jyx2/Assets/BuildSource/](../../jyx2/Assets/BuildSource/) — when extending
the prompt map, source character names/traits from there rather than guessing.

## Safety & cost rails

- **Never commit** `.env`, the API key, or `out/` images. `.gitignore` covers
  these — verify before staging.
- **Dry-run mode** (`--dry-run`): print what would be sent, don't call the API.
- **Cost log**: append one line per API call to `out/.cost.jsonl` with id,
  timestamp, prompt hash, and est. cost. Helps catch runaway loops.
- **Hard cap**: refuse to run a batch larger than `--max N` (default 20)
  without `--yes-i-know`. Avoid accidentally re-billing the whole set.

## Don'ts

- Don't modify files under `jyx2/Assets/BuildSource/head/`. Originals are
  read-only inputs.
- Don't add new dependencies that aren't strictly needed — keep the surface
  area small (HTTP client, image lib, bg-removal, .env loader).
- Don't write code that depends on xAI returning a specific image size — read
  what comes back, resize on our side.
- Don't hardcode the API key or sprinkle `os.environ["XAI_API_KEY"]` calls
  across modules — load once in `client.py`.

## Workflow — 4 agents

Work on this project is split across four specialised subagents (defined in
[.claude/agents/](../../.claude/agents/) at the repo root). The top-level
Claude conversation is the **orchestrator**; it dispatches agents but does not
itself code, review, or test. Roles:

| Agent | Role | Tools | Writes? |
| --- | --- | --- | --- |
| [imagine-master](../../.claude/agents/imagine-master.md) | Plans the next phase: decomposes work, defines acceptance criteria, sequences tasks. | Read, Glob, Grep, Web | No |
| [imagine-coder](../../.claude/agents/imagine-coder.md) | Implements one task at a time per the master's spec. | Read, Write, Edit, Glob, Grep, Bash | Yes (code) |
| [imagine-reviewer](../../.claude/agents/imagine-reviewer.md) | Reviews coder's diff against the spec and CLAUDE.md constraints. | Read, Glob, Grep, Bash | No |
| [imagine-tester](../../.claude/agents/imagine-tester.md) | Runs the code and reports pass/fail against acceptance criteria. | Read, Glob, Grep, Bash | Only under `out/` |

**Standard flow per task:**
`orchestrator → master (plan) → coder (implement) → reviewer (approve) → tester (verify) → orchestrator (next)`

The orchestrator may skip the tester for doc/config-only changes, or send a
diff back to coder if reviewer requests changes. Cost-bearing API calls
happen only inside the tester, and only after the reviewer has approved.

## Status

Phase 0 complete (2026-05-12). Phase 1 complete (2026-05-13). Phase 2
complete (2026-05-13).

**Phase 2 deliverables:**
- `src/imagine/postprocess.py` — `detect_extension(bytes)` magic-byte sniff,
  `to_final_png(bytes)` runs rembg → resize 384×384 LANCZOS → PNG RGBA.
- `src/imagine/cache.py` — `is_cached` / `mark_cached` backed by
  `out/.cache.json` (atomic write via `.tmp` + `Path.replace`).
- `cli.py` extended: `--ids "0-10,12,15-20"` range parser, `--force`,
  `--max N` (default 20) + `--yes-i-know`, mutual-exclusion of `--id`/`--ids`,
  batch loop with per-id `OK:` / `SKIP:` / `FAIL:` progress, dry-run prints
  estimated cost. `_DEFAULT_OUT` now anchored to project dir via
  `pathlib.Path(__file__)` (Phase 1 cwd bug fixed).
- Outputs land as true 384×384 RGBA PNG at `out/<id>.png`; raw API responses
  archived to `out/raw/<id>.jpg` for debugging.

**Phase 2 findings:**
- `rembg` requires the `[cpu]` extra in `pyproject.toml` to bring in
  `onnxruntime` — plain `rembg` crashes at runtime with "No onnxruntime
  backend found".
- First `rembg.remove()` call downloads U2-Net model (~176 MB) to
  `~/.u2net/` — ~5–30s delay on first image only.
- Live cost confirmed steady at exactly $0.06/image (5 paid calls so far,
  all `est_cost_usd: 0.06`).
- Phase 1's `out/0.png` and `out/1.png` are still JPEG-in-PNG and broken —
  not auto-migrated. Run `imagine edit --id 0 --force` (and `--id 1 --force`)
  to reprocess them through Phase 2's pipeline ($0.12 total).

## SHIPPED (2026-05-13)

**All 87 photoreal portraits delivered** to `project/imagine/out/`:
- 79 numbered heads: `0.png`–`75.png`, `109.png`, `111.png`, `112.png`
- 8 variants: `2old.png`, `17old.png`, `47old.png`, `56old.png`, `59old.png`,
  `63old.png`, `25new.png`, `27a.png`

**Final spend: ~$7.02** (117 paid xAI calls × $0.06 each). Includes ~$0.42
wasted on ids 76-82 that turned out to be non-head graphics (since deleted)
plus modest prompt-iteration retries.

**Final pipeline state:**
- Numbered heads via `imagine edit` from cartoon source under
  `jyx2/Assets/BuildSource/head/` with the gender-aware, youth-when-young,
  source-preservation SHARED_STYLE_PROMPT.
- Variants via `imagine variants` using already-generated `out/N.png` as
  source + per-variant suffix (AGE_UP for ~35yo, MAKEOVER for elaborated
  styling, FEMINIZE for the Dongfang Bubai trope).
- Postprocess: rembg `u2net_human_seg` model + center-crop-to-square +
  resize 384×384 LANCZOS + alpha-sharpen (binary alpha via piecewise
  remap, kills halo fog) + RGBA PNG encode.
- Cache: `out/.cache.json` keyed by `(source_hash, prompt_hash)` —
  resumable, free re-runs on unchanged inputs.
- Cost log: `out/.cost.jsonl` — one line per paid call, 117 lines total.

**Subcommands shipped:** `ping`, `edit`, `reprocess`, `variants`.

**Windows env quirk:** if running fresh, set `NUMBA_DISABLE_JIT=1` to
sidestep numba JIT cache write failures under sandboxed Python installs
(needed by `rembg[cpu]` → `pymatting` → numba transitively).

**Open follow-ups (none blocking ship):**
- `out/0.png` and `out/1.png` were not re-rendered under the final
  youthified SHARED prompt — they were generated in early Phase 1 and the
  user accepted them as-is.
- Source character 109 still has a subtle Grok-invented translucent veil
  detail; user accepted current state.
- If Unity integration surfaces edge issues on specific sprites, the
  `reprocess` subcommand can re-cut with the human-seg rembg model at $0.
