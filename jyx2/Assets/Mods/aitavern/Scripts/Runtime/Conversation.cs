// T5: Conversation POCO + FSM scaffold.
// Implements §3.2 (shape), §3.5 (per-frame Tick), §3.9 (Start invariants), and
// the typing-lock acquire/release surface from §4.3 / INV-3.10-6.
//
// T8: Stop()/StopWithManager() lifecycle hand-off (Plan §4.3) — writes
// LastConversation/ToRemember on non-human participants, records pair cooldown,
// flushes transcripts to MemoryStash, clears IsTyping, removes from
// ConversationTable. Also tightens AddMessage to enforce the author-lock guard.
//
// NOT implemented here (deferred):
//   - Anything that touches AgentDecision / Operation state (T9).
using System;
using System.Collections.Generic;
using System.Text;

namespace Jyx2.AITavern
{
    public class Conversation
    {
        public Guid Id = Guid.NewGuid();
        public GameId CreatorPlayerId;
        public long Created;                    // ms epoch
        public IsTypingLock IsTyping;           // nullable
        public Message LastMessage;             // nullable
        public int NumMessages;
        public Dictionary<GameId, ConversationMember> Participants = new Dictionary<GameId, ConversationMember>();
        public List<Message> Transcript = new List<Message>();

        // Static factory enforces:
        //   INV-3.9-1 (rejects if either party already in active conv)
        //   INV-3.9-2 (creator → WalkingOver, invitee → Invited)
        //   INV-3.9-3 (member.Invited = now for both)
        public static Conversation Start(ConversationTable table, GameId creator, GameId invitee, long now)
        {
            if (table.IsInActiveConversation(creator) || table.IsInActiveConversation(invitee)) return null;

            var c = new Conversation
            {
                CreatorPlayerId = creator,
                Created = now,
                Participants = new Dictionary<GameId, ConversationMember>
                {
                    [creator] = new ConversationMember { Invited = now, Status = MemberStatusKind.WalkingOver },
                    [invitee] = new ConversationMember { Invited = now, Status = MemberStatusKind.Invited },
                },
            };
            table.Add(c);
            return c;
        }

        // Transition a member Invited → WalkingOver. Used by:
        //   - AgentDecision.Tick when an NPC accepts an invite (INV-3.4-7).
        //   - AITavernBubbleUI when the human player auto-accepts on proximity (INV-3.10-3).
        public bool AcceptInvite(GameId player)
        {
            if (!Participants.TryGetValue(player, out var m)) return false;
            if (m.Status != MemberStatusKind.Invited) return false;
            m.Status = MemberStatusKind.WalkingOver;
            return true;
        }

        // INV-3.10-6: acquire typing lock. Refuses if held by a different player.
        public void SetIsTyping(GameId player, string messageUuid, long now)
        {
            if (IsTyping != null && IsTyping.PlayerId != player)
                throw new InvalidOperationException("typing lock held by another player");
            IsTyping = new IsTypingLock { PlayerId = player, MessageUuid = messageUuid, Since = now };
        }

        public void ClearIsTyping()
        {
            IsTyping = null;
        }

        // Appends to transcript and clears IsTyping. Mirrors ai-town
        // conversation.ts AddMessage — the typing lock is released by the
        // arrival of the message it authorized.
        //
        // T8 tightening (T5-reviewer forward-look): the author MUST hold the
        // typing lock. Calling AddMessage without a prior SetIsTyping, or with
        // a different author than the lock-holder, throws. Calling AddMessage
        // on a stopped conversation also throws.
        public void AddMessage(GameId author, string text, long now)
        {
            if (_stopped) throw new InvalidOperationException("AddMessage on stopped conversation");
            if (IsTyping == null) throw new InvalidOperationException("AddMessage requires prior SetIsTyping by the same author");
            if (IsTyping.PlayerId != author) throw new InvalidOperationException($"AddMessage author {author} does not hold the typing lock (held by {IsTyping.PlayerId})");

            var msg = new Message
            {
                Author = author,
                Text = text,
                Timestamp = now,
                MessageUuid = IsTyping.MessageUuid,
            };
            Transcript.Add(msg);
            LastMessage = msg;
            NumMessages++;
            IsTyping = null;
        }

        // Per-frame tick. Returns true if any state transition occurred.
        //   INV-3.5-1: stale typing lock is cleared.
        //   INV-3.5-2: both WalkingOver + within CONVERSATION_DISTANCE → both Participating(StartedAt=now).
        //
        // `bodyOf` is injected so this POCO doesn't need to know about
        // NPCRegistry (T7). T15 (AgentSimulator) will pass NPCRegistry.GetBody.
        public bool Tick(long now, Func<GameId, INPCBody> bodyOf)
        {
            if (_stopped) return false;
            bool changed = false;

            // 1. INV-3.5-1: stale typing lock.
            if (IsTyping != null && now > IsTyping.Since + AITavernConstants.TYPING_TIMEOUT_MS)
            {
                IsTyping = null;
                changed = true;
            }

            // 2. INV-3.5-2: 2-NPC WalkingOver→Participating proximity transition.
            //    Phase 1 is strictly 2-party; if a Phase 5+ change adds N>2 we
            //    revisit the iteration shape.
            if (Participants.Count == 2 && bodyOf != null)
            {
                var keys = new GameId[2];
                int i = 0;
                foreach (var k in Participants.Keys) keys[i++] = k;
                var m1 = Participants[keys[0]];
                var m2 = Participants[keys[1]];
                if (m1.Status == MemberStatusKind.WalkingOver && m2.Status == MemberStatusKind.WalkingOver)
                {
                    var b1 = bodyOf(keys[0]);
                    var b2 = bodyOf(keys[1]);
                    if (b1 != null && b2 != null)
                    {
                        var d = UnityEngine.Vector3.Distance(b1.Position, b2.Position);
                        if (d < AITavernConstants.CONVERSATION_DISTANCE_M)
                        {
                            b1.StopPathfinding();
                            b2.StopPathfinding();
                            m1.Status = MemberStatusKind.Participating;
                            m1.ParticipatingStartedAt = now;
                            m2.Status = MemberStatusKind.Participating;
                            m2.ParticipatingStartedAt = now;
                            changed = true;
                        }
                    }
                }
            }

            return changed;
        }

        // Phase 1 (2-NPC conversation): removing a participant drops them from
        // Participants. If the remaining count is < 2, the conversation can no
        // longer make progress, so we trigger the full Stop() lifecycle.
        public void Leave(GameId player, long now)
        {
            if (_stopped) return;
            if (!Participants.ContainsKey(player)) return;

            // If dropping this participant ends the conversation (Phase 1 is
            // strictly 2-party), run the full Stop() lifecycle BEFORE the
            // removal so the snapshot still contains BOTH parties. Otherwise
            // the leaver is excluded from ToRemember / ToRememberPartner /
            // memory-flush and its post-conversation reflection (Plan §5.3)
            // never fires — and the remaining party's ToRememberPartner
            // resolves to default, so its reflection finds no raw/ring either.
            if (Participants.Count <= 2)
            {
                Stop(now);
                return;
            }
            Participants.Remove(player);
        }

        // T8 lifecycle hand-off (Plan §4.3). Convenience overload routes through
        // AITavernManager.Instance — production callers use this.
        public void Stop(long now)
        {
            StopWithManager(now, AITavernManager.Instance);
        }

        // Test-friendly overload: lets tests stop a conversation against a
        // specific manager without depending on the static Instance singleton.
        //
        // Order of operations:
        //   1. Snapshot participant ids (we mutate state below).
        //   2. For each non-human participant: stamp Agent.LastConversation = now
        //      and Agent.ToRemember = this.Id so AgentRememberConversationOp
        //      will fire on its next tick.
        //   3. Record canonical pair cooldown (ParticipatedTogether is bidirectional;
        //      we record the single canonical (p1, p2) ordering it internalizes).
        //   4. Flush transcript to MemoryStash for each non-human participant,
        //      pairing them with their conversation partner as "other".
        //   5. Clear IsTyping (no one holds the lock on a dead conversation).
        //   6. Remove from ConversationTable so IsInActiveConversation flips false.
        //   7. Set _stopped — Tick/AddMessage/Leave become no-ops or throw.
        public void StopWithManager(long now, AITavernManager mgr)
        {
            if (_stopped) return;
            var participantIds = new List<GameId>(Participants.Keys);

            // 2. LastConversation + ToRemember on non-human participants.
            if (mgr != null && mgr.NPCs != null)
            {
                foreach (var pid in participantIds)
                {
                    var agent = mgr.NPCs.Get(pid);
                    if (agent == null || agent.IsHuman) continue;
                    agent.LastConversation = now;
                    agent.ToRemember = Id;
                    // Stamp the partner so AgentRememberConversationOp can find it
                    // without a MemoryStash scan + magic time window (Phase 2 §6.1).
                    GameId partnerId = default;
                    foreach (var otherPid in participantIds)
                        if (!otherPid.Equals(pid)) { partnerId = otherPid; break; }
                    agent.ToRememberPartner = partnerId;
                }
            }

            // 3. Pair cooldown (Phase 1 is strictly 2-party).
            if (mgr != null && mgr.PairCooldowns != null && participantIds.Count >= 2)
            {
                mgr.PairCooldowns.Record(participantIds[0], participantIds[1], now);
            }

            // 4. MemoryStash flush — once per non-human participant, pairing them
            //    with their counterpart.
            if (mgr != null && mgr.Memory != null && mgr.NPCs != null && participantIds.Count >= 2)
            {
                var transcriptText = BuildTranscriptString();
                foreach (var pid in participantIds)
                {
                    var agent = mgr.NPCs.Get(pid);
                    if (agent == null || agent.IsHuman) continue;
                    GameId other = default;
                    foreach (var otherPid in participantIds)
                    {
                        if (!otherPid.Equals(pid)) { other = otherPid; break; }
                    }
                    mgr.Memory.AppendConversationMemory(pid, other, transcriptText, now);
                }
            }

            // 5. Clear typing lock.
            IsTyping = null;

            // 6. Remove from active conversation table.
            if (mgr != null && mgr.Conversations != null)
            {
                mgr.Conversations.Remove(this);
            }

            // 7. Mark stopped.
            _stopped = true;
        }

        bool _stopped;
        public bool IsStopped => _stopped;

        // Author:Text per line. ai-town stores transcript objects; Phase 1's
        // MemoryStash is text-blob so we collapse here. Null Author.Value is
        // possible only for malformed test data — we substitute "?" rather than
        // throw to keep Stop() infallible.
        string BuildTranscriptString()
        {
            if (Transcript == null || Transcript.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var msg in Transcript)
            {
                sb.Append(msg.Author.Value ?? "?");
                sb.Append(": ");
                sb.Append(msg.Text);
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
