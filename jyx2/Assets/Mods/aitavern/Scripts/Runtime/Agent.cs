// T5: Agent POCO — per Phase 1 Plan §3.1 and INV-3.4 / INV-3.10.
// NOTE: T5 originally omitted `Bio`; T10 (AgentDoSomethingOp) needs it for
// interest-weighted candidate selection per INV-3.6-5, so it is added here as
// a one-line addition alongside `Body`. T16 will wire bios onto agents at
// registration time.
namespace Jyx2.AITavern
{
    public class Agent
    {
        public GameId AgentId;
        public GameId PlayerId;

        // Pending conversation id to summarize (Phase 2 hook). When set,
        // AgentDecision.Tick schedules agentRememberConversation and clears.
        public System.Nullable<System.Guid> ToRemember;

        // Partner agent id paired with ToRemember; set by Conversation.StopWithManager
        // so AgentRememberConversationOp can find them without scanning MemoryStash.
        public GameId? ToRememberPartner;

        // ms epoch; written by Conversation.Stop / Conversation.Leave.
        // Drives `justLeftConversation` per INV-3.6-1.
        public long? LastConversation;

        // ms epoch; written each time the agent attempts to start a conversation.
        // Drives `recentlyAttemptedInvite` per INV-3.4-5.
        public long? LastInviteAttempt;

        // Set synchronously before any await; cleared in continuation. INV-3.8-*.
        public InProgressOperation Operation;

        // INV-3.10-1: AgentDecision.Tick skips ticking when IsHuman is true.
        public bool IsHuman;

        // Body reference is forward-declared (INPCBody) to break the dependency
        // cycle with T7. NPCRegistry / AgentSimulator wire this on registration.
        public INPCBody Body;

        // T10: persona reference used by AgentDoSomethingOp for interest scoring
        // (INV-3.6-5). May be null if the agent was registered before T16 wires
        // bios — AgentDoSomethingOp falls back to no-candidate on null.
        public CharacterBio Bio;
    }
}
