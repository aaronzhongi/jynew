// T17: Conversation FSM invariants — INV-3.9-* / INV-3.10-6 / INV-3.5-*.
//
// Exercises Conversation.Start / AcceptInvite / SetIsTyping / AddMessage /
// Stop / StopWithManager / Tick (per-frame proximity + TYPING_TIMEOUT_MS).
using NUnit.Framework;
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class ConversationFsmTests
    {
        AITavernManager _mgr;
        FakeClock _clock;
        Agent _a, _b;

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000);
            _clock = (FakeClock)_mgr.Clock;
            _a = TestBuilders.MakeAgent("a");
            _b = TestBuilders.MakeAgent("b");
            _mgr.NPCs.Register(_a);
            _mgr.NPCs.Register(_b);
        }

        [TearDown]
        public void TearDown() => TestBuilders.DestroyManager(_mgr);

        // ----- INV-3.9-1: non-overlap on Start -----
        [Test]
        public void Start_RejectsIfEitherAlreadyInActiveConversation()
        {
            var first = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            Assert.IsNotNull(first);

            var c = TestBuilders.MakeAgent("c");
            _mgr.NPCs.Register(c);
            var dup = Conversation.Start(_mgr.Conversations, _a.PlayerId, c.PlayerId, _clock.NowMs());
            Assert.IsNull(dup, "Start must reject when creator is already in active conv");
        }

        // ----- INV-3.9-2 / 3.9-3: creator → WalkingOver, invitee → Invited, both stamped -----
        [Test]
        public void Start_AssignsRolesAndInvitedTimestamp()
        {
            long now = _clock.NowMs();
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, now);

            Assert.AreEqual(MemberStatusKind.WalkingOver, conv.Participants[_a.PlayerId].Status);
            Assert.AreEqual(MemberStatusKind.Invited, conv.Participants[_b.PlayerId].Status);
            Assert.AreEqual(now, conv.Participants[_a.PlayerId].Invited);
            Assert.AreEqual(now, conv.Participants[_b.PlayerId].Invited);
            Assert.AreEqual(_a.PlayerId, conv.CreatorPlayerId);
        }

        // ----- AcceptInvite transitions Invited → WalkingOver -----
        [Test]
        public void AcceptInvite_TransitionsInvitedToWalkingOver()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            Assert.IsTrue(conv.AcceptInvite(_b.PlayerId));
            Assert.AreEqual(MemberStatusKind.WalkingOver, conv.Participants[_b.PlayerId].Status);
        }

        // ----- AcceptInvite refuses non-member / wrong-state -----
        [Test]
        public void AcceptInvite_RefusesIfNotInvited()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            // A is WalkingOver (not Invited), so AcceptInvite should reject.
            Assert.IsFalse(conv.AcceptInvite(_a.PlayerId));
        }

        // ----- INV-3.10-6: SetIsTyping refuses when held by another player -----
        [Test]
        public void SetIsTyping_RefusesWhenHeldByDifferentPlayer()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            conv.SetIsTyping(_a.PlayerId, "uuid-1", _clock.NowMs());
            Assert.Throws<System.InvalidOperationException>(() =>
                conv.SetIsTyping(_b.PlayerId, "uuid-2", _clock.NowMs()));
        }

        // ----- SetIsTyping is idempotent for the same holder -----
        [Test]
        public void SetIsTyping_AllowsSameHolderReacquire()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            conv.SetIsTyping(_a.PlayerId, "uuid-1", _clock.NowMs());
            Assert.DoesNotThrow(() => conv.SetIsTyping(_a.PlayerId, "uuid-2", _clock.NowMs()));
            Assert.AreEqual("uuid-2", conv.IsTyping.MessageUuid);
        }

        // ----- AddMessage throws on no-lock -----
        [Test]
        public void AddMessage_ThrowsIfNoTypingLock()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            Assert.Throws<System.InvalidOperationException>(() =>
                conv.AddMessage(_a.PlayerId, "no lock", _clock.NowMs()));
        }

        // ----- AddMessage throws on author mismatch -----
        [Test]
        public void AddMessage_ThrowsIfAuthorMismatch()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            conv.SetIsTyping(_a.PlayerId, "uuid", _clock.NowMs());
            Assert.Throws<System.InvalidOperationException>(() =>
                conv.AddMessage(_b.PlayerId, "wrong author", _clock.NowMs()));
        }

        // ----- AddMessage clears IsTyping and appends to transcript -----
        [Test]
        public void AddMessage_ClearsLockAndAppendsTranscript()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            conv.SetIsTyping(_a.PlayerId, "uuid-x", _clock.NowMs());
            conv.AddMessage(_a.PlayerId, "hello", _clock.NowMs());

            Assert.IsNull(conv.IsTyping, "AddMessage releases the lock");
            Assert.AreEqual(1, conv.NumMessages);
            Assert.AreEqual("hello", conv.LastMessage.Text);
            Assert.AreEqual(_a.PlayerId, conv.LastMessage.Author);
            Assert.AreEqual("uuid-x", conv.LastMessage.MessageUuid);
        }

        // ----- AddMessage throws on stopped conv -----
        [Test]
        public void AddMessage_ThrowsIfStopped()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            conv.StopWithManager(_clock.NowMs(), _mgr);
            Assert.Throws<System.InvalidOperationException>(() =>
                conv.AddMessage(_a.PlayerId, "after stop", _clock.NowMs()));
        }

        // ----- StopWithManager runs the full Phase-1 lifecycle hand-off -----
        [Test]
        public void StopWithManager_WritesLastConversation_ToRemember_PairCooldown_Memory_RemovesFromTable()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            // Add a transcript entry so the MemoryStash flush has content.
            conv.SetIsTyping(_a.PlayerId, "uuid", _clock.NowMs());
            conv.AddMessage(_a.PlayerId, "hi", _clock.NowMs());

            long stopAt = _clock.NowMs() + 5000;
            conv.StopWithManager(stopAt, _mgr);

            Assert.AreEqual(stopAt, _a.LastConversation);
            Assert.AreEqual(stopAt, _b.LastConversation);
            Assert.AreEqual(conv.Id, _a.ToRemember);
            Assert.AreEqual(conv.Id, _b.ToRemember);

            // Pair cooldown recorded.
            Assert.IsTrue(_mgr.PairCooldowns.IsCoolingDown(_a.PlayerId, _b.PlayerId, stopAt));

            // Memory flush: one entry per non-human participant.
            int aMemCount = 0, bMemCount = 0;
            foreach (var e in _mgr.Memory.ForOwner(_a.PlayerId)) aMemCount++;
            foreach (var e in _mgr.Memory.ForOwner(_b.PlayerId)) bMemCount++;
            Assert.AreEqual(1, aMemCount);
            Assert.AreEqual(1, bMemCount);

            // IsTyping cleared, removed from active table, marked stopped.
            Assert.IsNull(conv.IsTyping);
            Assert.IsFalse(_mgr.Conversations.IsInActiveConversation(_a.PlayerId));
            Assert.IsFalse(_mgr.Conversations.IsInActiveConversation(_b.PlayerId));
            Assert.IsTrue(conv.IsStopped);
        }

        // ----- Stop is idempotent: a 2nd Stop call is a no-op -----
        [Test]
        public void StopWithManager_SecondCallIsNoOp()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            conv.StopWithManager(_clock.NowMs(), _mgr);
            // Repeat — should not double-record pair cooldown or memory entries.
            int memBefore = _mgr.Memory.Entries.Count;
            conv.StopWithManager(_clock.NowMs() + 1, _mgr);
            Assert.AreEqual(memBefore, _mgr.Memory.Entries.Count);
        }

        // ----- INV-3.5-2: per-tick proximity transitions WalkingOver → Participating -----
        [Test]
        public void Tick_BothWalkingOverWithinDistance_TransitionsToParticipating_StopsPathfinding()
        {
            var bodyA = (FakeBody)_a.Body;
            var bodyB = (FakeBody)_b.Body;
            bodyA.Position = new Vector3(0, 0, 0);
            bodyB.Position = new Vector3(1.0f, 0, 0); // < CONVERSATION_DISTANCE
            bodyA.IsPathfinding = true;
            bodyB.IsPathfinding = true;

            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            // Force B into WalkingOver too (Start gave them Invited).
            conv.Participants[_b.PlayerId].Status = MemberStatusKind.WalkingOver;

            long tickTime = _clock.NowMs() + 100;
            bool changed = conv.Tick(tickTime, _mgr.NPCs.GetBody);

            Assert.IsTrue(changed);
            Assert.AreEqual(MemberStatusKind.Participating, conv.Participants[_a.PlayerId].Status);
            Assert.AreEqual(MemberStatusKind.Participating, conv.Participants[_b.PlayerId].Status);
            Assert.AreEqual(tickTime, conv.Participants[_a.PlayerId].ParticipatingStartedAt);
            Assert.AreEqual(tickTime, conv.Participants[_b.PlayerId].ParticipatingStartedAt);
            Assert.AreEqual(1, bodyA.StopCallCount);
            Assert.AreEqual(1, bodyB.StopCallCount);
        }

        // ----- INV-3.5-1: TYPING_TIMEOUT_MS boundary — exactly at boundary retains lock, +1 clears -----
        [Test]
        public void Tick_TypingTimeoutBoundary_AtBoundaryRetains_PastBoundaryClears()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            long lockSince = _clock.NowMs();
            conv.SetIsTyping(_a.PlayerId, "uuid", lockSince);

            // Exactly at TYPING_TIMEOUT_MS: NOT past the boundary (strict `>`).
            conv.Tick(lockSince + AITavernConstants.TYPING_TIMEOUT_MS, _mgr.NPCs.GetBody);
            Assert.IsNotNull(conv.IsTyping, "exactly at boundary retains lock");

            // One ms past boundary: cleared.
            conv.Tick(lockSince + AITavernConstants.TYPING_TIMEOUT_MS + 1, _mgr.NPCs.GetBody);
            Assert.IsNull(conv.IsTyping, "past boundary clears lock");
        }

        // ----- Tick is a no-op on stopped conversations -----
        [Test]
        public void Tick_StoppedConversation_ReturnsFalseNoMutation()
        {
            var conv = Conversation.Start(_mgr.Conversations, _a.PlayerId, _b.PlayerId, _clock.NowMs());
            conv.StopWithManager(_clock.NowMs(), _mgr);

            bool changed = conv.Tick(_clock.NowMs() + AITavernConstants.TYPING_TIMEOUT_MS + 100,
                _mgr.NPCs.GetBody);
            Assert.IsFalse(changed);
        }
    }
}
