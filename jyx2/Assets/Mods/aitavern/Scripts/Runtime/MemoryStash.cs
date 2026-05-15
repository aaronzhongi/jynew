using System;
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
        public bool IsFolded;           // Phase 2: true once this transcript's content is represented by the per-pair CompactedSummary.
    }

    // Phase 2: one summary per pair (canonical PairKey). Overwritten on each
    // compaction; the new summary is always re-generated from the raw
    // MemoryEntry.Description text of all folded + newly-selected entries,
    // never from the previous summary text. Bounds information loss to one
    // Grok hop regardless of compaction count.
    public class CompactedSummary
    {
        public string PairKey;
        public GameId OwnerA;
        public GameId OwnerB;
        public string SummaryText;
        public long CoveredFromMs;
        public long CoveredUntilMs;
        public int FoldedEntryCount;
        public long CreatedAt;
    }

    // Phase 2: context-based memory. Raw MemoryEntries accumulate per pair; when thresholds
    // trip, a per-pair CompactedSummary is regenerated from raw entry text and folded entries
    // get IsFolded = true. Callers should only use the helpers below, not mutate state directly.
    public class MemoryStash
    {
        public readonly List<MemoryEntry> Entries = new List<MemoryEntry>();
        public readonly Dictionary<string, CompactedSummary> Summaries = new Dictionary<string, CompactedSummary>();

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

        public CompactedSummary GetSummary(GameId a, GameId b)
        {
            var key = CanonicalPair(a, b);
            return Summaries.TryGetValue(key, out var s) ? s : null;
        }

        public void SetSummary(CompactedSummary s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.PairKey == null) throw new ArgumentNullException(nameof(s) + "." + nameof(s.PairKey));
            Summaries[s.PairKey] = s;
        }

        private static string CanonicalPair(GameId a, GameId b)
        {
            return string.CompareOrdinal(a.Value, b.Value) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        }
    }
}
