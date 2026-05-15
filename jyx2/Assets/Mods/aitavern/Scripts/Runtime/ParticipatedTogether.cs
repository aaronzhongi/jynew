using System.Collections.Generic;

namespace Jyx2.AITavern
{
    // Per-pair conversation cooldown store.
    //
    // INV-3.3: Bidirectional. Record(A, B, t) implies LastEnded(A, B) == LastEnded(B, A) and
    // IsCoolingDown(A, B, now) == IsCoolingDown(B, A, now). Achieved by canonicalising the
    // key via lexicographic ordering on GameId.Value before storage and lookup.
    //
    // INV-3.6-4: Candidate selection filters via IsCoolingDown(self, candidate, now).
    //
    // Phase 1 NOTE: PLAYER_CONVERSATION_COOLDOWN_MS is defined locally as a const here.
    // Constant consolidation into AITavernConstants is deferred to T9/T10/T11.
    public class ParticipatedTogether
    {
        // Bumped from 60s → 180s so NPCs don't immediately re-engage the same
        // partner after a conversation ends. Combined with prior-transcript
        // injection in ConversationPrompts.BuildStart, this stops the "they
        // just keep saying the same thing" loop.
        private const long PLAYER_CONVERSATION_COOLDOWN_MS = 180_000L;

        private readonly Dictionary<(GameId, GameId), long> _lastEnded
            = new Dictionary<(GameId, GameId), long>();

        public void Record(GameId p1, GameId p2, long endedAt)
        {
            _lastEnded[Canonical(p1, p2)] = endedAt;
        }

        public long? LastEnded(GameId p1, GameId p2)
        {
            return _lastEnded.TryGetValue(Canonical(p1, p2), out var v) ? (long?)v : null;
        }

        public bool IsCoolingDown(GameId p1, GameId p2, long now)
        {
            var ended = LastEnded(p1, p2);
            return ended.HasValue && now < ended.Value + PLAYER_CONVERSATION_COOLDOWN_MS;
        }

        // Canonical ordering: ensures (A, B) and (B, A) hash to the same dictionary key.
        private static (GameId, GameId) Canonical(GameId a, GameId b)
        {
            return string.CompareOrdinal(a.Value, b.Value) <= 0 ? (a, b) : (b, a);
        }
    }
}
