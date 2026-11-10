using System.Text.RegularExpressions;

namespace N17YuHelper.Grammar;

public enum DiffKind
{
    Equal,
    Deleted,
    Inserted,
}

public sealed record DiffPart(DiffKind Kind, string Text);

/// <summary>
/// Разница между исходным и исправленным текстом по словам (LCS).
/// Для китайского каждый иероглиф — отдельный «токен».
/// </summary>
public static partial class TextDiff
{
    [GeneratedRegex(@"\p{IsCJKUnifiedIdeographs}|[\w']+|\s+|[^\w\s]", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private const long MaxCells = 4_000_000;

    public static List<DiffPart> Compute(string original, string corrected)
    {
        var a = Tokenize(original);
        var b = Tokenize(corrected);

        // Слишком длинный текст — без подсветки, просто «было/стало».
        if ((long)a.Count * b.Count > MaxCells)
            return [new DiffPart(DiffKind.Deleted, original), new DiffPart(DiffKind.Inserted, corrected)];

        int n = a.Count, m = b.Count;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var parts = new List<DiffPart>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { Add(parts, DiffKind.Equal, a[x]); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { Add(parts, DiffKind.Deleted, a[x]); x++; }
            else { Add(parts, DiffKind.Inserted, b[y]); y++; }
        }
        while (x < n) Add(parts, DiffKind.Deleted, a[x++]);
        while (y < m) Add(parts, DiffKind.Inserted, b[y++]);
        return parts;
    }

    private static List<string> Tokenize(string s) =>
        TokenRegex().Matches(s.Replace("\r\n", "\n")).Select(mt => mt.Value).ToList();

    private static void Add(List<DiffPart> parts, DiffKind kind, string text)
    {
        if (parts.Count > 0 && parts[^1].Kind == kind)
            parts[^1] = parts[^1] with { Text = parts[^1].Text + text };
        else
            parts.Add(new DiffPart(kind, text));
    }
}
