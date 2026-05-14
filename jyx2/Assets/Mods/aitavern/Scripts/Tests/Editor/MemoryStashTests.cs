// T17: MemoryStash — append helpers + ForOwner filter + INV-8-12 death record shape.
using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class MemoryStashTests
    {
        [Test]
        public void AppendConversationMemory_AddsEntryWithExpectedFields()
        {
            var ms = new MemoryStash();
            var owner = new GameId("a");
            var other = new GameId("b");
            ms.AppendConversationMemory(owner, other, "a: hi\nb: hello\n", 12345);

            Assert.AreEqual(1, ms.Entries.Count);
            var e = ms.Entries[0];
            Assert.AreEqual(MemoryType.Conversation, e.Type);
            Assert.AreEqual(owner, e.Owner);
            Assert.AreEqual(other, e.Target);
            Assert.AreEqual("a|b", e.PairKey, "canonical pair ordering");
            Assert.AreEqual(12345, e.EndedAt);
            Assert.AreEqual("a: hi\nb: hello\n", e.Description);
            Assert.IsNull(e.Importance, "conversation memory does not preset importance");
        }

        // INV-8-12: death record is Relationship + Importance preset to 9.
        [Test]
        public void AppendDeathRecord_SetsRelationshipTypeAndImportanceNine()
        {
            var ms = new MemoryStash();
            var owner = new GameId("survivor");
            var dead = new GameId("victim");
            ms.AppendDeathRecord(owner, dead, "survivor 与 victim 决斗, victim 身亡。", 99999);

            Assert.AreEqual(1, ms.Entries.Count);
            var e = ms.Entries[0];
            Assert.AreEqual(MemoryType.Relationship, e.Type);
            Assert.AreEqual(9, e.Importance);
            Assert.AreEqual(owner, e.Owner);
            Assert.AreEqual(dead, e.Target);
        }

        [Test]
        public void ForOwner_FiltersByOwner()
        {
            var ms = new MemoryStash();
            var a = new GameId("a");
            var b = new GameId("b");
            ms.AppendConversationMemory(a, b, "t1", 1);
            ms.AppendConversationMemory(b, a, "t2", 2);
            ms.AppendDeathRecord(a, b, "death", 3);

            int aCount = 0;
            foreach (var _ in ms.ForOwner(a)) aCount++;
            int bCount = 0;
            foreach (var _ in ms.ForOwner(b)) bCount++;

            Assert.AreEqual(2, aCount);
            Assert.AreEqual(1, bCount);
        }

        [Test]
        public void CanonicalPair_OrderingStableAcrossInvocations()
        {
            var ms = new MemoryStash();
            var a = new GameId("apple");
            var b = new GameId("banana");
            ms.AppendConversationMemory(a, b, "ab", 1);
            ms.AppendConversationMemory(b, a, "ba", 2);

            // Both entries must share the canonical pair key.
            Assert.AreEqual(ms.Entries[0].PairKey, ms.Entries[1].PairKey);
        }
    }
}
