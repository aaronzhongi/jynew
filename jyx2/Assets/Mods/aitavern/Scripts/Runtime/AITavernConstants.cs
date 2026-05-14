// T5 STUB: Only the constants Conversation.cs references in this task are
// declared here. Full constant set (per docs/aitavern_invariants.md INV-Constants
// and Phase 1 Plan §3.7) arrives in T10/T11. Subsequent tasks APPEND to this
// file — do not overwrite existing values.
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

        // INTENTIONALLY incomplete — T10/T11 append the remaining constants
        // (INVITE_ACCEPT_PROBABILITY, ACTION_TIMEOUT_MS, AWKWARD_CONVERSATION_TIMEOUT,
        // CONVERSATION_COOLDOWN_MS, PLAYER_CONVERSATION_COOLDOWN, SCENE_RADIUS,
        // MIN_CANDIDATE_SCORE, RandomSeed, etc.).
    }
}
