using N17YuHelper.Translate;

namespace N17YuHelper.UI;

/// <summary>Что окно настроек просит у программы.</summary>
public sealed class SettingsHost
{
    /// <summary>Применить настройки. Возвращает результат регистрации горячих клавиш.</summary>
    public required Func<AppSettings, bool, IReadOnlyList<HotkeyStatus>> Apply { get; init; }
    /// <summary>Отключить глобальные клавиши, пока вводится новая комбинация.</summary>
    public required Action SuspendHotkeys { get; init; }
    public required Action ResumeHotkeys { get; init; }
    public required Action FreeGpu { get; init; }
    public required Action OpenModelManager { get; init; }
    public required Icon Icon { get; init; }
}

/// <summary>Окно настроек: горячие клавиши (как в ShareX), общие, модели, перевод.</summary>
internal sealed class SettingsForm : Form
{
    private readonly SettingsHost _host;
    private readonly AppSettings _s;   // рабочая копия
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000 };

    // Hotkeys
    private readonly FlowLayoutPanel _rows;
    private readonly List<HotkeyRow> _hotkeyRows = [];

    // General
    private CheckBox _startup = null!;
    private ComboBox _theme = null!;
    private TextBox _unloadApps = null!;
    private ComboBox _trayClick = null!;
    private CheckBox _snap = null!;
    private CheckBox _magnifier = null!;
    private NumericUpDown _copyTimeout = null!;
    private TextBox _neverCopy = null!;
    private TextBox _ctrlInsert = null!;

    // Models
    private TextBox _modelsPath = null!;
    private TextBox _runtimePath = null!;
    private NumericUpDown _unloadMinutes = null!;
    private CheckBox _preloadOcr = null!;
    private CheckBox _preloadText = null!;
    private ComboBox _textModel = null!;
    private NumericUpDown _temperature = null!;
    private CheckBox _upscale = null!;
    private CheckBox _voiceEnabled = null!;
    private NumericUpDown _voiceSilence = null!;
    private ComboBox _microphone = null!;
    private CheckBox _saveHistory = null!;
    private CheckBox _keepOpen = null!;

    // Translation
    private readonly Dictionary<string, ComboBox> _defaultTargets = [];
    private ModernButton _forgetChoices = null!;
    private TextBox _glossary = null!;

    // Actions
    private readonly List<(TextBox Name, TextBox Prompt, Control Row)> _actionRows = [];
    private FlowLayoutPanel _actionList = null!;

    public SettingsForm(AppSettings current, SettingsHost host)
    {
        _host = host;
        _s = current.Clone();

        Text = "n17 YuHelper - Settings";
        Icon = host.Icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = Theme.UiFont(9.5f);
        ClientSize = new Size(1080, 600);
        MinimumSize = new Size(760, 420);
        KeyPreview = true;

        _rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4),
        };
        Panel[] pages = [BuildHotkeysTab(), BuildGeneralTab(), BuildModelsTab(), BuildTranslationTab(), BuildActionsTab()];

        // Слева — навигация, справа — заголовок и содержимое раздела.
        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 190,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10, 14, 6, 10),
            BackColor = Theme.Bg,
        };
        var appTitle = new Label
        {
            Text = "n17 YuHelper",
            AutoSize = true,
            Font = Theme.UiFont(11f, FontStyle.Bold),
            Margin = new Padding(10, 0, 0, 14),
        };
        nav.Controls.Add(appTitle);

        var pageTitle = new Label { Dock = DockStyle.Top, Height = 46, Font = Theme.UiFont(15f, FontStyle.Bold), Padding = new Padding(12, 10, 0, 0) };
        var pageHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 12, 0) };
        var navButtons = new List<ModernButton>();
        string[] glyphs = ["\uE765", "\uE713", "\uE8F1", "\uE774", "\uE70F"];
        for (int pi = 0; pi < pages.Length; pi++)
        {
            var page = pages[pi];
            var b = new ModernButton
            {
                Glyph = glyphs[pi],
                Text = page.Text,
                Kind = ButtonKind.Ghost,
                AlignLeft = true,
                AutoFit = false,
                Width = 168,
                Height = 36,
                Margin = new Padding(0, 0, 0, 2),
            };
            b.Click += (_, _) =>
            {
                pageHost.Controls.Clear();
                pageHost.Controls.Add(page);
                pageTitle.Text = page.Text;
                foreach (var nb in navButtons) nb.Selected = nb == b;
            };
            navButtons.Add(b);
            nav.Controls.Add(b);
        }
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(pageHost);
        right.Controls.Add(pageTitle);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        var cancel = new ModernButton { Text = "Cancel", AutoFit = false, Width = 100 };
        cancel.Click += (_, _) => Close();
        var apply = new ModernButton { Text = "Apply", AutoFit = false, Width = 100 };
        apply.Click += (_, _) => DoApply();
        var ok = new ModernButton { Text = "OK", Kind = ButtonKind.Accent, AutoFit = false, Width = 100 };
        ok.Click += (_, _) => { if (DoApply()) Close(); };
        bottom.Controls.AddRange([cancel, apply, ok]);

        Controls.Add(right);
        Controls.Add(nav);
        Controls.Add(bottom);

        Theme.ApplyWindow(this);
        Theme.ApplyTo(this);
        foreach (var pg in pages)
        {
            pg.BackColor = Theme.Bg;
            Theme.ApplyTo(pg);
        }
        navButtons[0].PerformClickSafe();
    }

    // =============================================================== Hotkeys

    private Panel BuildHotkeysTab()
    {
        var page = new Panel { Text = "Hotkeys", Dock = DockStyle.Fill };

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(6, 6, 6, 0) };
        header.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Tag = "dim",
            Text = "Click a hotkey field and press the combination. Backspace clears it. " +
                   "Source \"Auto\" uses the selected text if there is any, otherwise shows the region frame. " +
                   "Source \"Voice\" records speech until you press the hotkey again or pause.",
        });
        header.Controls.Add(HotkeyRow.HeaderRow());

        foreach (var h in _s.Hotkeys) AddRow(h);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        var add = new ModernButton { Text = "Add hotkey", Kind = ButtonKind.Accent };
        add.Click += (_, _) =>
        {
            var row = AddRow(new HotkeyEntry { Name = "New hotkey", Keys = "", Source = InputSource.Auto, Action = HotkeyAction.Menu });
            _rows.ScrollControlIntoView(row);
        };
        var reset = new ModernButton { Text = "Reset to defaults" };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Replace all hotkeys with the default set?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            foreach (var r in _hotkeyRows.ToList()) RemoveRow(r);
            foreach (var h in AppSettings.DefaultHotkeys()) AddRow(h);
        };
        buttons.Controls.AddRange([add, reset]);

        page.Controls.Add(_rows);
        page.Controls.Add(header);
        page.Controls.Add(buttons);
        return page;
    }

    private HotkeyRow AddRow(HotkeyEntry entry)
    {
        var row = new HotkeyRow(entry, _tips, _s.CustomActions.Select(a => a.Name).Where(n => n.Length > 0));
        row.RemoveRequested += () => RemoveRow(row);
        row.KeysBox.CaptureStarted += _host.SuspendHotkeys;
        row.KeysBox.CaptureEnded += _host.ResumeHotkeys;
        _hotkeyRows.Add(row);
        _rows.Controls.Add(row);
        row.BackColor = Theme.Bg;
        Theme.ApplyTo(row);
        return row;
    }

    private void RemoveRow(HotkeyRow row)
    {
        _hotkeyRows.Remove(row);
        _rows.Controls.Remove(row);
        row.Dispose();
    }

    // =============================================================== General

    private Panel BuildGeneralTab()
    {
        var page = new Panel { Text = "General", Dock = DockStyle.Fill };
        var t = NewGrid();

        _startup = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = Startup.IsEnabledForThisExe };
        Tip(_startup, "Starts this exe when you sign in to Windows.\n" + Environment.ProcessPath);
        AddFull(t, _startup);

        _theme = Combo(["System", "Light", "Dark"]);
        _theme.SelectedIndex = _s.Theme.ToLowerInvariant() switch { "light" => 1, "dark" => 2, _ => 0 };
        Tip(_theme, "Takes effect after restarting the app.");
        AddRow(t, "Theme (after restart):", _theme);

        _trayClick = Combo(["Capture region", "Auto (selected text or region)", "Use clipboard"]);
        _trayClick.SelectedIndex = _s.TrayClickSource switch { InputSource.Auto => 1, InputSource.Clipboard => 2, _ => 0 };
        AddRow(t, "Tray icon left click:", _trayClick);

        _keepOpen = new CheckBox { Text = "Keep window open (don't close the action window when clicking elsewhere)", AutoSize = true, Checked = _s.KeepWindowOpen };
        Tip(_keepOpen, "Same as the pin button in the action window, for every new window. Close it with Esc or the × button.");
        AddFull(t, _keepOpen);

        _snap = new CheckBox { Text = "Snap the region frame to windows and controls", AutoSize = true, Checked = _s.SnapToWindows };
        AddFull(t, _snap);
        _magnifier = new CheckBox { Text = "Show magnifier in the region frame", AutoSize = true, Checked = _s.ShowMagnifier };
        AddFull(t, _magnifier);

        _copyTimeout = new NumericUpDown { Minimum = 50, Maximum = 2000, Increment = 50, Value = Math.Clamp(_s.SelectionCopyTimeoutMs, 50, 2000), Width = 80 };
        Tip(_copyTimeout, "How long to wait for an app to copy the selected text. Increase it if selected text is sometimes missed.");
        AddRow(t, "Wait for selected text (ms):", _copyTimeout);

        _neverCopy = ListBox(_s.NeverCopyProcesses);
        Tip(_neverCopy, "In these apps the selected text is never taken: the region frame opens right away.\nOne process name per line, without .exe.");
        AddRow(t, "Never take selected text in:", _neverCopy);

        _voiceEnabled = new CheckBox { Text = "Voice input (loads the speech part of the translation model, ~0.2–0.6 GB)", AutoSize = true, Checked = _s.Voice.Enabled };
        Tip(_voiceEnabled, "Dictate with Ctrl+Shift+Win+V (translate) or Ctrl+Shift+Win+D (type into the app).\nWorks with Gemma 4 E2B, E4B and 12B. Download the voice part in Models...");
        AddFull(t, _voiceEnabled);
        _voiceSilence = new NumericUpDown { Minimum = 0, Maximum = 10, Increment = 0.5m, DecimalPlaces = 1, Width = 80, Value = (decimal)Math.Clamp(_s.Voice.AutoStopSilenceSeconds, 0, 10) };
        Tip(_voiceSilence, "Recording stops by itself after this many seconds of silence. 0 = stop only with the hotkey or the stop button.");
        AddRow(t, "Stop dictation after silence (s):", _voiceSilence);
        _microphone = Combo(["Default (Windows)", .. Voice.AudioRecorder.DeviceNames()]);
        _microphone.SelectedIndex = Math.Max(0, _microphone.Items.IndexOf(_s.Voice.Microphone));
        if (_s.Voice.Microphone.Length > 0 && _microphone.SelectedIndex == 0)
        {
            _microphone.Items.Add(_s.Voice.Microphone + " (not connected)");
            _microphone.SelectedIndex = _microphone.Items.Count - 1;
        }
        Tip(_microphone, "Microphone for voice input. Default follows the Windows setting.");
        AddRow(t, "Microphone:", _microphone);

        _saveHistory = new CheckBox { Text = "Keep history after restart (history.json next to the app)", AutoSize = true, Checked = _s.SaveHistory };
        Tip(_saveHistory, $"Saves the last {_s.HistoryLimit} results: what you translated, recognized and dictated. Off = history is cleared when the app exits.");
        AddFull(t, _saveHistory);

        _ctrlInsert = ListBox(_s.CopyWithCtrlInsertProcesses);
        Tip(_ctrlInsert, "Terminals: copy with Ctrl+Insert and paste with Shift+Insert, because Ctrl+C would stop a running command.\nOne process name per line, without .exe.");
        AddRow(t, "Terminals (Ctrl+Insert):", _ctrlInsert);

        page.Controls.Add(t);
        return page;
    }

    // =============================================================== Models

    private Panel BuildModelsTab()
    {
        var page = new Panel { Text = "Models", Dock = DockStyle.Fill };
        var t = NewGrid();

        _modelsPath = new TextBox { Text = _s.ModelsPath, PlaceholderText = "auto: " + _s.ModelsDir, Width = 520 };
        AddRow(t, "Models folder:", WithBrowse(_modelsPath));
        _runtimePath = new TextBox { Text = _s.RuntimePath, PlaceholderText = "auto: " + _s.RuntimeDir, Width = 520 };
        AddRow(t, "llama-server folder:", WithBrowse(_runtimePath));

        _textModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 520 };
        foreach (var f in FindModels(_s.ModelsDir)) _textModel.Items.Add(f);
        _textModel.Text = _s.TextModel.ModelFile;
        Tip(_textModel, "Model for translation and grammar check (path inside the models folder).\nModels found in models\\llm are listed.");
        AddRow(t, "Translation model:", _textModel);

        _temperature = new NumericUpDown { Minimum = 0, Maximum = 1.5m, Increment = 0.1m, DecimalPlaces = 1, Width = 80, Value = (decimal)Math.Clamp(_s.TextModel.Temperature, 0, 1.5) };
        Tip(_temperature, "Lower = more literal and stable translation. 0.3 is a good default.");
        AddRow(t, "Translation temperature:", _temperature);

        _unloadMinutes = new NumericUpDown { Minimum = 0, Maximum = 600, Width = 80, Value = Math.Clamp(_s.ModelIdleUnloadMinutes, 0, 600) };
        Tip(_unloadMinutes, "Unload models from GPU memory after this many idle minutes. 0 = keep them loaded.");
        AddRow(t, "Unload models after (min):", _unloadMinutes);

        _unloadApps = ListBox(_s.UnloadWhileRunning);
        Tip(_unloadApps, "While one of these apps is running, models are unloaded after a minute of inactivity to free GPU memory.\nOne process name per line, without .exe.");
        AddRow(t, "Free GPU memory while running:", _unloadApps);

        _preloadOcr = new CheckBox { Text = "Start loading the OCR model as soon as a hotkey is pressed", AutoSize = true, Checked = _s.Ocr.PreloadOnHotkey };
        AddFull(t, _preloadOcr);
        _preloadText = new CheckBox { Text = "Start loading the translation model when the action window opens", AutoSize = true, Checked = _s.TextModel.PreloadOnMenu };
        AddFull(t, _preloadText);
        _upscale = new CheckBox { Text = "Enlarge small regions before OCR (better for small UI text)", AutoSize = true, Checked = _s.Ocr.UpscaleSmallImages };
        AddFull(t, _upscale);

        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        var free = new ModernButton { Text = "Free GPU memory now" };
        free.Click += (_, _) => _host.FreeGpu();
        var open = new ModernButton { Text = "Open models folder" };
        open.Click += (_, _) => OpenFolder(_s.ModelsDir);
        var setup = new ModernButton { Text = "Model manager...", Kind = ButtonKind.Accent };
        Tip(setup, "Shows your GPU memory, recommends models and downloads them.");
        setup.Click += (_, _) => _host.OpenModelManager();
        actions.Controls.AddRange([free, open, setup]);
        AddFull(t, actions);

        page.Controls.Add(t);
        return page;
    }

    private static IEnumerable<string> FindModels(string modelsDir)
    {
        var llm = Path.Combine(modelsDir, "llm");
        if (!Directory.Exists(llm)) yield break;
        foreach (var f in Directory.EnumerateFiles(llm, "*.gguf", SearchOption.AllDirectories).OrderBy(x => x))
        {
            var name = Path.GetFileName(f);
            if (name.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase) || name.StartsWith("mtp", StringComparison.OrdinalIgnoreCase)) continue;
            yield return Path.GetRelativePath(modelsDir, f);
        }
    }

    // =============================================================== Translation

    private Panel BuildTranslationTab()
    {
        var page = new Panel { Text = "Translation", Dock = DockStyle.Fill };
        var t = NewGrid();
        AddFull(t, new Label
        {
            AutoSize = true,
            Tag = "dim",
            MaximumSize = new Size(820, 0),
            Text = "Default target language for each source language. The source language is detected from the letters of the text.",
        });

        foreach (var src in Languages.All)
        {
            var combo = Combo(Languages.All.Select(l => l.Name).ToArray());
            var target = _s.Translation.DefaultTargets.TryGetValue(src.Code, out var code) ? Languages.ByCode(code) : null;
            combo.SelectedIndex = target is null ? 0 : Languages.All.ToList().IndexOf(target);
            _defaultTargets[src.Code] = combo;
            AddRow(t, $"{src.Name} →", combo);
        }

        _glossary = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Width = 420,
            Height = 160,
            Font = Theme.UiFont(9.5f),
            Text = string.Join("\r\n", _s.Translation.Glossary),
        };
        Tip(_glossary, "One term per line: term => translation. Use \"term => =\" to keep a term untranslated.\nOnly terms found in the text are sent to the model, in both directions.");
        AddRow(t, "Glossary:", _glossary);
        AddFull(t, new Label
        {
            AutoSize = true,
            Tag = "dim",
            Text = "Example:  viewport => вьюпорт     rig => риг     UV => =",
        });

        _forgetChoices = new ModernButton { Margin = new Padding(0, 10, 0, 0) };
        UpdateForgetButton();
        Tip(_forgetChoices, "When you pick a language in the action window, it is remembered and used instead of the default.");
        _forgetChoices.Click += (_, _) =>
        {
            _s.Translation.LastTargets.Clear();
            UpdateForgetButton();
        };
        AddFull(t, _forgetChoices);

        page.Controls.Add(t);
        return page;
    }

    private void UpdateForgetButton()
    {
        int n = _s.Translation.LastTargets.Count;
        _forgetChoices.Text = n == 0 ? "No remembered choices" : $"Forget remembered choices ({n})";
        _forgetChoices.Enabled = n > 0;
    }

    // =============================================================== Actions

    private Panel BuildActionsTab()
    {
        var page = new Panel { Text = "Actions", Dock = DockStyle.Fill };
        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Tag = "dim",
            Padding = new Padding(6, 4, 6, 0),
            Text = "Your own actions for the action window (keys 6–9) and for hotkeys (Hotkeys > Action). " +
                   "Write what the model should do with the text; the text itself is added automatically.",
        };
        _actionList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4),
        };
        foreach (var a in _s.CustomActions) AddActionRow(a);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        var add = new ModernButton { Text = "Add action", Kind = ButtonKind.Accent };
        add.Click += (_, _) => AddActionRow(new CustomAction { Name = "New action", Prompt = "" }).Prompt.Focus();
        var reset = new ModernButton { Text = "Reset to examples" };
        reset.Click += (_, _) =>
        {
            foreach (var r in _actionRows.ToList()) { _actionList.Controls.Remove(r.Row); r.Row.Dispose(); }
            _actionRows.Clear();
            foreach (var a in CustomAction.Defaults()) AddActionRow(a);
        };
        buttons.Controls.AddRange([add, reset]);

        page.Controls.Add(_actionList);
        page.Controls.Add(header);
        page.Controls.Add(buttons);
        return page;
    }

    private (TextBox Name, TextBox Prompt, Control Row) AddActionRow(CustomAction a)
    {
        var row = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Width = 760, Height = 64, Margin = new Padding(0, 2, 0, 6), BackColor = Theme.Bg };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        var name = new TextBox { Text = a.Name, Dock = DockStyle.Top, PlaceholderText = "Name" };
        var prompt = new TextBox { Text = a.Prompt, Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "What to do with the text, e.g. \"Rewrite it as a short tweet\"" };
        var remove = new ModernButton { Text = "✕", Kind = ButtonKind.Ghost, AutoFit = false, Width = 28, Height = 28 };
        row.Controls.Add(name, 0, 0);
        row.Controls.Add(prompt, 1, 0);
        row.Controls.Add(remove, 2, 0);
        var entry = (name, prompt, (Control)row);
        remove.Click += (_, _) => { _actionRows.Remove(entry); _actionList.Controls.Remove(row); row.Dispose(); };
        _actionRows.Add(entry);
        _actionList.Controls.Add(row);
        Theme.ApplyTo(row);
        return entry;
    }

    // =============================================================== Apply

    private bool DoApply()
    {
        // Горячие клавиши
        var hotkeys = new List<HotkeyEntry>();
        foreach (var row in _hotkeyRows)
        {
            var e = row.ToEntry();
            if (e.Enabled && e.Keys.Length > 0 && !HotkeyManager.TryParse(e.Keys, out _, out _, out var err))
            {
                MessageBox.Show(this, $"Hotkey \"{e.Name}\": {err}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            hotkeys.Add(e);
        }
        _s.Hotkeys = hotkeys;

        // Общие
        _s.Theme = _theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        _s.UnloadWhileRunning = Lines(_unloadApps);
        _s.TrayClickSource = _trayClick.SelectedIndex switch { 1 => InputSource.Auto, 2 => InputSource.Clipboard, _ => InputSource.Region };
        _s.SnapToWindows = _snap.Checked;
        _s.KeepWindowOpen = _keepOpen.Checked;
        _s.ShowMagnifier = _magnifier.Checked;
        _s.SelectionCopyTimeoutMs = (int)_copyTimeout.Value;
        _s.NeverCopyProcesses = Lines(_neverCopy);
        _s.CopyWithCtrlInsertProcesses = Lines(_ctrlInsert);
        _s.Voice.Enabled = _voiceEnabled.Checked;
        _s.Voice.AutoStopSilenceSeconds = (double)_voiceSilence.Value;
        _s.Voice.Microphone = _microphone.SelectedIndex <= 0 ? "" : _microphone.Text.Replace(" (not connected)", "");
        _s.SaveHistory = _saveHistory.Checked;

        // Модели
        _s.ModelsPath = _modelsPath.Text.Trim();
        _s.RuntimePath = _runtimePath.Text.Trim();
        _s.TextModel.ModelFile = _textModel.Text.Trim();
        _s.TextModel.Temperature = (double)_temperature.Value;
        _s.TextModel.PreloadOnMenu = _preloadText.Checked;
        _s.ModelIdleUnloadMinutes = (int)_unloadMinutes.Value;
        _s.Ocr.PreloadOnHotkey = _preloadOcr.Checked;
        _s.Ocr.UpscaleSmallImages = _upscale.Checked;

        // Свои действия
        _s.CustomActions = _actionRows
            .Select(r => new CustomAction { Name = r.Name.Text.Trim(), Prompt = r.Prompt.Text.Trim() })
            .Where(a => a.Name.Length > 0 && a.Prompt.Length > 0)
            .ToList();

        // Перевод
        _s.Translation.Glossary = _glossary.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var (code, combo) in _defaultTargets)
            _s.Translation.DefaultTargets[code] = Languages.All[Math.Max(0, combo.SelectedIndex)].Code;

        var statuses = _host.Apply(_s.Clone(), _startup.Checked);
        foreach (var row in _hotkeyRows)
        {
            var st = statuses.FirstOrDefault(x => x.Entry.Keys == row.KeysBox.Keys && x.Entry.Name == row.EntryName);
            row.ShowStatus(st);
        }
        return true;
    }

    // =============================================================== Helpers

    private static TableLayoutPanel NewGrid() => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        AutoScroll = true,
        Padding = new Padding(10),
        ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100) },
    };

    private static void AddRow(TableLayoutPanel t, string label, Control control)
    {
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 6, 8, 0) });
        control.Margin = new Padding(0, 3, 0, 3);
        t.Controls.Add(control);
    }

    private static void AddFull(TableLayoutPanel t, Control control)
    {
        control.Margin = new Padding(0, 4, 0, 4);
        t.Controls.Add(control);
        t.SetColumnSpan(control, 2);
    }

    private static ComboBox Combo(string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        c.Items.AddRange(items);
        return c;
    }

    private static TextBox ListBox(List<string> items) => new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Width = 260,
        Height = 90,
        Text = string.Join("\r\n", items),
    };

    private static List<string> Lines(TextBox box) =>
        box.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x[..^4] : x)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private Control WithBrowse(TextBox box)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var browse = new ModernButton { Text = "Browse...", Margin = new Padding(6, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Choose a folder" };
            if (Directory.Exists(box.Text)) dlg.InitialDirectory = box.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
        };
        panel.Controls.AddRange([box, browse]);
        return panel;
    }

    private void Tip(Control c, string text) => _tips.SetToolTip(c, text);

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("Open folder", ex); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && ActiveControl is not HotkeyBox) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _host.ResumeHotkeys();
        _tips.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>Одна строка списка горячих клавиш.</summary>
internal sealed class HotkeyRow : TableLayoutPanel
{
    private static readonly (string Name, InputSource Value)[] Sources =
    [
        ("Auto", InputSource.Auto), ("Selected text", InputSource.SelectedText),
        ("Region", InputSource.Region), ("Clipboard", InputSource.Clipboard), ("Last region", InputSource.LastRegion),
        ("Voice", InputSource.Voice),
    ];

    private static readonly (string Name, HotkeyAction Value)[] BaseActions =
    [
        ("Action window", HotkeyAction.Menu), ("Recognize text", HotkeyAction.Ocr),
        ("Recognize table", HotkeyAction.OcrTable), ("Recognize formula", HotkeyAction.OcrFormula),
        ("Translate", HotkeyAction.Translate), ("Check grammar", HotkeyAction.Grammar),
        ("Text as is", HotkeyAction.AsIs),
    ];

    /// <summary>Действия в списке: базовые + свои (Custom с именем).</summary>
    private readonly List<(string Name, HotkeyAction Value, string Custom)> _actions;

    private static readonly (string Name, AfterAction Value)[] Afters =
    [
        ("Show window", AfterAction.ShowWindow), ("Copy result", AfterAction.Copy), ("Paste into the app", AfterAction.Replace),
        ("Show over the region", AfterAction.Overlay),
    ];

    private static readonly int[] Widths = [24, 150, 170, 105, 150, 150, 85, 70, 28];

    private readonly CheckBox _enabled;
    private readonly TextBox _name;
    private readonly ComboBox _source;
    private readonly ComboBox _action;
    private readonly ComboBox _after;
    private readonly ComboBox _target;
    private readonly Label _status;
    private readonly ToolTip _tips;

    public HotkeyBox KeysBox { get; }
    public string EntryName => _name.Text.Trim();
    public event Action? RemoveRequested;

    public HotkeyRow(HotkeyEntry e, ToolTip tips, IEnumerable<string> customActions)
    {
        _tips = tips;
        _actions = [.. BaseActions.Select(a => (a.Name, a.Value, "")), .. customActions.Select(c => ($"Action: {c}", HotkeyAction.Custom, c))];
        Setup(this);

        _enabled = new CheckBox { Checked = e.Enabled, AutoSize = true, Anchor = AnchorStyles.None };
        tips.SetToolTip(_enabled, "Enabled");
        _name = new TextBox { Text = e.Name, Dock = DockStyle.Fill };
        KeysBox = new HotkeyBox { Keys = e.Keys, Dock = DockStyle.Fill };
        _source = Combo(Sources.Select(x => x.Name), Array.FindIndex(Sources, x => x.Value == e.Source));
        int actionIndex = e.Action == HotkeyAction.Custom
            ? _actions.FindIndex(x => x.Value == HotkeyAction.Custom && string.Equals(x.Custom, e.CustomAction, StringComparison.OrdinalIgnoreCase))
            : _actions.FindIndex(x => x.Value == e.Action);
        _action = Combo(_actions.Select(x => x.Name), actionIndex);
        _after = Combo(Afters.Select(x => x.Name), Array.FindIndex(Afters, x => x.Value == e.After));
        _target = Combo(["Auto", .. Languages.All.Select(l => l.Name)],
            Languages.ByCode(e.TargetLanguage) is { } l ? Languages.All.ToList().IndexOf(l) + 1 : 0);
        tips.SetToolTip(_target, "Target language for \"Translate\". Auto = default rules.");
        _status = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var remove = new ModernButton { Text = "✕", Kind = ButtonKind.Ghost, AutoFit = false, Dock = DockStyle.Fill };
        tips.SetToolTip(remove, "Remove");
        remove.Click += (_, _) => RemoveRequested?.Invoke();

        Controls.AddRange([_enabled, _name, KeysBox, _source, _action, _after, _target, _status, remove]);
        for (int i = 0; i < Controls.Count; i++) SetColumn(Controls[i], i);

        _action.SelectedIndexChanged += (_, _) => UpdateTargetEnabled();
        UpdateTargetEnabled();
    }

    private void UpdateTargetEnabled() => _target.Enabled = _actions[Math.Max(0, _action.SelectedIndex)].Value == HotkeyAction.Translate;

    /// <summary>Строка заголовков столбцов.</summary>
    public static Control HeaderRow()
    {
        var t = new TableLayoutPanel();
        Setup(t);
        string[] titles = ["", "Name", "Hotkey", "Source", "Action", "Then", "Language", "Status", ""];
        for (int i = 0; i < titles.Length; i++)
            t.Controls.Add(new Label { Text = titles[i], AutoSize = true, Tag = "dim", Anchor = AnchorStyles.Left }, i, 0);
        t.Margin = new Padding(4, 8, 0, 0);
        return t;
    }

    private static void Setup(TableLayoutPanel t)
    {
        t.ColumnCount = Widths.Length;
        t.RowCount = 1;
        t.AutoSize = true;
        t.Margin = new Padding(0, 1, 0, 1);
        foreach (var w in Widths) t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    }

    private static ComboBox Combo(IEnumerable<string> items, int index)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        c.Items.AddRange([.. items]);
        c.SelectedIndex = Math.Max(0, index);
        return c;
    }

    public HotkeyEntry ToEntry() => new()
    {
        Enabled = _enabled.Checked,
        Name = EntryName.Length > 0 ? EntryName : "Hotkey",
        Keys = KeysBox.Keys,
        Source = Sources[Math.Max(0, _source.SelectedIndex)].Value,
        Action = _actions[Math.Max(0, _action.SelectedIndex)].Value,
        CustomAction = _actions[Math.Max(0, _action.SelectedIndex)].Custom,
        After = Afters[Math.Max(0, _after.SelectedIndex)].Value,
        TargetLanguage = _target.SelectedIndex <= 0 ? "auto" : Languages.All[_target.SelectedIndex - 1].Code,
    };

    public void ShowStatus(HotkeyStatus? st)
    {
        _tips.SetToolTip(_status, st?.Problem ?? "");
        if (st is null) { _status.Text = ""; return; }
        if (st.Registered)
        {
            _status.Text = "✓ OK";
            _status.ForeColor = Theme.Success;
            KeysBox.BackColor = Theme.Surface;
        }
        else if (!st.Entry.Enabled || st.Entry.Keys.Length == 0)
        {
            _status.Text = "off";
            _status.ForeColor = Theme.TextDim;
        }
        else
        {
            _status.Text = st.Problem?.Contains("already used") == true ? "✗ in use" : "✗ error";
            _status.ForeColor = Theme.Danger;
            KeysBox.BackColor = Theme.IsDark ? Color.FromArgb(90, 40, 40) : Color.FromArgb(255, 225, 225);
        }
    }
}
