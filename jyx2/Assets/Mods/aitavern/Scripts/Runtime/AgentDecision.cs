// T9: AgentDecision.Tick — central per-agent decision tree.
//
// Ports ai-town's Agent.tick (convex/aiTown/agent.ts:52-236) to C#. Phase 1 is
// strictly 2-party; the in-conversation branches assume exactly one "other"
// participant. INV references throughout map every branch back to the
// corresponding invariant in docs/aitavern_invariants.md §3.4 and §3.8/§3.10.
//
// One-op invariant (INV-3.8-1): agent.Operation is ALWAYS set BEFORE
// scheduler.Schedule is called. The scheduler is responsible for clearing it
// when the UniTask resolves (INV-3.8-2) — T9 does not own the continuation.
//
// Decision logic only. Operation runners are T10 (DoSomething) and T11
// (GenerateMessage + Grok). T9 fires names + args and walks away.
namespace Jyx2.AITavern
{
    // -------- Operation argument POCOs --------

    /// <summary>
    /// No payload — the DoSomething runner re-reads agent + manager state at
    /// op-execution time to pick a target / wander destination.
    /// </summary>
    public class DoSomethingArgs { }

    public enum MessageGenerationType { Start, Continue, Leave }

    public class GenerateMessageArgs
    {
        public System.Guid ConversationId;
        public GameId OtherPlayerId;
        public string MessageUuid;
        public MessageGenerationType Type;
    }

    public class RememberConversationArgs
    {
        public System.Guid ConversationId;
    }

    // -------- Operation name constants --------

    public static class OperationNames
    {
        public const string DoSomething = "agentDoSomething";
        public const string GenerateMessage = "agentGenerateMessage";
        public const string RememberConversation = "agentRememberConversation";
    }

    // -------- The decision tree itself --------

    public static class AgentDecision
    {
        /// <summary>
        /// Per-agent decision. Skips human agents (INV-3.10-1) — caller filters
        /// via NPCRegistry.NonHumanAgents but we double-gate here for safety.
        ///
        /// Reads agent + AITavernManager state, decides at most one operation
        /// to fire, sets agent.Operation synchronously (INV-3.8-1), then defers
        /// to the scheduler. Returns immediately; the scheduler runs the work
        /// fire-and-forget.
        /// </summary>
        public static void Tick(Agent agent, AITavernManager mgr, IOperationScheduler scheduler, long now)
        {
            if (agent == null) return;

            // INV-3.10-1: humans are never ticked by AgentDecision.
            if (agent.IsHuman) return;

            // ---------------- BRANCH 1 + BRANCH 2: in-flight op check ----------------
            // INV-3.4-1: if an op is in flight and not yet timed out, do nothing.
            // INV-3.4-2: if the op has exceeded ACTION_TIMEOUT_MS we treat it as
            //            stale, clear Operation, and continue ticking. (Mirrors
            //            ai-town agent.ts:62-74.)
            if (agent.Operation != null)
            {
                if (now < agent.Operation.StartedAt + ActionTimeoutMs)
                    return; // INV-3.4-1
                agent.Operation = null; // INV-3.4-2 (stale)
            }

            // Conversation context (may be null).
            var conversation = (mgr != null && mgr.Conversations != null)
                ? mgr.Conversations.GetConversationOf(agent.PlayerId)
                : null;

            // Phase 1 has no activity layer; this placeholder keeps the
            // BRANCH 3/4 shape intact for forward fidelity (Phase 5 will add
            // a real activity system).
            bool doingActivity = false;

            bool pathfinding = agent.Body != null && agent.Body.IsPathfinding;

            // INV-3.4-5: recentlyAttemptedInvite is true when LastInviteAttempt
            // was stamped within the cooldown window. Stamped by FireOp below
            // when DoSomething fires.
            bool recentlyAttemptedInvite =
                agent.LastInviteAttempt.HasValue
                && now < agent.LastInviteAttempt.Value + ConversationCooldownMs;

            // ---------------- BRANCH 5 + 6 + 7: agentDoSomething gate ----------------
            // INV-3.4-3 / INV-3.4-4: out-of-conversation, not doing an activity,
            // and either idle (not pathfinding) OR pathfinding but the prior
            // invite attempt has aged out — fire DoSomething.
            if (conversation == null
                && !doingActivity
                && (!pathfinding || !recentlyAttemptedInvite))
            {
                FireOp(agent, scheduler, now, OperationNames.DoSomething, new DoSomethingArgs());
                return;
            }

            // ---------------- BRANCH 8: agentRememberConversation ----------------
            // INV-3.4-6: pending memory write — fire and walk away. The
            // remembrance runner (AgentRememberConversationOp)
            // is the one that clears ToRemember.
            if (agent.ToRemember.HasValue)
            {
                FireOp(agent, scheduler, now, OperationNames.RememberConversation,
                    new RememberConversationArgs { ConversationId = agent.ToRemember.Value });
                return;
            }

            // ---------------- BRANCHES 9-24: in-conversation handling ----------------
            if (conversation != null
                && conversation.Participants.TryGetValue(agent.PlayerId, out var selfMember))
            {
                // 2-party Phase 1: pick the single "other" participant.
                GameId otherId = default;
                ConversationMember otherMember = null;
                foreach (var kv in conversation.Participants)
                {
                    if (!kv.Key.Equals(agent.PlayerId))
                    {
                        otherId = kv.Key;
                        otherMember = kv.Value;
                        break;
                    }
                }
                if (otherMember == null) return;

                Agent otherAgent = (mgr != null && mgr.NPCs != null) ? mgr.NPCs.Get(otherId) : null;

                switch (selfMember.Status)
                {
                    // -------- Invited: roll to accept --------
                    case MemberStatusKind.Invited:
                    {
                        // INV-3.4-7: roll < INVITE_ACCEPT_PROBABILITY → accept;
                        // otherwise leave.
                        // INV-3.10-x always-accept-human override: if the
                        // counterpart is a human player, NPCs always accept so
                        // the player never gets ghosted by the dice.
                        bool acceptHuman = otherAgent != null && otherAgent.IsHuman;
                        double roll = mgr != null && mgr.Rng != null ? mgr.Rng.NextDouble() : 0.5;

                        if (acceptHuman || roll < InviteAcceptProbability)
                        {
                            // BRANCH 9 / BRANCH 10: accept
                            conversation.AcceptInvite(agent.PlayerId);
                            // ai-town clears pathfinding on accept so the
                            // WalkingOver transition starts from rest.
                            if (agent.Body != null) agent.Body.StopPathfinding();
                        }
                        else
                        {
                            // BRANCH 11: decline → leave (Conversation.Leave
                            // tears the conversation down once participants < 2)
                            conversation.Leave(agent.PlayerId, now);
                        }
                        return;
                    }

                    // -------- WalkingOver: drive toward the other --------
                    case MemberStatusKind.WalkingOver:
                    {
                        // BRANCH 12: INVITE_TIMEOUT_MS exceeded since the
                        // member.Invited stamp — give up. (INV-3.4-8)
                        if (selfMember.Invited + InviteTimeoutMs < now)
                        {
                            conversation.Leave(agent.PlayerId, now);
                            return;
                        }

                        if (agent.Body != null && otherAgent != null && otherAgent.Body != null)
                        {
                            var d = UnityEngine.Vector3.Distance(agent.Body.Position, otherAgent.Body.Position);

                            // BRANCH 13: already within conversation distance.
                            // Conversation.Tick (INV-3.5-2) handles the actual
                            // WalkingOver → Participating transition; here we
                            // just stop driving. (INV-3.4-9)
                            if (d < AITavernConstants.CONVERSATION_DISTANCE_M)
                                return;

                            // BRANCH 14 + BRANCH 15: not already pathfinding —
                            // pick a destination.
                            // INV-3.4-10 case A: distance < MIDPOINT_THRESHOLD
                            //                  → go directly to the other.
                            // INV-3.4-10 case B: distance >= MIDPOINT_THRESHOLD
                            //                  → meet at the midpoint.
                            if (!agent.Body.IsPathfinding)
                            {
                                UnityEngine.Vector3 dest;
                                if (d < AITavernConstants.MIDPOINT_THRESHOLD_M)
                                {
                                    dest = otherAgent.Body.Position; // BRANCH 14
                                }
                                else
                                {
                                    dest = (agent.Body.Position + otherAgent.Body.Position) * 0.5f; // BRANCH 15
                                }
                                agent.Body.MoveTo(dest);
                            }
                        }
                        return;
                    }

                    // -------- Participating: message exchange --------
                    case MemberStatusKind.Participating:
                    {
                        // BRANCH 16: someone else holds the typing lock — yield.
                        // (INV-3.4-11)
                        if (conversation.IsTyping != null
                            && !conversation.IsTyping.PlayerId.Equals(agent.PlayerId))
                        {
                            return;
                        }

                        if (conversation.LastMessage == null)
                        {
                            // No message yet. Either the initiator opens
                            // immediately, or the non-initiator waits until the
                            // awkward deadline elapses and then opens to break
                            // the silence.
                            bool isInitiator = conversation.CreatorPlayerId.Equals(agent.PlayerId); // INV-3.4-17
                            long participatingStart = selfMember.ParticipatingStartedAt ?? now;
                            long awkwardDeadline = participatingStart + AwkwardConversationTimeoutMs;

                            if (isInitiator || awkwardDeadline < now)
                            {
                                // BRANCH 17 / 18: open the conversation.
                                // INV-3.4-12 (initiator path), INV-3.4-18 (awkward break).
                                var uuid = System.Guid.NewGuid().ToString("N");
                                conversation.SetIsTyping(agent.PlayerId, uuid, now);
                                FireOp(agent, scheduler, now, OperationNames.GenerateMessage,
                                    new GenerateMessageArgs
                                    {
                                        ConversationId = conversation.Id,
                                        OtherPlayerId = otherId,
                                        MessageUuid = uuid,
                                        Type = MessageGenerationType.Start,
                                    });
                                return;
                            }

                            // BRANCH 19: not initiator, awkward deadline not
                            // reached — keep waiting. (INV-3.4-19)
                            return;
                        }

                        // ---- LastMessage exists ----

                        // BRANCH 20 / 21: end-of-conversation conditions.
                        long participatingStart2 = selfMember.ParticipatingStartedAt ?? now;
                        bool durationExceeded = (participatingStart2 + MaxConversationDurationMs) < now; // INV-3.4-13 / 20
                        bool tooManyMessages = conversation.NumMessages > MaxConversationMessages;       // INV-3.4-21

                        if (durationExceeded || tooManyMessages)
                        {
                            var uuid = System.Guid.NewGuid().ToString("N");
                            conversation.SetIsTyping(agent.PlayerId, uuid, now);
                            FireOp(agent, scheduler, now, OperationNames.GenerateMessage,
                                new GenerateMessageArgs
                                {
                                    ConversationId = conversation.Id,
                                    OtherPlayerId = otherId,
                                    MessageUuid = uuid,
                                    Type = MessageGenerationType.Leave,
                                });
                            return;
                        }

                        // BRANCH 22: I sent the last message — wait until the
                        // awkward deadline before continuing. Stops the same
                        // agent from monopolizing the floor. (INV-3.4-14 / 22)
                        if (conversation.LastMessage.Author.Equals(agent.PlayerId))
                        {
                            // Human partner override: never auto-continue when
                            // the player owes the next line. The player must
                            // reply (or close the speak panel to leave) before
                            // this NPC speaks again. Stops "NPC keeps talking
                            // while PC is thinking" — ai-town's awkward-deadline
                            // mechanic assumes both sides are NPCs.
                            if (otherAgent != null && otherAgent.IsHuman) return;

                            long selfAwkward = conversation.LastMessage.Timestamp + AwkwardConversationTimeoutMs;
                            if (now < selfAwkward) return;
                        }

                        // BRANCH 23: generic post-message cooldown (applies
                        // regardless of author). (INV-3.4-15 / 23)
                        if (now < conversation.LastMessage.Timestamp + MessageCooldownMs)
                            return;

                        // BRANCH 24: continue the conversation. (INV-3.4-16 / 24)
                        var continueUuid = System.Guid.NewGuid().ToString("N");
                        conversation.SetIsTyping(agent.PlayerId, continueUuid, now);
                        FireOp(agent, scheduler, now, OperationNames.GenerateMessage,
                            new GenerateMessageArgs
                            {
                                ConversationId = conversation.Id,
                                OtherPlayerId = otherId,
                                MessageUuid = continueUuid,
                                Type = MessageGenerationType.Continue,
                            });
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Fires an operation: sets agent.Operation SYNCHRONOUSLY before any
        /// call to the scheduler (INV-3.8-1), stamps LastInviteAttempt when
        /// DoSomething is firing (INV-3.4-5), then defers to the scheduler.
        /// </summary>
        static void FireOp(Agent agent, IOperationScheduler scheduler, long now, string opName, object args)
        {
            // INV-3.8-1: synchronous write before any await / schedule.
            var opId = System.Guid.NewGuid().ToString("N");
            agent.Operation = new InProgressOperation(opName, opId, now);

            // NOTE: LastInviteAttempt is NOT stamped here. ai-town only sets it
            // when an invite is actually attempted (invitee selected). Stamping
            // on every DoSomething fire would re-arm the recentlyAttemptedInvite
            // gate every 500ms, permanently suppressing invites. The
            // AgentDoSomethingOp runner stamps it when (and only when) it picks
            // an invitee.

            scheduler?.Schedule(agent, opName, args, now);
        }

        // ---- Constants ----
        // Kept local per T9 spec; T10/T11 will consolidate into
        // AITavernConstants.cs. Values per the INV-Constants block of
        // docs/aitavern_invariants.md.
        const long ActionTimeoutMs = 120_000;
        const long ConversationCooldownMs = 15_000;
        const long InviteTimeoutMs = 60_000;
        const long AwkwardConversationTimeoutMs = 20_000;
        // ai-town's value is 2000ms — but we render via ChatUIPanel which
        // shows each line for 6s before auto-dismiss. Set this slightly
        // longer than NPC_BUBBLE_AUTO_DISMISS_MS so the next Grok call only
        // starts after the player has had time to read the previous bubble.
        // Effective pacing: bubble shows 6s → 1s breath → next message starts.
        const long MessageCooldownMs = 7_000;
        // Bumped from 120s → 600s (10 min). 2 minutes is too short when a
        // human player is composing Chinese via IME — the chat would auto-end
        // before they finish their second reply. MaxConversationMessages
        // still caps NPC-NPC conversations to ~8 exchanges so the duration
        // bump doesn't let them ramble forever.
        const long MaxConversationDurationMs = 600_000;
        const int MaxConversationMessages = 8;
        const double InviteAcceptProbability = 0.8;
    }
}
