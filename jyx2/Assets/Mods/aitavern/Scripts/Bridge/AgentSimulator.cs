// T15: Two-cadence tick loop + IOperationScheduler implementation.
//
// Lives in `Scripts/Bridge/` (no .asmdef → Assembly-CSharp) so it can drive UI
// updates (player speak panel via Jyx2_UIManager) when the player needs to
// take a turn. AgentSimulator is attached to the AITavernManager GameObject
// in AITavernBoot so it survives DontDestroyOnLoad across the Phase 3 battle
// round-trip.
//
// Per-frame work (Plan §4.2 fast cadence + INV-3.5):
//   - Conversation.Tick for every active conversation (proximity → Participating
//     transition, stale typing-lock cleanup).
//   - Player auto-accept on proximity to an outstanding NPC invite (INV-3.10-3).
//   - Open AITavernPlayerSpeakPanel when it's the player's turn to talk.
//
// Slow cadence (every 500 ms — Plan §4.2 / INV-3.10-1):
//   - AgentDecision.Tick for each non-human agent. Humans are excluded via
//     NPCRegistry.NonHumanAgents.
//
// As IOperationScheduler, dispatches operations fire-and-forget. The
// continuation ALWAYS clears agent.Operation = null in a finally block to
// honour INV-3.8-2 (one-op invariant cleanup), regardless of success/failure.
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Jyx2;
using Jyx2.AITavern;

namespace Jyx2.AITavern.Bridge
{
    /// <summary>
    /// Per-frame + 500 ms cadence tick loop, plus the IOperationScheduler
    /// implementation that fires operations and clears `agent.Operation` in a
    /// finally block (INV-3.8-2).
    /// </summary>
    public class AgentSimulator : MonoBehaviour, IOperationScheduler
    {
        public AITavernManager Manager;

        const long AGENT_DECISION_INTERVAL_MS = 500;

        long _lastAgentDecisionMs;

        bool _playerSpeakPanelOpen;
        long _playerSpeakSeenLastMessageTimestamp = -1;

        void Update()
        {
            if (Manager == null) return;
            long now = Manager.Clock != null ? Manager.Clock.NowMs() : 0L;

            // Per-frame Conversation.Tick for each active conversation (INV-3.5).
            // Snapshot first — Conversation.Stop removes itself from the table
            // during iteration, which would invalidate enumerator state.
            var snapshot = new List<Conversation>(Manager.Conversations.Active);
            foreach (var c in snapshot)
            {
                try { c.Tick(now, Manager.NPCs.GetBody); }
                catch (Exception e) { Debug.LogException(e); }
            }

            // Player auto-accept on proximity to an outstanding NPC invite
            // (INV-3.10-3). Player-turn speak-panel triggering when the player
            // owes a reply (Plan §3.10).
            HandlePlayerAutoAccept(now);
            HandlePlayerSpeakTurn(now);

            // Slow cadence: AgentDecision.Tick once every 500 ms.
            if (now - _lastAgentDecisionMs >= AGENT_DECISION_INTERVAL_MS)
            {
                _lastAgentDecisionMs = now;
                foreach (var agent in Manager.NPCs.NonHumanAgents)
                {
                    try { AgentDecision.Tick(agent, Manager, this, now); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        // -------- IOperationScheduler --------

        public void Schedule(Agent agent, string opName, object args, long now)
        {
            // Fire-and-forget. RunOp clears agent.Operation in its finally
            // (INV-3.8-2). AgentDecision.FireOp already set Operation
            // synchronously before invoking Schedule (INV-3.8-1).
            RunOp(agent, opName, args, now).Forget();
        }

        async UniTaskVoid RunOp(Agent agent, string opName, object args, long now)
        {
            try
            {
                switch (opName)
                {
                    case OperationNames.DoSomething:
                        AgentDoSomethingOp.Run(agent, Manager, now);
                        break;
                    case OperationNames.GenerateMessage:
                        if (args is GenerateMessageArgs gma)
                            await AgentGenerateMessageOp.RunAsync(agent, Manager, gma);
                        else
                            Debug.LogWarning($"[AgentSimulator] GenerateMessage missing args (got {args}).");
                        break;
                    case OperationNames.RememberConversation:
                        AgentRememberConversationStub.Run(agent, Manager, now);
                        break;
                    default:
                        Debug.LogWarning($"[AgentSimulator] Unknown op '{opName}'");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                // INV-3.8-2: always clear so the next tick can fire again.
                if (agent != null) agent.Operation = null;
            }
        }

        // -------- Player flow helpers --------

        // Find the single human agent. NPCRegistry doesn't expose a direct
        // human-only iterator (only NonHumanAgents); the human cohort is
        // 1-element in Phase 1 so a linear scan over All is fine.
        Agent FindHumanAgent()
        {
            if (Manager == null || Manager.NPCs == null) return null;
            foreach (var a in Manager.NPCs.All)
            {
                if (a != null && a.IsHuman) return a;
            }
            return null;
        }

        void HandlePlayerAutoAccept(long now)
        {
            // INV-3.10-3: when the player has an outstanding Invited membership
            // and the inviter is within CONVERSATION_DISTANCE_M, auto-accept on
            // the player's behalf (the player doesn't see an Invite UI in Phase 1).
            var human = FindHumanAgent();
            if (human == null || human.Body == null) return;

            var conv = Manager.Conversations.GetConversationOf(human.PlayerId);
            if (conv == null) return;
            if (!conv.Participants.TryGetValue(human.PlayerId, out var member)) return;
            if (member.Status != MemberStatusKind.Invited) return;

            // Inviter is the "other" participant (Phase 1 is strictly 2-party).
            GameId inviterId = default;
            foreach (var kv in conv.Participants)
            {
                if (!kv.Key.Equals(human.PlayerId)) { inviterId = kv.Key; break; }
            }
            var inviterBody = Manager.NPCs.GetBody(inviterId);
            if (inviterBody == null) return;
            if (Vector3.Distance(inviterBody.Position, human.Body.Position) < AITavernConstants.CONVERSATION_DISTANCE_M)
            {
                conv.AcceptInvite(human.PlayerId);
            }
        }

        void HandlePlayerSpeakTurn(long now)
        {
            // Open the speak panel when:
            //   - Player is Participating in a conversation.
            //   - Either no LastMessage yet AND the player is the initiator,
            //     OR LastMessage exists and its author is NOT the player.
            //   - We haven't already opened a panel for this LastMessage.
            // Don't re-open while one is already open.
            if (_playerSpeakPanelOpen) return;

            var human = FindHumanAgent();
            if (human == null) return;

            var conv = Manager.Conversations.GetConversationOf(human.PlayerId);
            if (conv == null) return;
            if (!conv.Participants.TryGetValue(human.PlayerId, out var member)) return;
            if (member.Status != MemberStatusKind.Participating) return;

            bool playersTurn;
            if (conv.LastMessage == null)
            {
                // No messages yet — only the initiator may open.
                playersTurn = conv.CreatorPlayerId.Equals(human.PlayerId);
            }
            else
            {
                // If the player just spoke, it's not their turn anymore.
                if (conv.LastMessage.Author.Equals(human.PlayerId)) return;
                playersTurn = true;
            }
            if (!playersTurn) return;

            // Don't retrigger for the same LastMessage timestamp.
            long currentLastTs = conv.LastMessage?.Timestamp ?? 0L;
            if (currentLastTs == _playerSpeakSeenLastMessageTimestamp) return;
            _playerSpeakSeenLastMessageTimestamp = currentLastTs;

            ShowSpeakPanelAsync(conv, human, now).Forget();
        }

        async UniTaskVoid ShowSpeakPanelAsync(Conversation conv, Agent human, long now)
        {
            _playerSpeakPanelOpen = true;
            try
            {
                // Wait for the NPC's bubble to finish rendering before stealing
                // the ChatUIPanel slot. Both ChatUIPanel and AITavernPlayerSpeakPanel
                // are IsOnly=true so opening the speak panel immediately would
                // hide the NPC's last line before the player could read it.
                while (AITavernBubbleUI.Instance != null && AITavernBubbleUI.Instance.IsBusy)
                {
                    await UniTask.Yield();
                }

                string lastLine = conv.LastMessage?.Text;
                Action<string> onSubmit = text =>
                {
                    long nowAtSubmit = Manager.Clock?.NowMs() ?? now;
                    try
                    {
                        // Defensive re-acquire: the speak panel refreshes the
                        // lock every 5s, but a frame-level hiccup could still
                        // leave IsTyping cleared by Conversation.Tick's stale
                        // sweep. SetIsTyping is a no-op when the player already
                        // holds it, and only throws when another agent does —
                        // BRANCH 22 keeps NPCs away while the player is owed
                        // the next line, so this is safe.
                        conv.SetIsTyping(human.PlayerId, Guid.NewGuid().ToString("N"), nowAtSubmit);
                        conv.AddMessage(human.PlayerId, text, nowAtSubmit);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AgentSimulator] Player AddMessage failed: {e.Message}");
                        // Allow the panel to reopen on the next frame: dedup
                        // is keyed on LastMessage.Timestamp, so without this
                        // reset the player loses their turn permanently.
                        _playerSpeakSeenLastMessageTimestamp = -1;
                    }
                    // Player-authored echo into the bubble UI. isPlayerTurn=false:
                    // the speak panel itself is the modal UI; the bubble here is
                    // just an echo so the player sees what they sent.
                    AITavernBubbleUI.Instance?.EnqueueMessage(0 /* player headId */, text, isPlayerTurn: false);
                };
                Action onCancel = () =>
                {
                    try { conv.Leave(human.PlayerId, Manager.Clock?.NowMs() ?? now); }
                    catch (Exception e) { Debug.LogWarning($"[AgentSimulator] Player Leave failed: {e.Message}"); }
                };

                // Speak panel arg shape matches AITavernPlayerSpeakPanel.OnShowPanel
                // (T14): [conversation, playerAgentId, lastNpcLine, onSubmit, onCancel, now].
                await Jyx2_UIManager.Instance.ShowUIAsync(
                    nameof(AITavernPlayerSpeakPanel),
                    conv,
                    human.PlayerId,
                    lastLine,
                    onSubmit,
                    onCancel,
                    now);
            }
            catch (Exception e)
            {
                // The prefab is a USER smoke handoff per §10.2.1 — if it isn't
                // registered with Jyx2_UIManager the ShowUIAsync call fails.
                // Log a clear hint instead of letting it bubble silently.
                Debug.LogError($"[AgentSimulator] AITavernPlayerSpeakPanel not registered or failed to open ({e.Message}). SMOKE HANDOFF: create AITavernPlayerSpeakPanel.prefab and register it in Jyx2_UIManager per T14.");
            }
            finally
            {
                _playerSpeakPanelOpen = false;
            }
        }
    }
}
