// T4 (Phase 2): tail-compaction of prior-conversation memory for an owner-partner pair.
//
// Called from AgentRememberConversationOp (T5) AFTER a conversation ends. If the
// rendered priorMemory block for (owner, other) exceeds MEMORY_CONTEXT_BUDGET_CHARS,
// selects the oldest un-folded Conversation entries, summarizes them via one Grok
// call, and writes the result to MemoryStash.Summaries[PairKey].
//
// Critical design points (Plan §3.2, §5):
//   - Always re-fold from raw MemoryEntry.Description (never from the previous
//     summary text). Information loss is bounded to one Grok hop regardless of
//     compaction count.
//   - Exactly one CompactedSummary per pair, overwritten on each compaction.
//   - MemoryType.Relationship (Phase 3 death records) and MemoryType.Reflection
//     are exempt — never folded.
//   - On Grok failure: returns false, entries stay un-folded; the read-side
//     soft-truncate (T6, Plan §4 step 4) catches the overflow.

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Jyx2.AITavern
{
    public static class MemoryCompactor
    {
        // Approximate char overhead of the priorMemory block headers + separators
        // ("你与 X 的过往：", "[较早｜摘要]", "[较近｜逐字]", "---" lines, etc.).
        // T6 ships the real renderer; this is a local estimate so T4 can ship
        // without depending on T6's output.
        const int HEADER_OVERHEAD_CHARS = 200;

        // Facet-slot prefixes used in the §8.1 system prompt.
        static readonly string[] FacetSlotPrefixes = { "关系：", "共同经历：", "未解矛盾：", "关键事实：" };

        // Phase 2: tail-compaction of prior-conversation memory for an owner-partner pair.
        //
        // Returns true iff a summary was generated and written; false on
        // (a) no compaction needed (under budget), or (b) Grok failure (logged).
        public static async UniTask<bool> MaybeCompactForOwner(
            AITavernManager mgr, GameId owner, GameId other, long now)
        {
            if (mgr == null || mgr.Memory == null) return false;

            // Step 1: estimate current size of the priorMemory block (Plan §5 step 1).
            // size = existing summary text (if any) + sum of un-folded Conversation
            // entry Description lengths for this pair + header/separator overhead.
            var existingSummary = mgr.Memory.GetSummary(owner, other);
            int summaryLen = existingSummary != null && existingSummary.SummaryText != null
                ? existingSummary.SummaryText.Length
                : 0;

            // Collect all Conversation entries for this owner targeting other.
            // Exempt Relationship / Reflection per Plan §3.1 / §9.
            var pairEntries = new List<MemoryEntry>();
            foreach (var e in mgr.Memory.ForOwner(owner))
            {
                if (e.Type != MemoryType.Conversation) continue;
                if (!e.Target.HasValue) continue;
                if (!e.Target.Value.Equals(other)) continue;
                pairEntries.Add(e);
            }

            // Sort oldest-first. ForOwner yields insertion order which is typically
            // chronological, but Plan §5 step 3 says do not rely on that.
            pairEntries.Sort((a, b) => a.EndedAt.CompareTo(b.EndedAt));

            int unfoldedLen = 0;
            foreach (var e in pairEntries)
            {
                if (e.IsFolded) continue;
                if (e.Description != null) unfoldedLen += e.Description.Length;
            }

            int sizeBytes = summaryLen + unfoldedLen + HEADER_OVERHEAD_CHARS;

            // Step 2: under budget → no-op.
            if (sizeBytes <= AITavernConstants.MEMORY_CONTEXT_BUDGET_CHARS) return false;

            // Step 3: select entries to fold. Walk oldest-first; for each entry,
            // mark it for fold and project the post-compaction size as:
            //   ~MEMORY_SUMMARY_MAX_CHARS (upper bound for the NEW summary,
            //                              regardless of any existing summary —
            //                              we re-fold from raw, the old summary
            //                              gets overwritten)
            //   + sum of REMAINING un-folded entries' Description lengths
            //   + HEADER_OVERHEAD_CHARS
            // Stop when projectedSize ≤ BUDGET × MEMORY_COMPACTION_TARGET_FRACTION.
            // Always fold at least 1; if even folding all entries doesn't get
            // under target, fold all — the read-side soft-truncate handles the rest.
            float targetSize = AITavernConstants.MEMORY_CONTEXT_BUDGET_CHARS
                * AITavernConstants.MEMORY_COMPACTION_TARGET_FRACTION;

            // Total raw length of every un-folded entry — used to compute the
            // remaining-after-fold length as we walk.
            int remainingUnfoldedLen = unfoldedLen;

            var toFold = new List<MemoryEntry>();
            foreach (var e in pairEntries)
            {
                if (e.IsFolded) continue;
                toFold.Add(e);
                int descLen = e.Description != null ? e.Description.Length : 0;
                remainingUnfoldedLen -= descLen;

                int projectedSize = AITavernConstants.MEMORY_SUMMARY_MAX_CHARS
                    + remainingUnfoldedLen
                    + HEADER_OVERHEAD_CHARS;

                if (projectedSize <= targetSize) break;
            }

            // Defensive: if there were no un-folded entries at all, nothing to do.
            // (sizeBytes was over budget due to an existing summary alone — there
            // is nothing we can fold to shrink further; bail.)
            if (toFold.Count == 0) return false;

            // Step 4: build the Grok call per Plan §8.
            // Re-fold from RAW: gather every folded entry's Description + every
            // newly-selected entry's Description. The previous summary text is
            // NOT passed (Plan §3.2 / §8.2 design decision).
            var foldedAndSelected = new List<MemoryEntry>();
            foreach (var e in pairEntries)
            {
                if (e.IsFolded) foldedAndSelected.Add(e);
            }
            foreach (var e in toFold)
            {
                foldedAndSelected.Add(e);
            }
            // Sort the union oldest-first for the transcript-history block.
            foldedAndSelected.Sort((a, b) => a.EndedAt.CompareTo(b.EndedAt));

            string systemPrompt = BuildSystemPrompt();
            string userBody = BuildUserBody(mgr, owner, other, foldedAndSelected);

            string raw;
            try
            {
                if (mgr.Grok == null)
                {
                    Debug.LogWarning(
                        $"[Summary] no Grok client wired; skipping compaction for owner={owner}");
                    return false;
                }
                raw = await mgr.Grok.CompleteChatAsync(
                    systemPrompt,
                    new List<(string, string)> { ("user", userBody) },
                    maxTokens: AITavernConstants.MEMORY_SUMMARY_MAX_TOKENS,
                    stopSequences: new[] { "User:", "Assistant:" },
                    temperature: AITavernConstants.MEMORY_SUMMARY_TEMPERATURE);
            }
            catch (Exception e)
            {
                // Plan §9: HTTP failure → entries stay un-folded; read-side
                // soft-truncate (T6) handles overflow on next read.
                Debug.LogWarning(
                    $"[Summary] Grok call failed for owner={owner} other={other}: {e.Message}");
                return false;
            }

            // Step 5: parse / fallback per Plan §8.4.
            string text = raw != null ? raw.Trim() : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning($"[Summary] empty response for owner={owner} other={other}");
                return false;
            }

            int slotHits = 0;
            for (int i = 0; i < FacetSlotPrefixes.Length; i++)
            {
                if (text.IndexOf(FacetSlotPrefixes[i], StringComparison.Ordinal) >= 0) slotHits++;
            }
            if (slotHits < 2)
            {
                string head = text.Length > 80 ? text.Substring(0, 80) : text;
                Debug.LogWarning($"[Summary] facet-slot parse miss agent={owner}; raw={head}");
                // Accept the text anyway — model occasionally returns plain prose
                // which is still useful (Plan §8.4 step 3).
            }

            // Step 7: write the new CompactedSummary.
            long coveredFromMs = long.MaxValue;
            long coveredUntilMs = long.MinValue;
            int foldedEntryCount = 0;
            foreach (var e in foldedAndSelected)
            {
                foldedEntryCount++;
                if (e.EndedAt < coveredFromMs) coveredFromMs = e.EndedAt;
                if (e.EndedAt > coveredUntilMs) coveredUntilMs = e.EndedAt;
            }
            // Defensive (foldedAndSelected was non-empty because toFold was non-empty,
            // but guard against the degenerate case anyway).
            if (foldedEntryCount == 0) return false;

            string pairKey = CanonicalPairKey(owner, other);
            GameId ownerA, ownerB;
            if (string.CompareOrdinal(owner.Value, other.Value) <= 0)
            {
                ownerA = owner;
                ownerB = other;
            }
            else
            {
                ownerA = other;
                ownerB = owner;
            }

            var summary = new CompactedSummary
            {
                PairKey = pairKey,
                OwnerA = ownerA,
                OwnerB = ownerB,
                SummaryText = text,
                CoveredFromMs = coveredFromMs,
                CoveredUntilMs = coveredUntilMs,
                FoldedEntryCount = foldedEntryCount,
                CreatedAt = now,
            };
            mgr.Memory.SetSummary(summary);

            // Mark each newly-selected entry as folded. Previously-folded entries
            // retain their flag (they remain represented by this rebuilt summary).
            foreach (var e in toFold)
            {
                e.IsFolded = true;
            }

            return true;
        }

        // §8.1 verbatim. Kept as a single string literal so future prompt edits
        // are diff-localized to this method.
        static string BuildSystemPrompt()
        {
            return
                "你正在为下一次对话准备背景资料：把两位武侠人物的过往对话压缩成结构化摘要，供他们再次相遇时作为上下文使用。\n"
                + "\n"
                + "仅输出中文。简洁但保留任何会影响他们今后关系的信息。\n"
                + "\n"
                + "记录对白内容时使用「某人说/声称/暗示/抱怨」等归属句式，避免把任一方的言论当作既成事实（人物可能撒谎、虚张声势、口是心非）。\n"
                + "\n"
                + "按以下四个固定槽位输出，每槽位 1-2 句话，缺槽位写\"无\"：\n"
                + "关系：<两人当前的态度倾向，敌意/警惕/好奇/亲近 等>\n"
                + "共同经历：<具体发生过的事，包括地点、物件、动作>\n"
                + "未解矛盾：<尚未化解的冲突或承诺>\n"
                + "关键事实：<任一方陈述过的具体事实，用归属句式>";
        }

        // §8.2: name + short Bio.Identity (first sentence, ≤50 chars) for each side,
        // followed by the transcript block. Falls back to raw GameId.Value when Bio
        // is missing or BioName is empty (Plan §8.2 fallback).
        static string BuildUserBody(
            AITavernManager mgr, GameId self, GameId other, List<MemoryEntry> transcripts)
        {
            var sb = new System.Text.StringBuilder(512);

            string selfHeader = FormatParticipantHeader(mgr, self);
            string otherHeader = FormatParticipantHeader(mgr, other);

            sb.Append("对话双方：").Append(selfHeader).Append("与 ").Append(otherHeader).Append('\n');
            sb.Append('\n');
            sb.Append("[需要压缩的对话历史]\n");

            bool firstTranscript = true;
            foreach (var e in transcripts)
            {
                if (string.IsNullOrWhiteSpace(e.Description)) continue;
                if (!firstTranscript) sb.Append("---\n");
                sb.Append(e.Description);
                if (!e.Description.EndsWith("\n")) sb.Append('\n');
                firstTranscript = false;
            }

            return sb.ToString();
        }

        // Produces either "<BioName>（<short>）" when a bio is wired, or just
        // "<id>" when the bio is null / BioName is empty (Plan §8.2 fallback).
        static string FormatParticipantHeader(AITavernManager mgr, GameId id)
        {
            Agent agent = mgr.NPCs != null ? mgr.NPCs.Get(id) : null;
            CharacterBio bio = agent != null ? agent.Bio : null;

            if (bio == null || string.IsNullOrEmpty(bio.BioName))
            {
                // Fallback: raw id, no parenthetical.
                return id.Value ?? string.Empty;
            }

            string shortLine = ShortIdentity(bio.Identity);
            if (string.IsNullOrEmpty(shortLine))
            {
                return bio.BioName;
            }
            return bio.BioName + "（" + shortLine + "）";
        }

        // First sentence of Identity, truncated to 50 chars. "Sentence" = up to
        // the first Chinese-period / period / newline (whichever comes first).
        // Returns empty if Identity is null/empty.
        static string ShortIdentity(string identity)
        {
            if (string.IsNullOrEmpty(identity)) return string.Empty;
            int end = -1;
            for (int i = 0; i < identity.Length; i++)
            {
                char c = identity[i];
                if (c == '。' || c == '.' || c == '\n' || c == '\r')
                {
                    end = i;
                    break;
                }
            }
            string first = end >= 0 ? identity.Substring(0, end) : identity;
            first = first.Trim();
            if (first.Length > 50) first = first.Substring(0, 50);
            return first;
        }

        // Mirrors MemoryStash.CanonicalPair (which is private). Must use the same
        // string.CompareOrdinal logic so SetSummary stores under the same key
        // GetSummary will retrieve.
        static string CanonicalPairKey(GameId a, GameId b)
        {
            return string.CompareOrdinal(a.Value, b.Value) <= 0
                ? $"{a}|{b}"
                : $"{b}|{a}";
        }
    }
}
