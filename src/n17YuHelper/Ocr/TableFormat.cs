using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace N17YuHelper.Ocr;

/// <summary>
/// Таблицы и формулы из PaddleOCR-VL. Таблица приходит в формате OTSL:
/// &lt;fcel&gt; — ячейка с текстом, &lt;ecel&gt; — пустая, &lt;lcel&gt;/&lt;ucel&gt;/&lt;xcel&gt; — объединённые, &lt;nl&gt; — новая строка.
/// </summary>
public static partial class TableFormat
{
    [GeneratedRegex(@"<(fcel|ecel|lcel|ucel|xcel)>", RegexOptions.CultureInvariant)]
    private static partial Regex CellTag();

    public static List<List<string>> ParseOtsl(string otsl)
    {
        var rows = new List<List<string>>();
        foreach (var line in otsl.Split("<nl>", StringSplitOptions.RemoveEmptyEntries))
        {
            var row = new List<string>();
            var parts = CellTag().Split(line);
            // Split с группой: [до, тег, текст, тег, текст...]
            for (int i = 1; i + 1 < parts.Length + 1; i += 2)
            {
                string tag = parts[i];
                string text = i + 1 < parts.Length ? parts[i + 1].Trim() : "";
                row.Add(tag == "fcel" ? text : "");
            }
            if (row.Count > 0) rows.Add(row);
        }
        // Если модель вернула не OTSL, а обычный текст — по строке на ряд.
        if (rows.Count == 0)
            foreach (var line in otsl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                rows.Add([line.Trim()]);
        return rows;
    }

    /// <summary>Строки, разделённые табуляцией: так таблица вставляется в Excel по ячейкам.</summary>
    public static string ToTsv(List<List<string>> rows) =>
        string.Join("\r\n", rows.Select(r => string.Join("\t", r.Select(c => c.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ")))));

    public static List<List<string>> FromTsv(string tsv) =>
        tsv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('\t').ToList()).ToList();

    public static string ToHtml(List<List<string>> rows)
    {
        var sb = new StringBuilder("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\">");
        for (int r = 0; r < rows.Count; r++)
        {
            sb.Append("<tr>");
            foreach (var c in rows[r])
            {
                string tag = r == 0 ? "th" : "td";
                sb.Append('<').Append(tag).Append('>').Append(WebUtility.HtmlEncode(c)).Append("</").Append(tag).Append('>');
            }
            sb.Append("</tr>");
        }
        return sb.Append("</table>").ToString();
    }

    /// <summary>HTML в формате буфера обмена Windows (CF_HTML) — Word и Google Docs вставят настоящую таблицу.</summary>
    public static string ToClipboardHtml(string htmlFragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string pre = "<html><body><!--StartFragment-->";
        const string post = "<!--EndFragment--></body></html>";
        int headerLen = string.Format(header, 0, 0, 0, 0).Length;
        int startHtml = headerLen;
        int startFrag = startHtml + Encoding.UTF8.GetByteCount(pre);
        int endFrag = startFrag + Encoding.UTF8.GetByteCount(htmlFragment);
        int endHtml = endFrag + Encoding.UTF8.GetByteCount(post);
        return string.Format(header, startHtml, endHtml, startFrag, endFrag) + pre + htmlFragment + post;
    }

    /// <summary>Таблица в буфер: текст с табуляциями (Excel) и HTML (Word).</summary>
    public static DataObject ToClipboard(string tsv)
    {
        var data = new DataObject();
        data.SetText(tsv, TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Html, ToClipboardHtml(ToHtml(FromTsv(tsv))));
        return data;
    }

    /// <summary>Убирает обёртки \[ \], $$ и т.п. — остаётся чистый LaTeX.</summary>
    public static string CleanLatex(string s)
    {
        s = s.Trim();
        foreach (var (open, close) in new[] { ("\\[", "\\]"), ("$$", "$$"), ("\\(", "\\)"), ("$", "$") })
        {
            if (s.StartsWith(open) && s.EndsWith(close) && s.Length >= open.Length + close.Length)
            {
                s = s[open.Length..^close.Length].Trim();
                break;
            }
        }
        return s;
    }
}
