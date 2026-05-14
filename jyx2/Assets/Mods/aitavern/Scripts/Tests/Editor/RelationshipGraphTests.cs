// T17: RelationshipGraph — asymmetric directed map, Neutral default for unknown pairs.
using NUnit.Framework;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class RelationshipGraphTests
    {
        [Test]
        public void GetRelation_Default_IsNeutral()
        {
            var g = new RelationshipGraph();
            Assert.AreEqual(RelationType.Neutral, g.GetRelation(new GameId("a"), new GameId("b")));
        }

        [Test]
        public void Set_IsAsymmetric_AtoBDoesNotImplyBtoA()
        {
            var g = new RelationshipGraph();
            var a = new GameId("a");
            var b = new GameId("b");
            g.Set(a, b, RelationType.Ally);

            Assert.AreEqual(RelationType.Ally, g.GetRelation(a, b));
            Assert.AreEqual(RelationType.Neutral, g.GetRelation(b, a),
                "reverse direction is not auto-populated");
        }

        [Test]
        public void Set_OverwritesExistingEdge()
        {
            var g = new RelationshipGraph();
            var a = new GameId("a");
            var b = new GameId("b");
            g.Set(a, b, RelationType.Friend);
            g.Set(a, b, RelationType.Enemy);
            Assert.AreEqual(RelationType.Enemy, g.GetRelation(a, b));
        }

        [Test]
        public void Set_SupportsAllRelationTypes()
        {
            var g = new RelationshipGraph();
            var a = new GameId("a");
            var b = new GameId("b");
            foreach (RelationType t in System.Enum.GetValues(typeof(RelationType)))
            {
                g.Set(a, b, t);
                Assert.AreEqual(t, g.GetRelation(a, b));
            }
        }
    }
}
