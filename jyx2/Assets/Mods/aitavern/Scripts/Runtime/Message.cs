// T5: Single transcript entry. MessageUuid threads back to the IsTyping lock
// that authorized this message (INV-3.10-6 / typing-lock matching).
namespace Jyx2.AITavern
{
    public class Message
    {
        public GameId Author;
        public long Timestamp;        // ms epoch
        public string Text;
        public string MessageUuid;    // matches the IsTypingLock that authorized this message
    }
}
