// T8 (Phase 2): AgentGenerateMessageOp.BuildPriorMemoryBlock editor-mode tests.
//
// `BuildPriorMemoryBlock` is `internal static`; the test asmdef has access via
// the InternalsVisibleTo line in Runtime/AssemblyInfo.cs (also added in T8).
//
// Verifies Plan §4 read-path layout:
//   - Header "你与 <other> 的过往：".
//   - Summary block ([较早｜摘要]) is emitted BEFORE the transcripts block ([较近｜逐字]).
//   - The newest transcript is tagged [刚刚结束的对话] (counter long-context recency dip).
//   - Folded entries (IsFolded == true) never appear in the rendered output.
using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class BuildPriorMemoryBlockTests
    {
        AITavernManager _mgr;
        GameId _huangrong;
        GameId _ouyangke;
        readonly System.Collections.Generic.List<CharacterBio> _spawnedBios =
            new System.Collections.Generic.List<CharacterBio>();

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000);
            _huangrong = new GameId("huangrong");
            _ouyangke = new GameId("ouyangke");
            _spawnedBios.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var bio in _spawnedBios) TestBuilders.DestroyBio(bio);
            _spawnedBios.Clear();
            TestBuilders.DestroyManager(_mgr);
        }

        // ---------- helpers ----------

        // Registers an agent with a Bio so BuildPriorMemoryBlock can use BioName
        // in the header. Returns the agent for inline test convenience. The bio
        // is tracked in _spawnedBios so TearDown can DestroyImmediate it.
        Agent RegisterAgentWithBio(string id, string bioName)
        {
            var bio = TestBuilders.MakeBio(id);
            bio.BioName = bioName;
            _spawnedBios.Add(bio);
            var agent = TestBuilders.MakeAgent(id, bio: bio);
            _mgr.NPCs.Register(agent);
            return agent;
        }

        // ---------- Test 4: summary + transcripts in the right order ----------

        [Test]
        public void BuildPriorMemoryBlock_emitsSummaryThenTranscripts()
        {
            RegisterAgentWithBio("huangrong", "黄蓉");
            RegisterAgentWithBio("ouyangke", "欧阳克");

            // Seed a CompactedSummary for the pair.
            const string summaryText =
                "关系：警惕\n共同经历：客栈试探\n未解矛盾：无\n关键事实：欧阳克声称无意";
            _mgr.Memory.SetSummary(new CompactedSummary
            {
                PairKey = string.CompareOrdinal("huangrong", "ouyangke") <= 0
                    ? "huangrong|ouyangke"
                    : "ouyangke|huangrong",
                OwnerA = _huangrong,
                OwnerB = _ouyangke,
                SummaryText = summaryText,
                CoveredFromMs = 1,
                CoveredUntilMs = 2,
                FoldedEntryCount = 2,
                CreatedAt = 100,
            });

            // Two un-folded transcripts (newest gets the [刚刚结束的对话] marker).
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, "黄蓉：第一段对话\n欧阳克：回应一", 1_000_000);
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, "黄蓉：第二段对话\n欧阳克：回应二", 1_001_000);

            string s = AgentGenerateMessageOp.BuildPriorMemoryBlock(_mgr, _huangrong, _ouyangke);
            Assert.IsNotNull(s, "block must render when summary + transcripts exist");

            // Header includes the other agent's BioName.
            StringAssert.Contains("你与 欧阳克 的过往：", s);

            // Summary header + its text.
            StringAssert.Contains("[较早｜摘要]", s);
            StringAssert.Contains(summaryText, s);

            // Transcripts header + recency marker.
            StringAssert.Contains("[较近｜逐字]", s);
            StringAssert.Contains("[刚刚结束的对话]", s);

            // Ordering: summary block appears BEFORE the transcripts block.
            int summaryIdx = s.IndexOf("[较早｜摘要]", System.StringComparison.Ordinal);
            int transcriptsIdx = s.IndexOf("[较近｜逐字]", System.StringComparison.Ordinal);
            Assert.AreNotEqual(-1, summaryIdx);
            Assert.AreNotEqual(-1, transcriptsIdx);
            Assert.Less(summaryIdx, transcriptsIdx, "summary block must precede transcripts block");

            // The [刚刚结束的对话] marker is on the LAST (newest) transcript — so it
            // sits AFTER the second transcript's header content.
            int recencyIdx = s.IndexOf("[刚刚结束的对话]", System.StringComparison.Ordinal);
            int firstTranscriptIdx = s.IndexOf("黄蓉：第一段对话", System.StringComparison.Ordinal);
            int secondTranscriptIdx = s.IndexOf("黄蓉：第二段对话", System.StringComparison.Ordinal);
            Assert.AreNotEqual(-1, recencyIdx);
            Assert.AreNotEqual(-1, firstTranscriptIdx);
            Assert.AreNotEqual(-1, secondTranscriptIdx);
            Assert.Less(firstTranscriptIdx, recencyIdx, "first transcript appears BEFORE recency marker");
            Assert.Less(recencyIdx, secondTranscriptIdx, "recency marker tags the last (newest) transcript");
        }

        // ---------- Test 5: folded entries are skipped ----------

        [Test]
        public void BuildPriorMemoryBlock_skipsFoldedEntries()
        {
            RegisterAgentWithBio("huangrong", "黄蓉");
            RegisterAgentWithBio("ouyangke", "欧阳克");

            // Three entries — mark the oldest two as folded. No summary set
            // (deliberately tests the unusual "folded entries without summary" path).
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, "最旧的对话内容OLDEST", 1_000_000);
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, "中间的对话内容MIDDLE", 1_001_000);
            _mgr.Memory.AppendConversationMemory(_huangrong, _ouyangke, "最新的对话内容NEWEST", 1_002_000);

            // Locate the entries we just appended (last 3) — order in Entries is
            // insertion order from AppendConversationMemory.
            int n = _mgr.Memory.Entries.Count;
            _mgr.Memory.Entries[n - 3].IsFolded = true;  // oldest
            _mgr.Memory.Entries[n - 2].IsFolded = true;  // middle
            // newest stays unfolded.

            string s = AgentGenerateMessageOp.BuildPriorMemoryBlock(_mgr, _huangrong, _ouyangke);
            Assert.IsNotNull(s);

            StringAssert.Contains("最新的对话内容NEWEST", s);
            Assert.IsFalse(s.Contains("最旧的对话内容OLDEST"), "folded oldest must not appear");
            Assert.IsFalse(s.Contains("中间的对话内容MIDDLE"), "folded middle must not appear");
            StringAssert.Contains("[刚刚结束的对话]", s);
        }
    }
}
