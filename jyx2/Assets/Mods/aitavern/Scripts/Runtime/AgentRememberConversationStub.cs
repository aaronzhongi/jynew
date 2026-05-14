namespace Jyx2.AITavern
{
    /// <summary>
    /// Phase 1 stub for ai-town's `agentRememberConversation` operation.
    ///
    /// In Phase 1 the actual memory write happens in `Conversation.Stop`
    /// (called from `Conversation.Leave` when participants drop below 2 —
    /// see T8). That direct path bypasses the LLM-summary / embedding /
    /// importance-scoring pipeline ai-town runs; Phase 2 will add those
    /// per memory.ts:246-269 (importance), memory.ts:212-217 (ranking).
    ///
    /// This operation is dispatched by AgentDecision.Tick when an Agent's
    /// ToRemember field is non-null (INV-3.4-6). Its sole job in Phase 1
    /// is to clear ToRemember so the agent's tick doesn't fire indefinitely.
    /// </summary>
    public static class AgentRememberConversationStub
    {
        /// <summary>
        /// Runs the stub. Synchronous in Phase 1 (no LLM call).
        ///
        /// Returns true if the operation completed normally.
        /// </summary>
        public static bool Run(Agent agent, AITavernManager mgr, long now)
        {
            if (agent == null) return false;
            if (agent.ToRemember == null) return false;
            if (agent.IsHuman) { agent.ToRemember = null; return true; }

            // Phase 2 hook: when memory pipeline lands, fetch the conversation
            // transcript by `agent.ToRemember.Value` (Guid), summarize via Grok,
            // compute embedding + importance, and call mgr.Memory.AppendXxx.
            //
            // Phase 1: the conversation already wrote its raw transcript via
            // Conversation.Stop → MemoryStash.AppendConversationMemory. We
            // just acknowledge and clear the flag.

            agent.ToRemember = null;
            return true;
        }
    }
}
