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
        // before the read-side soft-truncate (Plan §4 step 4) kicks in to deterministically
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

        // Summary call output bounds. The summarizer must capture 4 facets
        // (relationship / shared experiences / unresolved tensions / concrete facts)
        // — too-tight budgets collapse them into mush. ~400 chars / 500 tokens
        // gives the model headroom while still keeping summaries concise.
        public const int MEMORY_SUMMARY_MAX_CHARS = 400;
        public const int MEMORY_SUMMARY_MAX_TOKENS = 500;
        public const float MEMORY_SUMMARY_TEMPERATURE = 0.3f;  // factual compression, not creative
    }
}
