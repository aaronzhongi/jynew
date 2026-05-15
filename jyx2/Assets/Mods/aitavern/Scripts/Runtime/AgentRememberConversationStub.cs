// T5 (Phase 2): hosts AgentRememberConversationOp (file name kept as
// AgentRememberConversationStub.cs to avoid Unity .meta-file churn).
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Jyx2.AITavern
{
    /// <summary>
    /// Phase 2 implementation of ai-town's `agentRememberConversation` op
    /// (OperationNames.RememberConversation — name unchanged from Phase 1).
    ///
    /// Runs after `Conversation.Stop` writes `ToRemember` + `ToRememberPartner`
    /// on each non-human participant. Delegates compaction policy to
    /// `MemoryCompactor.MaybeCompactForOwner`. Clears `ToRemember` and
    /// `ToRememberPartner` synchronously before awaiting so re-entry can't
    /// re-fire the op. INV-3.8-2 cleanup of `agent.Operation` is the
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
                return await MemoryCompactor.MaybeCompactForOwner(mgr, agent.PlayerId, partner.Value, now);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Remember] compaction failed for {agent.PlayerId}: {e.Message}");
                return false;
            }
        }
    }
}
