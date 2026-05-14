// T17: Interest-weighted candidate selection — INV-3.6-1..10.
//
// Exercises AgentDoSomethingOp.Run with controlled inputs. The wander
// fallback (TryWander → NavMesh.SamplePosition) does not work in edit-mode
// tests without a baked NavMesh; we deliberately set up scenarios that
// either pick an invitee (returns true) or fall through to wander (returns
// false because NavMesh sample fails). Both outcomes are deterministic and
// observable.
using NUnit.Framework;
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class InterestSelectionTests
    {
        AITavernManager _mgr;
        FakeClock _clock;
        Agent _self, _candidate;

        [SetUp]
        public void SetUp()
        {
            _mgr = TestBuilders.MakeManager(startTimeMs: 1_000_000, rngSeed: 42);
            _clock = (FakeClock)_mgr.Clock;
            _self = TestBuilders.MakeAgent("self", bio: TestBuilders.MakeBio("self"));
            _candidate = TestBuilders.MakeAgent("cand", bio: TestBuilders.MakeBio("cand"));
            _mgr.NPCs.Register(_self);
            _mgr.NPCs.Register(_candidate);
        }

        [TearDown]
        public void TearDown()
        {
            TestBuilders.DestroyBio(_self.Bio);
            TestBuilders.DestroyBio(_candidate.Bio);
            TestBuilders.DestroyManager(_mgr);
        }

        // ----- INV-3.6-2: only inviteee picked when pathfinding -----
        [Test]
        public void Run_PathfindingAndCandidateAvailable_StartsConversation()
        {
            // Default bio.InterestIn returns 0.3 (ambient), proximity ~ 1, recencyDamp = 1.
            // score = 0.3 * 1 * 1 = 0.3 > MIN_CANDIDATE_SCORE (0.05) → eligible.
            ((FakeBody)_self.Body).IsPathfinding = true;

            bool acted = AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs());

            Assert.IsTrue(acted, "pathfinding self + eligible candidate → starts conv");
            Assert.AreEqual(1, _mgr.Conversations.Active.Count);
        }

        // ----- INV-3.6-3: justLeftConversation suppresses invitee selection -----
        [Test]
        public void Run_JustLeftConversation_SuppressesInvite()
        {
            ((FakeBody)_self.Body).IsPathfinding = true;
            _self.LastConversation = _clock.NowMs(); // very recent → justLeftConversation

            bool acted = AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs());

            // Falls through to TryWander; in edit-mode (no NavMesh) it returns false.
            Assert.IsFalse(acted);
            Assert.AreEqual(0, _mgr.Conversations.Active.Count,
                "justLeftConversation must suppress the invite");
        }

        // ----- INV-3.6-3: recentlyAttemptedInvite suppresses invitee selection -----
        [Test]
        public void Run_RecentlyAttemptedInvite_SuppressesInvite()
        {
            ((FakeBody)_self.Body).IsPathfinding = true;
            _self.LastInviteAttempt = _clock.NowMs();

            bool acted = AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs());

            Assert.IsFalse(acted, "recentlyAttemptedInvite suppresses invite, wander fails (no NavMesh)");
            Assert.AreEqual(0, _mgr.Conversations.Active.Count);
        }

        // ----- INV-3.6-4: cooling-down candidate is filtered out -----
        [Test]
        public void Run_PairCoolingDown_CandidateFiltered()
        {
            ((FakeBody)_self.Body).IsPathfinding = true;
            // Record a pair end so the candidate is on cooldown.
            _mgr.PairCooldowns.Record(_self.PlayerId, _candidate.PlayerId, _clock.NowMs());

            bool acted = AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs());
            Assert.IsFalse(acted, "no eligible candidate → wander fallback → false in edit mode");
            Assert.AreEqual(0, _mgr.Conversations.Active.Count);
        }

        // ----- INV-3.6-7: empty candidate set returns null (no throw) -----
        [Test]
        public void Run_NoCandidates_DoesNotThrow()
        {
            // Drop the only candidate so the pool is empty.
            _mgr.NPCs.Unregister(_candidate.AgentId);
            ((FakeBody)_self.Body).IsPathfinding = true;

            Assert.DoesNotThrow(() => AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs()));
            Assert.AreEqual(0, _mgr.Conversations.Active.Count);
        }

        // ----- INV-3.6-6: sub-floor candidates are dropped -----
        [Test]
        public void Run_AllCandidatesBelowFloor_NoInviteFires()
        {
            // Author candidate bio with zero interest in self. Self bio with zero
            // interest in candidate. score = 0 * proximity * recencyDamp = 0 < floor.
            _self.Bio.Interests.Add(new InterestEntry { TargetAgentId = "cand", Score = 0f });
            _candidate.Bio.Interests.Add(new InterestEntry { TargetAgentId = "self", Score = 0f });

            ((FakeBody)_self.Body).IsPathfinding = true;
            bool acted = AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs());

            Assert.IsFalse(acted);
            Assert.AreEqual(0, _mgr.Conversations.Active.Count,
                "sub-floor scores are dropped → no invite, wander fallback returns false");
        }

        // ----- INV-3.6-8: deterministic winner with seeded RNG over many candidates -----
        // Re-runs the same scenario twice with the same RNG seed against the SAME
        // manager (after fully resetting state). Asserts the partner chosen is
        // identical across the two runs — this is the determinism guarantee
        // INV-3.6-8 relies on for reproducible scenarios.
        [Test]
        public void Run_DeterministicWinnerWithSeededRng()
        {
            // Build a third candidate so the weighted sample has a real choice to make.
            var third = TestBuilders.MakeAgent("third", bio: TestBuilders.MakeBio("third"));
            _mgr.NPCs.Register(third);

            // High interest in `cand`, low in `third`. Both above the floor so
            // the weighted sample has two viable picks.
            _self.Bio.Interests.Add(new InterestEntry { TargetAgentId = "cand", Score = 1.0f });
            _self.Bio.Interests.Add(new InterestEntry { TargetAgentId = "third", Score = 0.1f });

            ((FakeBody)_self.Body).IsPathfinding = true;

            // Run #1 with seed 0xA17E.
            _mgr.Rng = new System.Random(unchecked((int)0xA17EL));
            bool acted1 = AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs());
            Assert.IsTrue(acted1);
            Assert.AreEqual(1, _mgr.Conversations.Active.Count);
            var firstPartner = GetOtherParticipant(_mgr.Conversations.Active[0], _self.PlayerId);

            // Reset world state so we can re-run.
            _mgr.Conversations.Remove(_mgr.Conversations.Active[0]);
            _self.LastInviteAttempt = null;
            _self.LastConversation = null;

            // Run #2 with the SAME seed → must pick the same partner.
            _mgr.Rng = new System.Random(unchecked((int)0xA17EL));
            bool acted2 = AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs());
            Assert.IsTrue(acted2);
            Assert.AreEqual(1, _mgr.Conversations.Active.Count);
            var secondPartner = GetOtherParticipant(_mgr.Conversations.Active[0], _self.PlayerId);

            Assert.AreEqual(firstPartner, secondPartner,
                "same seed + same inputs → same weighted-sample winner");

            TestBuilders.DestroyBio(third.Bio);
        }

        // ----- Human agents are skipped by Run (defensive) -----
        [Test]
        public void Run_HumanAgent_ReturnsFalseNoMutation()
        {
            _mgr.NPCs.Unregister(_self.AgentId);
            _self = TestBuilders.MakeAgent("self", isHuman: true);
            _mgr.NPCs.Register(_self);

            bool acted = AgentDoSomethingOp.Run(_self, _mgr, _clock.NowMs());
            Assert.IsFalse(acted);
            Assert.AreEqual(0, _mgr.Conversations.Active.Count);
        }

        static GameId GetOtherParticipant(Conversation c, GameId self)
        {
            foreach (var kv in c.Participants)
            {
                if (!kv.Key.Equals(self)) return kv.Key;
            }
            return default;
        }
    }
}
