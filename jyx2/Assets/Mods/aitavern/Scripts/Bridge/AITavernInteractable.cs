// T7: Player-initiated conversation hook. Attached by AITavernBoot (T15) to
// each NPC GameObject alongside NPCBody.
//
// This file lives OUTSIDE the `AITavern.Runtime` asmdef (the `Bridge/` folder
// has no .asmdef of its own) so it falls into Assembly-CSharp and can reference
// `Jyx2_Input` / `Jyx2PlayerAction` / `RoleHelper.FindPlayer`. The
// `AITavern.Runtime` asmdef sets `references: []` so it cannot see those types
// itself — but `autoReferenced = true` means Assembly-CSharp CAN see runtime
// types, so this direction works.
//
// Per INV-2.6 we poll the existing Rewired `Jyx2PlayerAction.Interact1` action
// (NOT a KeyCode, NOT GameEvent dispatch — GameEventManager only resolves
// EventGraph assets or Lua-file ids, neither of which a runtime-spawned NPC has).
using UnityEngine;
using Jyx2.InputCore;
using Jyx2.AITavern;

namespace Jyx2.AITavern.Bridge
{
    public class AITavernInteractable : MonoBehaviour
    {
        const float INTERACT_RANGE_M = 2f;

        // Wired by AITavernBoot on spawn (T15).
        public GameId AgentId;

        // Set by AITavernBoot once. Receives this NPC's agentId when the player
        // triggers an interact. T13 (AITavernBubbleUI) and T15 wire this to the
        // Conversation.StartPlayerInitiated path. We do NOT invoke that path
        // here — handler decides cooldown / already-in-conversation policy.
        public static System.Action<GameId> OnPlayerInteract;

        // Cache the player transform once; OK to re-resolve if it goes null
        // (e.g. across a scene unload during a battle round-trip in Phase 3).
        Transform _player;

        void Update()
        {
            if (_player == null)
            {
                var p = RoleHelper.FindPlayer();
                if (p == null) return;
                _player = p.transform;
            }

            var d = Vector3.Distance(_player.position, transform.position);
            if (d > INTERACT_RANGE_M) return;

            // Player context guard: only fire when the player is allowed to act
            // (skip during in-progress dialog UI or battle).
            if (!Jyx2_Input.IsPlayerContext) return;
            // INV-2.6: poll the existing Rewired Interact1 action, not KeyCode.
            if (!Jyx2_Input.GetButtonDown(Jyx2PlayerAction.Interact1)) return;

            OnPlayerInteract?.Invoke(AgentId);
        }
    }
}
