using System.Collections.Generic;
using UnityEngine;

namespace Jyx2.AITavern
{
    // Phase 3 (§2.3) CharacterDossier — per roster character, novel-scanned.
    // The talker's §4 long-term semantic memory (polity/faction knowledge beyond the
    // World Codex + per-notable-person view). Immutable canon, bounded to CUTOFF_HUI=10
    // by the offline pipeline; runtime overlay deltas live elsewhere (RuntimeMindState).
    [CreateAssetMenu(fileName = "Dossier_Character", menuName = "AI Tavern/CharacterDossier", order = 11)]
    public class CharacterDossier : ScriptableObject
    {
        public string AgentId;                                                     // links to CharacterBio.AgentId
        public List<KnowledgeLine> PolityKnowledge = new List<KnowledgeLine>();    // §4.1.1 beyond World Codex
        public List<KnowledgeLine> FactionKnowledge = new List<KnowledgeLine>();   // §4.1.2 beyond World Codex
        public List<PersonView> People = new List<PersonView>();                   // §4.2 curated notable set
    }

    [System.Serializable]
    public class KnowledgeLine
    {
        public string Subject;
        [TextArea] public string Text;
    }

    [System.Serializable]
    public class PersonView
    {
        public string Target;                  // AgentId or canonical name
        [TextArea] public string Relationship; // §4.2.1
        [TextArea] public string Impression;   // §4.2.2
        [TextArea] public string MartialNote;  // §4.2.3 武功认知 (novel-lore reviewer; canon at cutoff 回10)
        [TextArea] public string SharedHistory;// §4.2.4 共历桥段 (replaces an event-graph)
        [TextArea] public string TheyDoNotKnow;// §5.5.4 backing: what THIS person canonically doesn't know about the dossier owner, at cutoff 回
    }
}
