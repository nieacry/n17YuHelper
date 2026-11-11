using System.Runtime.InteropServices;
using N17YuHelper.Files;
using N17YuHelper.Grammar;
using N17YuHelper.Llm;
using N17YuHelper.Ocr;
using N17YuHelper.Tools;
using N17YuHelper.Translate;

namespace N17YuHelper.UI;

/// <summary>What came in: selected text or a region image.</summary>
public sealed class CapturedInput : IDisposable
{
    public string? Text { get; init; }
    public Bitmap? Image { get; init; }
    /// <summary>Window where the text was selected (for replacing). Null for a screen region and the clipboard.</summary>
    public SourceWindow? Source { get; init; }
    /// <summary>Where the region was on the screen (for the overlay and repeating).</summary>
    public Rectangle? Region { get; init; }
    public bool IsText => Text is not null;
    public void Dispose() => Image?.Dispose();
}

/// <summary>What was in the action window when it was closed (for "Open last window").</summary>
public sealed class WindowSnapshot : IDisposable
{
    public string? Text { get; init; }
    public Bitmap? Image { get; init; }
    public SourceWindow? Source { get; init; }
    public string SourceCaption { get; init; } = "TEXT";
    public string? ResultText { get; init; }
    public string? ResultRtf { get; init; }
    public string? ResultCaption { get; init; }
    public Language? ResultLang { get; init; }
    public Language? Target { get; init; }
    public bool UserPickedTarget { get; init; }
    public string? FilePath { get; init; }
    public string? LoadedText { get; init; }
    public byte[]? TranslatedDoc { get; init; }
    public bool IsTable { get; init; }
    public bool Pinned { get; init; }
    public Rectangle Bounds { get; init; }
    public string Status { get; init; } = "";
    public void Dispose() => Image?.Dispose();
}

/// <summary>Recognition function: image → text.</summary>
public delegate Task<string> OcrFunc(Bitmap image, IProgress<string> progress, CancellationToken ct, OcrMode mode);

/// <summary>Services the action window needs.</summary>
public sealed class ActionServices
{
    public required OcrFunc Ocr { get; init; }
    public required TranslationService Translation { get; init; }
    public required GrammarService Grammar { get; init; }
    public required PromptService Prompts { get; init; }
    /// <summary>Custom actions from the settings (buttons 6–9).</summary>
    public required Func<IReadOnlyList<CustomAction>> CustomActions { get; init; }
    /// <summary>Paste text instead of the selection in the source window.</summary>
    public required Func<SourceWindow, string, Task> Replace { get; init; }
    /// <summary>Write the result to history.</summary>
    public required Action<string, string, string> AddHistory { get; init; }
    /// <summary>Called when the window opens: start loading the text model in advance.</summary>
    public Action? PreloadText { get; init; }
    /// <summary>Record voice and recognize it (null — cancelled or nothing was said).</summary>
    public Func<IProgress<string>, Task<string?>>? Dictate { get; init; }
    /// <summary>"Keep window open" setting: a new window is pinned right away.</summary>
    public Func<bool>? KeepOpen { get; init; }
    /// <summary>Translate a Word / PowerPoint document keeping formatting: path, from, to, paragraphs as they go → file.</summary>
    public Func<string, Language, Language, Action<string>, IProgress<string>, CancellationToken, Task<byte[]>>? TranslateDocument { get; init; }
    /// <summary>Finish the voice recording in progress (true — cancel).</summary>
    public Action<bool>? StopDictation { get; init; }
}

/// <summary>
/// Action window next to the cursor (no system frame, Windows 11 style).
/// The source text can be edited or typed by hand.
/// 1 — translate, 2 — recognize text in the image, 3 — check grammar,
/// 4 — copy the result, 5 — replace the selected text. While the cursor is in the input box —
/// the same actions on Ctrl+1…Ctrl+5. Esc — close.
/// </summary>
internal sealed class ActionForm : Form
{
    private readonly CapturedInput _input;
    private readonly ActionServices _services;
    private readonly CancellationTokenSource _cts = new();

    private readonly Panel _header;
    private readonly Panel _contentHost;
    private readonly Card _sourceCard;
    private readonly Panel _sourcePane;
    private readonly Caption _sourceCaption;
    private Caption? _resultCaption;
    private readonly BusyLine _busyLine;
    private readonly TextBox _sourceBox;
    private readonly Label _status;
    private readonly ModernButton _langChip;
    private readonly ContextMenuStrip _langMenu;
    private readonly ModernButton _btnTranslate;
    private readonly ModernButton _btnOcr;
    private readonly ModernButton _btnOcrMode;
    private readonly ContextMenuStrip _ocrMenu;
    private readonly ModernButton _btnActions;
    private readonly ContextMenuStrip _actionsMenu;
    private bool _isTable;
    private readonly ModernButton _btnQr;
    private Language? _resultLang;      // result language (for text-to-speech)              // the box holds a table (copied for both Excel and Word)
    private readonly ModernButton _btnGrammar;
    private readonly ModernButton _btnCopy;
    private readonly ModernButton _btnReplace;
    private readonly ModernButton _btnSave;
    private readonly ModernButton _btnMic;
    private readonly ModernButton _btnPin;

    private Bitmap? _image;             // image (region snapshot or an opened file)
    private string? _filePath;          // opened file (for "Save")
    private bool _pinned;               // do not close on focus loss
    private bool _dictating;
    private string? _loadedText;        // file text right after opening (if not edited — translate the document with formatting)
    private byte[]? _translatedDoc;     // translated .docx / .pptx

    private string? _resultText;        // translation or corrected text
    private string? _resultForSource;   // which source text the result belongs to
    private Language? _sourceLang;
    private Language? _target;          // selected target language
    private RichTextBox? _resultBox;
    private CancellationTokenSource? _translateCts;
    private bool _busyFlag;
    private bool _busy
    {
        get => _busyFlag;
        set { _busyFlag = value; if (!_busyLine.IsDisposed) _busyLine.Running = value; }
    }
    private bool _userPickedTarget;
    private bool _retranslatePending;
    private bool _hasText;              // there is source text (selected, recognized or being typed)

    public ActionForm(CapturedInput input, ActionServices services, string? note = null)
    {
        _input = input;
        _services = services;

        Text = "n17 YuHelper";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = Theme.UiFont(9.5f);
        ClientSize = new Size(680, 440);
        MinimumSize = new Size(500, 300);
        Padding = new Padding(5);   // edge strip — the window can be resized by it
        DoubleBuffered = true;
        Theme.ApplyWindow(this);

        // ---------- header: icon, title, target language, close
        _header = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(12, 6, 8, 4), BackColor = Theme.Bg };
        var badge = new BadgeLabel { Dock = DockStyle.Left, Width = 30 };
        var title = new Label
        {
            Text = "YuHelper",
            Dock = DockStyle.Left,
            AutoSize = true,
            Font = Theme.UiFont(10f, FontStyle.Bold),
            Padding = new Padding(6, 7, 10, 0),
            ForeColor = Theme.Text,
        };
        _langChip = new ModernButton { Kind = ButtonKind.Ghost, Dock = DockStyle.Left, Glyph = "\uE774", Text = "Auto → English  ▾", Height = 30 };
        _langMenu = new ContextMenuStrip();
        foreach (var lang in Languages.All)
        {
            var item = new ToolStripMenuItem(lang.Name) { Tag = lang };
            item.Click += (_, _) => SelectTarget(lang, byUser: true);
            _langMenu.Items.Add(item);
        }
        Theme.StyleMenu(_langMenu);
        _langChip.Click += (_, _) =>
        {
            foreach (ToolStripMenuItem mi in _langMenu.Items) mi.Checked = mi.Tag is Language l && l == CurrentTarget;
            _langMenu.Show(_langChip, new Point(0, _langChip.Height));
        };
        var close = new ModernButton { Kind = ButtonKind.Ghost, Glyph = "\uE8BB", Dock = DockStyle.Right, AutoFit = false, Width = 36, Height = 30 };
        close.Click += (_, _) => Close();
        _btnPin = HeaderButton("\uE718", "Keep the window open (it stays when you click elsewhere)", () => Pinned = !Pinned);
        _btnMic = HeaderButton("\uE720", "Dictate (Ctrl+M): speak, then click again or pause to finish", () => _ = DictateAsync());
        _btnMic.Visible = services.Dictate is not null;
        var open = HeaderButton("\uE8E5", "Open a file to translate (Ctrl+O). You can also drop files here.", OpenFile);
        _header.Controls.AddRange([_langChip, title, badge, open, _btnMic, _btnPin, close]);
        foreach (Control c in new Control[] { _header, badge, title })
            c.MouseDown += DragWindow;

        // ---------- button bar and status line
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(12, 6, 12, 2),
            BackColor = Theme.Bg,
        };
        _btnTranslate = MakeButton("Translate", "1", "\uE774", ButtonKind.Normal, () => _ = TranslateAsync());
        _btnOcr = MakeButton("Recognize text", "2", "\uE91B", ButtonKind.Normal, () => _ = RunOcrAsync());
        _btnOcr.Margin = new Padding(0, 0, 0, 4);
        _btnOcrMode = new ModernButton { Glyph = "\uE70D", Kind = ButtonKind.Normal, AutoFit = false, Width = 28, Height = 34, Margin = new Padding(1, 0, 6, 4) };
        _ocrMenu = new ContextMenuStrip();
        _ocrMenu.Items.Add("Text", null, (_, _) => _ = RunOcrAsync(OcrMode.Text));
        _ocrMenu.Items.Add("Table (for Excel / Word)", null, (_, _) => _ = RunOcrAsync(OcrMode.Table));
        _ocrMenu.Items.Add("Formula (LaTeX)", null, (_, _) => _ = RunOcrAsync(OcrMode.Formula));
        Theme.StyleMenu(_ocrMenu);
        _btnOcrMode.Click += (_, _) => _ocrMenu.Show(_btnOcrMode, new Point(0, _btnOcrMode.Height));
        _btnGrammar = MakeButton("Check grammar", "3", "\uE8FB", ButtonKind.Normal, () => _ = CheckGrammarAsync());
        _btnCopy = MakeButton("Copy", "4", "\uE8C8", ButtonKind.Normal, CopyCurrent);
        _btnReplace = MakeButton("Replace", "5", "\uE77F", ButtonKind.Accent, () => _ = ReplaceAsync());
        _btnSave = MakeButton("Save", "Ctrl+S", "\uE74E", ButtonKind.Accent, SaveResult);
        _btnSave.Visible = false;

        // Custom actions: menu and keys 6–9.
        _btnActions = MakeButton("Actions", "6-9", "\uE70F", ButtonKind.Normal, () => { });
        _actionsMenu = new ContextMenuStrip();
        var actions = services.CustomActions();
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            var item = new ToolStripMenuItem(a.Name, null, (_, _) => _ = RunCustomAsync(a));
            if (i < 4) item.ShortcutKeyDisplayString = (6 + i).ToString();
            _actionsMenu.Items.Add(item);
        }
        if (actions.Count > 0) _actionsMenu.Items.Add(new ToolStripSeparator());
        _actionsMenu.Items.Add("Speak text", null, (_, _) => SpeakText(SourceText, _sourceLang));
        _actionsMenu.Items.Add("Speak result", null, (_, _) => SpeakText(_resultText ?? "", _resultLang));
        _actionsMenu.Items.Add("Pinyin (Chinese)", null, (_, _) => _ = ShowPinyinAsync());
        Theme.StyleMenu(_actionsMenu);
        _btnActions.Click += (_, _) => _actionsMenu.Show(_btnActions, new Point(0, _btnActions.Height));

        _btnOcr.Visible = !input.IsText;
        _btnOcrMode.Visible = !input.IsText;
        _btnReplace.Visible = input.Source is not null && input.IsText;
        _btnReplace.Enabled = false;
        _btnQr = MakeButton("QR", "", "\uED14", ButtonKind.Accent, () => { });
        _btnQr.Visible = false;
        toolbar.Controls.AddRange([_btnQr, _btnTranslate, _btnOcr, _btnOcrMode, _btnGrammar, _btnActions, _btnCopy, _btnReplace, _btnSave]);
        _image = input.Image;
        if (_image is not null) _ = DetectQrAsync(_image);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 26,
            Padding = new Padding(14, 2, 12, 6),
            ForeColor = Theme.TextDim,
            BackColor = Theme.Bg,
            Font = Theme.UiFont(8.75f),
            AutoEllipsis = true,
        };

        // ---------- content
        _contentHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 2, 12, 4), BackColor = Theme.Bg };

        _sourceBox = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(10.5f),
            MaxLength = 0, // no 32767-character limit: files can be large
        };
        _sourceBox.TextChanged += OnSourceEdited;
        _sourceBox.GotFocus += (_, _) => UpdateHints();
        _sourceBox.LostFocus += (_, _) => UpdateHints();
        _sourceCard = new Card { Dock = DockStyle.Fill };
        _sourceCard.Controls.Add(_sourceBox);
        _sourceCaption = new Caption(input.IsText ? "Text" : "Image");
        _sourcePane = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        _sourcePane.Controls.Add(_sourceCard);
        _sourcePane.Controls.Add(_sourceCaption);

        _busyLine = new BusyLine { Dock = DockStyle.Top, BackColor = Theme.Bg };

        Controls.Add(_contentHost);
        Controls.Add(_busyLine);
        Controls.Add(toolbar);
        Controls.Add(_status);
        Controls.Add(_header);

        if (input.Text is not null)
        {
            SetSourceText(input.Text);
            ShowSourceOnly();
            _status.Text = note ?? (input.Text.Length == 0
                ? "Type or paste text · Ctrl+1 translate · Ctrl+3 check grammar · Esc close"
                : "You can edit the text · Ctrl+1…5 while typing · Esc close");
        }
        else
        {
            ShowImage();
            _status.Text = note ?? "2 recognize text · 1 translate · 3 check grammar · 4 copy image · Esc close";
        }
        UpdateChip();
        EnableDrop(this);
        if (services.KeepOpen?.Invoke() == true) Pinned = true;
    }

    private ModernButton HeaderButton(string glyph, string tip, Action onClick)
    {
        var b = new ModernButton { Kind = ButtonKind.Ghost, Glyph = glyph, Dock = DockStyle.Right, AutoFit = false, Width = 36, Height = 30 };
        b.Click += (_, _) => onClick();
        _tips.SetToolTip(b, tip);
        return b;
    }

    private readonly ToolTip _tips = new();

    /// <summary>Do not close the window when it loses focus (for working with files and dictation).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Pinned
    {
        get => _pinned;
        set
        {
            _pinned = value;
            _btnPin.Selected = value;
            _btnPin.Glyph = value ? "\uE840" : "\uE718";
        }
    }

    private ModernButton MakeButton(string text, string hint, string glyph, ButtonKind kind, Action onClick)
    {
        var b = new ModernButton { Text = text, Hint = hint, Glyph = glyph, Kind = kind, Height = 34, Margin = new Padding(0, 0, 6, 4) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private string SourceText => _sourceBox.Text;
    private Language CurrentTarget => _target ?? _services.Translation.DefaultTarget(_sourceLang ?? Languages.English);

    // ---------------- Source text and language ----------------

    private void SetSourceText(string text)
    {
        _hasText = true;
        _sourceBox.Text = Normalize(text);
        _sourceBox.SelectionStart = _sourceBox.TextLength;
        _sourceBox.SelectionLength = 0;
        UpdateLanguage();
    }

    private void OnSourceEdited(object? sender, EventArgs e)
    {
        if (!_hasText) return;
        UpdateLanguage();
        // The text changed — the previous result no longer applies to it.
        if (_resultText is not null && _resultForSource != SourceText) _btnReplace.Enabled = false;
    }

    private void UpdateLanguage()
    {
        var lang = Languages.Detect(SourceText);
        if (lang == _sourceLang) return;
        _sourceLang = lang;
        if (!_userPickedTarget) _target = null;
        UpdateChip();
    }

    private void UpdateChip()
    {
        string src = _hasText && !string.IsNullOrWhiteSpace(SourceText) ? _sourceLang?.Name ?? "Auto" : "Auto";
        _langChip.Text = $"{src} → {CurrentTarget.Name}  ▾";
    }

    private void SelectTarget(Language lang, bool byUser)
    {
        _target = lang;
        if (byUser)
        {
            _userPickedTarget = true;
            if (_sourceLang is not null) _services.Translation.RememberTarget(_sourceLang, lang);
        }
        UpdateChip();
        if (!byUser || _resultBox is null || _resultForSource is null) return;

        // A translation is shown or in progress — translate again into the chosen language.
        if (_busy)
        {
            _retranslatePending = true;
            _translateCts?.Cancel();
        }
        else
        {
            BeginInvokeSafe(() => _ = TranslateAsync());
        }
    }

    /// <summary>While the cursor is in the input box, hints show Ctrl+digit.</summary>
    private void UpdateHints()
    {
        string p = _sourceBox.Focused ? "Ctrl+" : "";
        _btnTranslate.Hint = p + "1";
        _btnOcr.Hint = p + "2";
        _btnGrammar.Hint = p + "3";
        _btnCopy.Hint = p + "4";
        _btnReplace.Hint = p + "5";
        _btnActions.Hint = p + "6-9";
    }

    // ---------------- Display ----------------

    private void ShowImage()
    {
        _contentHost.Controls.Clear();
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(6) };
        card.Controls.Add(new PictureBox
        {
            Image = _image,
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
        });
        _contentHost.Controls.Add(card);
        _contentHost.Controls.Add(new Caption(_filePath is null ? "Captured region" : "Image · " + Path.GetFileName(_filePath)));
        EnableDrop(_contentHost);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private void ShowSourceOnly()
    {
        _contentHost.Controls.Clear();
        _sourcePane.Parent?.Controls.Remove(_sourcePane);
        _sourceCaption.Text = "TEXT";
        _contentHost.Controls.Add(_sourcePane);
        _resultBox = null;
        _resultCaption = null;
    }

    /// <summary>Source text on top (editable), result below.</summary>
    private void ShowSourceAndResult()
    {
        if (_resultBox is not null) return;
        _contentHost.Controls.Clear();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 8,
            BackColor = Theme.Bg,
        };
        split.Panel1.BackColor = Theme.Bg;
        split.Panel2.BackColor = Theme.Bg;
        _sourcePane.Parent?.Controls.Remove(_sourcePane);
        _sourceCaption.Text = "ORIGINAL";
        split.Panel1.Controls.Add(_sourcePane);

        _resultBox = new RichTextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(10.5f),
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
        };
        var resultCard = new Card { Dock = DockStyle.Fill };
        resultCard.Controls.Add(_resultBox);
        _resultCaption = new Caption("Result");
        split.Panel2.Controls.Add(resultCard);
        split.Panel2.Controls.Add(_resultCaption);
        _contentHost.Controls.Add(split);
        try
        {
            if (split.Height > 120) split.SplitterDistance = (int)(split.Height * 0.42);
        }
        catch (InvalidOperationException) { /* the window is too small */ }
    }

    private void ResetResultBox()
    {
        var box = _resultBox!;
        box.Clear();
        box.SelectionFont = box.Font;
        box.SelectionColor = Theme.Text;
        box.SelectionBackColor = Theme.Surface;
    }

    private bool EnsureText()
    {
        if (!string.IsNullOrWhiteSpace(SourceText)) return true;
        SetStatus("Type or paste some text first.");
        _sourceBox.Focus();
        return false;
    }

    // ---------------- Actions ----------------

    /// <summary>Recognizes text in the image. Returns true if text was obtained.</summary>
    public async Task<bool> RunOcrAsync(OcrMode mode = OcrMode.Text)
    {
        if (_hasText) return true;
        if (_busy || _image is null) return false;
        _busy = true;
        _btnOcr.Enabled = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var progress = new Progress<string>(SetStatus);

        try
        {
            var text = await _services.Ocr(_image, progress, _cts.Token, mode);
            if (IsDisposed) return false;

            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("No text found.");
                _btnOcr.Enabled = true;
                return false;
            }
            _isTable = mode == OcrMode.Table;
            SetSourceText(text);
            ShowSourceOnly();
            _sourceCaption.Text = mode switch { OcrMode.Table => "TABLE", OcrMode.Formula => "FORMULA (LATEX)", _ => "TEXT" };
            _btnOcr.Visible = false;
            _btnOcrMode.Visible = false;
            _services.AddHistory(mode == OcrMode.Text ? "OCR" : $"OCR {mode}", "(image)", text);
            SetStatus(mode switch
            {
                OcrMode.Table => $"Table recognized in {sw.Elapsed.TotalSeconds:0.0} s · Copy puts it in the clipboard for Excel and Word",
                OcrMode.Formula => $"Formula recognized in {sw.Elapsed.TotalSeconds:0.0} s · Copy puts the LaTeX in the clipboard",
                _ => $"Recognized {text.Length} characters in {sw.Elapsed.TotalSeconds:0.0} s · you can edit it",
            });
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("OCR from action window", ex);
            ShowError("OCR failed", ex);
            if (!IsDisposed) _btnOcr.Enabled = true;
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Translates the text (recognizing the image first if needed).</summary>
    public async Task TranslateAsync(Language? forcedTarget = null)
    {
        if (_busy) return;
        if (!_hasText && !await RunOcrAsync()) return;
        if (!EnsureText()) return;

        var text = SourceText;
        var source = Languages.Detect(text);
        if (forcedTarget is not null) SelectTarget(forcedTarget, byUser: false);
        var target = CurrentTarget;

        _translateCts?.Cancel();
        _translateCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = _translateCts.Token;

        _busy = true;
        _btnTranslate.Enabled = false;
        ShowSourceAndResult();
        _resultCaption!.Text = $"TRANSLATION · {target.Name.ToUpperInvariant()}";
        ResetResultBox();
        _resultText = null;
        _btnReplace.Enabled = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var progress = new Progress<string>(SetStatus);

        try
        {
            var box = _resultBox!;
            string result;
            _translatedDoc = null;
            if (IsOfficeOriginal && _services.TranslateDocument is { } translateDoc)
            {
                // Word / PowerPoint, text not edited: translate the document itself with formatting.
                var paragraphs = new List<string>();
                var doc = await translateDoc(_filePath!, source, target,
                    para => { paragraphs.Add(para); if (!box.IsDisposed) box.AppendText((paragraphs.Count > 1 ? "\r\n" : "") + Normalize(para)); },
                    progress, ct);
                if (IsDisposed || ct.IsCancellationRequested) return;
                _translatedDoc = doc;
                result = string.Join("\n", paragraphs);
            }
            else
            {
                result = await _services.Translation.TranslateAsync(
                    text, source, target,
                    // The reply arrives on the UI thread (await without ConfigureAwait), so append right away.
                    onDelta: piece => { if (!box.IsDisposed) box.AppendText(Normalize(piece)); },
                    progress, ct);
            }
            if (IsDisposed || ct.IsCancellationRequested) return;

            _resultText = result;
            _resultForSource = text;
            _resultLang = target;
            box.Text = Normalize(result);
            _btnReplace.Enabled = true;
            if (_filePath is not null) _btnSave.Visible = true;
            _services.AddHistory($"Translate {source.Code}→{target.Code}", text, result);
            SetStatus($"{source.Name} → {target.Name} · {sw.Elapsed.TotalSeconds:0.0} s · " +
                      (_translatedDoc is not null ? $"Save writes a translated {Path.GetExtension(_filePath)} with the same formatting (Ctrl+S)"
                       : _filePath is not null ? "Save writes the translation to a file (Ctrl+S)" : $"Copy puts the translation in the clipboard{ReplaceHint}"));
        }
        catch (OperationCanceledException)
        {
            // translation cancelled: the window was closed or another language was chosen
        }
        catch (Exception ex)
        {
            Log.Error("Translate from action window", ex);
            ShowError("Translation failed", ex);
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) _btnTranslate.Enabled = true;
            if (_retranslatePending && !IsDisposed)
            {
                _retranslatePending = false;
                BeginInvokeSafe(() => _ = TranslateAsync());
            }
        }
    }

    /// <summary>Custom action: the result is shown below as it is generated.</summary>
    public async Task RunCustomAsync(CustomAction action, string? textOverride = null)
    {
        if (_busy) return;
        if (!_hasText && !await RunOcrAsync()) return;
        if (textOverride is null && !EnsureText()) return;

        var text = textOverride ?? SourceText;
        _translateCts?.Cancel();
        _busy = true;
        _btnActions.Enabled = false;
        ShowSourceAndResult();
        _resultCaption!.Text = action.Name.ToUpperInvariant();
        ResetResultBox();
        _resultText = null;
        _btnReplace.Enabled = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var progress = new Progress<string>(SetStatus);

        try
        {
            var box = _resultBox!;
            var result = await _services.Prompts.RunAsync(action, text,
                piece => { if (!box.IsDisposed) box.AppendText(Normalize(piece)); }, progress, _cts.Token);
            if (IsDisposed) return;
            _resultText = result;
            _resultForSource = text;
            _resultLang = Languages.Detect(result);
            box.Text = Normalize(result);
            _btnReplace.Enabled = true;
            _services.AddHistory(action.Name, text, result);
            SetStatus($"{action.Name} · {sw.Elapsed.TotalSeconds:0.0} s · Copy puts the result in the clipboard{ReplaceHint}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Action '{action.Name}'", ex);
            ShowError(action.Name + " failed", ex);
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) _btnActions.Enabled = true;
        }
    }

    // ---------------- Text-to-speech, pinyin, QR ----------------

    private void SpeakText(string text, Language? lang)
    {
        if (string.IsNullOrWhiteSpace(text)) { SetStatus("Nothing to speak yet."); return; }
        lang ??= Languages.Detect(text);
        var error = Speech.Speak(text, lang.Code);
        SetStatus(error ?? $"Speaking ({lang.Name})...");
    }

    /// <summary>Pinyin for Chinese text: of the translation (if it is Chinese) or of the source.</summary>
    private async Task ShowPinyinAsync()
    {
        string? chinese = _resultLang == Languages.Chinese && _resultText is not null ? _resultText
            : _sourceLang == Languages.Chinese ? SourceText : null;
        if (chinese is null)
        {
            SetStatus("No Chinese text here. Translate to Chinese first or select Chinese text.");
            return;
        }
        var action = new CustomAction
        {
            Name = "Pinyin",
            Prompt = "Write pinyin with tone marks for this Chinese text. For each line, write the Chinese line, " +
                     "then on the next line its pinyin, then a short Russian translation in brackets. Nothing else.",
        };
        await RunCustomAsync(action, chinese);
        _resultLang = Languages.Chinese;
    }

    private async Task DetectQrAsync(Bitmap image)
    {
        Bitmap copy;
        try { copy = (Bitmap)image.Clone(); } catch { return; }
        var code = await Task.Run(() => { using (copy) return QrReader.TryRead(copy); });
        if (code is null || IsDisposed) return;

        _btnQr.Text = code.IsLink ? "Open link" : "Copy QR";
        _btnQr.Glyph = code.IsLink ? "\uE71B" : "\uE8C8";
        _btnQr.Click += (_, _) =>
        {
            try
            {
                if (code.IsLink) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(code.Text) { UseShellExecute = true });
                else Clipboard.SetText(code.Text);
                Close();
            }
            catch (Exception ex) { Log.Error("QR action", ex); }
        };
        _btnQr.Visible = true;
        SetStatus($"{code.Format.Replace('_', ' ')}: {code.Text}");
        _services.AddHistory("QR", "(image)", code.Text);
    }

    private string ReplaceHint => _btnReplace.Visible ? " · Replace puts it into the original app" : "";

    /// <summary>Checks spelling and grammar and shows the corrections.</summary>
    public async Task CheckGrammarAsync()
    {
        if (_busy) return;
        if (!_hasText && !await RunOcrAsync()) return;
        if (!EnsureText()) return;

        var text = SourceText;
        if (text.Length > 8000)
        {
            SetStatus($"The text is too long for the grammar check ({text.Length:N0} characters, up to 8,000). Select a part of it.");
            return;
        }
        _translateCts?.Cancel();
        _busy = true;
        _btnGrammar.Enabled = false;
        _btnReplace.Enabled = false;
        ShowSourceAndResult();
        _resultCaption!.Text = "CORRECTED";
        ResetResultBox();
        _resultText = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var progress = new Progress<string>(SetStatus);

        try
        {
            var result = await _services.Grammar.CheckAsync(text, progress, _cts.Token);
            if (IsDisposed || _resultBox is null) return;

            RenderGrammar(_resultBox, result);
            _resultText = result.Corrected;
            _resultLang = result.Language;
            _resultForSource = text;
            if (result.HasChanges)
            {
                _btnReplace.Enabled = true;
                int n = Math.Max(result.Fixes.Count, 1);
                _services.AddHistory("Grammar", text, result.Corrected);
                SetStatus($"{result.Language.Name}: {n} fix{(n == 1 ? "" : "es")} · {sw.Elapsed.TotalSeconds:0.0} s · Copy puts the corrected text in the clipboard{ReplaceHint}");
            }
            else
            {
                SetStatus($"{result.Language.Name}: no errors found · {sw.Elapsed.TotalSeconds:0.0} s");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Grammar from action window", ex);
            ShowError("Grammar check failed", ex);
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) _btnGrammar.Enabled = true;
        }
    }

    /// <summary>Corrected text with highlighting: removed — red strikethrough, added — green. Explanations below.</summary>
    private static void RenderGrammar(RichTextBox box, GrammarResult result)
    {
        box.Clear();
        var normal = box.Font;
        using var strike = new Font(normal, FontStyle.Strikeout);
        using var bold = new Font(normal, FontStyle.Bold);

        if (!result.HasChanges)
        {
            box.AppendText(Normalize(result.Original));
            return;
        }

        foreach (var part in TextDiff.Compute(result.Original, result.Corrected))
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            switch (part.Kind)
            {
                case DiffKind.Equal:
                    box.SelectionFont = normal;
                    box.SelectionColor = Theme.Text;
                    box.SelectionBackColor = Theme.Surface;
                    break;
                case DiffKind.Deleted:
                    box.SelectionFont = strike;
                    box.SelectionColor = Theme.Danger;
                    box.SelectionBackColor = Theme.Surface;
                    break;
                case DiffKind.Inserted:
                    box.SelectionFont = bold;
                    box.SelectionColor = Theme.Success;
                    box.SelectionBackColor = Theme.InsertBack;
                    break;
            }
            box.AppendText(part.Text);
        }

        if (result.Fixes.Count > 0)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionFont = new Font(normal.FontFamily, normal.Size * 0.9f);
            box.SelectionColor = Theme.TextDim;
            box.SelectionBackColor = Theme.Surface;
            box.AppendText("\n\n");
            foreach (var f in result.Fixes)
                box.AppendText($"• {f.From} → {f.To}: {f.Reason}\n");
        }
        box.SelectionStart = 0;
        box.ScrollToCaret();
    }

    /// <summary>Pastes the result instead of the selected text in the source app.</summary>
    private async Task ReplaceAsync()
    {
        if (_resultText is null || _input.Source is null || !_btnReplace.Enabled || !_btnReplace.Visible) return;
        var text = _resultText;
        var source = _input.Source;
        Hide();
        Close();
        await _services.Replace(source, text);
    }

    private void CopyCurrent()
    {
        try
        {
            if (_resultText is not null) Clipboard.SetText(_resultText);
            else if (_hasText && SourceText.Length > 0 && _isTable) Clipboard.SetDataObject(TableFormat.ToClipboard(SourceText), true);
            else if (_hasText && SourceText.Length > 0) Clipboard.SetText(SourceText);
            else if (_image is not null) Clipboard.SetImage(_image);
            else return;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus("Could not copy: the clipboard is busy.");
            Log.Error("Copy from action window", ex);
        }
    }

    private void ShowError(string title, Exception ex)
    {
        string hint = ex is ModelSetupException ? " Open the tray menu > Models... to download it." : "";
        SetStatus($"{title}: {ex.Message}{hint}");
    }

    private void SetStatus(string text)
    {
        if (!IsDisposed) _status.Text = text;
    }

    private void BeginInvokeSafe(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { /* the window is closing */ }
    }

    // ---------------- Last window ----------------

    /// <summary>Window snapshot on close (null — the window was empty).</summary>
    public WindowSnapshot? Snapshot { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;
        try { Snapshot = TakeSnapshot(); }
        catch (Exception ex) { Log.Error("Window snapshot", ex); }
    }

    private WindowSnapshot? TakeSnapshot()
    {
        bool text = _hasText && SourceText.Trim().Length > 0;
        if (!text && _image is null) return null;
        string? rtf = null;
        if (_resultText is not null && _resultBox is { IsDisposed: false } box)
        {
            try { rtf = box.Rtf; } catch { /* without formatting */ }
        }
        return new WindowSnapshot
        {
            Text = text ? SourceText : null,
            Image = text ? null : (Bitmap)_image!.Clone(),
            Source = _input.Source,
            SourceCaption = _sourceCaption.Text,
            ResultText = _resultText,
            ResultRtf = rtf,
            ResultCaption = _resultCaption?.Text,
            ResultLang = _resultLang,
            Target = _target,
            UserPickedTarget = _userPickedTarget,
            FilePath = _filePath,
            LoadedText = _loadedText,
            TranslatedDoc = _translatedDoc,
            IsTable = _isTable,
            Pinned = _pinned,
            Bounds = Bounds,
            Status = _status.Text,
        };
    }

    /// <summary>Restores what was in the window when it was closed.</summary>
    public void Restore(WindowSnapshot s)
    {
        _filePath = s.FilePath;
        _loadedText = s.LoadedText;
        _translatedDoc = s.TranslatedDoc;
        _isTable = s.IsTable;
        if (s.Pinned) Pinned = true;
        if (s.Target is not null)
        {
            _target = s.Target;
            _userPickedTarget = s.UserPickedTarget;
        }
        if (s.Text is not null)
        {
            _btnOcr.Visible = _btnOcrMode.Visible = false;
            _sourceCaption.Text = s.SourceCaption;
        }
        if (s.ResultText is not null && s.Text is not null)
        {
            ShowSourceAndResult();
            _sourceCaption.Text = s.SourceCaption;
            _resultCaption!.Text = s.ResultCaption ?? "RESULT";
            try
            {
                if (s.ResultRtf is not null) _resultBox!.Rtf = s.ResultRtf;
                else _resultBox!.Text = Normalize(s.ResultText);
            }
            catch { _resultBox!.Text = Normalize(s.ResultText); }
            _resultText = s.ResultText;
            _resultForSource = s.Text;
            _resultLang = s.ResultLang;
            _btnReplace.Enabled = true;
            _btnSave.Visible = _filePath is not null;
        }
        UpdateChip();
        SetStatus(s.Status.Length > 0 ? s.Status : "Last window restored");
    }

    // ---------------- Files: open, drag and drop, save ----------------

    private void EnableDrop(Control root)
    {
        root.AllowDrop = true;
        root.DragEnter -= OnDragEnter;
        root.DragEnter += OnDragEnter;
        root.DragDrop -= OnDragDrop;
        root.DragDrop += OnDragDrop;
        foreach (Control c in root.Controls) EnableDrop(c);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (_busy) { e.Effect = DragDropEffects.None; return; }
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            e.Effect = DocumentReader.IsSupported(files[0]) ? DragDropEffects.Copy : DragDropEffects.None;
        else if (e.Data?.GetDataPresent(DataFormats.UnicodeText) == true || e.Data?.GetDataPresent(DataFormats.Text) == true)
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            if (files.Length > 1) SetStatus("One file at a time: opening the first one.");
            Native.ForceForeground(Handle);
            BeginInvokeSafe(() => _ = LoadFileAsync(files[0], translate: true));
            return;
        }
        var text = e.Data?.GetData(DataFormats.UnicodeText) as string ?? e.Data?.GetData(DataFormats.Text) as string;
        if (!string.IsNullOrEmpty(text))
        {
            Native.ForceForeground(Handle);
            InsertText(text);
        }
    }

    private void OpenFile()
    {
        if (_busy) return;
        using var dlg = new OpenFileDialog
        {
            Title = "Open a file to translate",
            Filter = DocumentReader.OpenFilter,
            InitialDirectory = _filePath is not null ? Path.GetDirectoryName(_filePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        Pinned = true; // the dialog takes focus — our window must not close
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _ = LoadFileAsync(dlg.FileName, translate: true);
    }

    /// <summary>
    /// Opens a file: shows an image as a snapshot, takes the text from a document
    /// (recognizes PDF scans). translate — translate right away.
    /// </summary>
    public async Task LoadFileAsync(string path, bool translate)
    {
        if (_busy) return;
        if (!File.Exists(path)) { SetStatus("File not found: " + path); return; }
        if (!DocumentReader.IsSupported(path))
        {
            SetStatus($"This file type is not supported ({Path.GetExtension(path)}). Text, Word, PowerPoint, PDF and images work.");
            return;
        }
        Pinned = true;
        string name = Path.GetFileName(path);

        if (DocumentReader.IsImage(path))
        {
            Bitmap bmp;
            try
            {
                using var fs = File.OpenRead(path);
                using var img = Image.FromStream(fs);
                bmp = new Bitmap(img);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open the image: {ex.Message}");
                return;
            }
            if (!ReferenceEquals(_image, _input.Image)) _image?.Dispose();
            _image = bmp;
            _filePath = path;
            _translatedDoc = null;
            _loadedText = null;
            _hasText = false;
            _resultText = null;
            _isTable = false;
            _sourceBox.Text = "";
            _btnOcr.Visible = _btnOcrMode.Visible = _btnOcr.Enabled = true;
            _btnSave.Visible = false;
            _btnQr.Visible = false;
            _resultBox = null;
            ShowImage();
            SetStatus($"{name} · 1 translate · 2 recognize text");
            _ = DetectQrAsync(bmp);
            if (translate) await TranslateAsync();
            return;
        }

        _busy = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var progress = new Progress<string>(SetStatus);
        string text;
        int scanned = 0;
        try
        {
            SetStatus($"Reading {name}...");
            if (DocumentReader.IsPdf(path))
            {
                var pages = await Task.Run(() => DocumentReader.ReadPdf(path, progress, _cts.Token));
                var parts = new List<string>();
                int needOcr = pages.Count(p => p.ScanImage is not null);
                foreach (var page in pages)
                {
                    string pageText = page.Text;
                    if (page.ScanImage is not null)
                    {
                        scanned++;
                        SetStatus(needOcr == 1 ? "Recognizing the scanned page..." : $"Recognizing scanned page {scanned} of {needOcr}...");
                        using var ms = new MemoryStream(page.ScanImage);
                        using var img = Image.FromStream(ms);
                        using var bmp = new Bitmap(img);
                        var ocr = await _services.Ocr(bmp, new Progress<string>(_ => { }), _cts.Token, OcrMode.Text);
                        if (!string.IsNullOrWhiteSpace(ocr)) pageText = ocr.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(pageText)) parts.Add(pageText);
                }
                text = string.Join("\n\n", parts);
            }
            else
            {
                text = await Task.Run(() => Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".docx" => DocumentReader.ReadDocx(path),
                    ".pptx" => DocumentReader.ReadPptx(path),
                    ".rtf" => null!,
                    _ => DocumentReader.ReadTextFile(path),
                });
                if (text is null) text = RtfToText(path);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error($"Open file {path}", ex);
            ShowError($"Could not read {name}", ex);
            return;
        }
        finally
        {
            _busy = false;
        }
        if (IsDisposed) return;

        text = text.Replace("\0", "").Trim();
        if (text.Length == 0)
        {
            SetStatus($"No text found in {name}.");
            return;
        }
        if (text.Length > DocumentReader.MaxChars)
        {
            SetStatus($"{name} is too big: {text.Length:N0} characters (up to {DocumentReader.MaxChars:N0}).");
            return;
        }

        _filePath = path;
        _isTable = false;
        _resultText = null;
        _btnSave.Visible = false;
        GrowForDocument();
        _btnOcr.Visible = _btnOcrMode.Visible = false;
        _translatedDoc = null;
        SetSourceText(text);
        _loadedText = SourceText;
        ShowSourceOnly();
        _sourceCaption.Text = "FILE · " + name.ToUpperInvariant();
        _sourceBox.SelectionStart = 0;
        _sourceBox.ScrollToCaret();
        string scans = scanned > 0 ? $" ({scanned} scanned page{(scanned == 1 ? "" : "s")} recognized)" : "";
        SetStatus($"{name}: {text.Length:N0} characters{scans} · {sw.Elapsed.TotalSeconds:0.0} s · 1 translate · Ctrl+S save the result");
        _services.AddHistory("File", name, text);
        if (translate) await TranslateAsync();
    }

    /// <summary>A bigger window for documents (within the screen).</summary>
    private void GrowForDocument()
    {
        var area = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(Math.Max(Width, (int)(920 * DeviceDpi / 96f)), area.Width - 40);
        int h = Math.Min(Math.Max(Height, (int)(700 * DeviceDpi / 96f)), area.Height - 40);
        if (w == Width && h == Height) return;
        Bounds = new Rectangle(Math.Clamp(Left, area.Left, area.Right - w), Math.Clamp(Top, area.Top, area.Bottom - h), w, h);
    }

    private static string RtfToText(string path)
    {
        using var rtb = new RichTextBox();
        rtb.Rtf = File.ReadAllText(path);
        return rtb.Text;
    }

    /// <summary>Saves the result (translation) to a file next to the source.</summary>
    private void SaveResult()
    {
        if (_resultText is null)
        {
            SetStatus("Nothing to save yet: translate first.");
            return;
        }
        string dir = _filePath is not null ? Path.GetDirectoryName(_filePath)! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string baseName = _filePath is not null ? Path.GetFileNameWithoutExtension(_filePath) : "translation";
        string ext = _translatedDoc is not null ? Path.GetExtension(_filePath!).ToLowerInvariant()
            : _filePath is not null ? DocumentReader.ResultExtension(_filePath) : ".txt";
        string suffix = _resultLang is not null ? "." + _resultLang.Code : "";
        string kind = ext switch { ".docx" => "Word document", ".pptx" => "PowerPoint presentation", _ => ext.TrimStart('.').ToUpperInvariant() + " file" };
        using var dlg = new SaveFileDialog
        {
            Title = "Save the result",
            InitialDirectory = dir,
            FileName = baseName + suffix + ext,
            Filter = $"{kind}|*{ext}|Text file|*.txt|All files|*.*",
            OverwritePrompt = true,
        };
        Pinned = true;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string outExt = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            if (_translatedDoc is not null && outExt == ext)
            {
                File.WriteAllBytes(dlg.FileName, _translatedDoc);
            }
            else
            {
                // Notepad, Excel and subtitle editors understand UTF-8 with BOM more reliably.
                bool bom = outExt is ".txt" or ".srt" or ".vtt" or ".csv" or ".tsv" or ".ass" or ".ssa" or ".log";
                File.WriteAllText(dlg.FileName, Normalize(_resultText), new System.Text.UTF8Encoding(bom));
            }
            SetStatus("Saved: " + dlg.FileName);
            Log.Info("Saved result to " + dlg.FileName);
        }
        catch (Exception ex)
        {
            SetStatus("Could not save: " + ex.Message);
        }
    }

    /// <summary>A Word / PowerPoint document is open and its text was not edited.</summary>
    private bool IsOfficeOriginal =>
        _filePath is not null && OfficeTranslator.IsSupported(_filePath) && _loadedText is not null && SourceText == _loadedText;

    // ---------------- Voice ----------------

    /// <summary>Dictation into the text box. Calling again stops the recording.</summary>
    public async Task DictateAsync()
    {
        if (_services.Dictate is null) return;
        if (_dictating)
        {
            _services.StopDictation?.Invoke(false);
            return;
        }
        if (_busy) return;
        _dictating = true;
        _btnMic.Kind = ButtonKind.Accent;
        _btnMic.Selected = true;
        SetStatus("Listening... click the microphone again or pause to finish · Esc cancel");
        try
        {
            var text = await _services.Dictate(new Progress<string>(SetStatus));
            if (IsDisposed) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("No speech recognized.");
                return;
            }
            InsertText(text);
            SetStatus($"Dictated {text.Length} characters · 1 translate · 3 check grammar");
        }
        catch (Exception ex)
        {
            Log.Error("Dictation", ex);
            ShowError("Voice input failed", ex);
        }
        finally
        {
            _dictating = false;
            if (!IsDisposed)
            {
                _btnMic.Kind = ButtonKind.Ghost;
                _btnMic.Selected = false;
                Native.ForceForeground(Handle);
            }
        }
    }

    /// <summary>Inserts text into the box (at the cursor). If an image is shown, switches to text.</summary>
    private void InsertText(string text)
    {
        if (!_hasText)
        {
            SetSourceText(text);
            ShowSourceOnly();
        }
        else
        {
            int at = _sourceBox.SelectionStart;
            bool needSpace = at > 0 && _sourceBox.SelectionLength == 0 && !char.IsWhiteSpace(_sourceBox.Text[at - 1]);
            _sourceBox.SelectedText = (needSpace ? " " : "") + Normalize(text);
        }
        _sourceBox.Modified = true;
        _btnOcr.Visible = _btnOcrMode.Visible = false;
        _sourceBox.Focus();
    }

    // ---------------- Borderless window: shadow, dragging, resizing ----------------

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
        HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_NCHITTEST || (int)m.Result != HTCLIENT) return;

        var p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
        int g = (int)(6 * DeviceDpi / 96f);
        bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g, b = p.Y >= ClientSize.Height - g;
        int hit = (t, b, l, r) switch
        {
            (true, _, true, _) => HTTOPLEFT,
            (true, _, _, true) => HTTOPRIGHT,
            (_, true, true, _) => HTBOTTOMLEFT,
            (_, true, _, true) => HTBOTTOMRIGHT,
            (true, _, _, _) => HTTOP,
            (_, true, _, _) => HTBOTTOM,
            (_, _, true, _) => HTLEFT,
            (_, _, _, true) => HTRIGHT,
            _ => HTCLIENT,
        };
        m.Result = hit;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Thin border (on Windows 11 the DWM rounded border complements it).
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    // ---------------- Window ----------------

    /// <summary>Shows the window next to the cursor without leaving the monitor.</summary>
    public void ShowNearCursor()
    {
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        int x = Math.Clamp(cursor.X + 16, area.Left, Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(cursor.Y + 16, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Location = new Point(x, y);
        ShowAndFocus();
    }

    /// <summary>Shows the window where it was (if that place is still on screen).</summary>
    public void ShowAt(Rectangle bounds)
    {
        if (bounds.Width < 200 || !Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(bounds)))
        {
            ShowNearCursor();
            return;
        }
        var area = Screen.FromRectangle(bounds).WorkingArea;
        int w = Math.Min(bounds.Width, area.Width), h = Math.Min(bounds.Height, area.Height);
        Bounds = new Rectangle(Math.Clamp(bounds.X, area.Left, area.Right - w), Math.Clamp(bounds.Y, area.Top, area.Bottom - h), w, h);
        ShowAndFocus();
    }

    private void ShowAndFocus()
    {
        Show();
        Native.ForceForeground(Handle);
        Activate();
        if (_hasText)
        {
            _sourceBox.Focus();
            _sourceBox.SelectionStart = _sourceBox.TextLength;
            _sourceBox.SelectionLength = 0;
        }
        UpdateHints();
        _services.PreloadText?.Invoke();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Speech.Stop();
            Close();
            return true;
        }

        // While the cursor is in the input box, digits are typed; actions are on Ctrl+digit.
        var key = keyData & Keys.KeyCode;
        bool ctrl = (keyData & Keys.Modifiers) == Keys.Control;
        bool plain = (keyData & Keys.Modifiers) == Keys.None;
        bool typing = _sourceBox.Focused;
        if (!(ctrl || (plain && !typing))) return base.ProcessCmdKey(ref msg, keyData);

        switch (key)
        {
            case Keys.O when ctrl:
                OpenFile();
                return true;
            case Keys.S when ctrl:
                SaveResult();
                return true;
            case Keys.M when ctrl:
                _ = DictateAsync();
                return true;
            case Keys.D1 or Keys.NumPad1:
                _ = TranslateAsync();
                return true;
            case Keys.D2 or Keys.NumPad2:
                if (_btnOcr.Visible && _btnOcr.Enabled) _ = RunOcrAsync();
                return true;
            case Keys.D3 or Keys.NumPad3:
                _ = CheckGrammarAsync();
                return true;
            case Keys.D4 or Keys.NumPad4:
                CopyCurrent();
                return true;
            case Keys.D5 or Keys.NumPad5:
                _ = ReplaceAsync();
                return true;
            case Keys.Enter when ctrl:
                _ = TranslateAsync();
                return true;
            case Keys.D6 or Keys.D7 or Keys.D8 or Keys.D9 or Keys.NumPad6 or Keys.NumPad7 or Keys.NumPad8 or Keys.NumPad9:
            {
                int idx = key switch { Keys.D6 or Keys.NumPad6 => 0, Keys.D7 or Keys.NumPad7 => 1, Keys.D8 or Keys.NumPad8 => 2, _ => 3 };
                var list = _services.CustomActions();
                if (idx < list.Count) _ = RunCustomAsync(list[idx]);
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Like a popup menu: focus lost — the window closes. Not if the text was edited or the language list is open.
        if (IsHandleCreated && !IsDisposed && !Disposing && Visible && !_sourceBox.Modified && !_pinned && !_dictating)
            BeginInvoke(() => { if (!IsDisposed && !ContainsFocus && !_langMenu.Visible && !_ocrMenu.Visible && !_actionsMenu.Visible && Form.ActiveForm != this) Close(); });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _cts.Cancel(); // no Dispose: the token may still be checked by an unfinished request
        Speech.Stop();
        _langMenu.Dispose();
        _ocrMenu.Dispose();
        _actionsMenu.Dispose();
        _tips.Dispose();
        if (_dictating) _services.StopDictation?.Invoke(true);
        if (!ReferenceEquals(_image, _input.Image)) _image?.Dispose();
        _input.Dispose();
    }

    /// <summary>Round "Yu" badge in the accent color.</summary>
    private sealed class BadgeLabel : Control
    {
        public BadgeLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int d = Math.Min(Width, Height) - 6;
            var r = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);
            using (var path = Theme.RoundRect(r, 6))
            using (var b = new SolidBrush(Theme.Accent))
                g.FillPath(b, path);
            using var f = new Font(Theme.FontName, d * 0.36f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "Yu", f, r, Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
