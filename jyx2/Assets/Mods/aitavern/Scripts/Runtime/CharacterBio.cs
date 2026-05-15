using System.Collections.Generic;
using UnityEngine;

namespace Jyx2.AITavern
{
    // Phase 3 (§2.2) demographic sex. Top-level (NOT nested in CharacterBio) so a future
    // importer and CharacterBio.Sex can both reference it. Distinct from jynew's
    // Character.Sexual int (different assembly — no collision).
    public enum Sex { Male, Female, Other }

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

        // --- Phase 3 (§2.2): demographic / first-impression / trait split ---
        // Additive only — appended after existing fields so Phase 1/2 Bio_*.asset files
        // still deserialize (Unity default-inits new serialized fields).
        [Header("Phase 3 — Demographics & Surface")]
        public Sex Sex;                          // seed from jynew Character.Sexual (0=Male,1=Female,2=Other) at pipeline time
        public string AgeText;                   // "约十五" — prose, wuxia rarely gives exact ages
        [TextArea] public string Personality;    // §2 性情 talker self trait — distinct from Identity
        [TextArea] public string Appearance;     // §3 外貌 — what a STRANGER sees first
        [TextArea] public string SurfaceManner;  // §3 气度 — first-impression demeanor only

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
