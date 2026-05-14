# Phase 1 — Task Decomposition

Tasks are sized for one coder agent each. Dependencies noted. Acceptance criteria reference invariant ids from `docs/aitavern_invariants.md`.

Task states: `pending` / `coder-in-progress` / `review` / `test` / `done`.

---

## T1 — Mod scaffolding (foundation, ~1.5d)

**What:** Set up the bare-minimum mod skeleton so the launcher recognizes `aitavern`. No runtime code yet.

**Files:**
- `jyx2/Assets/Mods/aitavern/` (new folder)
- `jyx2/Assets/Mods/aitavern/ModInfo.xml` (descriptor — model after `Assets/Mods/JYX2/ModInfo.xml`)
- `jyx2/Assets/Mods/aitavern/Lua/modentry.lua` (empty `LuaMod_Init` / `LuaMod_DeInit` stubs)
- `jyx2/Assets/Mods/aitavern/ModSetting.asset` (copy from `Assets/Mods/JYX2/ModSetting.asset`; set `ModId="aitavern"`, `PreloadedLua=["modentry"]`, `EnableSaveBigMapOnly=false`)
- `jyx2/Assets/Mods/aitavern/Configs/` containing FULL copies of all 8 baseline xlsx files from `Assets/Mods/JYX2/Configs/`:
  - `人物.xlsx`, `武功.xlsx`, `物品.xlsx`, `战斗.xlsx`, `场景.xlsx`, `加成.xlsx`, `小宝商店.xlsx`, `游戏设置.xlsx`
- Edit `jyx2/Assets/Mods/aitavern/Configs/场景.xlsx` to ADD one row at `Id=0`, `Tags=START`, `MapScene=AITavern`, `InMusic=<valid id>` (preserve all other rows from JYX2 copy).
- Edit `jyx2/Assets/StreamingAssets/native_mods.txt` to append `,aitavern`.
- AssetBundle assignments via folder-level `.meta` files: `Configs.meta`, `Lua.meta` → `aitavern_mod`. (Scene goes in T2 → `aitavern_maps`.)

**Acceptance:**
- INV: §0 naming conventions (all lowercase `aitavern`).
- INV: §2.5 ships all 8 xlsx so `GameSettings.Refresh`, `InitAllRole`, etc. don't null-deref.
- INV: §4.1 `MODRootConfig.PreloadedLua` includes `"modentry"`.
- INV: §2.3 `场景.xlsx` row at Id=0 with `Tags=START`.
- Unity Editor opens the project without errors after these changes.
- Mod panel (run scene `0_MODLoaderScene`) lists `aitavern` among native mods.

**Notes:**
- `ModSetting.asset` is a Unity YAML asset — coder should COPY the JYX2 file and edit the relevant scalar fields rather than synthesize from scratch.
- `.xlsx` files are binary — coder copies via `cp` (Bash) from JYX2 folder.
- Tester verifies via Unity batch-mode build OR user opens Editor and confirms mod panel listing.

---

## T2 — Scene copy + scene-prep (~0.5d, depends on T1)

**What:** Copy the source tavern scene and prep it for mod use.

**Files:**
- Copy `jyx2/Assets/Mods/JYX2/Maps/GameMaps/40_yuelaikezhan.unity` → `jyx2/Assets/Mods/aitavern/Scenes/AITavern.unity` (and the `.unity.meta`).
- Update `Assets/Mods/aitavern/Scenes/AITavern.unity.meta` to `assetBundleName: aitavern_maps`.
- Edit the scene in Unity Editor (or via YAML inspection):
  - Strip pre-placed `GameEvent` objects (the `Level/Triggers/` children carrying `m_InteractiveEventId`).
  - Add three empty GameObjects: `Level/NPC/NPC_Spawn_A`, `Level/NPC/NPC_Spawn_B`, `Level/Props/CornerChest` (positions per §11.6 in plan).
  - Verify `Level/UI/BlackCover` retained (Phase 3 fade dependency, INV-8-14).
  - Verify NavMesh allows wandering between both spawn markers + chest corner without obstruction.

**Acceptance:**
- Loading `AITavern.unity` in Editor shows the tavern interior, NPCs not yet spawned, NavMesh visible in NavMesh window.
- No console errors on scene load.
- INV: §11.1 strip-down + markers added.

**Notes:**
- This task likely requires Unity Editor manual work for stripping/marker placement; coder produces a CHECKLIST and the human user performs the Editor edits during the per-phase smoke handoff (§10.2.1).

---

## T3 — Test infrastructure + IGrokClient/IClock interfaces (~0.5d, parallel with T1)

**What:** Set up Unity Test Framework asmdefs + the dependency-injection interfaces tests rely on.

**Files:**
- `jyx2/Assets/Mods/aitavern/Scripts/Tests/Editor/AITavern.Tests.Editor.asmdef` (references `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `Assembly-CSharp`)
- `jyx2/Assets/Mods/aitavern/Scripts/Tests/PlayMode/AITavern.Tests.PlayMode.asmdef`
- `jyx2/Assets/Mods/aitavern/Scripts/Runtime/IGrokClient.cs` (interface)
- `jyx2/Assets/Mods/aitavern/Scripts/Runtime/IClock.cs` (interface — `long NowMs();`)
- `jyx2/Assets/Mods/aitavern/Scripts/Runtime/SystemClock.cs` (production impl)
- `jyx2/Assets/Mods/aitavern/Scripts/Tests/Editor/FakeClock.cs` (test impl, advances on `Advance(long ms)`)
- `jyx2/Assets/Mods/aitavern/Scripts/Tests/Editor/MockGrokClient.cs` (substring-key canned table, default per-character fallback)
- One smoke test in `Tests/Editor/SmokeTest.cs` that asserts `1+1==2` to prove the test runner picks up the asmdef.

**Acceptance:**
- `Unity -batchmode -runTests -testPlatform editmode` exits 0 with the smoke test reported as PASS.
- INV: §9.1 Layer A scaffold complete.
- INV: §9.2 determinism — `FakeClock` + `MockGrokClient` available for downstream tasks.

---

## T4 — AITavernManager DontDestroyOnLoad singleton (~0.5d, depends on T1)

**What:** The persistent state carrier. Survives `LoadBattle` scene unload (Phase 3 critical, but needed structurally now).

**Files:**
- `jyx2/Assets/Mods/aitavern/Scripts/Runtime/AITavernManager.cs`

**Public surface:**
```csharp
public class AITavernManager : MonoBehaviour {
    public static AITavernManager Instance { get; private set; }
    public NPCRegistry NPCs;
    public ConversationTable Conversations;
    public ParticipatedTogether PairCooldowns;
    public MemoryStash Memory;
    public RelationshipGraph Relations;
    public IClock Clock;
    public IGrokClient Grok;
    public System.Random Rng;
    public Dictionary<GameId, Vector3> PreCombatPositions;  // Phase 3
    void Awake() { /* DontDestroyOnLoad; singleton enforcement */ }
}
```

**Acceptance:**
- Editor test: instantiate `AITavernManager`; `SceneManager.LoadScene("EmptyScene")`; assert `AITavernManager.Instance != null` after.
- INV-8-2: carries all the listed state.

---

## T5 — Core data POCOs: Agent, Conversation, ConversationMember, ConversationTable (~1.0d, depends on T1)

**Files:**
- `Scripts/Runtime/Agent.cs` (per §3.1)
- `Scripts/Runtime/Conversation.cs` (per §3.2, including `SetIsTyping/ClearIsTyping/AddMessage`)
- `Scripts/Runtime/ConversationMember.cs`
- `Scripts/Runtime/ConversationTable.cs`
- `Scripts/Runtime/InProgressOperation.cs`

**Acceptance:**
- All fields per §3.1 / §3.2 present.
- `Conversation.Start` enforces non-overlap invariant (INV-3.9-1), creator → WalkingOver, invitee → Invited (INV-3.9-2), `member.Invited = now` for both (INV-3.9-3).
- `Conversation.SetIsTyping` throws if already held by different player.
- Layer A unit tests pass for all of the above.

---

## T6 — Supporting data: ParticipatedTogether, RelationshipGraph, MemoryStash, CharacterBio SO (~0.5d, depends on T1)

**Files:**
- `Scripts/Runtime/ParticipatedTogether.cs` (per §3.3, bidirectional key)
- `Scripts/Runtime/RelationshipGraph.cs` (asymmetric directed map)
- `Scripts/Runtime/MemoryStash.cs` (Phase 1 stub: `List<{pairKey, transcript, endedAt}>`)
- `Scripts/Config/CharacterBio.cs` (ScriptableObject — `AgentId, RoleId, HeadId, BioName, Identity, Plans, OpinionOf: Dictionary, InterestIn: Dictionary, WantsItem(int) heuristic`)

**Acceptance:**
- `[CreateAssetMenu]` works in Editor (user can create `Bio_X.asset` instances).
- Tests verify ParticipatedTogether bidirectional symmetry and cooldown query.

---

## T7 — NPCBody + NPCRegistry + AITavernInteractable (~0.5d, depends on T5, T6)

**Files:**
- `Scripts/Runtime/NPCBody.cs` (MonoBehaviour wrapping NavMeshAgent on each NPC GameObject)
- `Scripts/Runtime/NPCRegistry.cs` (agentId ↔ NPCBody map; `IsHuman` flag)
- `Scripts/Runtime/AITavernInteractable.cs` (MonoBehaviour on NPCs; polled by `AgentSimulator` for the `Jyx2PlayerAction.Interact1` Rewired action; triggers `Conversation.StartPlayerInitiated`)

**Acceptance:**
- INV: §2.6 Rewired action `Jyx2PlayerAction.Interact1` used; NOT KeyCode; NOT GameEvent.
- Tests with mocked input verify interact callback fires when player is within `INTERACT_RANGE`.

---

## T8 — Conversation.Tick lifecycle (~0.5d, depends on T5)

**What:** Per-frame conversation tick (proximity transition + IsTyping timeout) and the lifecycle methods Stop/Leave (which write LastConversation/ToRemember + record pair cooldown).

**Acceptance:** INV-3.5-1, INV-3.5-2 verified by tests with `FakeClock` + scripted `NPCBody.Position`.

---

## T9 — AgentDecision.Tick + one-operation invariant (~1.5d, depends on T5, T7, T8)

**Files:**
- `Scripts/Runtime/AgentDecision.cs`

**Acceptance:**
- All 24 branches enumerated in §9.1 Layer A checklist pass as table-driven Layer A unit tests.
- INV-3.4-* + INV-3.8-* covered.

---

## T10 — AgentDoSomethingOp + interest-weighted candidate selection (~1.0d, depends on T9)

**Files:**
- `Scripts/Runtime/AgentDoSomethingOp.cs`

**Acceptance:**
- INV-3.6-1 through INV-3.6-10 all verifiable in tests.
- Edge case: empty candidate set returns null without throwing.

---

## T11 — GrokClient (HTTP) + ConversationPrompts + AgentGenerateMessageOp (~1.0d, depends on T9, T3)

**Files:**
- `Scripts/Runtime/GrokClient.cs` (implements `IGrokClient`)
- `Scripts/Runtime/ConversationPrompts.cs`
- `Scripts/Runtime/AgentGenerateMessageOp.cs`

**Acceptance:**
- Mocked `IGrokClient` test: agent emits `agentGenerateMessage`, mock returns canned text, `Conversation.AddMessage` is called with the text, IsTyping cleared.
- Prompt templates carry the guardrails (max_tokens, stop sequences, "DO NOT greet again", other-agent identity injection).

---

## T12 — AgentRememberConversationStub (~0.2d, depends on T6)

**Files:**
- `Scripts/Runtime/AgentRememberConversationStub.cs`

**Acceptance:** On invocation, appends `{pairKey, transcript, endedAt}` to `MemoryStash`; tests verify.

---

## T13 — AITavernBubbleUI + ChatUIPanel routing + auto-dismiss queue (~0.7d, depends on T5)

**Files:**
- `Scripts/Runtime/AITavernBubbleUI.cs`

**Acceptance:**
- INV: §4.7 queue + auto-dismiss after `NPC_BUBBLE_AUTO_DISMISS_MS` (6s) for non-player turns; player turns block via `StoryEngine.BlockPlayerControl`.
- Play-mode test (Layer B): two queued messages render sequentially with correct timing.

---

## T14 — AITavernPlayerSpeakPanel (new UI prefab + Jyx2_UIBase script + IsTyping lock) (~1.0d, depends on T5)

**Files:**
- `Scripts/Runtime/AITavernPlayerSpeakPanel.cs`
- `Prefabs/AITavernPlayerSpeakPanel.prefab` (new Unity asset — likely requires editor handoff)

**Acceptance:**
- INV-3.10-6: Show acquires IsTyping; refuses if held by other; submit clears via AddMessage; cancel releases.
- Registered in `Jyx2_UIManager`.

---

## T15 — AITavernBoot + AgentSimulator (two-cadence tick) (~0.8d, depends on T4–T13)

**Files:**
- `Scripts/Runtime/AITavernBoot.cs` (scene entry MonoBehaviour)
- `Scripts/Runtime/AgentSimulator.cs`

**Acceptance:**
- Scene boots, awaits `RuntimeEnvSetup.Setup()` + `LevelMaster.Instance.GetPlayer() != null`, spawns NPCs at markers, registers in `NPCRegistry`, kicks off ticking.
- Conversation.Tick every frame; AgentDecision.Tick every 500ms (via `IClock`, not `Time.unscaledTime` in test mode).

---

## T16 — Bio assets + RelationshipGraph + interest seeding (~0.3d, depends on T6 + Phase 0 design)

**Files:**
- `Configs/Bio_HuangRong.asset` (per §11.2)
- `Configs/Bio_OuyangKe.asset` (per §11.2)
- Code in `AITavernBoot.Start` to populate `AITavernManager.Relations` and `Bio.InterestIn` per §11.3, §11.4.

**Acceptance:** Loading the scene initializes both bios; `AITavernManager.Relations.GetRelation(huangrong, ouyangke) == Enemy`; etc.

---

## T17 — Layer A unit test suite (~1.5d, depends on T5–T12)

The 24 `AgentDecision` branch tests + Conversation FSM + ParticipatedTogether + interest selection + IsTyping + one-op + RelationshipGraph + edge cases. See §9.1 plan checklist.

**Acceptance:** all tests PASS in `Unity -batchmode -runTests -testPlatform editmode`.

---

## T18 — End-to-end smoke (~1.0d, depends on T15–T17)

Manual editor smoke check: launch mod, observe NPCs converse, walk player in, exchange a turn, verify cooldown holds.

---

## Dispatch order

```
T1 ─┬─ T2 (scene)
    ├─ T4 (manager)
    ├─ T5 (data POCOs)  ─┬─ T6 (supporting data) ─┬─ T7  ─┬─ T9 (decision) ─┬─ T10 (doSomething)
    │                    │                         │      │                  ├─ T11 (grok+prompts)
    │                    │                         │      └─ T12 (remember stub)
    │                    │                         │
    │                    │                         ├─ T8 (conv tick)
    │                    │                         ├─ T13 (bubble UI)
    │                    │                         └─ T14 (speak panel) ──── T15 (boot + sim) ── T17 (Layer A) ── T18 (smoke)
    │                    └─ T16 (bio assets) ─────────────────────────────────┘
    └─ T3 (test infra)  ────────────────────────────────────────────────┘
```

Parallelizable: T1‖T3; T2‖T4‖T5; T6‖T7; T8‖T13‖T14 (after T5); T10‖T11‖T12 (after T9).

Sequential gates: T9 (decision tree) blocks the agent-op layer; T15 (boot) blocks the smoke test.

---

## Coder/reviewer/tester dispatch protocol (per task)

For each task:
1. **Coder agent** invoked with: task spec verbatim + relevant invariant ids + file paths + plan v7 + Phase 0 doc reference. Returns diff summary.
2. **Reviewer agent** invoked with: diff + `aitavern_invariants.md` + task acceptance criteria. Returns APPROVE / REQUEST CHANGES.
3. On APPROVE: **Tester agent** invoked to run Layer A tests touching the changed files. Returns PASS / FAIL / BLOCKED.
4. On any FAIL: loop back to Coder with combined feedback.
5. Cap: 3 review rounds per task. Escalate to master if reviewer + coder disagree.
6. On PASS: mark task `done` in this file; advance to next.

Tasks requiring binary asset creation (`.asset`, `.unity`, `.prefab`, `.asmdef` initial scaffolding) defer Unity-editor steps to user smoke handoff per §10.2.1.
