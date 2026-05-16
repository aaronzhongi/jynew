// T5 STUB: Only the constants Conversation.cs references in this task are
// declared here. Full constant set (per docs/aitavern_invariants.md INV-Constants
// and Phase 1 Plan §3.7) arrives in T10/T11. Subsequent tasks APPEND to this
// file — do not overwrite existing values.
// Phase 2 (T2) has appended the context-based memory constants below; this
// file is no longer "intentionally incomplete" relative to the Phase 1 doc
// for the constants Phase 2 needs. Remaining T10/T11 constants from Phase 1
// still land separately.
namespace Jyx2.AITavern
{
    public static class AITavernConstants
    {
        // Distances — interpreted as 3D metres in jynew (RETUNE from ai-town's
        // 1.3-tile / 4-tile values per Phase 1 Plan §3.7 retune notes).
        public const float CONVERSATION_DISTANCE_M = 2.0f;
        public const float MIDPOINT_THRESHOLD_M = 6.0f;

        // Time (ms)
        public const long TYPING_TIMEOUT_MS = 15_000;

        // Phase 3A — ContextAssembler §1-§4 static-section char budgets
        // (Plan §7). Each layered section is independently truncated so one
        // verbose dossier can't crowd out the rest of the prompt.
        public const int SECT_WORLD_BUDGET    = 1500;  // §1 World Codex
        public const int SECT_SELFBIO_BUDGET  = 600;   // §2 talker bio
        public const int SECT_TALKEE_BUDGET   = 400;   // §3 talkee first-impression surface
        public const int SECT_LONGTERM_BUDGET = 3000;  // §4 long-term knowledge (polity/faction/person)

        // Phase 3B — §5 short-term block budget (Plan §7). Covers
        // situation + task + surroundings (3B); 3C appends §5.4 emotion
        // and 3D §5.0/§5.5 within their own logic, not a separate budget.
        public const int SECT_SHORTTERM_BUDGET = 4000; // §5 working memory

        // Phase 3C — affect decay (Plan §7). Lazy exp decay via Affect.Current;
        // emotion relaxes to 平静/0 fast, affection reverts to the canon
        // relationship baseline slowly (NOT to 0). EMOTION_FLOOR: below this
        // Current() the §5.4 line is omitted ("mood has passed"). The deltas
        // that move Value on events (AFFECT_DELTA_DEADBAND etc.) are 3D.
        public const float EMOTION_HALFLIFE_MS   = 90_000f;   // ~1.5 min
        public const float EMOTION_FLOOR         = 0.12f;     // below → omit §5.4
        public const float AFFECTION_HALFLIFE_MS = 900_000f;  // ~15 min, reverts to canon baseline

        // Phase 3D — reflection-consolidation (Plan §7).
        //   MEMORY_RING_CAP: TargetState.Ring slots = 1 pinned anchor (first
        //     turn of the conversation) + the most recent (CAP-1). 11th turn
        //     evicts the oldest NON-anchor into the per-pair spill (§5.1/§5.2).
        //   AFFECT_DELTA_DEADBAND: salience gate (§5.3) — if |好恶变化| is
        //     below this AND 情绪变化 is neutral, the consolidation still
        //     folds the summary but does NOT apply the affect deltas / reset
        //     LastSetMs, so chit-chat can't starve the baseline-reversion
        //     decay (§4.4). Zero extra Grok cost (reads already-returned slots).
        public const int   MEMORY_RING_CAP        = 10;
        public const float AFFECT_DELTA_DEADBAND  = 0.08f;

        // Phase 3D — reflection-consolidation Grok call bounds (Plan §7
        // "Reflection call"): the §5.3 4-slot consolidation (往来印象/情绪变化/
        // 好恶变化/违背设定) factual compression, not creative generation.
        public const int   REFLECT_MAX_TOKENS  = 500;   // §7
        public const float REFLECT_TEMPERATURE = 0.3f;  // §7 — factual consolidation, not creative
    }
}
