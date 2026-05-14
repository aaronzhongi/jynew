// T17: Common factories used by AgentDecisionTests / ConversationFsmTests / etc.
//
// MakeAgent — convenience POCO build with FakeBody by default. AgentId == PlayerId
//   because the runtime's NPCRegistry keys on AgentId but several call sites
//   (Conversation.StopWithManager, AgentDecision lookups, AgentGenerateMessageOp)
//   pass PlayerId. Tests mirror that "single-id" convention.
//
// MakeManager — instantiates a real AITavernManager MonoBehaviour on a fresh
//   GameObject so Awake fires and lazily initializes NPCs/Conversations/etc.
//   Tests then override Clock / Rng to deterministic values. Caller MUST call
//   DestroyManager in TearDown to avoid leaking GameObjects across tests.
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    public static class TestBuilders
    {
        public static Agent MakeAgent(
            string id,
            bool isHuman = false,
            INPCBody body = null,
            CharacterBio bio = null)
        {
            var gid = new GameId(id);
            return new Agent
            {
                AgentId = gid,
                PlayerId = gid,
                IsHuman = isHuman,
                Body = body ?? new FakeBody(),
                Bio = bio,
            };
        }

        public static CharacterBio MakeBio(string agentId)
        {
            var bio = ScriptableObject.CreateInstance<CharacterBio>();
            bio.AgentId = agentId;
            return bio;
        }

        public static AITavernManager MakeManager(long startTimeMs = 0, int rngSeed = 42)
        {
            AITavernManager.ResetInstanceForTests();

            var go = new GameObject("TestManager_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            var mgr = go.AddComponent<AITavernManager>();
            // CRITICAL: in edit mode, Unity does NOT invoke Awake on a plain
            // MonoBehaviour added via AddComponent (needs [ExecuteAlways]). We
            // call the lazy-init explicitly so all owned subsystems exist.
            mgr.EnsureInitialized();
            // Same reason: Awake never set the static Instance. Code under test
            // (e.g. Conversation.Stop → AITavernManager.Instance) needs it.
            AITavernManager.SetInstanceForTests(mgr);
            // Overwrite the injectable singletons with deterministic versions.
            mgr.Clock = new FakeClock(startTimeMs);
            mgr.Rng = new System.Random(rngSeed);
            // After overwriting Rng, NPCRegistry already captured the lazy-init
            // RNG — rebuild it so the candidate-selection path uses our seeded one.
            mgr.NPCs = new NPCRegistry(mgr.Rng);
            return mgr;
        }

        public static void DestroyManager(AITavernManager mgr)
        {
            if (mgr != null && mgr.gameObject != null)
            {
                Object.DestroyImmediate(mgr.gameObject);
            }
            // Belt and suspenders: clear static Instance.
            AITavernManager.ResetInstanceForTests();
        }

        public static void DestroyBio(CharacterBio bio)
        {
            if (bio != null) Object.DestroyImmediate(bio);
        }
    }
}
