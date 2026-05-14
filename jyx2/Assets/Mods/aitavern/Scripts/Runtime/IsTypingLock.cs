// T5: Per-conversation typing lock. Held by exactly one player at a time.
// Acquired by SetIsTyping (INV-3.10-6); cleared by AddMessage / ClearIsTyping
// or by Conversation.Tick after TYPING_TIMEOUT_MS (INV-3.5-1).
namespace Jyx2.AITavern
{
    public class IsTypingLock
    {
        public GameId PlayerId;
        public string MessageUuid;
        public long Since;            // ms epoch
    }
}
