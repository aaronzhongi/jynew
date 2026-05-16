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

        // T3D.5 — the coexistence flip (Plan §6.1 / §8). The ContextAssembler
        // §1-§5 block (INCLUDING the §5.5.3 episodic ring) is now the SOLE
        // transcript + memory context source. The Phase 2 prior-memory block
        // and the live-transcript AppendTranscript are REMOVED from the
        // builders (the §5.5.2 ReflectionSummary + §5.5.3 ring supersede the
        // Phase 2 "你与 X 的过往" recap). The assembler block is still
        // PREPENDED ahead of the Phase 2 identity block; only PrependAssembler
        // + the Chinese anti-repeat tail remain alongside it.
        //
        // The Agent/mgr/now params feed the assembler; the CharacterBio
        // self/other params still drive the Phase 2 identity block.
        // `talker`/`talkee` may be null (tests / missing-agent paths) — the
        // assembler returns "" in that case and only the identity block emits.
        static void PrependAssembler(StringBuilder sp, Agent talker, Agent talkee,
            AITavernManager mgr, long now, ContextProfile profile)
        {
            string staticBlock = ContextAssembler.Build(talker, talkee, mgr, now, profile);
            if (string.IsNullOrWhiteSpace(staticBlock)) return;
            // Splice the static block at the FRONT, followed by a separator
            // newline, then the already-built Phase 2 content.
            string phase2 = sp.ToString();
            sp.Clear();
            sp.Append(staticBlock);
            if (!staticBlock.EndsWith("\n")) sp.Append('\n');
            sp.Append('\n');
            sp.Append(phase2);
        }

        public static Built BuildStart(CharacterBio self, CharacterBio other,
            Agent talker = null, Agent talkee = null, AITavernManager mgr = null, long now = 0L,
            string seedHook = null)
        {
            var sp = BuildIdentityBlock(self, other);
            // T3D.5: Phase 2 AppendPriorMemoryBlock removed — the assembler's
            // §5.5.2 ReflectionSummary + §5.5.3 ring now carry prior memory.
            if (!string.IsNullOrEmpty(seedHook))
                sp.Append("Possible topics: ").Append(seedHook).Append('\n');
            sp.Append("This is the beginning of your conversation. Stay in character. "
                + "Reply in 1-3 sentences, under 200 Chinese characters. "
                + "Do not narrate actions — only speak.\n"
                // Phase 2 (Plan §4.1): anti-repeat guard sits adjacent to the
                // generation instruction so it survives recency bias when the
                // priorMemory block carries N>2 transcripts.
                + "不要复述上面已有的对话内容；如无新话题可谈，简短礼貌告辞即可。");
            // T3D.5: prepend the §1-§5 assembler block (Full profile). It is
            // now the sole context source (no Phase 2 priorMemory alongside).
            PrependAssembler(sp, talker, talkee, mgr, now, ContextProfile.Full);
            return new Built
            {
                SystemPrompt = sp.ToString(),
                Messages = new List<(string, string)>(),
            };
        }

        public static Built BuildContinue(CharacterBio self, CharacterBio other, Conversation conv,
            Agent talker = null, Agent talkee = null, AITavernManager mgr = null, long now = 0L)
        {
            var sp = BuildIdentityBlock(self, other);
            // T3D.5 — the coexistence flip (Plan §6.1 / §8): the Phase 2
            // AppendPriorMemoryBlock AND the live-transcript AppendTranscript
            // ("\nConversation so far:\n" + AppendTranscript(conv)) are BOTH
            // DELETED here. The live exchange reaches the model via the
            // assembler's §5.5.3 ring (PerTargetBlock) — NOT a second
            // transcript path → no double-render; and because ring-append is
            // co-located with Conversation.AddMessage (T3D.2, EpisodicRing
            // .Record fired in the same synchronous step right after
            // conv.AddMessage), the just-added in-flight turn is ALREADY in
            // the ring when BuildContinue runs → no under-render. `conv` is
            // still threaded for signature stability / future use.
            sp.Append("\nIt is now your turn. Reply in 1-3 sentences, under 200 Chinese characters. "
                + "DO NOT greet again. DO NOT repeat what you just said. Stay in character.\n"
                // Phase 2 (Plan §4.1): anti-repeat guard for prior-history
                // content (the existing English line above only covers the
                // immediately-preceding message, not the broader transcript).
                + "不要复述上面已有的对话内容；如无新话题可谈，简短礼貌告辞即可。");
            // T3D.5: prepend the §1-§5 assembler block (Full profile),
            // INCLUDING the §5.5.3 ring — now the sole transcript source
            // (the deleted AppendTranscript above is fully superseded).
            PrependAssembler(sp, talker, talkee, mgr, now, ContextProfile.Full);
            return new Built
            {
                SystemPrompt = sp.ToString(),
                Messages = new List<(string, string)>(),
            };
        }

        public static Built BuildLeave(CharacterBio self, CharacterBio other, Conversation conv,
            Agent talker = null, Agent talkee = null, AITavernManager mgr = null, long now = 0L)
        {
            var sp = BuildIdentityBlock(self, other);
            // T3D.5 — the coexistence flip (Plan §6.1 / §8): the
            // "\nConversation so far:\n" + AppendTranscript(conv) live
            // transcript is DELETED here too. The lean Leave assembler
            // profile emits §2 + §5.4 + the §5.5.3 ring-only block
            // (RingOnlyBlock) — the ring is the sole transcript source for
            // the farewell (co-located ring-append per T3D.2 means the last
            // turn is present → no under-render). `conv` is still threaded.
            sp.Append("\nThis conversation has run long. "
                + "Give a short, in-character farewell (1 sentence, under 50 characters), then stop.");
            // T3D.5: prepend the lean Leave block (Plan §6.1) — §2 + §5.4 +
            // §5.5.3 ring only (§1/§3/§4/§5.0/§5.5.1/§5.5.2 excluded). Keeps
            // a <50-char goodbye from routing ~15k chars while still knowing
            // what was just said (the ring).
            PrependAssembler(sp, talker, talkee, mgr, now, ContextProfile.Leave);
            return new Built
            {
                SystemPrompt = sp.ToString(),
                Messages = new List<(string, string)>(),
            };
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

        // T3D.6 (Plan §5.4 / §6.1 / §8): the DEAD Phase 2 `AppendPriorMemory-
        // Block` and `AppendTranscript` helpers were DELETED here. Both had
        // zero callers after T3D.5 (BuildStart/BuildContinue/BuildLeave stopped
        // calling them — the §5.5.3 ring is now the SOLE transcript source and
        // §5.5.2 ReflectionSummary carries prior memory). Their Phase 2 tests
        // were rewritten to 3D reality in the same task.
    }
}
