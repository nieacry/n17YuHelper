using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace N17YuHelper;

/// <summary>Откуда брать входные данные.</summary>
public enum InputSource
{
    /// <summary>Если текст выделен — берём его, иначе показываем рамку.</summary>
    Auto,
    SelectedText,
    Region,
    Clipboard,
    /// <summary>Та же область экрана, что в прошлый раз (без рамки).</summary>
    LastRegion,
    /// <summary>Голос: запись с микрофона → текст.</summary>
    Voice,
}

public enum HotkeyAction
{
    Menu,
    Ocr,
    Translate,
    Grammar,
    /// <summary>Распознать таблицу (в буфер — для вставки в Excel / Word).</summary>
    OcrTable,
    /// <summary>Распознать формулу (в буфер — LaTeX).</summary>
    OcrFormula,
    /// <summary>Своё действие из списка CustomActions (по имени в CustomAction).</summary>
    Custom,
    /// <summary>Текст как есть, без обработки (например, диктовка прямо в программу).</summary>
    AsIs,
}

public enum AfterAction
{
    ShowWindow,
    Copy,
    Replace,
    /// <summary>Показать результат поверх обведённой области (можно включить живое обновление).</summary>
    Overlay,
}

public sealed class HotkeyEntry
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string Keys { get; set; } = "";
    public InputSource Source { get; set; } = InputSource.Auto;
    public HotkeyAction Action { get; set; } = HotkeyAction.Menu;
    public AfterAction After { get; set; } = AfterAction.ShowWindow;
    /// <summary>Язык перевода: "auto" или код языка (en, ru, uk, zh).</summary>
    public string TargetLanguage { get; set; } = "auto";
    /// <summary>Имя своего действия (для Action = Custom).</summary>
    public string CustomAction { get; set; } = "";
}

public sealed class AppSettings
{
    public const int CurrentSettingsVersion = 10;

    public int SettingsVersion { get; set; } = CurrentSettingsVersion;

    /// <summary>
    /// Папка с моделями. Пусто — искать папку models рядом с exe и выше
    /// (publish\..\models, versions\vX\..\..\models). Относительный путь — от папки с exe.
    /// </summary>
    public string ModelsPath { get; set; } = "";

    /// <summary>Папка с llama-server.exe. Пусто — искать runtime\llama-server рядом с exe и выше.</summary>
    public string RuntimePath { get; set; } = "";

    /// <summary>Настройки модели распознавания текста.</summary>
    public OcrSettings Ocr { get; set; } = new();

    /// <summary>Настройки текстовой модели (перевод, грамматика).</summary>
    public TextModelSettings TextModel { get; set; } = new();

    /// <summary>Настройки перевода.</summary>
    public TranslationSettings Translation { get; set; } = new();

    /// <summary>Голосовой ввод.</summary>
    public VoiceSettings Voice { get; set; } = new();

    /// <summary>Свои действия (кнопки 6–9 в окне действий, можно назначить на клавиши).</summary>
    public List<CustomAction> CustomActions { get; set; } = CustomAction.Defaults();

    /// <summary>Через сколько минут простоя выгружать модели (0 — держать всегда).</summary>
    public int ModelIdleUnloadMinutes { get; set; } = 5;

    /// <summary>Сколько ждать, пока программа скопирует выделенный текст (мс).</summary>
    public int SelectionCopyTimeoutMs { get; set; } = 250;

    /// <summary>
    /// Программы, где вместо Ctrl+C используется Ctrl+Insert.
    /// В терминалах Ctrl+C без выделения прерывает запущенную команду.
    /// </summary>
    public List<string> CopyWithCtrlInsertProcesses { get; set; } =
    [
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh",
        "mintty", "alacritty", "wezterm-gui", "putty", "kitty",
    ];

    /// <summary>
    /// Программы, где выделенный текст не берём вообще (сразу рамка).
    /// В Maya и Blender Ctrl+C копирует выделенные объекты сцены — это может быть долго.
    /// </summary>
    public List<string> NeverCopyProcesses { get; set; } = ["maya", "blender"];

    /// <summary>
    /// Видеопамять: Auto — решить по объёму памяти; Both — держать обе модели;
    /// OneAtATime — перед загрузкой одной модели выгружать другую.
    /// </summary>
    public string GpuMode { get; set; } = "Auto";

    /// <summary>Окно приветствия (мастер моделей) уже показывалось.</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>Оформление: System (как в Windows), Light, Dark. Применяется после перезапуска.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// Пока запущена одна из этих программ, модели выгружаются после минуты простоя,
    /// чтобы освободить видеопамять для работы.
    /// </summary>
    public List<string> UnloadWhileRunning { get; set; } =
    [
        "maya", "blender", "houdini", "houdinifx", "ZBrush", "3dsmax", "Cinema 4D",
        "UnrealEditor", "Adobe Substance 3D Painter", "Marvelous Designer",
    ];

    /// <summary>Последняя обведённая область (x,y,w,h в пикселях экрана).</summary>
    public string LastRegion { get; set; } = "";

    /// <summary>Как часто проверять область в живом режиме окна поверх (мс).</summary>
    public int LiveIntervalMs { get; set; } = 1500;

    /// <summary>Показывать лупу в рамке выделения.</summary>
    public bool ShowMagnifier { get; set; } = false;

    /// <summary>Прилипать рамкой к окнам и элементам интерфейса.</summary>
    public bool SnapToWindows { get; set; } = true;

    /// <summary>Окно действий не закрывается, когда щёлкаешь в другом месте (как кнопка 📌).</summary>
    public bool KeepWindowOpen { get; set; } = false;

    /// <summary>Хранить историю на диске (history.json в папке данных), иначе только до выхода.</summary>
    public bool SaveHistory { get; set; } = true;

    /// <summary>Сколько последних результатов хранить.</summary>
    public int HistoryLimit { get; set; } = 300;

    /// <summary>Проверять обновления в интернете (GitHub) при запуске и раз в 6 часов. Только для установленной программы.</summary>
    public bool CheckUpdatesAutomatically { get; set; } = true;

    /// <summary>Ставить скачанное обновление самому, когда программа ничем не занята. Иначе — только уведомление.</summary>
    public bool InstallUpdatesAutomatically { get; set; } = true;

    /// <summary>Что делать по левому клику по иконке в трее.</summary>
    public InputSource TrayClickSource { get; set; } = InputSource.Region;

    public List<HotkeyEntry> Hotkeys { get; set; } = DefaultHotkeys();

    public static List<HotkeyEntry> DefaultHotkeys() =>
    [
        new() { Name = "Smart key: menu", Keys = "Ctrl+Shift+Win+Q", Source = InputSource.Auto, Action = HotkeyAction.Menu, After = AfterAction.ShowWindow },
        new() { Name = "OCR region to clipboard", Keys = "Ctrl+Shift+Win+O", Source = InputSource.Region, Action = HotkeyAction.Ocr, After = AfterAction.Copy },
        new() { Name = "Translate", Keys = "Ctrl+Shift+Win+T", Source = InputSource.Auto, Action = HotkeyAction.Translate, After = AfterAction.ShowWindow },
        new() { Name = "Check grammar", Keys = "Ctrl+Shift+Win+G", Source = InputSource.SelectedText, Action = HotkeyAction.Grammar, After = AfterAction.ShowWindow },
        TranslateAndReplaceHotkey(),
        LastRegionOverlayHotkey(),
        VoiceTranslateHotkey(),
        VoiceTypeHotkey(),
    ];

    private static HotkeyEntry VoiceTranslateHotkey() =>
        new() { Name = "Voice: speak and translate", Keys = "Ctrl+Shift+Win+V", Source = InputSource.Voice, Action = HotkeyAction.Translate, After = AfterAction.ShowWindow };

    private static HotkeyEntry VoiceTypeHotkey() =>
        new() { Name = "Voice: type into the app", Keys = "Ctrl+Shift+Win+D", Source = InputSource.Voice, Action = HotkeyAction.AsIs, After = AfterAction.Replace };

    private static HotkeyEntry LastRegionOverlayHotkey() =>
        new() { Name = "Translate last region (overlay)", Keys = "Ctrl+Shift+Win+A", Source = InputSource.LastRegion, Action = HotkeyAction.Translate, After = AfterAction.Overlay };

    private static HotkeyEntry TranslateAndReplaceHotkey() =>
        new() { Name = "Translate and replace", Keys = "Ctrl+Shift+Win+R", Source = InputSource.SelectedText, Action = HotkeyAction.Translate, After = AfterAction.Replace };

    // ---------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // кириллица читаемо, без \uXXXX
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

    /// <summary>Быстро прочитать только тему (до создания окон).</summary>
    public static string PeekTheme()
    {
        try
        {
            if (!File.Exists(FilePath)) return "System";
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.NameEquals("Theme") || p.Name.Equals("theme", StringComparison.OrdinalIgnoreCase))
                    return p.Value.GetString() ?? "System";
        }
        catch { /* битый файл — тема по умолчанию */ }
        return "System";
    }

    /// <summary>Загружает настройки. Если файла нет — создаёт его с настройками по умолчанию.</summary>
    public static AppSettings Load(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                var def = new AppSettings();
                def.Save();
                return def;
            }
            var json = File.ReadAllText(FilePath);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            s.Hotkeys ??= DefaultHotkeys();
            if (s.Migrate()) s.Save();
            return s;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error("Failed to read settings.json", ex);
            return new AppSettings();
        }
    }

    /// <summary>Обновляет старые настройки до текущей версии. Возвращает true, если что-то изменилось.</summary>
    private bool Migrate()
    {
        if (SettingsVersion >= CurrentSettingsVersion) return false;

        // v1 → v2: интерфейс на английском — переименовываем стандартные клавиши.
        if (SettingsVersion < 2)
        {
            var names = new Dictionary<string, string>
            {
                ["Умная клавиша — меню"] = "Smart key: menu",
                ["Распознать область в буфер"] = "OCR region to clipboard",
                ["Перевести"] = "Translate",
                ["Проверить грамматику"] = "Check grammar",
            };
            foreach (var h in Hotkeys)
                if (names.TryGetValue(h.Name, out var en)) h.Name = en;
        }

        // v2 → v3: лупа в рамке выделения по умолчанию выключена.
        if (SettingsVersion < 3) ShowMagnifier = false;

        // v8 → v9: клавиша «перевести последнюю область поверх».
        if (SettingsVersion < 9 && !Hotkeys.Any(h => string.Equals(h.Keys, "Ctrl+Shift+Win+A", StringComparison.OrdinalIgnoreCase)))
            Hotkeys.Add(LastRegionOverlayHotkey());

        // v9 → v10: голосовой ввод.
        if (SettingsVersion < 10)
        {
            if (!Hotkeys.Any(h => string.Equals(h.Keys, "Ctrl+Shift+Win+V", StringComparison.OrdinalIgnoreCase)))
                Hotkeys.Add(VoiceTranslateHotkey());
            if (!Hotkeys.Any(h => string.Equals(h.Keys, "Ctrl+Shift+Win+D", StringComparison.OrdinalIgnoreCase)))
                Hotkeys.Add(VoiceTypeHotkey());
        }

        // v6 → v7: новая клавиша «перевести и заменить» (если такая комбинация ещё не занята в настройках).
        if (SettingsVersion < 7 && !Hotkeys.Any(h => string.Equals(h.Keys, "Ctrl+Shift+Win+R", StringComparison.OrdinalIgnoreCase)))
            Hotkeys.Add(TranslateAndReplaceHotkey());

        // v5 → v6: список программ для автовыгрузки появился — у старых настроек он заполнен по умолчанию инициализатором.

        // v3 → v4: пути к моделям и llama-server ищутся автоматически.
        if (SettingsVersion < 4)
        {
            if (ModelsPath == @".\models") ModelsPath = "";
            if (RuntimePath == @".\runtime\llama-server") RuntimePath = "";
        }

        SettingsVersion = CurrentSettingsVersion;
        Log.Info("settings.json migrated to version " + CurrentSettingsVersion);
        return true;
    }

    /// <summary>Независимая копия (для окна настроек: правки не применяются до «Apply»).</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Отличаются ли настройки, от которых зависит запуск моделей.</summary>
    public bool ModelConfigDiffers(AppSettings other) =>
        ModelsPath != other.ModelsPath || RuntimePath != other.RuntimePath ||
        JsonSerializer.Serialize(Ocr, JsonOptions) != JsonSerializer.Serialize(other.Ocr, JsonOptions) ||
        JsonSerializer.Serialize(TextModel, JsonOptions) != JsonSerializer.Serialize(other.TextModel, JsonOptions) ||
        Voice.Enabled != other.Voice.Enabled;

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings.json", ex);
        }
    }

    public string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppPaths.DataDir, path));

    /// <summary>Папка с моделями (настройка или автопоиск).</summary>
    [JsonIgnore]
    public string ModelsDir => ResolveDir(ModelsPath, "models");

    /// <summary>Папка с llama-server.exe (настройка или автопоиск).</summary>
    [JsonIgnore]
    public string RuntimeDir => ResolveDir(RuntimePath, Path.Combine("runtime", "llama-server"));

    /// <summary>
    /// Если путь задан — возвращает его. Иначе ищет папку рядом с exe и до трёх уровней выше.
    /// Если нигде нет — возвращает вариант рядом с exe (для сообщения об ошибке).
    /// </summary>
    private string ResolveDir(string configured, string relative)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return ResolvePath(configured);

        // Установленная программа: только папка данных (выше по дереву — чужие папки).
        if (AppPaths.IsInstalled) return Path.Combine(AppPaths.DataDir, relative);

        var dir = new DirectoryInfo(AppPaths.ExeDir);
        for (int i = 0; i < 4 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(AppPaths.ExeDir, relative);
    }
}

public sealed class OcrSettings
{
    /// <summary>Файл модели относительно папки models.</summary>
    public string ModelFile { get; set; } = @"ocr\paddleocr-vl-1.6\PaddleOCR-VL-1.6-GGUF.gguf";

    /// <summary>Файл vision-проектора относительно папки models.</summary>
    public string MmprojFile { get; set; } = @"ocr\paddleocr-vl-1.6\PaddleOCR-VL-1.6-GGUF-mmproj.gguf";

    /// <summary>Шаблон чата относительно папки models (если файла нет — используется встроенный в модель).</summary>
    public string ChatTemplateFile { get; set; } = @"ocr\paddleocr-vl-1.6\chat_template.jinja";

    /// <summary>Порт llama-server. 0 — выбрать свободный автоматически.</summary>
    public int Port { get; set; } = 0;

    public int ContextSize { get; set; } = 4096;
    public int GpuLayers { get; set; } = 99;

    /// <summary>Максимум токенов в ответе (защита от зацикливания модели).</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Увеличивать мелкие области перед распознаванием (лучше читается мелкий шрифт).</summary>
    public bool UpscaleSmallImages { get; set; } = true;

    /// <summary>Максимум пикселей в одном куске картинки (как в модели: 1003520). Больше — режем на полосы.</summary>
    public int MaxPixelsPerTile { get; set; } = 1_000_000;

    /// <summary>Загружать модель сразу при нажатии клавиши, пока выделяется область.</summary>
    public bool PreloadOnHotkey { get; set; } = true;
}

public sealed class TextModelSettings
{
    /// <summary>Файл модели относительно папки models.</summary>
    public string ModelFile { get; set; } = @"llm\gemma-4-12b-qat\gemma-4-12b-it-qat-q4_0.gguf";

    /// <summary>Порт llama-server. 0 — выбрать свободный автоматически.</summary>
    public int Port { get; set; } = 0;

    public int ContextSize { get; set; } = 8192;
    public int GpuLayers { get; set; } = 99;
    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Начинать загрузку модели, как только открылось окно действий.</summary>
    public bool PreloadOnMenu { get; set; } = true;

    /// <summary>
    /// Аудио-проектор (mmproj) для голосового ввода, относительно папки models.
    /// Пусто — взять файл из каталога для выбранной модели, если он скачан.
    /// </summary>
    public string MmprojFile { get; set; } = "";

    /// <summary>Дополнительные аргументы llama-server (через пробел), если понадобятся.</summary>
    public string ExtraArgs { get; set; } = "";
}

public sealed class VoiceSettings
{
    /// <summary>Загружать вместе с моделью перевода её аудио-часть (нужно для голосового ввода).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Закончить запись, если после речи столько секунд тишины (0 — только вручную).</summary>
    public double AutoStopSilenceSeconds { get; set; } = 2.0;

    /// <summary>Самая длинная запись (секунд).</summary>
    public int MaxSeconds { get; set; } = 180;

    /// <summary>Микрофон (название из списка Windows). Пусто — микрофон по умолчанию.</summary>
    public string Microphone { get; set; } = "";
}

public sealed class TranslationSettings
{
    /// <summary>Куда переводить по умолчанию: язык текста → язык перевода (en, ru, uk, zh).</summary>
    public Dictionary<string, string> DefaultTargets { get; set; } = new()
    {
        ["ru"] = "en",
        ["uk"] = "en",
        ["en"] = "ru",
        ["zh"] = "ru",
    };

    /// <summary>Последний выбранный вручную язык перевода для каждого языка текста.</summary>
    public Dictionary<string, string> LastTargets { get; set; } = new();

    /// <summary>
    /// Словарь терминов: строки «термин => перевод» (или «термин => =», чтобы не переводить).
    /// В запрос попадают только термины, которые есть в тексте.
    /// </summary>
    /// Для новых установок пусто; у существующих настроек сохраняется то, что уже есть.
    public List<string> Glossary { get; set; } = [];
}

/// <summary>Своё действие: имя и указание для модели. Текст подставляется сам.</summary>
public sealed class CustomAction
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";

    public static List<CustomAction> Defaults() =>
    [
        new() { Name = "Make polite", Prompt = "Rewrite the text to sound polite and friendly, keeping its meaning." },
        new() { Name = "Shorten", Prompt = "Make the text shorter and clearer, keeping all important information." },
        new() { Name = "Explain", Prompt = "Explain what the text means in simple words. Answer in the same language as the text." },
        new() { Name = "Reply", Prompt = "Write a short, friendly reply to this message in the same language as the message." },
    ];
}
