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

## Phase 3B/3C/3D — decomposed when 3A lands
(Working memory / affect decay / consolidation. Detail TBD post-3A so the
breakdown reflects what 3A actually shipped.)
