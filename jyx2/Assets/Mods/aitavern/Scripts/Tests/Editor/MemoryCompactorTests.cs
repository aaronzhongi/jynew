// T8 (Phase 2): MemoryCompactor.MaybeCompactForOwner editor-mode tests.
//
// Verifies the three core invariants from Plan §3.2 / §5:
//   1. Under-budget path is a true no-op (no Grok call, no Summaries write, no IsFolded flips).
//   2. Over-budget path produces exactly one CompactedSummary and folds at least one entry.
//   3. Refold-from-raw: a second compaction NEVER passes the previous SummaryText
//      back to Grok — it always re-folds from raw MemoryEntry.Description text.
//
// MockGrokClient gains queued Responses + LastUserContent capture (this task)
// so test 3 can both swap the canned summary mid-fixture and inspect what was
// actually sent on the second call.
using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class MemoryCompactorTests
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

        // ---------- helpers ----------

        // Appends an entry directly to MemoryStash bypassing the public helper
        // so tests can set EndedAt explicitly (the helper uses now-only).
        static MemoryEntry AppendConv(AITavernManager mgr, GameId owner, GameId other, string desc, long endedAt)
        {
            mgr.Memory.AppendConversationMemory(owner, other, desc, endedAt);
            return mgr.Memory.Entries[mgr.Memory.Entries.Count - 1];
        }

        // ---------- Test 1: below budget → no-op ----------

        [Test]
        public void MaybeCompactForOwner_belowBudget_returnsFalseAndDoesNothing()
        {
            // Two short transcripts well under the 20 000-char budget.
            AppendConv(_mgr, _huangrong, _ouyangke, new string('黄', 100), 1_000_000);
            AppendConv(_mgr, _huangrong, _ouyangke, new string('蓉', 100), 1_001_000);

            bool result = MemoryCompactor
                .MaybeCompactForOwner(_mgr, _huangrong, _ouyangke, 1_002_000)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result, "under-budget compaction must return false");
            Assert.AreEqual(0, _mgr.Memory.Summaries.Count, "no summary written");
            foreach (var e in _mgr.Memory.Entries)
            {
                Assert.IsFalse(e.IsFolded, "no entry should be marked folded under budget");
            }
            Assert.AreEqual(0, _mock.CompletionCallCount, "Grok must NOT be called under budget");
        }

        // ---------- Test 2: over budget → folds oldest, writes summary ----------

        [Test]
        public void MaybeCompactForOwner_overBudget_compactsOldestAndWritesSummary()
        {
            // 3 entries × 8000 chars = 24 000 chars > MEMORY_CONTEXT_BUDGET_CHARS (20 000).
            var e1 = AppendConv(_mgr, _huangrong, _ouyangke, new string('啊', 8000), 1_000_000);
            var e2 = AppendConv(_mgr, _huangrong, _ouyangke, new string('啊', 8000), 1_001_000);
            var e3 = AppendConv(_mgr, _huangrong, _ouyangke, new string('啊', 8000), 1_002_000);

            const string canned =
                "关系：试探\n共同经历：在客栈相遇\n未解矛盾：无\n关键事实：欧阳克声称无意争斗";
            _mock.Responses.Enqueue(canned);

            long now = 1_003_000;
            bool result = MemoryCompactor
                .MaybeCompactForOwner(_mgr, _huangrong, _ouyangke, now)
                .GetAwaiter().GetResult();

            Assert.IsTrue(result, "over-budget compaction must return true");
            Assert.AreEqual(1, _mgr.Memory.Summaries.Count, "exactly one summary per pair");

            var summary = _mgr.Memory.GetSummary(_huangrong, _ouyangke);
            Assert.IsNotNull(summary);
            Assert.GreaterOrEqual(summary.FoldedEntryCount, 1, "summary represents >=1 folded entry");
            Assert.IsFalse(string.IsNullOrWhiteSpace(summary.SummaryText), "summary text non-empty");
            Assert.AreEqual(canned, summary.SummaryText, "summary text == canned mock response");
            Assert.AreEqual(now, summary.CreatedAt, "CreatedAt == now passed in");

            int foldedCount = 0;
            foreach (var e in _mgr.Memory.Entries) if (e.IsFolded) foldedCount++;
            Assert.GreaterOrEqual(foldedCount, 1, "at least one entry got IsFolded=true");

            Assert.AreEqual(1, _mock.CompletionCallCount, "exactly one Grok call for one compaction");
            // Untouched-by-test references — silence "unused" warnings while still
            // documenting which entries we expected to exist.
            Assert.IsNotNull(e1); Assert.IsNotNull(e2); Assert.IsNotNull(e3);
        }

        // ---------- Test 3: existing summary → refold from RAW, not from summary text ----------

        [Test]
        public void MaybeCompactForOwner_existingSummary_refoldsFromRaw()
        {
            // First over-budget setup, same shape as test 2.
            AppendConv(_mgr, _huangrong, _ouyangke, new string('啊', 8000), 1_000_000);
            AppendConv(_mgr, _huangrong, _ouyangke, new string('啊', 8000), 1_001_000);
            AppendConv(_mgr, _huangrong, _ouyangke, new string('啊', 8000), 1_002_000);

            const string firstSummary =
                "关系：试探\n共同经历：客栈初见\n未解矛盾：无\n关键事实：欧阳克自称无敌意";
            const string secondSummary =
                "关系：警惕\n共同经历：再次相遇\n未解矛盾：玉玺归属\n关键事实：黄蓉指控对方撒谎";
            _mock.Responses.Enqueue(firstSummary);
            _mock.Responses.Enqueue(secondSummary);

            bool first = MemoryCompactor
                .MaybeCompactForOwner(_mgr, _huangrong, _ouyangke, 1_003_000)
                .GetAwaiter().GetResult();
            Assert.IsTrue(first, "precondition: first compaction succeeds");
            Assert.AreEqual(firstSummary, _mgr.Memory.GetSummary(_huangrong, _ouyangke).SummaryText);

            int firstFoldedCount = 0;
            foreach (var e in _mgr.Memory.Entries) if (e.IsFolded) firstFoldedCount++;
            Assert.GreaterOrEqual(firstFoldedCount, 1);

            // Append 2 more large entries — pushes back over budget.
            // The unfolded set is now: (any leftover unfolded from first compaction) + 2 new.
            AppendConv(_mgr, _huangrong, _ouyangke, new string('啊', 8000), 1_010_000);
            AppendConv(_mgr, _huangrong, _ouyangke, new string('啊', 8000), 1_011_000);

            bool second = MemoryCompactor
                .MaybeCompactForOwner(_mgr, _huangrong, _ouyangke, 1_012_000)
                .GetAwaiter().GetResult();
            Assert.IsTrue(second, "second compaction must fire (we are over budget again)");

            // Exactly ONE summary per pair (overwrite, not chain).
            Assert.AreEqual(1, _mgr.Memory.Summaries.Count);
            var summary = _mgr.Memory.GetSummary(_huangrong, _ouyangke);
            Assert.AreEqual(secondSummary, summary.SummaryText, "summary was overwritten with new text");
            Assert.AreNotEqual(firstSummary, summary.SummaryText, "new SummaryText differs from old");

            // Folded count is union (previously-folded + newly-folded).
            int secondFoldedCount = 0;
            foreach (var e in _mgr.Memory.Entries) if (e.IsFolded) secondFoldedCount++;
            Assert.GreaterOrEqual(secondFoldedCount, firstFoldedCount,
                "FoldedEntryCount is the union — previously folded entries stay folded");

            // Two Grok calls total (one per compaction).
            Assert.AreEqual(2, _mock.CompletionCallCount);

            // CRITICAL: the second call's user body must NOT contain the previous
            // SummaryText. It must contain raw Description text from the originally-
            // folded entries (Plan §3.2 — re-fold from raw, never from the prior summary).
            Assert.IsFalse(
                _mock.LastUserContent.Contains(firstSummary),
                "second compaction user-body must NOT contain prior SummaryText");
            // Sanity: it DOES contain raw transcript filler.
            Assert.IsTrue(
                _mock.LastUserContent.Contains(new string('啊', 100)),
                "second compaction user-body must contain raw transcript Description text");
        }
    }
}
