using System.Text.RegularExpressions;

namespace N17YuHelper.Translate;

/// <summary>A piece of long text and what followed it (empty lines) — to assemble the translation back.</summary>
public sealed record TextChunk(string Text, string Separator);

/// <summary>
/// Splits long text into pieces for translation: by paragraphs (empty lines), long paragraphs — by lines,
/// and very long lines — by sentences. Separators are kept as they were.
/// </summary>
public static class TextChunker
{
    public static List<TextChunk> Split(string text, int maxChars)
    {
        // Parts: [text, separator, text, separator, ...]
        var parts = Regex.Split(text, @"(\r?\n[ \t]*\r?\n(?:[ \t]*\r?\n)*)");
        var units = new List<(string Text, string Sep)>();
        for (int i = 0; i < parts.Length; i += 2)
        {
            string body = parts[i];
            string sep = i + 1 < parts.Length ? parts[i + 1] : "";
            if (body.Length <= maxChars)
            {
                units.Add((body, sep));
                continue;
            }
            // Long paragraph — by lines, then by sentences.
            var pieces = SplitKeeping(body, @"(\r?\n)", maxChars);
            for (int k = 0; k < pieces.Count; k++)
                units.Add((pieces[k].Text, k == pieces.Count - 1 ? sep : pieces[k].Sep));
        }

        // Merge neighbouring parts while they fit.
        var chunks = new List<TextChunk>();
        string cur = "", curSep = "";
        bool has = false;
        foreach (var (t, sep) in units)
        {
            if (has && cur.Length + curSep.Length + t.Length > maxChars)
            {
                chunks.Add(new TextChunk(cur, curSep));
                cur = ""; curSep = ""; has = false;
            }
            if (has) cur += curSep + t;
            else cur = t;
            curSep = sep;
            has = true;
        }
        if (has) chunks.Add(new TextChunk(cur, curSep));
        return chunks;
    }

    private static List<(string Text, string Sep)> SplitKeeping(string text, string sepPattern, int maxChars)
    {
        var parts = Regex.Split(text, sepPattern);
        var result = new List<(string, string)>();
        for (int i = 0; i < parts.Length; i += 2)
        {
            string body = parts[i];
            string sep = i + 1 < parts.Length ? parts[i + 1] : "";
            if (body.Length <= maxChars)
            {
                result.Add((body, sep));
                continue;
            }
            // Line longer than the limit: cut at the end of a sentence, or at a space as a last resort.
            int pos = 0;
            while (body.Length - pos > maxChars)
            {
                int limit = pos + maxChars;
                int cut = -1;
                for (int j = limit - 1; j > pos + maxChars / 2; j--)
                {
                    if (body[j] is '.' or '!' or '?' or '。' or '！' or '？' or ';' && j + 1 < body.Length && (char.IsWhiteSpace(body[j + 1]) || body[j] > 0x3000))
                    {
                        cut = j + 1;
                        break;
                    }
                }
                if (cut < 0) cut = body.LastIndexOf(' ', limit - 1, maxChars / 2) + 1;
                if (cut <= pos) cut = limit;
                // The space after the period stays a separator.
                int next = cut;
                while (next < body.Length && body[next] == ' ') next++;
                result.Add((body[pos..cut], body[cut..next]));
                pos = next;
            }
            result.Add((body[pos..], sep));
        }
        return result;
    }

    /// <summary>Assembles the translated pieces back with the original separators.</summary>
    public static string Join(IReadOnlyList<TextChunk> chunks, IReadOnlyList<string> translated)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < translated.Count; i++)
        {
            sb.Append(translated[i]);
            if (i < chunks.Count) sb.Append(chunks[i].Separator);
        }
        return sb.ToString();
    }
}
