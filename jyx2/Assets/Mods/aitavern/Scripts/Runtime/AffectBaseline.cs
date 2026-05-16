namespace Jyx2.AITavern
{
    // Phase 3C (Plan §4.4 / §10 Q3 amended): map the committed RelationType
    // canon to a starting affection baseline in [-1,1]. Affection decays
    // toward THIS, never toward 0 — absent new events a character reverts to
    // their canonical disposition. Deterministic, no Grok, no pipeline pass;
    // RelationType is the same canon the §4 dossier / "Your view of X" line
    // already reflects (§10 Q3 originally specced a pipeline-emitted scalar
    // that T3A.6 never produced — this is the amended source).
    //
    // The spread matters more than the precise numbers: this is only the
    // attractor. 3D's appraisal (§5.3 reflection deltas) moves Affect.Value
    // off this baseline on events; with no events Affect.Current(now) relaxes
    // back here over AFFECTION_HALFLIFE_MS (~15 min, §7).
    public static class AffectBaseline
    {
        // Exhaustive over the RelationshipGraph.cs RelationType enum
        // (Neutral / Ally / Friend / Rival / Enemy). Symmetric-ish spread:
        // Friend is the warm anchor, Enemy the cold one; Ally is a milder
        // positive (alliance of convenience, not affection); Rival a milder
        // negative (friction, not hatred). Neutral is the 0 attractor — also
        // what the graph returns for any unset edge (e.g. NPC→player, since
        // the player has no bio.Relationships → no graph entry → Neutral → 0).
        public static float ForRelation(RelationType r)
        {
            switch (r)
            {
                case RelationType.Enemy:   return -0.7f;
                case RelationType.Rival:   return -0.4f;
                case RelationType.Neutral: return  0.0f;
                case RelationType.Ally:    return  0.4f;
                case RelationType.Friend:  return  0.6f;
                default:                   return  0.0f;
            }
        }
    }
}
