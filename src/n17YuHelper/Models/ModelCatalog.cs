namespace N17YuHelper.Models;

public enum ModelKind
{
    Ocr,
    Text,
}

public sealed record ModelFile(string Url, string RelativePath, long Size);

/// <summary>Модель, которую можно скачать: один или несколько файлов в папке models.</summary>
public sealed record ModelPackage(
    string Id,
    ModelKind Kind,
    string Name,
    string Description,
    double VramGb,
    IReadOnlyList<ModelFile> Files,
    ModelFile? Audio = null)
{
    /// <summary>Все файлы: модель и (если есть) аудио-часть для голосового ввода.</summary>
    public IReadOnlyList<ModelFile> AllFiles => Audio is null ? Files : [.. Files, Audio];

    public long TotalSize => AllFiles.Sum(f => f.Size);

    public bool HasAudio => Audio is not null;

    public bool IsAudioInstalled(string modelsDir) =>
        Audio is not null && File.Exists(Path.Combine(modelsDir, Audio.RelativePath));

    /// <summary>Главный файл модели (относительно папки models) — его прописываем в настройки.</summary>
    public string MainFile => Files[0].RelativePath;

    public bool IsInstalled(string modelsDir) =>
        Files.All(f => File.Exists(Path.Combine(modelsDir, f.RelativePath)));

    public long InstalledSize(string modelsDir) =>
        AllFiles.Sum(f => File.Exists(Path.Combine(modelsDir, f.RelativePath)) ? new FileInfo(Path.Combine(modelsDir, f.RelativePath)).Length : 0);
}

/// <summary>Что можно скачать и что советовать под объём видеопамяти.</summary>
public static class ModelCatalog
{
    private const string HF = "https://huggingface.co";

    public static readonly ModelPackage Ocr = new(
        "ocr-paddleocr-vl-1.6", ModelKind.Ocr,
        "PaddleOCR-VL 1.6",
        "Text recognition: 109 languages incl. Cyrillic and Chinese, tables, small UI text.",
        2.0,
        [
            new($"{HF}/PaddlePaddle/PaddleOCR-VL-1.6-GGUF/resolve/main/PaddleOCR-VL-1.6-GGUF.gguf", @"ocr\paddleocr-vl-1.6\PaddleOCR-VL-1.6-GGUF.gguf", 935_769_056),
            new($"{HF}/PaddlePaddle/PaddleOCR-VL-1.6-GGUF/resolve/main/PaddleOCR-VL-1.6-GGUF-mmproj.gguf", @"ocr\paddleocr-vl-1.6\PaddleOCR-VL-1.6-GGUF-mmproj.gguf", 881_770_560),
            new($"{HF}/PaddlePaddle/PaddleOCR-VL-1.6-GGUF/resolve/main/chat_template.jinja", @"ocr\paddleocr-vl-1.6\chat_template.jinja", 1_831),
        ]);

    public static readonly ModelPackage GemmaE2B = new(
        "gemma-4-e2b", ModelKind.Text,
        "Gemma 4 E2B",
        "Smallest. For GPUs with 4 GB or less, or CPU only. Basic translation quality.",
        3.6,
        [new($"{HF}/unsloth/gemma-4-E2B-it-GGUF/resolve/main/gemma-4-E2B-it-Q4_K_M.gguf", @"llm\gemma-4-e2b\gemma-4-E2B-it-Q4_K_M.gguf", 3_106_738_272)],
        new($"{HF}/ggml-org/gemma-4-E2B-it-GGUF/resolve/main/mmproj-gemma-4-E2B-it-Q8_0.gguf", @"llm\gemma-4-e2b\mmproj-gemma-4-E2B-it-Q8_0.gguf", 557_368_096));

    public static readonly ModelPackage GemmaE4B = new(
        "gemma-4-e4b", ModelKind.Text,
        "Gemma 4 E4B",
        "Small and fast. For 6–8 GB GPUs. Good everyday translation.",
        5.8,
        [new($"{HF}/unsloth/gemma-4-E4B-it-GGUF/resolve/main/gemma-4-E4B-it-Q4_K_M.gguf", @"llm\gemma-4-e4b\gemma-4-E4B-it-Q4_K_M.gguf", 4_977_171_584)],
        new($"{HF}/ggml-org/gemma-4-E4B-it-GGUF/resolve/main/mmproj-gemma-4-E4B-it-Q8_0.gguf", @"llm\gemma-4-e4b\mmproj-gemma-4-E4B-it-Q8_0.gguf", 559_874_848));

    public static readonly ModelPackage Gemma12B = new(
        "gemma-4-12b-qat", ModelKind.Text,
        "Gemma 4 12B (QAT)",
        "Balanced. For 10–12 GB GPUs. Strong translation and grammar check.",
        8.0,
        [new($"{HF}/google/gemma-4-12B-it-qat-q4_0-gguf/resolve/main/gemma-4-12b-it-qat-q4_0.gguf", @"llm\gemma-4-12b-qat\gemma-4-12b-it-qat-q4_0.gguf", 6_975_879_296)],
        new($"{HF}/google/gemma-4-12B-it-qat-q4_0-gguf/resolve/main/mmproj-gemma-4-12b-it-qat-q4_0.gguf", @"llm\gemma-4-12b-qat\mmproj-gemma-4-12b-it-qat-q4_0.gguf", 175_115_616));

    public static readonly ModelPackage Gemma26B = new(
        "gemma-4-26b-a4b-qat", ModelKind.Text,
        "Gemma 4 26B-A4B (QAT)",
        "Smarter, still fast (only ~4B parameters active). For 16–24 GB GPUs.",
        15.5,
        [new($"{HF}/google/gemma-4-26B-A4B-it-qat-q4_0-gguf/resolve/main/gemma-4-26B_q4_0-it.gguf", @"llm\gemma-4-26b-a4b-qat\gemma-4-26B_q4_0-it.gguf", 14_439_363_584)]);

    public static readonly ModelPackage Gemma31B = new(
        "gemma-4-31b-qat", ModelKind.Text,
        "Gemma 4 31B (QAT)",
        "Best quality, slower. For 24 GB GPUs and more.",
        19.5,
        [new($"{HF}/google/gemma-4-31B-it-qat-q4_0-gguf/resolve/main/gemma-4-31B_q4_0-it.gguf", @"llm\gemma-4-31b-qat\gemma-4-31B_q4_0-it.gguf", 17_651_001_568)]);

    public static readonly ModelPackage Qwen9B = new(
        "qwen3.5-9b", ModelKind.Text,
        "Qwen3.5 9B",
        "Alternative model, very strong in Chinese. For 8–12 GB GPUs.",
        7.0,
        [new($"{HF}/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf", @"llm\qwen3.5-9b\Qwen3.5-9B-Q4_K_M.gguf", 5_680_522_464)]);

    public static readonly IReadOnlyList<ModelPackage> All = [Ocr, GemmaE2B, GemmaE4B, Gemma12B, Qwen9B, Gemma26B, Gemma31B];

    public static ModelPackage? ByMainFile(string relativePath) =>
        All.FirstOrDefault(p => string.Equals(p.MainFile, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Лучшая текстовая модель под объём видеопамяти (с учётом, что модель распознавания тоже нужна).</summary>
    public static ModelPackage RecommendedText(GpuInfo gpu)
    {
        double gb = gpu.Vendor == GpuVendor.None ? 0 : gpu.VramGb;
        return gb switch
        {
            >= 23 => Gemma31B,
            >= 15 => Gemma26B,
            >= 9.5 => Gemma12B,
            >= 5.5 => GemmaE4B,
            _ => GemmaE2B,
        };
    }

    /// <summary>Сколько видеопамяти нужно модели (из каталога или по размеру файла).</summary>
    public static double VramNeeded(string modelsDir, string relativeModelFile, double fallbackGb)
    {
        if (ByMainFile(relativeModelFile) is { } p) return p.VramGb;
        var path = Path.Combine(modelsDir, relativeModelFile);
        return File.Exists(path) ? new FileInfo(path).Length / 1024.0 / 1024 / 1024 * 1.15 + 0.8 : fallbackGb;
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1L << 20):0} MB";
}
