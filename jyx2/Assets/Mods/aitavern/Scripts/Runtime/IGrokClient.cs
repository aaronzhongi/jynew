using System.Threading.Tasks;
using System.Collections.Generic;

namespace Jyx2.AITavern
{
    public interface IGrokClient
    {
        // Returns the generated message text. Throws on hard error (timeout, auth fail).
        // Caller should catch and treat thrown errors as "operation failed, retry next tick".
        Task<string> CompleteChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens = 200,
            string[] stopSequences = null,
            double temperature = 0.85);
    }
}
