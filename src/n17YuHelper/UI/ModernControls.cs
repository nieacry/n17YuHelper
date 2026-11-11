using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace N17YuHelper.UI;

public enum ButtonKind
{
    /// <summary>Regular button with a background.</summary>
    Normal,
    /// <summary>Main action — accent color.</summary>
    Accent,
    /// <summary>No background, highlighted on hover.</summary>
    Ghost,
}

/// <summary>Rounded Windows 11 style button with an optional key hint on the right.</summary>
internal class ModernButton : Control
{
    private bool _hover, _down;
    private ButtonKind _kind;
    private string _hint = "";
    private string _glyph = "";
    private bool _selected;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(9.5f);
        Height = 32;
        Margin = new Padding(0, 0, 6, 0);
        BackColor = Color.Transparent;
        TabStop = true;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    /// <summary>Key hint (e.g. "1" or "Ctrl+1"), drawn as a small badge.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Hint { get => _hint; set { _hint = value ?? ""; FitWidth(); Invalidate(); } }

    /// <summary>Glyph from the Segoe Fluent Icons / MDL2 Assets font (e.g. "\uE8C8" — copy).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Glyph { get => _glyph; set { _glyph = value ?? ""; FitWidth(); Invalidate(); } }

    /// <summary>Selected state (for navigation items).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    /// <summary>Left-aligned text (for navigation).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AlignLeft { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AutoFit { get; set; } = true;

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitWidth();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FitWidth();
    }

    private void FitWidth()
    {
        if (!AutoFit) return;
        int w = (Text.Length > 0 ? TextRenderer.MeasureText(Text, Font).Width : 0) + 24;
        if (_glyph.Length > 0) w += GlyphWidth + (Text.Length > 0 ? 8 : 0);
        if (_hint.Length > 0) w += HintWidth() + 8;
        Width = Math.Max(Height, w);
    }

    private int HintWidth()
    {
        using var f = HintFont();
        return TextRenderer.MeasureText(_hint, f).Width + 6;
    }

    private Font HintFont() => new(Font.FontFamily, Font.Size * 0.8f);

    private int GlyphWidth => (int)(Font.Size * 1.6f * DeviceDpi / 96f);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    public void PerformClickSafe() => OnClick(EventArgs.Empty);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var parentBack = Parent?.BackColor ?? Theme.Bg;
        g.Clear(parentBack);

        Color back, fore, border;
        switch (_kind)
        {
            case ButtonKind.Accent:
                back = !Enabled ? Theme.Pressed : _down ? Theme.AccentHover : _hover ? Theme.AccentHover : Theme.Accent;
                fore = Enabled ? Theme.AccentText : Theme.TextDim;
                border = back;
                break;
            case ButtonKind.Ghost:
                back = _selected ? Theme.Hover : _down ? Theme.Pressed : _hover && Enabled ? Theme.Hover : parentBack;
                fore = Enabled ? Theme.Text : Theme.TextDim;
                border = back;
                break;
            default:
                back = !Enabled ? Theme.SurfaceAlt : _down ? Theme.Pressed : _hover ? Theme.Hover : Theme.Surface;
                fore = Enabled ? Theme.Text : Theme.TextDim;
                border = Theme.Border;
                break;
        }

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundRect(rect, 5))
        {
            using var b = new SolidBrush(back);
            g.FillPath(b, path);
            using var p = new Pen(Focused && ShowFocusCues ? Theme.Accent : border);
            g.DrawPath(p, path);
        }

        // Accent bar on the selected navigation item.
        if (_selected && _kind == ButtonKind.Ghost)
        {
            using var accent = new SolidBrush(Theme.Accent);
            using var bar = Theme.RoundRect(new RectangleF(2, Height * 0.28f, 3, Height * 0.44f), 1.5f);
            g.FillPath(accent, bar);
        }

        int hintW = _hint.Length > 0 ? HintWidth() : 0;
        int left = AlignLeft ? 14 : 12;
        int contentRight = Width - (hintW > 0 ? hintW + 8 : 0) - (AlignLeft ? 0 : 6);

        // Icon to the left of the text.
        if (_glyph.Length > 0)
        {
            using var iconFont = new Font(Theme.IconFontName, Font.Size * 1.05f);
            var gr = new Rectangle(left, 0, GlyphWidth, Height);
            if (Text.Length == 0) gr = new Rectangle(0, 0, Width - (hintW > 0 ? hintW + 8 : 0), Height);
            var iconColor = _kind == ButtonKind.Accent ? fore : Enabled ? Theme.Accent : Theme.TextDim;
            if (_kind == ButtonKind.Ghost && Text.Length == 0) iconColor = fore;
            TextRenderer.DrawText(g, _glyph, iconFont, gr, iconColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            left += GlyphWidth + 6;
        }

        if (Text.Length > 0)
        {
            bool centered = !AlignLeft && _glyph.Length == 0 && hintW == 0;
            var textRect = centered ? new Rectangle(0, 0, Width, Height) : new Rectangle(left, 0, Math.Max(0, contentRight - left), Height);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                        (centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            TextRenderer.DrawText(g, Text, Font, textRect, fore, flags);
        }

        if (hintW > 0)
        {
            using var hf = HintFont();
            var hr = new RectangleF(Width - hintW - 8, (Height - 18) / 2f, hintW, 18);
            var hintBack = _kind == ButtonKind.Accent ? Color.FromArgb(50, Theme.AccentText) : Color.FromArgb(Theme.IsDark ? 40 : 18, Theme.Text);
            using (var hp = Theme.RoundRect(hr, 4))
            using (var hb = new SolidBrush(hintBack))
                g.FillPath(hb, hp);
            var hintFore = _kind == ButtonKind.Accent ? Theme.AccentText : Theme.TextDim;
            TextRenderer.DrawText(g, _hint, hf, Rectangle.Round(hr), hintFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}

/// <summary>"Card" panel with a rounded border. The inner control is placed with padding.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(10, 8, 6, 8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundRect(r, 8);
        using var b = new SolidBrush(Theme.Surface);
        g.FillPath(b, path);
        if (Mark is { } mark)
        {
            // Colored bar on the left and a border of the same color.
            var state = g.Save();
            g.SetClip(path);
            float strip = 5f * DeviceDpi / 96f;
            using (var mb = new SolidBrush(mark)) g.FillRectangle(mb, 0, 0, strip, Height);
            g.Restore(state);
            using var mp = new Pen(Color.FromArgb(200, mark), 1.5f);
            g.DrawPath(mp, path);
            return;
        }
        using var p = new Pen(Theme.Border);
        g.DrawPath(p, path);
    }

    /// <summary>Mark color (green — recommended, red — not). Null — a plain card.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color? Mark { get; set { field = value; Invalidate(); } }
}

/// <summary>Thin "working" bar under the header (a running segment in the accent color).</summary>
internal sealed class BusyLine : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private float _pos;
    private bool _running;

    public BusyLine()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 2;
        _timer.Tick += (_, _) => { _pos = (_pos + 0.012f) % 1.4f; Invalidate(); };
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Running
    {
        get => _running;
        set
        {
            if (_running == value) return;
            _running = value;
            if (value) { _pos = 0; _timer.Start(); } else _timer.Stop();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
        if (!_running) return;
        float w = Width * 0.3f;
        float x = (_pos - 0.3f) * Width;
        using var b = new SolidBrush(Theme.Accent);
        e.Graphics.FillRectangle(b, x, 0, w, Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Small caption above a card ("ORIGINAL", "TRANSLATION").</summary>
internal sealed class Caption : Label
{
    public Caption(string text)
    {
        Text = text.ToUpperInvariant();
        Dock = DockStyle.Top;
        Height = 22;
        Font = Theme.UiFont(7.75f, FontStyle.Bold);
        ForeColor = Theme.TextDim;
        BackColor = Theme.Bg;
        Padding = new Padding(4, 4, 0, 0);
        Tag = "dim";
    }
}
