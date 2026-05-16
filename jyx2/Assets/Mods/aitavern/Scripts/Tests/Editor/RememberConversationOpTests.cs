// T3D.6 (Plan §5.2 / §5.3 / §5.3.1 / §6.2) — AgentRememberConversationOp
// .RunAsync editor tests. The Phase 2 safety properties are KEPT (they are
// still valid):
//   - ToRemember / ToRememberPartner cleared SYNCHRONOUSLY before any await
//     (a thrown Grok call / unexpected re-tick cannot re-enter the op);
//   - humans / no-op agents bail and still clear the trigger;
//   - missing ToRememberPartner → returns false, still clears.
// The Phase 2 "MaybeCompact volume gate" assertions are REWRITTEN to the 3D
// wiring (Plan §5.4 scheduled): RunAsync now delegates to
// MemoryCompactor.ConsolidateForOwner, then FoldGlobalReflection ONLY when
// the per-pair consolidation actually ran (consolidated == true).
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
        GameId _xiaoer;

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000);
            _mock = new MockGrokClient();
            _mgr.Grok = _mock;
            _huangrong = new GameId("huangrong");
            _ouyangke = new GameId("ouyangke");
            _xiaoer = new GameId("xiaoer");
        }

        [TearDown]
        public void TearDown()
        {
            TestBuilders.DestroyManager(_mgr);
        }

        // §5.3 4-slot block helper.
        static string Slots(string impression, string emotion = "平静",
                            string affection = "0", string contradiction = "空")
        {
            return "往来印象：" + impression + "\n情绪变化：" + emotion
                 + "\n好恶变化：" + affection + "\n违背设定：" + contradiction;
        }

        // ---------- Test 1: ToRemember cleared even when the reflection Grok call throws ----------

        [Test]
        public void RunAsync_clearsToRememberEvenOnException()
        {
            // Raw for the pair so ConsolidateForOwner actually reaches the Grok
            // call (no more Phase 2 volume gate — conv-end ALWAYS consolidates
            // when there is raw). ThrowOnCall exercises the failure path.
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, "黄蓉：你是谁\n欧阳克：路人", 1_000_000);
            _mock.ThrowOnCall = true;

            var agent = TestBuilders.MakeAgent("huangrong", isHuman: false);
            _mgr.NPCs.Register(agent);
            agent.ToRemember = System.Guid.NewGuid();
            agent.ToRememberPartner = _ouyangke;

            AgentRememberConversationOp
                .RunAsync(agent, _mgr, 1_003_000)
                .GetAwaiter().GetResult();

            Assert.IsNull(agent.ToRemember,
                "ToRemember cleared synchronously before the await — survives a Grok throw");
            Assert.IsNull(agent.ToRememberPartner,
                "ToRememberPartner cleared even on reflection-consolidation failure");
        }

        // ---------- Test 2: missing ToRememberPartner → false + clears (defensive) ----------

        [Test]
        public void RunAsync_returnsFalseWhenToRememberPartnerMissing()
        {
            var agent = TestBuilders.MakeAgent("huangrong", isHuman: false);
            _mgr.NPCs.Register(agent);
            agent.ToRemember = System.Guid.NewGuid();
            agent.ToRememberPartner = null;   // defensive path.

            bool result = AgentRememberConversationOp
                .RunAsync(agent, _mgr, 1_000_000)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result, "missing partner → no consolidation → returns false");
            Assert.IsNull(agent.ToRemember, "ToRemember cleared even on the defensive bail-out path");
        }

        // ---------- Test 3: human / no-op agent bails and clears the trigger ----------

        [Test]
        public void RunAsync_humanAgent_bailsAndClears()
        {
            var human = TestBuilders.MakeAgent("player", isHuman: true);
            _mgr.NPCs.Register(human);
            human.ToRemember = System.Guid.NewGuid();
            human.ToRememberPartner = _ouyangke;

            bool result = AgentRememberConversationOp
                .RunAsync(human, _mgr, 1_000_000)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result, "human agent → op bails (no consolidation)");
            Assert.IsNull(human.ToRemember, "human bail still clears ToRemember");
            Assert.IsNull(human.ToRememberPartner, "human bail still clears ToRememberPartner");
            Assert.AreEqual(0, _mock.CompletionCallCount, "human bail → no Grok call");
        }

        // ---------- Test 4: wiring — ConsolidateForOwner runs, then FoldGlobalReflection
        //                    ONLY when consolidated == true (per-pair → global order) ----------

        [Test]
        public void RunAsync_consolidatesThenFoldsGlobal_whenConsolidatedTrue()
        {
            // Owner already has ONE other pair with a non-blank summary so that,
            // once THIS pair consolidates, the owner has ≥2 → the global fold
            // fires (Plan §5.3.1 ≥2 gate).
            var mind = _mgr.GetOrCreateMind(_huangrong);
            mind.Targets[_xiaoer] = new TargetState { ReflectionSummary = "店小二神色慌张" };

            // Raw for (huangrong, ouyangke) so ConsolidateForOwner produces a
            // summary (→ consolidated == true).
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, "黄蓉：你想做什么\n欧阳克：探探木箱", 1_000_000);

            // Call 1 = the §5.3 per-pair 4-slot consolidation; call 2 = the
            // §5.3.1 global fold (free-form ≤2-sentence blob).
            _mock.Responses.Enqueue(Slots("他屡屡试探木箱、回避正面问话", "警惕 0.6", "-0.3"));
            _mock.Responses.Enqueue("在场众人皆回避木箱话题，各怀心事");

            var agent = TestBuilders.MakeAgent("huangrong", isHuman: false);
            _mgr.NPCs.Register(agent);
            agent.ToRemember = System.Guid.NewGuid();
            agent.ToRememberPartner = _ouyangke;

            bool consolidated = AgentRememberConversationOp
                .RunAsync(agent, _mgr, 1_003_000)
                .GetAwaiter().GetResult();

            Assert.IsTrue(consolidated, "per-pair consolidation succeeded → op returns true");
            Assert.AreEqual(2, _mock.CompletionCallCount,
                "exactly two Grok calls: §5.3 per-pair consolidation THEN §5.3.1 global fold");

            Assert.AreEqual("他屡屡试探木箱、回避正面问话",
                mind.Targets[_ouyangke].ReflectionSummary,
                "per-pair ReflectionSummary written by ConsolidateForOwner");
            Assert.AreEqual("在场众人皆回避木箱话题，各怀心事", mind.GlobalReflection,
                "GlobalReflection folded AFTER the per-pair consolidation (consolidated == true)");
        }

        // ---------- Test 5: consolidated == false → FoldGlobalReflection NOT invoked ----------

        [Test]
        public void RunAsync_noRaw_consolidatedFalse_globalFoldNotInvoked()
        {
            // Even though the owner has a pre-existing summary that WOULD make
            // the ≥2 gate plausible, with NO raw for the (huangrong, ouyangke)
            // pair ConsolidateForOwner returns false → the op must NOT run the
            // global fold (strict per-pair → global ordering, Plan §5.3.1).
            var mind = _mgr.GetOrCreateMind(_huangrong);
            mind.Targets[_xiaoer] = new TargetState { ReflectionSummary = "店小二神色慌张" };
            mind.Targets[_ouyangke] = new TargetState { ReflectionSummary = "他屡屡试探木箱" };

            var agent = TestBuilders.MakeAgent("huangrong", isHuman: false);
            _mgr.NPCs.Register(agent);
            agent.ToRemember = System.Guid.NewGuid();
            agent.ToRememberPartner = _ouyangke;

            bool consolidated = AgentRememberConversationOp
                .RunAsync(agent, _mgr, 1_003_000)
                .GetAwaiter().GetResult();

            Assert.IsFalse(consolidated, "no raw → ConsolidateForOwner returns false");
            Assert.AreEqual(0, _mock.CompletionCallCount,
                "consolidated == false → FoldGlobalReflection is NOT invoked (no Grok call at all)");
        }
    }
}
