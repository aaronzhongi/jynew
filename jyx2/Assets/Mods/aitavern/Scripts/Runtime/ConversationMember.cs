// T5: Per-participant conversation FSM state. The (Invited, Status,
// ParticipatingStartedAt) triple is what INV-3.4 and INV-3.5 read.
namespace Jyx2.AITavern
{
    public enum MemberStatusKind
    {
        Invited,
        WalkingOver,
        Participating,
    }

    public class ConversationMember
    {
        // ms epoch when this member was added (or, for the creator, when the
        // conversation was created). Drives INV-3.4-8 (INVITE_TIMEOUT_MS).
        public long Invited;

        public MemberStatusKind Status;

        // ms epoch when this member transitioned to Participating. Non-null
        // only while Status == Participating. Drives the awkward-deadline and
        // MAX_CONVERSATION_DURATION_MS gates in INV-3.4-12/13.
        public long? ParticipatingStartedAt;
    }
}
