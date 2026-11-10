using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace N17YuHelper.UI;

/// <summary>
/// Оформление: светлая / тёмная / как в Windows, цвет акцента Windows,
/// тёмный заголовок и скруглённые углы окон (Windows 11).
/// </summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    public static Color Bg { get; private set; }
    public static Color Surface { get; private set; }
    public static Color SurfaceAlt { get; private set; }
    public static Color Hover { get; private set; }
    public static Color Pressed { get; private set; }
    public static Color Border { get; private set; }
    public static Color Text { get; private set; }
    public static Color TextDim { get; private set; }
    public static Color Accent { get; private set; }
    public static Color AccentHover { get; private set; }
    public static Color AccentText { get; private set; }
    public static Color Danger => IsDark ? Color.FromArgb(255, 110, 110) : Color.FromArgb(196, 43, 28);
    public static Color Success => IsDark ? Color.FromArgb(108, 203, 95) : Color.FromArgb(15, 123, 15);
    public static Color InsertBack => IsDark ? Color.FromArgb(32, 72, 40) : Color.FromArgb(221, 244, 224);

    public static string FontName { get; private set; } = "Segoe UI";
    /// <summary>Шрифт значков Windows (Segoe Fluent Icons на Windows 11, Segoe MDL2 Assets на Windows 10).</summary>
    public static string IconFontName { get; private set; } = "Segoe MDL2 Assets";
    public static Font IconFont(float size = 10f) => new(IconFontName, size);
    public static Font UiFont(float size = 9.5f, FontStyle style = FontStyle.Regular) => new(FontName, size, style);

    /// <summary>Вызывать один раз до создания окон. mode: System, Light, Dark.</summary>
    public static void Init(string? mode)
    {
        mode ??= "System";
        IsDark = mode.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                 || (mode.Equals("System", StringComparison.OrdinalIgnoreCase) && WindowsUsesDarkTheme());

        FontName = FontInstalled("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        IconFontName = FontInstalled("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        Accent = WindowsAccent() ?? Color.FromArgb(0, 103, 192);
        if (IsDark)
        {
            Bg = Color.FromArgb(32, 32, 32);
            Surface = Color.FromArgb(43, 43, 43);
            SurfaceAlt = Color.FromArgb(50, 50, 50);
            Hover = Color.FromArgb(58, 58, 58);
            Pressed = Color.FromArgb(70, 70, 70);
            Border = Color.FromArgb(64, 64, 64);
            Text = Color.FromArgb(242, 242, 242);
            TextDim = Color.FromArgb(160, 160, 160);
            // В тёмной теме акцент делаем светлее, чтобы читался.
            Accent = Lighten(Accent, 0.25);
        }
        else
        {
            Bg = Color.FromArgb(243, 243, 243);
            Surface = Color.FromArgb(255, 255, 255);
            SurfaceAlt = Color.FromArgb(249, 249, 249);
            Hover = Color.FromArgb(234, 234, 234);
            Pressed = Color.FromArgb(222, 222, 222);
            Border = Color.FromArgb(222, 222, 222);
            Text = Color.FromArgb(27, 27, 27);
            TextDim = Color.FromArgb(105, 105, 105);
        }
        AccentHover = IsDark ? Lighten(Accent, 0.12) : Darken(Accent, 0.12);
        AccentText = Luminance(Accent) > 0.55 ? Color.Black : Color.White;
    }

    /// <summary>Режим цвета для стандартных контролов WinForms (.NET 10).</summary>
#pragma warning disable WFO5001
    public static SystemColorMode ColorMode => IsDark ? SystemColorMode.Dark : SystemColorMode.Classic;
#pragma warning restore WFO5001

    // ------------------------------------------------------------------ окна

    /// <summary>Тёмный/светлый заголовок, скруглённые углы и цвет рамки (Windows 11; на Windows 10 тихо игнорируется).</summary>
    public static void ApplyWindow(Form form)
    {
        form.BackColor = Bg;
        form.ForeColor = Text;
        if (!form.IsHandleCreated) form.HandleCreated += (_, _) => ApplyDwm(form.Handle);
        else ApplyDwm(form.Handle);
    }

    public static void ApplyDwm(IntPtr hwnd, bool round = true)
    {
        try
        {
            int dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            if (round)
            {
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
            int border = ColorRef(Border);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch
        {
            // старая Windows — без украшений
        }
    }

    /// <summary>Перекрашивает стандартные контролы внутри (рекурсивно).</summary>
    public static void ApplyTo(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case ModernButton or Card:
                    break;
                case TextBoxBase or ComboBox or NumericUpDown or ListBox or ListView:
                    c.BackColor = Surface;
                    c.ForeColor = Text;
                    break;
                case Label l when l.Tag as string == "dim":
                    l.ForeColor = TextDim;
                    break;
                default:
                    if (c.BackColor != Surface) c.BackColor = c.Parent?.BackColor ?? Bg;
                    c.ForeColor = Text;
                    break;
            }
            ApplyTo(c);
        }
    }

    public static ToolStripRenderer MenuRenderer() => new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = true };

    public static void StyleMenu(ToolStrip menu)
    {
        menu.Renderer = MenuRenderer();
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.Font = UiFont(9.5f);
        foreach (ToolStripItem item in menu.Items) StyleMenuItem(item);
    }

    public static void StyleMenuItem(ToolStripItem item)
    {
        item.ForeColor = Text;
        item.Padding = new Padding(2, 3, 2, 3);
        if (item is ToolStripMenuItem mi)
        {
            mi.DropDown.BackColor = Surface;
            mi.DropDown.Renderer = MenuRenderer();
            foreach (ToolStripItem sub in mi.DropDownItems) StyleMenuItem(sub);
            mi.DropDownOpening += (_, _) => { foreach (ToolStripItem sub in mi.DropDownItems) StyleMenuItem(sub); };
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Pressed;
        public override Color MenuItemPressedGradientEnd => Pressed;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Color.FromArgb(60, Accent);
        public override Color CheckSelectedBackground => Color.FromArgb(90, Accent);
        public override Color CheckPressedBackground => Color.FromArgb(110, Accent);
    }

    // ------------------------------------------------------------------ рисование

    public static System.Drawing.Drawing2D.GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ------------------------------------------------------------------ системные настройки

    private static bool WindowsUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private static Color? WindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                uint v = unchecked((uint)abgr);
                return Color.FromArgb(255, (int)(v & 0xFF), (int)((v >> 8) & 0xFF), (int)((v >> 16) & 0xFF));
            }
        }
        catch { /* нет ключа */ }
        return null;
    }

    private static bool FontInstalled(string name)
    {
        try
        {
            using var f = new Font(name, 9f);
            return f.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
    private static Color Lighten(Color c, double k) => Color.FromArgb(c.A, Mix(c.R, 255, k), Mix(c.G, 255, k), Mix(c.B, 255, k));
    private static Color Darken(Color c, double k) => Color.FromArgb(c.A, Mix(c.R, 0, k), Mix(c.G, 0, k), Mix(c.B, 0, k));
    private static int Mix(int a, int b, double k) => (int)Math.Round(a + (b - a) * k);
    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
