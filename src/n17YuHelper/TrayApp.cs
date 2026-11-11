using System.Diagnostics;
using System.Reflection;
using N17YuHelper.Capture;
using N17YuHelper.Grammar;
using N17YuHelper.Llm;
using N17YuHelper.Models;
using N17YuHelper.Ocr;
using N17YuHelper.Translate;
using N17YuHelper.UI;
using N17YuHelper.Voice;

namespace N17YuHelper;

/// <summary>Tray icon, hotkeys, models and running action chains.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _hotkeysMenu;
    private readonly ToolStripMenuItem _modelStatusItem;
    private readonly ToolStripMenuItem _freeGpuItem;
    private ToolStripMenuItem? _lastWindowItem;
    private readonly HotkeyManager _hotkeys;
    private readonly System.Windows.Forms.Timer _trimTimer;
    private readonly System.Windows.Forms.Timer _idleTimer;
    private readonly Icon _icon;

    private readonly LlamaServer _ocrServer;
    private readonly OcrService _ocr;
    private readonly LlamaServer _textServer;
    private readonly TranslationService _translation;
    private readonly GrammarService _grammar;
    private readonly PromptService _prompts;
    private readonly VoiceService _voice;
    private RecordingToast? _recording;
    private readonly IpcWindow _ipc;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ActionServices _services;

    private AppSettings _settings;
    private ActionForm? _actionForm;
    private SettingsForm? _settingsForm;
    private HistoryForm? _historyForm;
    private ModelManagerForm? _modelsForm;
    private readonly HistoryStore _history;
    private readonly Updater _updater;
    private readonly ToolStripMenuItem _updateItem;
    private readonly OnlineUpdater _online;
    /// <summary>What to do when the notification balloon is clicked.</summary>
    private Action? _balloonClick;
    private bool _busy;

    public static string VersionText =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    public TrayApp(bool updated = false)
    {
        _settings = AppSettings.Load(out var settingsError);
        Models.CatalogUpdater.LoadCached();
        _history = new HistoryStore(() => _settings);
        _icon = LoadIcon();

        _ocrServer = new LlamaServer("ocr", _settings.Ocr.Port, BuildOcrLaunch);
        _ocr = new OcrService(_ocrServer, () => _settings.Ocr);
        _textServer = new LlamaServer("text", _settings.TextModel.Port, BuildTextLaunch);
        // One-at-a-time mode: unload one model before loading the other.
        _ocrServer.BeforeStart = () => { if (OneModelAtATime) _textServer.Stop("one model at a time"); };
        _textServer.BeforeStart = () => { if (OneModelAtATime) _ocrServer.Stop("one model at a time"); };
        _translation = new TranslationService(_textServer, () => _settings);
        _grammar = new GrammarService(_textServer, () => _settings);
        _prompts = new PromptService(_textServer, () => _settings);
        _voice = new VoiceService(_textServer, AudioEncoderFile,
            () => ModelCatalog.ByMainFile(_settings.TextModel.ModelFile)?.Name ?? Path.GetFileNameWithoutExtension(_settings.TextModel.ModelFile));
        _services = new ActionServices
        {
            Ocr = RunOcr,
            Translation = _translation,
            Grammar = _grammar,
            Prompts = _prompts,
            CustomActions = () => _settings.CustomActions,
            Replace = ReplaceInSourceAsync,
            AddHistory = AddHistory,
            PreloadText = () => { if (_settings.TextModel.PreloadOnMenu) Preload(_textServer); },
            Dictate = progress => DictateAsync(progress),
            KeepOpen = () => _settings.KeepWindowOpen,
            TranslateDocument = (path, source, target, onParagraph, progress, ct) =>
                Files.OfficeTranslator.TranslateAsync(path, target.Tag,
                    (items, token) => _translation.TranslateBatchAsync(items, source, target, token),
                    onParagraph, progress, ct),
            StopDictation = cancel => { if (cancel) _recording?.Cancel(); else _recording?.Finish(); },
        };
        _ipc = new IpcWindow();
        _ipc.FileReceived += path => SynchronizationContextHolder.Post(() => OpenFileWindow(path));

        _hotkeysMenu = new ToolStripMenuItem("Hotkeys");
        _modelStatusItem = new ToolStripMenuItem("Models: not loaded") { Enabled = false };
        _freeGpuItem = new ToolStripMenuItem("Free GPU memory", null, (_, _) => FreeGpu());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { CheckOnClick = false };
        _updateItem = new ToolStripMenuItem("Restart to update", null, (_, _) => ApplyUpdate())
        {
            Visible = false,
            Font = Theme.UiFont(9.5f, FontStyle.Bold),
        };
        Startup.FixStalePath();
        _menu = BuildMenu();

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = $"n17 YuHelper {VersionText}",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _ = RunAsync(new HotkeyEntry { Name = "Tray icon click", Source = _settings.TrayClickSource, Action = HotkeyAction.Menu });
        };

        _tray.BalloonTipClicked += (_, _) => { var a = _balloonClick; _balloonClick = null; a?.Invoke(); };
        _tray.BalloonTipClosed += (_, _) => _balloonClick = null;

        SynchronizationContextHolder.Context = SynchronizationContext.Current;
        _updater = new Updater();
        _updater.UpdateReady += OnUpdateReady;
        _updater.CheckNow();
        _online = new OnlineUpdater(() => _settings);
        _online.UpdateDownloaded += OnOnlineUpdateDownloaded;

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += entry => _ = RunAsync(entry);

        _trimTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _trimTimer.Tick += (_, _) => { _trimTimer.Stop(); TrimMemory(); };

        // Every 30 seconds check whether it is time to unload the models.
        _idleTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _idleTimer.Tick += (_, _) => UnloadIdleModels();
        _idleTimer.Start();

        Log.Info($"Starting n17 YuHelper {VersionText}");
        Log.Info($"Data folder: {AppPaths.DataDir} ({(AppPaths.IsInstalled ? "installed" : "portable")})");
        Log.Info($"Models folder: {_settings.ModelsDir}");
        Log.Info($"llama-server folder: {_settings.RuntimeDir}");
        ApplyHotkeys(showProblems: true);

        if (settingsError is not null)
            Notify("Error in settings.json", $"Using default settings.\n{settingsError}", ToolTipIcon.Warning);

        if (updated)
            Notify("n17 YuHelper updated", $"Now running version {VersionText}. Click to see what's new.", ToolTipIcon.Info,
                () => OpenUrl(OnlineUpdater.ReleaseUrl(VersionText)));

        // Fresh model catalog (links, tested llama.cpp build) — half a minute later, in the background.
        _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => Models.CatalogUpdater.RefreshAsync()).Unwrap();
        Log.Info($"Model catalog: {Models.ModelCatalog.Source}, llama.cpp {Models.ModelCatalog.LlamaCppTag}");

        // Detect the graphics card early, in the background (needed for the GPU memory mode and the wizard).
        Task.Run(() => GpuInfo.Detect());
        // Model wizard — after the message loop starts.
        SynchronizationContextHolder.Post(MaybeShowWelcome);
        // File from the command line (Explorer "Send to" or drag and drop onto the exe).
        if (Program.StartupFile is { } file) SynchronizationContextHolder.Post(() => OpenFileWindow(file));

        ScheduleTrim();
    }

    // ---------------- Models ----------------

    private ServerLaunch BuildOcrLaunch(int port)
    {
        var s = _settings.Ocr;
        string models = _settings.ModelsDir;
        string exe = Path.Combine(_settings.RuntimeDir, "llama-server.exe");
        string model = Path.Combine(models, s.ModelFile);
        string mmproj = Path.Combine(models, s.MmprojFile);
        string template = Path.Combine(models, s.ChatTemplateFile);
        string logFile = Path.Combine(AppPaths.LogsDir, "llama-ocr.log");

        var args = new List<string>
        {
            "-m", model,
            "--mmproj", mmproj,
            "--host", "127.0.0.1",
            "--port", port.ToString(),
            "-c", s.ContextSize.ToString(),
            "-ngl", s.GpuLayers.ToString(),
            "-np", "1",
            "--temp", "0",
            "--log-file", logFile,
        };
        if (File.Exists(template))
        {
            args.Add("--jinja");
            args.Add("--chat-template-file");
            args.Add(template);
        }
        return new ServerLaunch(exe, args, logFile, [model, mmproj]);
    }

    private ServerLaunch BuildTextLaunch(int port)
    {
        var s = _settings.TextModel;
        string exe = Path.Combine(_settings.RuntimeDir, "llama-server.exe");
        string model = Path.Combine(_settings.ModelsDir, s.ModelFile);
        string logFile = Path.Combine(AppPaths.LogsDir, "llama-text.log");

        var args = new List<string>
        {
            "-m", model,
            "--host", "127.0.0.1",
            "--port", port.ToString(),
            "-c", s.ContextSize.ToString(),
            "-ngl", s.GpuLayers.ToString(),
            "-np", "1",
            "--jinja",
            "--log-file", logFile,
        };
        if (!string.IsNullOrWhiteSpace(s.ExtraArgs))
            args.AddRange(s.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // The model's audio part — for voice input. If an old llama.cpp build does not support it,
        // the server starts without it and translation keeps working.
        if (AudioEncoderFile() is { } mmproj)
        {
            var withAudio = new List<string>(args) { "--mmproj", mmproj };
            return new ServerLaunch(exe, withAudio, logFile, [model], FallbackArgs: args);
        }
        return new ServerLaunch(exe, args, logFile, [model]);
    }

    /// <summary>Audio part (mmproj) of the translation model, if voice input is on and the file is downloaded.</summary>
    private string? AudioEncoderFile()
    {
        if (!_settings.Voice.Enabled) return null;
        var s = _settings.TextModel;
        string? rel = !string.IsNullOrWhiteSpace(s.MmprojFile) ? s.MmprojFile : ModelCatalog.ByMainFile(s.ModelFile)?.Audio?.RelativePath;
        if (rel is null) return null;
        var path = Path.Combine(_settings.ModelsDir, rel);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Whether there is enough GPU memory to keep both models loaded.</summary>
    private bool OneModelAtATime
    {
        get
        {
            switch (_settings.GpuMode)
            {
                case "Both": return false;
                case "OneAtATime": return true;
            }
            var gpu = GpuInfo.Detect();
            if (gpu.Vendor == GpuVendor.None) return false; // they run on the CPU and in system memory
            double need = ModelCatalog.Ocr.VramGb
                          + ModelCatalog.VramNeeded(_settings.ModelsDir, _settings.TextModel.ModelFile, 8)
                          + (AudioEncoderFile() is { } a ? new FileInfo(a).Length / 1024.0 / 1024 / 1024 * 1.3 : 0)
                          + 1.0; // Windows and other apps
            return gpu.VramGb < need;
        }
    }

    /// <summary>Starts loading a model in advance (while the user selects a region or picks an action).</summary>
    private void Preload(LlamaServer server)
    {
        if (server.State is ServerState.Ready or ServerState.Starting) return;
        // In one-at-a-time mode do not evict a model that is busy.
        var other = server == _ocrServer ? _textServer : _ocrServer;
        if (OneModelAtATime && other.State is ServerState.Ready or ServerState.Starting) return;
        server.EnsureReadyAsync().ContinueWith(t =>
        {
            if (t.Exception is not null) Log.Info($"[{server.Name}] preload failed: " + t.Exception.InnerException?.Message);
        }, TaskScheduler.Default);
    }

    private void PreloadOcr()
    {
        if (_settings.Ocr.PreloadOnHotkey) Preload(_ocrServer);
    }

    private Task<string> RunOcr(Bitmap image, IProgress<string> progress, CancellationToken ct, OcrMode mode) =>
        _ocr.RecognizeAsync(image, progress, ct, mode);

    private void UnloadIdleModels()
    {
        TryAutoUpdate();
        int minutes = _settings.ModelIdleUnloadMinutes;
        if (minutes <= 0 || _busy) return;
        foreach (var server in new[] { _ocrServer, _textServer })
            if (server.State == ServerState.Ready && DateTime.UtcNow - server.LastUsedUtc > TimeSpan.FromMinutes(minutes))
                server.Stop($"idle for {minutes} min");

        UnloadForHeavyApps();
    }

    /// <summary>While Maya, Blender, etc. are running, models are not kept in GPU memory for more than a minute of idle time.</summary>
    private void UnloadForHeavyApps()
    {
        if (_busy || _settings.UnloadWhileRunning.Count == 0) return;
        var idle = new[] { _ocrServer, _textServer }
            .Where(x => x.State == ServerState.Ready && DateTime.UtcNow - x.LastUsedUtc > TimeSpan.FromMinutes(1))
            .ToList();
        if (idle.Count == 0) return;

        string? running = null;
        var names = new HashSet<string>(_settings.UnloadWhileRunning, StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (running is null && names.Contains(p.ProcessName)) running = p.ProcessName;
                }
                catch { /* the process has already exited */ }
            }
        }
        if (running is null) return;
        foreach (var server in idle) server.Stop($"{running} is running");
    }

    private void FreeGpu()
    {
        _ocrServer.Stop("Free GPU memory");
        _textServer.Stop("Free GPU memory");
        Notify("GPU memory freed", "Models are unloaded. They will load again when needed.", ToolTipIcon.Info);
    }

    private static string StateText(ServerState s) => s switch
    {
        ServerState.Ready => "loaded",
        ServerState.Starting => "loading...",
        ServerState.Failed => "failed to load (see logs)",
        _ => "not loaded",
    };

    // ---------------- Menu ----------------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"n17 YuHelper {VersionText}") { Enabled = false });
        menu.Items.Add(_updateItem);
        menu.Items.Add("Check for updates...", null, async (_, _) => await CheckForUpdatesManuallyAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Capture region", null, (_, _) =>
            _ = RunAsync(new HotkeyEntry { Name = "Menu: region", Source = InputSource.Region, Action = HotkeyAction.Menu }));
        var ocrMenu = new ToolStripMenuItem("Recognize region to clipboard");
        ocrMenu.DropDownItems.Add("Text", null, (_, _) =>
            _ = RunAsync(new HotkeyEntry { Name = "Menu: OCR", Source = InputSource.Region, Action = HotkeyAction.Ocr, After = AfterAction.Copy }));
        ocrMenu.DropDownItems.Add("Table (for Excel / Word)", null, (_, _) =>
            _ = RunAsync(new HotkeyEntry { Name = "Menu: OCR table", Source = InputSource.Region, Action = HotkeyAction.OcrTable, After = AfterAction.Copy }));
        ocrMenu.DropDownItems.Add("Formula (LaTeX)", null, (_, _) =>
            _ = RunAsync(new HotkeyEntry { Name = "Menu: OCR formula", Source = InputSource.Region, Action = HotkeyAction.OcrFormula, After = AfterAction.Copy }));
        menu.Items.Add(ocrMenu);
        menu.Items.Add("Translate region over the original", null, (_, _) =>
            _ = RunAsync(new HotkeyEntry { Name = "Menu: overlay", Source = InputSource.Region, Action = HotkeyAction.Translate, After = AfterAction.Overlay }));
        menu.Items.Add("Use clipboard", null, (_, _) =>
            _ = RunAsync(new HotkeyEntry { Name = "Menu: clipboard", Source = InputSource.Clipboard, Action = HotkeyAction.Menu }));
        menu.Items.Add("Type text...", null, (_, _) => ShowTypeWindow());
        menu.Items.Add("Dictate...", null, (_, _) =>
            _ = RunAsync(new HotkeyEntry { Name = "Menu: voice", Source = InputSource.Voice, Action = HotkeyAction.Menu }));
        menu.Items.Add("Translate file...", null, (_, _) => ShowOpenFileWindow());
        menu.Items.Add("History", null, (_, _) => ShowHistory());
        _lastWindowItem = new ToolStripMenuItem("Open last window", null, (_, _) => OpenLastWindow());
        menu.Items.Add(_lastWindowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_modelStatusItem);
        menu.Items.Add("Models...", null, (_, _) => ShowModelManager(welcome: false));
        menu.Items.Add(_freeGpuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings...", null, (_, _) => ShowSettings()) { Font = Theme.UiFont(9.5f, FontStyle.Bold) });
        menu.Items.Add(_hotkeysMenu);
        menu.Items.Add(_startupItem);
        var advanced = new ToolStripMenuItem("Advanced");
        advanced.DropDownItems.Add("Open settings.json", null, (_, _) => OpenPath(AppSettings.FilePath));
        advanced.DropDownItems.Add("Reload settings.json", null, (_, _) => ReloadSettings());
        advanced.DropDownItems.Add("Add to Explorer \"Send to\" menu", null, (_, _) => AddToSendTo());
        menu.Items.Add(advanced);
        menu.Items.Add("Open app folder", null, (_, _) => OpenPath(AppPaths.DataDir));
        menu.Items.Add("Open logs folder", null, (_, _) => OpenPath(AppPaths.LogsDir));
        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add("What's new", null, (_, _) => OpenUrl(OnlineUpdater.ReleasesUrl));
        help.DropDownItems.Add("Report a problem...", null, (_, _) => OpenUrl(OnlineUpdater.RepoUrl + "/issues/new?template=bug_report.md"));
        help.DropDownItems.Add("Project page (GitHub)", null, (_, _) => OpenUrl(OnlineUpdater.RepoUrl));
        menu.Items.Add(help);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        Theme.StyleMenu(menu);
        menu.Opening += (_, _) =>
        {
            _modelStatusItem.Text = $"OCR model: {StateText(_ocrServer.State)} · Translation model: {StateText(_textServer.State)}";
            _startupItem.Checked = Startup.IsEnabledForThisExe;
            _startupItem.ToolTipText = Startup.RegisteredPath is { } rp && !Startup.IsEnabledForThisExe
                ? $"Currently starts: {rp}" : null;
            _lastWindowItem!.Enabled = _lastWindow is not null;
            _freeGpuItem.Enabled = new[] { _ocrServer, _textServer }.Any(x => x.State is ServerState.Ready or ServerState.Starting);
        };
        return menu;
    }

    private IReadOnlyList<HotkeyStatus> ApplyHotkeys(bool showProblems)
    {
        var statuses = _hotkeys.RegisterAll(_settings.Hotkeys);

        _hotkeysMenu.DropDownItems.Clear();
        foreach (var s in statuses)
        {
            string mark = s.Registered ? "✓" : "✗";
            string text = $"{mark}  {s.Entry.Keys} — {s.Entry.Name}";
            if (!s.Registered) text += $"  ({s.Problem})";
            _hotkeysMenu.DropDownItems.Add(new ToolStripMenuItem(text) { Enabled = s.Registered });
        }
        if (statuses.Count == 0)
            _hotkeysMenu.DropDownItems.Add(new ToolStripMenuItem("(no hotkeys in settings)") { Enabled = false });
        Theme.StyleMenuItem(_hotkeysMenu);

        var failed = statuses.Where(s => !s.Registered && s.Entry.Enabled).ToList();
        if (showProblems && failed.Count > 0)
        {
            var lines = string.Join("\n", failed.Select(f => $"{f.Entry.Keys}: {f.Problem}"));
            Notify("Some hotkeys could not be registered", lines, ToolTipIcon.Warning);
        }
        return statuses;
    }

    // ---------------- Windows: settings, history, text input ----------------

    private int _hotkeySuspend;

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        var host = new SettingsHost
        {
            Apply = ApplySettings,
            SuspendHotkeys = () => { if (_hotkeySuspend++ == 0) _hotkeys.UnregisterAll(); },
            ResumeHotkeys = () => { if (_hotkeySuspend > 0 && --_hotkeySuspend == 0) ApplyHotkeys(showProblems: false); },
            FreeGpu = FreeGpu,
            OpenModelManager = () => ShowModelManager(welcome: false),
            Icon = _icon,
        };
        _settingsForm = new SettingsForm(_settings, host);
        _settingsForm.FormClosed += (_, _) =>
        {
            _hotkeySuspend = 0;
            ApplyHotkeys(showProblems: false);
            _settingsForm = null;
            ScheduleTrim();
            BeginTryAutoUpdate();
        };
        _settingsForm.Show();
        Native.ForceForeground(_settingsForm.Handle);
    }

    private IReadOnlyList<HotkeyStatus> ApplySettings(AppSettings s, bool startWithWindows)
    {
        bool modelsChanged = _settings.ModelConfigDiffers(s);
        bool historyChanged = _settings.SaveHistory != s.SaveHistory;
        _settings = s;
        _settings.Save();
        Log.Info("Settings applied from the settings window");
        if (historyChanged) _history.SettingsChanged();

        if (modelsChanged)
        {
            // New paths or model apply on the next load.
            _ocrServer.Stop("model settings changed");
            _textServer.Stop("model settings changed");
        }

        try
        {
            if (startWithWindows && !Startup.IsEnabledForThisExe) Startup.Enable();
            else if (!startWithWindows && Startup.IsEnabledForThisExe) Startup.Disable();
        }
        catch (Exception ex)
        {
            Log.Error("Start with Windows", ex);
        }

        _hotkeySuspend = 0;
        return ApplyHotkeys(showProblems: false);
    }

    // ---------------- Updates ----------------

    private bool _updatePending;

    /// <summary>A new version is next to the exe: install it as soon as the app is idle.</summary>
    private void OnUpdateReady()
    {
        _updatePending = true;
        _updateItem.Visible = true;
        TryAutoUpdate();
    }

    private bool IsIdle => !_busy && _recording is null && _actionForm is null && _settingsForm is null && _historyForm is null && _modelsForm is null;

    /// <summary>A new version was downloaded from the internet.</summary>
    private void OnOnlineUpdateDownloaded(string version)
    {
        _updateItem.Text = $"Restart to update to {version}";
        _updateItem.Visible = true;
        if (_settings.InstallUpdatesAutomatically)
        {
            _updatePending = true;
            TryAutoUpdate();
        }
        else
        {
            Notify("Update available", $"n17 YuHelper {version} is downloaded. Click here or use \"Restart to update\" in the tray menu.",
                ToolTipIcon.Info, ApplyUpdate);
        }
    }

    /// <summary>Menu item "Check for updates...".</summary>
    private async Task CheckForUpdatesManuallyAsync()
    {
        if (!_online.Supported)
        {
            Notify("Updates", "This is a portable copy, it does not update itself. Click to open the download page.",
                ToolTipIcon.Info, () => OpenUrl(OnlineUpdater.ReleasesUrl));
            return;
        }
        Notify("Updates", "Checking for updates...", ToolTipIcon.Info);
        string result = await _online.CheckAsync(manual: true);
        if (_online.Ready is not null && !_settings.InstallUpdatesAutomatically)
            Notify("Update available", result + " Click to restart and install it.", ToolTipIcon.Info, ApplyUpdate);
        else if (_online.Ready is null)
            Notify("Updates", result, ToolTipIcon.Info);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Open " + url, ex); }
    }

    private bool HasAnyUpdate => Updater.HasUpdate || _online.Ready is not null;

    private void TryAutoUpdate()
    {
        if (!_updatePending || !IsIdle || !HasAnyUpdate) return;
        _updatePending = false;
        Log.Info("Applying update automatically");
        ApplyUpdate();
    }

    private void ApplyUpdate()
    {
        if (_busy) return;
        // Downloaded from the internet — via Velopack; otherwise — the local file n17YuHelper.update.exe.
        bool started = _online.Ready is not null ? _online.StartApplyAndRestart() : Updater.StartApplyAndRestart();
        if (started) ExitThread();
        else Notify("Update failed", "See logs\\yuhelper.log.", ToolTipIcon.Error);
    }

    private void ShowModelManager(bool welcome)
    {
        if (_modelsForm is { IsDisposed: false })
        {
            _modelsForm.Activate();
            return;
        }
        var host = new ModelManagerHost
        {
            Settings = () => _settings,
            UseTextModel = file =>
            {
                if (string.Equals(_settings.TextModel.ModelFile, file, StringComparison.OrdinalIgnoreCase)) return;
                _settings.TextModel.ModelFile = file;
                _settings.Save();
                _textServer.Stop("translation model changed");
                Log.Info("Translation model: " + file);
            },
            SetModelsFolder = path =>
            {
                _ocrServer.Stop("models folder changed");
                _textServer.Stop("models folder changed");
                _settings.ModelsPath = path;
                _settings.Save();
                Log.Info("Models folder: " + _settings.ModelsDir);
            },
            SetGpuMode = mode =>
            {
                _settings.GpuMode = mode;
                _settings.Save();
            },
            StopModels = () =>
            {
                _ocrServer.Stop("model manager");
                _textServer.Stop("model manager");
            },
            Icon = _icon,
        };
        _modelsForm = new ModelManagerForm(host, welcome);
        _modelsForm.FormClosed += (_, _) => { _modelsForm = null; ScheduleTrim(); BeginTryAutoUpdate(); };
        _modelsForm.Show();
        Native.ForceForeground(_modelsForm.Handle);
    }

    /// <summary>First start or missing files — show the model wizard (once).</summary>
    private void MaybeShowWelcome()
    {
        bool missing = Downloader.InstalledRuntime(_settings.RuntimeDir) is null
                       || !ModelCatalog.Ocr.IsInstalled(_settings.ModelsDir)
                       || !File.Exists(Path.Combine(_settings.ModelsDir, _settings.TextModel.ModelFile));
        if (!missing || _settings.WelcomeShown) return;
        _settings.WelcomeShown = true;
        _settings.Save();
        ShowModelManager(welcome: true);
    }

    private void ShowHistory()
    {
        if (_historyForm is { IsDisposed: false })
        {
            _historyForm.Activate();
            return;
        }
        _historyForm = new HistoryForm(_history, _icon, () => _settings.SaveHistory);
        _historyForm.FormClosed += (_, _) => { _historyForm = null; ScheduleTrim(); BeginTryAutoUpdate(); };
        _historyForm.Show();
        Native.ForceForeground(_historyForm.Handle);
    }

    /// <summary>Empty action window: type or paste text and translate / check it.</summary>
    private void ShowTypeWindow()
    {
        _actionForm?.Close();
        ShowActionForm(new CapturedInput { Text = "" }, null);
    }

    private void ToggleStartup()
    {
        try
        {
            if (Startup.IsEnabledForThisExe)
            {
                Startup.Disable();
                Notify("Start with Windows: off", "n17 YuHelper will not start automatically.", ToolTipIcon.Info);
            }
            else
            {
                Startup.Enable();
                Notify("Start with Windows: on", $"n17 YuHelper will start when you sign in to Windows.\n{Environment.ProcessPath}", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Start with Windows", ex);
            Notify("Could not change autostart", ex.Message, ToolTipIcon.Error);
        }
    }

    private async Task ReplaceInSourceAsync(SourceWindow source, string text)
    {
        if (!await TextReplacer.ReplaceAsync(source, text, _settings))
        {
            try { Clipboard.SetText(text); } catch { /* clipboard is busy */ }
            Notify("Could not paste into the window", "The text is in the clipboard. Paste it with Ctrl+V.", ToolTipIcon.Warning);
        }
    }

    private void ReloadSettings()
    {
        _settings = AppSettings.Load(out var error);
        ApplyHotkeys(showProblems: true);
        // New paths and model parameters apply on the next load.
        _ocrServer.Stop("settings reloaded");
        _textServer.Stop("settings reloaded");
        if (error is not null)
            Notify("Error in settings.json", error, ToolTipIcon.Warning);
        else
            Notify("Settings reloaded", $"Active hotkeys: {_settings.Hotkeys.Count(h => h.Enabled)}", ToolTipIcon.Info);
    }

    // ---------------- Action chain ----------------

    private async Task RunAsync(HotkeyEntry entry)
    {
        // Voice recording in progress: any of our hotkeys (usually the same one) stops it.
        if (_recording is { IsRecording: true } rec)
        {
            rec.Finish();
            return;
        }
        if (_busy) return;
        _busy = true;
        _trimTimer.Stop();
        CapturedInput? input = null;
        // The window where the text was selected — before our windows appear.
        var source = TextReplacer.CaptureForeground();

        try
        {
            // Leave the WM_HOTKEY handler so that the modal frame does not open inside it.
            await Task.Yield();
            Log.Info($"Run: {entry.Name} [{entry.Source} -> {entry.Action} -> {entry.After}]");
            _actionForm?.Close();

            if (entry.Action is HotkeyAction.Translate or HotkeyAction.Grammar or HotkeyAction.Custom || entry.Source == InputSource.Voice)
                Preload(_textServer);

            input = await GetInputAsync(entry, source);
            if (input is null) return;

            if (entry.After == AfterAction.Overlay)
            {
                await OverlayAsync(input, entry);
                input.Dispose();
                return;
            }

            switch (entry.Action)
            {
                case HotkeyAction.Ocr or HotkeyAction.OcrTable or HotkeyAction.OcrFormula when entry.After != AfterAction.ShowWindow:
                    await OcrToClipboardAsync(input, OcrModeOf(entry.Action));
                    input.Dispose();
                    break;

                case HotkeyAction.Ocr or HotkeyAction.OcrTable or HotkeyAction.OcrFormula:
                    _ = ShowActionForm(input, null).RunOcrAsync(OcrModeOf(entry.Action));
                    break;

                case HotkeyAction.Custom:
                {
                    var action = _settings.CustomActions.FirstOrDefault(a => string.Equals(a.Name, entry.CustomAction, StringComparison.OrdinalIgnoreCase));
                    if (action is null)
                    {
                        Notify("Action not found", $"There is no action \"{entry.CustomAction}\". Check Settings > Actions.", ToolTipIcon.Warning);
                        input.Dispose();
                        break;
                    }
                    if (entry.After == AfterAction.ShowWindow)
                        _ = ShowActionForm(input, null).RunCustomAsync(action);
                    else
                    {
                        await CustomDirectAsync(input, action, entry.After);
                        input.Dispose();
                    }
                    break;
                }

                case HotkeyAction.Translate when entry.After != AfterAction.ShowWindow:
                    await TranslateDirectAsync(input, Languages.ByCode(entry.TargetLanguage), entry.After);
                    input.Dispose();
                    break;

                case HotkeyAction.Translate:
                    _ = ShowActionForm(input, null).TranslateAsync(Languages.ByCode(entry.TargetLanguage));
                    break;

                case HotkeyAction.Grammar when entry.After != AfterAction.ShowWindow:
                    await GrammarDirectAsync(input, entry.After);
                    input.Dispose();
                    break;

                case HotkeyAction.Grammar:
                    _ = ShowActionForm(input, null).CheckGrammarAsync();
                    break;

                case HotkeyAction.AsIs when entry.After is AfterAction.Copy or AfterAction.Replace:
                    await AsIsDirectAsync(input, entry.After);
                    input.Dispose();
                    break;

                default:
                    ShowActionForm(input, null);
                    break;
            }
            input = null; // now it is owned by the window or already disposed
        }
        catch (Exception ex)
        {
            Log.Error($"Error in '{entry.Name}'", ex);
            Notify("Error", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            input?.Dispose();
            _busy = false;
            if (_actionForm is null) ScheduleTrim();
            BeginTryAutoUpdate();
        }
    }

    private void BeginTryAutoUpdate()
    {
        if (_updatePending) SynchronizationContextHolder.Post(TryAutoUpdate);
    }

    private async Task<CapturedInput?> GetInputAsync(HotkeyEntry entry, SourceWindow source)
    {
        HotkeyManager.TryParse(entry.Keys, out _, out uint vk, out _);

        switch (entry.Source)
        {
            case InputSource.Auto:
            {
                var text = await SelectionGrabber.TryGetSelectedTextAsync(vk, _settings);
                return text is not null ? new CapturedInput { Text = text, Source = source } : CaptureRegion(entry);
            }
            case InputSource.SelectedText:
            {
                var text = await SelectionGrabber.TryGetSelectedTextAsync(vk, _settings);
                if (text is null)
                    Notify("No text selected", "Select some text and press the hotkey again.", ToolTipIcon.Info);
                return text is null ? null : new CapturedInput { Text = text, Source = source };
            }
            case InputSource.Region:
                return CaptureRegion(entry);

            case InputSource.LastRegion:
                return CaptureLastRegion(entry);

            case InputSource.Voice:
            {
                var text = await DictateAsync(null);
                return string.IsNullOrWhiteSpace(text) ? null : new CapturedInput { Text = text, Source = source };
            }

            case InputSource.Clipboard:
                if (Clipboard.ContainsText()) return new CapturedInput { Text = Clipboard.GetText() };
                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
                {
                    var bmp = new Bitmap(img);
                    img.Dispose();
                    return new CapturedInput { Image = bmp };
                }
                Notify("Clipboard is empty", "The clipboard has no text or image.", ToolTipIcon.Info);
                return null;
        }
        return null;
    }

    private CapturedInput? CaptureRegion(HotkeyEntry entry)
    {
        // Almost everything done with an image starts with recognition — preload the model.
        PreloadOcr();

        var result = RegionCaptureForm.Run(_settings);
        if (result is null) return null;
        RememberRegion(result.ScreenRect);
        return new CapturedInput { Image = result.Image, Region = result.ScreenRect };
    }

    // ---------------- Last region and overlay window ----------------

    private readonly List<OverlayForm> _overlays = [];

    private void RememberRegion(Rectangle r)
    {
        _settings.LastRegion = $"{r.X},{r.Y},{r.Width},{r.Height}";
        _settings.Save();
    }

    private Rectangle? LastRegion()
    {
        var p = _settings.LastRegion.Split(',');
        if (p.Length == 4 && p.All(x => int.TryParse(x, out _)))
        {
            var r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
            if (r.Width > 2 && r.Height > 2 && SystemInformation.VirtualScreen.IntersectsWith(r)) return r;
        }
        return null;
    }

    /// <summary>The same region as last time, without the frame. If there was none yet — the normal frame.</summary>
    private CapturedInput? CaptureLastRegion(HotkeyEntry entry)
    {
        if (LastRegion() is not { } r) return CaptureRegion(entry);
        PreloadOcr();
        return new CapturedInput { Image = RegionCaptureForm.CaptureRect(r), Region = r };
    }

    /// <summary>What to do with the recognized text for this action (for the overlay window).</summary>
    private Func<string, CancellationToken, Task<string>> ProcessorFor(HotkeyEntry entry)
    {
        switch (entry.Action)
        {
            case HotkeyAction.Translate:
            {
                var forced = Languages.ByCode(entry.TargetLanguage);
                return (text, ct) =>
                {
                    var src = Languages.Detect(text);
                    return _translation.TranslateAsync(text, src, forced ?? _translation.DefaultTarget(src), null, null, ct);
                };
            }
            case HotkeyAction.Grammar:
                return async (text, ct) => (await _grammar.CheckAsync(text, null, ct)).Corrected;
            case HotkeyAction.Custom when _settings.CustomActions.FirstOrDefault(a => string.Equals(a.Name, entry.CustomAction, StringComparison.OrdinalIgnoreCase)) is { } action:
                return (text, ct) => _prompts.RunAsync(action, text, null, null, ct);
            default:
                return (text, _) => Task.FromResult(text);
        }
    }

    /// <summary>Result over the selected region. Live mode: redo when the region changes.</summary>
    private async Task OverlayAsync(CapturedInput input, HotkeyEntry entry)
    {
        var mode = OcrModeOf(entry.Action);
        var process = ProcessorFor(entry);
        var toast = new ProgressToast("Preparing...");
        toast.ShowNearCursor();
        try
        {
            var progress = new Progress<string>(toast.SetText);
            string text = input.Text ?? await _ocr.RecognizeAsync(input.Image!, progress, CancellationToken.None, mode);
            if (string.IsNullOrWhiteSpace(text)) { toast.CloseAfter("No text found", 1500); return; }
            toast.SetText("Working...");
            string result = await process(text, CancellationToken.None);
            toast.Close();
            AddHistory(entry.Action.ToString(), text, result);

            Func<CancellationToken, Task<string?>>? refresh = null;
            if (input.Region is { } region)
            {
                ulong lastSig = ImageSignature(input.Image!);
                string lastText = text;
                refresh = async ct =>
                {
                    using var shot = RegionCaptureForm.CaptureRect(region);
                    ulong sig = ImageSignature(shot);
                    if (sig == lastSig) return null;
                    lastSig = sig;
                    var t = await _ocr.RecognizeAsync(shot, null, ct, mode);
                    if (string.IsNullOrWhiteSpace(t) || t == lastText) return null;
                    lastText = t;
                    var r = await process(t, ct);
                    AddHistory(entry.Action.ToString(), t, r);
                    return r;
                };
            }

            var bounds = input.Region ?? new Rectangle(Cursor.Position.X + 16, Cursor.Position.Y + 16, 420, 140);
            if (bounds.Height < 40) bounds.Height = 40;
            if (bounds.Width < 160) bounds.Width = 160;
            var overlay = new OverlayForm(bounds, result, refresh, _settings.LiveIntervalMs, startLive: false);
            overlay.FormClosed += (_, _) => { _overlays.Remove(overlay); ScheduleTrim(); };
            _overlays.Add(overlay);
            overlay.Show();
        }
        catch (ModelSetupException ex)
        {
            toast.Close();
            Notify("Model is not set up", ex.Message + "\nOpen the tray menu > Models... to download it.", ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            toast.Close();
            Log.Error("Overlay", ex);
            Notify("Failed", ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>A quick image "fingerprint" to tell whether the region changed.</summary>
    private static unsafe ulong ImageSignature(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            ulong h = 1469598103934665603UL;
            for (int y = 0; y < bmp.Height; y += 2)
            {
                byte* row = (byte*)data.Scan0 + (long)y * data.Stride;
                for (int x = 0; x < bmp.Width * 3; x += 6)
                {
                    h ^= (ulong)(row[x] >> 3); // small noise does not count as a change
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static OcrMode OcrModeOf(HotkeyAction a) =>
        a switch { HotkeyAction.OcrTable => OcrMode.Table, HotkeyAction.OcrFormula => OcrMode.Formula, _ => OcrMode.Text };

    /// <summary>Recognize and copy right away, with a hint at the cursor.</summary>
    private async Task OcrToClipboardAsync(CapturedInput input, OcrMode mode = OcrMode.Text)
    {
        if (input.IsText)
        {
            Clipboard.SetText(input.Text!);
            ShowToast("Text copied", 1200);
            return;
        }

        var toast = new ProgressToast(_ocrServer.State == ServerState.Ready ? "Recognizing text..." : "Loading OCR model...");
        toast.ShowNearCursor();
        try
        {
            var progress = new Progress<string>(toast.SetText);
            var text = await _ocr.RecognizeAsync(input.Image!, progress, CancellationToken.None, mode);
            if (string.IsNullOrWhiteSpace(text))
            {
                toast.CloseAfter("No text found", 1500);
                return;
            }
            AddHistory(mode == OcrMode.Text ? "OCR" : $"OCR {mode}", "(image)", text);
            if (mode == OcrMode.Table)
            {
                Clipboard.SetDataObject(TableFormat.ToClipboard(text), true);
                int rows = text.Split('\n').Length;
                toast.CloseAfter($"Table copied ({rows} rows): paste into Excel or Word", 2200);
                return;
            }
            Clipboard.SetText(text);
            var preview = text.Replace("\r", "").Replace('\n', ' ');
            if (preview.Length > 60) preview = preview[..60] + "...";
            toast.CloseAfter(mode == OcrMode.Formula ? $"LaTeX copied: {preview}" : $"Copied {text.Length} characters: {preview}", 2000);
        }
        catch (ModelSetupException ex)
        {
            toast.Close();
            Notify("Model is not set up", ex.Message + "\nOpen the tray menu > Models... to download it.", ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            toast.Close();
            Log.Error("OCR to clipboard", ex);
            Notify("OCR failed", ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>Custom action without a window: replace the selected text or copy the result right away.</summary>
    private async Task CustomDirectAsync(CapturedInput input, CustomAction action, AfterAction after)
    {
        var toast = new ProgressToast("Preparing...");
        toast.ShowNearCursor();
        try
        {
            var progress = new Progress<string>(toast.SetText);
            string? text = input.Text;
            if (text is null)
            {
                text = await _ocr.RecognizeAsync(input.Image!, progress, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(text)) { toast.CloseAfter("No text found", 1500); return; }
            }
            var result = await _prompts.RunAsync(action, text, null, progress, CancellationToken.None);
            AddHistory(action.Name, text, result);
            if (after == AfterAction.Replace && input.Source is not null)
            {
                toast.Close();
                await ReplaceInSourceAsync(input.Source, result);
                ShowToast($"{action.Name}: replaced", 1500);
                return;
            }
            Clipboard.SetText(result);
            toast.CloseAfter($"{action.Name}: result copied", 1800);
        }
        catch (ModelSetupException ex)
        {
            toast.Close();
            Notify("Model is not set up", ex.Message + "\nOpen the tray menu > Models... to download it.", ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            toast.Close();
            Log.Error($"Action '{action.Name}' direct", ex);
            Notify(action.Name + " failed", ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>Translate without a window: replace the selected text or copy the translation right away.</summary>
    private async Task TranslateDirectAsync(CapturedInput input, Language? forcedTarget, AfterAction after)
    {
        var toast = new ProgressToast("Preparing...");
        toast.ShowNearCursor();
        try
        {
            var progress = new Progress<string>(toast.SetText);
            string? text = input.Text;
            if (text is null)
            {
                text = await _ocr.RecognizeAsync(input.Image!, progress, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(text))
                {
                    toast.CloseAfter("No text found", 1500);
                    return;
                }
            }
            var source = Languages.Detect(text);
            var target = forcedTarget ?? _translation.DefaultTarget(source);
            var result = await _translation.TranslateAsync(text, source, target, null, progress, CancellationToken.None);
            AddHistory($"Translate {source.Code}→{target.Code}", text, result);
            if (after == AfterAction.Replace && input.Source is not null)
            {
                toast.Close();
                await ReplaceInSourceAsync(input.Source, result);
                ShowToast($"Replaced with {target.Name} translation", 1500);
                return;
            }
            Clipboard.SetText(result);
            var preview = result.Replace("\r", "").Replace('\n', ' ');
            if (preview.Length > 60) preview = preview[..60] + "...";
            toast.CloseAfter($"Copied {target.Name} translation: {preview}", 2500);
        }
        catch (ModelSetupException ex)
        {
            toast.Close();
            Notify("Model is not set up", ex.Message + "\nOpen the tray menu > Models... to download it.", ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            toast.Close();
            Log.Error("Translate to clipboard", ex);
            Notify("Translation failed", ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>Check grammar without a window: replace the selected text or copy the corrected one right away.</summary>
    private async Task GrammarDirectAsync(CapturedInput input, AfterAction after)
    {
        var toast = new ProgressToast("Preparing...");
        toast.ShowNearCursor();
        try
        {
            var progress = new Progress<string>(toast.SetText);
            string? text = input.Text;
            if (text is null)
            {
                text = await _ocr.RecognizeAsync(input.Image!, progress, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(text))
                {
                    toast.CloseAfter("No text found", 1500);
                    return;
                }
            }

            var result = await _grammar.CheckAsync(text, progress, CancellationToken.None);
            if (!result.HasChanges)
            {
                toast.CloseAfter($"{result.Language.Name}: no errors found", 1800);
                return;
            }

            int n = Math.Max(result.Fixes.Count, 1);
            string fixes = $"{n} fix{(n == 1 ? "" : "es")}";
            AddHistory("Grammar", text, result.Corrected);
            if (after == AfterAction.Replace && input.Source is not null)
            {
                toast.Close();
                await ReplaceInSourceAsync(input.Source, result.Corrected);
                ShowToast($"Corrected: {fixes}", 1800);
            }
            else
            {
                Clipboard.SetText(result.Corrected);
                toast.CloseAfter($"Corrected text copied ({fixes})", 2000);
            }
        }
        catch (ModelSetupException ex)
        {
            toast.Close();
            Notify("Model is not set up", ex.Message + "\nOpen the tray menu > Models... to download it.", ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            toast.Close();
            Log.Error("Grammar direct", ex);
            Notify("Grammar check failed", ex.Message, ToolTipIcon.Error);
        }
    }

    // ---------------- Voice ----------------

    /// <summary>
    /// Voice recording and recognition. progress == null — hints at the cursor (hotkey),
    /// otherwise — in the action window status line. Null — cancelled or nothing was said.
    /// </summary>
    private async Task<string?> DictateAsync(IProgress<string>? progress)
    {
        if (_recording is not null) return null;
        if (!_voice.IsAvailable)
        {
            var model = ModelCatalog.ByMainFile(_settings.TextModel.ModelFile);
            string how = !_settings.Voice.Enabled ? "Turn on voice input in Settings > General."
                : model?.HasAudio == true ? $"Open Models... and click \"Add voice\" for {model.Name} ({ModelCatalog.FormatSize(model.Audio!.Size)})."
                : "Choose Gemma 4 E2B, E4B or 12B in Models...: the current model does not understand speech.";
            if (progress is not null) progress.Report("Voice input is not set up. " + how);
            else Notify("Voice input is not set up", how, ToolTipIcon.Info);
            return null;
        }

        Preload(_textServer); // the model loads while the person is speaking
        HotkeyEntry? key = _settings.Hotkeys.FirstOrDefault(h => h.Enabled && h.Source == InputSource.Voice);
        string hint = progress is null && key is not null
            ? $"{key.Keys} again or pause to finish · Esc cancel"
            : "Click ■ or pause to finish · Esc cancel";
        if (_settings.Voice.AutoStopSilenceSeconds <= 0) hint = hint.Replace(" or pause", "");

        var recorder = new RecordingToast(_settings.Voice, hint);
        _recording = recorder;
        byte[]? pcm;
        try
        {
            pcm = await recorder.RecordAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Microphone", ex);
            if (progress is not null) progress.Report(ex.Message);
            else Notify("Microphone", ex.Message, ToolTipIcon.Warning);
            return null;
        }
        finally
        {
            _recording = null;
            if (!recorder.IsDisposed)
            {
                recorder.Close();
                recorder.Dispose();
            }
        }
        if (pcm is null) return null; // cancelled

        ProgressToast? toast = null;
        if (progress is null)
        {
            toast = new ProgressToast(_textServer.State == ServerState.Ready ? "Recognizing speech..." : "Loading the model...");
            toast.ShowNearCursor();
            progress = new Progress<string>(toast.SetText);
        }
        try
        {
            var text = await _voice.TranscribeAsync(pcm, progress, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (toast is not null) toast.CloseAfter("No speech recognized", 1500);
                else progress.Report("No speech recognized.");
                return null;
            }
            toast?.Close();
            AddHistory("Voice", "(speech)", text);
            return text;
        }
        catch (ModelSetupException ex)
        {
            toast?.Close();
            if (toast is null) progress.Report(ex.Message);
            else Notify("Voice input is not set up", ex.Message, ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            toast?.Close();
            Log.Error("Speech recognition", ex);
            if (toast is null) progress.Report("Speech recognition failed: " + ex.Message);
            else Notify("Speech recognition failed", ex.Message, ToolTipIcon.Error);
        }
        return null;
    }

    /// <summary>Text without processing: type it into the app (dictation) or copy it.</summary>
    private async Task AsIsDirectAsync(CapturedInput input, AfterAction after)
    {
        string? text = input.Text;
        if (text is null)
        {
            var toast = new ProgressToast("Recognizing text...");
            toast.ShowNearCursor();
            try
            {
                text = await _ocr.RecognizeAsync(input.Image!, new Progress<string>(toast.SetText), CancellationToken.None);
            }
            finally
            {
                toast.Close();
            }
            if (string.IsNullOrWhiteSpace(text)) { ShowToast("No text found", 1500); return; }
        }
        if (after == AfterAction.Replace && input.Source is not null)
        {
            await ReplaceInSourceAsync(input.Source, text);
            return;
        }
        Clipboard.SetText(text);
        var preview = text.Replace("\r", "").Replace('\n', ' ');
        if (preview.Length > 60) preview = preview[..60] + "...";
        ShowToast($"Copied: {preview}", 1800);
    }

    // ---------------- Files ----------------

    /// <summary>File translation window: pick a file in the dialog.</summary>
    private void ShowOpenFileWindow()
    {
        using var dlg = new OpenFileDialog { Title = "Translate a file", Filter = Files.DocumentReader.OpenFilter };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        OpenFileWindow(dlg.FileName);
    }

    /// <summary>Opens a file in the action window and translates it right away.</summary>
    private void OpenFileWindow(string path)
    {
        Log.Info("Open file: " + path);
        _actionForm?.Close();
        var form = ShowActionForm(new CapturedInput { Text = "" }, "Reading the file...");
        form.Pinned = true;
        _ = form.LoadFileAsync(path, translate: true);
    }

    /// <summary>Shortcut in the Explorer "Send to" menu: right-click a file → Send to → n17 YuHelper.</summary>
    private void AddToSendTo()
    {
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            string lnk = Path.Combine(dir, "n17 YuHelper (translate).lnk");
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available.");
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = Environment.ProcessPath;
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.Description = "Translate the file with n17 YuHelper";
            shortcut.Save();
            Notify("Added to \"Send to\"", "Right-click a file in Explorer > Send to > n17 YuHelper (translate).", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Error("Send to shortcut", ex);
            Notify("Could not add to \"Send to\"", ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>In-memory history: very long texts (files) are truncated.</summary>
    private void AddHistory(string kind, string source, string result)
    {
        const int max = 20_000;
        static string Cut(string t) => t.Length > max ? t[..max] + "\n…" : t;
        _history.Add(kind, Cut(source), Cut(result));
    }

    private static void ShowToast(string text, int ms)
    {
        var t = new ProgressToast(text);
        t.ShowNearCursor();
        t.CloseAfter(text, ms);
    }

    private ActionForm ShowActionForm(CapturedInput input, string? note, WindowSnapshot? restore = null)
    {
        var form = new ActionForm(input, _services, note);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_actionForm, form)) _actionForm = null;
            // Remember what was in the window (tray menu → Open last window).
            if (form.Snapshot is { } snap)
            {
                _lastWindow?.Dispose();
                _lastWindow = snap;
            }
            form.Dispose();
            ScheduleTrim();
            BeginTryAutoUpdate();
        };
        _actionForm = form;
        if (restore is not null)
        {
            form.Restore(restore);
            form.ShowAt(restore.Bounds);
        }
        else form.ShowNearCursor();
        return form;
    }

    private WindowSnapshot? _lastWindow;

    /// <summary>Opens the action window the way it was closed last time.</summary>
    private void OpenLastWindow()
    {
        if (_lastWindow is not { } snap)
        {
            ShowToast("No window to restore yet", 1500);
            return;
        }
        if (_actionForm is { IsDisposed: false } open)
        {
            // The window is open anyway — just show it.
            Native.ForceForeground(open.Handle);
            return;
        }
        var input = snap.Text is not null
            ? new CapturedInput { Text = snap.Text, Source = snap.Source }
            : new CapturedInput { Image = (Bitmap)snap.Image!.Clone(), Source = snap.Source };
        ShowActionForm(input, null, snap);
    }

    // ---------------- Housekeeping ----------------

    private void Notify(string title, string text, ToolTipIcon icon, Action? onClick = null)
    {
        _balloonClick = onClick;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = string.IsNullOrEmpty(text) ? " " : text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(3000);
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Path.HasExtension(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open {path}", ex);
        }
    }

    private void ScheduleTrim()
    {
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    /// <summary>Returns spare memory to the system while the app just sits in the tray.</summary>
    private void TrimMemory()
    {
        if (_busy || _actionForm is not null || _settingsForm is not null || _historyForm is not null || _modelsForm is not null) return;
        _history.Unload();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using var p = Process.GetCurrentProcess();
        Native.EmptyWorkingSet(p.Handle);
    }

    private static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
        return stream is not null ? new Icon(stream, SystemInformation.SmallIconSize) : SystemIcons.Application;
    }

    protected override void ExitThreadCore()
    {
        Log.Info("Exit");
        _actionForm?.Close();
        _settingsForm?.Close();
        _historyForm?.Close();
        _modelsForm?.Close();
        foreach (var o in _overlays.ToList()) o.Close();
        _recording?.Cancel();
        _ipc.Dispose();
        _hotkeys.Dispose();
        _updater.Dispose();
        _online.Dispose();
        _trimTimer.Dispose();
        _idleTimer.Dispose();
        _ocrServer.Dispose();
        _textServer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
