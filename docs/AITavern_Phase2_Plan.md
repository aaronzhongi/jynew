# AI Tavern — Phase 2 Plan (Context-Based Memory + Tail Compaction)

## 1. Why this looks different from the original Phase 2

The Phase 1 plan §582 described Phase 2 as the full ai-town memory pipeline:
embeddings + importance scoring + ranked recall + reflection. That design
was authored when context windows were small and ai-town's `memory.ts`
ranking math was the SOTA. **Grok-4 ships with a 256K-token context
window**, which changes the cost/benefit:

- A typical 8-message NPC conversation transcript is ~600 tokens.
- 50 such conversations is ~30K tokens — 12 % of the budget.
- The 2-NPC tavern roster generates conversations slowly enough that the
  budget is not a practical limit for normal play.

So Phase 2 swaps the embedding + ranking pipeline for the simpler approach
modern chatbots actually use: **dump every prior transcript for the pair
into the system prompt verbatim; only summarize when the prompt would
otherwise blow past a token budget**. Grok reads the full history and
implicitly decides what's relevant — no embedding, no importance score, no
ranking math.

Phase 2 also drops explicit reflection. With full transcript context every
turn, Grok can synthesize "how does this character feel about the other
now" from the raw conversations without a separate reflection memory.

## 2. Scope

In scope:
1. Replace `BuildPriorMemoryBlock`'s "last 2 transcripts" cap with "all
   transcripts for this pair, in chronological order".
2. Add a character/token budget (`MEMORY_CONTEXT_BUDGET_CHARS`).
3. When the would-be memory block exceeds the budget, fold the oldest
   transcripts into a single Grok-generated summary block. Cache the
   summary on the `MemoryStash` so subsequent reads don't regenerate it.
4. Wire the existing `AgentRememberConversationStub` (currently a no-op
   that just clears `ToRemember`) into a real op that triggers the
   compaction check after each conversation ends.

Out of scope (deferred to Phase 2.5 or dropped):
- Embeddings (no xAI endpoint; local ONNX not worth the dependency at
  current scale).
- Per-memory importance score.
- Three-term `relevance + importance + recency` ranking.
- Explicit Reflection memories.
- Phase 3 death records (already speced).

## 3. Data model

### 3.1 MemoryEntry — minimal additive change

Add one nullable field:

```csharp
public class MemoryEntry
{
    public MemoryType Type;
    public GameId Owner;
    public GameId? Target;
    public string PairKey;
    public string Description;     // raw transcript (verbatim) — kept even after folding so we can re-fold from raw next compaction
    public long EndedAt;
    public int? Importance;        // Phase 3 (death records) still uses this; Phase 2 leaves null for Conversation entries
    public bool IsFolded;          // NEW: true once this transcript is represented by the per-pair CompactedSummary
}
```

`IsFolded == true` means "this entry's content is now represented by the
single `CompactedSummary` for this pair; do NOT inject this entry
verbatim into the prompt". The raw `Description` stays so the next
compaction can re-fold from raw transcripts (see §3.2) and so a future
Phase 2.5 (e.g. importance back-fill) can re-examine it.

**Compaction is restricted to `MemoryType.Conversation`.**
`MemoryType.Relationship` (Phase 3 death records, `Importance = 9`) and
`MemoryType.Reflection` (deferred) are exempt — they don't carry a
`Description` worth folding, and the high-importance signal must survive
intact.

### 3.2 New CompactedSummary type — exactly one per pair

```csharp
public class CompactedSummary
{
    public string PairKey;                // canonical "A|B"
    public GameId OwnerA;                 // forward-search (PairKey is the source of truth)
    public GameId OwnerB;
    public string SummaryText;            // Grok-generated; see §8 for facet-slot format
    public long CoveredFromMs;            // EndedAt of the oldest folded entry
    public long CoveredUntilMs;           // EndedAt of the newest folded entry
    public int FoldedEntryCount;          // total folded entries this summary represents
    public long CreatedAt;                // when this summary was regenerated last
}
```

**One `CompactedSummary` per pair, overwritten on each compaction.**
(Plan v1 originally proposed chaining summaries; the architecture reviewer
flagged that summary-of-summary degrades quality after 3-4 iterations.)
When compaction fires and a summary already exists, the new summary's
Grok input is `<all folded MemoryEntry.Description blobs> +
<newly-folded transcripts>` — NOT the previous summary text. Information
loss is bounded to exactly one Grok hop regardless of compaction count.

### 3.3 MemoryStash gains a parallel store

```csharp
public class MemoryStash
{
    public readonly List<MemoryEntry> Entries = ...;          // unchanged
    // Keyed by canonical PairKey. At most one entry per pair (overwrite on compaction).
    public readonly Dictionary<string, CompactedSummary> Summaries = ...;  // NEW

    public CompactedSummary GetSummary(GameId a, GameId b);
    public void SetSummary(CompactedSummary s);
}
```

## 4. Read path — BuildPriorMemoryBlock

The old "Earlier today" framing conflicts with summaries that may cover
much older material. New layout, all-Chinese (the content is Chinese,
matching content language reduces translate-then-generate overhead):

```
你与 <other.BioName> 的过往：

[较早｜摘要]
<CompactedSummary.SummaryText>      // omitted if no summary exists

[较近｜逐字]
<oldest un-folded transcript>
---
<next un-folded transcript>
...
[刚刚结束的对话]
<newest un-folded transcript>        // the last one is explicitly marked
```

The anti-repeat guard is REMOVED from this block (was at the tail in
Phase 1). It moves into the per-type instruction tail of `BuildStart` /
`BuildContinue` where the model generates from — see §4.1.

Algorithm:
1. Look up `Summaries[PairKey]` for this `(self, other)`. If present,
   emit the `[较早｜摘要]` block; otherwise skip it.
2. Collect all `Entries` for this pair with `IsFolded == false` and
   `Type == MemoryType.Conversation`, in `EndedAt` ascending order.
3. Emit the summary block, then the un-folded transcripts. Tag the LAST
   un-folded transcript with `[刚刚结束的对话]` instead of `---` so the
   model treats the most recent exchange with the highest fidelity
   (counter-acts long-context recency dip).
4. **Read-side soft-truncate safety net**: if the rendered character
   count exceeds `MEMORY_CONTEXT_BUDGET_CHARS × 1.5` (the budget × 1.5
   guard means compaction has failed repeatedly), deterministically drop
   the oldest un-folded transcripts one at a time until the block fits,
   and prepend `（更早的对话因故未能保留）` so the model knows there's
   missing history. No Grok call, no race — purely a deterministic
   write-side fallback to keep the chat call from blowing the 256K
   context.
5. If both `Summaries[PairKey]` is null AND there are no un-folded
   entries, return `null` (caller drops the block entirely, no header
   line for a brand-new pair).

Read path remains synchronous and cheap. Compaction (Grok call) stays
exclusively a write path concern (§5).

### 4.1 Anti-repeat guard moves to per-type instruction tail

`BuildStart` and `BuildContinue` already have a tail of
generation-instruction copy ("Reply in 1-3 sentences..."). Inject the
anti-repeat reminder there, next to the active generation instruction,
not at the end of the priorMemory block. Concretely:

`BuildStart` tail:
```
This is the beginning of your conversation. Stay in character.
Reply in 1-3 sentences, under 200 Chinese characters.
Do not narrate actions — only speak.
不要复述上面已有的对话内容；如无新话题可谈，简短礼貌告辞即可。
```

`BuildContinue` tail similarly appends the Chinese line.

Rationale: with N>2 transcripts above, a single anti-repeat line at the
end of the priorMemory block sits too far from the generation point.
Placing it adjacent to "Reply in 1-3 sentences" keeps it in the model's
focus.

## 5. Write path — MemoryCompactor

A new static class `Jyx2.AITavern.MemoryCompactor` with one entry point:

```csharp
public static async UniTask<bool> MaybeCompactForOwner(
    AITavernManager mgr, GameId owner, GameId other, long now);
```

Returns `true` if a summary was generated, `false` if no compaction was
needed (or it errored out and bailed).

Algorithm:
1. Compute the rendered character count of what
   `BuildPriorMemoryBlock(owner, other)` would emit right now (existing
   summary + un-folded transcripts + headers). Cheap — same code path as
   read, no Grok call.
2. If `<= MEMORY_CONTEXT_BUDGET_CHARS`, return `false`.
3. Otherwise, decide which entries to fold:
   - **Always re-fold from raw.** Gather ALL entries for this pair with
     `Type == MemoryType.Conversation` (regardless of current `IsFolded`
     flag), oldest-first. The existing summary (if any) is discarded —
     we re-summarize from raw transcripts.
   - Pick the oldest entries to fold until the projected post-compaction
     block (one summary block at upper-bound ~500 chars + remaining
     un-folded transcripts + headers) fits under
     `MEMORY_CONTEXT_BUDGET_CHARS × MEMORY_COMPACTION_TARGET_FRACTION`
     (60 % target so a single new conversation doesn't immediately
     re-trigger compaction). The 500-char upper bound for the summary
     comes from `MEMORY_SUMMARY_MAX_CHARS` (§7).
   - Always fold at least 1 entry; if even folding the oldest doesn't
     get under the target, fold more.
4. Build a Grok call to summarize the selected entries — see §8 for the
   exact prompt structure.
5. On success: write the new `CompactedSummary` to
   `mgr.Memory.Summaries[PairKey]` (overwriting any prior summary for
   this pair). Mark each selected entry `IsFolded = true`. Verify the
   actual rendered size after the new summary is in place; if it's
   STILL over `MEMORY_CONTEXT_BUDGET_CHARS`, fold more entries (run the
   selection step again) without making a second Grok call — the
   existing summary will be regenerated on the next compaction tick
   anyway.
6. On Grok failure (HTTP error, empty response, whitespace, parse-fail
   on the §8 facet format): log a warning, return `false`. Entries stay
   un-folded; the read-side soft-truncate (§4 step 4) catches the
   budget overflow. Next conversation end retries compaction.

Compaction call is `async` and runs from `AgentRememberConversationOp`
(§6). It does NOT block the current conversation's `Stop` — that's
already complete by the time `Remember` fires.

## 6. AgentRememberConversationStub → AgentRememberConversationOp

Currently the stub just clears `agent.ToRemember`. Phase 2 makes it real.
The 5-second EndedAt heuristic from plan v1 is dropped in favor of a new
`Agent.ToRememberPartner` field that `Conversation.StopWithManager`
populates alongside `ToRemember = convId`. Direct, no scan, no magic
constant.

### 6.1 Agent.ToRememberPartner

```csharp
public class Agent
{
    // ... existing fields ...
    public Guid? ToRemember;
    public GameId? ToRememberPartner;   // NEW: partner GameId for the pending memory op
}
```

`Conversation.StopWithManager` step 2 (the existing "LastConversation +
ToRemember on non-human participants" loop) gains one line:

```csharp
foreach (var pid in participantIds)
{
    var agent = mgr.NPCs.Get(pid);
    if (agent == null || agent.IsHuman) continue;
    agent.LastConversation = now;
    agent.ToRemember = Id;
    // NEW: stamp the partner so AgentRememberConversationOp can find it
    // without a MemoryStash scan + magic time window.
    GameId partnerId = default;
    foreach (var otherPid in participantIds)
        if (!otherPid.Equals(pid)) { partnerId = otherPid; break; }
    agent.ToRememberPartner = partnerId;
}
```

### 6.2 The op itself

```csharp
public static class AgentRememberConversationOp
{
    public static async UniTask<bool> RunAsync(Agent agent, AITavernManager mgr, long now)
    {
        if (agent == null || agent.IsHuman || agent.ToRemember == null)
        {
            // Defensive: humans / no-op agents — clear and bail.
            if (agent != null) { agent.ToRemember = null; agent.ToRememberPartner = null; }
            return false;
        }

        // Capture and clear FIRST so a thrown exception or unexpected tick
        // can't re-enter. INV-3.8-2 says agent.Operation is cleared by the
        // scheduler's finally in AgentSimulator.RunOp, which still runs even
        // if this method throws. AgentDecision BRANCH 8 (INV-3.4-6) only
        // fires when ToRemember != null, so we cleared the trigger.
        var partner = agent.ToRememberPartner;
        agent.ToRemember = null;
        agent.ToRememberPartner = null;
        if (!partner.HasValue) return false;

        try
        {
            return await MemoryCompactor.MaybeCompactForOwner(mgr, agent.PlayerId, partner.Value, now);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Remember] compaction failed for {agent.PlayerId}: {e.Message}");
            return false;
        }
    }
}
```

The signature flips from sync `bool Run(...)` to `async UniTask<bool>
RunAsync(...)`. `AgentSimulator.RunOp` is already an `async UniTaskVoid`
context; the switch arm changes to `await AgentRememberConversationOp
.RunAsync(agent, Manager, now);`. INV-3.8-1 (`Operation` set
synchronously before await) is already satisfied by
`AgentDecision.FireOp`. INV-3.8-2 (`Operation` cleared in finally) is
already handled by `RunOp`'s finally block — no per-op duplication
needed.

### 6.3 Who runs compaction for the OTHER agent?

When 黄蓉 and 欧阳克 finish a conversation, both have `ToRemember` /
`ToRememberPartner` set. On the next tick, *each* fires
`AgentRememberConversationOp`, which calls `MaybeCompactForOwner` for
that agent. The compaction state is per-`(owner, other)` pair — so
黄蓉's compaction is independent of 欧阳克's. Two Grok calls per
conversation end (one per NPC, only fired when budget is exceeded).
This matches ai-town (memory is owner-local).

## 7. Constants

Append to `AITavernConstants.cs`:

```csharp
// Phase 2 — context-based memory.
//
// MEMORY_CONTEXT_BUDGET_CHARS: max char-length of the priorMemory block
// in the system prompt before compaction is requested.
//
// Budget math (re-verify if numbers change):
//   - 20 000 chars * ~0.7 tokens/char (mixed CJK/ASCII) ≈ 14 000 tokens
//     memory block
//   - + persona ~500 tokens
//   - + transcript-so-far ~3 000 tokens (8-msg conv at 200 tok/msg)
//   - + reply allowance ~250 tokens
//   - = ~18 000 tokens total prompt, well under grok-4 256K context
//
// The block can transiently grow up to MEMORY_CONTEXT_BUDGET_CHARS * 1.5
// before the read-side soft-truncate (§4 step 4) kicks in to deterministically
// drop the oldest un-folded transcripts. Above ~50 % of context depth,
// long-context recall degrades — keep the budget well below.
public const int MEMORY_CONTEXT_BUDGET_CHARS = 20_000;

// Read-side soft-truncate threshold (multiplier on the budget). When
// compaction has failed repeatedly and the block exceeds this, the read
// path deterministically drops the oldest un-folded transcripts to avoid
// blowing the 256K hard cap mid-conversation.
public const float MEMORY_CONTEXT_HARD_OVERFLOW_FACTOR = 1.5f;

// Compaction target: after compaction, the projected block (including the
// new summary at upper-bound ~MEMORY_SUMMARY_MAX_CHARS) must fit under
// MEMORY_CONTEXT_BUDGET_CHARS * this fraction. Leaves headroom so a single
// new conversation doesn't immediately re-trigger compaction.
public const float MEMORY_COMPACTION_TARGET_FRACTION = 0.6f;

// Summary call output bounds. Bumped from plan v1's 200 chars / 250 tokens
// because the summarizer must capture 4 facets (relationship / shared
// experiences / unresolved tensions / concrete facts) — too-tight budgets
// collapsed them into mush in plan v1 review. Reviewer recommendation:
// ~400 chars / 500 tokens for headroom.
public const int MEMORY_SUMMARY_MAX_CHARS = 400;
public const int MEMORY_SUMMARY_MAX_TOKENS = 500;
public const float MEMORY_SUMMARY_TEMPERATURE = 0.3f;  // factual compression, not creative
```

## 8. Prompt-engineering details for the summary call

### 8.1 System prompt

```
你正在为下一次对话准备背景资料：把两位武侠人物的过往对话压缩成结构化摘要，供他们再次相遇时作为上下文使用。

仅输出中文。简洁但保留任何会影响他们今后关系的信息。

记录对白内容时使用「某人说/声称/暗示/抱怨」等归属句式，避免把任一方的言论当作既成事实（人物可能撒谎、虚张声势、口是心非）。

按以下四个固定槽位输出，每槽位 1-2 句话，缺槽位写"无"：
关系：<两人当前的态度倾向，敌意/警惕/好奇/亲近 等>
共同经历：<具体发生过的事，包括地点、物件、动作>
未解矛盾：<尚未化解的冲突或承诺>
关键事实：<任一方陈述过的具体事实，用归属句式>
```

### 8.2 User message body

```
对话双方：<self.BioName>（<self short one-line>）与 <other.BioName>（<other short one-line>）

[需要压缩的对话历史]
<transcript 1>
---
<transcript 2>
---
...
```

The `short one-line` is the FIRST sentence of `Bio.Identity` (truncated
to 50 chars). Full Identity / Plans / Relationship are NOT passed —
those describe present state; the summarizer should compress past
conversations, not project a current view.

Note: the previous summary text is NOT passed. We always re-fold from
the raw `MemoryEntry.Description` of all folded + newly-selected entries
(see §3.2 design decision — single summary per pair, re-fold from raw).

### 8.3 Grok call parameters

```csharp
var text = await mgr.Grok.CompleteChatAsync(
    systemPrompt: <§8.1 text>,
    messages: new List<(string, string)> { ("user", <§8.2 body>) },
    maxTokens: AITavernConstants.MEMORY_SUMMARY_MAX_TOKENS,
    stopSequences: new[] { "User:", "Assistant:" },  // defend against role-leak
    temperature: AITavernConstants.MEMORY_SUMMARY_TEMPERATURE);
```

No new `IGrokClient` method — reuse the existing `CompleteChatAsync`.
Stop sequences keep `"User:"` / `"Assistant:"` (defends against
role-leakage if Grok mimics the transcript format) but drop `"\n\n"` from
the chat call's set since the 4-slot output deliberately uses blank
lines.

### 8.4 Parse / fallback

The summarizer is asked for a 4-slot structured output. The op:

1. Trims whitespace; rejects if empty.
2. If the response contains at least 2 of the 4 slot prefixes
   (`关系：`, `共同经历：`, `未解矛盾：`, `关键事实：`), accept verbatim
   as the summary text.
3. If fewer than 2 slots are present, accept the text anyway BUT log a
   warning (`[Summary] facet-slot parse miss agent=<id>; raw=<truncated>`)
   — model occasionally returns plain prose, which is still useful, just
   noisier.
4. Reject and bail (return `false` from `MaybeCompactForOwner`) only on
   empty/whitespace or HTTP error.

## 9. Failure / edge cases

| Case | Behavior |
|------|----------|
| Grok summary call HTTP-fails | `MaybeCompactForOwner` returns `false`; entries stay un-folded; warning logged. Next conversation end retries. The read-side soft-truncate (§4 step 4) prevents the chat call from blowing 256K context — deterministically drops oldest transcripts when block exceeds budget × 1.5. |
| Grok returns empty / whitespace | Treated as failure; same as above. |
| Grok returns prose without facet-slot markers | Accepted with a `[Summary] facet-slot parse miss` warning (§8.4). Still useful, just noisier. |
| `ToRememberPartner` is null when op fires | Defensive — op clears `ToRemember` and returns `false`. Should not happen in normal flow because `Conversation.StopWithManager` always sets both together. |
| Player Bio changes mid-game | Memories with `Target = player` unaffected — `PairKey` is a GameId pair, not a bio reference. |
| Repeated immediate Stops (race) | `agent.ToRemember = null` / `ToRememberPartner = null` set BEFORE the await; `Operation` still set (cleared by `RunOp` finally per INV-3.8-2). Duplicate tick can't re-enter. |
| Compaction fires while a NEW conversation is mid-Generate | The new generate call reads the memory block synchronously at prompt-build time. If compaction happens to land between two generate calls in the same conversation, the second turn sees a summary instead of raw transcripts — fine, both represent the same history. |
| Both NPCs fire compaction near-simultaneously after the same conversation | Each compaction is per-`(owner, other)` and writes to a different `Summaries[PairKey]` slot (canonical PairKey is symmetric, but the WRITES are to the same key). Two near-simultaneous writes — the later one wins. Both call Grok with overlapping but owner-distinct prompts (each owner's perspective on the same history). Acceptable: the per-owner read path still sees a coherent summary. |
| `MemoryType.Relationship` (Phase 3 death record) or `Reflection` (deferred) entries | Excluded from compaction selection in §5 step 3. Their `Description` never gets folded; their `Importance = 9` signal survives. |

## 10. Tests

All Phase 1 tests must continue to pass. New tests:

| Test | What it verifies |
|------|-----------------|
| `MemoryCompactor_belowBudget_noOp` | `MaybeCompactForOwner` returns false; no Summaries appended; no entries marked folded. |
| `MemoryCompactor_overBudget_compactsOldest` | After compaction, `Summaries` has 1 entry covering the oldest N transcripts; those entries have `IsFolded = true`; total rendered block size ≤ `MEMORY_COMPACTION_TARGET_FRACTION × MEMORY_CONTEXT_BUDGET_CHARS`. |
| `MemoryCompactor_existingSummaryFoldsIn` | When a CompactedSummary already exists and a 2nd compaction fires: (a) the new summary's Grok input is the raw `Description` text of all folded + newly-selected entries — NOT the previous summary text; (b) exactly one entry remains in `Summaries[PairKey]` (overwrite, per §3.2 decision); (c) all selected entries are marked `IsFolded = true`. |
| `BuildPriorMemoryBlock_emitsSummaryThenTranscripts` | Output starts with the summary block, then `---`-separated un-folded transcripts in chronological order. |
| `BuildPriorMemoryBlock_skipsFoldedEntries` | Entries with `IsFolded = true` do not appear in the output. |
| `RememberOp_clearsToRemember_evenOnFailure` | If compaction throws, `agent.ToRemember` is still cleared. |
| `RememberOp_handlesMissingOther` | When `MemoryStash` has no recent entry for this owner, RememberOp returns false but doesn't throw. |
| `MockGrokClient` enhancement | Test double for compaction calls — return a canned summary string when system prompt contains "summarizing prior conversations". |

Layer: all editor-mode (no PlayMode), under
`jyx2/Assets/Mods/aitavern/Scripts/Tests/Editor/`. Existing
`TestBuilders.MakeManager` builds the manager with mock Grok + clock; new
tests use the same helper.

## 11. Resolved design decisions (was: open questions in v1)

| # | Decision | Rationale |
|---|----------|-----------|
| Q1 | **Single CompactedSummary per pair, re-fold from raw on each compaction.** | Plan v1 proposed chaining summaries; arch reviewer flagged summary-of-summary degrades after 3-4 hops. Re-folding from raw `MemoryEntry.Description` bounds information loss to exactly one Grok hop, regardless of compaction count. Folded entries are kept (`IsFolded = true`) precisely so this re-fold has source material. |
| Q2 | **Same `XAI_API_KEY` & `MODEL` as chat.** | One config knob. Revisit if cost telemetry shows summary spend is significant; in practice it's ~10 % of total per §12 cost notes. |
| Q3 | **Both NPCs run their own compaction independently, post-conversation.** | Per-owner memory model matches ai-town. 2 Grok calls per conversation end (only when budget exceeded). Acceptable cost. |
| Q4 | **No migration. `IsFolded` defaults to `false` on deserialize.** | Phase 1 entries pass through as raw transcripts until a future compaction folds them. |
| Q5 | **`ToRememberPartner` field on `Agent` instead of 5-second EndedAt scan.** | Plan v1 scanned `MemoryStash` to disambiguate the partner; arch reviewer flagged brittleness and the magic-constant smell. Direct field set by `Conversation.StopWithManager` is robust and removes the scan from the hot path. |
| Q6 | **Anti-repeat guard moves to per-type instruction tail of `BuildStart` / `BuildContinue`, in Chinese.** | Prompt reviewer flagged that a single tail line under N>2 transcripts sits too far from the generation point. Adjacent to "Reply in 1-3 sentences" keeps it in focus. Chinese matches the content language. |
| Q7 | **Memory block becomes all-Chinese; `BuildStart` / `BuildContinue` English instruction tails stay English for now.** | Full prompt translation is a bigger refactor that touches Phase 1 code; a follow-up phase can do it. Phase 2 only translates the new memory block to keep content-language consistency where it matters most. |
| Q8 | **`MemoryType.Relationship` and `MemoryType.Reflection` are exempt from compaction.** | Their high-importance signal (Phase 3 death records with `Importance = 9`) must survive intact. Plan §5 step 3 selects only `MemoryType.Conversation`. |
| Q9 | **xAI prompt caching (`cache_control` markers on the persona + memory prefix)** — deferred to a follow-up task. | Prompt reviewer noted that the system prompt is byte-identical across all turns of one conversation and that caching could cut chat cost ~70 %. We add a stub task (`T9` below) but treat it as optional for Phase 2 scope; ship without first, validate the pipeline, then enable. |

## 12. Implementation tasks (preview — full list in `phase2_tasks.md`)

1. **T1: Schema** — Extend `MemoryEntry` with `IsFolded`; add `CompactedSummary` POCO; add `MemoryStash.Summaries` (Dictionary keyed by PairKey) with `GetSummary` / `SetSummary` helpers.
2. **T2: Constants** — Append to `AITavernConstants.cs`: `MEMORY_CONTEXT_BUDGET_CHARS`, `MEMORY_CONTEXT_HARD_OVERFLOW_FACTOR`, `MEMORY_COMPACTION_TARGET_FRACTION`, `MEMORY_SUMMARY_MAX_CHARS`, `MEMORY_SUMMARY_MAX_TOKENS`, `MEMORY_SUMMARY_TEMPERATURE`. Include the budget-math comment per §7.
3. **T3: Agent.ToRememberPartner** — Add field; update `Conversation.StopWithManager` step 2 to set it alongside `ToRemember`. No behavior change in Phase 1 callers.
4. **T4: MemoryCompactor** — New static class. Implement `MaybeCompactForOwner`. Reuses `mgr.Grok.CompleteChatAsync` directly (no new interface method). Skips `MemoryType.Relationship` / `Reflection`. Re-folds from raw `Description`. Includes the §8.4 facet-slot parse path.
5. **T5: AgentRememberConversationStub → Op** — Rename file & class to `AgentRememberConversationOp`. Convert `Run` (sync `bool`) to `RunAsync` (`UniTask<bool>`). Wire into `AgentSimulator.RunOp` switch arm with `await`. Uses `ToRememberPartner` directly — no MemoryStash scan.
6. **T6: BuildPriorMemoryBlock rewrite** — Drop the `PRIOR_MEMORY_MAX_TRANSCRIPTS = 2` ring buffer. New layout per §4: summary block (if any) + all un-folded transcripts in chronological order + `[刚刚结束的对话]` tag on the last. Include the read-side soft-truncate fallback (§4 step 4). Remove the anti-repeat tail line from this block.
7. **T7: Anti-repeat guard move** — Modify `ConversationPrompts.BuildStart` / `BuildContinue` to append the Chinese anti-repeat line to the per-type instruction tail (§4.1). Note: this is a Phase 1 prompt edit gated by Phase 2 land; ship in the same commit as T6.
8. **T8: Tests** — Per §10 list.
9. **T9 (optional, deferred)**: xAI prompt caching. Inspect xAI docs for current `cache_control` / prefix-cache convention; if available, mark the persona + memory prefix as cacheable in `GrokClient.BuildBodyJson`. Skip if the docs don't expose a stable flag; revisit when xAI publishes one. Not blocking Phase 2 ship.
10. **T10: Smoke test in editor** — Play through 3 NPC↔NPC conversations between 黄蓉 & 欧阳克 (after temporarily lowering `MEMORY_CONTEXT_BUDGET_CHARS` to ~1 500 to force compaction). Verify (a) compaction fires after the 2nd conversation, (b) the 3rd conversation's system prompt contains the `[较早｜摘要]` block, (c) the summary contains all 4 facet slots, (d) NPC responses reference the summarized history without echoing the verbatim early lines.

## 13. Migration / rollback

- **Forward:** additive. Existing `MemoryEntry`s deserialize with `IsFolded = false`; existing read path keeps working until budget triggers compaction. No back-fill needed.
- **Rollback is LOSSY** (arch reviewer flagged plan v1 as too optimistic):
  - The `CompactedSummary` dictionary has no Phase 1 reader; reverting Phase 2 orphans the summary data. It's still in memory (in-process only — no save format yet) but unreachable.
  - Folded entries (`IsFolded = true`) will be re-injected verbatim by Phase 1's ring-buffer `BuildPriorMemoryBlock`, because the field is unknown to Phase 1 code. If the ring buffer's "last 2 transcripts" happens to overlap a folded transcript, the player will see content that was already summarized away — no actual bug, just suboptimal selection.
  - If clean rollback ever becomes a hard requirement (e.g. once save files include MemoryStash), gate the `IsFolded` write behind a version flag. For Phase 2 in-process-only memory, accept the lossy revert.

## 14. Why not embeddings, importance, reflection?

| Feature | Why dropped |
|---------|-------------|
| Embeddings | xAI has no embeddings API. Local ONNX (MiniLM ~80 MB) is doable but the relevance term is the least load-bearing of the three at our scale (2–8 NPCs, low memory volume per pair). Grok reads the full transcripts and can decide relevance itself. |
| Importance scoring | With full context available, Grok weighs importance implicitly while reading the transcripts. An explicit 0-9 score adds a Grok call per memory and a parsing failure surface for no measured quality win. Phase 3 death records still set `Importance = 9` directly (no Grok call) so the high-importance signal is preserved where it matters. |
| Explicit Reflection memories | A reflection is "the agent's current high-level view of someone, synthesized from memories." With every conversation injecting the full transcripts, Grok produces that synthesis on-the-fly inside each chat response. No need to materialize it as a separate memory entry. |

If the scale changes (Phase 5 expands to 20+ characters and conversations
get dense), revisit and add embeddings + ranked recall. The Phase 2 code
is structured so those additions slot in beside `BuildPriorMemoryBlock`
without rewriting the storage layer.
