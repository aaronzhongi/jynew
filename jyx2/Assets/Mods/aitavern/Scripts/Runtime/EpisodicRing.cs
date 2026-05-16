// Phase 3D — T3D.2: episodic ring buffer + mid-conversation spill
// (Plan §5.1 / §5.2 / §5.4).
//
// A single static helper invoked IMMEDIATELY AFTER conv.AddMessage at
// EVERY turn-write call site (NPC-generated in AgentGenerateMessageOp,
// player-submitted in AgentSimulator's speak-panel onSubmit). The
// co-location with AddMessage — NOT inside Conversation.AddMessage — is
// the §5.1 invariant: it keeps Conversation free of a RuntimeMindState /
// MemoryStash dependency and is what makes T3D.5's AppendTranscript
// deletion safe (the ring is the single source of recent turns).
//
// Both NON-human participants record the exchange into their OWN per-pair
// ring (each NPC remembers it from its own side). The human player has no
// mind (mirrors SeedSurroundings / SeedAffection IsHuman-skip) so it is
// skipped as a ring OWNER, but it IS a valid talkee key.
//
// Ring shape (§5.1): index 0 is the pinned anchor (first turn of the
// current conversation). On overflow past MEMORY_RING_CAP the OLDEST
// NON-anchor record (Ring[1]) is evicted — never Ring[0] — and spilled
// to the per-pair raw MemoryStash list (§5.2 / §5.4) so T3D.3's
// reflection-consolidation can re-fold it. Synchronous, NO Grok.
//
// T3D.3 will, at conv-end, consume the surviving ring + the spill log
// and CLEAR the ring. T3D.2 in isolation only grows-then-evicts.

namespace Jyx2.AITavern
{
    public static class EpisodicRing
    {
        /// <summary>
        /// Record one turn into BOTH non-human participants' per-pair rings
        /// (each NPC remembers the exchange from its own side). The player
        /// has no mind so it is skipped as a ring owner, but is a valid
        /// talkee key. Synchronous, no Grok. Called immediately AFTER
        /// conv.AddMessage at every turn-write site — Plan §5.1. All paths
        /// are guarded returns: this MUST NOT throw into the turn-write path.
        /// </summary>
        public static void Record(AITavernManager mgr, Conversation conv,
                                  GameId speaker, string text, long ms)
        {
            // 1. Guards — never throw into the turn path.
            if (mgr == null || conv == null || mgr.NPCs == null) return;
            if (conv.Participants == null) return;
            if (string.IsNullOrWhiteSpace(text)) return; // don't ring blank turns

            // 2. Each participant whose POV we record from. 2-party (Phase 1);
            //    the talkee is the single OTHER participant id.
            foreach (var kv in conv.Participants)
            {
                GameId owner = kv.Key;

                // Player has no mind — skip as an OWNER (mirrors
                // SeedSurroundings / SeedAffection). It still appears below
                // as a talkee key for the NPC's ring.
                var a = mgr.NPCs.Get(owner);
                if (a == null || a.IsHuman) continue;

                // The talkee = the other participant (2-party → exactly one).
                GameId talkee = default;
                bool foundTalkee = false;
                foreach (var other in conv.Participants.Keys)
                {
                    if (!other.Equals(owner)) { talkee = other; foundTalkee = true; break; }
                }
                if (!foundTalkee) continue; // degenerate single-party — nothing to record

                // 3. Idempotent mind/target lookup. T3C.2 already seeds
                //    Targets at spawn; defensively create if absent. Ring is
                //    default-initialised (T3D.1) so it is non-null.
                var mind = mgr.GetOrCreateMind(owner);
                if (mind.Targets == null)
                    mind.Targets = new System.Collections.Generic.Dictionary<GameId, TargetState>();
                if (!mind.Targets.TryGetValue(talkee, out var ts) || ts == null)
                {
                    ts = new TargetState();
                    mind.Targets[talkee] = ts;
                }
                if (ts.Ring == null)
                    ts.Ring = new System.Collections.Generic.List<TurnRecord>();

                // 4. Append this turn.
                ts.Ring.Add(new TurnRecord { Speaker = speaker, Text = text, Ms = ms });

                // 5. Anchor + last-9 cap (Plan §5.1). Ring[0] is the pinned
                //    anchor (first turn of the current conversation — emerges
                //    naturally because T3D.3 clears the ring at conv-end; in
                //    T3D.2 isolation the ring just grows then evicts). On
                //    overflow evict the OLDEST NON-anchor = Ring[1] (NEVER
                //    Ring[0]) and spill it before removal.
                if (ts.Ring.Count > AITavernConstants.MEMORY_RING_CAP)
                {
                    var evicted = ts.Ring[1];          // oldest non-anchor
                    SpillEvicted(mgr, owner, talkee, evicted);
                    ts.Ring.RemoveAt(1);
                }

                // T3D.3: at conv-end, consume the surviving ring + the spill
                // log into the per-pair ReflectionSummary, then CLEAR ts.Ring
                // (so the next conversation's first turn becomes the new
                // anchor). Not done here — T3D.2 only grows/evicts/spills.
            }
        }

        // §5.2 / §5.4 spill: the evicted TurnRecord lands in the per-pair raw
        // MemoryStash list — the SAME store + canonical pair-keying Phase 2
        // uses for conversation memory — so T3D.3's reflection re-fold reads
        // it exactly where it reads post-conv transcripts. Reuses the
        // existing MemoryStash.AppendConversationMemory(owner, other,
        // body, endedAt) append verbatim (no MemoryStash structural change).
        // Body is the one evicted line, prefixed with a [spill] marker so
        // T3D.3 can tell a single mid-conv overflow turn apart from a full
        // post-conv transcript blob. Synchronous, NO Grok.
        static void SpillEvicted(AITavernManager mgr, GameId owner, GameId talkee, TurnRecord evicted)
        {
            if (mgr == null || mgr.Memory == null || evicted == null) return;
            string body = "[spill] " + (evicted.Speaker.Value ?? "?") + ": " + (evicted.Text ?? string.Empty);
            mgr.Memory.AppendConversationMemory(owner, talkee, body, evicted.Ms);
        }
    }
}
