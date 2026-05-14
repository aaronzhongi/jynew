// T5: Process-level registry of active conversations. Lives on AITavernManager
// (T4). Kept deliberately small — list-scan is fine for a 2-NPC tavern; we'll
// revisit if Phase 5 spawns many parallel conversations.
using System.Collections.Generic;

namespace Jyx2.AITavern
{
    public class ConversationTable
    {
        private readonly List<Conversation> _active = new List<Conversation>();

        public IReadOnlyList<Conversation> Active => _active;

        public void Add(Conversation c) { _active.Add(c); }
        public void Remove(Conversation c) { _active.Remove(c); }

        // INV-3.9-1: Conversation.Start consults this to refuse overlapping
        // membership. Both NPC-NPC and player-NPC starts go through here.
        public bool IsInActiveConversation(GameId player)
        {
            foreach (var c in _active)
            {
                if (c.Participants.ContainsKey(player)) return true;
            }
            return false;
        }

        public Conversation GetConversationOf(GameId player)
        {
            foreach (var c in _active)
            {
                if (c.Participants.ContainsKey(player)) return c;
            }
            return null;
        }
    }
}
