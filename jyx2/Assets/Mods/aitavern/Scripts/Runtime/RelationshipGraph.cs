using System.Collections.Generic;

namespace Jyx2.AITavern
{
    // INV-8-7: Faction-ally precedence Ally > Friend > Enemy > Neutral.
    // Rival is reserved for Phase 2/3 narrative use; treated like Enemy at the decision layer
    // only where invariants explicitly call it out.
    public enum RelationType
    {
        Neutral,
        Ally,
        Friend,
        Rival,
        Enemy,
    }

    // Asymmetric directed relationship graph.
    //
    // GetRelation(A, B) is NOT required to equal GetRelation(B, A). For example, a hero may
    // see a former mentor as Ally while the mentor regards the hero as Neutral. Callers that
    // need symmetric semantics (e.g. pair cooldowns) should use ParticipatedTogether instead.
    //
    // Unset edges default to Neutral.
    public class RelationshipGraph
    {
        private readonly Dictionary<(GameId, GameId), RelationType> _relations
            = new Dictionary<(GameId, GameId), RelationType>();

        public void Set(GameId from, GameId to, RelationType r)
        {
            _relations[(from, to)] = r;
        }

        public RelationType GetRelation(GameId from, GameId to)
        {
            return _relations.TryGetValue((from, to), out var r) ? r : RelationType.Neutral;
        }
    }
}
