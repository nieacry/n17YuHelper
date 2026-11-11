using System.Diagnostics;
using System.Text.Json.Nodes;
using N17YuHelper.Llm;
using N17YuHelper.Translate;

namespace N17YuHelper.Grammar;

public sealed record GrammarFix(string From, string To, string Reason);

public sealed record GrammarResult(string Original, string Corrected, IReadOnlyList<GrammarFix> Fixes, Language Language)
{
    public bool HasChanges => !string.Equals(Normalize(Original), Normalize(Corrected), StringComparison.Ordinal);
    private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();
}

/// <summary>
/// Spelling, grammar and punctuation check with a local model.
/// The model's reply is constrained by a JSON schema, so it always parses.
/// </summary>
public sealed class GrammarService(LlamaServer server, Func<AppSettings> settings)
{
    public LlamaServer Server { get; } = server;

    private static readonly JsonObject Schema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["changes"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["from"] = new JsonObject { ["type"] = "string" },
                        ["to"] = new JsonObject { ["type"] = "string" },
                        ["reason"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("from", "to", "reason"),
                },
            },
            ["corrected"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("changes", "corrected"),
    };

    public async Task<GrammarResult> CheckAsync(string text, IProgress<string>? progress, CancellationToken ct)
    {
        var s = settings().TextModel;
        var lang = Languages.Detect(text);
        var sw = Stopwatch.StartNew();

        if (Server.State != ServerState.Ready)
            progress?.Report("Loading language model...");
        await Server.EnsureReadyAsync(ct);
        progress?.Report($"Checking {lang.Name} text...");

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(lang) },
            new JsonObject { ["role"] = "user", ["content"] = text },
        };
        var extra = new JsonObject
        {
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "proofreading", ["schema"] = Schema.DeepClone() },
            },
        };

        int received = 0;
        var json = await ChatClient.StreamAsync(Server, messages, 0.1, s.MaxTokens, piece =>
        {
            received += piece.Length;
            if (received % 200 < piece.Length) progress?.Report($"Checking {lang.Name} text... ({received} chars)");
        }, ct, extra);

        var result = Parse(json, text, lang);
        Log.Info($"Grammar {lang.Code}: {text.Length} chars, {result.Fixes.Count} fixes, changed={result.HasChanges}, {sw.Elapsed.TotalSeconds:0.00} s");
        return result;
    }

    private static GrammarResult Parse(string json, string original, Language lang)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (Exception ex) { throw new InvalidOperationException("The model returned an incomplete answer (the text may be too long): " + ex.Message); }

        var corrected = root?["corrected"]?.GetValue<string>() ?? original;
        var fixes = new List<GrammarFix>();
        if (root?["changes"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var from = item?["from"]?.GetValue<string>() ?? "";
                var to = item?["to"]?.GetValue<string>() ?? "";
                var reason = item?["reason"]?.GetValue<string>() ?? "";
                // Small models sometimes list "unchanged" fragments — skip them.
                if (from.Trim() == to.Trim()) continue;
                fixes.Add(new GrammarFix(from, to, reason));
            }
        }

        // Keep line breaks as in the source text.
        if (original.Contains("\r\n") && !corrected.Contains("\r\n"))
            corrected = corrected.Replace("\n", "\r\n");
        return new GrammarResult(original, corrected, fixes, lang);
    }

    private static string SystemPrompt(Language lang) =>
        $"""
        You are a careful proofreader of {lang.PromptName} text. Find and fix spelling, grammar, punctuation and typing errors in the user's text.
        Pay special attention to: {Hints(lang)}.
        Rules:
        - Fix only real errors. Keep the author's wording, style, tone, formatting and line breaks. Do not rephrase text that is already correct.
        - In "changes", list every fix: "from" is the original fragment, "to" is the corrected fragment, "reason" is a short explanation in English.
        - "corrected" is the whole text with all fixes applied.
        - If there are no errors, return an empty "changes" list and the text unchanged.
        """;

    private static string Hints(Language lang) => lang.Code switch
    {
        "ru" => "agreement of gender, number and case; commas before conjunctions and around participial and adverbial phrases; -тся/-ться; н/нн; не/ни; typos",
        "uk" => "Russianisms (Russian words and constructions instead of Ukrainian ones), confusion of і/и, є/е, ї, the apostrophe and soft sign, the vocative case, typos",
        "zh" => "wrong homophone characters (的/得/地, 在/再, 做/作, 已/以), full-width Chinese punctuation (，。：；？！) instead of ASCII punctuation, typos",
        _ => "articles, verb tenses, subject-verb agreement, prepositions, common typos",
    };
}
