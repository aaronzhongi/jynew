// T5 (Phase 2): hosts AgentRememberConversationOp (file name kept as
// AgentRememberConversationStub.cs to avoid Unity .meta-file churn).
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Jyx2.AITavern
{
    /// <summary>
    /// ai-town's `agentRememberConversation` op (OperationNames.Remember-
    /// Conversation — name unchanged from Phase 1). Phase 3D (§5.3) makes
    /// this the conv-end reflection-consolidation trigger.
    ///
    /// Runs after `Conversation.Stop` writes `ToRemember` + `ToRememberPartner`
    /// on each non-human participant. Delegates consolidation policy to
    /// `MemoryCompactor.ConsolidateForOwner` (T3D.3). Clears `ToRemember`
    /// and `ToRememberPartner` synchronously before awaiting so re-entry
    /// can't re-fire the op. INV-3.8-2 cleanup of `agent.Operation` is the
    /// scheduler's responsibility (`AgentSimulator.RunOp` finally).
    /// </summary>
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
                // T3D.3 (§5.3): conv-end reflection-consolidation for THIS
                // (owner = agent.PlayerId, partner) pair — re-fold raw spill
                // + ring into the durable ReflectionSummary, emit affect
                // deltas from the raw turns, free the raw buffer. Fires per
                // non-human participant (Conversation.Stop set ToRemember on
                // each) so each call handles exactly one pair.
                bool consolidated = await MemoryCompactor.ConsolidateForOwner(
                    mgr, agent.PlayerId, partner.Value, now);

                // T3D.4 (§5.0 / §5.3.1): cross-person GlobalReflection fold.
                // ONLY when the per-pair consolidation actually ran this
                // conv-end (consolidated == true) — order is strictly
                // per-pair → global, never the reverse, because the global
                // fold reads the ReflectionSummary the just-finished
                // ConsolidateForOwner may have just written. Best-effort
                // (folds the UNION of this owner's Targets[*].ReflectionSummary
                // into mind.GlobalReflection only when ≥2 non-blank; never an
                // input to any per-pair fold — output-only, no recursion).
                // Stays inside this op try/catch so a global-fold throw can't
                // escape the op.
                if (consolidated)
                    await MemoryCompactor.FoldGlobalReflection(mgr, agent.PlayerId, now);

                return consolidated;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Remember] reflection-consolidation failed for {agent.PlayerId}: {e.Message}");
                return false;
            }
        }
    }
}
