// T10: AgentDoSomethingOp — picks either an invitee (interest-weighted) or a
// wander destination, per §3.6 / §4.5 / INV-3.6-1..10.
//
// Mirrors ai-town's `agentDoSomething` operation
// (convex/aiTown/agentOperations.ts lines 107-146). Synchronous; no LLM call
// in this op. The scheduler (AgentSimulator, T15) is responsible for
// clearing `agent.Operation = null` after Run completes (INV-3.8-2).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Jyx2.AITavern
{
    /// <summary>
    /// Implements ai-town's `agentDoSomething` operation. Decides between
    /// picking an invitee (interest-weighted) or wandering, per §3.6.
    ///
    /// Called by the scheduler (AgentSimulator, T15) after AgentDecision.Tick
    /// has fired the op. Synchronous — no LLM call in this op. The scheduler
    /// is responsible for clearing `agent.Operation = null` after Run completes
    /// (INV-3.8-2).
    /// </summary>
    public static class AgentDoSomethingOp
    {
        // Local consts pulled from invariants checklist; T11 / a later refactor
        // can consolidate into AITavernConstants once the file stabilizes.
        const long CONVERSATION_COOLDOWN_MS = 15_000;
        const float SCENE_RADIUS_M = 20f;
        const float MIN_CANDIDATE_SCORE = 0.05f;
        const float WANDER_RADIUS_M = 8f;
        const float WANDER_NAVMESH_SAMPLE_RANGE_M = 4f;
        const int WANDER_RETRY_ATTEMPTS = 3;

        /// <summary>
        /// Run the op. Returns true if state-changing action taken
        /// (invite OR wander), false otherwise.
        /// </summary>
        public static bool Run(Agent agent, AITavernManager mgr, long now)
        {
            if (agent == null || mgr == null) return false;
            if (agent.IsHuman) return false;

            // INV-3.6-1: justLeftConversation derived from LastConversation +
            // CONVERSATION_COOLDOWN_MS window.
            bool justLeftConversation = agent.LastConversation.HasValue
                && now < agent.LastConversation.Value + CONVERSATION_COOLDOWN_MS;
            bool recentlyAttemptedInvite = agent.LastInviteAttempt.HasValue
                && now < agent.LastInviteAttempt.Value + CONVERSATION_COOLDOWN_MS;

            bool pathfinding = agent.Body != null && agent.Body.IsPathfinding;

            // INV-3.6-3: justLeftConversation || recentlyAttemptedInvite suppresses
            // invitee selection. Mirrors ai-town agentOperations.ts:147-149
            // forcing invitee=undefined in those cases.
            bool suppressInvite = justLeftConversation || recentlyAttemptedInvite;

            // Invite selection runs whenever the op fires, gated only by
            // suppression. ai-town's pathfinding gate (agentOperations.ts:115)
            // affects wander-vs-activity choice, NOT invite eligibility — see
            // agentOperations.ts:147-149 where invitee selection runs
            // independently of pathfinding. Our short tavern paths complete
            // before the next tick, leaving IsPathfinding=false at decision
            // time, so gating on pathfinding effectively never invites.
            if (!suppressInvite)
            {
                var invitee = PickInvitee(agent, mgr, now);
                if (invitee != null)
                {
                    Debug.Log($"[DoSomething] {agent.AgentId} → INVITE {invitee.AgentId} (pathfinding={pathfinding})");
                    // Stamp LastInviteAttempt ONLY when actually attempting an
                    // invite. Stamping on every DoSomething fire (as we did
                    // before) re-armed recentlyAttemptedInvite every 500ms and
                    // permanently suppressed invites.
                    agent.LastInviteAttempt = now;
                    var conv = Conversation.Start(mgr.Conversations, agent.PlayerId, invitee.PlayerId, now);
                    Debug.Log($"[DoSomething] Conversation.Start → {(conv != null ? "OK id=" + conv.Id : "NULL (overlap?)")}");
                    return true;
                }
                Debug.Log($"[DoSomething] {agent.AgentId} no invitee found → wander");
            }
            else
            {
                Debug.Log($"[DoSomething] {agent.AgentId} suppressed (justLeft={justLeftConversation}, recentInvite={recentlyAttemptedInvite}) → wander");
            }

            // INV-3.6-9: activity arm is no-op in Phase 1; we go straight to wander.
            return TryWander(agent);
        }

        // Interest-weighted candidate selection per §3.6 / INV-3.6-4..8.
        static Agent PickInvitee(Agent self, AITavernManager mgr, long now)
        {
            if (mgr.NPCs == null) return null;

            var scored = new List<(Agent other, float score)>();

            foreach (var other in mgr.NPCs.All)
            {
                if (other == null) continue;
                if (other.AgentId.Equals(self.AgentId)) continue;

                // INV-3.6-4: skip anyone in an active conversation (pre-filter).
                if (mgr.Conversations != null && mgr.Conversations.IsInActiveConversation(other.PlayerId))
                    continue;
                // INV-3.6-4 cont'd: skip pair-cooldown candidates.
                if (mgr.PairCooldowns != null && mgr.PairCooldowns.IsCoolingDown(self.PlayerId, other.PlayerId, now))
                    continue;

                // INV-3.6-5: interest defaults to 0.3 when bio missing or no
                // explicit entry exists. CharacterBio.InterestIn already
                // applies the 0.3 ambient default when no entry matches; we
                // fall back to 0.3 directly when either bio reference is null.
                float interest = (self.Bio != null && other.Bio != null)
                    ? self.Bio.InterestIn(other.Bio.AgentId)
                    : 0.3f;

                float proximity = 1.0f;
                if (self.Body != null && other.Body != null)
                {
                    float d = Vector3.Distance(self.Body.Position, other.Body.Position);
                    proximity = 1.0f - Mathf.Clamp01(d / SCENE_RADIUS_M);
                }

                long? lastEnded = mgr.PairCooldowns != null
                    ? mgr.PairCooldowns.LastEnded(self.PlayerId, other.PlayerId)
                    : null;
                bool lastPartnerOfThisPair = lastEnded.HasValue
                    && now < lastEnded.Value + 2 * CONVERSATION_COOLDOWN_MS;
                float recencyDamp = lastPartnerOfThisPair ? 0.5f : 1.0f;

                // INV-3.6-5: score = interest * proximity * recencyDamp.
                float score = interest * proximity * recencyDamp;
                // INV-3.6-6: drop sub-floor candidates.
                if (score > MIN_CANDIDATE_SCORE) scored.Add((other, score));
            }

            // INV-3.6-7: empty candidate set returns null without throw.
            if (scored.Count == 0) return null;

            // INV-3.6-8: weighted random sample using the seeded RNG;
            // NOT argmax — keeps "free will" feel.
            return WeightedSample(scored, mgr.Rng);
        }

        static Agent WeightedSample(List<(Agent other, float score)> scored, System.Random rng)
        {
            float total = 0f;
            for (int i = 0; i < scored.Count; i++) total += scored[i].score;
            if (total <= 0f) return null;

            double r = rng != null ? rng.NextDouble() : new System.Random().NextDouble();
            double target = r * total;
            double acc = 0;
            for (int i = 0; i < scored.Count; i++)
            {
                acc += scored[i].score;
                if (target <= acc) return scored[i].other;
            }
            return scored[scored.Count - 1].other; // floating-point fallback
        }

        // §4.5 NPC wander: sample a NavMesh point within radius; up to 3
        // retries. Returns true if a destination was set; false on repeated
        // sample failure (idle this tick).
        //
        // The NavMesh sample is straight UnityEngine.AI — we don't push it
        // through INPCBody because the interface is intentionally minimal
        // (Position / IsPathfinding / StopPathfinding / MoveTo). The body
        // wrapper still owns the actual NavMeshAgent driver via MoveTo.
        static bool TryWander(Agent agent)
        {
            if (agent.Body == null) return false;

            Vector3 origin = agent.Body.Position;
            for (int attempt = 0; attempt < WANDER_RETRY_ATTEMPTS; attempt++)
            {
                Vector3 candidate = origin + RandomXZInRadius(WANDER_RADIUS_M);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, WANDER_NAVMESH_SAMPLE_RANGE_M, NavMesh.AllAreas))
                {
                    agent.Body.MoveTo(hit.position);
                    return true;
                }
            }
            return false;
        }

        // Uniform random point in a disc on the XZ plane.
        // Uses Unity's default Random because the seed-determinism point of
        // AITavernConstants.RandomSeed is the candidate-selection path, not
        // the wander-destination noise (Phase 1 wander is forgiving). T17
        // tests that depend on determinism should mock NavMesh or stub
        // TryWander.
        static Vector3 RandomXZInRadius(float radius)
        {
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float r = UnityEngine.Random.Range(0f, radius);
            return new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        }
    }
}
