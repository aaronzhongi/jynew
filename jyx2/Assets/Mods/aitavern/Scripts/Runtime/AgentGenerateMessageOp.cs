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
                // T3D.5 — the coexistence flip (Plan §6.1 / §8): the Phase 2
                // BuildPriorMemoryBlock prior-memory computation is REMOVED.
                // Start/Continue continuity context now comes entirely from
                // the assembler's §5.5.2 ReflectionSummary + §5.5.3 episodic
                // ring (the §5.5.3 ring also being the sole live-transcript
                // source — AppendTranscript was deleted from the builders).

                // Thread the agents/mgr/clock through so ConversationPrompts
                // can PREPEND the ContextAssembler §1-§5 block (Plan §6).
                long promptNow = mgr.Clock != null ? mgr.Clock.NowMs() : 0L;

                ConversationPrompts.Built built;
                switch (args.Type)
                {
                    case MessageGenerationType.Start:
                        built = ConversationPrompts.BuildStart(selfBio, otherBio,
                            talker: agent, talkee: otherAgent, mgr: mgr, now: promptNow);
                        break;
                    case MessageGenerationType.Continue:
                        built = ConversationPrompts.BuildContinue(selfBio, otherBio, conv,
                            talker: agent, talkee: otherAgent, mgr: mgr, now: promptNow);
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

            // ---- Episodic ring (Plan §5.1 co-location) ----
            // Co-located with the AddMessage success above (NOT inside
            // Conversation.AddMessage). Synchronous, no Grok; all guarded
            // returns so it can't throw into the turn path. Skipped on the
            // catch path above (don't ring a failed add).
            EpisodicRing.Record(mgr, conv, agent.PlayerId, text, now);

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

        // T3D.6 (Plan §5.4 / §8): the Phase 2 `BuildPriorMemoryBlock`
        // prior-memory builder and its sole-use `Render` helper were DELETED
        // here. They had zero callers after T3D.5 stopped computing the block
        // (the assembler's §5.5.2 ReflectionSummary + §5.5.3 episodic ring are
        // now the single continuity/transcript source). Their Phase 2 tests
        // were rewritten to the §5.3 4-slot / 3D reality in the same task.
        // `StubLine` below is still live (RunAsync's missing-bio / Grok-fail
        // fallback).

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
