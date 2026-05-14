// T11: HTTP implementation of IGrokClient (Plan §4.4).
//
// Talks to xAI's OpenAI-compatible /v1/chat/completions endpoint via
// UnityWebRequest (works in builds; HttpClient is fine in editor but
// UnityWebRequest is portable). The body and response are JSON; we
// hand-roll both to avoid pulling in Newtonsoft and because UnityEngine's
// JsonUtility cannot handle tuple lists or nested dynamic shapes.
//
// API key resolution order (LoadApiKey):
//   1. Environment variable XAI_API_KEY
//   2. File at Application.persistentDataPath/aitavern/xai_key.txt
//
// Missing key → CompleteChatAsync throws InvalidOperationException on
// first call. AgentGenerateMessageOp catches and falls back to a stub
// line so the conversation continues to make progress instead of jamming.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Jyx2.AITavern
{
    public class GrokClient : IGrokClient
    {
        const string ENDPOINT = "https://api.x.ai/v1/chat/completions";

        // xAI model id used for Phase 1 NPC chat. Re-verify at integration
        // time via `curl https://api.x.ai/v1/models -H 'Authorization: Bearer $XAI_API_KEY'`
        // before shipping; xAI rotates non-reasoning variants periodically.
        const string MODEL = "grok-4.20-non-reasoning";

        const int TIMEOUT_SEC = 30;

        readonly string _apiKey;

        public GrokClient(string apiKey = null)
        {
            _apiKey = apiKey ?? LoadApiKey();
        }

        static string LoadApiKey()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("XAI_API_KEY");
                if (!string.IsNullOrEmpty(env)) return env;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GrokClient] failed reading XAI_API_KEY env: {e.Message}");
            }

            try
            {
                var path = System.IO.Path.Combine(Application.persistentDataPath, "aitavern", "xai_key.txt");
                if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path).Trim();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GrokClient] failed reading key file: {e.Message}");
            }

            return null;
        }

        public bool HasKey => !string.IsNullOrEmpty(_apiKey);

        public async Task<string> CompleteChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens = 200,
            string[] stopSequences = null,
            double temperature = 0.85)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException(
                    "XAI_API_KEY not set. Provide env var XAI_API_KEY or "
                    + "persistentDataPath/aitavern/xai_key.txt.");

            var bodyJson = BuildBodyJson(systemPrompt, messages, maxTokens, stopSequences, temperature);

            using (var req = new UnityWebRequest(ENDPOINT, "POST"))
            {
                var raw = Encoding.UTF8.GetBytes(bodyJson);
                req.uploadHandler = new UploadHandlerRaw(raw) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = TIMEOUT_SEC;

                await req.SendWebRequest().ToUniTask();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    var msg = $"[GrokClient] HTTP {req.responseCode}: {req.error}";
                    Debug.LogWarning(msg);
                    throw new Exception(msg);
                }

                return ParseChoiceText(req.downloadHandler.text);
            }
        }

        // ---- JSON building (hand-rolled) ----

        static string BuildBodyJson(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens,
            string[] stop,
            double temperature)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"model\":\"").Append(JsonEscape(MODEL)).Append("\",");
            sb.Append("\"messages\":[");

            bool first = true;
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.Append("{\"role\":\"system\",\"content\":\"")
                    .Append(JsonEscape(systemPrompt))
                    .Append("\"}");
                first = false;
            }

            if (messages != null)
            {
                foreach (var pair in messages)
                {
                    if (!first) sb.Append(',');
                    sb.Append("{\"role\":\"").Append(JsonEscape(pair.role))
                        .Append("\",\"content\":\"").Append(JsonEscape(pair.content)).Append("\"}");
                    first = false;
                }
            }

            sb.Append("],");
            sb.Append("\"max_tokens\":").Append(maxTokens).Append(',');
            sb.Append("\"temperature\":")
                .Append(temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (stop != null && stop.Length > 0)
            {
                sb.Append(",\"stop\":[");
                for (int i = 0; i < stop.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(JsonEscape(stop[i])).Append('"');
                }
                sb.Append(']');
            }

            sb.Append('}');
            return sb.ToString();
        }

        static string JsonEscape(string s)
        {
            if (s == null) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ---- Response parsing ----
        //
        // OpenAI-compat shape: { "choices": [ { "message": { "role": "assistant", "content": "..." } } ], ... }
        // We need the FIRST `"content":"..."` after the FIRST `"message"` key. Hand-parsed to avoid
        // JsonUtility's struct-only limitation; a strict tokenizer would be safer but Phase 1's
        // payload shape is fixed.
        static string ParseChoiceText(string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;

            // Anchor on "message" first so we ignore stray "content" keys
            // that might appear earlier (e.g. system_fingerprint). Fall back
            // to the first "content" if no "message" key is present.
            int msgKey = body.IndexOf("\"message\"", StringComparison.Ordinal);
            int searchFrom = msgKey >= 0 ? msgKey : 0;

            int contentKey = body.IndexOf("\"content\"", searchFrom, StringComparison.Ordinal);
            if (contentKey < 0) return string.Empty;

            int colon = body.IndexOf(':', contentKey);
            if (colon < 0) return string.Empty;

            int start = body.IndexOf('"', colon + 1);
            if (start < 0) return string.Empty;

            var sb = new StringBuilder();
            int i = start + 1;
            while (i < body.Length)
            {
                char c = body[i];
                if (c == '\\' && i + 1 < body.Length)
                {
                    char n = body[i + 1];
                    switch (n)
                    {
                        case 'n':  sb.Append('\n'); i += 2; break;
                        case 'r':  sb.Append('\r'); i += 2; break;
                        case 't':  sb.Append('\t'); i += 2; break;
                        case '"':  sb.Append('"');  i += 2; break;
                        case '\\': sb.Append('\\'); i += 2; break;
                        case '/':  sb.Append('/');  i += 2; break;
                        case 'b':  sb.Append('\b'); i += 2; break;
                        case 'f':  sb.Append('\f'); i += 2; break;
                        case 'u':
                            if (i + 5 < body.Length
                                && ushort.TryParse(body.Substring(i + 2, 4),
                                    System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var code))
                            {
                                sb.Append((char)code);
                                i += 6;
                            }
                            else
                            {
                                i += 2; // malformed escape, skip
                            }
                            break;
                        default: sb.Append(n); i += 2; break;
                    }
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}
