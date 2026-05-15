// T8 (Phase 2): AgentRememberConversationOp.RunAsync editor-mode tests.
//
// Verifies the two safety properties from Plan §6.2:
//   - ToRemember / ToRememberPartner are cleared SYNCHRONOUSLY before any
//     await, so a thrown exception (or unexpected re-tick) cannot re-enter
//     the op. Holds even when the underlying Grok call fails.
//   - When ToRememberPartner is missing (defensive — should not happen in
//     normal flow), the op returns false and still clears the trigger.
using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class RememberConversationOpTests
    {
        AITavernManager _mgr;
        MockGrokClient _mock;
        GameId _huangrong;
        GameId _ouyangke;

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000);
            _mock = new MockGrokClient();
            _mgr.Grok = _mock;
            _huangrong = new GameId("huangrong");
            _ouyangke = new GameId("ouyangke");
        }

        [TearDown]
        public void TearDown()
        {
            TestBuilders.DestroyManager(_mgr);
        }

        // ---------- Test 6: ToRemember cleared even when compaction Grok call throws ----------

        [Test]
        public void RunAsync_clearsToRememberEvenOnException()
        {
            // Force compaction to actually fire (volume > MEMORY_CONTEXT_BUDGET_CHARS)
            // so the Grok call happens and ThrowOnCall is exercised.
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, new string('啊', 8000), 1_000_000);
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, new string('啊', 8000), 1_001_000);
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, new string('啊', 8000), 1_002_000);

            _mock.ThrowOnCall = true;

            var agent = TestBuilders.MakeAgent("huangrong", isHuman: false);
            _mgr.NPCs.Register(agent);
            agent.ToRemember = System.Guid.NewGuid();
            agent.ToRememberPartner = _ouyangke;

            // RunAsync returns false on Grok failure (caught inside MemoryCompactor),
            // but the assertion we care about is the cleanup, not the return value.
            AgentRememberConversationOp
                .RunAsync(agent, _mgr, 1_003_000)
                .GetAwaiter().GetResult();

            Assert.IsNull(agent.ToRemember, "ToRemember must be cleared even on compaction failure");
            Assert.IsNull(agent.ToRememberPartner, "ToRememberPartner must be cleared even on compaction failure");
        }

        // ---------- Test 7: returns false (and clears) when ToRememberPartner is missing ----------

        [Test]
        public void RunAsync_returnsFalseWhenToRememberPartnerMissing()
        {
            var agent = TestBuilders.MakeAgent("huangrong", isHuman: false);
            _mgr.NPCs.Register(agent);
            agent.ToRemember = System.Guid.NewGuid();
            agent.ToRememberPartner = null;   // intentionally not set — defensive path.

            bool result = AgentRememberConversationOp
                .RunAsync(agent, _mgr, 1_000_000)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result, "missing partner → no compaction → returns false");
            Assert.IsNull(agent.ToRemember, "ToRemember cleared even on the defensive bail-out path");
        }
    }
}
