using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace N17YuHelper.Capture;

/// <summary>Результат выделения: прямоугольник в координатах экрана и картинка.</summary>
public sealed class RegionCaptureResult(Rectangle screenRect, Bitmap image) : IDisposable
{
    public Rectangle ScreenRect { get; } = screenRect;
    public Bitmap Image { get; } = image;
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// Рамка выделения как в ShareX: снимок всех мониторов, затемнение,
/// перекрестие, лупа, прилипание к окнам и элементам.
/// ЛКМ+тянуть — область; клик — окно/элемент под курсором;
/// ПКМ — сбросить выделение или выйти; Esc — выйти; Enter — взять окно под курсором;
/// стрелки — сдвиг курсора на 1 px (с Shift — на 10 px).
/// </summary>
internal sealed class RegionCaptureForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int MagnifierPixels = 15;   // сколько пикселей экрана видно в лупе
    private const int MagnifierZoom = 8;

    private readonly Bitmap _shot;
    private readonly Bitmap _dimmed;
    private readonly Rectangle _virtual;
    private readonly List<Rectangle> _snapRects;
    private readonly Rectangle[] _screens;
    private readonly bool _showMagnifier;

    private Point _cursor;
    private bool _dragging;
    private Point _dragStart;
    private Rectangle _selection;
    private Rectangle _hover;
    private Rectangle _lastActive;
    private readonly List<Rectangle> _lastDecor = [];

    private Font? _font;
    private readonly Pen _crossPen = new(Color.FromArgb(150, 255, 255, 255), 1) { DashStyle = DashStyle.Dot };
    private readonly Pen _selPen = new(UI.Theme.Accent, 2);
    private readonly Pen _hoverPen = new(UI.Theme.Accent, 2) { DashStyle = DashStyle.Dash };
    private readonly SolidBrush _labelBack = new(Color.FromArgb(210, 20, 20, 20));

    private Rectangle? _result;

    private RegionCaptureForm(Bitmap shot, Bitmap dimmed, Rectangle virtualScreen, List<Rectangle> snapRects, Rectangle[] screens, bool showMagnifier)
    {
        _shot = shot;
        _dimmed = dimmed;
        _virtual = virtualScreen;
        _snapRects = snapRects;
        _screens = screens;
        _showMagnifier = showMagnifier;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        Text = "n17 YuHelper - region capture";
        Bounds = virtualScreen;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }

    /// <summary>Показывает рамку и возвращает выделенную область или null при отмене.</summary>
    public static RegionCaptureResult? Run(AppSettings settings)
    {
        var vs = SystemInformation.VirtualScreen;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var shot = CaptureScreen(vs);

        var dimmed = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(dimmed))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(shot, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, vs.Width, vs.Height);
        }

        var client = new Rectangle(Point.Empty, vs.Size);
        var snap = new List<Rectangle>();
        if (settings.SnapToWindows)
        {
            foreach (var r in WindowFinder.Collect())
            {
                var lr = Rectangle.Intersect(new Rectangle(r.X - vs.X, r.Y - vs.Y, r.Width, r.Height), client);
                if (lr.Width > 4 && lr.Height > 4) snap.Add(lr);
            }
        }
        var screens = Screen.AllScreens
            .Select(s => new Rectangle(s.Bounds.X - vs.X, s.Bounds.Y - vs.Y, s.Bounds.Width, s.Bounds.Height))
            .ToArray();

        Log.Info($"Screenshot {vs.Width}x{vs.Height}, {snap.Count} snap regions, {sw.ElapsedMilliseconds} ms");

        Rectangle? local;
        using (var form = new RegionCaptureForm(shot, dimmed, vs, snap, screens, settings.ShowMagnifier))
        {
            form.ShowDialog();
            local = form._result;
        }

        RegionCaptureResult? result = null;
        if (local is { } rect)
        {
            var image = shot.Clone(rect, PixelFormat.Format24bppRgb);
            result = new RegionCaptureResult(new Rectangle(rect.X + vs.X, rect.Y + vs.Y, rect.Width, rect.Height), image);
            Log.Info($"Selected region {result.ScreenRect}");
        }

        shot.Dispose();
        dimmed.Dispose();
        return result;
    }

    /// <summary>
    /// Снимок всего виртуального экрана через BitBlt (SRCCOPY | CAPTUREBLT —
    /// чтобы попали и полупрозрачные окна). Graphics.CopyFromScreen не принимает
    /// комбинацию этих флагов, поэтому вызываем GDI напрямую.
    /// </summary>
    /// <summary>Снимок прямоугольника экрана (координаты экрана), 24 бита.</summary>
    public static Bitmap CaptureRect(Rectangle r)
    {
        using var full = CaptureScreen(r);
        return full.Clone(new Rectangle(Point.Empty, r.Size), PixelFormat.Format24bppRgb);
    }

    private static Bitmap CaptureScreen(Rectangle vs)
    {
        var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        IntPtr dest = g.GetHdc();
        IntPtr src = Native.GetDC(IntPtr.Zero);
        try
        {
            if (!Native.BitBlt(dest, 0, 0, vs.Width, vs.Height, src, vs.X, vs.Y, Native.SRCCOPY | Native.CAPTUREBLT))
                throw new InvalidOperationException("BitBlt failed: could not capture the screen.");
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, src);
            g.ReleaseHdc(dest);
        }
        return bmp;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Окно растянуто на все мониторы — не даём WinForms менять его размер при смене DPI.
        if (m.Msg == Native.WM_DPICHANGED)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _virtual;
        float scale = DeviceDpi / 96f;
        _font = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        Native.ForceForeground(Handle);
        Activate();
        _cursor = PointToClient(Cursor.Position);
        _hover = FindSnap(_cursor);
        Invalidate();
    }

    // ---------------- Ввод ----------------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        MoveCursorTo(e.Location);
    }

    private void MoveCursorTo(Point p)
    {
        _cursor = p;
        if (_dragging)
            _selection = Normalize(_dragStart, _cursor);
        else
            _hover = FindSnap(_cursor);
        Redraw();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStart = e.Location;
            _selection = Normalize(_dragStart, _dragStart);
            Redraw();
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (_dragging)
            {
                _dragging = false;
                _selection = Rectangle.Empty;
                _hover = FindSnap(_cursor);
                Redraw();
            }
            else
            {
                Cancel();
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_dragging) return;
        _dragging = false;

        bool wasDrag = Math.Abs(e.X - _dragStart.X) >= 3 || Math.Abs(e.Y - _dragStart.Y) >= 3;
        if (wasDrag)
            Finish(_selection);
        else if (!_hover.IsEmpty)
            Finish(_hover);
        else
        {
            _selection = Rectangle.Empty;
            Redraw();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        int step = (keyData & Keys.Shift) != 0 ? 10 : 1;
        switch (key)
        {
            case Keys.Escape:
                Cancel();
                return true;
            case Keys.Enter:
                if (_dragging && _selection.Width > 0) Finish(_selection);
                else if (!_hover.IsEmpty) Finish(_hover);
                return true;
            case Keys.Left: NudgeCursor(-step, 0); return true;
            case Keys.Right: NudgeCursor(step, 0); return true;
            case Keys.Up: NudgeCursor(0, -step); return true;
            case Keys.Down: NudgeCursor(0, step); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void NudgeCursor(int dx, int dy)
    {
        var p = new Point(
            Math.Clamp(_cursor.X + dx, 0, ClientSize.Width - 1),
            Math.Clamp(_cursor.Y + dy, 0, ClientSize.Height - 1));
        Cursor.Position = PointToScreen(p);
        MoveCursorTo(p);
    }

    private void Finish(Rectangle r)
    {
        r = Rectangle.Intersect(r, new Rectangle(Point.Empty, ClientSize));
        if (r.Width < 1 || r.Height < 1) return;
        _result = r;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Cancel()
    {
        _result = null;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private static Rectangle Normalize(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);

    private Rectangle FindSnap(Point p)
    {
        foreach (var r in _snapRects)
            if (r.Contains(p)) return r;
        return Rectangle.Empty;
    }

    private Rectangle ActiveRect => _dragging ? _selection : _hover;

    // ---------------- Перерисовка только изменившихся частей ----------------

    private void Redraw()
    {
        var active = ActiveRect;

        // Внутренность выделения: перерисовываем только то, что сменило «затемнено/светло».
        if (active != _lastActive)
        {
            using var region = new Region(_lastActive);
            region.Xor(active);
            Invalidate(region);
            foreach (var r in Ring(_lastActive, 4)) Invalidate(r);
            foreach (var r in Ring(active, 4)) Invalidate(r);
            _lastActive = active;
        }

        var decor = DecorationRects(active);
        foreach (var r in _lastDecor) Invalidate(r);
        foreach (var r in decor) Invalidate(r);
        _lastDecor.Clear();
        _lastDecor.AddRange(decor);
    }

    private static IEnumerable<Rectangle> Ring(Rectangle r, int t)
    {
        if (r.IsEmpty) yield break;
        yield return new Rectangle(r.Left - t, r.Top - t, r.Width + 2 * t, 2 * t);
        yield return new Rectangle(r.Left - t, r.Bottom - t, r.Width + 2 * t, 2 * t);
        yield return new Rectangle(r.Left - t, r.Top - t, 2 * t, r.Height + 2 * t);
        yield return new Rectangle(r.Right - t, r.Top - t, 2 * t, r.Height + 2 * t);
    }

    private List<Rectangle> DecorationRects(Rectangle active)
    {
        var list = new List<Rectangle>
        {
            new(_cursor.X - 1, 0, 3, ClientSize.Height),
            new(0, _cursor.Y - 1, ClientSize.Width, 3),
        };
        if (!active.IsEmpty) list.Add(SizeLabelRect(active));
        if (_showMagnifier) list.Add(Rectangle.Inflate(MagnifierRect(), 2, 2));
        return list;
    }

    private float DpiScale => DeviceDpi / 96f;

    private Rectangle SizeLabelRect(Rectangle active)
    {
        int w = (int)(120 * DpiScale), h = (int)(22 * DpiScale);
        int y = active.Top - h - 4;
        if (y < 0) y = active.Top + 4;
        return new Rectangle(active.Left, y, w, h);
    }

    private Rectangle MagnifierRect()
    {
        int box = (int)(MagnifierPixels * MagnifierZoom * Math.Max(1f, DpiScale * 0.75f));
        int infoH = (int)(22 * DpiScale);
        int offset = (int)(24 * DpiScale);
        var size = new Size(box, box + infoH);

        var screen = _screens.FirstOrDefault(s => s.Contains(_cursor), new Rectangle(Point.Empty, ClientSize));
        int x = _cursor.X + offset;
        int y = _cursor.Y + offset;
        if (x + size.Width > screen.Right) x = _cursor.X - offset - size.Width;
        if (y + size.Height > screen.Bottom) y = _cursor.Y - offset - size.Height;
        return new Rectangle(new Point(x, y), size);
    }

    // ---------------- Рисование ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var clip = e.ClipRectangle;

        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

        var active = ActiveRect;
        if (!active.IsEmpty)
        {
            var inner = Rectangle.Intersect(active, clip);
            if (!inner.IsEmpty) g.DrawImage(_shot, inner, inner, GraphicsUnit.Pixel);
        }

        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.Default;

        // Перекрестие
        g.DrawLine(_crossPen, _cursor.X, 0, _cursor.X, ClientSize.Height);
        g.DrawLine(_crossPen, 0, _cursor.Y, ClientSize.Width, _cursor.Y);

        // Рамка и размер
        if (!active.IsEmpty)
        {
            g.DrawRectangle(_dragging ? _selPen : _hoverPen, active.X, active.Y, active.Width - 1, active.Height - 1);
            DrawLabel(g, SizeLabelRect(active), $"{active.Width} × {active.Height}");
        }

        if (_showMagnifier) DrawMagnifier(g);
    }

    private void DrawLabel(Graphics g, Rectangle r, string text)
    {
        if (_font is null) return;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var size = g.MeasureString(text, _font);
        var box = new Rectangle(r.X, r.Y, Math.Min(r.Width, (int)size.Width + 10), r.Height);
        g.FillRectangle(_labelBack, box);
        g.DrawString(text, _font, Brushes.White, box.X + 5, box.Y + (box.Height - size.Height) / 2);
    }

    private void DrawMagnifier(Graphics g)
    {
        var rect = MagnifierRect();
        int box = rect.Width;
        var dest = new Rectangle(rect.X, rect.Y, box, box);

        int half = MagnifierPixels / 2;
        var src = new Rectangle(_cursor.X - half, _cursor.Y - half, MagnifierPixels, MagnifierPixels);

        g.FillRectangle(Brushes.Black, dest);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_shot, dest, src, GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.Default;

        float cell = box / (float)MagnifierPixels;
        using (var center = new Pen(Color.FromArgb(220, 255, 60, 60), 1))
            g.DrawRectangle(center, dest.X + half * cell, dest.Y + half * cell, cell, cell);
        using (var border = new Pen(Color.FromArgb(230, 255, 255, 255), 1))
            g.DrawRectangle(border, dest.X, dest.Y, dest.Width - 1, dest.Height - 1);

        var screenPt = new Point(_cursor.X + _virtual.X, _cursor.Y + _virtual.Y);
        DrawLabel(g, new Rectangle(rect.X, rect.Y + box, box, rect.Height - box), $"X {screenPt.X}  Y {screenPt.Y}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font?.Dispose();
            _crossPen.Dispose();
            _selPen.Dispose();
            _hoverPen.Dispose();
            _labelBack.Dispose();
        }
        base.Dispose(disposing);
    }
}
