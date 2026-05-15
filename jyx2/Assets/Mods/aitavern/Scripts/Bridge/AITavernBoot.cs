// T15: Scene-entry MonoBehaviour for the AI Tavern mod. Lives in `Scripts/Bridge/`
// (no .asmdef → Assembly-CSharp) so it can reference Assembly-CSharp types
// (LevelMaster, RoleHelper, RoleInstance, BattleRole, Jyx2Player, Jyx2_UIManager)
// alongside AITavern.Runtime types (AITavernManager, Agent, Conversation, ...).
//
// Bootstrap responsibilities per Phase 1 Plan §2.4 / §2.5 / §3.10 / §11:
//   1. Await RuntimeEnvSetup.Setup() so Lua tables are populated.
//   2. Wait for LevelMaster + Jyx2Player to be ready.
//   3. Bail out if RuntimeEnvSetup.CurrentModId != "aitavern".
//   4. Ensure AITavernManager + AITavernBubbleUI singletons (DontDestroyOnLoad).
//   5. Inject GrokClient if XAI key is present; otherwise stub-line mode.
//   6. Load CharacterBio assets (Resources first, AssetDatabase editor fallback).
//   7. Populate RelationshipGraph from each bio.
//   8. Spawn NPCs at Level/NPC/<SpawnMarkerName>, attach NPCBody +
//      AITavernInteractable, register Agent POCOs in mgr.NPCs.
//   9. Register the player as an IsHuman agent with a no-op INPCBody adapter
//      (INV-3.10-1, INV-3.10-4).
//  10. Wire AgentGenerateMessageOp.OnMessageGenerated → AITavernBubbleUI.
//  11. Wire AITavernInteractable.OnPlayerInteract → Conversation.Start
//      (player-initiated, INV-3.10-2).
//  12. Spawn AgentSimulator on the manager GameObject so it survives scene loads.
//
// USER SMOKE HANDOFF (Editor-only setup not done by this script):
//   (1) Drop this MonoBehaviour onto an empty GameObject in AITavern.unity.
//   (2) Author Bio_HuangRong.asset / Bio_OuyangKe.asset (CharacterBio assets)
//       under Assets/Mods/aitavern/Configs/ — must live somewhere loadable at
//       runtime (Resources/AITavern/Bios for player builds; AssetDatabase
//       fallback covers editor-only iteration).
//   (3) AITavernPlayerSpeakPanel prefab + registration in Jyx2_UIManager
//       (already documented in T14). Boot wraps the ShowUIAsync call in a
//       try/catch so a missing prefab logs a clear hint instead of hard-failing.
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Jyx2;
using Jyx2.AITavern;

namespace Jyx2.AITavern.Bridge
{
    /// <summary>
    /// Scene-entry MonoBehaviour for the AI Tavern mod. Place on an empty
    /// GameObject in AITavern.unity (USER SMOKE HANDOFF). On Start: ensures
    /// singletons, spawns NPCs at scene markers, registers the player as an
    /// IsHuman agent, wires UI bridges, and spawns AgentSimulator.
    /// </summary>
    public class AITavernBoot : MonoBehaviour
    {
        // The player is an "agent" for FSM purposes; assign a stable id so NPCs
        // can target them in their decision tree (INV-3.10-1, INV-3.10-2).
        const string PLAYER_AGENT_ID = "player";

        async void Start()
        {
            try { await BootAsync(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        async UniTask BootAsync()
        {
            await RuntimeEnvSetup.Setup();

            if (RuntimeEnvSetup.CurrentModId != "aitavern")
            {
                Debug.LogWarning($"[AITavernBoot] Active mod is '{RuntimeEnvSetup.CurrentModId}', not 'aitavern'. Skipping boot.");
                return;
            }

            // Wait for LevelMaster + player to be wired up.
            await UniTask.WaitUntil(() =>
                LevelMaster.Instance != null && LevelMaster.Instance.GetPlayer() != null);

            // Singleton manager + bubble UI.
            var mgr = EnsureManager();
            EnsureBubbleUI();

            // Inject Grok if a key is available; otherwise the stub-line path
            // in AgentGenerateMessageOp engages so conversations still make
            // progress in dev (Plan §4.4 fallback).
            try
            {
                var grok = new GrokClient();
                if (grok.HasKey) mgr.Grok = grok;
                else Debug.LogWarning("[AITavernBoot] No XAI key; NPCs will use canned stub lines.");
            }
            catch (Exception e) { Debug.LogWarning($"[AITavernBoot] GrokClient init failed: {e.Message}"); }

            // Load bios (Resources first; AssetDatabase fallback for editor).
            var bios = LoadBios();
            if (bios.Count == 0)
            {
                Debug.LogError("[AITavernBoot] No CharacterBio assets found. SMOKE HANDOFF: create Bio_HuangRong.asset and Bio_OuyangKe.asset under Assets/Mods/aitavern/Configs/ (or move them under a Resources/AITavern/Bios folder for player-build loading).");
                return;
            }

            // Phase 3A (Plan §6 / §8): load the static layered-memory assets.
            // Both are OPTIONAL — graceful degradation. If the offline lore
            // pipeline (T3A.6) hasn't run yet, Resources returns null/empty;
            // the ContextAssembler simply omits §1/§4 and conversations still
            // run on the retained Phase 2 memory path (the 3A coexistence
            // rule). 3A must ship and run with NPCs talking even before the
            // assets exist.
            LoadWorldAndDossiers(mgr);

            // Populate RelationshipGraph from each bio (§11.2 / §11.5).
            foreach (var bio in bios)
            {
                if (bio == null || string.IsNullOrEmpty(bio.AgentId)) continue;
                var selfId = new GameId(bio.AgentId);
                if (bio.Relationships == null) continue;
                foreach (var rel in bio.Relationships)
                {
                    if (rel == null || string.IsNullOrEmpty(rel.TargetAgentId)) continue;
                    mgr.Relations.Set(selfId, new GameId(rel.TargetAgentId), rel.Relation);
                }
            }

            // Spawn NPCs under Level/NPC/. Create the container if missing.
            var npcRoot = GameObject.Find("Level/NPC");
            if (npcRoot == null)
            {
                var lvl = GameObject.Find("Level");
                if (lvl == null) lvl = new GameObject("Level");
                npcRoot = new GameObject("NPC");
                npcRoot.transform.SetParent(lvl.transform, false);
            }
            foreach (var bio in bios)
            {
                if (bio == null || string.IsNullOrEmpty(bio.AgentId)) continue;
                SpawnNpc(bio, npcRoot.transform, mgr);
            }

            // Register the player agent (no spawn — uses existing Level/Player).
            RegisterPlayerAgent(mgr);

            // Wire UI bridges.
            AgentGenerateMessageOp.OnMessageGenerated += OnNpcMessageGenerated;
            AITavernInteractable.OnPlayerInteract += OnPlayerInteractWithNpc;

            // Spawn the simulator on the manager's GameObject so it survives
            // DontDestroyOnLoad alongside the manager (Phase 3 reload flow).
            // AgentSimulator implements IOperationScheduler.
            var sim = mgr.gameObject.GetComponent<AgentSimulator>();
            if (sim == null) sim = mgr.gameObject.AddComponent<AgentSimulator>();
            sim.Manager = mgr;
        }

        void OnDestroy()
        {
            AgentGenerateMessageOp.OnMessageGenerated -= OnNpcMessageGenerated;
            AITavernInteractable.OnPlayerInteract -= OnPlayerInteractWithNpc;
        }

        // ---------------- Helpers ----------------

        AITavernManager EnsureManager()
        {
            if (AITavernManager.Instance != null) return AITavernManager.Instance;
            var go = new GameObject("AITavernManager");
            return go.AddComponent<AITavernManager>();
        }

        void EnsureBubbleUI()
        {
            if (AITavernBubbleUI.Instance != null) return;
            var go = new GameObject("AITavernBubbleUI");
            go.AddComponent<AITavernBubbleUI>();
        }

        // Phase 1: load bios from Resources/AITavern/Bios/*.asset (player builds)
        // with an editor AssetDatabase fallback for dev iteration. Resources
        // beats AssetDatabase because Resources works in builds.
        List<CharacterBio> LoadBios()
        {
            var fromResources = Resources.LoadAll<CharacterBio>("AITavern/Bios");
            if (fromResources != null && fromResources.Length > 0) return fromResources.ToList();

#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("t:CharacterBio", new[] { "Assets/Mods/aitavern" });
            var list = new List<CharacterBio>();
            foreach (var g in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var bio = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterBio>(path);
                if (bio != null) list.Add(bio);
            }
            return list;
#else
            return new List<CharacterBio>();
#endif
        }

        // Phase 3A: mirror LoadBios for the static layered-memory assets.
        //   - WorldCodex: one global asset at Resources/AITavern/WorldCodex.
        //   - Dossiers:   Resources/AITavern/Dossiers/*.asset, keyed by AgentId.
        // Both OPTIONAL: if the lore pipeline hasn't run yet, log an INFO
        // note and continue (graceful degradation — §1/§4 just omitted).
        // AssetDatabase editor fallback mirrors LoadBios so dev iteration
        // works before assets are moved under a Resources folder.
        void LoadWorldAndDossiers(AITavernManager mgr)
        {
            // ---- WorldCodex (one global asset) ----
            var world = Resources.Load<WorldCodex>("AITavern/WorldCodex");
#if UNITY_EDITOR
            if (world == null)
            {
                var wguids = UnityEditor.AssetDatabase.FindAssets("t:WorldCodex", new[] { "Assets/Mods/aitavern" });
                if (wguids != null && wguids.Length > 0)
                {
                    var wpath = UnityEditor.AssetDatabase.GUIDToAssetPath(wguids[0]);
                    world = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldCodex>(wpath);
                }
            }
#endif
            mgr.World = world; // may stay null — §1 omitted, not an error

            // ---- Dossiers (per roster character) ----
            var dossiers = Resources.LoadAll<CharacterDossier>("AITavern/Dossiers");
            List<CharacterDossier> dossierList = (dossiers != null && dossiers.Length > 0)
                ? dossiers.ToList()
                : new List<CharacterDossier>();
#if UNITY_EDITOR
            if (dossierList.Count == 0)
            {
                var dguids = UnityEditor.AssetDatabase.FindAssets("t:CharacterDossier", new[] { "Assets/Mods/aitavern" });
                foreach (var g in dguids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                    var dos = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDossier>(path);
                    if (dos != null) dossierList.Add(dos);
                }
            }
#endif
            if (mgr.Dossiers == null) mgr.Dossiers = new Dictionary<string, CharacterDossier>();
            foreach (var dos in dossierList)
            {
                if (dos == null || string.IsNullOrEmpty(dos.AgentId)) continue;
                mgr.Dossiers[dos.AgentId] = dos;
            }

            if (mgr.World == null || mgr.Dossiers.Count == 0)
            {
                // INFO-level (Log, not Warning/Error): this is the expected
                // pre-pipeline state, not a failure. 3A ships and runs here.
                Debug.Log("[AITavern] no WorldCodex/Dossiers yet — §1/§4 will be omitted until lore pipeline runs"
                    + $" (World={(mgr.World != null ? "ok" : "null")}, Dossiers={mgr.Dossiers.Count}).");
            }
        }

        void SpawnNpc(CharacterBio bio, Transform npcRoot, AITavernManager mgr)
        {
            // Build RoleInstance from the character row id (jynew character row).
            RoleInstance role;
            try { role = new RoleInstance(bio.RoleId); }
            catch (Exception e)
            {
                Debug.LogError($"[AITavernBoot] Failed to construct RoleInstance({bio.RoleId}) for {bio.AgentId}: {e.Message}");
                return;
            }

            // RoleHelper.CreateRoleView is an extension method (RoleHelper is in
            // the global namespace). The returned BattleRole is the spawned NPC
            // view GameObject we attach NPCBody + AITavernInteractable to.
            var view = role.CreateRoleView("NPC");
            if (view == null)
            {
                Debug.LogError($"[AITavernBoot] CreateRoleView returned null for {bio.AgentId}.");
                return;
            }
            view.transform.SetParent(npcRoot, false);

            // Spawn marker lookup. Fallback to npcRoot origin if missing.
            var marker = GameObject.Find("Level/NPC/" + bio.SpawnMarkerName);
            if (marker == null)
            {
                view.transform.position = npcRoot.position;
                Debug.LogWarning($"[AITavernBoot] Spawn marker '{bio.SpawnMarkerName}' not found; using NPC parent origin for {bio.AgentId}.");
            }
            else
            {
                view.transform.position = marker.transform.position;
                view.transform.rotation = marker.transform.rotation;
            }

            // Ensure NavMeshAgent exists so NPCBody can wrap it. BattleRole
            // prefab may or may not ship with one — add if missing and tune.
            var nav = view.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (nav == null) nav = view.gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
            // The BattleRole prefab ships NavMeshAgent disabled (battle scenes
            // drive movement directly). Enable it for our wandering NPCs.
            nav.enabled = true;
            nav.speed = 2.5f;
            nav.angularSpeed = 360f;
            nav.acceleration = 8f;
            nav.stoppingDistance = 0.5f;

            // NPCBody + Interactable wiring.
            var body = view.gameObject.AddComponent<NPCBody>();
            var agentId = new GameId(bio.AgentId);
            body.AgentId = agentId;

            var interact = view.gameObject.AddComponent<AITavernInteractable>();
            interact.AgentId = agentId;

            // Build Agent POCO and register with the manager.
            var agent = new Agent
            {
                AgentId = agentId,
                PlayerId = agentId, // Phase 1: player-id == agent-id
                IsHuman = false,
                Body = body,
                Bio = bio,
            };
            mgr.NPCs.Register(agent);
        }

        void RegisterPlayerAgent(AITavernManager mgr)
        {
            var playerId = new GameId(PLAYER_AGENT_ID);
            // Wrap the existing Jyx2Player as an INPCBody so the FSM can read
            // its position. Pathfinding methods are no-ops — the player walks
            // via their own input.
            var playerGo = LevelMaster.Instance.GetPlayer().gameObject;
            var adapter = playerGo.GetComponent<JyxPlayerBodyAdapter>();
            if (adapter == null) adapter = playerGo.AddComponent<JyxPlayerBodyAdapter>();
            var agent = new Agent
            {
                AgentId = playerId,
                PlayerId = playerId,
                IsHuman = true,
                Body = adapter,
                Bio = BuildPlayerBio(),
            };
            mgr.NPCs.Register(agent);
        }

        // Runtime-only bio for the player. ConversationPrompts needs an `other`
        // bio to render the identity block — without it AgentGenerateMessageOp
        // falls back to canned stub lines for every NPC→player message. The
        // persona is intentionally vague ("江湖客") so Grok can take cues from
        // whatever the player actually says.
        static CharacterBio BuildPlayerBio()
        {
            var bio = ScriptableObject.CreateInstance<CharacterBio>();
            bio.AgentId = PLAYER_AGENT_ID;
            bio.BioName = "江湖客";
            bio.Identity = "一名来历不明的江湖过客，刚踏入这家客栈。身份、来路、目的皆未明。";
            bio.HeadId = 0;
            return bio;
        }

        // ---------------- Event handlers ----------------

        void OnNpcMessageGenerated(int headId, string text)
        {
            // NPC-authored messages render in the bubble UI; isPlayerTurn=false
            // because the FSM owns the modal flag here (Plan §2.7 / INV-2.7).
            AITavernBubbleUI.Instance?.EnqueueMessage(headId, text, isPlayerTurn: false);
        }

        void OnPlayerInteractWithNpc(GameId npcAgentId)
        {
            var mgr = AITavernManager.Instance;
            if (mgr == null) return;

            var playerId = new GameId(PLAYER_AGENT_ID);
            // INV-3.9-1: refuse if the player is already in a conversation.
            if (mgr.Conversations.IsInActiveConversation(playerId)) return;

            var now = mgr.Clock?.NowMs() ?? 0L;
            Conversation.Start(mgr.Conversations, playerId, npcAgentId, now);
        }
    }

    /// <summary>
    /// Adapter that makes Jyx2Player look like an INPCBody so the FSM can query
    /// its position. Pathfinding is always false (player walks via their own
    /// input). StopPathfinding / MoveTo are no-ops — INV-3.10-4 keeps the FSM
    /// from steering the human.
    /// </summary>
    public class JyxPlayerBodyAdapter : MonoBehaviour, INPCBody
    {
        public Vector3 Position => transform.position;
        public bool IsPathfinding => false;
        public void StopPathfinding() { /* no-op */ }
        public void MoveTo(Vector3 destination) { /* no-op — player controls movement */ }
    }
}
