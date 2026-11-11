using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace N17YuHelper.Files;

/// <summary>Translates a batch of paragraphs (tags &lt;1&gt;…&lt;/1&gt; must be kept). Returns the same number of lines.</summary>
public delegate Task<IReadOnlyList<string>> BatchTranslator(IReadOnlyList<string> items, CancellationToken ct);

/// <summary>
/// Translates a Word document (.docx) or presentation (.pptx) keeping the formatting:
/// only the text inside paragraphs changes; styles, tables, images and markup stay as they were.
/// Parts of a paragraph with different formatting (a bold word, a link) are sent to the model in tags &lt;1&gt;…&lt;/1&gt;
/// so the translation lands in the same places. If the model loses the tags, the whole paragraph gets the first part's formatting.
/// </summary>
public static class OfficeTranslator
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Xml = XNamespace.Xml;

    public static bool IsSupported(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".pptx";

    private sealed class Segment
    {
        public required List<XElement> Runs { get; init; }
        public string Text { get; set; } = "";
    }

    private sealed class Paragraph
    {
        public required List<Segment> Segments { get; init; }
        public string Source { get; set; } = "";   // text for the model (with tags if there are several parts)
        public string Plain { get; set; } = "";    // text without tags
        public bool Tagged { get; set; }
    }

    /// <summary>
    /// Translates the document. onParagraph receives each translated paragraph in order (for display).
    /// Returns the finished file.
    /// </summary>
    public static async Task<byte[]> TranslateAsync(string path, string targetLangTag, BatchTranslator translate,
        Action<string>? onParagraph, IProgress<string>? progress, CancellationToken ct, int batchChars = 1500, int batchItems = 40)
    {
        bool word = Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase);
        var ms = new MemoryStream();
        await using (var fs = File.OpenRead(path)) await fs.CopyToAsync(ms, ct);

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Parts with text: document, headers/footers, footnotes / slides and notes.
            var parts = zip.Entries
                .Where(e => word
                    ? Regex.IsMatch(e.FullName, @"^word/(document|header\d*|footer\d*|footnotes|endnotes)\.xml$")
                    : Regex.IsMatch(e.FullName, @"^ppt/(slides/slide|notesSlides/notesSlide)\d+\.xml$"))
                .OrderBy(e => PartOrder(e.FullName))
                .ToList();

            var docs = new List<(ZipArchiveEntry Entry, XDocument Doc)>();
            var paragraphs = new List<Paragraph>();
            foreach (var entry in parts)
            {
                XDocument doc;
                await using (var s = entry.Open()) doc = await XDocument.LoadAsync(s, LoadOptions.PreserveWhitespace, ct);
                docs.Add((entry, doc));
                paragraphs.AddRange(Collect(doc, word));
            }
            if (paragraphs.Count == 0) throw new InvalidDataException("No text found in the document.");

            // Batches of paragraphs for the model.
            var batches = new List<List<Paragraph>>();
            var cur = new List<Paragraph>();
            int chars = 0;
            foreach (var p in paragraphs)
            {
                if (cur.Count > 0 && (chars + p.Source.Length > batchChars || cur.Count >= batchItems))
                {
                    batches.Add(cur);
                    cur = [];
                    chars = 0;
                }
                cur.Add(p);
                chars += p.Source.Length;
            }
            if (cur.Count > 0) batches.Add(cur);

            int fallbacks = 0;
            for (int b = 0; b < batches.Count; b++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(batches.Count == 1 ? "Translating the document..." : $"Translating the document: part {b + 1} of {batches.Count}...");
                var batch = batches[b];
                var result = await translate(batch.Select(p => p.Source).ToList(), ct);
                for (int i = 0; i < batch.Count; i++)
                {
                    string t = i < result.Count ? result[i] : "";
                    if (string.IsNullOrWhiteSpace(t)) t = batch[i].Source; // the model returned nothing — keep the original
                    if (!Apply(batch[i], t)) fallbacks++;
                    onParagraph?.Invoke(StripTags(t));
                }
            }

            foreach (var (entry, doc) in docs)
            {
                SetLanguage(doc, word, targetLangTag);
                string name = entry.FullName;
                entry.Delete();
                var created = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var s = created.Open();
                await doc.SaveAsync(s, SaveOptions.DisableFormatting, ct);
            }
            Log.Info($"Office translation: {paragraphs.Count} paragraphs, {batches.Count} batches, formatting simplified in {fallbacks}");
        }
        return ms.ToArray();
    }

    private static int PartOrder(string name)
    {
        var m = Regex.Match(name, @"(\d+)\.xml$");
        int n = m.Success ? int.Parse(m.Groups[1].Value) : 0;
        int group = name.Contains("document") || name.Contains("/slides/") ? 0 : name.Contains("header") ? 1 : name.Contains("footer") ? 2 : 3;
        return group * 100_000 + n;
    }

    // ---------------- parsing ----------------

    private static IEnumerable<Paragraph> Collect(XDocument doc, bool word)
    {
        XName p = word ? W + "p" : A + "p";
        XName r = word ? W + "r" : A + "r";
        XName rPr = word ? W + "rPr" : A + "rPr";
        XName t = word ? W + "t" : A + "t";

        foreach (var para in doc.Descendants(p))
        {
            // Runs of this paragraph (without nested paragraphs — text boxes inside drawings are parsed separately).
            var runs = para.Descendants(r)
                .Where(x => x.Ancestors(p).First() == para && x.Elements(t).Any())
                .ToList();
            if (runs.Count == 0) continue;

            var segments = new List<Segment>();
            string? lastFormat = null;
            foreach (var run in runs)
            {
                string format = FormatKey(run.Element(rPr)) + "|" + (run.Parent?.Name.LocalName ?? "");
                string text = string.Concat(run.Elements(t).Select(x => x.Value));
                if (segments.Count > 0 && format == lastFormat)
                {
                    segments[^1].Runs.Add(run);
                    segments[^1].Text += text;
                }
                else
                {
                    segments.Add(new Segment { Runs = [run], Text = text });
                }
                lastFormat = format;
            }

            string plain = string.Concat(segments.Select(s => s.Text));
            if (!plain.Any(char.IsLetter)) continue; // numbers, spaces, symbols — nothing to translate

            var meaningful = segments.Where(s => s.Text.Trim().Length > 0).ToList();
            var paragraph = new Paragraph { Segments = segments, Plain = plain };
            if (meaningful.Count <= 1)
            {
                paragraph.Source = plain;
            }
            else
            {
                var sb = new StringBuilder();
                for (int i = 0; i < segments.Count; i++)
                    sb.Append('<').Append(i + 1).Append('>').Append(segments[i].Text).Append("</").Append(i + 1).Append('>');
                paragraph.Source = sb.ToString();
                paragraph.Tagged = true;
            }
            yield return paragraph;
        }
    }

    /// <summary>Run formatting without language and spell-check settings (they do not make the text look different).</summary>
    private static string FormatKey(XElement? rPr)
    {
        if (rPr is null) return "";
        var copy = new XElement(rPr);
        copy.Elements().Where(e => e.Name.LocalName is "lang" or "noProof").Remove();
        foreach (var f in copy.Elements().Where(e => e.Name.LocalName == "rFonts")) f.Attributes().Where(a => a.Name.LocalName == "hint").Remove();
        copy.Attributes().Where(a => a.Name.LocalName is "lang" or "altLang" or "dirty" or "err" or "noProof" or "smtClean").Remove();
        return copy.ToString(SaveOptions.DisableFormatting);
    }

    private static readonly Regex TagRx = new(@"<(\d+)>(.*?)</\1>", RegexOptions.Singleline);

    public static string StripTags(string s) => Regex.Replace(s, @"</?\d+>", "");

    /// <summary>Writes the translation into the paragraph. False — tags were lost, formatting simplified.</summary>
    private static bool Apply(Paragraph p, string translated)
    {
        if (!p.Tagged)
        {
            // Spaces at the paragraph edges — as in the original.
            string lead = p.Plain[..(p.Plain.Length - p.Plain.TrimStart().Length)];
            string trail = p.Plain[p.Plain.TrimEnd().Length..];
            var target = p.Segments.First(s => s.Text.Trim().Length > 0);
            foreach (var s in p.Segments) SetText(s, s == target ? lead + StripTags(translated).Trim() + trail : s.Text.Trim().Length == 0 ? s.Text : "");
            return true;
        }

        // Parse the reply in order: spaces between tags are attached to the previous part.
        var found = new Dictionary<int, string>();
        var order = new List<int>();
        int pos = 0;
        int prev = -1;
        bool ok = true;
        foreach (Match m in TagRx.Matches(translated))
        {
            string between = translated[pos..m.Index];
            if (between.Trim().Length > 0) ok = false;
            else if (between.Length > 0 && prev > 0) found[prev] += between;
            pos = m.Index + m.Length;
            if (!int.TryParse(m.Groups[1].Value, out int n) || n < 1 || n > p.Segments.Count || found.ContainsKey(n)) { ok = false; continue; }
            found[n] = (prev < 0 && between.Length > 0 ? between : "") + m.Groups[2].Value;
            order.Add(n);
            prev = n;
        }
        if (translated[pos..].Trim().Length > 0) ok = false;
        else if (prev > 0) found[prev] += translated[pos..];

        bool allMeaningful = p.Segments.Select((s, i) => (s, i)).All(x => x.s.Text.Trim().Length == 0 || found.ContainsKey(x.i + 1));
        bool sorted = order.SequenceEqual(order.OrderBy(x => x));
        // The order of parts changed (another language, another word order) — reorder only if all parts are present.
        if (ok && allMeaningful && (sorted || order.Count == p.Segments.Count))
        {
            for (int i = 0; i < p.Segments.Count; i++)
                SetText(p.Segments[i], found.TryGetValue(i + 1, out var v) ? v : p.Segments[i].Text);
            if (!sorted) Reorder(p, order);
            return true;
        }

        // Tags lost: the whole translation goes into the first part with text.
        var first = p.Segments.First(s => s.Text.Trim().Length > 0);
        foreach (var s in p.Segments) SetText(s, s == first ? StripTags(translated).Trim() : "");
        return false;
    }

    /// <summary>Reorders the parts' runs in the order the model placed them.</summary>
    private static void Reorder(Paragraph p, List<int> order)
    {
        // Only if all runs are siblings in one parent; otherwise (links, fields) leave them alone.
        var allRuns = p.Segments.SelectMany(s => s.Runs).ToList();
        var parent = allRuns[0].Parent;
        if (parent is null || allRuns.Any(r => r.Parent != parent)) return;
        var siblings = parent.Elements().ToList();
        int start = siblings.IndexOf(allRuns[0]);
        if (start < 0 || siblings.Skip(start).Take(allRuns.Count).Where(e => allRuns.Contains(e)).Count() != allRuns.Count) return;

        var rest = Enumerable.Range(1, p.Segments.Count).Except(order);
        var sequence = order.Concat(rest).SelectMany(n => p.Segments[n - 1].Runs).ToList();
        var anchor = allRuns[0].PreviousNode;
        foreach (var r in allRuns) r.Remove();
        if (anchor is not null) anchor.AddAfterSelf(sequence);
        else parent.AddFirst(sequence);
    }

    /// <summary>Part text: into the first text element of the first run, other text elements are removed.</summary>
    private static void SetText(Segment s, string text)
    {
        bool placed = false;
        foreach (var run in s.Runs)
        {
            foreach (var t in run.Elements().Where(e => e.Name.LocalName == "t").ToList())
            {
                if (!placed)
                {
                    t.Value = text;
                    if (t.Name.Namespace == W) t.SetAttributeValue(Xml + "space", "preserve");
                    placed = true;
                }
                else t.Remove();
            }
        }
    }

    /// <summary>Text language in the document — so Word/PowerPoint spell-check in the new language.</summary>
    private static void SetLanguage(XDocument doc, bool word, string tag)
    {
        if (word)
        {
            foreach (var lang in doc.Descendants(W + "lang"))
            {
                bool eastAsia = tag.StartsWith("zh");
                lang.SetAttributeValue(W + (eastAsia ? "eastAsia" : "val"), tag);
            }
        }
        else
        {
            foreach (var rPr in doc.Descendants().Where(e => e.Name == A + "rPr" || e.Name == A + "endParaRPr"))
                if (rPr.Attribute("lang") is not null) rPr.SetAttributeValue("lang", tag);
        }
    }
}
