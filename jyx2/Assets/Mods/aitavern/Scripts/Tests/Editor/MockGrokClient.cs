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

        // Fallback if no key matches.
        public string FinalFallback = "[mock] 嗯。";

        public int CallCount { get; private set; }
        public List<string> CallLog { get; } = new List<string>();

        public Task<string> CompleteChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens = 200,
            string[] stopSequences = null,
            double temperature = 0.85)
        {
            CallCount++;
            var tail = (systemPrompt + " || " + string.Join(" / ", messages.Select(m => m.content)));
            tail = tail.Substring(System.Math.Max(0, tail.Length - 80));
            CallLog.Add(tail);

            foreach (var kv in Canned)
            {
                if (tail.Contains(kv.Key)) return Task.FromResult(kv.Value);
            }
            // Default per-character if system prompt mentions the character
            foreach (var kv in DefaultPerCharacter)
            {
                if (systemPrompt.Contains(kv.Key)) return Task.FromResult(kv.Value);
            }
            return Task.FromResult(FinalFallback);
        }
    }
}
