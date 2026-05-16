// T3D.6 (Plan §5.3.1 / §5.0 / §9) — REPURPOSED. This file previously tested
// AgentGenerateMessageOp.BuildPriorMemoryBlock, a Phase 2 method DELETED in
// T3D.6 (zero callers after T3D.5; the §5.5.2 ReflectionSummary + §5.5.3 ring
// supersede it — Plan §5.4). Rather than delete the .cs (and its Unity .meta),
// the file is repurposed into the §5.3.1 cross-person GlobalReflection fold
// tests (the file/class name is kept solely to avoid Unity .meta churn — the
// AgentRememberConversationStub.cs precedent).
//
// FoldGlobalReflection (Plan §5.3.1) is `public static async UniTask` and is
// driven directly. Invariants under test:
//   - <2 non-blank Targets[*].ReflectionSummary → GlobalReflection UNTOUCHED
//     (an existing one is NOT cleared); no Grok call.
//   - ≥2 → exactly one Grok call; mind.GlobalReflection set.
//   - Grok throw / empty → mind.GlobalReflection UNCHANGED (best-effort).
//   - it is OUTPUT-ONLY: it NEVER writes any Targets[*].ReflectionSummary,
//     and reads CURRENT per-pair summaries (cross-pair staleness intentional).
using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class BuildPriorMemoryBlockTests   // name kept to avoid .meta churn; see header
    {
        AITavernManager _mgr;
        MockGrokClient _mock;
        GameId _huangrong;   // the reflecting owner
        GameId _ouyangke;
        GameId _xiaoer;      // 店小二 — a second talkee so the owner has ≥2 pairs

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

        // ---------- helpers ----------

        TargetState SeedPairSummary(GameId other, string summary)
        {
            var mind = _mgr.GetOrCreateMind(_huangrong);
            var ts = new TargetState { ReflectionSummary = summary };
            mind.Targets[other] = ts;
            return ts;
        }

        void Fold(long now) =>
            MemoryCompactor.FoldGlobalReflection(_mgr, _huangrong, now)
                .GetAwaiter().GetResult();

        // ---------- Test 1: <2 non-blank summaries → skipped, existing not cleared ----------

        [Test]
        public void FoldGlobalReflection_fewerThanTwoSummaries_isSkippedAndDoesNotClear()
        {
            // Exactly ONE non-blank per-pair summary (+ one blank, doesn't count).
            SeedPairSummary(_ouyangke, "他屡屡试探木箱");
            SeedPairSummary(_xiaoer, "   ");   // blank — not counted toward the ≥2 gate

            const string existing = "EXISTING_GLOBAL_SENTINEL 先前的跨人反思";
            _mgr.GetOrCreateMind(_huangrong).GlobalReflection = existing;

            Fold(1_002_000);

            Assert.AreEqual(0, _mock.CompletionCallCount,
                "<2 non-blank per-pair summaries → no global Grok call (Plan §5.3.1 ≥2 gate)");
            Assert.AreEqual(existing, _mgr.GetOrCreateMind(_huangrong).GlobalReflection,
                "an existing GlobalReflection is NOT cleared when the gate fails (Plan §5.3.1)");
        }

        // ---------- Test 2: ≥2 → one Grok call, GlobalReflection set ----------

        [Test]
        public void FoldGlobalReflection_twoOrMoreSummaries_setsGlobalReflection()
        {
            SeedPairSummary(_ouyangke, "他屡屡试探木箱、回避正面问话");
            SeedPairSummary(_xiaoer, "他对木箱欲言又止，神色慌张");

            const string global = "在场众人皆回避木箱话题，各怀心事";
            _mock.Responses.Enqueue(global);

            Fold(1_002_000);

            Assert.AreEqual(1, _mock.CompletionCallCount, "≥2 summaries → exactly one global Grok fold");
            Assert.AreEqual(global, _mgr.GetOrCreateMind(_huangrong).GlobalReflection,
                "mind.GlobalReflection set from the global fold output");
        }

        // ---------- Test 3: output-only — never writes any Targets[*].ReflectionSummary ----------

        [Test]
        public void FoldGlobalReflection_isOutputOnly_neverWritesPerPairSummaries()
        {
            const string sumA = "对欧阳克：他屡屡试探木箱";
            const string sumB = "对店小二：他神色慌张";
            var tsA = SeedPairSummary(_ouyangke, sumA);
            var tsB = SeedPairSummary(_xiaoer, sumB);

            _mock.Responses.Enqueue("众人皆回避木箱");

            Fold(1_002_000);

            Assert.AreEqual(sumA, tsA.ReflectionSummary,
                "per-pair ReflectionSummary[ouyangke] is NEVER written by the global fold (output-only)");
            Assert.AreEqual(sumB, tsB.ReflectionSummary,
                "per-pair ReflectionSummary[xiaoer] is NEVER written by the global fold (output-only)");
        }

        // ---------- Test 4: Grok throw → GlobalReflection unchanged (best-effort) ----------

        [Test]
        public void FoldGlobalReflection_grokThrows_leavesGlobalReflectionUnchanged()
        {
            SeedPairSummary(_ouyangke, "他屡屡试探木箱");
            SeedPairSummary(_xiaoer, "他神色慌张");

            const string existing = "PRE_EXISTING_GLOBAL 先前折出的跨人反思";
            _mgr.GetOrCreateMind(_huangrong).GlobalReflection = existing;
            _mock.ThrowOnCall = true;

            Fold(1_002_000);

            Assert.AreEqual(existing, _mgr.GetOrCreateMind(_huangrong).GlobalReflection,
                "a failed global fold must NOT corrupt/clear the existing GlobalReflection (Plan §5.3.1 best-effort)");
        }

        // ---------- Test 5: Grok empty → GlobalReflection unchanged ----------

        [Test]
        public void FoldGlobalReflection_grokEmpty_leavesGlobalReflectionUnchanged()
        {
            SeedPairSummary(_ouyangke, "他屡屡试探木箱");
            SeedPairSummary(_xiaoer, "他神色慌张");

            const string existing = "PRE_EXISTING_GLOBAL_2";
            _mgr.GetOrCreateMind(_huangrong).GlobalReflection = existing;
            _mock.Responses.Enqueue("   ");

            Fold(1_002_000);

            Assert.AreEqual(existing, _mgr.GetOrCreateMind(_huangrong).GlobalReflection,
                "empty global-fold response leaves the existing GlobalReflection intact");
        }
    }
}
