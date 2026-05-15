using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Jyx2.AITavern.Tests
{
    public class MockGrokClient : IGrokClient
    {
        // Substring-match canned responses keyed by transcript-tail (last 80 chars of system+transcript).
        public readonly Dictionary<string, string> Canned = new Dictionary<string, string>();

        // Per-character default fallback line.
        public readonly Dictionary<string, string> DefaultPerCharacter = new Dictionary<string, string>();

        // Phase 2 (T8): queue of distinct responses returned in FIFO order. If
        // non-empty, takes priority over Canned / DefaultPerCharacter / FinalFallback.
        // Tests can enqueue multiple summaries to verify behavior across
        // back-to-back compaction calls. Falls back to the standard match
        // chain once empty.
        public readonly Queue<string> Responses = new Queue<string>();

        // Fallback if no key matches.
        public string FinalFallback = "[mock] 嗯。";

        // Phase 2 (T8): when true, CompleteChatAsync returns a faulted Task with
        // a System.InvalidOperationException. Used by RememberConversationOpTests
        // to verify cleanup semantics on the failure path.
        public bool ThrowOnCall = false;

        public int CallCount { get; private set; }
        public List<string> CallLog { get; } = new List<string>();

        // Phase 2 (T8): full per-call capture so tests can assert what was
        // actually passed to Grok. CompletionCallCount mirrors CallCount but
        // is named per the T8 spec; both are updated together for clarity.
        public int CompletionCallCount { get; private set; }
        public string LastSystemPrompt { get; private set; }
        // Concatenated content of every user-role message in the last call,
        // joined by "\n" so tests can substring-search the body for raw
        // transcript text (vs the previous summary text).
        public string LastUserContent { get; private set; }

        public Task<string> CompleteChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens = 200,
            string[] stopSequences = null,
            double temperature = 0.85)
        {
            CallCount++;
            CompletionCallCount++;
            LastSystemPrompt = systemPrompt;
            LastUserContent = messages != null
                ? string.Join("\n", messages.Where(m => m.role == "user").Select(m => m.content))
                : string.Empty;

            if (ThrowOnCall)
            {
                var ex = new System.InvalidOperationException("[MockGrokClient] forced failure");
                var tcs = new TaskCompletionSource<string>();
                tcs.SetException(ex);
                return tcs.Task;
            }

            var tail = (systemPrompt + " || " + string.Join(" / ", messages.Select(m => m.content)));
            tail = tail.Substring(System.Math.Max(0, tail.Length - 80));
            CallLog.Add(tail);

            // Queued responses win — used by tests that need a different
            // canned summary on each successive compaction call.
            if (Responses.Count > 0)
            {
                return Task.FromResult(Responses.Dequeue());
            }

            foreach (var kv in Canned)
            {
                if (tail.Contains(kv.Key)) return Task.FromResult(kv.Value);
            }
            // Default per-character if system prompt mentions the character
            foreach (var kv in DefaultPerCharacter)
            {
                if (systemPrompt != null && systemPrompt.Contains(kv.Key)) return Task.FromResult(kv.Value);
            }
            return Task.FromResult(FinalFallback);
        }
    }
}
