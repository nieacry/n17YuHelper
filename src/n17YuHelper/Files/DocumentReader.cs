using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace N17YuHelper.Files;

/// <summary>Страница PDF: текст (если есть текстовый слой) или картинка скана для распознавания.</summary>
public sealed record PdfPageContent(int Number, string Text, byte[]? ScanImage);

/// <summary>
/// Чтение файлов для перевода: текстовые форматы, Word (.docx), PowerPoint (.pptx), PDF и картинки.
/// Без Office и без внешних программ.
/// </summary>
public static class DocumentReader
{
    public const int MaxChars = 400_000;

    private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".rst", ".srt", ".vtt", ".ass", ".ssa", ".sub", ".csv", ".tsv",
        ".json", ".xml", ".html", ".htm", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".log", ".po", ".pot",
        ".strings", ".resx", ".tex", ".lua", ".py", ".js", ".ts", ".cs", ".cpp", ".h", ".mel", ".nfo",
    };

    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff",
    };

    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path));

    public static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return TextExt.Contains(ext) || ImageExt.Contains(ext) || ext.ToLowerInvariant() is ".pdf" or ".docx" or ".pptx" or ".rtf";
    }

    /// <summary>Какое расширение оставить у переведённого файла (текстовые форматы — своё, остальное — .txt).</summary>
    public static string ResultExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return TextExt.Contains(ext) ? ext : ".txt";
    }

    public const string OpenFilter =
        "Documents and images|*.txt;*.md;*.srt;*.vtt;*.ass;*.csv;*.tsv;*.json;*.xml;*.html;*.htm;*.yaml;*.yml;*.ini;*.log;*.po;*.docx;*.pptx;*.rtf;*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|" +
        "All files|*.*";

    // ---------------- текст ----------------

    /// <summary>Текстовый файл: UTF-8/UTF-16 (по BOM), иначе UTF-8, а если он не подходит — Windows-1251.</summary>
    public static string ReadTextFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF) ||
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            using var r = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return r.ReadToEnd();
        }
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    // ---------------- Word / PowerPoint ----------------

    /// <summary>Текст документа Word: абзацы по строкам, ячейки таблиц через табуляцию.</summary>
    public static string ReadDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("This is not a Word document.");
        using var s = entry.Open();
        return OfficeXmlText(s, "p", wordprocessing: true);
    }

    /// <summary>Текст презентации: слайды по порядку, между слайдами — пустая строка.</summary>
    public static string ReadPptx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var slides = zip.Entries
            .Select(e => (e, m: Regex.Match(e.FullName, @"^ppt/slides/slide(\d+)\.xml$")))
            .Where(x => x.m.Success)
            .OrderBy(x => int.Parse(x.m.Groups[1].Value))
            .ToList();
        var sb = new StringBuilder();
        foreach (var (e, _) in slides)
        {
            using var s = e.Open();
            var text = OfficeXmlText(s, "p", wordprocessing: false).Trim();
            if (text.Length == 0) continue;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(text);
        }
        return sb.ToString();
    }

    /// <summary>Общий разбор XML Office: текст из w:t / a:t, абзацы — новая строка.</summary>
    private static string OfficeXmlText(Stream xml, string paragraph, bool wordprocessing)
    {
        var sb = new StringBuilder();
        using var r = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = false });
        bool inText = false;
        bool cellStarted = false;
        while (r.Read())
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Element:
                    switch (r.LocalName)
                    {
                        case "t":
                            inText = !r.IsEmptyElement;
                            break;
                        case "tab" when wordprocessing:
                            sb.Append('\t');
                            break;
                        case "br" or "cr":
                            sb.Append('\n');
                            break;
                        case "tc" when wordprocessing:
                            if (cellStarted) sb.Append('\t');
                            cellStarted = true;
                            break;
                        case "tr" when wordprocessing:
                            cellStarted = false;
                            break;
                    }
                    break;
                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace when inText:
                    sb.Append(r.Value);
                    break;
                case XmlNodeType.EndElement:
                    if (r.LocalName == "t") inText = false;
                    else if (r.LocalName == paragraph)
                    {
                        // Абзацы внутри ячейки таблицы — через пробел, чтобы строка таблицы не разваливалась.
                        if (wordprocessing && cellStarted) sb.Append(' ');
                        else sb.Append('\n');
                    }
                    else if (r.LocalName == "tr" && wordprocessing)
                    {
                        sb.Append('\n');
                        cellStarted = false;
                    }
                    break;
            }
        }
        // Пустые абзацы Word превращаем максимум в одну пустую строку.
        return Regex.Replace(sb.ToString().Replace(" \t", "\t").Replace(" \n", "\n"), @"\n{3,}", "\n\n").Trim();
    }

    // ---------------- PDF ----------------

    /// <summary>
    /// Страницы PDF. Если на странице почти нет текста (скан), отдаётся самая большая картинка страницы —
    /// её нужно распознать моделью OCR.
    /// </summary>
    public static List<PdfPageContent> ReadPdf(string path, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var pages = new List<PdfPageContent>();
        using var doc = PdfDocument.Open(path);
        int count = doc.NumberOfPages;
        for (int n = 1; n <= count; n++)
        {
            ct.ThrowIfCancellationRequested();
            if (count > 3) progress?.Report($"Reading PDF page {n}/{count}...");
            var page = doc.GetPage(n);
            string text;
            try { text = ContentOrderTextExtractor.GetText(page, true); }
            catch { text = page.Text; }
            text = text.Trim();

            byte[]? scan = null;
            if (CountLetters(text) < 20) scan = LargestImage(page);
            pages.Add(new PdfPageContent(n, text, scan));
        }
        return pages;
    }

    private static int CountLetters(string s) => s.Count(char.IsLetter);

    /// <summary>Самая крупная картинка страницы в виде PNG или JPEG (то, что может открыть GDI+).</summary>
    private static byte[]? LargestImage(UglyToad.PdfPig.Content.Page page)
    {
        byte[]? best = null;
        long bestArea = 0;
        foreach (var img in page.GetImages())
        {
            long area = (long)img.WidthInSamples * img.HeightInSamples;
            if (area < 200 * 200 || area <= bestArea) continue;
            byte[]? bytes = null;
            try
            {
                if (img.TryGetPng(out var png)) bytes = png;
                else
                {
                    var raw = img.RawMemory.ToArray();
                    // JPEG (DCTDecode) лежит в PDF как есть.
                    if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8) bytes = raw;
                }
            }
            catch { /* необычный формат картинки — пропускаем */ }
            if (bytes is null) continue;
            best = bytes;
            bestArea = area;
        }
        return best;
    }
}
