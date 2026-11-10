using System.Diagnostics;
using System.Text.Json.Nodes;
using N17YuHelper.Llm;

namespace N17YuHelper.Translate;

/// <summary>Свои действия: текст + указание пользователя → ответ модели (по мере генерации).</summary>
public sealed class PromptService(LlamaServer server, Func<AppSettings> settings)
{
    public async Task<string> RunAsync(CustomAction action, string text, Action<string>? onDelta, IProgress<string>? progress, CancellationToken ct)
    {
        var s = settings().TextModel;
        var sw = Stopwatch.StartNew();

        if (server.State != ServerState.Ready)
            progress?.Report("Loading language model...");
        await server.EnsureReadyAsync(ct);
        progress?.Report($"{action.Name}...");

        var system =
            $"""
            You are a helpful writing assistant. Apply the instruction to the user's text.
            Instruction: {action.Prompt}
            Rules:
            - Output only the result. No explanations, headings, quotes or alternatives, unless the instruction asks for them.
            - Answer in the same language as the text, unless the instruction says otherwise.
            - Keep line breaks, lists, numbers, names, code and URLs where it makes sense.
            """;
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = system },
            new JsonObject { ["role"] = "user", ["content"] = text },
        };

        var result = (await ChatClient.StreamAsync(server, messages, Math.Max(s.Temperature, 0.5), s.MaxTokens, onDelta, ct)).Trim();
        Log.Info($"Action '{action.Name}': {text.Length} -> {result.Length} chars, {sw.Elapsed.TotalSeconds:0.00} s");
        return result;
    }
}
