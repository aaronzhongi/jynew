# AI Tavern — Offline Lore Pipeline (Phase 3, T3A.6)

A one-time, **offline** map-reduce that scans 《射雕英雄傳》
(`shediao.txt`, 繁體, 4 卷 40 回, ~2.7 MB) with the official **xAI SDK**
(`xai-sdk`) and emits three JSON files the Unity-side importer (T3A.5)
reads to build the AI Tavern static memory spine (World Codex, Bios,
Dossiers).

**You run this, not Claude** — it costs xAI API tokens. The code is
written, validated for shape, and committed; running it is a manual step.

- Output JSON conforms exactly to **`JSON_CONTRACT.md`** (frozen by
  T3A.5). Do not edit the contract; the importer depends on it.
- Output is **简体中文** (the novel is 繁體; the game/bios are 简体). The
  繁→简 conversion is instructed per-call to Grok — the 2.7 MB file is
  **never** pre-converted (Plan §10 Q7).
- Anti-omniscience: everything is bounded to **`CUTOFF_HUI = 10`**
  (回10 「冤家聚頭」, inclusive). This is enforced **structurally** (the
  chapter list is physically sliced before any per-character call) **and**
  by an explicit spoiler fence in every prompt (Plan §3.3).

## Files

| File | Role |
|---|---|
| `config.py` | All tunables + the auditable curated sets (`ROSTER`, `NOTABLE_SET`, `WORLD_SPEC`, `CUTOFF_HUI`, `SPOILER_EXCLUSIONS`, model id). |
| `chunker.py` | Splits `shediao.txt` into 40 回; `chapters_up_to_cutoff()` is the structural cutoff guard. |
| `xai_client.py` | Thin `xai-sdk` chat wrapper. Retry/backoff; `--dry-run` works with no key / no SDK. |
| `extract.py` | The map-reduce: world reduce, per-roster presence-gate + extract map, per-roster reduce. |
| `run.py` | CLI entrypoint. Writes the 3 JSON files into `out/`. |
| `validate.py` | Stdlib-only post-generation check vs. `JSON_CONTRACT.md`. |
| `requirements.txt` | `xai-sdk` only. |
| `out/` | Generated `world.json`, `bios.json`, `dossiers.json` (created on first real run). |

## How to run

**Run from the `tools/` directory** (the package is `tools/lore_pipeline`,
so `python -m lore_pipeline.run` only resolves with `tools/` as the
working dir):

```powershell
cd tools
```

All commands below assume you are in `tools/`.

### 1. Preview cost (no API key, no SDK needed)

```powershell
python -m lore_pipeline.run --dry-run
```

Prints every prompt and a conservative token estimate. Nothing is spent.
Use this to sanity-check the prompts and the cutoff slice before paying.

### 2. Install the SDK + set the key

```powershell
pip install -r lore_pipeline/requirements.txt
$env:XAI_API_KEY = "xai-..."          # bash: export XAI_API_KEY=xai-...
```

> Verify the model id in `config.py` (`MODEL`, default
> `grok-4.20-non-reasoning`) against the current xAI docs
> (<https://docs.x.ai/>) — model ids change. Override without editing
> code via the `JYNEW_GROK_MODEL` env var. Novel path overridable via
> `--novel` or `JYNEW_NOVEL_PATH` (default
> `C:\Users\aaronzhong\Downloads\shediao.txt`).

### 3. Real run

```powershell
python -m lore_pipeline.run                       # all three
# or one stage:
python -m lore_pipeline.run --only world
python -m lore_pipeline.run --only bios
python -m lore_pipeline.run --only dossiers
```

Writes `tools/lore_pipeline/out/{world,bios,dossiers}.json` (UTF-8, no
BOM).

### 4. Validate the contract

```powershell
python -m lore_pipeline.validate --out tools/lore_pipeline/out
```

Stdlib-only structural check against `JSON_CONTRACT.md`. Exit 0 = OK.

### 5. Import into Unity

In the Unity editor: **AI Tavern ▸ Import Lore JSON…** ▸ pick
`tools/lore_pipeline/out/`. The importer (T3A.5):

- **re-writes** `WorldCodex.asset` and each `Dossier_<agentId>.asset`
  (greenfield — no hand-authoring to preserve);
- **patches only 5 Phase 3 fields** of an existing `Bio_*.asset`
  (`Sex / AgeText / Personality / Appearance / SurfaceManner`) — it
  **never** touches `AgentId / RoleId / HeadId / BioName / Identity /
  Plans / Relationships / Interests / StartingItems / SpawnMarkerName`
  (Phase 1/2 hand-authored data). No `Bio_*.asset` for an `agentId` →
  warned-and-skipped (the importer never creates a bio).

## Cost (Plan §3.2)

Measured by `--dry-run` for the current config (2-character roster
huangrong + ouyangke, `CUTOFF_HUI=10`): **33 model calls, ≈373K input +
≈63K output tokens** — one-time, offline. At grok-4-non-reasoning public
pricing (~$2/M in, ~$10/M out — verify current rates) that is on the
order of **$1–2 total**, NOT the "~2M tokens" Plan §3.2 hand-waved (that
estimate assumed mapping the full notable set per-character; the
implementation only maps the 2 roster characters that actually get a
Bio/Dossier — the notable set is just the *targets* inside their
dossiers). Always run `--dry-run` first to see the exact estimate for
your config before spending.

## Why `CUTOFF_HUI = 10`

回10 「冤家聚頭」 (趙王府, 燕京) is the first and defining
黃蓉×歐陽克 contact, and the latest point consistent with *"they barely
know each other; he covets her"* (textual anchor, 回10:
「歐陽克…在趙王府中卻遇到了黃蓉…早已神魂飄蕩…心癢骨軟」). Post-回10
facts (歐陽鋒 逆練九陰真經發瘋 回40; 楊康 身世 as certainty; 歐陽克 之死
回21-22; 黃蓉 任丐幫幫主／打狗棒法 回12+; 九陰真經 周伯通/桃花島試題/竄改
回16-20; the 武功 names 降龍十八掌/打狗棒/九陰白骨爪/一陽指/蛤蟆功 — 黃蓉
doesn't know these by name at 回10) **must not** appear in any dossier.
The chunker physically passes only 回1–10 to per-character extraction;
the prompt fence is the second line of defence.

## Idempotency

Same novel + same `config.py` ⇒ same JSON **shape** (low temperature,
deterministic structure). Re-running and re-importing is safe: the
importer fully re-writes `WorldCodex`/`Dossier_*`, and only patches the 5
bio fields — it never duplicates assets and never clobbers hand-authored
Phase 1/2 bio data.

## Constraints honoured

- **xai-sdk only** — no `openai`, no `requests`/raw HTTP anywhere.
- **Offline only** — nothing under `jyx2/` is touched (no C#, no assets).
- `--dry-run` works with **no API key and no SDK installed**.
- A non-dry run with the key/SDK missing exits with a **clear actionable
  error**, not a stack trace.
