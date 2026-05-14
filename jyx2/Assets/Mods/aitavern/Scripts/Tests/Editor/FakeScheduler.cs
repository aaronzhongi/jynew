// T17: Test double for IOperationScheduler — records every Schedule()
// invocation so AgentDecisionTick assertions can read the (op-name, agent,
// args, timestamp) tuple back out. Does NOT actually run the op (Layer A
// tests exercise AgentDoSomethingOp / AgentRememberConversationStub
// directly when they want runner semantics).
using System.Collections.Generic;

namespace Jyx2.AITavern.Tests
{
    public class FakeScheduler : IOperationScheduler
    {
        public class Call
        {
            public Agent Agent;
            public string OpName;
            public object Args;
            public long Now;
        }

        public readonly List<Call> Calls = new List<Call>();

        public void Schedule(Agent agent, string opName, object args, long now)
        {
            Calls.Add(new Call
            {
                Agent = agent,
                OpName = opName,
                Args = args,
                Now = now,
            });
        }

        public Call LastCall => Calls.Count == 0 ? null : Calls[Calls.Count - 1];
        public int CallCount => Calls.Count;
    }
}
