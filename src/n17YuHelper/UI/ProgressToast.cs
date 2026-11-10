namespace N17YuHelper.UI;

/// <summary>
/// Маленькая подсказка у курсора («Recognizing text...», «Copied»), не забирает фокус.
/// </summary>
internal sealed class ProgressToast : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;

    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _closeTimer = new();

    public ProgressToast(string text)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        BackColor = Theme.Surface;
        Padding = new Padding(1);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _label = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Font = Theme.UiFont(9.5f),
            Padding = new Padding(22, 8, 12, 8),
            Text = text,
        };
        Controls.Add(_label);

        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); Close(); };
        _label.Paint += (_, e) =>
        {
            // Точка цвета акцента слева от текста.
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int d = (int)(7 * DeviceDpi / 96f);
            using var b = new SolidBrush(Theme.Accent);
            e.Graphics.FillEllipse(b, (int)(10 * DeviceDpi / 96f), (_label.Height - d) / 2, d, d);
        };
        HandleCreated += (_, _) => Theme.ApplyDwm(Handle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    public void ShowNearCursor()
    {
        var c = Cursor.Position;
        var area = Screen.FromPoint(c).WorkingArea;
        Show();
        int x = Math.Clamp(c.X + 18, area.Left, Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(c.Y + 18, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Location = new Point(x, y);
    }

    public void SetText(string text)
    {
        if (IsDisposed) return;
        _label.Text = text;
    }

    /// <summary>Показывает текст и закрывается через заданное время.</summary>
    public void CloseAfter(string text, int ms)
    {
        if (IsDisposed) return;
        SetText(text);
        _closeTimer.Interval = Math.Max(100, ms);
        _closeTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _closeTimer.Dispose();
        base.Dispose(disposing);
    }
}
