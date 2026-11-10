using System.Diagnostics;
using System.Text.Json.Nodes;
using N17YuHelper.Llm;

namespace N17YuHelper.Translate;

/// <summary>Перевод текста локальной моделью (Gemma 4 12B) через llama-server.</summary>
public sealed class TranslationService(LlamaServer server, Func<AppSettings> settings)
{
    public LlamaServer Server { get; } = server;

    /// <summary>Язык перевода по умолчанию: последний выбранный вручную, иначе правило из настроек.</summary>
    public Language DefaultTarget(Language source)
    {
        var t = settings().Translation;
        if (t.LastTargets.TryGetValue(source.Code, out var last) && Languages.ByCode(last) is { } l) return l;
        if (t.DefaultTargets.TryGetValue(source.Code, out var def) && Languages.ByCode(def) is { } d) return d;
        return source == Languages.English ? Languages.Russian : Languages.English;
    }

    /// <summary>Запоминает выбранный вручную язык перевода.</summary>
    public void RememberTarget(Language source, Language target)
    {
        var s = settings();
        if (s.Translation.LastTargets.TryGetValue(source.Code, out var old) && old == target.Code) return;
        s.Translation.LastTargets[source.Code] = target.Code;
        s.Save();
    }

    /// <summary>Текст длиннее этого переводится по частям (абзацами).</summary>
    public const int ChunkChars = 2000;

    public async Task<string> TranslateAsync(string text, Language source, Language target,
        Action<string>? onDelta, IProgress<string>? progress, CancellationToken ct)
    {
        var s = settings().TextModel;
        var sw = Stopwatch.StartNew();

        if (Server.State != ServerState.Ready)
            progress?.Report("Loading translation model...");
        await Server.EnsureReadyAsync(ct);

        var chunks = text.Length > ChunkChars ? TextChunker.Split(text, ChunkChars) : [new TextChunk(text, "")];
        var done = new List<string>(chunks.Count);
        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = chunks[i];
            progress?.Report(chunks.Count == 1
                ? $"Translating {source.Name} → {target.Name}..."
                : $"Translating {source.Name} → {target.Name}: part {i + 1} of {chunks.Count}...");

            // Пустые куски (только пробелы) не переводим.
            if (string.IsNullOrWhiteSpace(chunk.Text))
            {
                done.Add(chunk.Text);
            }
            else
            {
                // Пробелы в начале куска (отступы) сохраняем как были.
                string lead = chunk.Text[..(chunk.Text.Length - chunk.Text.TrimStart().Length)];
                if (lead.Length > 0) onDelta?.Invoke(lead);
                var glossary = GlossaryFor(chunk.Text, settings().Translation.Glossary);
                string system = SystemPrompt(source, target) + glossary;
                if (chunks.Count > 1)
                    system += $"\nThis is part {i + 1} of {chunks.Count} of a longer document. Translate only this part.";
                var messages = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = system },
                    new JsonObject { ["role"] = "user", ["content"] = chunk.Text },
                };
                int maxTokens = Math.Max(s.MaxTokens, chunk.Text.Length);
                var part = await ChatClient.StreamAsync(Server, messages, s.Temperature, maxTokens, onDelta, ct);
                done.Add(lead + part.Trim());
            }
            if (i < chunks.Count - 1 && chunk.Separator.Length > 0) onDelta?.Invoke(chunk.Separator);
        }

        var result = chunks.Count == 1 ? done[0].Trim() : TextChunker.Join(chunks, done).TrimEnd();
        Log.Info($"Translate {source.Code}->{target.Code}: {text.Length} -> {result.Length} chars, {chunks.Count} part(s), {sw.Elapsed.TotalSeconds:0.00} s");
        return result;
    }

    /// <summary>
    /// Переводит список абзацев документа за один запрос (ответ — JSON того же размера).
    /// Теги &lt;1&gt;…&lt;/1&gt; отмечают части с разным оформлением и должны остаться на месте.
    /// </summary>
    public async Task<IReadOnlyList<string>> TranslateBatchAsync(IReadOnlyList<string> items, Language source, Language target, CancellationToken ct)
    {
        var s = settings().TextModel;
        await Server.EnsureReadyAsync(ct);
        string joined = string.Join("\n", items);
        string system = SystemPrompt(source, target) + GlossaryFor(joined, settings().Translation.Glossary) + $$"""

            The user sends a JSON array of {{items.Count}} paragraphs from a document.
            Answer with JSON {"t": [...]} — an array of exactly {{items.Count}} strings: the translation of each paragraph at the same position.
            Never merge, split, skip or reorder paragraphs.
            Some paragraphs contain numbered tags like <1>...</1><2>...</2> that mark differently formatted parts (bold, links).
            Keep every tag and put the translation of each part inside its own tag. Words between tags stay inside tags.
            """;
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = system },
            new JsonObject { ["role"] = "user", ["content"] = System.Text.Json.JsonSerializer.Serialize(items, JsonOptions) },
        };
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["t"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["minItems"] = items.Count,
                    ["maxItems"] = items.Count,
                },
            },
            ["required"] = new JsonArray("t"),
        };
        var extra = new JsonObject
        {
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "translation", ["schema"] = schema },
            },
        };
        int maxTokens = Math.Max(s.MaxTokens, joined.Length * 2 + 200);
        var json = await ChatClient.StreamAsync(Server, messages, s.Temperature, maxTokens, null, ct, extra);
        try
        {
            var arr = JsonNode.Parse(json)?["t"]?.AsArray();
            if (arr is not null && arr.Count == items.Count)
                return arr.Select(x => x?.GetValue<string>() ?? "").ToList();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            Log.Info("Batch translation: bad JSON, translating one by one: " + ex.Message);
        }

        // Запасной путь: по одному абзацу.
        var list = new List<string>(items.Count);
        foreach (var item in items)
            list.Add(await TranslateAsync(item, source, target, null, null, ct));
        return list;
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Термины из словаря, которые встречаются в тексте (в любую сторону):
    /// «rig => риг» сработает и для «rig» в английском, и для «риг» в русском тексте.
    /// </summary>
    public static string GlossaryFor(string text, IEnumerable<string> lines)
    {
        var used = new List<string>();
        foreach (var raw in lines)
        {
            var parts = raw.Split("=>", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0) continue;
            string a = parts[0], b = parts[1];
            bool keep = b is "" or "=";
            if (ContainsTerm(text, a))
                used.Add(keep ? $"- \"{a}\": keep as is, do not translate" : $"- \"{a}\" → \"{b}\"");
            else if (!keep && ContainsTerm(text, b))
                used.Add($"- \"{b}\" → \"{a}\"");
        }
        return used.Count == 0 ? "" : "\nGlossary (always use these translations):\n" + string.Join("\n", used);
    }

    private static bool ContainsTerm(string text, string term) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(term)}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string SystemPrompt(Language source, Language target) =>
        $"""
        You are a professional translator. Translate the user's text from {source.PromptName} into {target.PromptName}.
        Rules:
        - Output only the translation. No explanations, notes, quotes, headings or alternatives.
        - Keep the meaning, tone and formatting: line breaks, lists, punctuation style, numbers, URLs, file paths, code and placeholders stay as they are.
        - Do not translate program names, commands, code identifiers or keyboard shortcuts.
        - The text may come from a screenshot of a user interface: translate menu items and buttons the way localized software usually names them.
        - If part of the text is already in {target.PromptName}, keep that part unchanged.
        """;
}
