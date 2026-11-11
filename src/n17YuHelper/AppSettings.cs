using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace N17YuHelper;

/// <summary>Where the input comes from.</summary>
public enum InputSource
{
    /// <summary>Selected text if any, otherwise the region frame.</summary>
    Auto,
    SelectedText,
    Region,
    Clipboard,
    /// <summary>The same screen region as last time (no frame).</summary>
    LastRegion,
    /// <summary>Voice: microphone recording → text.</summary>
    Voice,
}

public enum HotkeyAction
{
    Menu,
    Ocr,
    Translate,
    Grammar,
    /// <summary>Recognize a table (to the clipboard — for pasting into Excel / Word).</summary>
    OcrTable,
    /// <summary>Recognize a formula (to the clipboard — LaTeX).</summary>
    OcrFormula,
    /// <summary>A custom action from CustomActions (by the name in CustomAction).</summary>
    Custom,
    /// <summary>Text as is, no processing (e.g. dictation straight into an app).</summary>
    AsIs,
    /// <summary>Reopen the action window the way it was last closed (Source and Then are ignored).</summary>
    OpenLastWindow,
}

public enum AfterAction
{
    ShowWindow,
    Copy,
    Replace,
    /// <summary>Show the result over the selected region (live updates can be turned on).</summary>
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
    /// <summary>Target language: "auto" or a language code (en, ru, uk, zh).</summary>
    public string TargetLanguage { get; set; } = "auto";
    /// <summary>Custom action name (for Action = Custom).</summary>
    public string CustomAction { get; set; } = "";
}

public sealed class AppSettings
{
    public const int CurrentSettingsVersion = 11;

    public int SettingsVersion { get; set; } = CurrentSettingsVersion;

    /// <summary>
    /// Models folder. Empty — look for a models folder next to the exe and above
    /// (publish\..\models, versions\vX\..\..\models). A relative path is relative to the data folder.
    /// </summary>
    public string ModelsPath { get; set; } = "";

    /// <summary>Folder with llama-server.exe. Empty — look for runtime\llama-server next to the exe and above.</summary>
    public string RuntimePath { get; set; } = "";

    /// <summary>Text recognition model settings.</summary>
    public OcrSettings Ocr { get; set; } = new();

    /// <summary>Text model settings (translation, grammar).</summary>
    public TextModelSettings TextModel { get; set; } = new();

    /// <summary>Translation settings.</summary>
    public TranslationSettings Translation { get; set; } = new();

    /// <summary>Voice input.</summary>
    public VoiceSettings Voice { get; set; } = new();

    /// <summary>Custom actions (buttons 6–9 in the action window, can be bound to hotkeys).</summary>
    public List<CustomAction> CustomActions { get; set; } = CustomAction.Defaults();

    /// <summary>Unload models after this many idle minutes (0 — keep loaded).</summary>
    public int ModelIdleUnloadMinutes { get; set; } = 5;

    /// <summary>How long to wait for the app to copy the selected text (ms).</summary>
    public int SelectionCopyTimeoutMs { get; set; } = 250;

    /// <summary>
    /// Apps where Ctrl+Insert is used instead of Ctrl+C.
    /// In terminals Ctrl+C without a selection interrupts the running command.
    /// </summary>
    public List<string> CopyWithCtrlInsertProcesses { get; set; } =
    [
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh",
        "mintty", "alacritty", "wezterm-gui", "putty", "kitty",
    ];

    /// <summary>
    /// Apps where selected text is never taken (straight to the frame).
    /// In Maya and Blender Ctrl+C copies selected scene objects, which can take long.
    /// </summary>
    public List<string> NeverCopyProcesses { get; set; } = ["maya", "blender"];

    /// <summary>
    /// GPU memory: Auto — decide by the amount of memory; Both — keep both models loaded;
    /// OneAtATime — unload one model before loading the other.
    /// </summary>
    public string GpuMode { get; set; } = "Auto";

    /// <summary>The welcome window (model wizard) has been shown.</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>Theme: System (as in Windows), Light, Dark. Applied after restart.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// While one of these apps is running, models are unloaded after a minute of idle time
    /// to free GPU memory for work.
    /// </summary>
    public List<string> UnloadWhileRunning { get; set; } =
    [
        "maya", "blender", "houdini", "houdinifx", "ZBrush", "3dsmax", "Cinema 4D",
        "UnrealEditor", "Adobe Substance 3D Painter", "Marvelous Designer",
    ];

    /// <summary>Last selected region (x,y,w,h in screen pixels).</summary>
    public string LastRegion { get; set; } = "";

    /// <summary>How often the region is checked in the live overlay mode (ms).</summary>
    public int LiveIntervalMs { get; set; } = 1500;

    /// <summary>Show the magnifier in the region frame.</summary>
    public bool ShowMagnifier { get; set; } = false;

    /// <summary>Snap the frame to windows and controls.</summary>
    public bool SnapToWindows { get; set; } = true;

    /// <summary>The action window does not close when you click elsewhere (like the 📌 button).</summary>
    public bool KeepWindowOpen { get; set; } = false;

    /// <summary>Keep history on disk (history.json in the data folder), otherwise only until exit.</summary>
    public bool SaveHistory { get; set; } = true;

    /// <summary>How many recent results to keep.</summary>
    public int HistoryLimit { get; set; } = 300;

    /// <summary>Check for updates online (GitHub) at startup and every 6 hours. Installed app only.</summary>
    public bool CheckUpdatesAutomatically { get; set; } = true;

    /// <summary>Install a downloaded update automatically when the app is idle. Otherwise only notify.</summary>
    public bool InstallUpdatesAutomatically { get; set; } = true;

    /// <summary>What a left click on the tray icon does.</summary>
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
        OpenLastWindowHotkey(),
    ];

    private static HotkeyEntry OpenLastWindowHotkey() =>
        new() { Name = "Open last window", Keys = "Ctrl+Shift+Win+W", Source = InputSource.Auto, Action = HotkeyAction.OpenLastWindow, After = AfterAction.ShowWindow };

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
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // non-Latin text stays readable, no \uXXXX
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

    /// <summary>Quickly read only the theme (before any window is created).</summary>
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
        catch { /* broken file — default theme */ }
        return "System";
    }

    /// <summary>Loads settings. Creates the file with defaults if it does not exist.</summary>
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

    /// <summary>Upgrades old settings to the current version. Returns true if anything changed.</summary>
    private bool Migrate()
    {
        if (SettingsVersion >= CurrentSettingsVersion) return false;

        // v1 → v2 (Russian hotkey names → English) is no longer needed: such settings no longer exist.

        // v2 → v3: the magnifier in the region frame is off by default.
        if (SettingsVersion < 3) ShowMagnifier = false;

        // v8 → v9: hotkey "translate the last region over the original".
        if (SettingsVersion < 9 && !Hotkeys.Any(h => string.Equals(h.Keys, "Ctrl+Shift+Win+A", StringComparison.OrdinalIgnoreCase)))
            Hotkeys.Add(LastRegionOverlayHotkey());

        // v9 → v10: voice input.
        if (SettingsVersion < 10)
        {
            if (!Hotkeys.Any(h => string.Equals(h.Keys, "Ctrl+Shift+Win+V", StringComparison.OrdinalIgnoreCase)))
                Hotkeys.Add(VoiceTranslateHotkey());
            if (!Hotkeys.Any(h => string.Equals(h.Keys, "Ctrl+Shift+Win+D", StringComparison.OrdinalIgnoreCase)))
                Hotkeys.Add(VoiceTypeHotkey());
        }

        // v10 → v11: hotkey "Open last window".
        if (SettingsVersion < 11 && !Hotkeys.Any(h => h.Action == HotkeyAction.OpenLastWindow ||
                                                      string.Equals(h.Keys, "Ctrl+Shift+Win+W", StringComparison.OrdinalIgnoreCase)))
            Hotkeys.Add(OpenLastWindowHotkey());

        // v6 → v7: new hotkey "translate and replace" (if that combination is not taken yet).
        if (SettingsVersion < 7 && !Hotkeys.Any(h => string.Equals(h.Keys, "Ctrl+Shift+Win+R", StringComparison.OrdinalIgnoreCase)))
            Hotkeys.Add(TranslateAndReplaceHotkey());

        // v5 → v6: the auto-unload app list appeared — old settings get the default list from the initializer.

        // v3 → v4: model and llama-server paths are found automatically.
        if (SettingsVersion < 4)
        {
            if (ModelsPath == @".\models") ModelsPath = "";
            if (RuntimePath == @".\runtime\llama-server") RuntimePath = "";
        }

        SettingsVersion = CurrentSettingsVersion;
        Log.Info("settings.json migrated to version " + CurrentSettingsVersion);
        return true;
    }

    /// <summary>Independent copy (for the settings window: changes apply only on Apply).</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Whether settings that affect starting the models differ.</summary>
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

    /// <summary>Models folder (setting or auto-detected).</summary>
    [JsonIgnore]
    public string ModelsDir => ResolveDir(ModelsPath, "models");

    /// <summary>Folder with llama-server.exe (setting or auto-detected).</summary>
    [JsonIgnore]
    public string RuntimeDir => ResolveDir(RuntimePath, Path.Combine("runtime", "llama-server"));

    /// <summary>
    /// Returns the path if it is set. Otherwise looks for the folder next to the exe and up to three levels above.
    /// If it is nowhere — returns the path next to the exe (for the error message).
    /// </summary>
    private string ResolveDir(string configured, string relative)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return ResolvePath(configured);

        // Installed app: only the data folder (folders further up belong to others).
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
    /// <summary>Model file relative to the models folder.</summary>
    public string ModelFile { get; set; } = @"ocr\paddleocr-vl-1.6\PaddleOCR-VL-1.6-GGUF.gguf";

    /// <summary>Vision projector file relative to the models folder.</summary>
    public string MmprojFile { get; set; } = @"ocr\paddleocr-vl-1.6\PaddleOCR-VL-1.6-GGUF-mmproj.gguf";

    /// <summary>Chat template relative to the models folder (if missing, the model's built-in one is used).</summary>
    public string ChatTemplateFile { get; set; } = @"ocr\paddleocr-vl-1.6\chat_template.jinja";

    /// <summary>llama-server port. 0 — pick a free one automatically.</summary>
    public int Port { get; set; } = 0;

    public int ContextSize { get; set; } = 4096;
    public int GpuLayers { get; set; } = 99;

    /// <summary>Max tokens in a reply (protects against the model looping).</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Upscale small regions before recognition (small fonts read better).</summary>
    public bool UpscaleSmallImages { get; set; } = true;

    /// <summary>Max pixels in one image tile (as in the model: 1003520). Larger images are cut into strips.</summary>
    public int MaxPixelsPerTile { get; set; } = 1_000_000;

    /// <summary>Start loading the model as soon as the hotkey is pressed, while the region is being selected.</summary>
    public bool PreloadOnHotkey { get; set; } = true;
}

public sealed class TextModelSettings
{
    /// <summary>Model file relative to the models folder.</summary>
    public string ModelFile { get; set; } = @"llm\gemma-4-12b-qat\gemma-4-12b-it-qat-q4_0.gguf";

    /// <summary>llama-server port. 0 — pick a free one automatically.</summary>
    public int Port { get; set; } = 0;

    public int ContextSize { get; set; } = 8192;
    public int GpuLayers { get; set; } = 99;
    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Start loading the model as soon as the action window opens.</summary>
    public bool PreloadOnMenu { get; set; } = true;

    /// <summary>
    /// Audio projector (mmproj) for voice input, relative to the models folder.
    /// Empty — take the file from the catalog for the selected model, if it is downloaded.
    /// </summary>
    public string MmprojFile { get; set; } = "";

    /// <summary>Extra llama-server arguments (space-separated), if needed.</summary>
    public string ExtraArgs { get; set; } = "";
}

public sealed class VoiceSettings
{
    /// <summary>Load the translation model's audio part together with it (needed for voice input).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Stop recording after this many seconds of silence following speech (0 — manual only).</summary>
    public double AutoStopSilenceSeconds { get; set; } = 2.0;

    /// <summary>Longest recording (seconds).</summary>
    public int MaxSeconds { get; set; } = 180;

    /// <summary>Microphone (name from the Windows list). Empty — default microphone.</summary>
    public string Microphone { get; set; } = "";
}

public sealed class TranslationSettings
{
    /// <summary>Default translation direction: text language → target language (en, ru, uk, zh).</summary>
    public Dictionary<string, string> DefaultTargets { get; set; } = new()
    {
        ["ru"] = "en",
        ["uk"] = "en",
        ["en"] = "ru",
        ["zh"] = "ru",
    };

    /// <summary>Last manually chosen target language for each text language.</summary>
    public Dictionary<string, string> LastTargets { get; set; } = new();

    /// <summary>
    /// Glossary: lines "term => translation" (or "term => =" to keep it untranslated).
    /// Only terms that occur in the text are sent with the request.
    /// </summary>
    /// Empty for new installs; existing settings keep what they have.
    public List<string> Glossary { get; set; } = [];
}

/// <summary>Custom action: name and instruction for the model. The text is added automatically.</summary>
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
