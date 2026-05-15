using System.Collections.Generic;
using UnityEngine;

namespace Jyx2.AITavern
{
    // Phase 3 (§2.1) WorldCodex — one global novel-scanned ScriptableObject.
    // §1 世界背景 semantic memory: era, polities, factions, "everyone knows" figures.
    // Immutable canon (produced by the offline lore pipeline; runtime read-only).
    [CreateAssetMenu(fileName = "WorldCodex", menuName = "AI Tavern/WorldCodex", order = 10)]
    public class WorldCodex : ScriptableObject
    {
        [TextArea] public string Era;                                              // §1 时代
        public List<PolityEntry> Polities = new List<PolityEntry>();               // 国/势力 + pairwise relations
        public List<FactionEntry> Factions = new List<FactionEntry>();             // 门派 + pairwise relations
        public List<NotableFigure> NotableFigures = new List<NotableFigure>();     // "everyone knows" set
    }

    [System.Serializable]
    public class RelationLine
    {
        public string Target;             // canonical name of the related polity/faction
        [TextArea] public string Text;    // "对<Target>: <text>"
    }

    [System.Serializable]
    public class PolityEntry
    {
        public string Name;
        [TextArea] public string Brief;
        public List<RelationLine> Relations = new List<RelationLine>();
    }

    [System.Serializable]
    public class FactionEntry
    {
        public string Name;
        [TextArea] public string Brief;
        public string HomeRegion;
        public List<RelationLine> Relations = new List<RelationLine>();
    }

    [System.Serializable]
    public class NotableFigure
    {
        public string Name;
        public string Polity;
        public string Faction;
        [TextArea] public string OneLine;
    }
}
