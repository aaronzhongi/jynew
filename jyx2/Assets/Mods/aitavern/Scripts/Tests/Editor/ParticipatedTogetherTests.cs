// T17: ParticipatedTogether — INV-3.3 bidirectional symmetry + cooldown query.
using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class ParticipatedTogetherTests
    {
        const long PLAYER_CONVERSATION_COOLDOWN_MS = 60_000;

        [Test]
        public void Record_LastEndedSymmetric_BothDirections()
        {
            var pt = new ParticipatedTogether();
            var a = new GameId("a");
            var b = new GameId("b");
            pt.Record(a, b, 1000);

            Assert.AreEqual(1000, pt.LastEnded(a, b));
            Assert.AreEqual(1000, pt.LastEnded(b, a));
        }

        [Test]
        public void Record_LaterTimestampOverwrites()
        {
            var pt = new ParticipatedTogether();
            var a = new GameId("a");
            var b = new GameId("b");
            pt.Record(a, b, 1000);
            pt.Record(b, a, 2000);
            Assert.AreEqual(2000, pt.LastEnded(a, b));
        }

        [Test]
        public void IsCoolingDown_True_WithinWindow()
        {
            var pt = new ParticipatedTogether();
            var a = new GameId("a");
            var b = new GameId("b");
            pt.Record(a, b, 1000);

            Assert.IsTrue(pt.IsCoolingDown(a, b, 1000 + PLAYER_CONVERSATION_COOLDOWN_MS - 1));
            Assert.IsTrue(pt.IsCoolingDown(b, a, 1000 + PLAYER_CONVERSATION_COOLDOWN_MS - 1),
                "bidirectional symmetry");
        }

        [Test]
        public void IsCoolingDown_False_AtBoundary()
        {
            var pt = new ParticipatedTogether();
            var a = new GameId("a");
            var b = new GameId("b");
            pt.Record(a, b, 1000);
            // At exactly the boundary, IsCoolingDown is false (strict `<`).
            Assert.IsFalse(pt.IsCoolingDown(a, b, 1000 + PLAYER_CONVERSATION_COOLDOWN_MS));
        }

        [Test]
        public void IsCoolingDown_False_AfterWindow()
        {
            var pt = new ParticipatedTogether();
            var a = new GameId("a");
            var b = new GameId("b");
            pt.Record(a, b, 1000);
            Assert.IsFalse(pt.IsCoolingDown(a, b, 1000 + PLAYER_CONVERSATION_COOLDOWN_MS + 1));
        }

        [Test]
        public void IsCoolingDown_FalseForUnknownPair()
        {
            var pt = new ParticipatedTogether();
            Assert.IsFalse(pt.IsCoolingDown(new GameId("a"), new GameId("b"), 0));
            Assert.IsNull(pt.LastEnded(new GameId("a"), new GameId("b")));
        }
    }
}
