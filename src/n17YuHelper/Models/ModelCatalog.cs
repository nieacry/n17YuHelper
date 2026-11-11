using System.Text.Json;
using System.Text.Json.Serialization;

namespace N17YuHelper.Models;

public enum ModelKind
{
    Ocr,
    Text,
}

/// <summary>Model file: where to download it from, where to put it (relative to the models folder), size and SHA256 (if known).</summary>
public sealed record ModelFile(string Url, string RelativePath, long Size, string? Sha256 = null);

/// <summary>A downloadable model: one or more files in the models folder.</summary>
public sealed record ModelPackage(
    string Id,
    ModelKind Kind,
    string Name,
    string Description,
    double VramGb,
    IReadOnlyList<ModelFile> Files,
    ModelFile? Audio = null)
{
    /// <summary>All files: the model and (if any) the audio part for voice input.</summary>
    public IReadOnlyList<ModelFile> AllFiles => Audio is null ? Files : [.. Files, Audio];

    public long TotalSize => AllFiles.Sum(f => f.Size);

    public bool HasAudio => Audio is not null;

    public bool IsAudioInstalled(string modelsDir) =>
        Audio is not null && File.Exists(Path.Combine(modelsDir, Audio.RelativePath));

    /// <summary>Main model file (relative to the models folder) — this is what goes into the settings.</summary>
    public string MainFile => Files[0].RelativePath;

    public bool IsInstalled(string modelsDir) =>
        Files.All(f => File.Exists(Path.Combine(modelsDir, f.RelativePath)));

    public long InstalledSize(string modelsDir) =>
        AllFiles.Sum(f => File.Exists(Path.Combine(modelsDir, f.RelativePath)) ? new FileInfo(Path.Combine(modelsDir, f.RelativePath)).Length : 0);
}

/// <summary>
/// What can be downloaded and what to recommend for the amount of GPU memory.
///
/// The built-in list is a fallback. The fresh catalog lives in the repository (catalog.json) and is downloaded
/// at startup (<see cref="CatalogUpdater"/>): links can be fixed, models added or
/// the tested llama.cpp build changed without releasing a new app version.
/// Hugging Face links are pinned to a specific revision; every file has a SHA256.
/// </summary>
public static class ModelCatalog
{
    /// <summary>Which catalog.json format this app version understands.</summary>
    public const int SupportedFormat = 1;

    private const string HF = "https://huggingface.co";

    private static ModelFile F(string repo, string rev, string file, string localDir, long size, string? sha) =>
        new($"{HF}/{repo}/resolve/{rev}/{file}", $@"{localDir}\{file}", size, sha);

    private static readonly IReadOnlyList<ModelPackage> Builtin =
    [
        new("ocr-paddleocr-vl-1.6", ModelKind.Ocr,
            "PaddleOCR-VL 1.6",
            "Text recognition: 109 languages incl. Cyrillic and Chinese, tables, small UI text.",
            2.0,
            [
                F("PaddlePaddle/PaddleOCR-VL-1.6-GGUF", "511b09642bb324401f15f97cc23bc67e8f0a291d", "PaddleOCR-VL-1.6-GGUF.gguf", @"ocr\paddleocr-vl-1.6", 935_769_056, "f3ae46ec885050acf4b3d31944431e1fd90d50664fb09126af4a3c050ba14ee8"),
                F("PaddlePaddle/PaddleOCR-VL-1.6-GGUF", "511b09642bb324401f15f97cc23bc67e8f0a291d", "PaddleOCR-VL-1.6-GGUF-mmproj.gguf", @"ocr\paddleocr-vl-1.6", 881_770_560, "204d757d7610d9b3faab10d506d69e5b244e32bf765e2bab2d0167e65e0a058a"),
                F("PaddlePaddle/PaddleOCR-VL-1.6-GGUF", "511b09642bb324401f15f97cc23bc67e8f0a291d", "chat_template.jinja", @"ocr\paddleocr-vl-1.6", 1_831, "1369e43952c7a84e834be95732d65111e96cbefe30aa32a4826eca344f5ef939"),
            ]),
        new("gemma-4-e2b", ModelKind.Text,
            "Gemma 4 E2B",
            "Smallest. For GPUs with 4 GB or less, or CPU only. Basic translation quality.",
            3.6,
            [F("unsloth/gemma-4-E2B-it-GGUF", "0314792d7f1f7e229411f620751375812bb9faf2", "gemma-4-E2B-it-Q4_K_M.gguf", @"llm\gemma-4-e2b", 3_106_738_272, "740185b21d22ceb83a11c3aa62ad5842ef32c70f6096d756bbee85a1e4ec34b8")],
            F("ggml-org/gemma-4-E2B-it-GGUF", "63f6c6f23b3e9e5f9737708daec2f88c9ac2e921", "mmproj-gemma-4-E2B-it-Q8_0.gguf", @"llm\gemma-4-e2b", 557_368_096, "042cc2561806cd3b5cb5efb25cab76911e3100c43cc09d854904f921924b1429")),
        new("gemma-4-e4b", ModelKind.Text,
            "Gemma 4 E4B",
            "Small and fast. For 6–8 GB GPUs. Good everyday translation.",
            5.8,
            [F("unsloth/gemma-4-E4B-it-GGUF", "bfc15c382204943c3a8fff0c750b94ae2364d7a3", "gemma-4-E4B-it-Q4_K_M.gguf", @"llm\gemma-4-e4b", 4_977_171_584, "85a896a047553e842f25297ee5b031d64ff30147d9c4af17b1e4b394cd1fab87")],
            F("ggml-org/gemma-4-E4B-it-GGUF", "27cd6836f8e2a6e5e6b57ea0e77fa72490706985", "mmproj-gemma-4-E4B-it-Q8_0.gguf", @"llm\gemma-4-e4b", 559_874_848, "9257f21a0f5358204b4a612cff83fad2e56412f4f607f0049331ce60c0cf9d34")),
        new("gemma-4-12b-qat", ModelKind.Text,
            "Gemma 4 12B (QAT)",
            "Balanced. For 10–12 GB GPUs. Strong translation and grammar check.",
            8.0,
            [F("google/gemma-4-12B-it-qat-q4_0-gguf", "29d097773436b69ff9feafd636ab4cf873786537", "gemma-4-12b-it-qat-q4_0.gguf", @"llm\gemma-4-12b-qat", 6_975_879_296, "93567e57a8fe10b23569b9d9ec38cd005deedf71e29477c421a4b83f418a538b")],
            F("google/gemma-4-12B-it-qat-q4_0-gguf", "29d097773436b69ff9feafd636ab4cf873786537", "mmproj-gemma-4-12b-it-qat-q4_0.gguf", @"llm\gemma-4-12b-qat", 175_115_616, "cb018338a7538a9814d994bfe54644c71eb7ed54e31eae2f721e45fd3c260da7")),
        new("qwen3.5-9b", ModelKind.Text,
            "Qwen3.5 9B",
            "Alternative model, very strong in Chinese. For 8–12 GB GPUs.",
            7.0,
            [F("unsloth/Qwen3.5-9B-GGUF", "3885219b6810b007914f3a7950a8d1b469d598a5", "Qwen3.5-9B-Q4_K_M.gguf", @"llm\qwen3.5-9b", 5_680_522_464, "03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8")]),
        new("gemma-4-26b-a4b-qat", ModelKind.Text,
            "Gemma 4 26B-A4B (QAT)",
            "Smarter, still fast (only ~4B parameters active). For 16–24 GB GPUs.",
            15.5,
            [F("google/gemma-4-26B-A4B-it-qat-q4_0-gguf", "d1c082be9cf3c8a514acf63b8761f4b41935842e", "gemma-4-26B_q4_0-it.gguf", @"llm\gemma-4-26b-a4b-qat", 14_439_363_584, "3eca3b8f6d7baf218a7dd6bba5fb59a56ee25fe2d567b6f5f589b4f697eca51d")]),
        new("gemma-4-31b-qat", ModelKind.Text,
            "Gemma 4 31B (QAT)",
            "Best quality, slower. For 24 GB GPUs and more.",
            19.5,
            [F("google/gemma-4-31B-it-qat-q4_0-gguf", "59dde24573e7e61570dba08b18a2e1fe246955ed", "gemma-4-31B_q4_0-it.gguf", @"llm\gemma-4-31b-qat", 17_651_001_568, "179cfb99212709597eae5929112cfca677e1bbf566178b479ae1da0c4772874b")]),
    ];

    /// <summary>Recommendations by GPU memory: from larger to smaller (GB → model id).</summary>
    private static readonly IReadOnlyList<(double MinVramGb, string Id)> BuiltinRecommend =
    [
        (23, "gemma-4-31b-qat"),
        (15, "gemma-4-26b-a4b-qat"),
        (9.5, "gemma-4-12b-qat"),
        (5.5, "gemma-4-e4b"),
        (0, "gemma-4-e2b"),
    ];

    /// <summary>Tested llama.cpp build (ggml-org/llama.cpp release tag).</summary>
    public const string BuiltinLlamaCppTag = "b12248";

    private static IReadOnlyList<ModelPackage> _all = Builtin;
    private static IReadOnlyList<(double MinVramGb, string Id)> _recommend = BuiltinRecommend;

    public static IReadOnlyList<ModelPackage> All => _all;

    /// <summary>llama.cpp build tag to install (from the catalog). Empty — the newest.</summary>
    public static string LlamaCppTag { get; private set; } = BuiltinLlamaCppTag;

    /// <summary>Where the catalog came from: "built-in" or the catalog.json date.</summary>
    public static string Source { get; private set; } = "built-in";

    public const string OcrId = "ocr-paddleocr-vl-1.6";

    public static ModelPackage Ocr => ById(OcrId) ?? Builtin[0];

    public static ModelPackage? ById(string id) => _all.FirstOrDefault(p => p.Id == id);

    public static ModelPackage? ByMainFile(string relativePath) =>
        _all.FirstOrDefault(p => string.Equals(p.MainFile, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Best text model for the amount of GPU memory (taking into account that the OCR model is needed too).</summary>
    public static ModelPackage RecommendedText(GpuInfo gpu)
    {
        double gb = gpu.Vendor == GpuVendor.None ? 0 : gpu.VramGb;
        foreach (var (min, id) in _recommend)
            if (gb >= min && ById(id) is { Kind: ModelKind.Text } p) return p;
        return _all.Where(p => p.Kind == ModelKind.Text).MinBy(p => p.VramGb) ?? Builtin[1];
    }

    /// <summary>How much GPU memory a model needs (from the catalog or by file size).</summary>
    public static double VramNeeded(string modelsDir, string relativeModelFile, double fallbackGb)
    {
        if (ByMainFile(relativeModelFile) is { } p) return p.VramGb;
        var path = Path.Combine(modelsDir, relativeModelFile);
        return File.Exists(path) ? new FileInfo(path).Length / 1024.0 / 1024 / 1024 * 1.15 + 0.8 : fallbackGb;
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1L << 20):0} MB";

    // ---------------- catalog.json ----------------

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Applies catalog.json. Returns an error text or null.
    /// A catalog with an unknown format or without the OCR model is not applied.
    /// </summary>
    public static string? Apply(string json, string source)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions);
            if (doc is null) return "empty catalog";
            if (doc.Format > SupportedFormat) return $"catalog format {doc.Format} is newer than this app understands";
            var packages = new List<ModelPackage>();
            foreach (var m in doc.Models ?? [])
            {
                if (string.IsNullOrWhiteSpace(m.Id) || m.Files is not { Count: > 0 }) continue;
                if (m.Files.Concat(m.Audio is null ? [] : [m.Audio]).Any(f => !IsSafeFile(f))) continue;
                packages.Add(new ModelPackage(m.Id, m.Kind, m.Name ?? m.Id, m.Description ?? "", m.VramGb,
                    m.Files.Select(ToFile).ToList(), m.Audio is null ? null : ToFile(m.Audio)));
            }
            if (!packages.Any(p => p.Id == OcrId) || !packages.Any(p => p.Kind == ModelKind.Text))
                return "catalog has no OCR or text models";

            _all = packages;
            _recommend = doc.Recommend is { Count: > 0 } r
                ? r.OrderByDescending(x => x.MinVramGb).Select(x => (x.MinVramGb, x.Id)).ToList()
                : BuiltinRecommend;
            LlamaCppTag = doc.LlamaCpp?.Tag ?? BuiltinLlamaCppTag;
            Source = source;
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Only https and a path inside the models folder (no ".." and no absolute paths).</summary>
    private static bool IsSafeFile(CatalogFile f) =>
        Uri.TryCreate(f.Url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrWhiteSpace(f.Path) && !Path.IsPathRooted(f.Path) &&
        !f.Path.Replace('/', '\\').Split('\\').Any(part => part is ".." or "") && f.Size > 0;

    private static ModelFile ToFile(CatalogFile f) =>
        new(f.Url, f.Path.Replace('/', '\\'), f.Size, string.IsNullOrWhiteSpace(f.Sha256) ? null : f.Sha256.ToLowerInvariant());

    /// <summary>Built-in catalog in catalog.json format (for the file in the repository).</summary>
    public static string ExportBuiltin() => JsonSerializer.Serialize(new CatalogDocument
    {
        Format = SupportedFormat,
        LlamaCpp = new CatalogLlama { Tag = BuiltinLlamaCppTag },
        Recommend = BuiltinRecommend.Select(r => new CatalogRecommend { MinVramGb = r.MinVramGb, Id = r.Id }).ToList(),
        Models = Builtin.Select(p => new CatalogModel
        {
            Id = p.Id, Kind = p.Kind, Name = p.Name, Description = p.Description, VramGb = p.VramGb,
            Files = p.Files.Select(FromFile).ToList(),
            Audio = p.Audio is null ? null : FromFile(p.Audio),
        }).ToList(),
    }, JsonOptions);

    private static CatalogFile FromFile(ModelFile f) =>
        new() { Url = f.Url, Path = f.RelativePath.Replace('\\', '/'), Size = f.Size, Sha256 = f.Sha256 };
}

// ---------------- catalog.json format ----------------

internal sealed class CatalogDocument
{
    public int Format { get; set; } = 1;
    public CatalogLlama? LlamaCpp { get; set; }
    public List<CatalogRecommend>? Recommend { get; set; }
    public List<CatalogModel>? Models { get; set; }
}

internal sealed class CatalogLlama
{
    /// <summary>ggml-org/llama.cpp release tag, e.g. b12248. Empty — the newest build.</summary>
    public string? Tag { get; set; }
}

internal sealed class CatalogRecommend
{
    public double MinVramGb { get; set; }
    public string Id { get; set; } = "";
}

internal sealed class CatalogModel
{
    public string Id { get; set; } = "";
    public ModelKind Kind { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public double VramGb { get; set; }
    public List<CatalogFile>? Files { get; set; }
    public CatalogFile? Audio { get; set; }
}

internal sealed class CatalogFile
{
    public string Url { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sha256 { get; set; }
}
