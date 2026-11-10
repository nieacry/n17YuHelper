using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using N17YuHelper.Llm;

namespace N17YuHelper.Ocr;

/// <summary>Что распознавать: обычный текст, таблицу или формулу.</summary>
public enum OcrMode
{
    Text,
    Table,
    Formula,
}

/// <summary>
/// Распознавание текста на картинке через PaddleOCR-VL в llama-server.
/// Мелкие области увеличиваются, большие режутся на полосы по пустым строкам.
/// </summary>
public sealed class OcrService(LlamaServer server, Func<OcrSettings> settings)
{
    public LlamaServer Server { get; } = server;

    public async Task<string> RecognizeAsync(Bitmap image, IProgress<string>? progress, CancellationToken ct, OcrMode mode = OcrMode.Text)
    {
        var s = settings();
        var sw = Stopwatch.StartNew();

        if (Server.State != ServerState.Ready)
            progress?.Report("Loading OCR model...");
        await Server.EnsureReadyAsync(ct);

        string what = mode switch { OcrMode.Table => "table", OcrMode.Formula => "formula", _ => "text" };
        progress?.Report($"Recognizing {what}...");
        // Таблицу и формулу не режем на полосы — модель должна видеть их целиком.
        var tiles = mode == OcrMode.Text ? PrepareTiles(image, s) : [PrepareSingle(image, s)];
        var sb = new StringBuilder();
        try
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (tiles.Count > 1) progress?.Report($"Recognizing text... ({i + 1}/{tiles.Count})");
                var text = await RequestAsync(tiles[i], s, mode, ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(text.Trim());
                }
            }
        }
        finally
        {
            foreach (var t in tiles)
                if (!ReferenceEquals(t, image)) t.Dispose();
            Server.Touch();
        }

        Log.Info($"OCR {mode}: {image.Width}x{image.Height}, {tiles.Count} tile(s), {sb.Length} chars, {sw.Elapsed.TotalSeconds:0.00} s");
        return mode switch
        {
            OcrMode.Table => TableFormat.ToTsv(TableFormat.ParseOtsl(sb.ToString())),
            OcrMode.Formula => TableFormat.CleanLatex(sb.ToString()),
            _ => sb.ToString(),
        };
    }

    private async Task<string> RequestAsync(Bitmap tile, OcrSettings s, OcrMode mode, CancellationToken ct)
    {
        string dataUrl;
        using (var ms = new MemoryStream())
        {
            tile.Save(ms, ImageFormat.Png);
            dataUrl = "data:image/png;base64," + Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);
        }

        var payload = new JsonObject
        {
            ["model"] = "ocr",
            ["temperature"] = 0,
            ["max_tokens"] = mode == OcrMode.Table ? Math.Max(s.MaxTokens, 4096) : s.MaxTokens,
            ["repeat_penalty"] = 1.05,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = dataUrl },
                        },
                        // Модель обучена на фиксированных подсказках: «OCR:», «Table Recognition:», «Formula Recognition:».
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = mode switch { OcrMode.Table => "Table Recognition:", OcrMode.Formula => "Formula Recognition:", _ => "OCR:" },
                        },
                    },
                },
            },
        };

        using var resp = await LlamaServer.Http.PostAsJsonAsync(new Uri(Server.BaseUri, "v1/chat/completions"), payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OCR request failed ({(int)resp.StatusCode}): {Shorten(body, 300)}");

        var json = JsonNode.Parse(body);
        return json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
    }

    // ---------------- Подготовка картинки ----------------

    /// <summary>Одна картинка целиком (мелкая — увеличивается, крупная — уменьшается моделью сама).</summary>
    private static Bitmap PrepareSingle(Bitmap source, OcrSettings s)
    {
        if (!s.UpscaleSmallImages) return source;
        int longSide = Math.Max(source.Width, source.Height);
        if (longSide >= 640) return source;
        double f = Math.Min(3.0, 1000.0 / longSide);
        f = Math.Min(f, Math.Sqrt(Math.Max(200_000, s.MaxPixelsPerTile) / ((double)source.Width * source.Height)));
        return f >= 1.25 ? Resize(source, f) : source;
    }

    private static List<Bitmap> PrepareTiles(Bitmap source, OcrSettings s)
    {
        Bitmap img = source;
        int maxPixels = Math.Max(200_000, s.MaxPixelsPerTile);

        // Мелкий интерфейсный шрифт модель читает лучше, если область увеличить.
        if (s.UpscaleSmallImages)
        {
            int longSide = Math.Max(img.Width, img.Height);
            if (longSide < 640)
            {
                double f = Math.Min(3.0, 1000.0 / longSide);
                f = Math.Min(f, Math.Sqrt(maxPixels / ((double)img.Width * img.Height)));
                if (f >= 1.25) img = Resize(img, f);
            }
        }

        long area = (long)img.Width * img.Height;
        if (area <= maxPixels) return [img];

        // Большая область: режем на горизонтальные полосы по самым «пустым» строкам.
        int stripHeight = Math.Max(64, maxPixels / img.Width);
        var rowNoise = RowNoise(img);
        var tiles = new List<Bitmap>();
        int y = 0;
        while (y < img.Height)
        {
            int end = y + stripHeight;
            if (end >= img.Height)
            {
                end = img.Height;
            }
            else
            {
                int from = Math.Max(y + stripHeight * 3 / 4, y + 1);
                int best = end;
                double bestNoise = double.MaxValue;
                for (int r = end; r >= from; r--)
                {
                    if (rowNoise[r] < bestNoise)
                    {
                        bestNoise = rowNoise[r];
                        best = r;
                    }
                }
                end = best;
            }
            tiles.Add(img.Clone(new Rectangle(0, y, img.Width, end - y), PixelFormat.Format24bppRgb));
            y = end;
        }

        if (!ReferenceEquals(img, source)) img.Dispose();
        return tiles;
    }

    private static Bitmap Resize(Bitmap src, double factor)
    {
        int w = (int)Math.Round(src.Width * factor);
        int h = (int)Math.Round(src.Height * factor);
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return dst;
    }

    /// <summary>Разброс яркости в каждой строке: чем меньше, тем вероятнее строка между строками текста.</summary>
    private static unsafe double[] RowNoise(Bitmap img)
    {
        var result = new double[img.Height + 1];
        var data = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (int y = 0; y < img.Height; y++)
            {
                byte* row = (byte*)data.Scan0 + (long)y * data.Stride;
                double sum = 0, sumSq = 0;
                int n = 0;
                for (int x = 0; x < img.Width; x += 2)
                {
                    byte* p = row + x * 3;
                    double l = 0.114 * p[0] + 0.587 * p[1] + 0.299 * p[2];
                    sum += l;
                    sumSq += l * l;
                    n++;
                }
                double mean = sum / n;
                result[y] = sumSq / n - mean * mean;
            }
        }
        finally
        {
            img.UnlockBits(data);
        }
        result[img.Height] = 0;
        return result;
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
