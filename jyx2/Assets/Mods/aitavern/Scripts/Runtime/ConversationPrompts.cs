// T11: Prompt builders for the three message generation types (Plan §4.6).
//
// Each builder returns a (system prompt, messages) pair. The system prompt
// carries identity / plans / relationship context plus the per-type
// guardrails:
//   - Start    : open the conversation; no "greet again" rule yet.
//   - Continue : add "DO NOT greet again, DO NOT repeat what you just said".
//   - Leave    : ask for a short in-character farewell, 1 sentence, <50 chars.
//
// All three keep the model on a 1-3 sentence / <200 Chinese character budget
// so we don't blow out the chat bubble UI.
//
// The transcript is rendered inline INTO the system prompt rather than via
// chat-role messages because xAI / OpenAI chat APIs only have user/assistant
// roles — we'd lose author names for multi-party conversations. Phase 1 is
// strictly 2-party, but the in-prompt rendering is forward-compatible with
// N>2 if Phase 5 expands it.

using System.Collections.Generic;
using System.Text;

namespace Jyx2.AITavern
{
    public static class ConversationPrompts
    {
        public struct Built
        {
            public string SystemPrompt;
            public List<(string role, string content)> Messages;
        }

        public static Built BuildStart(CharacterBio self, CharacterBio other, string seedHook = null, string priorMemory = null)
        {
            var sp = BuildIdentityBlock(self, other);
            AppendPriorMemoryBlock(sp, other, priorMemory);
            if (!string.IsNullOrEmpty(seedHook))
                sp.Append("Possible topics: ").Append(seedHook).Append('\n');
            sp.Append("This is the beginning of your conversation. Stay in character. "
                + "Reply in 1-3 sentences, under 200 Chinese characters. "
                + "Do not narrate actions — only speak.\n"
                // Phase 2 (Plan §4.1): anti-repeat guard sits adjacent to the
                // generation instruction so it survives recency bias when the
                // priorMemory block carries N>2 transcripts.
                + "不要复述上面已有的对话内容；如无新话题可谈，简短礼貌告辞即可。");
            return new Built
            {
                SystemPrompt = sp.ToString(),
                Messages = new List<(string, string)>(),
            };
        }

        public static Built BuildContinue(CharacterBio self, CharacterBio other, Conversation conv, string priorMemory = null)
        {
            var sp = BuildIdentityBlock(self, other);
            AppendPriorMemoryBlock(sp, other, priorMemory);
            sp.Append("\nConversation so far:\n");
            AppendTranscript(sp, conv);
            sp.Append("\nIt is now your turn. Reply in 1-3 sentences, under 200 Chinese characters. "
                + "DO NOT greet again. DO NOT repeat what you just said. Stay in character.\n"
                // Phase 2 (Plan §4.1): anti-repeat guard for prior-history
                // content (the existing English line above only covers the
                // immediately-preceding message, not the broader transcript).
                + "不要复述上面已有的对话内容；如无新话题可谈，简短礼貌告辞即可。");
            return new Built
            {
                SystemPrompt = sp.ToString(),
                Messages = new List<(string, string)>(),
            };
        }

        public static Built BuildLeave(CharacterBio self, CharacterBio other, Conversation conv)
        {
            var sp = BuildIdentityBlock(self, other);
            sp.Append("\nConversation so far:\n");
            AppendTranscript(sp, conv);
            sp.Append("\nThis conversation has run long. "
                + "Give a short, in-character farewell (1 sentence, under 50 characters), then stop.");
            return new Built
            {
                SystemPrompt = sp.ToString(),
                Messages = new List<(string, string)>(),
            };
        }

        // Phase 2 (Plan §4): pass-through stitcher. The block is now fully
        // pre-rendered by AgentGenerateMessageOp.BuildPriorMemoryBlock with
        // its own Chinese "你与 <other> 的过往：" framing, summary section,
        // and [刚刚结束的对话] marker. The per-type anti-repeat / wrap-up
        // tail is appended elsewhere (T7 attaches it to BuildStart /
        // BuildContinue). All this helper does now is splice in the
        // already-rendered text and ensure it ends with a newline so the
        // following prompt section starts on a fresh line.
        static void AppendPriorMemoryBlock(StringBuilder sp, CharacterBio other, string priorMemory)
        {
            if (string.IsNullOrWhiteSpace(priorMemory)) return;
            sp.Append(priorMemory);
            if (!priorMemory.EndsWith("\n")) sp.Append('\n');
        }

        // ---- Shared identity / relationship block ----

        static StringBuilder BuildIdentityBlock(CharacterBio self, CharacterBio other)
        {
            var sp = new StringBuilder();
            // Self identity.
            sp.Append("You are ").Append(self.BioName).Append(". ").Append(self.Identity).Append('\n');
            // Other identity injection (Plan §4.6 guardrail).
            sp.Append("You are talking with ").Append(other.BioName)
                .Append(". About ").Append(other.BioName).Append(": ")
                .Append(other.Identity).Append('\n');
            // Plans line is optional — the bio may leave it blank.
            if (!string.IsNullOrEmpty(self.Plans))
                sp.Append("Your current plans: ").Append(self.Plans).Append('\n');
            // Relationship line is suppressed when Neutral to keep the prompt tight.
            var rel = self.RelationTo(other.AgentId);
            if (rel != RelationType.Neutral)
                sp.Append("Your view of ").Append(other.BioName).Append(": ").Append(rel).Append('\n');
            return sp;
        }

        static void AppendTranscript(StringBuilder sp, Conversation conv)
        {
            if (conv == null || conv.Transcript == null) return;
            foreach (var msg in conv.Transcript)
            {
                var author = msg.Author.Value ?? "?";
                sp.Append(author).Append(": ").Append(msg.Text).Append('\n');
            }
        }
    }
}
