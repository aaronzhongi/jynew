// T5: Snapshot of an in-flight agent operation. Used by the one-op invariant
// (INV-3.8-*): Agent.Operation is set synchronously before any await; the
// presence + StartedAt are checked on every Agent.Tick (INV-3.4-1/2).
namespace Jyx2.AITavern
{
    public class InProgressOperation
    {
        public readonly string Name;     // e.g. "agentDoSomething", "agentGenerateMessage", "agentRememberConversation"
        public readonly string OpId;     // unique id (e.g. Guid string)
        public readonly long StartedAt;  // ms epoch (from IClock)

        public InProgressOperation(string name, string opId, long startedAt)
        {
            Name = name;
            OpId = opId;
            StartedAt = startedAt;
        }
    }
}
