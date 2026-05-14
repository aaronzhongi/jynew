// T17: One-operation invariant — INV-3.8-1..3.
//
// Two consecutive AgentDecision.Tick calls during an in-flight operation
// must NOT double-schedule. After ACTION_TIMEOUT_MS elapses, the in-flight
// op is treated as stale and cleared before the new tick fires.
using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class OneOperationInvariantTests
    {
        const long ACTION_TIMEOUT_MS = 120_000;

        AITavernManager _mgr;
        FakeClock _clock;
        FakeScheduler _scheduler;
        Agent _a;

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000);
            _clock = (FakeClock)_mgr.Clock;
            _scheduler = new FakeScheduler();
            _a = TestBuilders.MakeAgent("a");
            _mgr.NPCs.Register(_a);
        }

        [TearDown]
        public void TearDown() => TestBuilders.DestroyManager(_mgr);

        // ----- INV-3.8-1 / 3: two ticks during in-flight op → only one schedule -----
        [Test]
        public void TwoConsecutiveTicksDuringInFlightOp_OnlyOneSchedule()
        {
            AgentDecision.Tick(_a, _mgr, _scheduler, _clock.NowMs());
            Assert.AreEqual(1, _scheduler.CallCount, "first tick schedules");
            Assert.IsNotNull(_a.Operation, "Operation set synchronously before scheduler");

            // The scheduler does NOT clear agent.Operation (production
            // scheduler does this in a continuation). Simulating a 500 ms
            // gap between ticks — well within ACTION_TIMEOUT_MS.
            _clock.Advance(500);
            AgentDecision.Tick(_a, _mgr, _scheduler, _clock.NowMs());
            Assert.AreEqual(1, _scheduler.CallCount,
                "second tick during in-flight op must not double-fire");
        }

        // ----- INV-3.4-2 / INV-3.8-3 boundary: stale op cleared after timeout -----
        [Test]
        public void OperationGoesStaleAfterActionTimeout_NextTickReFiresDoSomething()
        {
            AgentDecision.Tick(_a, _mgr, _scheduler, _clock.NowMs());
            Assert.AreEqual(1, _scheduler.CallCount);
            var staleOpId = _a.Operation.OpId;

            // Advance just past the timeout.
            _clock.Advance(ACTION_TIMEOUT_MS + 1);

            AgentDecision.Tick(_a, _mgr, _scheduler, _clock.NowMs());
            Assert.AreEqual(2, _scheduler.CallCount, "stale op cleared, new op fired");
            Assert.IsNotNull(_a.Operation);
            Assert.AreNotEqual(staleOpId, _a.Operation.OpId,
                "stale op was cleared and a fresh op was scheduled");
        }

        // ----- Operation set BEFORE scheduler.Schedule was called -----
        [Test]
        public void OperationIsSetSynchronouslyBeforeSchedulerCalled()
        {
            // We can verify this by inspecting agent.Operation FROM the scheduler.
            var inspecting = new InspectingScheduler();
            AgentDecision.Tick(_a, _mgr, inspecting, _clock.NowMs());

            Assert.IsNotNull(inspecting.OperationAtSchedule,
                "agent.Operation must be set before Schedule() is invoked (INV-3.8-1)");
        }

        class InspectingScheduler : IOperationScheduler
        {
            public InProgressOperation OperationAtSchedule;
            public void Schedule(Agent agent, string opName, object args, long now)
            {
                OperationAtSchedule = agent.Operation;
            }
        }
    }
}
