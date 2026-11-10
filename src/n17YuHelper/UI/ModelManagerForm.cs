using N17YuHelper.Models;

namespace N17YuHelper.UI;

/// <summary>Что окну моделей нужно от программы.</summary>
public sealed class ModelManagerHost
{
    public required Func<AppSettings> Settings { get; init; }
    /// <summary>Сделать модель текущей (путь внутри папки models).</summary>
    public required Action<string> UseTextModel { get; init; }
    /// <summary>Режим видеопамяти: Auto, Both, OneAtATime.</summary>
    public required Action<string> SetGpuMode { get; init; }
    /// <summary>Сменить папку для моделей (полный путь).</summary>
    public required Action<string> SetModelsFolder { get; init; }
    /// <summary>Выгрузить модели (перед обновлением llama-server или удалением файлов).</summary>
    public required Action StopModels { get; init; }
    public required Icon Icon { get; init; }
}

/// <summary>
/// Менеджер моделей и мастер первого запуска: видеокарта и её память,
/// рекомендуемые модели, скачивание с прогрессом, удаление, выбор текущей модели.
/// </summary>
internal sealed class ModelManagerForm : Form
{
    private readonly ModelManagerHost _host;
    private readonly bool _welcome;
    private readonly GpuInfo _gpu;
    private readonly FlowLayoutPanel _list;
    private readonly Panel _progressPanel;
    private readonly Label _progressText;
    private readonly ProgressBarLine _progressBar;
    private readonly ModernButton _cancel;
    private readonly ModernButton? _recommendedButton;
    private readonly Label _diskLabel;
    private CancellationTokenSource? _cts;
    private bool _working;

    private string ModelsDir => _host.Settings().ModelsDir;
    private string RuntimeDir => _host.Settings().RuntimeDir;

    public ModelManagerForm(ModelManagerHost host, bool welcome)
    {
        _host = host;
        _welcome = welcome;
        _gpu = GpuInfo.Detect();

        Text = welcome ? "Welcome to n17 YuHelper" : "n17 YuHelper - Models";
        Icon = host.Icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = Theme.UiFont(9.5f);
        ClientSize = new Size(820, 680);
        MinimumSize = new Size(640, 480);
        KeyPreview = true;

        // ---------- шапка: видеокарта, диск, рекомендация
        var head = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(20, 16, 20, 6),
        };
        head.Controls.Add(new Label
        {
            Text = welcome ? "Welcome to n17 YuHelper" : "Models",
            AutoSize = true,
            Font = Theme.UiFont(16f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6),
        });
        if (welcome)
        {
            head.Controls.Add(new Label
            {
                Text = "n17 YuHelper translates, recognizes and proofreads text with AI models that run on your own computer. " +
                       "Nothing is sent to the internet. Download the set recommended for your graphics card to start.",
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                Margin = new Padding(0, 0, 0, 10),
            });
        }

        var rec = ModelCatalog.RecommendedText(_gpu);
        head.Controls.Add(InfoLine("", "Graphics card: " + _gpu));
        _diskLabel = InfoLine("", "");
        head.Controls.Add(_diskLabel);
        head.Controls.Add(InfoLine("", $"Recommended: {ModelCatalog.Ocr.Name} + {rec.Name}" +
            (_gpu.Vendor == GpuVendor.None ? " (no dedicated GPU found: models will run on the CPU and be slow)" : "")));

        long recSize = new[] { ModelCatalog.Ocr, rec }.Where(p => !p.IsInstalled(ModelsDir)).Sum(p => p.TotalSize)
                       + (Downloader.InstalledRuntime(RuntimeDir) is null ? 400L << 20 : 0);
        if (recSize > 0)
        {
            _recommendedButton = new ModernButton
            {
                Text = $"Download recommended ({ModelCatalog.FormatSize(recSize)})",
                Glyph = "",
                Kind = ButtonKind.Accent,
                Height = 38,
                Margin = new Padding(0, 10, 0, 4),
            };
            _recommendedButton.Click += (_, _) => _ = DownloadRecommendedAsync(rec);
            head.Controls.Add(_recommendedButton);
        }

        // ---------- режим видеопамяти
        var modeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        modeRow.Controls.Add(new Label { Text = "GPU memory:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        mode.Items.AddRange(["Auto (decide by free GPU memory)", "Keep both models loaded (faster)", "One model at a time (less memory)"]);
        mode.SelectedIndex = host.Settings().GpuMode switch { "Both" => 1, "OneAtATime" => 2, _ => 0 };
        mode.SelectedIndexChanged += (_, _) => host.SetGpuMode(mode.SelectedIndex switch { 1 => "Both", 2 => "OneAtATime", _ => "Auto" });
        modeRow.Controls.Add(mode);
        head.Controls.Add(modeRow);

        // ---------- список
        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(20, 6, 20, 10),
        };
        _list.Resize += (_, _) => FitRows();

        // ---------- прогресс
        _progressPanel = new Panel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(20, 8, 20, 12), Visible = false };
        _progressText = new Label { Dock = DockStyle.Top, Height = 24, AutoEllipsis = true };
        _progressBar = new ProgressBarLine { Dock = DockStyle.Top, Height = 6 };
        _cancel = new ModernButton { Text = "Cancel", Glyph = "", Dock = DockStyle.Right, Height = 30 };
        _cancel.Click += (_, _) => _cts?.Cancel();
        var progressInner = new Panel { Dock = DockStyle.Fill };
        progressInner.Controls.Add(_progressBar);
        progressInner.Controls.Add(_progressText);
        _progressPanel.Controls.Add(progressInner);
        _progressPanel.Controls.Add(_cancel);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 6, 16, 12) };
        var close = new ModernButton { Text = welcome ? "Later" : "Close", AutoFit = false, Width = 110 };
        close.Click += (_, _) => Close();
        bottom.Controls.Add(close);
        var openFolder = new ModernButton { Text = "Open models folder", Glyph = "" };
        openFolder.Click += (_, _) => { Directory.CreateDirectory(ModelsDir); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ModelsDir) { UseShellExecute = true }); };
        bottom.Controls.Add(openFolder);
        var changeFolder = new ModernButton { Text = "Change models folder...", Glyph = "" };
        changeFolder.Click += (_, _) => ChangeModelsFolder();
        bottom.Controls.Add(changeFolder);

        Controls.Add(_list);
        Controls.Add(head);
        Controls.Add(_progressPanel);
        Controls.Add(bottom);

        Theme.ApplyWindow(this);
        Theme.ApplyTo(this);
        RebuildList();
    }

    private static Label InfoLine(string glyph, string text) => new GlyphLabel(glyph, text);

    /// <summary>Модели большие (3–20 ГБ): даём положить их на другой диск.</summary>
    private void ChangeModelsFolder()
    {
        if (_working)
        {
            MessageBox.Show(this, "Wait until the download finishes or cancel it first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new FolderBrowserDialog
        {
            Description = "Folder for AI models (needs several GB of free space; a local SSD is best)",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(ModelsDir) ? ModelsDir : AppPaths.DataDir,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath)) return;
        string old = ModelsDir;
        string chosen = Path.GetFullPath(dlg.SelectedPath);
        if (string.Equals(chosen.TrimEnd('\\'), old.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;

        bool oldHasModels = ModelCatalog.All.Any(p => p.InstalledSize(old) > 0);
        _host.SetModelsFolder(chosen);
        RebuildList();
        if (oldHasModels)
            MessageBox.Show(this,
                $"New models will be downloaded to:\n{chosen}\n\nModels already downloaded stay in:\n{old}\n" +
                "To keep using them, move the \"ocr\" and \"llm\" folders from there to the new folder.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------------- список моделей ----------------

    private void RebuildList()
    {
        _list.SuspendLayout();
        foreach (Control c in _list.Controls) c.Dispose();
        _list.Controls.Clear();

        _diskLabel.Text = $"Free disk space for models: {ModelCatalog.FormatSize(Downloader.FreeSpace(ModelsDir))}  ({ModelsDir})";

        _list.Controls.Add(Section("Engine"));
        _list.Controls.Add(RuntimeRow());
        _list.Controls.Add(Section("Text recognition"));
        _list.Controls.Add(PackageRow(ModelCatalog.Ocr, recommended: true));
        _list.Controls.Add(Section("Translation and grammar"));
        var rec = ModelCatalog.RecommendedText(_gpu);
        foreach (var p in ModelCatalog.All.Where(x => x.Kind == ModelKind.Text))
            _list.Controls.Add(PackageRow(p, recommended: p == rec));

        foreach (Control c in _list.Controls)
        {
            if (c is Card) continue;
            c.BackColor = Theme.Bg;
        }
        Theme.ApplyTo(_list);
        _list.ResumeLayout();
        FitRows();
    }

    private void FitRows()
    {
        int w = _list.ClientSize.Width - _list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
        foreach (Control c in _list.Controls) c.Width = Math.Max(300, w);
    }

    private static Label Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        AutoSize = false,
        Height = 30,
        Font = Theme.UiFont(8f, FontStyle.Bold),
        ForeColor = Theme.TextDim,
        Tag = "dim",
        Padding = new Padding(2, 12, 0, 0),
        Margin = new Padding(0),
    };

    private Card RuntimeRow()
    {
        var installed = Downloader.InstalledRuntime(RuntimeDir);
        string build = _gpu.LlamaBuild switch
        {
            "cuda-13" => "NVIDIA CUDA 13",
            "cuda-12" => "NVIDIA CUDA 12",
            "vulkan" => "Vulkan (AMD / Intel)",
            _ => "CPU only",
        };
        var card = RowCard(
            "llama.cpp server",
            $"Runs the models on your graphics card. Build for this PC: {build}.",
            (installed is null ? "Not installed · about 400 MB" : $"Installed: {installed}") +
            (string.IsNullOrEmpty(ModelCatalog.LlamaCppTag) ? "" : $" · tested version: {ModelCatalog.LlamaCppTag}"),
            installed is not null);

        bool upToDate = installed is not null && string.Equals(installed, ModelCatalog.LlamaCppTag, StringComparison.OrdinalIgnoreCase);
        var btn = new ModernButton
        {
            Text = installed is null ? "Install" : upToDate ? "Reinstall" : string.IsNullOrEmpty(ModelCatalog.LlamaCppTag) ? "Update" : $"Update to {ModelCatalog.LlamaCppTag}",
            Glyph = installed is null ? "" : "",
            Kind = installed is null ? ButtonKind.Accent : ButtonKind.Normal,
        };
        btn.Click += (_, _) => _ = RunAsync("llama.cpp", async (p, ct) =>
        {
            _host.StopModels();
            await Downloader.InstallRuntimeAsync(_gpu, RuntimeDir, p, ct);
        });
        AddButtons(card, btn);
        return card;
    }

    private Card PackageRow(ModelPackage p, bool recommended)
    {
        bool installed = p.IsInstalled(ModelsDir);
        bool inUse = p.Kind == ModelKind.Ocr
            ? installed
            : string.Equals(_host.Settings().TextModel.ModelFile, p.MainFile, StringComparison.OrdinalIgnoreCase);
        double vram = _gpu.Vendor == GpuVendor.None ? 0 : _gpu.VramGb;
        bool tooBig = p.Kind == ModelKind.Text && vram > 0 && p.VramGb > vram - 0.3;

        string status = installed
            ? (inUse ? "In use" : "Downloaded") + $" · {ModelCatalog.FormatSize(p.InstalledSize(ModelsDir))}"
            : $"{ModelCatalog.FormatSize(p.TotalSize)} download";
        status += $" · needs ~{p.VramGb:0.#} GB GPU memory";
        if (p.Kind == ModelKind.Text)
            status += !p.HasAudio ? " · no voice input"
                : p.IsAudioInstalled(ModelsDir) ? " · voice input ✓"
                : installed ? $" · voice input: not downloaded ({ModelCatalog.FormatSize(p.Audio!.Size)})" : " · with voice input";
        // Зелёным — рекомендуемая для этого компьютера, красным — те, что не поместятся в видеопамять.
        // Остальные без отметки.
        string? verdict = recommended
            ? (_gpu.Vendor == GpuVendor.None ? "★ Recommended for your PC (no dedicated GPU: the smallest model)" : $"★ Recommended for your PC ({vram:0.#} GB GPU memory)")
            : tooBig ? $"✗ Too big: needs more GPU memory than your {vram:0.#} GB, will be very slow"
            : null;
        Color? mark = recommended ? Theme.Success : tooBig ? Theme.Danger : null;
        var card = RowCard(p.Name, p.Description, status, installed, warn: false, mark: mark, verdict: verdict);

        var buttons = new List<ModernButton>();
        if (!installed)
        {
            var dl = new ModernButton { Text = "Download", Glyph = "", Kind = recommended ? ButtonKind.Accent : ButtonKind.Normal };
            dl.Click += (_, _) => _ = RunAsync(p.Name, async (pr, ct) =>
            {
                await Downloader.DownloadPackageAsync(p, ModelsDir, pr, ct);
                // Если текущая модель перевода не скачана — сразу переключаемся на новую.
                if (p.Kind == ModelKind.Text && !File.Exists(Path.Combine(ModelsDir, _host.Settings().TextModel.ModelFile)))
                    _host.UseTextModel(p.MainFile);
            });
            buttons.Add(dl);
        }
        else
        {
            if (p.Kind == ModelKind.Text && !inUse)
            {
                var use = new ModernButton { Text = "Use", Glyph = "", Kind = ButtonKind.Accent };
                use.Click += (_, _) => { _host.UseTextModel(p.MainFile); RebuildList(); };
                buttons.Add(use);
            }
            if (p.HasAudio && !p.IsAudioInstalled(ModelsDir))
            {
                var voice = new ModernButton { Text = "Add voice", Glyph = "\uE720" };
                voice.Click += (_, _) => _ = RunAsync(p.Name + " voice", async (pr, ct) =>
                {
                    await Downloader.DownloadFilesAsync(p.Name + " (voice)", [p.Audio!], ModelsDir, pr, ct);
                    // Аудио-часть подхватится при следующей загрузке модели.
                    if (inUse) _host.StopModels();
                });
                buttons.Add(voice);
            }
            var del = new ModernButton { Text = "Delete", Glyph = "" };
            del.Click += (_, _) => Delete(p);
            buttons.Add(del);
        }
        AddButtons(card, [.. buttons]);
        return card;
    }

    private static Card RowCard(string title, string description, string status, bool ok, bool warn = false,
        Color? mark = null, string? verdict = null)
    {
        var card = new Card { Height = verdict is null ? 92 : 112, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(16, 10, 10, 10), Mark = mark };
        var text = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = verdict is null ? 3 : 4, BackColor = Theme.Surface };
        text.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.UiFont(10.5f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Surface });
        if (verdict is not null)
            text.Controls.Add(new Label
            {
                Text = verdict,
                AutoSize = true,
                Font = Theme.UiFont(9f, FontStyle.Bold),
                ForeColor = mark ?? Theme.Text,
                BackColor = Theme.Surface,
                Padding = new Padding(0, 2, 0, 0),
            });
        text.Controls.Add(new Label { Text = description, AutoSize = true, ForeColor = Theme.TextDim, BackColor = Theme.Surface, Tag = "dim", Padding = new Padding(0, 2, 0, 0) });
        text.Controls.Add(new Label
        {
            Text = (ok ? "✓ " : "") + status,
            AutoSize = true,
            ForeColor = warn ? Theme.Danger : ok ? Theme.Success : Theme.TextDim,
            BackColor = Theme.Surface,
            Padding = new Padding(0, 4, 0, 0),
            Font = Theme.UiFont(8.75f),
        });
        card.Controls.Add(text);
        return card;
    }

    private static void AddButtons(Card card, params ModernButton[] buttons)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Surface,
            Padding = new Padding(0, 16, 0, 0),
        };
        foreach (var b in buttons) flow.Controls.Add(b);
        card.Controls.Add(flow);
        flow.SendToBack(); // справа, текст занимает остальное
    }

    // ---------------- действия ----------------

    private async Task DownloadRecommendedAsync(ModelPackage text)
    {
        await RunAsync("recommended set", async (p, ct) =>
        {
            if (Downloader.InstalledRuntime(RuntimeDir) is null)
            {
                _host.StopModels();
                await Downloader.InstallRuntimeAsync(_gpu, RuntimeDir, p, ct);
            }
            await Downloader.DownloadPackageAsync(ModelCatalog.Ocr, ModelsDir, p, ct);
            await Downloader.DownloadPackageAsync(text, ModelsDir, p, ct);
            _host.UseTextModel(text.MainFile);
        });
        if (_recommendedButton is not null && Downloader.InstalledRuntime(RuntimeDir) is not null &&
            ModelCatalog.Ocr.IsInstalled(ModelsDir) && text.IsInstalled(ModelsDir))
        {
            _recommendedButton.Visible = false;
            if (_welcome)
                MessageBox.Show(this, "All set! Press Ctrl+Shift+Win+Q anywhere to start.\nSelect some text first to translate or check it, or press without selection to capture a screen region.",
                    "n17 YuHelper", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task RunAsync(string what, Func<IProgress<DownloadProgress>, CancellationToken, Task> work)
    {
        if (_working) return;
        _working = true;
        _cts = new CancellationTokenSource();
        SetButtonsEnabled(false);
        _progressPanel.Visible = true;
        _progressText.Text = $"Preparing {what}...";
        _progressBar.Value = 0;

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (IsDisposed) return;
            _progressBar.Value = p.Fraction;
            string speed = p.BytesPerSecond > 0 ? $" · {ModelCatalog.FormatSize((long)p.BytesPerSecond)}/s" : "";
            string eta = p.BytesPerSecond > 0 && p.Total > p.Done ? $" · {TimeSpan.FromSeconds((p.Total - p.Done) / p.BytesPerSecond):h\\:mm\\:ss} left" : "";
            _progressText.Text = p.Total > 0
                ? $"{p.Stage}: {ModelCatalog.FormatSize(p.Done)} of {ModelCatalog.FormatSize(p.Total)}{speed}{eta}"
                : p.Stage;
        });

        try
        {
            await work(progress, _cts.Token);
            _progressText.Text = "Done.";
        }
        catch (OperationCanceledException)
        {
            _progressText.Text = "Cancelled. The download will resume where it stopped next time.";
        }
        catch (Exception ex)
        {
            Log.Error($"Download {what}", ex);
            _progressText.Text = "Failed: " + ex.Message;
        }
        finally
        {
            _working = false;
            _cts.Dispose();
            _cts = null;
            if (!IsDisposed)
            {
                SetButtonsEnabled(true);
                RebuildList();
            }
        }
    }

    private void Delete(ModelPackage p)
    {
        if (MessageBox.Show(this, $"Delete {p.Name} ({ModelCatalog.FormatSize(p.InstalledSize(ModelsDir))})?", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _host.StopModels();
        try
        {
            foreach (var f in p.AllFiles)
            {
                var path = Path.Combine(ModelsDir, f.RelativePath);
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".part")) File.Delete(path + ".part");
            }
            var dir = Path.GetDirectoryName(Path.Combine(ModelsDir, p.MainFile));
            if (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            Log.Info($"Deleted {p.Name}");
        }
        catch (Exception ex)
        {
            Log.Error($"Delete {p.Name}", ex);
            MessageBox.Show(this, "Could not delete: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RebuildList();
    }

    private void SetButtonsEnabled(bool on)
    {
        void Walk(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ModernButton b && b != _cancel) b.Enabled = on;
                Walk(c);
            }
        }
        Walk(this);
        _cancel.Enabled = !on;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_working &&
            MessageBox.Show(this, "A download is in progress. Stop it? It will resume next time.", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            e.Cancel = true;
            return;
        }
        _cts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Строка со значком слева.</summary>
    private sealed class GlyphLabel : Label
    {
        private readonly string _glyph;

        public GlyphLabel(string glyph, string text)
        {
            _glyph = glyph;
            Text = text;
            AutoSize = true;
            MaximumSize = new Size(780, 0);
            Padding = new Padding(24, 2, 0, 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var f = Theme.IconFont(Font.Size);
            TextRenderer.DrawText(e.Graphics, _glyph, f, new Rectangle(0, 0, 22, Height), Theme.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Тонкая полоса прогресса цвета акцента.</summary>
internal sealed class ProgressBarLine : Control
{
    private double _value;

    public ProgressBarLine()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var back = new SolidBrush(Theme.Hover))
        using (var path = Theme.RoundRect(r, Height / 2f))
            g.FillPath(back, path);
        if (_value > 0)
        {
            using var fill = new SolidBrush(Theme.Accent);
            using var path = Theme.RoundRect(new RectangleF(0, 0, Math.Max(Height, (float)(r.Width * _value)), r.Height), Height / 2f);
            g.FillPath(fill, path);
        }
    }
}
