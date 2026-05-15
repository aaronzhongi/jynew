# AI Tavern — Phase 3 Plan (Layered Human-Like Memory)

## 0. Premise & design philosophy (non-negotiables I will defend)

Phase 2 gave NPCs a flat "all prior transcripts, summarize when over budget"
memory. The user's directive for Phase 3 is to model memory the way a
**human** actually carries it into a conversation, and the user explicitly
endorsed ai-town's *reflection/resummarization* (which Phase 2 deferred)
because re-summarizing lived experience into durable memory **is** how
human memory consolidates. The only ai-town mechanism explicitly rejected
is **embeddings / vector similarity retrieval**.

Cognitive framing (this is the spine of the design; reviewers should argue
*within* it, not against it):

| Human faculty | Phase 3 layer | Source | Mutability |
|---|---|---|---|
| Semantic memory (world facts) | World Codex | novel scan | immutable canon |
| Semantic memory (self) | Talker Bio | novel scan | immutable canon |
| First impression | Talkee Bio (surface) | novel scan | immutable canon |
| Semantic memory (learned knowledge) | Long-Term Knowledge: per-country, per-faction, per-person | novel scan | immutable canon + **runtime overlay delta** |
| Working memory (now) | Short-Term: situation, task, surroundings | designer / runtime | volatile |
| Affect | Short-Term: emotion, per-target affection | runtime | volatile, **decays toward long-term baseline** |
| Episodic buffer | last-10 transcript ring | runtime | volatile, FIFO |
| Consolidation (sleep) | Reflection: episodic → running summary → affection/impression delta | runtime | the bridge volatile→durable |

> **Review Round 1 outcome (4 reviewers): 2 APPROVE (ai-town, research),
> 2 REQUEST CHANGES (novel-lore, existing-impl).** All must-fixes folded
> into this v2; full audit trail in §14. The ai-town and research lenses
> explicitly instructed the plan author to *defend* the five
> non-negotiables below against erosion — they are literature- and
> reference-validated, not arbitrary.

**Five non-negotiables** (state these so reviewers attack the right things):

1. **No embeddings.** Retrieval is structural, not similarity-based — the
   prompt deterministically includes everything relevant to *this talkee*
   plus the running summary plus the last-10 buffer. Grok decides salience
   at read time, exactly as Phase 2 established. (User directive.)
2. **Novel canon is immutable.** The offline scan produces ground truth.
   Runtime never overwrites it. In-playthrough experience is a **separate
   overlay delta** ("canonically 黄蓉 distrusts 欧阳克 — but this run he
   was unexpectedly civil"). Prompt shows canon + delta, clearly labeled.
3. **Static layers are generated offline, committed to the repo, zero
   runtime cost.** A Python pipeline (xai-sdk) scans `shediao.txt` once.
   Runtime only reads ScriptableObjects.
4. **Decay is lazy, timestamp-based, no per-frame tick.** Emotion and
   affection are computed on read from `(lastValue, lastSetTime,
   baseline, halfLife)`. No `Update()` cost, deterministic, testable.
5. **Reflection is consolidation, not retrieval.** It compresses the
   episodic ring into a durable per-pair summary AND emits affection /
   impression deltas. This is the human-memory mechanism the user wants;
   it is the heart of Phase 3, not an optional extra.

**Scope re-evaluation trigger (research-lens, non-blocking, recorded so it
isn't forgotten):** the embedding-free bet is correct *at N≤8 NPCs within
a single 256K window*. If a later phase grows the roster past ~8 or adds
cross-session persistent history that exceeds one context window, the
long-context-vs-RAG literature says this is exactly where embedding-free
breaks (lost-in-the-middle, cost). The decision is explicitly
re-evaluated then — not now.

## 1. The prompt Grok receives (target end-state)

Ordered context assembled by a new `ContextAssembler`. Sections map 1:1 to
the user's spec (their `5.4` collision renumbered: emotion = §5.4,
per-target = §5.5).

```
【世界背景】                                        (§1  World Codex, static)
  时代：南宋宁宗庆元年间，蒙古崛起于漠北，宋抗金、蒙古势盛…
  势力：大宋 / 金 / 蒙古 — 关系：宋金世仇，宋蒙未明…
    （西夏在射雕中无足轻重，不入势力表；如需仅作一句旁注）
  门派：全真教 / 桃花岛 / 白驼山 / 丐帮 / 大理段氏 — 关系：…
  天下知名人物：东邪黄药师(桃花岛)、西毒欧阳锋(白驼山)、
    南帝段智兴—今为「一灯大师」，已退位为僧(大理)、
    北丐洪七公(丐帮)、中神通王重阳(全真,已故,仅追述)…

【我是谁】                                          (§2  Talker Bio, static)
  姓名：黄蓉  性别：女  年龄：约十五  
  性情：灵动机敏、心思缜密、口才锋利、对人有情亦有戒
  出身：桃花岛黄药师之女

【对面是谁 — 初见印象】                              (§3  Talkee Bio surface, static)
  姓名：欧阳克  性别：男  年龄：约二十余  
  外貌：锦衣华服、相貌不俗  
  气度：举止温文、言辞含蜜（仅凭初见可知，不含未亲历的隐情）

【我所知 — 长期记忆】                                (§4  Long-Term Knowledge, static + overlay)
  ·关于势力·
    大宋：…（黄蓉视角额外所知，世界背景之外）
    蒙古：…
  ·关于门派·
    白驼山：地处西域，欧阳锋为主，毒功冠绝…
    全真教：…
  ·关于此人（欧阳克）·
    关系：世交之敌？／ 觊觎者（canon）
    印象：白驼山少主，武功不弱，心术不正，觊觎我已久（canon）
    ［本局所历］：…（runtime overlay delta, omitted if none）

【眼前局势 — 短期记忆】                              (§5  Short-Term, runtime)
  处境：（设计者设定的虚构情境，非小说原文桥段）暂避客栈，追兵在外  (§5.1 situation)
  目标：辨明在场谁可信，伺机取回角落木箱    (§5.2 task)
  环境：低矮客栈，七八桌客人，角落有一口木箱，
        店小二在擦桌；可交谈者：欧阳克、店小二  (§5.3 surroundings, runtime-summarized)
  此刻心绪：警惕（强度 0.6，由先前对话触发，正缓缓平复）  (§5.4 emotion, decaying)

  此间众人，我之所察（跨人反思）：                    (§5.0 per-talker global reflection)
    在场者似都各怀心事，皆回避木箱话题…

  ·对 欧阳克·                                       (§5.5 per-target)
    当下好恶：厌恶偏强（−0.55，本局对话所致，向长期基线缓回）  (§5.5.1 affection, decaying)
    往来印象（已沉淀）：他屡屡试探木箱、回避正面问话…       (§5.5.2 reflection summary)
    对方应不知：我桃花岛底细、木箱真正所藏              (§5.5.4 theory-of-mind, canon-cutoff derived)
    最近交谈（逐字，最多10轮 = 首轮 + 最近9轮）：           (§5.5.3 ring: anchor + last-9)
      欧阳克：蓉儿妹妹，别来无恙…    ← 首轮（钉住不被挤出）
      黄蓉：……
      ［刚刚结束的对话］
      欧阳克：……

【此刻该说什么】                                     (generation instruction tail)
  以黄蓉的身份、性情、当下心绪作答。1-3句，<200字。
  不要复述上面已有内容；无新意则简短收尾。
```

Every section is independently token-capped (see §7). Sections that are
empty (no overlay, no emotion at game start, etc.) are omitted entirely so
a fresh game produces a lean prompt that grows as experience accrues.

## 2. Data model

Four new asset/POCO families. Phase 2's `MemoryStash` is **generalized**,
not discarded (see §5.4).

### 2.1 WorldCodex (ScriptableObject, one global asset, novel-scanned)

```csharp
[CreateAssetMenu(menuName="AI Tavern/WorldCodex")]
public class WorldCodex : ScriptableObject
{
    [TextArea] public string Era;                 // §1 时代
    public List<PolityEntry> Polities;            // 国/势力 + pairwise relations
    public List<FactionEntry> Factions;           // 门派 + pairwise relations
    public List<NotableFigure> NotableFigures;    // "everyone knows" set
}
public class PolityEntry   { public string Name; [TextArea] public string Brief;
                             public List<RelationLine> Relations; }   // Relations[i] = "对<Name>: <text>"
public class FactionEntry  { public string Name; [TextArea] public string Brief;
                             public string HomeRegion; public List<RelationLine> Relations; }
public class NotableFigure { public string Name; public string Polity;
                             public string Faction; [TextArea] public string OneLine; }
public class RelationLine  { public string Target; [TextArea] public string Text; }
```

### 2.2 CharacterBio — extended (existing ScriptableObject, additive fields)

Add the demographic/surface fields the architecture needs (none exist
today; jynew's `Sexual` int per RoleId is the only reusable datum):

```csharp
// existing: AgentId, RoleId, HeadId, BioName, Identity, Plans,
//           Relationships, Interests, StartingItems, SpawnMarkerName
public Sex Sex;                 // enum Male/Female/Other — seed from jynew Character.Sexual
public string AgeText;          // "约十五" — prose, not int (wuxia rarely gives exact ages)
[TextArea] public string Personality;     // §2 性情 (talker self) — distinct from Identity
[TextArea] public string Appearance;      // §3 外貌 — what a STRANGER sees first
[TextArea] public string SurfaceManner;   // §3 气度 — first-impression demeanor only
```

`Identity`/`Plans` stay (Phase 1/2 compatibility). `Personality` is the
*durable trait* slice used in §2; `Appearance`+`SurfaceManner` are the
*first-impression* slice used in §3 when this character is the **talkee**.
Rule enforced by the assembler: §3 emits ONLY `Sex/AgeText/Appearance/
SurfaceManner` — never `Personality`, `Identity`, plans, or relationships,
because a stranger can't perceive those (anti-omniscience guard).

### 2.3 CharacterDossier (ScriptableObject, per roster character, novel-scanned)

The talker's §4 long-term semantic memory.

```csharp
[CreateAssetMenu(menuName="AI Tavern/CharacterDossier")]
public class CharacterDossier : ScriptableObject
{
    public string AgentId;                       // links to CharacterBio.AgentId
    public List<KnowledgeLine> PolityKnowledge;  // §4.1.1 beyond World Codex
    public List<KnowledgeLine> FactionKnowledge; // §4.1.2 beyond World Codex
    public List<PersonView> People;              // §4.2 per-notable-person
}
public class KnowledgeLine { public string Subject; [TextArea] public string Text; }
public class PersonView    { public string Target;            // AgentId or canonical name
                             [TextArea] public string Relationship;  // §4.2.1
                             [TextArea] public string Impression;    // §4.2.2
                             [TextArea] public string MartialNote;   // §4.2.3 武功认知 (novel-lore reviewer: martial-artists banter about 武功; canon at cutoff)
                             [TextArea] public string SharedHistory; // §4.2.4 共历桥段 (e.g. 赵王府宴/软猬甲; replaces an event-graph)
                             [TextArea] public string TheyDoNotKnow; }// §5.5.4 backing: what THIS person canonically doesn't know about the dossier owner, at cutoff回
```

**Scoping decision I will defend:** `People` is bounded to a curated
*notable set* (五绝 + roster + major players the character canonically
knows), NOT every named person in 40 chapters. The novel has hundreds of
minor names; dossiers cover the people that materially shape this
character's worldview. The notable set is an explicit input list to the
pipeline (so it's auditable and stable), seeded from the World Codex
`NotableFigures` + the mod roster.

### 2.4 RuntimeMindState (POCO, per live agent, volatile)

The §5 short-term layer. Lives in `AITavernManager`, never serialized to
an asset (in-process only, like Phase 2's MemoryStash).

```csharp
public class RuntimeMindState
{
    public GameId Owner;
    public string Situation;          // §5.1 designer/test set
    public string Task;               // §5.2 designer/test set
    public SurroundingsModel Surroundings;   // §5.3 runtime-summarized
    public Affect Emotion;            // §5.4 decaying scalar+label toward 平静
    public string GlobalReflection;   // §5.0 per-TALKER cross-person reflection,
                                      //      folded from the union of Targets[*].ReflectionSummary
                                      //      at conversation-end. Flat (no recursion). ai-town-lens add.
    public Dictionary<GameId, TargetState> Targets;   // §5.5 keyed by talkee
}
public class SurroundingsModel {
    public string PlaceText; public List<string> KnownPresent;   // who is here
    public string Summary; public long LastUpdatedMs; }          // re-summarized on新info
public class Affect {                 // emotion AND affection share this shape
    public string Label;              // 平静/警惕/愤怒/喜悦/恐惧 … or like/hate scale label
    public float Value;               // emotion: 0..1 intensity; affection: -1..1
    public float Baseline;            // emotion → 0 ; affection → long-term relation baseline
    public long  LastSetMs;
    public float HalfLifeMs;          // emotion short, affection long (constants §7)
    // System.Math.Exp (double) — NOT UnityEngine.Mathf — so this stays a pure
    // POCO with no engine dependency and matches the long-epoch-ms precision
    // IClock/FakeClock use. (existing-impl-lens fix.)
    public float Current(long now) {
        double dt = now - LastSetMs;          // long-long = long, widened to double
        return (float)(Baseline + (Value - Baseline) * System.Math.Exp(-dt / HalfLifeMs));
    }
}
public class TargetState {
    public Affect Affection;          // §5.5.1
    public string ReflectionSummary;  // §5.5.2  (Phase 2 CompactedSummary generalized)
    public List<TurnRecord> Ring;     // §5.5.3  hard cap = MEMORY_RING_CAP (10)
    public string ImpressionDelta;    // runtime overlay on Dossier.PersonView.Impression
}
public class TurnRecord { public GameId Speaker; public string Text; public long Ms; }
```

## 3. Offline novel pipeline (`tools/lore_pipeline/`)

Pulled forward from the Phase 1 plan's Phase 4 (plan line 635). Python,
`xai-sdk` (per the user's standing rule: xAI work uses the xAI SDK, not
the openai package or raw HTTP). Input `C:\Users\aaronzhong\Downloads\
shediao.txt` (2.7 MB, 繁體, 40 回). One-time, run by the user, outputs
committed to the repo.

### 3.1 Pipeline shape (map-reduce over the book)

1. **Segment** by 回 (chapter). 40 chapters → 40 chunks (~65 K chars each;
   fits a single Grok call with margin).
2. **World pass (reduce):** stream chapter summaries → one consolidation
   call → `WorldCodex` (era, polities, factions, notable figures). Trad→
   Simplified output enforced by prompt.
3. **Per-character map:** for each name in the notable set, for each
   chapter, extract `{appears?, facts, relationship-touchpoints}`. Skip
   chapters where the character is absent (cheap gate first).
4. **Per-character reduce:** consolidate that character's per-chapter
   extracts → `CharacterBio` (Sex/AgeText/Personality/Appearance/
   SurfaceManner) + `CharacterDossier` (polity, faction, per-person
   relationship+impression).
5. **Emit** Unity assets via a JSON → ScriptableObject **editor importer**
   (§10 Q1 resolved: JSON + importer, not hand-emitted YAML). The importer
   needs `UnityEditor`, which `AITavern.Runtime` cannot reference (no
   editor platform; would break player builds). **Deliverable: a new
   `AITavern.Editor` asmdef** (`includePlatforms:["Editor"]`, references
   `AITavern.Runtime`) housing the importer (existing-impl-lens fix — this
   asmdef did not exist and was unstated in v1). Output committed; runtime
   cost zero.

### 3.2 Cost & determinism

~2 M input tokens for the map pass (40 chapters × notable-set), once.
Reduce passes add ~0.5 M. Acceptable one-time offline spend. Pipeline is
re-runnable and idempotent; outputs are reviewed by the novel-lore
reviewer before commit. Prompts pin `temperature` low (factual
extraction) and instruct **Simplified Chinese** output (game/bios are
Simplified; novel is Traditional).

### 3.3 Anti-omniscience at extraction time — `CUTOFF_HUI = 10`

**Setpoint correction (novel-lore reviewer, must-fix #1):** the mod scene
("客栈, on the run, 黄蓉 in beggar-boy disguise, with 欧阳克") is **NOT a
canonical 射雕 scene** — it conflates 黄蓉's beggar disguise (回7-9, paired
with 郭靖, not 欧阳克), 欧阳克's first sight of her (回10 冤家聚頭, 赵王府
燕京, she is in white silk), and a "悦来客栈" that does not exist anywhere
in the 2.7 MB text. This is **fine** because §5.1 situation is *by design*
a designer-authored fiction (the mod is a what-if sandbox, not a novel
retelling). The fix is a separation of concerns, not a scene rewrite:

- **The scene situation (§5.1/§5.2) is explicitly mod-authored fiction.**
  The plan/prompt must label it as such (done in §1 mock:「设计者设定的
  虚构情境，非小说原文桥段」) so the model doesn't try to reconcile it
  with canon it can't find.
- **The static dossier (§4) MUST be pure canon, bounded to `CUTOFF_HUI =
  10` (回10 冤家聚頭, inclusive).** 回10 is the first and defining
  黄蓉×欧阳克 contact and the latest point consistent with "they barely
  know each other; he covets her." (Textual anchor, 回10: 「歐陽克…在趙王
  府中卻遇到了黃蓉…早已神魂飄蕩…心癢骨軟」.)

Pipeline parameter `CUTOFF_HUI = 10`: every per-character extraction call
is fed only 回1–10 and instructed to exclude anything later. The
novel-lore reviewer confirmed the cutoff is **load-bearing** — these
post-回10 facts MUST NOT appear in any dossier (regression-test these):

1. 欧阳锋 goes mad from 逆练九阴真经 (回40 华山论剑).
2. 杨康's parentage *certainty* (回10 hints; treat 黄蓉's knowledge as
   thin/uncertain, not the later established fact).
3. 欧阳克's death (crushed at 桃花岛, 回21-22).
4. 黄蓉 becomes 丐帮帮主 / learns 打狗棒法 (post-回12; she has not met
   洪七公 at 回10).
5. 九阴真经 plot — 周伯通, 桃花岛 试题, 经文 竄改 (回16-20). At 回10 黄蓉
   knows 真经 only as her father's legend, not its contents.
   The cutoff must also bite **武功 knowledge**: at 回10 黄蓉 knows none
   of 降龙十八掌/打狗棒/九阴白骨爪/一阳指/蛤蟆功 *by name* (those are
   post-cutoff 洪七公/欧阳锋 学); `PersonView.MartialNote` must reflect
   only what each has *witnessed by 回10* (e.g. 黄蓉 clocking 欧阳克's
   借力打力/白驼山内功 in the 回10 比武; 欧阳克 seeing her 桃花岛 路数 +
   软猬甲).

## 4. Runtime: short-term state lifecycle

### 4.1 Situation & Task (§5.1/§5.2)
Designer-authored on the `CharacterBio` (new optional `DefaultSituation`/
`DefaultTask` fields) OR injected by a scene script. For testing, a
one-line default is generated from the existing `Plans` prose. Static for
a given scene run; no LLM call.

### 4.2 Surroundings (§5.3)
`SurroundingsModel` updated when the agent perceives something new
(entered a place, a new talkable actor came in range). Update = cheap
structural append (place text + present-actor list). The prose `Summary`
is regenerated by **one Grok call only when the structural set changed**
since `LastUpdatedMs` — not every tick. Frozen-NPC test mode: surroundings
set once at spawn, never changes, zero calls.

### 4.3 Emotion (§5.4) — event-appraisal-primary, decay-secondary
**Research-lens fix:** appraisal theory (Mei et al., PLOS ONE 2024) shows
NPC affect is *event-driven*, not clock-driven; a 90 s half-life left to
run between turns reads as goldfish-memoried. So:
- Emotion is **set by appraisal of events**, then merely *relaxed* by
  decay *between* events. The appraisal that produces `情绪变化` (the §5.3
  reflection slot) is **also runnable per salient turn**, not only at
  conversation-end. "Salient" = a cheap deterministic gate (turn contains
  a detected threat/insult/revelation marker, or the talker is the Phase
  3+ combat path); ordinary chit-chat turns do NOT trigger a per-turn
  appraisal call (cost guard). At game start: no emotion (section omitted).
- Between appraisals, `Current(now)` decays toward `Baseline = 平静/0`,
  half-life `EMOTION_HALFLIFE_MS`. Below `EMOTION_FLOOR` the section is
  omitted (mood "passed"). Decay only ever *relaxes* an event-set value;
  it never originates affect.

### 4.4 Affection (§5.5.1)
- Per target. `Baseline` = the long-term relationship valence derived
  from `CharacterDossier.PersonView` (mapped to a scalar once at load).
- Decays toward that baseline, NOT toward 0 — a person reverts to their
  canonical disposition over time, not to neutral. Half-life =
  `AFFECTION_HALFLIFE_MS` (much longer than emotion).
- Updated by reflection (§5) with a clamped delta.

## 5. Reflection / consolidation (the core mechanism)

This is the human-memory bridge the user explicitly wants. Generalizes
Phase 2's `MemoryCompactor` (we KEEP its proven decisions: one summary per
pair, always re-fold from raw, no summary-of-summary chaining).

### 5.1 Episodic ring (§5.5.3) — anchor + last-9
**Ring-append is co-located with `Conversation.AddMessage`**
(existing-impl-lens R2 fix): the `TurnRecord` is written to
`Targets[talkee].Ring` at the *same call site* that appends the turn to
`conv.Transcript` (in `AgentGenerateMessageOp` / the turn writer), in the
same synchronous step, for BOTH the NPC's generated turn and the player's
submitted turn. This is the invariant that makes §6.1's "assembler is the
sole transcript source" safe: when `BuildContinue` runs, the just-added
in-flight turn is *already* in the ring — so deleting `AppendTranscript`
cannot under-render. (§9 asserts this explicitly: a Continue prompt
contains the most recent turn, present in both `conv.Transcript` and the
ring.)

Every turn appends a `TurnRecord` to `TargetState.Ring`. Hard cap
`MEMORY_RING_CAP = 10`, but the slots are **the FIRST turn of the current
conversation (pinned) + the most recent 9** (ai-town-lens: the opening
accusation of a heated exchange is disproportionately load-bearing and
pure FIFO-10 evicts it by turn 11). On the 11th turn the *oldest non-
anchor* record is evicted into the consolidation spill.

### 5.2 Consolidation trigger & mid-conversation eviction mechanism
Two triggers:
- **Conversation end** — `AgentRememberConversationOp` (Phase 2 op,
  already async, already wired post-`Conversation.Stop`). Consolidates
  the whole exchange + folds `GlobalReflection` (§5.0).
- **Ring overflow mid-conversation** — `AgentRememberConversationOp`
  fires only at conv-end, so it does NOT cover this (existing-impl-lens
  blocker). Mechanism: evicted turns are appended to a per-pair
  **spill list** (the existing `MemoryStash` raw `MemoryEntry` list,
  reused as the spill log) synchronously *at the turn-append site*
  (in `AgentGenerateMessageOp` / the turn writer). The Grok re-fold is
  NOT run mid-conversation (cost + latency); the spill simply accumulates
  raw and the next prompt build's ring still shows anchor+last-9 while
  the spilled-but-not-yet-summarized turns sit in the raw list. The
  conv-end consolidation then re-folds the entire raw spill in one call.
  No turn is lost; no mid-conversation Grok call. (This is the explicit
  ordering the ai-town lens asked be stated.)

### 5.3 Consolidation call (one Grok call at conv-end, re-fold from raw)
Input: prior `ReflectionSummary` is **discarded** as input; re-fold from
the *raw* spill (`MemoryEntry` list) covering this pair (Phase 2 anti-
degradation rule — bounded to one Grok hop). Output is a structured
4-part block:

```
往来印象：<2-4句，沉淀的关系认知，用归属句式（"他声称…"）>
情绪变化：<net effect on emotion: label + delta>     ← derived from RAW turns
好恶变化：<net effect on affection: signed delta -1..+1> ← derived from RAW turns
违背设定：<空 OR a flag if anything contradicts canon>   ← drift tripwire
```

**Critical (ai-town-lens must-fix):** `情绪变化` / `好恶变化` are computed
by the model **from the raw turn text**, NOT from the current decayed
`Affect.Current(now)`. A dramatic mid-conversation beat must survive into
the durable summary even if its *mood* already decayed below floor before
consolidation runs. This is a §9 regression test.

The op then:
- writes `ReflectionSummary` (§5.5.2) from 往来印象;
- **salience gate (ai-town-lens):** apply the `情绪变化`/`好恶变化` deltas
  to `Emotion`/`Affection` ONLY if the exchange was non-trivial — i.e.
  skip the delta application (NOT the summary fold) when `好恶变化 ≈ 0`
  (|δ| < `AFFECT_DELTA_DEADBAND`) AND `情绪变化` label is neutral. This
  stops chit-chat from perpetually resetting `LastSetMs` so the
  baseline-reversion decay (§4.4) can actually fire for a chatty pair.
  Costs zero extra Grok calls (it reads the already-returned slots).
- when applied: set `Emotion.Value`/`Affection.Value`, `LastSetMs = now`;
- appends to `ImpressionDelta` (§4 overlay) — never the immutable Dossier;
- if `违背设定` is non-empty, log `[Reflect] canon-contradiction <text>`
  (drift tripwire for licensed IP; non-fatal).

### 5.3.1 Global (cross-person) reflection (§5.0) — ai-town-lens add
At conversation-end, after per-pair consolidation, one additional fold:
`GlobalReflection` = a single ≤2-sentence summary folded from the **union
of this talker's `Targets[*].ReflectionSummary`** (NOT raw turns — these
are already-distilled). Flat, no recursion (preserves the anti-degradation
rule). Captures "everyone here is dodging the box" — the cross-person
inference a strictly per-pair fold structurally cannot produce. Rendered
as §5.0, above the per-target blocks. One extra Grok call per
conversation-end, only when the talker has ≥2 non-empty per-pair
summaries (else the global slot just mirrors the single pair — skip).

**Cross-pair staleness is intentional (ai-town-lens R2 clarification):**
the global fold reads the *current* state of all `Targets[*]
.ReflectionSummary`; only the just-finished pair's summary was refreshed
this conv-end, the others are as of their last interaction. This is
correct — it mirrors "what I last gathered about everyone," not an
omniscient resync. `GlobalReflection` is **never an input to any per-pair
fold** (no feedback loop, no recursion). A future implementer must NOT
"fix" the staleness by force-refolding all pairs — that reintroduces the
cost and degradation the salience gate and re-fold-from-raw rules exist
to prevent.

This is "sleep consolidation": volatile episodic experience compresses
into durable disposition; the raw buffer is freed.

### 5.4 MemoryStash generalization — honest scope (existing-impl-lens fix)
The `CompactedSummary` **struct** survives and its `SummaryText` field
backs `ReflectionSummary`; the raw `MemoryEntry` list is reused as the
spill log the re-fold reads. **What does NOT survive:** `MemoryCompactor`'s
*body* — the 20k-char budget projection, the `FacetSlotPrefixes`
(4-slot 关系/共同经历/未解矛盾/关键事实) parser, and `BuildSystemPrompt`/
`BuildUserBody` — is **replaced**, not "reused verbatim," by the §5.3
4-slot (往来印象/情绪变化/好恶变化/违背设定) prompt + parser. The
re-fold-from-raw *discipline* and the per-pair-single-summary *invariant*
carry over; the prompt/trigger/parse code is rewritten in sub-phase 3D.
Consequently the **Phase 2 tests that assert `FacetSlotPrefixes` and the
old `BuildPriorMemoryBlock` 4-slot output are REWRITTEN in 3D, not
inherited** (they will fail when the prompt changes — that is expected and
scheduled, not a regression). No serialized-data migration is needed
(Phase 2 memory is in-process only; Phase 2 plan §13 documented the lossy
boundary).

## 6. ContextAssembler (replaces flat BuildIdentityBlock)

New `ContextAssembler.Build(talker, talkee, mgr, now)` returns the §1-§5
ordered string. `ConversationPrompts.BuildStart/Continue/Leave` call it
instead of `BuildIdentityBlock` + the Phase 2 prior-memory block. The
Phase 2 anti-repeat tail (per-type instruction) is preserved. Each section
is a private builder; empty sections are skipped; each section is
truncated to its own char budget (§7). Deterministic, synchronous, no
Grok call (all Grok work happened offline or in the reflection op).

### 6.1 Transcript ownership — no double-render (existing-impl-lens blocker)

The single highest-risk integration point. Phase 2 `BuildContinue` does
`BuildIdentityBlock` + `AppendPriorMemoryBlock` + `"\nConversation so far:
\n"` + `AppendTranscript(conv.Transcript)` (the *live in-flight*
transcript). Phase 3's §5.5.3 ring ALSO renders recent turns. If both
stay, the current exchange renders twice.

**Resolution — the assembler is the SOLE owner of all transcript
rendering.** Per builder:
- `BuildStart`: assembler emits §1-§5; ring is empty for a brand-new
  pair → §5.5.3 omitted. No `AppendTranscript`.
- `BuildContinue`: **`AppendTranscript(conv.Transcript)` is DELETED.**
  The live exchange is in `Targets[talkee].Ring` (every turn is appended
  there — §5.1); the assembler's §5.5.3 renders anchor+last-9 with the
  `[刚刚结束的对话]` tag on the newest. Single source of truth.
- `BuildLeave`: a <50-char farewell does NOT need the full §1-§5 (routing
  the whole ~15k-char context into a one-sentence goodbye is the cost
  regression Phase 2 deliberately avoided — existing-impl-lens R2). It
  uses a **lean Leave profile**: §2 (self bio — stay in voice) + §5.4
  (current emotion) + §5.5.3 (the ring — what just happened, so the
  farewell is coherent). NO §1 World Codex, NO §4 long-term knowledge,
  NO §5.0 global reflection. `ContextAssembler.Build` takes a
  `profile` arg (`Full` for Start/Continue, `Leave` for Leave) selecting
  which sections emit. It still does NOT separately `AppendTranscript`
  (the ring is the sole transcript source here too).

Invariant the §9 tests assert: for any (Start/Continue/Leave) the live
conversation's turns appear **exactly once** in the assembled prompt
(via §5.5.3), never via a second `AppendTranscript` path.

### 6.2 Anti-omniscience guard lives here
§3 (talkee surface) pulls ONLY `Sex/AgeText/Appearance/SurfaceManner`
from the talkee's bio — never `Personality`/`Identity`/`Plans`/
`Relationships`. §4 pulls the talker's Dossier view *of the talkee*
(canon + that talker's runtime `ImpressionDelta`) — never the talkee's
own private bio. §5.5.4 「对方应不知」 is sourced from the talkee's
`PersonView.TheyDoNotKnow` (canon, cutoff-bounded) so the talker doesn't
reference things the talkee couldn't know (research-lens ToM add).

## 7. Constants (append to AITavernConstants.cs)

```csharp
// Phase 3 — layered memory section budgets (chars).
public const int SECT_WORLD_BUDGET   = 1500;
public const int SECT_SELFBIO_BUDGET = 600;
public const int SECT_TALKEE_BUDGET  = 400;
public const int SECT_LONGTERM_BUDGET= 3000;
public const int SECT_SHORTTERM_BUDGET=4000;   // situation+task+surroundings+emotion
public const int SECT_PERTARGET_BUDGET=6000;   // affection+reflection+ring

public const int MEMORY_RING_CAP = 10;          // §5.5.3 last-N turns

// Decay (lazy, exp). Tunable; defended in review.
public const float EMOTION_HALFLIFE_MS   = 90_000f;   // ~1.5 min
public const float EMOTION_FLOOR         = 0.12f;     // below → omit §5.4
public const float AFFECTION_HALFLIFE_MS = 900_000f;  // ~15 min, reverts to canon baseline

// Reflection call
public const int   REFLECT_MAX_TOKENS = 500;
public const float REFLECT_TEMPERATURE = 0.3f;

// Salience gate (§5.3): below this |好恶变化|, AND neutral 情绪变化 label,
// the affect deltas are NOT applied (summary is still folded). Stops
// chit-chat from perpetually resetting the decay clock.
public const float AFFECT_DELTA_DEADBAND = 0.08f;
```

## 8. Internal sub-phasing (plan covers all; implement in order)

- **3A — Static spine.** WorldCodex + extended CharacterBio +
  CharacterDossier schema; offline `tools/lore_pipeline/`; `AITavern
  .Editor` asmdef + JSON→SO importer; ContextAssembler §1-§4 (static
  only). **Coexistence rule (existing-impl-lens fix):** in 3A the
  assembler's §1-§4 static block is *appended alongside* the retained
  Phase 2 `BuildPriorMemoryBlock` memory block — the Phase 2 memory path
  is NOT removed until 3D replaces it with the §5.5.3 ring. This
  guarantees **no memory-continuity regression** across 3A-3C. 3A ship:
  NPCs talk with full novel-grounded world/self/other/long-term context
  PLUS their existing Phase 2 memory. The call-site swap in
  `AgentGenerateMessageOp` is "prepend assembler static block", not
  "replace prior-memory block".
- **3B — Working memory.** RuntimeMindState; §5.1 situation, §5.2 task,
  §5.3 surroundings; assembler §5 (static affect). No decay, no reflection.
- **3C — Affect with decay.** Emotion + per-target affection; lazy
  `Current()` decay; assembler renders/omit by floor.
- **3D — Consolidation.** Replace MemoryCompactor body → §5.3 reflection
  op (4-slot prompt, salience gate, raw-not-decayed deltas); ring buffer
  §5.5.3 (anchor+last-9, mid-conv spill); §5.0 global reflection;
  ImpressionDelta overlay; wire affect updates. **Rewrite the Phase 2
  tests** asserting `FacetSlotPrefixes` / old 4-slot `BuildPriorMemory-
  Block` (scheduled, not a regression — see §5.4). Remove the Phase 2
  memory block now superseded by the ring (ends the 3A coexistence).

Each sub-phase is independently smoke-testable and committable. 3A is the
big one (offline pipeline). 3D reuses the most Phase 2 code.

## 9. Tests

Editor-mode NUnit, same harness as Phase 1/2. Highlights:
- `WorldCodex` / `Dossier` deserialize; assembler emits sections in
  fixed order; empty sections omitted.
- Anti-omniscience: §3 for talkee NEVER contains `Personality`/`Identity`/
  `Plans`/`Relationships` of the talkee.
- **No-double-render AND no-under-render (existing-impl-lens):** for
  Start/Continue/Leave the live conversation's turns appear in the
  assembled prompt **exactly once** (via §5.5.3), never via a second
  `AppendTranscript`. The companion **under-render** assertion: after a
  turn is added via the co-located ring-append (§5.1), a subsequent
  `BuildContinue` prompt CONTAINS that most-recent turn — i.e. the turn
  is in both `conv.Transcript` and the ring at prompt-build time (guards
  the §6.1 `AppendTranscript` deletion from silently dropping the
  in-flight turn).
- **Leave lean profile:** a `BuildLeave` prompt contains §2+§5.4+§5.5.3
  only — it does NOT contain the §1 World Codex or §4 long-term knowledge
  (asserts `ContextAssembler` honors the `Leave` profile arg).
- Decay math: `Current()` monotone toward baseline; emotion below floor →
  omitted; affection reverts to canon baseline not zero.
- Ring: anchor (first turn) is pinned; 11th turn evicts oldest *non-
  anchor*; eviction spills to the raw list synchronously at append site
  (mid-conv, no Grok call).
- **Raw-not-decayed deltas (ai-town-lens must-fix):** a dramatic turn +
  *delayed* consolidation (emotion already decayed below floor) → the
  `ReflectionSummary` still captures the event AND `情绪变化`/`好恶变化`
  are computed from the raw turns, not from `Affect.Current(now)`.
- **Salience gate:** a trivial chit-chat exchange folds the summary but
  does NOT apply affect deltas / does NOT reset `LastSetMs` (so the
  baseline-reversion decay still fires for a chatty pair).
- **Global reflection (§5.0):** with ≥2 non-empty per-pair summaries, a
  cross-person `GlobalReflection` is produced and rendered above §5.5;
  with <2 it is skipped.
- Reflection: re-folds from raw (prior summary NOT in Grok input —
  inherits the Phase 2 discipline test, rewritten to the 4-slot prompt);
  appends `ImpressionDelta`; never mutates the Dossier asset; logs on
  `违背设定` non-empty.
- Pipeline: a tiny fixture (1 fake chapter) → assert WorldCodex/Dossier
  JSON shape; `CUTOFF_HUI=10` respected (a planted post-回10 fact in the
  fixture does NOT appear in the emitted dossier).

## 10. Resolved decisions (was "open questions"; Round 1 closed all)

| # | Question | Resolution |
|---|---|---|
| Q1 | Pipeline emits `.asset` YAML vs. JSON + importer? | **JSON + `AITavern.Editor` importer.** (existing-impl-lens confirmed an `AITavern.Editor` asmdef is required and was added to §3.) |
| Q2 | Story cutoff回 | **`CUTOFF_HUI = 10` (回10 冤家聚頭, inclusive).** Set by novel-lore reviewer with textual anchor; the mod scene situation is explicitly mod-authored fiction, decoupled from the canon dossier (§3.3). |
| Q3 | Affection baseline (prose → scalar) | **One offline call per (character,target) emits baseline ∈[-1,1] alongside the dossier**, committed; runtime never recomputes. |
| Q4 | Notable-person set for §4.2 | **Novel-lore reviewer's corrected, cutoff-bounded list** (not the v1 ~25-40). Must-add 黄药师 + 欧阳锋 (first-order to both speakers); exclude 洪七公/一灯大师/江南七怪 (not met by 回10); 江南七怪 as 听闻-only. Curated list is an auditable pipeline input. |
| Q5 | §5.3 surroundings: model vs. template | **Template until ≥2 dynamic facts, then one Grok call.** Frozen-NPC test mode never calls. |
| Q6 | One mega-commit vs. per-sub-phase | **Per-sub-phase commits (3A/3B/3C/3D).** |
| Q7 | Trad→Simp | **Instruct per-call** (繁→简 maps 1:1 on all load-bearing names — novel-lore reviewer spot-checked 黃蓉/歐陽鋒/歐陽克/黃藥師/軟猬甲; no pre-converted artifact). |

## 11. Migration / rollback

Additive at the data layer. New assets are new files; extended
`CharacterBio` fields default empty (Phase 1/2 `.asset` files still
deserialize — Unity default-inits new serialized fields; assembler falls
back to `Identity` when `Personality` empty — the ONLY compat concern,
confirmed by existing-impl-lens). `RuntimeMindState` is in-process only
(no save format yet — same boundary Phase 2 §13 documented). Rollback =
revert commits; orphaned new `.asset` files are inert (no Phase 2 reader).
**`MemoryStash` data is backward-compatible** (`CompactedSummary` struct
+ raw `MemoryEntry` list reused, no serialized schema break) — but the
*code* path is not: the §5.3 4-slot prompt/parser replaces
`MemoryCompactor`'s 4-facet body and the Phase 2 prompt/parse tests are
rewritten in 3D (scheduled, see §5.4). This is a code change, not a data
migration.

## 12. What this is NOT (scope fence)

- Not combat. The Phase 1 plan's "Phase 3 = hostility-triggered combat +
  death drop + faction allies" is **renumbered to Phase 5**; this Phase 3
  is the *memory* system. (The novel-lore pipeline, originally "Phase 4",
  is folded into this Phase 3's §3.) Net phase order going forward:
  P3 = memory (this), P4 = combat/death/faction, P5+ = scale/polish.
- Not embeddings / vector recall (user directive, §0 non-negotiable #1).
- Not multi-novel (only 射雕; jynew's cross-novel cast is out of scope —
  the roster is 射雕-only).
- Not online lore (the pipeline is offline, one-time, committed).

## 13. Why this is the right design (stand-my-ground summary)

The user's spec **is** the Stanford "Generative Agents" memory model
(observation → reflection → plan) refined with a static semantic spine
sourced from canon — which is exactly where the 2024-2026 literature on
believable NPCs has converged. The two deliberate departures from ai-town
are *correct for this game*: (a) no embeddings, because at 2-8 NPCs with
canon-bounded knowledge the retrieval problem is small and Grok's long
context subsumes it; (b) immutable canon + runtime overlay, because a
licensed-IP character must not drift off-model — a constraint ai-town
(original sandbox characters) never had. Reflection/consolidation is
retained and made central precisely because it is the mechanism by which
lived play becomes durable character — the thing the user asked for.

## 14. Review Round 1 audit trail (v1 → v2)

4 reviewers, 4 lenses. Verdicts: ai-town **APPROVE**, research
**APPROVE**, novel-lore **REQUEST CHANGES**, existing-impl **REQUEST
CHANGES**. Every item and its disposition:

### Novel-lore (REQUEST CHANGES) — all ADOPTED
| Item | Disposition |
|---|---|
| Setpoint "悦来客栈/beggar-boy/欧阳克" is non-canonical conflation | ADOPTED §3.3 — scene = explicitly mod-authored fiction; dossier = pure canon; separation of concerns, no scene rewrite |
| 西夏 absent; 南帝→一灯大师; 中神通 dead | ADOPTED §1 mock |
| Cutoff = 回10 冤家聚頭 (textual anchor) | ADOPTED `CUTOFF_HUI=10` §3.3 |
| 5 post-回10 spoilers to exclude (+武功 names) | ADOPTED §3.3 list + §9 regression test |
| Notable-set correction; must-add 黄药师+欧阳锋 | ADOPTED §10 Q4 |
| Add `MartialNote` + `SharedHistory` to PersonView | ADOPTED §2.3 (+ `TheyDoNotKnow` for the ToM line) |
| Trad→Simp safe | ACK §10 Q7 |

### Existing-impl (REQUEST CHANGES) — all ADOPTED
| Item | Disposition |
|---|---|
| §5.4 "no schema break / reuse verbatim" optimistic | ADOPTED §5.4 rewritten — struct survives, body replaced, Phase 2 tests rewritten in 3D (scheduled, not regression) |
| §6 double-render (ring vs `AppendTranscript`) | ADOPTED §6.1 — assembler is sole transcript owner; `BuildContinue` drops `AppendTranscript`; §9 exactly-once test |
| §8 3A not independently shippable | ADOPTED §8 — explicit coexistence rule (assembler prepended; Phase 2 memory retained until 3D); no continuity regression |
| `Affect.Current` needs engine ref | ADOPTED §2.4 — `System.Math.Exp` (double), pure POCO |
| Mid-conv ring-overflow has no hook | ADOPTED §5.2 — synchronous raw spill at append site, no mid-conv Grok call |
| No `AITavern.Editor` asmdef for importer | ADOPTED §3 step 5 — new asmdef is a deliverable |
| Decay math type-safe / RuntimeMindState fits / IVT exists | ACK (no change needed) |

### ai-town (APPROVE) — strong-recommends ADOPTED, endorsements DEFENDED
| Item | Disposition |
|---|---|
| No cross-target/global reflection | ADOPTED §5.0/§5.3.1 — flat per-talker global fold |
| Over-consolidation of trivial chatter | ADOPTED §5.3 salience gate (`AFFECT_DELTA_DEADBAND`) |
| Decay-before-consolidate ordering | ADOPTED §5.3 — deltas from RAW turns not decayed value; §9 test |
| FIFO-10 vs recency: pin first turn | ADOPTED §5.1 — anchor + last-9 |
| DEFEND: no-embeddings / re-fold-from-raw / canon+overlay / decay-to-baseline / attribution句式 | KEPT as §0 non-negotiables; §14 records they are reviewer-endorsed, not to be eroded |

### Research (APPROVE) — gaps ADOPTED, strengths DEFENDED
| Item | Disposition |
|---|---|
| Emotion event-appraisal-primary, decay-secondary | ADOPTED §4.3 — per-salient-turn appraisal; decay only relaxes |
| Cheap ToM line "对方应不知" | ADOPTED §5.5.4 + `PersonView.TheyDoNotKnow` |
| Canon-contradiction tripwire | ADOPTED §5.3 `违背设定` slot + log |
| Re-evaluate embedding-free beyond N≤8/single-window | ADOPTED §0 note |
| DEFEND: observation→reflection→disposition / memory-as-compression / canon-overlay / long-context@N≤8 / attribution | KEPT; reviewer-endorsed |

**Not adopted (deliberately, with reason):** A-MEM linked notes, EM-LLM
surprise segmentation, hierarchical/temporal memory trees, recursive
reflection — all solve *scale* (1M+ notes, many entities) a 2-NPC tavern
does not have; adopting them is the over-engineering both the research and
ai-town lenses explicitly warned against. The §5.4 flat simplification is
the correct scope decision and is reviewer-endorsed.

### Review Round 2 (v2 → v2.1)

Verdicts: novel-lore **APPROVE** (all 7 resolved), ai-town **APPROVE**
(all 4 resolved), research **APPROVE** (all 3 + non-blocking resolved),
existing-impl **REQUEST CHANGES** (2 narrow lane-internal fixes). R2
items, all ADOPTED into v2.1:

| Lens | Item | Disposition |
|---|---|---|
| existing-impl | "no-double-render" could silently become "no-render": ring-append site vs `conv.AddMessage` unpinned | ADOPTED §5.1 — ring-append co-located with `Conversation.AddMessage`, same synchronous step; §9 under-render assertion added |
| existing-impl | `BuildLeave` routed through full §1-§5 inflates a <50-char farewell ~15k chars | ADOPTED §6.1 — lean Leave profile (§2+§5.4+§5.5.3 only); `ContextAssembler.Build` gains a `profile` arg; §9 test |
| ai-town | global-fold cross-pair staleness should be stated as intentional / no feedback loop | ADOPTED §5.3.1 — explicit "staleness is intentional; GlobalReflection never an input to any per-pair fold; do not force-refold" |

R2 introduced no new must-fix from the three APPROVE lenses.
