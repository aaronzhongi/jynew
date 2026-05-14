using System.Collections.Generic;
using UnityEngine;

namespace Jyx2.AITavern
{
    [System.Serializable]
    public class InterestEntry
    {
        public string TargetAgentId;     // matches CharacterBio.AgentId of another character
        [Range(0f, 1f)] public float Score;
    }

    [System.Serializable]
    public class RelationshipEntry
    {
        public string TargetAgentId;
        public RelationType Relation;    // defined in RelationshipGraph.cs (same namespace)
    }

    // Phase 0 persona definition for a single AI Tavern character.
    //
    // Plan §11.2 fields. Authored as a ScriptableObject so designers can tune personas in
    // the editor without touching code. T16 creates the concrete Bio_*.asset instances.
    [CreateAssetMenu(fileName = "Bio_Character", menuName = "AI Tavern/CharacterBio", order = 0)]
    public class CharacterBio : ScriptableObject
    {
        [Header("Identity")]
        public string AgentId;           // e.g. "huangrong"
        public int RoleId;               // jynew character row id (e.g. 15)
        public int HeadId;               // portrait id

        [Header("Persona")]
        public string BioName;           // e.g. "黄蓉"
        [TextArea(3, 6)] public string Identity;
        [TextArea(2, 5)] public string Plans;

        [Header("Social")]
        public List<RelationshipEntry> Relationships = new List<RelationshipEntry>();
        public List<InterestEntry> Interests = new List<InterestEntry>();

        [Header("Inventory")]
        public List<int> StartingItems = new List<int>();  // CsRoleItem ids (count = 1 each)

        [Header("World Placement")]
        public string SpawnMarkerName;   // GameObject name under Level/NPC/

        // Helpers -----------------------------------------------------------------------

        // INV-3.6-5: ambient curiosity default 0.3 when no explicit interest authored.
        public float InterestIn(string otherAgentId)
        {
            foreach (var e in Interests)
            {
                if (e.TargetAgentId == otherAgentId) return e.Score;
            }
            return 0.3f;
        }

        public RelationType RelationTo(string otherAgentId)
        {
            foreach (var e in Relationships)
            {
                if (e.TargetAgentId == otherAgentId) return e.Relation;
            }
            return RelationType.Neutral;
        }

        // Phase 1 heuristic: a character "wants" any item present in its starting inventory.
        // Phase 3 will refine into preference categories (weapons, scrolls, healing, etc.).
        public bool WantsItem(int itemId)
        {
            return StartingItems.Contains(itemId);
        }
    }
}
