# AITavern Algorithm Invariants

Static checklist extracted from `AITavern_Phase1_Plan.md` §§3.4, 3.6, 3.8, 3.9, 3.10, 8.
Used by Reviewer agents in the implementation orchestration (§10.2) to keep reviews deterministic across tasks.

Each item has a stable id. Acceptance criteria in `docs/phase{N}_tasks.md` reference these ids.

---

## INV-3.4: Agent.Tick decision tree

- **INV-3.4-1**: If `Operation != null` and `now < Operation.StartedAt + ACTION_TIMEOUT_MS` → return immediately, no new op.
- **INV-3.4-2**: If `Operation != null` and timed out → clear `Operation`, continue tick.
- **INV-3.4-3**: If `Activity.Until > now` AND (in conversation OR pathfinding) → set `Activity.Until = now`.
- **INV-3.4-4**: `agentDoSomething` is scheduled iff `!conversation && !doingActivity && (!Pathfinding || !recentlyAttemptedInvite)`.
- **INV-3.4-5**: `recentlyAttemptedInvite = LastInviteAttempt != null && now < LastInviteAttempt + CONVERSATION_COOLDOWN_MS`.
- **INV-3.4-6**: If `ToRemember != null` → schedule `agentRememberConversation`, clear `ToRemember`, return.
- **INV-3.4-7**: Conversation `Invited` branch: accept if `otherAgent.IsHuman` (always) OR `rng.NextDouble() < INVITE_ACCEPT_PROBABILITY`; else reject. On accept clear `Pathfinding`. `otherAgent.IsHuman` comes from `NPCRegistry.GetAgent(otherPlayerId).IsHuman`.
- **INV-3.4-8**: Conversation `WalkingOver`: if `member.Invited + INVITE_TIMEOUT_MS < now` → leave conversation.
- **INV-3.4-9**: Conversation `WalkingOver`: if `distance(self, other) < CONVERSATION_DISTANCE` → return (transition handled by `Conversation.Tick`).
- **INV-3.4-10**: Conversation `WalkingOver`, not pathfinding: destination = other if `distance < MIDPOINT_THRESHOLD`, else midpoint.
- **INV-3.4-11**: Conversation `Participating`, `IsTyping != null && IsTyping.PlayerId != self` → return (typing lock yield).
- **INV-3.4-12**: Conversation `Participating`, `!LastMessage`, `isInitiator || awkwardDeadline < now` → grab typing lock, schedule `agentGenerateMessage(start)`.
- **INV-3.4-13**: Conversation `Participating`, `participating.StartedAt + MAX_CONVERSATION_DURATION_MS < now || NumMessages > MAX_CONVERSATION_MESSAGES` → grab typing lock, schedule `agentGenerateMessage(leave)`.
- **INV-3.4-14**: Conversation `Participating`, `LastMessage.Author == self && now < LastMessage.Timestamp + AWKWARD_CONVERSATION_TIMEOUT` → return.
- **INV-3.4-15**: Conversation `Participating`, `now < LastMessage.Timestamp + MESSAGE_COOLDOWN` → return.
- **INV-3.4-16**: Conversation `Participating`, all gates clear → grab typing lock, schedule `agentGenerateMessage(continue)`.
- **INV-3.4-17**: `isInitiator = Conversation.CreatorPlayerId == self`.

## INV-3.5: Conversation.Tick (per-frame cadence)

- **INV-3.5-1**: If `IsTyping != null && now > IsTyping.Since + TYPING_TIMEOUT_MS` → clear `IsTyping`.
- **INV-3.5-2**: If both participants in `WalkingOver` AND `distance(p1, p2) < CONVERSATION_DISTANCE` → stop both pathfinding, transition both to `Participating(StartedAt = now)`.

## INV-3.6: agentDoSomething + interest-weighted candidate selection

- **INV-3.6-1**: `justLeftConversation = Agent.LastConversation != null && now < Agent.LastConversation + CONVERSATION_COOLDOWN_MS`. Does NOT abort the op.
- **INV-3.6-2**: `justLeftConversation` biases the not-pathfinding branch to wander over activity.
- **INV-3.6-3**: `justLeftConversation || recentlyAttemptedInvite` → candidate selection returns `null` (no invite this op).
- **INV-3.6-4**: Candidate pool = `otherFreePlayers` (excludes anyone in active conversation) further filtered by `!ParticipatedTogether.IsCoolingDown(self, candidate, now)`.
- **INV-3.6-5**: Score = `interest × proximity × recencyDamp`. Interest from `CharacterBio.InterestIn`, default 0.3 ambient. Proximity = `1 - clamp(distance / SCENE_RADIUS, 0, 1)`. RecencyDamp = 0.5 if last pair partner within `2 * CONVERSATION_COOLDOWN_MS`, else 1.0.
- **INV-3.6-6**: Score floor `MIN_CANDIDATE_SCORE = 0.05` — candidates below floor are dropped. Tunable constant in `AITavernConstants`.
- **INV-3.6-7**: Empty candidate set returns `null` (no throw). Matches ai-town `candidates[0]?.id` → undefined.
- **INV-3.6-8**: Winner picked by weighted random sampling using RNG seeded from `AITavernConstants.RandomSeed`. NOT argmax.
- **INV-3.6-9**: Activity arm of `agentDoSomething` is a no-op `idle` in Phase 1. Branch structure preserved.
- **INV-3.6-10**: `agentDoSomething` always runs to completion and writes state via `finishDoSomething` — never short-circuits the op itself.

## INV-3.8: One-operation invariant

- **INV-3.8-1**: `agent.Operation = new InProgressOperation(...)` is written SYNCHRONOUSLY before any await/HTTP call.
- **INV-3.8-2**: Operation cleared on success AND error via `.ContinueWith(_ => agent.Operation = null)`.
- **INV-3.8-3**: Two consecutive ticks during a 5-10s LLM call see `Operation != null` and bail (per INV-3.4-1).
- **INV-3.8-4**: All operation state mutation runs on Unity main thread; only HTTP await is off-main.

## INV-3.9: Conversation.Start invariants

- **INV-3.9-1**: Rejects (returns null) if either participant is already in any active conversation.
- **INV-3.9-2**: Creator (initiator) starts in `WalkingOver`. Invitee starts in `Invited`. NOT both in WalkingOver.
- **INV-3.9-3**: `member.Invited = now` is set for both at creation.
- **INV-3.9-4**: Transition `WalkingOver → Participating` is per-tick proximity check in `Conversation.Tick`, never in `Agent.Tick`.

## INV-3.10: Player participation

- **INV-3.10-1**: Player is in `NPCRegistry` with `IsHuman = true`. `AgentDecision.Tick` skips human agents.
- **INV-3.10-2**: Player-initiated `StartPlayerInitiated`: player = `WalkingOver` (creator), NPC = `Invited`. NPC's next tick always-accepts (INV-3.4-7 + `otherAgent.IsHuman`).
- **INV-3.10-3**: NPC-initiated invite of player: `AITavernBubbleUI.Update` checks proximity + outstanding `Invited` membership → calls `Conversation.AcceptInvite(playerAgent)` (transitions Invited → WalkingOver). The transition to Participating is the per-tick proximity check.
- **INV-3.10-4**: Player walks themselves via `Jyx2_PlayerInput`. NPC counterpart closes distance via its WalkingOver branch (INV-3.4-10).
- **INV-3.10-5**: When player is `Participating` creator with `!LastMessage` and `!isInitiator-for-NPC`: NPC waits `AWKWARD_CONVERSATION_TIMEOUT` before breaking silence. Player can preempt by typing.
- **INV-3.10-6**: `AITavernPlayerSpeakPanel.Show` acquires `IsTyping` via `Conversation.SetIsTyping(playerAgent, uuid, now)` on open. Refuses to open if another player holds the lock. Submit clears via `AddMessage`. Cancel calls `ClearIsTyping()`.

## INV-8: Phase 3 combat/death/drop

- **INV-8-1**: Pre-`LoadBattle` snapshot on `AITavernManager` (DontDestroyOnLoad): current map, player pos/rot, every NPC's world pos. On `OnBattleResult`, reload tavern scene via `LevelLoader.LoadGameMap(...)` with `LevelLoadPara.ReturnFromBattle`.
- **INV-8-2**: `AITavernManager` carries `NPCRegistry`, `ConversationTable`, `ParticipatedTogether`, `MemoryStash`, `RelationshipGraph`, `RNG` — all rehydrated on scene reload.
- **INV-8-3**: Hostility scoring: rules-lexicon + Grok JSON sidecar at sentinel `\n###META\n{...}`. Parser splits at sentinel. On parse failure → `hostility = lexiconScore`, `intent = "talk"`, log raw, no throw.
- **INV-8-4**: Hostility accumulation: messages with `hostility ≥ 3` ADD score (no decay); messages with `hostility < 3` apply `HOSTILITY_DECAY_PER_MESSAGE`. Clamped to `[0, ∞)`.
- **INV-8-5**: Combat fires only on the message that **crosses** `HOSTILITY_THRESHOLD` upward (latch). Doesn't re-fire on subsequent hostile messages in same conversation. Relation gate: both NPCs must be `Rival|Enemy`.
- **INV-8-6**: `CsBattleConfig` for combat: `InitForDynamicData()`, empty `TeamMates`/`Enemies` lists, `AutoTeamMates` contains a valid roleId (NOT -1), combatants/joiners in `DynamicTeammate`/`DynamicEnemies`.
- **INV-8-7**: Faction-ally precedence: `Ally > Friend > Enemy > Neutral`. `Ally→side X` joins X at p=1.0; `Friend→side X` joins X at p=0.5; `Enemy→side X` joins opposite at p=0.7. Conflicting same-precedence relations → Bystander.
- **INV-8-8**: Ally-join RNG seeded `AITavernConstants.RandomSeed XOR conversationId.GetHashCode()`.
- **INV-8-9**: Player auto-joins partner's side if in trigger conversation; otherwise prompted only if within `PLAYER_COMBAT_PROMPT_RADIUS = 10 m`; beyond that, combat proceeds without them.
- **INV-8-10**: Death lifecycle order: (1) `Agent.Operation = null` (in-flight Grok callbacks null-check before mutating); (2) `Conversation.Stop(now)` BEFORE de-register so survivors' `LastConversation`/`ToRemember` are set; (3) `NPCRegistry.Remove(agent)`.
- **INV-8-11**: Items dropped per `RoleInstance.Items` (`List<CsRoleItem>` of `{Id, Count}`) plus equipped `Weapon`/`Armor`/`Xiulianwupin` (each `Count=1`). One `WorldDropItem` per item. Pickup adds back as `CsRoleItem`.
- **INV-8-12**: Death memory is `MemoryType.Relationship` (NOT `Conversation`), keyed to survivor, `targetPlayerId = <dead agent>`, `description = "<self> 与 <other> 决斗, <other> 身亡。"`, `importance = 9` (pre-set, skips LLM importance call).
- **INV-8-13**: `WorldDropItem` is a custom MonoBehaviour using `Jyx2PlayerAction.Interact1` Rewired action for PC pickup. NOT a `GameEvent`. NPC pickup via `agentTryPickup` in `agentDoSomething` branching, gated by `Bio.WantsItem(itemId)` + `PICKUP_RADIUS = 2.5 m`.
- **INV-8-14**: Player death: drop inventory, mark `GameRuntimeData.Defeated`, NO game-over flow. Respawn via `Jyx2LuaBridge.DarkScence(callback)` → reposition → `LightScence(callback)`. Tavern scene must retain `Level/UI/BlackCover`.

## INV-Constants (Phase 1+3)

```
TICK_INTERVAL_AGENT_MS        = 500
CONVERSATION_DISTANCE         = 2.0 m       (RETUNE from ai-town 1.3 tiles)
MIDPOINT_THRESHOLD            = 6.0 m       (RETUNE from ai-town 4 tiles)
INVITE_ACCEPT_PROBABILITY     = 0.8
INVITE_TIMEOUT_MS             = 60_000
AWKWARD_CONVERSATION_TIMEOUT  = 20_000      (RETUNE — ai-town prod value)
MESSAGE_COOLDOWN_MS           = 2_000
MAX_CONVERSATION_DURATION_MS  = 120_000     (RETUNE — ai-town prod)
MAX_CONVERSATION_MESSAGES     = 8
CONVERSATION_COOLDOWN_MS      = 15_000      (DUAL-USE: justLeftConversation AND recentlyAttemptedInvite)
PLAYER_CONVERSATION_COOLDOWN  = 60_000      (PER-PAIR via ParticipatedTogether)
ACTION_TIMEOUT_MS             = 120_000
TYPING_TIMEOUT_MS             = 15_000
ACTIVITY_COOLDOWN_MS          = 10_000      (no-op in Phase 1)
GROK_MAX_TOKENS               = 200         (RETUNE from ai-town 300)
NPC_BUBBLE_AUTO_DISMISS_MS    = 6_000
SCENE_RADIUS                  = 20 m        (interest scoring proximity normalization)
MIN_CANDIDATE_SCORE           = 0.05        (interest scoring floor; sub-floor candidates dropped)
RandomSeed                    = 0xA17E
HOSTILITY_THRESHOLD            = 7          (Phase 3)
HOSTILITY_DECAY_PER_MESSAGE    = 1          (Phase 3)
ALLY_JOIN_PROBABILITY          = 1.0        (Phase 3)
FRIEND_JOIN_PROBABILITY        = 0.5        (Phase 3)
ENEMY_JOIN_OPPOSITE_PROBABILITY= 0.7        (Phase 3)
PICKUP_RADIUS                  = 2.5 m      (Phase 3)
PLAYER_COMBAT_PROMPT_RADIUS    = 10 m       (Phase 3)
PLAYER_RESPAWN_FADE_MS         = 3_000      (Phase 3)
```
