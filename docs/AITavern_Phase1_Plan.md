# AI Tavern Mod — Implementation Plan (v7)

**Status:** v7. v6 approved by both reviewers (Round 6); v7 appends the §11 Scene Design Document captured via Phase 0 interactive Q&A.
**Target:** Phase 1 walking skeleton — mod boots through standard jynew flow, tavern scene with player + 2 NPCs, NPCs converse via Grok when adjacent, dialog through existing `ChatUIPanel`. No memory, no combat, no novel-derived lore.

---

## 0. Naming conventions

All of these MUST be the same lowercase string:
- Mod folder: `Assets/Mods/aitavern/`
- `MODRootConfig.ModId`: `"aitavern"`
- `native_mods.txt` entry: `aitavern`
- Built AssetBundles: `aitavern_mod`, `aitavern_maps`
- Generated descriptor: `Assets/StreamingAssets/aitavern.xml`

C# class names and `.unity` filenames may be PascalCase (e.g. `AITavernBoot.cs`, `AITavern.unity`).

---

## 0.5 Phase 0 — Scene Design (interactive, before Phase 1 implementation)

Phase 1 implementation needs concrete inputs: which scene, which character roster, what background story and relationships, what extra items. Phase 0 is an interactive Q&A with the user, captured as **§11 Scene Design Document** appended to this plan before Phase 1 starts.

### Design decisions captured in Phase 0
1. **Base scene**: pick an existing jynew tavern/region to copy, or build new. Inputs: list of candidate scenes from `Assets/Mods/JYX2/Maps/GameMaps/` filtered for tavern/indoor archetype (e.g. `40_yuelaikezhan`, `01_heluokezhan`, `60_longmenkezhan`, and any 射雕-themed scenes that exist).
2. **Character roster**: 2–3 NPCs for Phase 1 (default 郭靖 14 + 黄蓉 15; can substitute or add). Phase 5 expands to 5–8.
3. **Background story / setup** (prose): why these characters are together right now, what their immediate intents are, and the **directed relationship graph** between them. Relationship type ∈ `{Ally, Friend, Neutral, Rival, Enemy}`. Each character's relationship to each other character is one of these. Asymmetric is allowed (A admires B; B is wary of A).
4. **Per-character `InterestIn` map**: 0..1 score per other character indicating how much this character wants to seek them out for conversation. Defaults to 0.3 (ambient curiosity) if unspecified. Drives the interest-weighted candidate selection in §3.6.
5. **Extra items in scene**: any props or starting items spawned in world (table, wine cup, an item on a shelf, a hidden artifact). Plus initial inventory per NPC (Phase 3 drops these on death).
6. **Seeded conversation hooks**: optional 1-2 sentence "initial intent" per character that grounds the first conversation (e.g. 黄蓉's `Plans = "find news of 周伯通"`).

### Phase 0 process
- Master agent asks the user via `AskUserQuestion` for: scene choice, roster confirmation, story summary, relationship graph, interest map, extra items, seed hooks.
- Output rendered as `§11 Scene Design Document` appended below.
- Plan v6 = v5 + §11 design doc. v6 is the canonical input to Phase 1 implementation.

### Phase 0 output schema (filled in §11)
```yaml
base_scene: <path to existing scene to copy, or "BUILD_NEW">
roster:
  - role_id: 14
    bio_name: 郭靖
    identity: "<one-line identity>"
    plans: "<current intent>"
    starting_inventory: [<item ids>]
  - role_id: 15
    ...
relationships:                          # asymmetric directed graph
  guojing:
    huangrong: Friend
  huangrong:
    guojing: Ally
interest:                               # interest-weighted candidate scoring
  guojing:
    huangrong: 0.9
  huangrong:
    guojing: 0.9
background_story: |
  <prose, 3-6 sentences>
extra_items:                            # spawned in scene as world objects
  - id: <itemId>
    position: <vec3 or marker name>
seed_hooks:                             # optional starting topics
  - "<hook 1>"
```

---

## 1. Goals & non-goals

### In scope (Phase 1)
- Native mod `aitavern` registered through jynew's existing mod system.
- One scene (per Phase 0 design) loadable through the standard new-game flow via a START-tagged row (Id=0) in the mod's `场景.xlsx`.
- Player as embedded character (reuses `Jyx2Player`).
- 2–3 NPCs from Phase 0 roster — mod ships **full copies of all 8 baseline xlsx config files** (人物, 武功, 物品, 战斗, 场景, 加成, 小宝商店, 游戏设置) so the 8 tables `LuaConfigToCsInit` populates resolve and downstream lookups (Key 0 player, RoleInstance.Weapon/Armor/Skills, GameSettings.Refresh) don't null-deref.
- **Free-will autonomous interaction selection**: NPCs walk freely and choose conversation partners by interest score, not just by proximity (extends ai-town's pure-nearest selection — see §3.6).
- Agent tick loop, conversation FSM, `IsTyping` lock, per-pair cooldown — all faithful to ai-town.
- Grok HTTP client with `max_tokens` + stop words.
- Dialog through `ChatUIPanel` — non-modal by default, with auto-dismiss timer for NPC↔NPC turns.
- `ParticipatedTogether` map for per-pair cooldown.
- `RelationshipGraph` populated from Phase 0 (used by interest scoring now; used by Phase 3 ally-joining).

### Explicit Phase 1 simplifications (deliberate departures from ai-town)
- **Activities**: `agentDoSomething` activity arm = no-op `idle`. Branch structure preserved.
- **Memory**: `MemoryStash` records `{pairKey, transcript, endedAt}` only. No LLM importance, no embeddings, no recall, no reflection.
- **Player accept/decline UI**: player auto-accepts an outstanding NPC invite when within `CONVERSATION_DISTANCE`. Explicit accept/decline UI is Phase 5 polish.
- **Global Grok rate-limit mutex**: omitted in Phase 1 since `IsTyping` per-conversation + single-threaded `AgentDecision.Tick` already serialize a 2-NPC tavern. TODO marker for Phase 5 when multiple conversations may run in parallel.

### Out of scope until later phases
- Memory + embeddings + importance scoring + reflection (Phase 2).
- Hostile FSM transition → `BattleLoader` (Phase 3).
- Novel lore extraction pipeline (Phase 4).
- Full 5–8 cast, activities, 3D bubbles, daily schedule (Phase 5).

---

## 2. jynew grounding (verified against code)

### 2.1 Mod packaging
- Native mods live in `Assets/Mods/<id>/`. `ModSetting.asset` is a `MODRootConfig` SO; `RuntimeEnvSetup.Setup()` loads it via the mod-rewritten path `Assets/ModSetting.asset` (`RuntimeEnvSetup.cs:111`).
- Build pipeline (`JynewBuilder.cs`, `Jyx2ModTool.cs:122-133`) produces AssetBundles named `<modId>_mod` and `<modId>_maps`. **Bundle assignment is per-asset via the meta `assetBundleName` field.** Folder-level `meta` files may carry the bundle name to assign children by inheritance (verified in JYX2's `Configs.meta`).
- `GameModNativeLoader` (`GameModNative.cs:47-117`) enumerates `native_mods.txt`, requires `<id>.xml` (`GameModInfo`) in StreamingAssets, then loads `<id>_mod` and `<id>_maps`.
- C# scripts under `Assets/Mods/<id>/Scripts/` compile into the main Unity assembly.
- XLua codegen: irrelevant for Phase 1 (no Lua bridges added). Phase 2+ that adds bridges must rerun codegen.

### 2.2 AssetBundle assignment

| Asset / Folder | Bundle name |
|---|---|
| `Assets/Mods/aitavern/ModSetting.asset` | `aitavern_mod` |
| `Assets/Mods/aitavern/Scenes/AITavern.unity` | `aitavern_maps` |
| `Assets/Mods/aitavern/Configs/` (folder, inherited by children including auto-generated `Configs/Lua/*.lua`) | `aitavern_mod` |
| `Assets/Mods/aitavern/Configs/Bio_*.asset` | `aitavern_mod` (inherited) |
| `Assets/Mods/aitavern/Lua/` (folder, including `modentry.lua` — see §4.1) | `aitavern_mod` |
| Any prefabs under `Assets/Mods/aitavern/Prefabs/` | `aitavern_mod` |
| Mod-local heads under `Assets/Mods/aitavern/BuildSource/head/*.png` | `aitavern_mod` |

Set via each asset's Inspector → AssetBundle dropdown, or folder-level `.meta` for inheritance.

### 2.3 Boot & scene entry (standard new-game flow)
- Mod panel → `RuntimeEnvSetup.SetCurrentMod` → `SceneManager.LoadScene("0_MainMenu")` (`ModPanelNew.cs:168`).
- Mod ships `Configs/场景.xlsx` with **a row at Id=0, `Tags=START`** describing `AITavern.unity`. `LuaToCsBridge.MapTable` is `Dictionary<int, LMapConfig>` (`Jyx2LuaToCsBridge.cs:538`); `GameMainMenu.OnCreateRoleYesClick` accesses `MapTable[0].GetGameStartMap()` (`GameMainMenu.cs:210`). Pinning Id=0 avoids the `KeyNotFoundException` that would fire if the only START row had any other Id.
- `MODRootConfig.EnableSaveBigMapOnly = false` (default true, `MODRootConfig.cs:37`) so the save panel doesn't reject the tavern scene.
- No custom main-menu button. No `MainMenuBg.prefab` override needed.

### 2.4 Scene & player
- Tavern scene: copy `40_yuelaikezhan` (悦来客栈) into `Assets/Mods/aitavern/Scenes/AITavern.unity`. Strip pre-placed scripted GameEvents that don't apply; keep `Level/`, `Level/Player`, NavMesh. Verify in editor that NavMesh wander has reasonable open space before locking choice. **Pre-strip verification**: confirm no `kaXXX.lua` files reference the stripped triggers (would dangle if not cleaned).
- Player prefab at `Level/Player`, driven by `Jyx2Player` + `NavMeshAgent`.
- `AITavernBoot.Start` order: `await RuntimeEnvSetup.Setup()` → `await UniTask.WaitUntil(() => LevelMaster.Instance?.GetPlayer() != null)` → only then read `LuaToCsBridge.CharacterTable` and spawn NPCs.

### 2.5 NPC data — ship full baseline config set
- World NPC instantiation: `RoleHelper.CreateRoleView(roleInstance, "NPC")` → `BattleRole` prefab; `BattleRole.RefreshModel` depends on `LRoleConfig.ModelFileKey` from `人物.xlsx`.
- Beyond `人物.xlsx`, `LuaConfigToCsInit` (`Jyx2LuaToCsBridge.cs:567-583`) populates 8 static tables — Character, Skill, Item, Battle, Extra, Map, Shop, Settings. Mod's `AddConfigTable` (`Jyx2ConfigMgr.lua:18-21`) REPLACES rather than merges, so missing source xlsx → null table → boot crash:
  - `GameSettings.Refresh` (`Jyx2ResourceHelper.cs:79` → `GameSettings.cs:71`) reads `SettingsTable.Values` — null deref without `游戏设置.xlsx`.
  - `InitAllRole` (`GameRuntimeData.cs:229-237`) constructs `RoleInstance` for every CharacterTable entry; reads `ItemTable` for Weapon/Armor/Xiulianwupin (`RoleInstance.cs:421,427,434`) and `SkillTable` (`SkillInstance.cs:117`). Roles 14, 15 have skills/equipment → null deref without `武功.xlsx` and `物品.xlsx`.
  - Reference: xiastart_roguelike ships all 8 xlsx files (`Assets/Mods/xiastart_roguelike/Configs/`).
- **Therefore the mod ships full copies of all 8 baseline xlsx as `Assets/Mods/aitavern/Configs/{人物, 武功, 物品, 战斗, 场景, 加成, 小宝商店, 游戏设置}.xlsx`** — verbatim copies of JYX2's base data, with the single addition of one START row at Id=0 in `场景.xlsx` for `AITavern.unity`. Future phases may trim.
- `CharacterBio` ScriptableObjects (under `Configs/Bio_*.asset`) carry the **agent-layer** identity used in prompts/decisions. They sit alongside the `RoleInstance` data; `AgentId` ↔ `RoleId` is mapped in `NPCRegistry`.
- World-NPCs are parented under a `Level/NPC` GameObject (referenced by `LevelMaster.cs:200`). `AITavernBoot` creates this GameObject if missing.

### 2.6 Player-to-NPC interaction (custom MonoBehaviour, not GameEvent)
- `GameEvent`/`GameEventManager` dispatches via EventGraph or Lua file pattern only (`GameEventManager.cs:139-189`). No arbitrary C# delegate path.
- `AITavernInteractable` MonoBehaviour on each NPC. `AgentSimulator.Update` checks the player's nearest `AITavernInteractable` within `INTERACT_RANGE` and polls **the same Rewired action id that `Jyx2_PlayerInput` uses for the Interact action** (`Jyx2_PlayerInput.cs` — identify exact action id at impl time; do NOT bind a KeyCode directly since the project is Rewired-driven).
- On interact: `Conversation.StartPlayerInitiated(playerAgent, npc.AgentId, now)`.

### 2.7 Dialog UI behavior
- `ChatUIPanel` (`ChatUIPanel.cs`) does NOT touch `StoryEngine.BlockPlayerControl`. The block is set by callers (`Jyx2LuaBridge.cs:73,76,163,168,177,182,205,210`). `Jyx2Player.CanControlPlayer` (`Jyx2Player.cs:192-210`) consults `StoryEngine.BlockPlayerControl` but never `ChatUIPanel` open state.
- `ChatUIPanel.IsOnly = true` (`ChatUIPanel.cs:36`) → only one instance at a time; `Jyx2_UIManager` refuses a second show.
- **`AITavernBubbleUI` owns a message queue**. When a message arrives while the panel is open, it queues. Auto-dismiss timer (`NPC_BUBBLE_AUTO_DISMISS_MS = 6_000`) advances the queue for NPC↔NPC turns so the FSM doesn't stall on a player who isn't clicking. Player-turn messages do NOT auto-dismiss — they're for the player to read.
- Per-message modal flag: only set `StoryEngine.BlockPlayerControl = true` when `isPlayerTurn == true`; reset on dismiss.

### 2.8 Combat (Phase 3 forward-look, not built in Phase 1)
- Entry: `LevelLoader.LoadBattle(LBattleConfig, callback)` (`LevelLoader.cs:51-54`).
- `CsBattleConfig` (`Jyx2LuaToCsBridge.cs:47-100`) is the code-side constructible config; bypasses `BattleTable`.
- Dynamic role injection via `BattleLoader.AddDynamicRole` (`BattleLoader.cs:233`).
- Battle pos naming: `Level/BattlePos/battle{id}/{team}_{idx}` (`BattleLoader.cs:227, 318-327`).
- Outcome: soft knockout (set `RoleInstance.Hp = 1`, write a "defeated by X" memory). Not the global game-over flow.

### 2.9 Audio / map config
- Set `InMusic` in the mod's `场景.xlsx` row to a valid music id; otherwise the scene loads silent.

---

## 3. ai-town grounding (verified against code; constants from the file)

### 3.1 Agent state
Source: `convex/aiTown/agent.ts:26-50`.

```
class Agent {
    GameId AgentId
    GameId PlayerId
    GameId? ToRemember               // pending conversation to summarize (Phase 2)
    long? LastConversation           // ms epoch; set on Conversation.Stop
    long? LastInviteAttempt          // ms epoch; set on invite attempt
    InProgressOperation? Operation
    bool IsHuman                     // local addition: AgentDecision skips ticking; used in always-accept-human rule
}
```

### 3.2 Conversation state
Source: `convex/aiTown/conversation.ts:17-149`.

```
class Conversation {
    Guid Id
    GameId CreatorPlayerId
    long Created
    IsTypingLock? IsTyping           // { PlayerId, MessageUuid, Since }
    Message? LastMessage             // { Author, Timestamp, Text }
    int NumMessages
    Dictionary<GameId, ConversationMember> Participants
    List<Message> Transcript         // for Grok prompt context & Phase 2 backfill
}

class ConversationMember {
    long Invited                     // ms epoch; INVITE_TIMEOUT anchor
    MemberStatus Status              // Invited | WalkingOver | Participating(StartedAt)
}
```

### 3.3 Pair cooldown — `ParticipatedTogether`
Source: `convex/aiTown/agent.ts:336-367`, `conversation.ts` stop method.

```
class ParticipatedTogether {
    Dictionary<(GameId, GameId), long> LastEndedByPair    // both orderings stored
    void Record(p1, p2, endedAt)
    long? LastEnded(p1, p2)
}
```

### 3.4 Agent.Tick decision tree (verbatim from `agent.ts:52-236`)

1. **InProgressOperation** (`:57-64`): if set and `now < started + ACTION_TIMEOUT` → return; else clear stale.
2. **Activity cancellation side effect** (`:71-73`): if `Activity.Until > now` AND (in conversation OR pathfinding) → `Activity.Until = now`.
3. **agentDoSomething gate** (`:78-92`): if `!conversation && !doingActivity && (!Pathfinding || !recentlyAttemptedInvite)` → schedule, return.
   - `recentlyAttemptedInvite = LastInviteAttempt && now < LastInviteAttempt + CONVERSATION_COOLDOWN`.
4. **toRemember** (`:94-105`): if set → schedule `agentRememberConversation`, clear, return.
5. **Conversation branch** (`:106-235`):
   - **Invited** (`:111-126`): accept if `otherAgent.IsHuman` (always) OR `Math.random() < INVITE_ACCEPT_PROBABILITY`; else reject. On accept, clear `Pathfinding`. **NPCRegistry.GetAgent(otherPlayerId).IsHuman** is the lookup.
   - **WalkingOver** (`:127-160`):
     - If `member.Invited + INVITE_TIMEOUT < now` → leave, return.
     - If `distance(self, other) < CONVERSATION_DISTANCE` → return (transition handled by Conversation.Tick).
     - If not pathfinding → set destination: `< MIDPOINT_THRESHOLD` → direct, else midpoint.
   - **Participating** (`:161-234`):
     - If `IsTyping != null && IsTyping.PlayerId != self` → return.
     - If `!LastMessage`:
       - `isInitiator = CreatorPlayerId == self`
       - `awkwardDeadline = participating.StartedAt + AWKWARD_CONVERSATION_TIMEOUT`
       - If `isInitiator || awkwardDeadline < now` → grab typing lock, schedule `agentGenerateMessage(start)`, return.
       - Else return.
     - If `participating.StartedAt + MAX_CONVERSATION_DURATION < now || NumMessages > MAX_CONVERSATION_MESSAGES` → grab lock, schedule `agentGenerateMessage(leave)`, return.
     - If `LastMessage.Author == self && now < LastMessage.Timestamp + AWKWARD_CONVERSATION_TIMEOUT` → return.
     - If `now < LastMessage.Timestamp + MESSAGE_COOLDOWN` → return.
     - Grab lock, schedule `agentGenerateMessage(continue)`, return.

### 3.5 Conversation.Tick (faster cadence than Agent.Tick)
Source: `conversation.ts:68-107`.

For each active conversation each frame:
- If `IsTyping != null && now > IsTyping.Since + TYPING_TIMEOUT` → clear.
- If both participants in `WalkingOver` AND `distance(p1, p2) < CONVERSATION_DISTANCE`:
  - Stop both pathfinding; transition both to `Participating(StartedAt = now)`; (Phase 1: face each other = stop in place; orient is polish).

### 3.6 The three operations

Source: `convex/aiTown/agentOperations.ts:107-146`, `convex/agent/conversation.ts`.

| Operation | Phase 1 behavior |
|---|---|
| `agentDoSomething` | Compute `justLeftConversation = Agent.LastConversation && now < Agent.LastConversation + CONVERSATION_COOLDOWN` (`agentOperations.ts:107-115`). This DOES NOT abort the op — it (a) biases the not-pathfinding branch to wander over activity (`agentOperations.ts:115`), and (b) forces `invitee = undefined` in the pathfinding branch (`agentOperations.ts:147-149`, OR'd with `recentlyAttemptedInvite`). Then: if currently pathfinding → look for an invitee via **interest-weighted selection** (see below), filtering candidates by `ParticipatedTogether.LastEnded(self, candidate) + PLAYER_CONVERSATION_COOLDOWN < now` (per-pair cooldown). If not currently pathfinding → randomly pick wander vs no-op idle activity. The op always runs to completion and writes state via `finishDoSomething`. Activity arm is no-op in Phase 1; branch structure preserved. |

**Interest-weighted candidate selection (Phase 1 extension to ai-town's pure-nearest `findConversationCandidate`):**

```csharp
// Top-level short-circuit (mirrors ai-town agentOperations.ts:147-149 ternary):
if (justLeftConversation || recentlyAttemptedInvite) return null;

// otherFreePlayers excludes anyone currently in an active conversation
// (mirrors agent.ts:82-87 pre-filter in Agent.tick); also filter pair cooldown.
foreach (var other in otherFreePlayers.Where(o => !ParticipatedTogether.IsCoolingDown(self, o, now))) {
    float interest = self.Bio.InterestIn(other.Bio.AgentId);     // 0..1, default 0.3 ambient
    float proximity = 1f - Mathf.Clamp01(distance(self, other) / SCENE_RADIUS);
    long? lastEnded = ParticipatedTogether.LastEnded(self, other);
    bool lastPartnerOfThisPair = lastEnded.HasValue && now < lastEnded.Value + 2 * CONVERSATION_COOLDOWN_MS;
    float recencyDamp = lastPartnerOfThisPair ? 0.5f : 1.0f;
    float score = interest * proximity * recencyDamp;
    if (score > MIN_CANDIDATE_SCORE) candidates.Add((other, score));  // MIN_CANDIDATE_SCORE = 0.05f
}
if (candidates.Count == 0) return null;   // matches ai-town's candidates[0]?.id → undefined (agent.ts:366)
// Weighted random pick (NOT argmax — keeps behavior non-deterministic and "free will"-feeling)
return WeightedSample(candidates, _rng);  // _rng seeded by AITavernConstants.RandomSeed
```

`SCENE_RADIUS` is a per-scene constant (default 20m for an indoor tavern) set in `AITavernConstants` or per-scene config. `recencyDamp` is a soft damp on top of the hard pair cooldown to avoid back-to-back invites immediately after the pair cooldown expires; window = `2 * CONVERSATION_COOLDOWN_MS`. `WeightedSample` uses a `System.Random` seeded from `AITavernConstants.RandomSeed` for test determinism. The empty-candidate-set path returns `null` (caller treats as "no invite this op; wander/idle instead").
| `agentGenerateMessage` | Grok HTTP call with prompt from §4.6. |
| `agentRememberConversation` | Phase 1 stub: append `{pairKey, transcript, endedAt}` to `MemoryStash` (in-process). |

**Two cooldowns are distinct**:
- `justLeftConversation` = per-self gate on `Agent.LastConversation + CONVERSATION_COOLDOWN`.
- `PLAYER_CONVERSATION_COOLDOWN` = per-pair gate from `ParticipatedTogether`.

### 3.7 Constants

Source `convex/constants.ts` unless flagged retune.

```
TICK_INTERVAL_FRAME           = per-frame (Update)        // for Conversation.Tick
TICK_INTERVAL_AGENT_MS        = 500                       // for AgentDecision.Tick (matches ai-town's STEP_INTERVAL)
CONVERSATION_DISTANCE         = 2.0 m       // RETUNE from 1.3 tiles → 3D metres
MIDPOINT_THRESHOLD            = 6.0 m       // RETUNE from 4 tiles → 3D metres
INVITE_ACCEPT_PROBABILITY     = 0.8         // file
INVITE_TIMEOUT_MS             = 60_000      // file
AWKWARD_CONVERSATION_TIMEOUT  = 20_000      // RETUNE — ai-town file active = 60_000 (commented prod = 20_000); we use prod
MESSAGE_COOLDOWN_MS           = 2_000       // file
MAX_CONVERSATION_DURATION_MS  = 120_000     // RETUNE — ai-town file active = 600_000 (10 min); we use 2 min for testability
MAX_CONVERSATION_MESSAGES     = 8           // file
CONVERSATION_COOLDOWN_MS      = 15_000      // file. DUAL-USE: justLeftConversation AND recentlyAttemptedInvite
PLAYER_CONVERSATION_COOLDOWN  = 60_000      // file. Per-PAIR, enforced via ParticipatedTogether
ACTION_TIMEOUT_MS             = 120_000     // file (60_000 is the commented "normally fine" alt; we use the active value)
TYPING_TIMEOUT_MS             = 15_000      // file
ACTIVITY_COOLDOWN_MS          = 10_000      // file — present for branch fidelity; Phase 1 activity arm is no-op
GROK_MAX_TOKENS               = 200         // RETUNE — ai-town uses 300; tightened guardrail
NPC_BUBBLE_AUTO_DISMISS_MS    = 6_000       // local addition for non-blocking NPC↔NPC bubbles
```

Lives in `AITavernConstants.cs`. Every retune is flagged.

### 3.8 One-operation invariant

ai-town `agent.ts:238-257` sets `inProgressOperation` synchronously then throws if already set. C# port:

```csharp
// AgentDecision.Tick, when emitting an op:
if (agent.Operation != null && now < agent.Operation.StartedAt + ACTION_TIMEOUT_MS) return;
agent.Operation = new InProgressOperation(name, opId, now);   // synchronous write BEFORE await
RunOperationAsync(agent, name, args)
    .ContinueWith(_ => agent.Operation = null);                // clear on success/fail
```

All mutation runs on Unity main thread. Two consecutive ticks during a Grok call see `Operation != null` and bail.

### 3.9 Conversation.Start invariants

Source: `conversation.ts:127-149`.

- Rejects if either participant is already in any active conversation.
- **Creator (initiator) starts in `WalkingOver`; invitee starts in `Invited`.**
- `member.Invited = now` set for both at creation.
- Transition `WalkingOver → Participating` is per-tick proximity check in `Conversation.Tick` (§3.5).
- Path: invitee's Agent.Tick processes `Invited` → either accepts (→ `WalkingOver`, both then close distance) or rejects (→ conversation ends).

### 3.10 Player participation in the FSM

- Player is registered in `NPCRegistry` with `IsHuman = true`. `AgentDecision.Tick` skips human agents.
- **NPC invites player** (NPC-initiated):
  - NPC's `agentDoSomething` picks player as candidate. `Conversation.Start(npcAgent, playerAgent, now)` → NPC = `WalkingOver` (creator), player = `Invited`.
  - NPC's WalkingOver branch walks the NPC toward the player (`agent.ts:127-160`). Player walks themselves via `Jyx2_PlayerInput`.
  - Auto-accept on proximity: `AITavernBubbleUI.Update` checks "player has an outstanding `Invited` membership AND inviter within `CONVERSATION_DISTANCE`" → calls `Conversation.AcceptInvite(playerAgent)` which transitions Invited → WalkingOver. `Conversation.Tick` then handles the proximity transition to Participating.
- **Player invites NPC** (player-initiated):
  - `AITavernInteractable` interact → `Conversation.StartPlayerInitiated(playerAgent, npcAgent, now)` → player = `WalkingOver` (creator), NPC = `Invited`.
  - NPC's next tick processes `Invited`; since `otherAgent.IsHuman`, always accepts (§3.4 step 5 Invited branch + `NPCRegistry.GetAgent(other).IsHuman` lookup) → NPC = `WalkingOver`.
  - Player walks themselves. NPC closes the distance via `agent.ts:127-160`. `Conversation.Tick` triggers the proximity transition.
- **Player as Participating creator — initial-message handling**:
  - §3.4 step 5 (Participating, `!LastMessage`): `isInitiator = creator == self`. For the NPC, `isInitiator = false`, so the NPC waits until `awkwardDeadline = participating.StartedAt + AWKWARD_CONVERSATION_TIMEOUT (20s)`.
  - Player can preempt by typing into the speak panel (free-text) before that deadline; if so, the player's message becomes `LastMessage` and the NPC's next tick falls into the continue path with no awkward wait.
  - If player doesn't preempt, NPC breaks silence after 20s with a `start`-style line.
- **Player as Participating speaker**:
  - `AITavernBubbleUI` shows a `ChatUIPanel.ShowSelection` with `["（说话）", "（沉默离开）"]`.
  - Picking "说话" opens `AITavernPlayerSpeakPanel` (new prefab, see §4.7). **On open**, the panel calls `Conversation.SetIsTyping(playerAgent, uuid, now)` — mirroring ai-town's `MessageInput.tsx:30-50` where the human player grabs the typing lock on first keypress. If another agent already holds the lock the panel refuses to open and shows a brief "...对方正在说话..." hint. Submitting calls `Conversation.AddMessage(playerAgent, text, now)`, which clears `IsTyping`. The NPC's next tick will then reply (its FSM sees `LastMessage.Author != self` and proceeds to continue path).
  - Picking "沉默离开" calls `Conversation.Leave(playerAgent, now)`.

---

## 4. Phase 1 architecture

### 4.1 File layout
```
Assets/Mods/aitavern/
├── ModSetting.asset                        # MODRootConfig; ModId="aitavern", EnableSaveBigMapOnly=false, PreloadedLua=["modentry"]
├── ModInfo.xml                             # source of <id>.xml
├── Scenes/
│   └── AITavern.unity                      # copy of 40_yuelaikezhan
├── Lua/
│   └── modentry.lua                        # defines empty LuaMod_Init / LuaMod_DeInit
├── Scripts/
│   ├── Runtime/
│   │   ├── AITavernManager.cs              # DontDestroyOnLoad singleton; owns NPCRegistry, ConversationTable, ParticipatedTogether, MemoryStash, RelationshipGraph, RNG, pre-combat snapshots. Survives LoadBattle scene unload (Phase 3 critical).
│   │   ├── AITavernBoot.cs                 # awaits RuntimeEnvSetup.Setup + LevelMaster ready; rehydrates from AITavernManager on ReturnFromBattle
│   │   ├── AgentSimulator.cs               # Update: Conversation.Tick per-frame; AgentDecision.Tick every 500ms; uses IClock (injectable for tests)
│   │   ├── Agent.cs                        # POCO (§3.1)
│   │   ├── AgentDecision.cs                # the decision tree (§3.4)
│   │   ├── AgentDoSomethingOp.cs           # §3.6
│   │   ├── AgentGenerateMessageOp.cs       # Grok call
│   │   ├── AgentRememberConversationStub.cs# §3.6 Phase 1 stub
│   │   ├── Conversation.cs                 # §3.2 + §3.5 + §3.9
│   │   ├── ConversationTable.cs            # process-level registry
│   │   ├── ConversationMember.cs           # status + Invited timestamp
│   │   ├── ParticipatedTogether.cs         # §3.3
│   │   ├── NPCBody.cs                      # NavMeshAgent wrapper
│   │   ├── NPCRegistry.cs                  # agentId ↔ NPCBody; IsHuman flag for player
│   │   ├── AITavernInteractable.cs         # player-interact MonoBehaviour
│   │   ├── IGrokClient.cs                  # interface for DI (GrokClient + MockGrokClient)
│   │   ├── GrokClient.cs                   # xAI HTTP; implements IGrokClient
│   │   ├── IClock.cs                       # interface for time mocking in tests
│   │   ├── SystemClock.cs                  # production clock (wraps Time.unscaledTime/DateTimeOffset)
│   │   ├── ConversationPrompts.cs          # §4.6
│   │   ├── AITavernConstants.cs            # §3.7
│   │   ├── AITavernBubbleUI.cs             # ChatUIPanel routing + queue + auto-dismiss
│   │   ├── AITavernPlayerSpeakPanel.cs     # new Jyx2_UIBase prefab + script for free-text input
│   │   └── MemoryStash.cs                  # Phase 1 stub
│   └── Config/
│       └── CharacterBio.cs                 # ScriptableObject
├── Configs/
│   ├── 人物.xlsx                            # FULL copy of JYX2's 人物.xlsx (Key 0 + all base rows, including 14, 15)
│   ├── 武功.xlsx                            # FULL copy (required: SkillTable null-deref guard)
│   ├── 物品.xlsx                            # FULL copy (required: ItemTable null-deref guard)
│   ├── 战斗.xlsx                            # FULL copy (used in Phase 3, also keeps BattleTable populated)
│   ├── 场景.xlsx                            # FULL copy + one new row: Id=0, Tags=START, MapScene=AITavern, InMusic=<valid id>
│   ├── 加成.xlsx                            # FULL copy (ExtraTable)
│   ├── 小宝商店.xlsx                         # FULL copy (ShopTable)
│   ├── 游戏设置.xlsx                         # FULL copy (required: GameSettings.Refresh reads SettingsTable.Values)
│   ├── Lua/                                 # auto-generated by MODRootConfig.GenerateConfigs
│   ├── Bio_GuoJing.asset                   # AgentId=guojing, RoleId=14, HeadId=14
│   └── Bio_HuangRong.asset                 # AgentId=huangrong, RoleId=15, HeadId=15
├── BuildSource/                            # optional mod-specific assets
└── (AssetBundles output to StreamingAssets at build time)
```

Plus:
- `Assets/StreamingAssets/native_mods.txt` ← append `,aitavern`
- `Assets/StreamingAssets/aitavern.xml` ← from `ModInfo.xml`
- API key: `Application.persistentDataPath/aitavern/xai_key.txt` OR env `XAI_API_KEY`. Missing key → `Debug.LogError`; scene runs in canned-line stub mode.

**`Lua/modentry.lua`** ships a minimal stub even though the mod doesn't need Lua bridges, because `RuntimeEnvSetup.Setup` calls `LuaManager.LuaMod_Init()` (`RuntimeEnvSetup.cs:114`) which `LuaManager.Call`s the function (`LuaManager.cs:245-248`). For this file to be loaded (and the `LuaMod_Init` global registered), **`MODRootConfig.PreloadedLua` must include `"modentry"`** — mirroring JYX2's own `ModSetting.asset:PreloadedLua: - modentry`. Empty implementation:
```lua
function LuaMod_Init() end
function LuaMod_DeInit() end
```

### 4.2 Two-cadence tick
```csharp
class AgentSimulator : MonoBehaviour {
    float lastAgentDecisionTick;
    void Update() {
        long now = NowMs();
        foreach (var c in conversationTable.Active) c.Tick(now);  // per-frame
        if (Time.unscaledTime - lastAgentDecisionTick >= 0.5f) {
            lastAgentDecisionTick = Time.unscaledTime;
            foreach (var a in registry.NonHumanAgents) AgentDecision.Tick(a, now);
        }
    }
}
```

### 4.3 Conversation lifecycle
- `Conversation.Start(creator, invitee, now)`: validate non-overlap; creator → `WalkingOver`, invitee → `Invited`; `Invited = now` for both; insert into `ConversationTable`.
- `Conversation.AcceptInvite(playerOrAgent)`: transitions `Invited → WalkingOver`. Auto-called for human player on proximity (§3.10); also called from `AgentDecision.Tick` Invited branch for NPCs (always accept if other.IsHuman, else probabilistic).
- `Conversation.SetIsTyping(agent, uuid, now)`: sets `IsTyping = {PlayerId, MessageUuid, Since}`. Throws if already held by a different player.
- `Conversation.ClearIsTyping()`: releases the lock (called by player-panel cancel; `AddMessage` clears it automatically).
- `Conversation.AddMessage(authorAgent, text, now)`: appends to `Transcript`, sets `LastMessage`, `NumMessages++`, clears `IsTyping`.
- `Conversation.Leave(player, now)` and `Conversation.Stop(now)`: for each NPC member, set `Agent.LastConversation = now` and `Agent.ToRemember = conversationId` (Phase 2 hook). Record pair in `ParticipatedTogether`. Remove from `ConversationTable`. Run `AgentRememberConversationStub` (writes transcript to `MemoryStash`).

### 4.4 Grok HTTP client (implements `IGrokClient`)
- POST `https://api.x.ai/v1/chat/completions` (xAI OpenAI-compatible).
- **Model id**: `grok-4.20-non-reasoning` (matches the id used by the user's own working `xai-sdk` in `project/imagine/.venv/Lib/site-packages/xai_sdk/chat.py:79`). Verify at impl time by `curl https://api.x.ai/v1/models` with the API key to confirm exposure on the OpenAI-compat endpoint; stub-mode canned lines cover the failure case if the id needs adjustment. `grok-2-*` is retired.
- Request: `messages=[system, ...history], max_tokens=200 (GROK_MAX_TOKENS), stop=["\n\n", "User:", "Assistant:"], temperature=0.85`.
- Timeout: 30 s. On error: log warning, clear `Agent.Operation`, agent stays in current state.
- **No global rate-limit mutex in Phase 1** (per §1 — `IsTyping` per-conversation + single-thread already serialize a 2-NPC tavern). Add when Phase 5 introduces parallel conversations.
- Daily cost cap: `DailyGrokCallBudget` field on `MODRootConfig` (default 200). Counter persisted to `persistentDataPath/aitavern/usage.json`.

### 4.5 NPC wander
- Wander branch uses `NavMesh.SamplePosition(self.position + randomInRadius(8m), out hit, 4m, areaMask)`. 3 attempts; on failure, idle this tick.

### 4.6 Prompts

`ConversationPrompts.BuildStartPrompt(self, other)`:
```
You are {self.Name}. {self.Identity}
You are talking with {other.Name}. About {other.Name}: {other.Identity}

{self.Plans set ? "Your current plans: " + self.Plans : ""}
{self.OpinionOf(other) set ? "Your view of " + other.Name + ": " + self.OpinionOf(other) : ""}

This is the beginning of your conversation. Stay in character. Reply in 1–3 sentences, under 200 Chinese characters. Do not narrate actions — only speak.
```

`BuildContinuePrompt(self, other, transcript)`:
```
[Same identity + other-identity block as start prompt]

Conversation so far:
{transcript}

It is now your turn. Reply in 1–3 sentences, under 200 Chinese characters. DO NOT greet again. DO NOT repeat what you just said. Stay in character.
```

`BuildLeavePrompt(self, other, transcript)`:
```
[Same identity + other-identity block]

Conversation so far:
{transcript}

This conversation has run long. Give a short, in-character farewell (1 sentence, under 50 characters), then stop.
```

`max_tokens=200` is a deliberate tightening retune from ai-town's 300 (§3.7).

### 4.7 UI integration
- `AITavernBubbleUI.ShowMessage(headId, msg, isPlayerTurn, source)`:
  - If `ChatUIPanel` already open → enqueue.
  - If `isPlayerTurn`: `StoryEngine.BlockPlayerControl = true` around the show.
  - Else: queue uses `NPC_BUBBLE_AUTO_DISMISS_MS = 6_000` to auto-advance — prevents the FSM from stalling on a player who never clicks during an NPC↔NPC turn.
- `AITavernPlayerSpeakPanel` is a new `Jyx2_UIBase` panel (new prefab + script). API: `Show(conversation, onSubmit: Action<string>, onCancel: Action)`. **On `Show`, the panel acquires the typing lock via `conversation.SetIsTyping(playerAgent, uuid, now)`**; if `conversation.IsTyping != null && IsTyping.PlayerId != playerAgent` it refuses to open (shows transient "对方正在说话..." hint). Submit calls `Conversation.AddMessage(playerAgent, text, now)` (which clears `IsTyping`). Cancel releases the lock without sending. Submits via Enter, cancels via Esc. Registered in `Jyx2_UIManager` alongside ChatUIPanel.

**Cooldown anchoring**: `MESSAGE_COOLDOWN_MS` is measured from `LastMessage.Timestamp` (message creation), NOT from bubble dismissal. Bubbles and the FSM run on independent clocks: a 6 s auto-dismiss does not extend the 2 s FSM cooldown, and the FSM never waits for the bubble to be dismissed before proceeding to the next eligible state transition.

---

## 5. Phase 1 acceptance criteria

1. `aitavern` appears in the mod panel; selecting + Launch loads `0_MainMenu`. Clicking 新游戏 starts in `AITavern.unity` with player + 郭靖 + 黄蓉 visible.
2. NPCs wander within ~3 s; an invite fires within ~30 s; both walk together and transition to `Participating` once within `CONVERSATION_DISTANCE`.
3. NPC messages render via `ChatUIPanel` with correct portrait (`HeadId`) and name (`LuaToCsBridge.CharacterTable[headId]`). Player can keep walking during NPC↔NPC turns; bubbles auto-dismiss after 6 s.
4. Player can approach an NPC, press the existing Interact key (Rewired action), get the speak/leave selection panel; "说话" opens `AITavernPlayerSpeakPanel`; submitting text → NPC reply from Grok renders.
5. Conversation ends after ≤ `MAX_CONVERSATION_MESSAGES` or ≤ `MAX_CONVERSATION_DURATION`. **Explicit cooldown test**: wait 30 s after end → no re-invite occurs (per-pair cooldown active). At 60 s, a re-invite is allowed. Both NPCs resume wandering in between.
6. `Agent.Operation` is set during Grok call and cleared on completion — log assertion. No duplicate Grok calls per NPC during a single in-flight call.
7. `Conversation.IsTyping` lock prevents both NPCs from typing simultaneously — log assertion.
8. Missing Grok API key → clear `Debug.LogError`; canned-line stub mode (3 hardcoded lines per character) runs without crashing.
9. Transcripts recorded to `persistentDataPath/aitavern/transcripts/<timestamp>.txt`.

---

## 6. Risks / open questions

### Resolved from prior rounds
- ~~Modal `ChatUIPanel`~~ → non-modal by default, opt-in modal for player turns.
- ~~Synthetic `LMapConfig`~~ → standard 场景.xlsx START row at Id=0.
- ~~GameEvent for interaction~~ → `AITavernInteractable` MonoBehaviour with Rewired Interact action.
- ~~CharacterBio sufficient~~ → ship full JYX2 `人物.xlsx` plus per-character Bio_*.asset.
- ~~Stale Grok model id~~ → `grok-4.20-non-reasoning` (matches user's xai-sdk).
- ~~MapTable[0] KeyNotFoundException~~ → mod's START row pinned to Id=0.
- ~~ChatUIPanel.IsOnly single-instance~~ → `AITavernBubbleUI` owns queue + auto-dismiss.
- ~~Missing modentry.lua~~ → ship empty `Lua/modentry.lua`.

### Remaining risks
1. **NavMesh wander quality**: Phase 0 picks scene; verify NavMesh open space in editor before locking. Fallback: dress a simpler scene.
2. **Pre-placed scripted GameEvents in source scene**: confirm stripping doesn't leave dangling `kaXXX.lua` references in JYX2 mod's Lua. Inspect before scene copy.
3. **API key shipping**: file/env approach is dev-only. Shipping the mod broadly needs a different mechanism (proxy server, etc.). Acceptable for Phase 1.
4. **`AITavernPlayerSpeakPanel` is new UI**: prefab + Jyx2_UIBase + UIManager registration. Time estimate bumped (§7).
5. **In-editor scene-debug auto-bind**: opening `Assets/Mods/aitavern/Scenes/AITavern.unity` triggers `RuntimeEnvSetup.cs:93-100` auto-binding of `aitavern`. Confirm iteration works.
6. **Phase 3 battle map**: must pick an existing JYX2 battle scene under `Assets/Maps/BattlesMaps/` containing `Level/BattlePos/0` and `Level/BattlePos/1` spawn markers (per `BattleLoader.cs:316-328`). Defer to Phase 3 implementation start; document the choice in `docs/phase3_tasks.md`.

### Open questions for user
- Phase 3 soft-knockout combat outcome — confirm before Phase 3.
- Conversation transcript file location (`persistentDataPath/aitavern/transcripts/`) — confirm.
- Player speak UX (selection + InputField) acceptable for Phase 1, or wait for Phase 5 polish?
- Confirm `grok-4.20-non-reasoning` is still exposed on the OpenAI-compat endpoint at impl time (`curl https://api.x.ai/v1/models`).
- **Asymmetric loyalty conflict** (Phase 3): an NPC who is `Ally` of aggressor AND `Friend` of victim — by the §8 precedence rule (`Ally > Friend`) they join the aggressor at p=1.0. Acceptable, or should friendship-on-victim block ally-on-aggressor?
- **Player out-of-conversation combat trigger** (Phase 3): if combat triggers between two NPCs and the player is > `PLAYER_COMBAT_PROMPT_RADIUS = 10 m` away, the player isn't prompted and combat proceeds without them. Acceptable, or should the player always be pulled in?

---

## 7. Estimated work (engineer-days)

| Component | Days |
|---|---|
| Mod scaffolding (folder, xml, native_mods entry, ModSetting, AssetBundle assignments, ship all 8 baseline xlsx, new 场景.xlsx row, modentry.lua, AITavernManager DontDestroyOnLoad singleton) | 1.5 |
| Tavern scene (Phase 0 chosen scene copy, strip events, verify `Level/UI/BlackCover` retained for fade, verify NavMesh wander) | 0.5 |
| Test asmdef scaffold + IGrokClient/IClock interfaces + MockGrokClient + FakeClock | 0.5 |
| GrokClient + key handling + budget cap (no rate-limit mutex) | 0.5 |
| Agent / Conversation / ConversationMember / ParticipatedTogether / NPCBody / NPCRegistry / MemoryStash | 2.0 |
| AgentDecision + three operations (with one-op invariant, IsHuman accept-rule) | 1.0 |
| Prompts + ChatUIPanel routing + queue + auto-dismiss | 0.5 |
| `AITavernPlayerSpeakPanel` new prefab + script + UIManager registration | 1.0 |
| AITavernInteractable + player-initiated conversation + Rewired action lookup | 0.5 |
| End-to-end testing + tuning + transcript debug logging + cooldown verification | 1.0 |
| Layer A unit test suite (24 AgentDecision branch tests + Conversation FSM + ParticipatedTogether + interest selection + IsTyping + one-op + RelationshipGraph + death lifecycle) | 1.5 |
| **Total Phase 1** | **~10 days** |

Phase 3 additional (separate budget, not Phase 1):

| Component | Days |
|---|---|
| AITavernManager pre-combat snapshot + Phase 3 round-trip integration | 1.0 |
| Hostility detection (lexicon + sentinel JSON parsing + decay/threshold latch) | 1.0 |
| RelationshipGraph faction-ally selection + probabilistic joining (seeded RNG) | 0.5 |
| WorldDropItem prefab + pickup MonoBehaviour + NPC agentTryPickup operation | 1.5 |
| Death lifecycle (cleanup ordering, in-flight callback null-guard, memory record) | 1.0 |
| Player respawn (DarkScence/LightScence reuse, position restore, inventory wipe) | 0.5 |
| Player out-of-conversation combat prompt panel | 0.5 |
| Phase 3 Layer B+C tests | 1.5 |
| **Total Phase 3** | **~7.5 days** |

---

## 8. Phases 2–5 forward look

- **Phase 2 (memory)**: `MemoryStash` already records transcripts. Phase 2 adds: per-memory importance score (separate Grok call, 0–9 per `memory.ts:246-269`); three categories `relationship`/`conversation`/`reflection` (`schema.ts:12-30`); embeddings (Grok endpoint or local); ranking = `normalize(relevance) + normalize(importance) + normalize(recency)` (`memory.ts:212-217`); `NUM_MEMORIES_TO_SEARCH=3` with 10× overfetch (`constants.ts:53`); reflection triggered when cumulative importance > 500 (`memory.ts:325-397`); inject top-3 ranked memories into `ConversationPrompts`.
- **Phase 3 (combat with death, drop, and faction allies)**:
  - **Combat scene round-trip** (canonical pattern from `Jyx2LuaBridge.TryBattleWithConfig` at `Jyx2LuaBridge.cs:2005-2034`): before `LoadBattle`, snapshot `LevelMaster.GetCurrentGameMap()`, player position/orientation, and all per-NPC world positions onto `AITavernManager` (the DontDestroyOnLoad singleton — see §4.1). After `OnBattleResult`, call `LevelLoader.LoadGameMap(currentMap, new LevelLoadPara { loadType = ReturnFromBattle, Pos = pos, Rotate = rotate })` to reload `AITavern.unity`. The manager rehydrates `NPCRegistry`, `ConversationTable`, `ParticipatedTogether`, `MemoryStash` (all carried on the manager itself), and spawns `WorldDropItem`s at saved death positions.
  - **Hostility detection**: each NPC message in a conversation is scored by a lightweight rules+LLM check. Rules contribute points for words on a hostility lexicon (`{杀, 死, 仇, 滚, 找死, ...}`). The prompt asks Grok to emit a JSON sidecar on a final line prefixed by a sentinel `\n###META\n{"hostility": 0..10, "intent": "talk"|"threaten"|"attack"}`. Parser splits message body from sidecar at the sentinel. **On JSON parse failure** (truncated by `max_tokens`, malformed, missing sentinel): fall back to `hostility = lexiconScore`, `intent = "talk"`, log the raw response for tuning, do NOT raise — conversation continues.
  - **Hostility accumulation rules**: `Conversation.HostilityScore` clamped to `[0, ∞)`. Messages with `hostility ≥ 3` ADD their score (no decay applied that turn). Messages with `hostility < 3` apply `HOSTILITY_DECAY_PER_MESSAGE` decay. Combat fires only on the message that **crosses** `HOSTILITY_THRESHOLD` upward (latch — doesn't re-fire). Crossing must also satisfy: both NPCs have a `RelationshipGraph.GetRelation(other)` of `Rival`|`Enemy` (else the threat is rhetorical and the latch suppresses for this conversation's lifetime).
  - **Combat trigger**: `TriggerCombat(conversation)` builds a code-side `CsBattleConfig`. `CsBattleConfig` collection fields default `null` (`Jyx2LuaToCsBridge.cs:47-100`) and `BattleLoader.LoadJyx2Battle` (`BattleLoader.cs:104,111`) crashes on null; if `autoTeamMates[0] == -1` it pops a `SelectRolePanel` modal (`BattleLoader.cs:131-156`) — wrong UX. Recipe:
    ```csharp
    var cfg = new CsBattleConfig();
    cfg.InitForDynamicData();
    cfg.TeamMates = new List<int>();
    cfg.AutoTeamMates = new List<int> { sideA.Combatant.RoleId };   // != -1 → bypasses SelectRolePanel
    cfg.Enemies = new List<int>();
    cfg.DynamicTeammate = sideAJoinerInstances;     // includes the side A combatant
    cfg.DynamicEnemies = sideBJoinerInstances;      // includes the side B combatant
    cfg.MapScene = "<existing JYX2 battle map name, e.g. one of the tavern-themed battle scenes>"; // see Risk 6
    cfg.Music = "<music id>";
    ```
    Roles assembled by:
    1. The two combatants (one per team).
    2. **Faction-aware ally joining**: iterate every other NPC in scene. For each, check `RelationshipGraph`:
       - `Ally` of side X → joins side X (probability `ALLY_JOIN_PROBABILITY = 1.0`).
       - `Friend` of side X → joins side X (probability `FRIEND_JOIN_PROBABILITY = 0.5`).
       - `Enemy` of side X → joins the OPPOSITE side (probability `ENEMY_JOIN_OPPOSITE_PROBABILITY = 0.7`).
       - Otherwise stays out.
       - **Conflicting relations** (e.g. `Ally` of both sides; `Ally` of A AND `Friend` of B — asymmetric loyalty conflict): refuse to join; visible Bystander. The precedence is `Ally > Friend > Enemy > Neutral`; if two relations of the same precedence point opposite ways, conflict-bystander.
       - **Probability RNG**: a `System.Random` seeded from `AITavernConstants.RandomSeed XOR conversationId.GetHashCode()` so the same combat scenario reproduces in test runs but different combats roll independently.
    3. Player joining: if player was a participant in the trigger conversation, they auto-join their conversation partner's side. Otherwise (NPC↔NPC trigger, player not in conversation): if player is within `PLAYER_COMBAT_PROMPT_RADIUS = 10 m` of either combatant, a one-time decision panel appears ("加入哪边？郭靖 / 黄蓉 / 离开"). Beyond that radius, player is unaware — combat proceeds without them.
  - Combat scene loads via `LevelLoader.LoadBattle(cfg, OnBattleResult)` after the snapshot above.
  - **Death handling on combat return**:
    - For each `RoleInstance` with `Hp <= 0` in `BattleResult.Roles`: instead of game-over flow, treat as scene-removed.
    - Compute drop location: the NPC's last world position in the tavern scene (snapshotted on `AITavernManager` before `LoadBattle`).
    - **Agent lifecycle cleanup BEFORE de-register** (avoids stale-callback null-deref):
      1. `Agent.Operation = null` (cancels marker; in-flight Grok callbacks must null-check the agent before mutating shared state).
      2. Any active `Conversation` containing the dying agent: force `Conversation.Stop(now)` BEFORE de-registration so survivors' `LastConversation` / `ToRemember` are set normally.
      3. De-register from `NPCRegistry`.
    - **Items dropped** (per `RoleInstance.Items` which is `List<CsRoleItem>` — `{Id, Count}` per `Jyx2LuaToCsBridge.cs:140-150`): spawn one `WorldDropItem` per `CsRoleItem` with `Count` baked in. Equipped `Weapon`/`Armor`/`Xiulianwupin` (separate fields on `RoleInstance`, not in `Items`) are also dropped as `WorldDropItem`s (each `Count=1`). Pickup adds back as `CsRoleItem`, never as bare itemId.
    - **Death memory**: append a `MemoryStash.DeathRecord` keyed to each survivor (NOT the dead agent). Phase 2 reads these as `MemoryType.Relationship` (per ai-town `agent/schema.ts:12-30`), `targetPlayerId = <dead agent's id>`, `description = "<self> 与 <other> 决斗, <other> 身亡。"`, `importance = 9` (pre-set, skips the LLM importance call for known-high-importance events).
    - For player death: same item-drop logic. Mark player as `Defeated` in `GameRuntimeData` but do NOT trigger game-over (the `UI/GameOver.cs` flow exits to main menu — not reusable for respawn). Player respawns at scene entry with empty inventory after a fade using `Jyx2LuaBridge.DarkScence(callback)` → reposition → `LightScence(callback)` (`Jyx2LuaBridge.cs:305-340`). **The tavern scene must retain `Level/UI/BlackCover`** (inherited from the source scene copy; verify in Phase 0 strip-down).
  - **Pickup loop**:
    - PC pickup: `WorldDropItem` is a custom MonoBehaviour using the same `Jyx2PlayerAction.Interact1` Rewired action as `AITavernInteractable` (§2.6). NOT a `GameEvent` (`GameEventManager` only dispatches via Lua-file ids or EventGraph assets, which runtime-spawned drops have neither). Visual: new prefab at `Assets/Mods/aitavern/Prefabs/WorldDropItem.prefab` (mesh + collider + the MonoBehaviour); no existing item-on-ground prefab in jynew (`MapChest` is a chest, not a ground drop).
    - NPC pickup: new agent operation `agentTryPickup` invoked from `agentDoSomething`'s branching. When wandering, if a `WorldDropItem` is within `PICKUP_RADIUS` and `Bio.WantsItem(itemId)` passes (per-character desirability heuristic), agent walks to it and picks up. Picked-up `CsRoleItem`s go into the NPC's `RoleInstance.Items`.
  - **Survivor reaction**: after combat resolution, each survivor's `MemoryStash.DeathRecord` (above) covers the memory write — no separate `agentRememberConversation` synthesis needed for the death event (the conversation that triggered combat is force-stopped per the lifecycle cleanup, which fires the normal remember-conversation path for THAT chat).
  - Constants:
    ```
    HOSTILITY_THRESHOLD            = 7        // crossing-upward latch; doesn't re-fire
    HOSTILITY_DECAY_PER_MESSAGE    = 1        // applied only when current msg hostility < 3
    ALLY_JOIN_PROBABILITY          = 1.0
    FRIEND_JOIN_PROBABILITY        = 0.5
    ENEMY_JOIN_OPPOSITE_PROBABILITY= 0.7
    PICKUP_RADIUS                  = 2.5 m
    PLAYER_COMBAT_PROMPT_RADIUS    = 10 m
    PLAYER_RESPAWN_FADE_MS         = 3_000    // implemented via Jyx2LuaBridge.DarkScence/LightScence
    ```
- **Phase 4 (lore)**: `tools/lore_pipeline/` Python with `xai-sdk`. Chunks 射雕 → extracts per-character `{name, identity, plans, opinions}` → emits `CharacterBio` assets via Unity importer.
- **Phase 5 (full cast + polish)**: 5–8 chars; activity layer with `idle/eating/drinking`; 3D-floating bubbles replace `ChatUIPanel` for NPC↔NPC; daily schedule; global Grok rate-limit mutex for parallel conversations.

---

## 11. Scene Design Document (Phase 0 output, 2026-05-13)

### 11.1 Base scene
- **Source scene**: `Assets/Mods/JYX2/Maps/GameMaps/40_yuelaikezhan.unity` (悦来客栈)
- **Copy destination**: `Assets/Mods/aitavern/Scenes/AITavern.unity`
- Strip-down: remove pre-placed scripted `GameEvent` triggers; verify `Level/UI/BlackCover` retained for Phase 3 fade; confirm `Level/Player` and NavMesh intact. Add a `Level/NPC` parent GameObject (created at runtime by `AITavernBoot` if missing).
- Pre-strip verification: confirm no `kaXXX.lua` files in JYX2 mod reference the stripped triggers (would dangle).

### 11.2 Roster (2 NPCs for Phase 1)

```yaml
- agent_id: huangrong
  role_id: 15
  head_id: 15
  bio_name: 黄蓉
  identity: |
    桃花岛黄药师之女，年方十五，灵动机敏，武学已得家传精要。容貌秀美，
    常以乞丐少年装扮行走江湖。心思缜密、口才锋利。对人有情亦有戒。
  plans: |
    暂避此客栈，等追兵走远。打量在场之人，认清谁可信、谁不可信。
    若条件允许，再设法取回那只角落里的箱子。
  starting_inventory:
    - { id: 36, count: 1 }   # 打狗棒 (placeholder — verify item id at impl)
  spawn_marker: NPC_Spawn_A  # add to scene as empty GameObject

- agent_id: ouyangke
  role_id: 61
  head_id: 61
  bio_name: 欧阳克
  identity: |
    白驼山欧阳锋之侄，西毒派少主。武功不弱、心术不正。
    一向看上美貌女子便不放手，对黄蓉觊觎已久。
    举止温文，言辞含蜜，实则狠毒。
  plans: |
    本是为躲追兵而入店歇脚，没想到撞见蓉儿妹妹。
    若能令她落入掌中，再不放手；若不能，至少多说几句话探她底细。
  starting_inventory:
    - { id: 994, count: 1 }   # 蛇杖 (placeholder — verify item id)
  spawn_marker: NPC_Spawn_B
```

### 11.3 Relationships (directed graph, used by interest scoring + Phase 3 ally joining)

```yaml
huangrong:
  ouyangke: Enemy           # 黄蓉 despises 欧阳克 from lore
ouyangke:
  huangrong: Rival          # 欧阳克 obsessively wants her; not Ally but not Enemy either
```

Asymmetric is intentional. For Phase 3 combat:
- A hostility trigger between this pair would satisfy the relation gate (both are `Rival|Enemy`) — combat fires when the conversation crosses `HOSTILITY_THRESHOLD`.
- No third NPC in Phase 1, so faction-ally joining doesn't fire in Phase 1.

### 11.4 Interest scores (drives §3.6 candidate selection)

```yaml
huangrong:
  ouyangke: 0.8             # high — wants to keep tabs / be ready
ouyangke:
  huangrong: 0.95           # obsessive interest
```

Both are mutually high-interest, so they will quickly seek each other out. Combined with the hostile relationship, conversation is expected to escalate.

### 11.5 Background story (system prompt grounding)

> 江南某处，悦来客栈。深秋傍晚，店中人不多。黄蓉刚从一场江湖纠纷中脱身，
> 暂避此地等追兵远离。她乔装坐在角落，目光警觉。
> 几乎同时，欧阳克也步入店中——他亦为同一伙追兵所逼，匆匆找了个座位。
> 两人四目相对的那一刻，黄蓉心中一凛，欧阳克却扬起了那副熟悉的笑容。
> 客栈角落另有一只看似无人认领的箱子，貌似贵重。
> 黄蓉与欧阳克都注意到了。

This block (translated) is woven into each character's system prompt as context.

### 11.6 Extra items in scene

```yaml
- id: world_chest
  description: 角落里的箱子，封口完好。看上去无人认领。
  position: marker "Level/Props/CornerChest"  # add empty GameObject to scene
  contents:
    - { id: <high_value_item_id>, count: 1 }   # to be picked at impl — candidate: 九阴真经残页 or rare 药物
  pickup_behavior: |
    Phase 1: visual prop only, interact key prints "箱子上了锁" debug log.
    Phase 3 (when WorldDropItem system lands): chest can be opened, items become world drops.
    Phase 3+: NPCs may attempt to claim it via agentTryPickup; the contested chest creates
    additional conflict potential beyond the personal hostility.
```

### 11.7 Seed conversation hooks (optional, surfaced as initial-message variants)

Initial topic candidates the LLM may pick from (or invent its own):
- The pursuers ("追兵") — shared danger
- The chest in the corner — shared curiosity
- The other person's presence — direct confrontation
- Old grievances (从桃花岛的恩怨开始)

The LLM is not forced to use these; they're injected only as a `Possible topics:` line in the start prompt.

### 11.8 Phase 1 acceptance evaluation against this design

Given the hostile + high-interest setup:
- §5 criterion 2 (NPCs initiate within ~30s): both have interest ~0.8+ for each other in a small scene → invite should fire almost immediately.
- §5 criterion 5 (cooldown test): after a conversation ends, the 60s `PLAYER_CONVERSATION_COOLDOWN` is the only thing holding them apart — perfect natural test of cooldown enforcement.
- Conversations may feel **uncomfortable** (lore-accurate) — that's intentional and validates the prompt grounding.
- This design is also **Phase 3-ready**: once combat lands, the hostility threshold will trigger naturally without test-jig forcing.

### 11.9 Outstanding impl-time verifications

- Confirm `RoleInstance(61)` resolves to 欧阳克 model successfully (CharacterConfig.lua row 61 confirmed present in JYX2 mod's data).
- Verify item ids `36` (打狗棒) and `994` (蛇杖) in `物品.xlsx` — substitute valid ids if these don't match.
- Pick concrete `high_value_item_id` for chest contents during scene-prep task.
- Verify `40_yuelaikezhan.unity` NavMesh allows wandering between the two spawn markers + chest corner without obstruction.

---

## 9. Testing plan (minimal user involvement)

The goal: keep manual-play testing to a per-phase smoke check; everything else is automated and runs from the command line.

### 9.1 Three test layers

**Layer A — Edit-mode unit tests (Unity Test Framework, no scene load, fastest)**
- Location: `Assets/Mods/aitavern/Scripts/Tests/Editor/`.
- **One-time scaffold** (no `*.Tests.asmdef` exists anywhere in `jyx2/Assets/` today; `com.unity.test-framework@1.1.31` IS in `Packages/manifest.json` so the package is available): create `Tests/Editor/AITavern.Tests.Editor.asmdef` and `Tests/PlayMode/AITavern.Tests.PlayMode.asmdef`, both referencing `Test Assemblies` and the main `Assembly-CSharp` (where `Agent.cs` etc. compile per §2.1). Task 1 of Phase 1 implementation.
- Covers pure-C# logic decoupled from Unity rendering. **Explicit branch checklist for `AgentDecision.Tick`** (each row = one or more table-driven test cases):
  1. `Operation != null`, `now < started + ACTION_TIMEOUT` → returns, no new op.
  2. `Operation != null`, timed out → clears, continues.
  3. `Activity.Until > now` AND in-conversation → `Activity.Until = now`.
  4. `Activity.Until > now` AND pathfinding → `Activity.Until = now`.
  5. `agentDoSomething` gate: `!conv && !activity && !pathfinding` → schedules.
  6. `agentDoSomething` gate: `!conv && !activity && pathfinding && !recentlyAttemptedInvite` → schedules.
  7. `agentDoSomething` gate: `!conv && !activity && pathfinding && recentlyAttemptedInvite` → does NOT schedule.
  8. `toRemember != null` → schedules remember, clears `toRemember`.
  9. Conversation `Invited`, `other.IsHuman` → accept (always).
  10. Conversation `Invited`, `!other.IsHuman`, rand < `INVITE_ACCEPT_PROBABILITY` → accept.
  11. Conversation `Invited`, `!other.IsHuman`, rand ≥ `INVITE_ACCEPT_PROBABILITY` → reject.
  12. Conversation `WalkingOver`, `member.Invited + INVITE_TIMEOUT < now` → leave.
  13. Conversation `WalkingOver`, within `CONVERSATION_DISTANCE` → returns (transition handled by `Conversation.Tick`).
  14. Conversation `WalkingOver`, `distance < MIDPOINT_THRESHOLD`, not pathfinding → set destination = other.
  15. Conversation `WalkingOver`, `distance ≥ MIDPOINT_THRESHOLD`, not pathfinding → set destination = midpoint.
  16. Conversation `Participating`, `IsTyping.PlayerId != self` → returns.
  17. Conversation `Participating`, `!LastMessage`, `isInitiator` → schedules `agentGenerateMessage(start)`.
  18. Conversation `Participating`, `!LastMessage`, `!isInitiator`, awkward elapsed → schedules `start`.
  19. Conversation `Participating`, `!LastMessage`, `!isInitiator`, awkward not elapsed → returns.
  20. Conversation `Participating`, `LastMessage`, `MAX_CONVERSATION_DURATION` exceeded → schedules `leave`.
  21. Conversation `Participating`, `LastMessage`, `NumMessages > MAX_CONVERSATION_MESSAGES` → schedules `leave`.
  22. Conversation `Participating`, `LastMessage.Author == self`, awkward not elapsed → returns.
  23. Conversation `Participating`, `LastMessage`, `MESSAGE_COOLDOWN` not elapsed → returns.
  24. Conversation `Participating`, all cooldowns clear, eligible → schedules `agentGenerateMessage(continue)`.

  Plus separate test groups:
  - `Conversation` FSM: Start/Accept/AddMessage/Leave/Stop transitions; IsTyping lock acquire/release/timeout.
  - `ParticipatedTogether`: pair cooldown logic.
  - Interest-weighted candidate selection (§3.6 extended): with a fixed random seed, assert deterministic winner across N candidate sets. **Edge case test**: empty eligible set returns `null` without throwing (matches ai-town `candidates[0]?.id` → undefined at `agent.ts:366`).
  - Short-circuit invariant: when `justLeftConversation || recentlyAttemptedInvite`, candidate selection returns `null` without scoring loop (matches `agentOperations.ts:147-149`).
  - `RelationshipGraph` + faction-ally combat join (Phase 3): given a roster + relation map, assert the team assignment. RNG seeded from `AITavernConstants.RandomSeed XOR conversationId.GetHashCode()` for reproducibility.
  - Hostility detection: JSON parse-success path (sentinel + valid JSON), JSON parse-failure path (lexicon-only fallback, no throw), decay-vs-add rules, threshold-crossing latch (doesn't re-fire), relation-gate suppression.
  - Death lifecycle: in-flight Grok callback fires AFTER agent death — null-check guards prevent mutation; force-stopped conversation correctly sets survivors' `LastConversation`/`ToRemember`.
  - One-operation invariant: simulate two ticks during an in-flight Grok call (mocked), assert no double-fire.
- All Grok HTTP calls are intercepted by an `IGrokClient` interface; tests use `MockGrokClient` returning canned/predictable strings.
- Target: ≥ 80% line coverage of `Scripts/Runtime/Agent*`, `Conversation*`, `ParticipatedTogether*`, `RelationshipGraph*` in Phase 1 close-out.

**Layer B — Play-mode integration tests (Unity Test Framework, real scene, slower)**
- Location: `Assets/Mods/aitavern/Scripts/Tests/PlayMode/`.
- Boot `AITavern.unity`, spawn the Phase 0 roster, run the simulator with mocked Grok for N seconds of `Time.timeScale = 10`. Assert:
  - End-to-end NPC↔NPC conversation: 2 NPCs invite, walk over, exchange ≥3 messages, leave, pair cooldown active.
  - Player auto-accept on proximity to an NPC-initiated invite (player movement scripted via `Jyx2Player.transform.position` warps).
  - Combat trigger (Phase 3): inject a `MockGrokClient` that returns hostile responses; assert `BattleLoader` is invoked and the post-battle scene removes the dead NPC and spawns `WorldDropItem`s.
  - Death drop is picked up by another NPC: assert item moves to NPC's `RoleInstance.Items`.

**Layer C — Headless batch-mode scenario runs (no Unity Test Framework, full system, slowest)**
- Unity invoked from CLI: `Unity -batchmode -nographics -projectPath . -executeMethod Jyx2.AITavern.HeadlessRunner.Run -aitavern.scenario <name> -aitavern.duration 300`.
- `HeadlessRunner.Run` loads `0_MainMenu`, programmatically picks `aitavern`, loads the scene, runs the sim for N seconds (`Time.timeScale = 50` for fast-forward), dumps:
  - Full conversation transcripts → `out/<scenario>/transcripts.json`
  - Final agent state per NPC → `out/<scenario>/agents.json`
  - Death/drop log → `out/<scenario>/events.json`
- A `Scenarios/` folder ships fixtures: `basic_npc_npc`, `player_initiated`, `cooldown_test`, `combat_to_death`, `ally_joins_combat`, `cross_relationships`.
- Golden comparison: each scenario has expected event counts and invariants (not exact text — text is LLM-driven). E.g. `combat_to_death`: assert ≥1 `BattleStart`, ≥1 `Death`, ≥1 `Drop`, 0 player-game-over events.

### 9.2 Determinism
- `AITavernConstants.RandomSeed` (default `0xA17E`). All `System.Random` instances seeded from it; for per-event independence, sub-RNGs are seeded `RandomSeed XOR eventKey.GetHashCode()` (e.g. ally-join uses `conversationId`).
- **`MockGrokClient` canned-response strategy**: `Dictionary<string, string>` keyed by a substring match on the prompt's transcript-tail (last 80 chars). Test authors write `{ "黄蓉 said 'X'": "郭靖's hostile reply (with ###META sidecar)" }` directly without computing hashes. Falls back to a default per-character canned line if no key matches. For `combat_to_death` scenario, the canned table includes hostile responses with `\n###META\n{"hostility": 8, "intent": "attack"}` sentinels so hostility detection fires deterministically.
- **Clock injection (`IClock`)**: `AgentSimulator`, `Conversation.Tick`, and `AgentDecision.Tick` read `now` from an injected `IClock`. Production uses `SystemClock`. Tests inject `FakeClock` that advances in test-driven increments. This decouples cooldown verification from wall-clock — `MAX_CONVERSATION_DURATION_MS = 120_000` tests run in milliseconds, not 12 seconds.
- **NavMesh / `Time.timeScale` interaction (Layer B/C)**: do NOT crank `Time.timeScale` past 10× — NavMeshAgent steps integrate with scaled `Time.deltaTime` and overshoot `CONVERSATION_DISTANCE` at higher rates. Layer B uses `timeScale = 5`. Layer C uses `timeScale = 10` max, and AgentSimulator's tick cadence in test mode is driven by `IClock.Now()` (scaled), not `Time.unscaledTime` (which would not speed up).

### 9.3 CI integration
- A `scripts/run-aitavern-tests.ps1` PowerShell script (per repo platform):
  - Run Unity edit-mode tests via `-runTests -testPlatform editmode -testResults`.
  - Run play-mode tests via `-runTests -testPlatform playmode -testResults`.
  - Run all headless scenarios via `-executeMethod ... -scenario <name>` per scenario, with a 60s budget per.
  - Aggregate to `out/test-report.html`.
- **Headless mode caveat**: use `Unity -batchmode` WITHOUT `-nographics` on a build agent with a display server / null-renderer. jynew's `LevelMaster.Start` (`LevelMaster.cs:143-150`) gates Cinemachine on `Camera.main != null` (safe) but `GameViewPortManager.InitForLevel` may still touch RenderTextures. If `-nographics` is mandatory in CI, Layer C scenarios fall back to stripped test scenes that skip `GameViewPortManager` initialization.
- Phase end gate: this script passes locally before claiming a phase complete. No CI service required for Phase 1; phase-end manual invocation is fine.

### 9.4 What stays manual (per-phase smoke check, ~5 min)
- Open Unity Editor.
- Mod panel → launch `aitavern`.
- Verify scene loads, NPCs visible, conversation begins.
- Walk player into a conversation; verify text input works.
- Quit.
- Total user time per phase end: ~5 min. Daily during active impl: optional, agent does the heavy lifting via Layer A/B/C.

---

## 10. Implementation orchestration (per-phase coder/reviewer/tester loop)

Once the plan is finalized and Phase 0 design is captured, each phase is decomposed and driven by the master agent (this Claude session) dispatching child agents.

### 10.1 Per-phase decomposition
For each phase, the master:
1. Re-reads the phase section of this plan + the Phase 0 design doc + `docs/aitavern_invariants.md` (the algorithm-invariant checklist — extracted bullet list from §3.4, §3.6, §3.8, §3.9, §3.10, §8 so reviewers have a deterministic, tight checklist instead of re-reading the full plan + ai-town per task).
2. Decomposes into 4–10 discrete tasks, each with:
   - **What**: one-paragraph spec.
   - **Files**: concrete file paths the task touches.
   - **Acceptance criteria**: bullet list of testable claims, cross-referencing relevant invariant ids from `aitavern_invariants.md` (e.g. `INV-3.4-17` = "Participating, !LastMessage, isInitiator → schedules agentGenerateMessage(start)").
3. Writes the decomposition to `docs/phase{N}_tasks.md`.
4. Tracks task state via TodoWrite.

### 10.2 Per-task agent loop
For each task in order (parallelize independent ones):

**Coder** (general-purpose agent, Edit/Write/Bash tools, file scope limited)
- Prompt: task spec + acceptance criteria + relevant file paths + this plan + Phase 0 design doc.
- Constraint: no scope creep, no new files outside spec, no unrequested refactors.
- Returns: short diff summary.

**Reviewer** (general-purpose agent, read-only)
- Prompt: read the diff vs. the task spec + the relevant invariant ids in `docs/aitavern_invariants.md` + CLAUDE.md if present. Consult full plan or ai-town source ONLY if a checklist item is ambiguous.
- Verdict: `APPROVE` or `REQUEST CHANGES` with citations.
- If `REQUEST CHANGES`: send the punch list back to Coder; re-loop. Cap at 3 review rounds per task; escalate to master if reviewer + coder disagree.

**Tester** (general-purpose agent, Read + Bash, source code read-only)
- Prompt: run the relevant test layer(s) (per §9.1 — usually Layer A first; Layer B for cross-cutting tasks; Layer C only at phase end).
- Verdict: `PASS` / `FAIL` / `BLOCKED` with logs.
- On `FAIL`: feedback to Coder + Reviewer; re-loop.

### 10.2.1 Tasks needing Unity Editor invocation
Pure C# script tasks (the bulk of Phase 1) need only Edit/Write/Bash on the coder side. Tasks that create binary Unity assets — `.unity` scene copy, `.prefab` for `WorldDropItem`, `.asset` ScriptableObjects for `CharacterBio`, new `.asmdef` for tests, AssetBundle assignments in `.meta` files — need one of:
- A one-shot editor script invoked via `Unity.exe -batchmode -projectPath . -executeMethod <ScaffoldClass>.<Method>` (preferred; documented as a recipe in `docs/aitavern_invariants.md`).
- Hand-off to the user during the 5-min smoke check (acceptable for genuinely one-off setup).

Coder agents that need to invoke `Unity.exe` may require pre-approved Bash permissions in `.claude/settings.json` (one-time setup via `/update-config`).

### 10.3 Phase gate
A phase is closed when:
- All tasks PASS Tester AND APPROVE Reviewer.
- Layer C headless scenarios all pass.
- User runs the 5-min manual smoke check and confirms.

### 10.4 Why not use `imagine-master` / `imagine-coder` / `imagine-reviewer` / `imagine-tester` agents?
Those subagents are scoped to `project/imagine` (Python image-batch tool) per their definitions. The Unity mod lives outside that scope; we use `general-purpose` agents with carefully-scoped prompts that play the same roles. Future improvement: define `aitavern-coder`/`reviewer`/`tester` subagents under `.claude/agents/` once the patterns stabilize.
