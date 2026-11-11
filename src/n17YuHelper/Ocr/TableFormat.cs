using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace N17YuHelper.Ocr;

/// <summary>
/// Tables and formulas from PaddleOCR-VL. A table comes in OTSL format:
/// &lt;fcel&gt; — cell with text, &lt;ecel&gt; — empty, &lt;lcel&gt;/&lt;ucel&gt;/&lt;xcel&gt; — merged, &lt;nl&gt; — new row.
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
            // Split with a group: [before, tag, text, tag, text...]
            for (int i = 1; i + 1 < parts.Length + 1; i += 2)
            {
                string tag = parts[i];
                string text = i + 1 < parts.Length ? parts[i + 1].Trim() : "";
                row.Add(tag == "fcel" ? text : "");
            }
            if (row.Count > 0) rows.Add(row);
        }
        // If the model returned plain text instead of OTSL — one row per line.
        if (rows.Count == 0)
            foreach (var line in otsl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                rows.Add([line.Trim()]);
        return rows;
    }

    /// <summary>Tab-separated lines: this is how a table pastes into Excel cell by cell.</summary>
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

    /// <summary>HTML in the Windows clipboard format (CF_HTML) — Word and Google Docs paste a real table.</summary>
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

    /// <summary>Table to the clipboard: tab-separated text (Excel) and HTML (Word).</summary>
    public static DataObject ToClipboard(string tsv)
    {
        var data = new DataObject();
        data.SetText(tsv, TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Html, ToClipboardHtml(ToHtml(FromTsv(tsv))));
        return data;
    }

    /// <summary>Removes \[ \], $$ and similar wrappers — leaves clean LaTeX.</summary>
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
