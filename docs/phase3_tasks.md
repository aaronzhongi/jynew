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

## Phase 3B — STATUS: DONE

T3B.1-4 shipped, each reviewed (T3B.2 by existing-impl monitor), committed
1b677d9de. §5 working-memory block (处境/目标/环境) live, Full-only,
read-only, frozen-safe (no Grok). SECT_SHORTTERM_BUDGET=4000 landed
(T3A.2 had deferred it).

## Phase 3C — affect with decay (§5.4 emotion + §5.5.1 affection)

Plan §2.4 (Affect POCO), §4.3 (emotion: event-appraisal-primary,
decay-secondary), §4.4 (affection: decays toward CANON baseline, not 0),
§7 (decay constants — deferred from T3A.2/T3B), §8. NO Grok at runtime;
decay is lazy `System.Math.Exp` at read time. 3C builds the decaying
container + baseline seeding + render/omit; the *appraisal that moves
affect on events* is 3D (§5.3 reflection deltas) — until 3D, emotion is
unset (→ §5.4 omitted) and affection sits at baseline.

**PLAN AMENDMENT (flag to ai-town + existing-impl reviewers, §10 Q3):**
Q3 said the affection baseline is an offline-pipeline-emitted scalar
committed beside the dossier. The T3A.6 pipeline/JSON contract never
emitted one. Committed canon already carries per-pair `RelationType`
(Phase 1 `CharacterBio.Relationships` → `RelationshipGraph`, surfaced as
"Your view of X: Enemy"). 3C derives the baseline from a deterministic
`RelationType→[-1,1]` map (no Grok, no pipeline pass, same canon the §4
dossier/identity already reflect). The ai-town reviewer owns the
"decay-toward-canon-baseline" non-negotiable — they bless this source or
we amend Q3.

| # | Task | Files | Depends |
|---|---|---|---|
| T3C.1 | `Affect` POCO (plan §2.4 verbatim — `System.Math.Exp`, pure, no engine ref); extend `RuntimeMindState` +`Emotion` (Affect) and minimal `TargetState{ Affection }` (§5.5.1); 3D fields ReflectionSummary/Ring/ImpressionDelta + `Targets` dict declared as deferred seam comments. Constants: `EMOTION_HALFLIFE_MS`, `EMOTION_FLOOR`, `AFFECTION_HALFLIFE_MS` (plan §7) | `Affect.cs`(new) or in `RuntimeMindState.cs`, `AITavernConstants.cs` | — |
| T3C.2 | Affection-baseline seeding: deterministic `RelationType→[-1,1]` map; `AITavernBoot` post-spawn pass seeds per-(owner,talkee) `TargetState.Affection` (Baseline from `RelationshipGraph`/`bio.Relationships`, `Value=Baseline`, `LastSetMs=now`, `HalfLifeMs=AFFECTION_HALFLIFE_MS`); `Emotion` left unset (no appraisal till 3D). `RuntimeMindState.Targets` dict on the mind keyed by talkee GameId. No Grok. **ai-town + existing-impl monitor (baseline-source amendment).** | `AITavernBoot.cs`, maybe a tiny `AffectBaseline` helper | T3C.1 |
| T3C.3 | `ContextAssembler` §5.4 emotion line (`此刻心绪：…`, omit when `Emotion.Current(now) < EMOTION_FLOOR` or unset) + §5.5.1 per-talkee affection line (`当下好恶：…` via `Targets[talkee].Affection.Current(now)`), inside the §5 block at the 3C seam; Full-only (Leave still excludes); read-only; uses the `now` param already passed to Build | `ContextAssembler.cs` | T3C.1, T3C.2 |
| T3C.4 | Editor tests: `Affect.Current` decay math (monotone→baseline; below-floor omit; reverts to baseline NOT 0); RelationType→scalar map; assembler §5.4/§5.5.1 render+omit; Leave excludes; coexistence; read-only | new test file | T3C.3 |

Order T3C.1→.2→.3→.4. Commit 3C after .4.

## Phase 3D — decomposed when 3C lands
(Reflection-consolidation, the centerpiece: ring buffer §5.5.3, salience
gate, raw-not-decayed deltas, §5.0 global reflection, ImpressionDelta
overlay, the §5.3 op that finally MOVES emotion/affection on events.)
