using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace N17YuHelper.Llm;

/// <summary>Requests to /v1/chat/completions with the reply streamed as it is generated.</summary>
public static class ChatClient
{
    /// <summary>
    /// Sends messages and calls onDelta for every new piece of text.
    /// Returns the whole reply.
    /// </summary>
    public static async Task<string> StreamAsync(
        LlamaServer server,
        JsonArray messages,
        double temperature,
        int maxTokens,
        Action<string>? onDelta,
        CancellationToken ct,
        JsonObject? extra = null)
    {
        var payload = new JsonObject
        {
            ["model"] = "text",
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["top_p"] = 0.95,
            ["top_k"] = 64,
            ["max_tokens"] = maxTokens,
            ["stream"] = true,
            // "Thinking" mode is not needed: it only slows the reply down.
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
        };
        if (extra is not null)
            foreach (var (key, value) in extra)
                payload[key] = value?.DeepClone();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server.BaseUri, "v1/chat/completions"))
        {
            Content = JsonContent.Create(payload),
        };
        using var resp = await LlamaServer.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Model request failed ({(int)resp.StatusCode}): {(err.Length > 300 ? err[..300] + "..." : err)}");
        }

        var sb = new StringBuilder();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line.AsSpan(5).Trim().ToString();
            if (data == "[DONE]") break;

            JsonNode? json;
            try { json = JsonNode.Parse(data); }
            catch { continue; }

            var delta = json?["choices"]?[0]?["delta"]?["content"];
            if (delta is null) continue;
            var piece = delta.GetValue<string>();
            if (piece.Length == 0) continue;

            sb.Append(piece);
            onDelta?.Invoke(piece);
        }
        server.Touch();
        return sb.ToString();
    }
}
