// Phase 3D — T3D.3 (Plan §5.3): conversation-end reflection-consolidation
// for one (owner, partner) pair. This is the human-memory "sleep
// consolidation" bridge — volatile episodic experience (the raw spill
// the ring evicted + the surviving ring) is re-folded into a durable
// per-pair ReflectionSummary AND emits emotion/affection deltas judged
// FROM THE RAW TURNS (not the decayed Affect.Current).
//
// REPLACES the Phase 2 MemoryCompactor BODY (the 20k-char budget
// projection, the FacetSlotPrefixes 关系/共同经历/未解矛盾/关键事实
// parser, BuildSystemPrompt/BuildUserBody) per Plan §5.4. KEEPS the
// proven Phase 2 discipline that §5.3 explicitly inherits:
//   - re-fold ALWAYS from RAW (the prior ReflectionSummary is DISCARDED
//     as Grok input — anti-degradation, bounded to one Grok hop).
//   - exactly ONE CompactedSummary per pair, overwritten (CanonicalPair
//     keying — same string.CompareOrdinal logic MemoryStash uses).
//   - no summary-of-summary chaining.
//   - Grok-failure stance: log, return false, leave ring/spill intact so
//     the next conv-end retries (nothing consumed/cleared on failure).
//
// Trigger is AgentRememberConversationOp post-Conversation.Stop, firing
// PER non-human participant — so each call consolidates exactly ONE
// (owner = agent.PlayerId, partner) pair. There is NO size/budget gate:
// conv-end consolidation ALWAYS runs (the trigger is the conv ending,
// not the block growing).
//
// The cross-person GlobalReflection fold (§5.0 / §5.3.1) is T3D.4 — a
// marker is left at the AgentRememberConversationOp seam.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Jyx2.AITavern
{
    public static class MemoryCompactor
    {
        // §5.3 4-slot output prefixes. The parser keys on these EXACT
        // strings; the system prompt instructs the model to emit exactly
        // these four labeled lines. (Replaces the Phase 2 FacetSlotPrefixes
        // 关系/共同经历/未解矛盾/关键事实 — Plan §5.4.)
        const string SLOT_IMPRESSION   = "往来印象：";   // → durable ReflectionSummary
        const string SLOT_EMOTION      = "情绪变化：";   // → emotion label + intensity (from RAW)
        const string SLOT_AFFECTION    = "好恶变化：";   // → signed affection delta (from RAW)
        const string SLOT_CONTRADICTION = "违背设定：";  // → canon-drift tripwire

        // Default emotion intensity when the model emits a non-neutral
        // label but no parseable 0..1 magnitude (tolerant parse, §5.3 step 4).
        const float EMOTION_DEFAULT_INTENSITY = 0.5f;

        // ImpressionDelta runtime overlay (§4 / §5.3): bounded — keep the
        // last few lines only so it can't unbounded-grow across many
        // conversations. This is the runtime delta the assembler renders
        // as ［本局所历］; the immutable CharacterDossier asset is NEVER
        // written here.
        const int IMPRESSION_DELTA_MAX_ENTRIES = 3;
        const int IMPRESSION_DELTA_MAX_CHARS   = 600;

        // Phase 3D — T3D.3: conversation-end reflection-consolidation for
        // one (owner, partner) pair. ALWAYS runs at conv-end (no budget
        // gate). Returns true iff a durable summary was written; false on
        // nothing-to-consolidate or Grok/parse failure (ring + spill left
        // intact for the next conv-end to retry).
        public static async UniTask<bool> ConsolidateForOwner(
            AITavernManager mgr, GameId owner, GameId other, long now)
        {
            if (mgr == null || mgr.Memory == null) return false;

            // ---- Step 2: collect this conversation's RAW (re-fold-from-raw,
            // Phase 2 discipline). The Grok INPUT is the raw spill the ring
            // evicted (T3D.2 [spill] lines + any Phase 2 full-conv blobs)
            // PLUS the still-in-ring turns not yet spilled. The prior
            // ReflectionSummary is NOT included (§5.3 anti-degradation —
            // re-fold from raw only, never from the previous summary).
            //
            // Raw spill: un-folded Conversation entries for this pair,
            // oldest-first. Relationship/Reflection types are exempt (never
            // folded — Phase 2 invariant carried over).
            var rawSpill = new List<MemoryEntry>();
            foreach (var e in mgr.Memory.ForOwner(owner))
            {
                if (e.Type != MemoryType.Conversation) continue;
                if (!e.Target.HasValue) continue;
                if (!e.Target.Value.Equals(other)) continue;
                if (e.IsFolded) continue;             // already represented by a prior fold
                rawSpill.Add(e);
            }
            rawSpill.Sort((a, b) => a.EndedAt.CompareTo(b.EndedAt));  // oldest-first

            // The surviving ring (turns evicted-but-not-yet-spilled are in
            // rawSpill; the ones still in the ring are this conversation's
            // raw too — §5.1). T3C.2 seeds Targets at spawn; create
            // defensively if absent.
            var mind = mgr.GetOrCreateMind(owner);
            if (mind.Targets == null)
                mind.Targets = new Dictionary<GameId, TargetState>();
            if (!mind.Targets.TryGetValue(other, out var ts) || ts == null)
            {
                ts = new TargetState();
                mind.Targets[other] = ts;
            }
            if (ts.Ring == null) ts.Ring = new List<TurnRecord>();

            // ---- Build the raw concatenation (oldest-first): spill
            // Descriptions, then the ring turns as "Speaker: Text".
            var rawBody = new StringBuilder(1024);
            int rawPieces = 0;
            foreach (var e in rawSpill)
            {
                if (string.IsNullOrWhiteSpace(e.Description)) continue;
                if (rawPieces > 0) rawBody.Append('\n');
                rawBody.Append(e.Description.Trim());
                rawPieces++;
            }
            foreach (var tr in ts.Ring)
            {
                if (tr == null || string.IsNullOrWhiteSpace(tr.Text)) continue;
                if (rawPieces > 0) rawBody.Append('\n');
                rawBody.Append(tr.Speaker.Value ?? "?").Append(": ").Append(tr.Text.Trim());
                rawPieces++;
            }

            // Nothing happened for this pair → nothing to consolidate.
            if (rawPieces == 0) return false;

            // ---- Step 3: the §5.3 4-slot Grok call. Prior ReflectionSummary
            // is NOT in the prompt (re-fold from raw only).
            string systemPrompt = BuildReflectSystemPrompt();
            string userBody = BuildReflectUserBody(mgr, owner, other, rawBody.ToString());

            string raw;
            try
            {
                if (mgr.Grok == null)
                {
                    Debug.LogWarning(
                        $"[Reflect] no Grok client wired; skipping consolidation for owner={owner}");
                    return false;
                }
                raw = await mgr.Grok.CompleteChatAsync(
                    systemPrompt,
                    new List<(string, string)> { ("user", userBody) },
                    maxTokens: AITavernConstants.REFLECT_MAX_TOKENS,
                    stopSequences: new[] { "User:", "Assistant:" },
                    temperature: AITavernConstants.REFLECT_TEMPERATURE);
            }
            catch (Exception ex)
            {
                // §5.3 failure stance (mirrors Phase 2): log, bail, leave
                // ring + spill UNTOUCHED so the next conv-end retries.
                Debug.LogWarning(
                    $"[Reflect] grok failed for owner={owner} other={other}: {ex.Message}");
                return false;
            }

            string text = raw != null ? raw.Trim() : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning($"[Reflect] empty response for owner={owner} other={other}");
                return false;   // treat as Grok failure — nothing consumed
            }

            // ---- Step 4: parse the 4 slots (robust — text after each
            // prefix up to the next prefix / end).
            string summaryText  = ExtractSlot(text, SLOT_IMPRESSION);
            string emotionRaw   = ExtractSlot(text, SLOT_EMOTION);
            string affectionRaw = ExtractSlot(text, SLOT_AFFECTION);
            string contradiction = ExtractSlot(text, SLOT_CONTRADICTION);

            // 往来印象 is REQUIRED. Missing/blank → treat as Grok failure
            // (nothing consumed, retry next conv-end).
            if (string.IsNullOrWhiteSpace(summaryText))
            {
                string head = text.Length > 80 ? text.Substring(0, 80) : text;
                Debug.LogWarning(
                    $"[Reflect] 往来印象 slot missing owner={owner} other={other}; raw={head}");
                return false;
            }
            summaryText = summaryText.Trim();

            ParseEmotion(emotionRaw, out string emotionLabel, out float emotionIntensity,
                         out bool emotionNeutral);
            float affDelta = ParseAffectionDelta(affectionRaw);
            bool hasContradiction = HasContradiction(contradiction);

            // ---- Step 5: write the durable summary (§5.4). This op is the
            // SOLE writer of BOTH backings so they cannot diverge — set them
            // to the SAME text. ReflectionSummary is the live overlay the
            // assembler reads; CompactedSummary is the durable Phase-2-shaped
            // backing (one per pair, overwritten, CanonicalPair-keyed).
            ts.ReflectionSummary = summaryText;

            long coveredFromMs = long.MaxValue, coveredUntilMs = long.MinValue;
            int foldedEntryCount = 0;
            foreach (var e in rawSpill)
            {
                foldedEntryCount++;
                if (e.EndedAt < coveredFromMs) coveredFromMs = e.EndedAt;
                if (e.EndedAt > coveredUntilMs) coveredUntilMs = e.EndedAt;
            }
            foreach (var tr in ts.Ring)
            {
                if (tr == null) continue;
                if (tr.Ms < coveredFromMs) coveredFromMs = tr.Ms;
                if (tr.Ms > coveredUntilMs) coveredUntilMs = tr.Ms;
            }
            if (coveredFromMs == long.MaxValue) { coveredFromMs = now; coveredUntilMs = now; }

            GameId ownerA, ownerB;
            if (string.CompareOrdinal(owner.Value, other.Value) <= 0) { ownerA = owner; ownerB = other; }
            else { ownerA = other; ownerB = owner; }

            mgr.Memory.SetSummary(new CompactedSummary
            {
                PairKey = CanonicalPairKey(owner, other),
                OwnerA = ownerA,
                OwnerB = ownerB,
                SummaryText = summaryText,
                CoveredFromMs = coveredFromMs,
                CoveredUntilMs = coveredUntilMs,
                FoldedEntryCount = foldedEntryCount,
                CreatedAt = now,
            });

            // ---- Step 6: salience gate (§5.3, ai-town non-negotiable).
            // Chit-chat (|好恶变化| < deadband AND 情绪变化 neutral) STILL
            // folds the summary (above) but does NOT apply the affect deltas
            // and does NOT touch LastSetMs — so the baseline-reversion decay
            // (§4.4) can actually fire for a chatty pair.
            bool trivial = Math.Abs(affDelta) < AITavernConstants.AFFECT_DELTA_DEADBAND
                           && emotionNeutral;

            if (!trivial)
            {
                // ---- Step 7: apply affect deltas FROM THE MODEL'S READ OF
                // THE RAW TURNS (the parsed slots), NEVER from
                // Affect.Current(now). A dramatic mid-conv beat must survive
                // into durable disposition even if its mood already decayed
                // below floor before consolidation ran (§5.3 must-fix / §9
                // raw-not-decayed regression test).

                // Emotion reverts to 平静/0 → Baseline 0, short half-life.
                if (mind.Emotion == null) mind.Emotion = new Affect();
                mind.Emotion.Label      = emotionLabel;
                mind.Emotion.Value      = Mathf.Clamp01(emotionIntensity);
                mind.Emotion.Baseline   = 0f;
                mind.Emotion.HalfLifeMs = AITavernConstants.EMOTION_HALFLIFE_MS;
                mind.Emotion.LastSetMs  = now;

                // Affection: T3C.2 seeded Baseline (canon RelationType) +
                // AFFECTION_HALFLIFE_MS — DO NOT overwrite Baseline or
                // HalfLifeMs. Only Value (adjusted by the model's delta) and
                // LastSetMs move; decay (§4.4) then relaxes Value back toward
                // the canon Baseline over AFFECTION_HALFLIFE_MS.
                var aff = ts.Affection;
                if (aff == null)
                {
                    // Defensive: T3C.2 normally seeds this. With no canon
                    // baseline available here, fall back to a neutral 0
                    // baseline + the canon half-life (decay still reverts).
                    aff = new Affect
                    {
                        Value      = 0f,
                        Baseline   = 0f,
                        HalfLifeMs = AITavernConstants.AFFECTION_HALFLIFE_MS,
                    };
                    ts.Affection = aff;
                }
                aff.Value = Mathf.Clamp(aff.Value + affDelta, -1f, 1f);
                aff.LastSetMs = now;
                if (!string.IsNullOrWhiteSpace(emotionLabel) && !emotionNeutral)
                    aff.Label = emotionLabel;   // optional short standing hint
            }

            // ---- Step 8: ImpressionDelta runtime overlay (§5.3 / §4).
            // Bounded append derived from 往来印象. This is the runtime
            // overlay the assembler renders as ［本局所历］ over the
            // immutable CharacterDossier.PersonView.Impression — the Dossier
            // ASSET is NEVER written here.
            ts.ImpressionDelta = AppendImpressionDelta(ts.ImpressionDelta, summaryText, now);

            // ---- Step 9: 违背设定 tripwire (§5.3) — non-fatal drift log
            // for licensed IP.
            if (hasContradiction)
            {
                Debug.LogWarning(
                    $"[Reflect] canon-contradiction owner={owner} other={other}: {contradiction.Trim()}");
            }

            // ---- Step 10: free the raw buffer (§5.3 "the raw buffer is
            // freed"; the T3D.2 `// T3D.3:` marker). Only on SUCCESSFUL
            // fold+write: mark consumed spill entries IsFolded (reuse the
            // Phase 2 flag so they're not re-folded next conv) AND clear the
            // ring (so the next conversation's first turn becomes a fresh
            // anchor — §5.1). On Grok/parse failure NONE of this runs (every
            // failure path above already returned).
            foreach (var e in rawSpill) e.IsFolded = true;
            ts.Ring.Clear();

            return true;
        }

        // Phase 3D — T3D.4 (Plan §5.0 / §5.3.1): the cross-person
        // GlobalReflection fold. ONE extra Grok call at conversation-end,
        // AFTER per-pair consolidation (ConsolidateForOwner) has run.
        //
        // INPUT = the UNION of THIS owner's non-blank
        // Targets[*].ReflectionSummary — the ALREADY-DISTILLED per-pair
        // summaries, NOT raw turns. This is a FLAT fold OVER SUMMARIES, and
        // §5.3.1 explicitly defines it that way: the anti-degradation
        // "no summary-of-summary" rule applies to the PER-PAIR fold
        // (ConsolidateForOwner, which re-folds from RAW), NOT to this global
        // fold. So folding summaries here is correct and is NOT a
        // degradation path — do not "fix" it into a raw re-fold.
        //
        // OUTPUT = mind.GlobalReflection ONLY. This method READS
        // Targets[*].ReflectionSummary and WRITES nothing but
        // mind.GlobalReflection. It must NEVER write any
        // Targets[*].ReflectionSummary, and GlobalReflection must NEVER be
        // fed back as an input to any per-pair fold — it is OUTPUT-only,
        // flat, no recursion / no feedback loop (§5.3.1). A future
        // maintainer must NOT wire GlobalReflection into ConsolidateForOwner.
        //
        // Cross-pair staleness is INTENTIONAL (§5.3.1): we read the CURRENT
        // state of every Targets[*].ReflectionSummary. Only the just-
        // finished pair was refreshed this conv-end; the others are as of
        // their last interaction. That is "what I last gathered about
        // everyone," NOT an omniscient resync — do NOT force-refold the
        // other pairs to "freshen" them (that reintroduces the cost +
        // degradation the re-fold-from-raw / salience-gate rules prevent).
        //
        // Gate: only runs when the owner has ≥2 non-blank per-pair
        // summaries. With 0 or 1, the global slot would just mirror the
        // single pair → SKIP and leave mind.GlobalReflection UNCHANGED
        // (do NOT clear an existing one). Best-effort: a Grok null/throw/
        // empty here logs `[Reflect-Global]` and returns WITHOUT touching
        // mind.GlobalReflection — a failed global fold must NOT corrupt the
        // per-pair state ConsolidateForOwner already successfully wrote.
        public static async UniTask FoldGlobalReflection(
            AITavernManager mgr, GameId owner, long now)
        {
            if (mgr == null || mgr.Grok == null) return;

            var mind = mgr.GetOrCreateMind(owner);
            if (mind == null || mind.Targets == null) return;

            // ---- Collect the union of THIS owner's non-blank, already-
            // distilled per-pair ReflectionSummary values (current state —
            // cross-pair staleness is intentional, see header comment).
            var pairs = new List<(string name, string summary)>();
            foreach (var kv in mind.Targets)
            {
                var ts = kv.Value;
                if (ts == null || string.IsNullOrWhiteSpace(ts.ReflectionSummary))
                    continue;
                pairs.Add((FormatParticipantHeader(mgr, kv.Key),
                           ts.ReflectionSummary.Trim()));
            }

            // ≥2 gate (§5.3.1): with <2 summaries the global slot would just
            // mirror the single pair. SKIP — and do NOT clear an existing
            // mind.GlobalReflection (leave it exactly as-is).
            if (pairs.Count < 2) return;

            // ---- The one extra Grok fold. Flat over the per-pair summaries
            // (NOT raw turns) — same call plumbing/failure stance as
            // ConsolidateForOwner.
            string systemPrompt = BuildGlobalReflectSystemPrompt();
            string userBody = BuildGlobalReflectUserBody(mgr, owner, pairs);

            string raw;
            try
            {
                raw = await mgr.Grok.CompleteChatAsync(
                    systemPrompt,
                    new List<(string, string)> { ("user", userBody) },
                    maxTokens: AITavernConstants.REFLECT_MAX_TOKENS,
                    stopSequences: new[] { "User:", "Assistant:" },
                    temperature: AITavernConstants.REFLECT_TEMPERATURE);
            }
            catch (Exception ex)
            {
                // Best-effort: log + bail WITHOUT touching GlobalReflection.
                // The per-pair consolidation already succeeded this conv-end;
                // a failed global fold must not corrupt or undo it.
                Debug.LogWarning(
                    $"[Reflect-Global] grok failed for owner={owner}: {ex.Message}");
                return;
            }

            string text = raw != null ? raw.Trim() : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning(
                    $"[Reflect-Global] empty response for owner={owner}; leaving GlobalReflection unchanged");
                return;   // best-effort — leave existing GlobalReflection intact
            }

            // ---- Success: write ONLY mind.GlobalReflection (a single
            // ≤2-sentence blob). Trust the prompt + REFLECT_MAX_TOKENS for
            // length; a generous hard char-cap is the only guard so a
            // runaway model can't bloat the §5.0 render (T3D.5).
            const int GLOBAL_REFLECTION_MAX_CHARS = 300;
            if (text.Length > GLOBAL_REFLECTION_MAX_CHARS)
                text = text.Substring(0, GLOBAL_REFLECTION_MAX_CHARS).Trim();
            mind.GlobalReflection = text;
        }

        // ---- §5.3.1 global-fold system prompt. Chinese; instructs: you are
        // <owner>; below are your CURRENT impressions of several people you
        // know (already-distilled per-pair summaries); in ≤2 sentences state
        // the cross-person pattern/undercurrent you sense among them AS A
        // WHOLE, in attribution句式. Do NOT restate each impression — that
        // is the per-pair fold's job; here synthesize the shared pattern.
        static string BuildGlobalReflectSystemPrompt()
        {
            return
                "你在做「跨人反思」：下面是某武侠人物此刻对他所认识的几个人各自的印象（每条都是已经沉淀好的逐对印象，不是原始对话）。\n"
                + "\n"
                + "仅输出中文。请站在这个人物的角度，综观这几条印象，用不超过两句话点出你从这群人身上整体感到的共同点或暗流（例如「在场众人皆回避木箱话题、各怀心事」）。\n"
                + "\n"
                + "用归属句式（「我察觉」「众人似乎」「他们都」等），不要逐条复述每个人的印象——那是逐对记忆已经做过的；这里只综合出整体的模式或暗流。\n"
                + "\n"
                + "只输出这一两句话本身，不要加前缀、标号或解释。";
        }

        // §5.3.1 global-fold user body = owner header + one
        // 「<talkee name>」：<that pair's ReflectionSummary> line per pair.
        // These are the already-distilled per-pair summaries (flat — NOT
        // raw turns). The prior GlobalReflection is deliberately absent
        // (output-only, never fed back — no recursion, §5.3.1).
        static string BuildGlobalReflectUserBody(
            AITavernManager mgr, GameId owner, List<(string name, string summary)> pairs)
        {
            var sb = new StringBuilder(1024);
            sb.Append("反思的主人：").Append(FormatParticipantHeader(mgr, owner)).Append('\n');
            sb.Append('\n');
            sb.Append("【我对各人当前的印象】\n");
            foreach (var p in pairs)
            {
                sb.Append('「').Append(p.name).Append('」').Append('：')
                  .Append(p.summary.Replace("\n", " ").Trim()).Append('\n');
            }
            return sb.ToString();
        }

        // ---- §5.3 system prompt. Chinese; instructs: consolidating ONE
        // character's memory of a conversation with ONE other; output
        // EXACTLY the four labeled slots; attribution句式 ("他声称/据其所见",
        // not omniscient narration); the 情绪/好恶 deltas are judged from
        // the RAW transcript below, not from any prior belief.
        static string BuildReflectSystemPrompt()
        {
            return
                "你在做「记忆沉淀」：把某武侠人物刚结束的一段对话，整理成他对对方的持久记忆，并判断这段经历对他情绪与好恶的净影响。\n"
                + "\n"
                + "仅输出中文。所有判断只依据下面给出的【原始逐字对话】本身，不要臆测对话之外的事，也不要沿用任何先前印象——这是为防止记忆失真。\n"
                + "\n"
                + "复述对方言行时用归属句式（「他声称」「据其所见」「他抱怨」等），不要用全知视角把任一方的话当作既成事实（人物会撒谎、虚张声势、口是心非）。\n"
                + "\n"
                + "情绪变化、好恶变化必须依据这段原始对话里实际发生的事来判断（即便其中的激烈时刻在现实里已经过去，也要如实记入沉淀）。\n"
                + "\n"
                + "严格按以下四行输出，每行以给定前缀开头，缺失内容写「空」：\n"
                + "往来印象：<2-4句，沉淀下来的关系认知，用归属句式>\n"
                + "情绪变化：<情绪词 + 强度0~1，例如「警惕 0.6」；若本局平淡写「平静」>\n"
                + "好恶变化：<带符号的好恶增量，区间[-1,1]，例如「-0.25」；若几乎无变化写「0」>\n"
                + "违背设定：<空，或一句简述：原始对话里是否有与人物设定/常识明显矛盾之处>";
        }

        // §5.3 user body = name headers (continuity, NOT prior summary) +
        // the raw concatenation from step 2. The prior ReflectionSummary is
        // deliberately absent (re-fold from raw only).
        static string BuildReflectUserBody(
            AITavernManager mgr, GameId self, GameId other, string rawConcat)
        {
            var sb = new StringBuilder(1024);
            sb.Append("记忆的主人：").Append(FormatParticipantHeader(mgr, self)).Append('\n');
            sb.Append("对话的另一方：").Append(FormatParticipantHeader(mgr, other)).Append('\n');
            sb.Append('\n');
            sb.Append("【原始逐字对话】\n");
            sb.Append(rawConcat);
            if (!rawConcat.EndsWith("\n")) sb.Append('\n');
            return sb.ToString();
        }

        // Robust slot extract: text AFTER `prefix`, up to the start of the
        // NEXT known slot prefix (or end of string). Returns null if the
        // prefix is absent.
        static string ExtractSlot(string text, string prefix)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return null;
            start += prefix.Length;

            int end = text.Length;
            foreach (var p in new[] { SLOT_IMPRESSION, SLOT_EMOTION, SLOT_AFFECTION, SLOT_CONTRADICTION })
            {
                if (string.Equals(p, prefix, StringComparison.Ordinal)) continue;
                int idx = text.IndexOf(p, start, StringComparison.Ordinal);
                if (idx >= 0 && idx < end) end = idx;
            }
            return text.Substring(start, end - start).Trim();
        }

        // 情绪变化 → (label, intensity 0..1, isNeutral). Tolerates
        // "警惕 0.6", "警惕0.6", "警惕（0.6）", or a bare "警惕"
        // (→ default intensity). Neutral labels (平静/无/none/空) → neutral.
        static void ParseEmotion(string slot, out string label, out float intensity,
                                 out bool isNeutral)
        {
            label = "平静";
            intensity = 0f;
            isNeutral = true;

            if (string.IsNullOrWhiteSpace(slot)) return;
            slot = slot.Trim();

            // Pull the first floating-point magnitude (if any).
            float? mag = ExtractFirstFloat(slot, out int magStart, out int magLen);

            // Label = the slot with the magnitude substring (and common
            // separators) stripped.
            string lbl = slot;
            if (mag.HasValue && magStart >= 0)
                lbl = (slot.Substring(0, magStart) + slot.Substring(magStart + magLen));
            lbl = lbl.Trim().Trim('（', '）', '(', ')', '，', ',', '：', ':', '、', ' ', '强', '度');
            lbl = lbl.Trim();

            if (IsNeutralLabel(lbl))
            {
                label = string.IsNullOrWhiteSpace(lbl) ? "平静" : lbl;
                intensity = 0f;
                isNeutral = true;
                return;
            }

            label = lbl;
            isNeutral = false;
            intensity = mag.HasValue ? Mathf.Clamp01(mag.Value) : EMOTION_DEFAULT_INTENSITY;
        }

        static bool IsNeutralLabel(string lbl)
        {
            if (string.IsNullOrWhiteSpace(lbl)) return true;
            switch (lbl)
            {
                case "平静":
                case "无":
                case "无明显变化":
                case "无变化":
                case "空":
                case "none":
                case "None":
                case "neutral":
                case "Neutral":
                    return true;
                default:
                    return false;
            }
        }

        // 好恶变化 → signed float clamped to [-1, 1]. Unparseable → 0.
        static float ParseAffectionDelta(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) return 0f;
            float? v = ExtractFirstFloat(slot.Trim(), out _, out _);
            if (!v.HasValue) return 0f;
            return Mathf.Clamp(v.Value, -1f, 1f);
        }

        // First signed decimal in s. Sets start/len to its span (-1 / 0 if
        // none). Handles a leading '+' or '-' and a single decimal point.
        static float? ExtractFirstFloat(string s, out int start, out int len)
        {
            start = -1; len = 0;
            if (string.IsNullOrEmpty(s)) return null;

            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                bool sign = (c == '-' || c == '+')
                            && i + 1 < s.Length
                            && (char.IsDigit(s[i + 1]) || s[i + 1] == '.');
                if (char.IsDigit(c) || sign || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    int j = i;
                    if (s[j] == '-' || s[j] == '+') j++;
                    bool dot = false;
                    while (j < s.Length && (char.IsDigit(s[j]) || (s[j] == '.' && !dot)))
                    {
                        if (s[j] == '.') dot = true;
                        j++;
                    }
                    string num = s.Substring(i, j - i);
                    if (float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture,
                                       out float parsed))
                    {
                        start = i; len = j - i;
                        return parsed;
                    }
                }
                i++;
            }
            return null;
        }

        // 违背设定 non-empty? 空/无/empty/none → no contradiction.
        static bool HasContradiction(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) return false;
            string t = slot.Trim().Trim('。', '.', '，', ',', '：', ':', ' ');
            if (t.Length == 0) return false;
            switch (t)
            {
                case "空":
                case "无":
                case "none":
                case "None":
                case "empty":
                case "N/A":
                case "n/a":
                    return false;
                default:
                    return true;
            }
        }

        // Runtime ImpressionDelta overlay (§5.3 / §4): newline-join a short
        // dated line derived from 往来印象, keep only the last few entries /
        // a char cap so it cannot unbounded-grow. NEVER touches the Dossier
        // asset — this is the assembler's ［本局所历］ source.
        static string AppendImpressionDelta(string existing, string summaryText, long now)
        {
            string marker = "[" + now.ToString(CultureInfo.InvariantCulture) + "] ";
            string line = marker + summaryText.Replace("\n", " ").Trim();

            var kept = new List<string>();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                foreach (var l in existing.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(l)) kept.Add(l.Trim());
            }
            kept.Add(line);

            // Cap by entry count (keep most recent), then by chars.
            while (kept.Count > IMPRESSION_DELTA_MAX_ENTRIES) kept.RemoveAt(0);
            string joined = string.Join("\n", kept);
            while (joined.Length > IMPRESSION_DELTA_MAX_CHARS && kept.Count > 1)
            {
                kept.RemoveAt(0);
                joined = string.Join("\n", kept);
            }
            if (joined.Length > IMPRESSION_DELTA_MAX_CHARS)
                joined = joined.Substring(joined.Length - IMPRESSION_DELTA_MAX_CHARS);
            return joined;
        }

        // Produces "<BioName>（<short Identity>）" or just "<id>" when the
        // bio is null / BioName empty. (Kept from Phase 2 — still the right
        // header shape for the reflection prompt.)
        static string FormatParticipantHeader(AITavernManager mgr, GameId id)
        {
            Agent agent = mgr.NPCs != null ? mgr.NPCs.Get(id) : null;
            CharacterBio bio = agent != null ? agent.Bio : null;

            if (bio == null || string.IsNullOrEmpty(bio.BioName))
                return id.Value ?? string.Empty;

            string shortLine = ShortIdentity(bio.Identity);
            return string.IsNullOrEmpty(shortLine) ? bio.BioName : bio.BioName + "（" + shortLine + "）";
        }

        // First sentence of Identity, truncated to 50 chars.
        static string ShortIdentity(string identity)
        {
            if (string.IsNullOrEmpty(identity)) return string.Empty;
            int end = -1;
            for (int i = 0; i < identity.Length; i++)
            {
                char c = identity[i];
                if (c == '。' || c == '.' || c == '\n' || c == '\r') { end = i; break; }
            }
            string first = end >= 0 ? identity.Substring(0, end) : identity;
            first = first.Trim();
            if (first.Length > 50) first = first.Substring(0, 50);
            return first;
        }

        // Mirrors MemoryStash.CanonicalPair (private there) — same
        // string.CompareOrdinal logic so SetSummary stores under the same
        // key GetSummary retrieves (one CompactedSummary per pair).
        static string CanonicalPairKey(GameId a, GameId b)
        {
            return string.CompareOrdinal(a.Value, b.Value) <= 0
                ? $"{a}|{b}"
                : $"{b}|{a}";
        }
    }
}
