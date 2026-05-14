using System.Collections.Generic;

namespace Jyx2.AITavern
{
    // INV-8-12 forward-look: Phase 3 differentiates death records (Relationship) from regular
    // conversation summaries (Conversation). Reflection is reserved for Phase 2 LLM-driven
    // self-reflection passes.
    public enum MemoryType
    {
        Conversation,
        Relationship,
        Reflection,
    }

    public class MemoryEntry
    {
        public MemoryType Type;
        public GameId Owner;            // whose memory it is
        public GameId? Target;          // about whom (set for Relationship type)
        public string PairKey;          // canonical "A|B"
        public string Description;      // text (transcript summary OR death record)
        public long EndedAt;            // ms epoch
        public int? Importance;         // 0..9, Phase 2 LLM-rated; Phase 3 death records preset to 9
    }

    // Phase 1 stub: append-only list. Phase 2 will add embeddings + ranked recall on top of
    // this same data; callers should only use the helpers below, not mutate Entries directly.
    public class MemoryStash
    {
        public readonly List<MemoryEntry> Entries = new List<MemoryEntry>();

        // Phase 1: §4.3 — on Conversation.Stop, both participants append a Conversation memory.
        public void AppendConversationMemory(GameId owner, GameId other, string transcript, long endedAt)
        {
            Entries.Add(new MemoryEntry
            {
                Type = MemoryType.Conversation,
                Owner = owner,
                Target = other,
                PairKey = CanonicalPair(owner, other),
                Description = transcript,
                EndedAt = endedAt,
            });
        }

        // INV-8-12: Phase 3 death record. Importance pre-set to 9, skipping the LLM importance call.
        public void AppendDeathRecord(GameId owner, GameId deadOther, string description, long endedAt)
        {
            Entries.Add(new MemoryEntry
            {
                Type = MemoryType.Relationship,
                Owner = owner,
                Target = deadOther,
                PairKey = CanonicalPair(owner, deadOther),
                Description = description,
                EndedAt = endedAt,
                Importance = 9,
            });
        }

        public IEnumerable<MemoryEntry> ForOwner(GameId owner)
        {
            foreach (var e in Entries)
            {
                if (e.Owner == owner) yield return e;
            }
        }

        private static string CanonicalPair(GameId a, GameId b)
        {
            return string.CompareOrdinal(a.Value, b.Value) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        }
    }
}
