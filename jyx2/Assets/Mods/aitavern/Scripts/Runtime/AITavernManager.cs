using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jyx2.AITavern
{
    /// <summary>
    /// Persistent singleton that survives scene unloads — INV-8-2.
    /// Carries all process-level state so Phase 3's LoadBattle round-trip
    /// (tavern scene unloads, battle scene loads, tavern scene reloads)
    /// does not lose conversation / pair-cooldown / memory / relationship state.
    ///
    /// Created lazily by AITavernBoot (T15) on first scene entry. Subsequent
    /// scene loads find the existing instance via Instance and rehydrate
    /// scene-side objects (NPCBody GameObjects) from saved positions.
    /// </summary>
    [DefaultExecutionOrder(-200)] // run Awake before AITavernBoot's Start
    public class AITavernManager : MonoBehaviour
    {
        public static AITavernManager Instance { get; private set; }

        // Process-level state. All public for explicit access from AgentSimulator
        // (T15), AgentDecision (T9), and bridge MonoBehaviours.
        public NPCRegistry NPCs;
        public ConversationTable Conversations;
        public ParticipatedTogether PairCooldowns;
        public MemoryStash Memory;
        public RelationshipGraph Relations;

        // Phase 3A (Plan §6 / §8) — static layered-memory assets, loaded by
        // AITavernBoot from Resources/AITavern/ (graceful degradation: both
        // OPTIONAL — null/empty until the offline lore pipeline T3A.6 runs).
        //   World    : §1 World Codex (one global asset; null → §1 omitted).
        //   Dossiers : §4 per-talker long-term knowledge, keyed by AgentId
        //              (absent key → §4 omitted for that talker).
        // Mirrors the Memory/NPCs in-process-state pattern; never serialized.
        public WorldCodex World;
        public Dictionary<string, CharacterDossier> Dossiers = new Dictionary<string, CharacterDossier>();

        // Injectable interfaces — production uses SystemClock + GrokClient;
        // tests inject FakeClock + MockGrokClient.
        public IClock Clock;
        public IGrokClient Grok;

        // Single seeded RNG drives interest-weighted candidate selection,
        // wander destination sampling, and (Phase 3) ally-join probabilistic checks.
        // Seed from AITavernConstants.RANDOM_SEED if it exists; otherwise default 0xA17E.
        public System.Random Rng;

        // Phase 3: snapshotted before LevelLoader.LoadBattle so the tavern can
        // restore NPC positions on return-from-battle. Cleared on scene reload.
        public Dictionary<GameId, Vector3> PreCombatPositions;

        // Initialization hook fired once during Awake (after first scene boot).
        // AgentSimulator / AITavernBoot listens here to perform one-time wiring.
        public event Action Initialized;

        void Awake()
        {
            EnsureInitialized();

            if (Instance != null && Instance != this)
            {
                // Duplicate-component scenario (e.g. AITavernBoot reinstanced after
                // scene reload). Existing instance wins.
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // DontDestroyOnLoad only in play mode — in edit-mode tests this
            // causes the GameObject to persist past DestroyImmediate, bleeding
            // state across NUnit fixtures.
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            try { Initialized?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Idempotent lazy-init of all owned subsystems. Awake calls this in
        /// production. Edit-mode tests must call it explicitly after
        /// AddComponent because Unity does NOT invoke Awake on a plain
        /// MonoBehaviour when it's added in edit mode (requires
        /// [ExecuteAlways]/[ExecuteInEditMode], which we don't want for prod).
        ///
        /// ORDER MATTERS: Rng must be assigned BEFORE NPCs since NPCRegistry's
        /// ctor captures the RNG reference.
        /// </summary>
        public void EnsureInitialized()
        {
            if (Rng == null) Rng = new System.Random(unchecked((int)0xA17EL));
            if (NPCs == null) NPCs = new NPCRegistry(Rng);
            if (Conversations == null) Conversations = new ConversationTable();
            if (PairCooldowns == null) PairCooldowns = new ParticipatedTogether();
            if (Memory == null) Memory = new MemoryStash();
            if (Relations == null) Relations = new RelationshipGraph();
            // Phase 3A: dict must exist so ContextAssembler's §4 lookup
            // (mgr.Dossiers.TryGetValue) doesn't NRE in edit-mode tests
            // before the lore pipeline has produced assets. `World` stays
            // null until AITavernBoot loads it (null → §1 simply omitted).
            if (Dossiers == null) Dossiers = new Dictionary<string, CharacterDossier>();
            if (Clock == null) Clock = new SystemClock();
            // Grok left null intentionally — production must explicitly inject GrokClient
            // OR the missing-key path engages the stub-line fallback (Phase 1 §4.4).
            if (PreCombatPositions == null) PreCombatPositions = new Dictionary<GameId, Vector3>();
        }

        /// <summary>
        /// Test-only helper: force-clears the static Instance so the next
        /// MakeManager() in a test fixture gets a fresh singleton.
        /// Idempotent; safe to call in TearDown.
        /// </summary>
        public static void ResetInstanceForTests()
        {
            Instance = null;
        }

        /// <summary>
        /// Test-only helper: wires the static Instance to the given manager.
        /// In edit-mode tests Awake does NOT fire on AddComponent, so the
        /// `Instance = this` line never runs. Tests call this after
        /// EnsureInitialized so any code that reads AITavernManager.Instance
        /// (e.g. Conversation.Stop) finds the test's manager.
        /// </summary>
        public static void SetInstanceForTests(AITavernManager mgr)
        {
            Instance = mgr;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Convenience accessor used by AgentDecision / AgentSimulator that
        /// throws a clear error if AITavernManager wasn't booted first. Avoids
        /// downstream NullReferenceExceptions buried deep in agent logic.
        /// </summary>
        public static AITavernManager Require()
        {
            if (Instance == null)
                throw new InvalidOperationException(
                    "AITavernManager.Instance is null. AITavernBoot must spawn the manager before any tick logic runs.");
            return Instance;
        }
    }
}
