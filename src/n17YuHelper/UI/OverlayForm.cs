using System.Runtime.InteropServices;

namespace N17YuHelper.UI;

/// <summary>
/// Перевод поверх оригинала: полупрозрачное окно на месте обведённой области.
/// Не забирает фокус и не попадает в снимки экрана (поэтому в живом режиме
/// программа видит под ним оригинал). Наведи мышь — появятся кнопки:
/// живое обновление, копировать, закрыть. Тянуть — за любое место.
/// </summary>
internal sealed class OverlayForm : Form
{
    private readonly Label _text;
    private readonly FlowLayoutPanel _tools;
    private readonly ModernButton _live;
    private readonly Func<CancellationToken, Task<string?>>? _refresh;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 200 };
    private readonly CancellationTokenSource _cts = new();
    private bool _working;

    public OverlayForm(Rectangle region, string text, Func<CancellationToken, Task<string?>>? refresh, int intervalMs, bool startLive)
    {
        _refresh = refresh;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Surface;
        Opacity = 0.94;
        Padding = new Padding(1);
        DoubleBuffered = true;

        _text = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Padding = new Padding(10, 8, 10, 8),
            Text = text,
            UseMnemonic = false,
            AutoEllipsis = true,
        };

        _tools = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Surface,
            Visible = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _live = Tool("", "Live: re-translate when the region changes", ToggleLive);
        _tools.Controls.Add(_live);
        _tools.Controls.Add(Tool("", "Copy", CopyText));
        _tools.Controls.Add(Tool("", "Close", Close));
        _live.Visible = refresh is not null;

        Controls.Add(_tools);
        Controls.Add(_text);

        // Положение: прямо на месте области; если текст не помещается — вырастаем вниз.
        Bounds = region;
        FitFont();

        _timer.Interval = Math.Max(500, intervalMs);
        _timer.Tick += async (_, _) => await RefreshAsync();
        _hoverTimer.Tick += (_, _) => UpdateHover();
        _hoverTimer.Start();

        foreach (Control c in new Control[] { this, _text }) c.MouseDown += Drag;
        _text.DoubleClick += (_, _) => CopyText();

        if (startLive && refresh is not null) BeginInvokeWhenReady(ToggleLive);
    }

    private ModernButton Tool(string glyph, string tip, Action click)
    {
        var b = new ModernButton { Glyph = glyph, Kind = ButtonKind.Ghost, AutoFit = false, Width = 30, Height = 28, Margin = new Padding(0) };
        new ToolTip().SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    public void SetText(string text)
    {
        if (IsDisposed) return;
        _text.Text = text;
        FitFont();
    }

    /// <summary>Подбирает шрифт: от 12 pt вниз до 8 pt; если и так не влезает — окно растёт по высоте.</summary>
    private void FitFont()
    {
        var area = new Size(Math.Max(60, Width - 22), int.MaxValue);
        for (float size = 12f; size >= 8f; size -= 0.5f)
        {
            using var f = Theme.UiFont(size);
            var need = TextRenderer.MeasureText(_text.Text, f, area, TextFormatFlags.WordBreak);
            if (need.Height + 18 <= Height || size <= 8f)
            {
                _text.Font = Theme.UiFont(size);
                if (need.Height + 18 > Height) Height = Math.Min(need.Height + 20, Screen.FromRectangle(Bounds).WorkingArea.Height / 2);
                break;
            }
        }
        _tools.Location = new Point(Width - _tools.Width - 4, 4);
    }

    private void ToggleLive()
    {
        if (_refresh is null) return;
        if (_timer.Enabled)
        {
            _timer.Stop();
            _live.Glyph = "";
            _live.Kind = ButtonKind.Ghost;
            Invalidate();
        }
        else
        {
            _timer.Start();
            _live.Glyph = "";
            _live.Kind = ButtonKind.Accent;
            Invalidate();
            Log.Info("Overlay: live mode on");
        }
    }

    private async Task RefreshAsync()
    {
        if (_working || _refresh is null || IsDisposed) return;
        _working = true;
        try
        {
            var text = await _refresh(_cts.Token);
            if (text is not null && !IsDisposed && text != _text.Text) SetText(text);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Overlay refresh", ex);
        }
        finally
        {
            _working = false;
        }
    }

    private void CopyText()
    {
        try { Clipboard.SetText(_text.Text); }
        catch (Exception ex) { Log.Error("Overlay copy", ex); }
    }

    /// <summary>Кнопки видны, пока мышь над окном.</summary>
    private void UpdateHover()
    {
        bool inside = Bounds.Contains(Cursor.Position);
        if (_tools.Visible != inside)
        {
            _tools.Visible = inside;
            if (inside) _tools.BringToFront();
        }
    }

    private void Drag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
    }

    private void BeginInvokeWhenReady(Action a)
    {
        if (IsHandleCreated) BeginInvoke(a);
        else HandleCreated += (_, _) => BeginInvoke(a);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            cp.ClassStyle |= 0x00020000; // тень
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDwm(Handle);
        // Окно не попадает в снимки экрана — в живом режиме видим под ним оригинал.
        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_timer.Enabled ? Theme.Accent : Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts.Cancel();
        _timer.Dispose();
        _hoverTimer.Dispose();
        base.OnFormClosed(e);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
