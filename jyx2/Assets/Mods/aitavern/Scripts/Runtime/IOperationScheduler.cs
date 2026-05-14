namespace Jyx2.AITavern
{
    /// <summary>
    /// AgentDecision.Tick decides WHAT operation to fire; IOperationScheduler is
    /// the boundary at which the async UniTask is actually scheduled. AgentSimulator
    /// (T15) implements this; tests inject a fake that records calls (T17).
    ///
    /// CRITICAL: AgentDecision sets `agent.Operation = new InProgressOperation(...)`
    /// SYNCHRONOUSLY before calling Schedule (INV-3.8-1). The scheduler MUST clear
    /// `agent.Operation = null` when the underlying UniTask completes OR errors
    /// (INV-3.8-2). The scheduler runs the work fire-and-forget; AgentDecision.Tick
    /// returns immediately after calling Schedule.
    /// </summary>
    public interface IOperationScheduler
    {
        void Schedule(Agent agent, string opName, object args, long now);
    }
}
