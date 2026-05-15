// T11: agentGenerateMessage operation runner (Plan §3.6 / §4.7).
//
// Looks up the target Conversation, picks the right ConversationPrompts
// builder (Start / Continue / Leave), calls Grok, posts the result through
// Conversation.AddMessage (which clears the typing lock), and fires the
// OnMessageGenerated static event so AITavernBubbleUI (in the Bridge /
// Assembly-CSharp side) can render it.
//
// Why a static event for UI dispatch:
//   AITavern.Runtime.asmdef cannot reference Assembly-CSharp (one-way
//   visibility: Bridge sees Runtime, not the other way around). T15
//   subscribes AITavernBubbleUI.EnqueueMessage to this event during boot.
//
// Typing-lock contract (INV-3.10-6 / T8): the caller (AgentDecision.Tick
// BRANCH 17/18/20-24) has ALREADY acquired the lock via SetIsTyping with
// the same author this op posts as. AddMessage validates the author
// matches and clears the lock; if the lock was stolen / timed out we let
// the exception bubble through a log and bail — the next tick retries.
//
// Stub fallback: if the Grok client is missing/unset, the persona bios are
// missing, or the HTTP call throws, we substitute a short canned line so
// the conversation still makes progress. INV-3.10-5 ("NPC waits awkward
// when player is creator") is enforced upstream in AgentDecision; this op
// only runs after that gate has fired GenerateMessage.

using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Jyx2.AITavern
{
    public static class AgentGenerateMessageOp
    {
        /// <summary>
        /// Fires after a message is appended to the transcript. Subscribers
        /// (typically AITavernBubbleUI.EnqueueMessage, wired in T15) receive
        /// (headId, text). Bubble UI is NPC-side only — the player's own
        /// messages take a different routing path through the speak panel.
        /// </summary>
        public static event Action<int, string> OnMessageGenerated;

        public static async UniTask RunAsync(Agent agent, AITavernManager mgr, GenerateMessageArgs args)
        {
            if (agent == null || mgr == null || args == null) return;

            // ---- Locate conversation ----
            Conversation conv = null;
            if (mgr.Conversations != null)
            {
                foreach (var c in mgr.Conversations.Active)
                {
                    if (c.Id == args.ConversationId) { conv = c; break; }
                }
            }
            if (conv == null) { Debug.LogWarning($"[GenMsg] conv {args.ConversationId} not found in Active table"); return; }

            // ---- Look up the other agent's bio context ----
            Agent otherAgent = mgr.NPCs != null ? mgr.NPCs.Get(args.OtherPlayerId) : null;
            CharacterBio selfBio = agent.Bio;
            CharacterBio otherBio = otherAgent != null ? otherAgent.Bio : null;

            // ---- Build prompt + call Grok (with stub fallback) ----
            string text = null;
            if (selfBio == null || otherBio == null || mgr.Grok == null)
            {
                // Missing bios or no Grok client wired — Phase 1 stub line.
                Debug.LogWarning($"[GenMsg] stub fallback agent={agent.AgentId}: selfBio={(selfBio != null ? "ok" : "null")}, otherBio={(otherBio != null ? "ok" : "null")}, grok={(mgr.Grok != null ? "ok" : "null")}");
                text = StubLine(args.Type, selfBio);
            }
            else
            {
                // Pull prior conversations between these two from MemoryStash
                // so Start/Continue prompts have continuity context and the model
                // stops looping on the same opener. Leave-type doesn't need this
                // — the farewell only depends on the current transcript.
                string priorMemory = BuildPriorMemoryBlock(mgr, agent.PlayerId, args.OtherPlayerId);

                // Phase 3A: thread the agents/mgr/clock through so
                // ConversationPrompts can PREPEND the ContextAssembler
                // §1-§4 static block ahead of the retained Phase 2
                // identity+priorMemory content (Plan §8 coexistence).
                long promptNow = mgr.Clock != null ? mgr.Clock.NowMs() : 0L;

                ConversationPrompts.Built built;
                switch (args.Type)
                {
                    case MessageGenerationType.Start:
                        built = ConversationPrompts.BuildStart(selfBio, otherBio,
                            talker: agent, talkee: otherAgent, mgr: mgr, now: promptNow,
                            priorMemory: priorMemory);
                        break;
                    case MessageGenerationType.Continue:
                        built = ConversationPrompts.BuildContinue(selfBio, otherBio, conv,
                            talker: agent, talkee: otherAgent, mgr: mgr, now: promptNow,
                            priorMemory: priorMemory);
                        break;
                    case MessageGenerationType.Leave:
                        built = ConversationPrompts.BuildLeave(selfBio, otherBio, conv,
                            talker: agent, talkee: otherAgent, mgr: mgr, now: promptNow);
                        break;
                    default:
                        text = StubLine(args.Type, selfBio);
                        built = default;
                        break;
                }

                if (text == null)
                {
                    try
                    {
                        text = await mgr.Grok.CompleteChatAsync(
                            built.SystemPrompt,
                            built.Messages,
                            maxTokens: 200,
                            stopSequences: new[] { "\n\n", "User:", "Assistant:" },
                            temperature: 0.85);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AgentGenerateMessageOp] Grok call failed: {e.Message}. Using stub line.");
                        text = StubLine(args.Type, selfBio);
                    }
                }

                if (string.IsNullOrWhiteSpace(text))
                    text = StubLine(args.Type, selfBio);
            }

            // ---- Post the message ----
            // AddMessage clears IsTyping when the author matches the lock holder
            // (INV-3.10-6 / T8). The caller (AgentDecision) acquired the lock
            // synchronously before scheduling this op.
            long now = mgr.Clock != null ? mgr.Clock.NowMs() : 0L;
            try
            {
                conv.AddMessage(agent.PlayerId, text, now);
            }
            catch (Exception e)
            {
                // Lock was stolen / cleared / conversation stopped. Don't touch
                // the lock here — the next tick will retry from a clean state.
                Debug.LogWarning($"[AgentGenerateMessageOp] AddMessage failed: {e.Message}");
                return;
            }

            // ---- Render in the bubble UI ----
            try
            {
                int headId = selfBio != null ? selfBio.HeadId : 0;
                OnMessageGenerated?.Invoke(headId, text);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // ---- Leave-type: end the conversation after the farewell ----
            // AddMessage already appended the farewell line; now Leave so
            // Conversation.Stop fires (writes LastConversation / ToRemember
            // on participants, flushes MemoryStash, etc.).
            if (args.Type == MessageGenerationType.Leave)
            {
                try { conv.Leave(agent.PlayerId, now); }
                catch (Exception e) { Debug.LogWarning($"[AgentGenerateMessageOp] Leave failed: {e.Message}"); }
            }
        }

        // Phase 2 (Plan §4): Build the prior-memory block injected into Start /
        // Continue system prompts. Pulls the per-pair CompactedSummary plus all
        // un-folded Conversation MemoryEntries for (self, other), sorted by
        // EndedAt ascending. The most recent transcript is tagged
        // [刚刚结束的对话] so the model can distinguish "just happened" from
        // older history. If the rendered block exceeds the configured budget
        // (chars × MEMORY_CONTEXT_HARD_OVERFLOW_FACTOR), the oldest un-folded
        // transcripts are dropped one at a time as a deterministic read-side
        // soft-truncate fallback. The summary and the most recent transcript
        // are always retained.
        internal static string BuildPriorMemoryBlock(AITavernManager mgr, GameId self, GameId other)
        {
            if (mgr == null || mgr.Memory == null) return null;

            // 1) Per-pair compacted summary (may be null).
            var summary = mgr.Memory.GetSummary(self, other);

            // 2) All un-folded Conversation entries for this pair.
            var transcripts = new System.Collections.Generic.List<MemoryEntry>();
            foreach (var e in mgr.Memory.ForOwner(self))
            {
                if (e.Type != MemoryType.Conversation) continue;       // Relationship / Reflection never belong here.
                if (!e.Target.HasValue) continue;
                if (!e.Target.Value.Equals(other)) continue;
                if (e.IsFolded) continue;                              // Folded content is represented by `summary`.
                if (string.IsNullOrWhiteSpace(e.Description)) continue;
                transcripts.Add(e);
            }
            transcripts.Sort((a, b) => a.EndedAt.CompareTo(b.EndedAt));

            // 3) Nothing to inject — caller drops the block from the prompt.
            if (summary == null && transcripts.Count == 0) return null;

            // 4) Resolve the other agent's display name for the header.
            string otherName = other.Value;
            var otherAgent = mgr.NPCs != null ? mgr.NPCs.Get(other) : null;
            if (otherAgent != null && otherAgent.Bio != null && !string.IsNullOrEmpty(otherAgent.Bio.BioName))
                otherName = otherAgent.Bio.BioName;

            // 5) Render once. If we overflow budget × 1.5, drop the oldest
            //    transcript and re-render. Always retain the summary and at
            //    least the most recent transcript so [刚刚结束的对话] survives.
            float hardLimit = AITavernConstants.MEMORY_CONTEXT_BUDGET_CHARS
                * AITavernConstants.MEMORY_CONTEXT_HARD_OVERFLOW_FACTOR;
            bool truncated = false;
            string rendered = Render(otherName, summary, transcripts, truncated);
            int safety = transcripts.Count; // bounded iterations — worst case 1 transcript left
            while (rendered.Length > hardLimit && transcripts.Count > 1 && safety-- > 0)
            {
                transcripts.RemoveAt(0); // drop oldest
                truncated = true;
                rendered = Render(otherName, summary, transcripts, truncated);
            }
            return rendered;
        }

        // Render the prior-memory block from the resolved inputs. `truncated`
        // controls whether the "missing earlier history" notice is prepended.
        static string Render(
            string otherName,
            CompactedSummary summary,
            System.Collections.Generic.List<MemoryEntry> transcripts,
            bool truncated)
        {
            var sb = new StringBuilder();
            sb.Append("你与 ").Append(otherName).Append(" 的过往：\n");

            if (truncated)
                sb.Append("（更早的对话因故未能保留）\n");

            if (summary != null)
            {
                sb.Append("[较早｜摘要]\n");
                sb.Append(summary.SummaryText);
                if (summary.SummaryText == null || !summary.SummaryText.EndsWith("\n"))
                    sb.Append('\n');
            }

            if (transcripts.Count > 0)
            {
                sb.Append("[较近｜逐字]\n");
                int last = transcripts.Count - 1;
                for (int i = 0; i < transcripts.Count; i++)
                {
                    // The LAST transcript is preceded by [刚刚结束的对话]; all
                    // earlier ones use the --- separator. The first transcript
                    // (when it's NOT also the last) is emitted without a leading
                    // separator since the [较近｜逐字] header already separates it.
                    if (i == last)
                    {
                        sb.Append("[刚刚结束的对话]\n");
                    }
                    else if (i > 0)
                    {
                        sb.Append("---\n");
                    }
                    var text = transcripts[i].Description;
                    sb.Append(text);
                    if (text == null || !text.EndsWith("\n")) sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        // Fallback canned lines used when bios are missing or Grok fails.
        // Brief and in-character-agnostic so they slot into any persona.
        static string StubLine(MessageGenerationType type, CharacterBio bio)
        {
            string name = bio != null && !string.IsNullOrEmpty(bio.BioName) ? bio.BioName : "...";
            switch (type)
            {
                case MessageGenerationType.Start:    return $"({name} 开口) ...";
                case MessageGenerationType.Continue: return "嗯。";
                case MessageGenerationType.Leave:    return "告辞。";
                default:                             return "...";
            }
        }
    }
}
