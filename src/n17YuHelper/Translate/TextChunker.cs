using System.Text.RegularExpressions;

namespace N17YuHelper.Translate;

/// <summary>Кусок длинного текста и то, что стояло после него (пустые строки), — чтобы собрать перевод обратно.</summary>
public sealed record TextChunk(string Text, string Separator);

/// <summary>
/// Делит длинный текст на куски для перевода: по абзацам (пустым строкам), длинные абзацы — по строкам,
/// а совсем длинные строки — по предложениям. Разделители сохраняются как были.
/// </summary>
public static class TextChunker
{
    public static List<TextChunk> Split(string text, int maxChars)
    {
        // Части: [текст, разделитель, текст, разделитель, ...]
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
            // Длинный абзац — по строкам, потом по предложениям.
            var pieces = SplitKeeping(body, @"(\r?\n)", maxChars);
            for (int k = 0; k < pieces.Count; k++)
                units.Add((pieces[k].Text, k == pieces.Count - 1 ? sep : pieces[k].Sep));
        }

        // Склеиваем соседние части, пока помещаются.
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
            // Строка длиннее лимита: режем по концу предложения, в крайнем случае — по пробелу.
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
                // Пробел после точки остаётся разделителем.
                int next = cut;
                while (next < body.Length && body[next] == ' ') next++;
                result.Add((body[pos..cut], body[cut..next]));
                pos = next;
            }
            result.Add((body[pos..], sep));
        }
        return result;
    }

    /// <summary>Собирает переводы кусков обратно с исходными разделителями.</summary>
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
