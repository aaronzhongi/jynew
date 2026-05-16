# Phase 3 task decomposition

Plan: `docs/AITavern_Phase3_Plan.md` (v2.1, unanimously approved).
Sub-phases per plan §8: 3A static spine → 3B working memory → 3C affect
decay → 3D consolidation. Per-sub-phase commits (§10 Q6).

Execution model: per-task `coder → task-reviewer` loop (Phase 2 cadence).
The 4 plan reviewers stay alive and are consulted when a task surfaces a
plan deviation or lands in their lane:
- existing-impl `a1d57362dad8fefc0` — code integration / Phase-2 compat
- novel-lore `a46de255df12fe508` — pipeline output / dossier canon
- ai-town `afb6ca04d19355fef` — memory-model deviations
- research `a48c9e261cd7cbae8` — literature deviations

## Phase 3A — static spine

| # | Task | Files | Depends |
|---|---|---|---|
| T3A.1 | Schema: extend `CharacterBio` (Sex enum, AgeText, Personality, Appearance, SurfaceManner); new `WorldCodex` + `CharacterDossier` SOs incl. PersonView{Relationship,Impression,MartialNote,SharedHistory,TheyDoNotKnow}, KnowledgeLine, PolityEntry, FactionEntry, NotableFigure, RelationLine | `CharacterBio.cs` (mod), new `WorldCodex.cs`, `CharacterDossier.cs` | — |
| T3A.2 | Constants: §1-§4 section budgets from plan §7 (`SECT_WORLD_BUDGET`, `SECT_SELFBIO_BUDGET`, `SECT_TALKEE_BUDGET`, `SECT_LONGTERM_BUDGET`). Defer §5/decay/reflect constants to 3B-3D | `AITavernConstants.cs` | — |
| T3A.3 | `ContextAssembler.Build(talker,talkee,mgr,now,profile)` emitting §1-§4 (+ empty overlay placeholder for 3D); anti-omniscience guard (§3 talkee surface-only); `Profile{Full,Leave}` section selection; coexistence wiring — PREPEND assembler static block in `AgentGenerateMessageOp`, retain Phase 2 prior-memory block | new `ContextAssembler.cs`, `ConversationPrompts.cs`, `AgentGenerateMessageOp.cs` | T3A.1, T3A.2 |
| T3A.4 | Editor tests: schema deserialize; assembler section order + omission; anti-omniscience (§3 never leaks talkee Personality/Identity/Plans/Relationships); Leave profile excludes §1/§4; coexistence (Phase 2 block still present) | new test files, `MockGrokClient` reuse | T3A.3 |
| T3A.5 | `AITavern.Editor` asmdef (`includePlatforms:[Editor]`, refs `AITavern.Runtime`) + JSON→ScriptableObject importer (editor menu: read lore JSON dir → write/patch `WorldCodex.asset`, `Bio_*.asset`, `Dossier_*.asset`) | new `Scripts/Editor/` + asmdef + importer | T3A.1 |
| T3A.6 | Offline `tools/lore_pipeline/` (Python, xai-sdk): chunk `shediao.txt` by 回, map-reduce, `CUTOFF_HUI=10`, emit WorldCodex/Dossier/Bio-field/affection-baseline JSON. I write; USER runs (API cost); output committed | new `tools/lore_pipeline/` | T3A.1, T3A.5 |

Order: T3A.1 → T3A.2 → T3A.3 → T3A.4 → T3A.5 → T3A.6. Code+tests land
with hand-authored fixture assets first; the real pipeline (T3A.6, user-
run, token-expensive) generates production assets last. Commit 3A after
T3A.5 (code complete); T3A.6 output is a follow-up data commit.

## Phase 3A — STATUS: DONE & VALIDATED

T3A.1-6 shipped, reviewed, committed (def482edf + lore data eb21c6a07 +
pipeline fixes a739102b8/09acb85b6/e25a3aa2a). In-engine smoke test
confirmed all 4 layers render, anti-omniscience holds live, Phase 2
coexistence intact, dialogue novel-grounded (黄蓉 used 折扇 from §3 +
白驼山 from §4 in-character). Lore data canon-clean after 3-cycle audit.

## Phase 3B — working memory (§5.1 situation / §5.2 task / §5.3 surroundings)

Seam already marked: `ContextAssembler.cs:75` (`// 3B:` ) + plan §2.4,
§4.1, §4.2, §6.1, §8. NO decay, NO reflection, NO Grok in the assembler
(stays synchronous). Frozen-NPC reality: surroundings set once at spawn,
template-rendered, zero Grok calls (plan §10 Q5).

| # | Task | Files | Depends |
|---|---|---|---|
| T3B.1 | Schema: `RuntimeMindState` (Situation/Task/Surroundings subset — Emotion/GlobalReflection/Targets deferred to 3C/3D, declared then) + `SurroundingsModel` POCOs; `CharacterBio` +`DefaultSituation`/`DefaultTask` optional fields | new `RuntimeMindState.cs`, `CharacterBio.cs` | — |
| T3B.2 | `AITavernManager.Minds` (`Dictionary<GameId,RuntimeMindState>`, EnsureInitialized, `GetOrCreateMind`) + `AITavernBoot` wiring: on SpawnNpc create the mind, set Situation/Task (CharacterBio.Default* → fallback to `Plans` prose), set Surroundings structural model (PlaceText + KnownPresent = other talkable agents); frozen ⇒ set-once, template Summary, no Grok | `AITavernManager.cs`, `AITavernBoot.cs` | T3B.1 |
| T3B.3 | `ContextAssembler` §5 block 【眼前局势 — 短期记忆】 (处境/目标/环境) at the 3B seam; **Full profile only** (Leave excludes §5.1-5.3 per §6.1); empty-omission; `SECT_SHORTTERM_BUDGET` | `ContextAssembler.cs`, `AITavernConstants.cs` | T3B.1, T3B.2 |
| T3B.4 | Editor tests: schema; assembler §5 order/omission; Situation/Task Plans-fallback; surroundings structural model + template render + zero Grok; Leave excludes §5.1-5.3; coexistence (§1-§4 + Phase 2 path still intact) | new test file | T3B.3 |

Order T3B.1→.2→.3→.4. Commit 3B after .4. Plan-reviewers monitor:
existing-impl (T3B.2/.3 integration), others on lane-relevant deltas.

## Phase 3C / 3D — decomposed when 3B lands
(3C affect decay; 3D reflection-consolidation, the centerpiece.)
