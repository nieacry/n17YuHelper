using System.Runtime.InteropServices;

namespace N17YuHelper.Capture;

/// <summary>
/// Collects rectangles of visible windows and their controls (child windows)
/// for frame snapping. Order: top to bottom in Z-order,
/// inside a window — small controls first, then the window itself.
/// </summary>
internal static class WindowFinder
{
    private const int MaxChildrenPerWindow = 400;

    public static List<Rectangle> Collect()
    {
        var result = new List<Rectangle>();
        uint ownPid = (uint)Environment.ProcessId;

        var topLevel = new List<IntPtr>();
        Native.EnumWindows((h, _) => { topLevel.Add(h); return true; }, IntPtr.Zero);

        foreach (var hwnd in topLevel)
        {
            if (!IsCandidate(hwnd, ownPid, out var bounds)) continue;

            var children = new List<Rectangle>();
            Native.EnumChildWindows(hwnd, (ch, _) =>
            {
                if (children.Count >= MaxChildrenPerWindow) return false;
                if (Native.IsWindowVisible(ch) && Native.GetWindowRect(ch, out var r))
                {
                    var rect = Rectangle.Intersect(r.ToRectangle(), bounds);
                    if (rect.Width > 4 && rect.Height > 4 && rect != bounds)
                        children.Add(rect);
                }
                return true;
            }, IntPtr.Zero);

            children.Sort((a, b) => ((long)a.Width * a.Height).CompareTo((long)b.Width * b.Height));
            result.AddRange(children);
            result.Add(bounds);
        }
        return result;
    }

    private static bool IsCandidate(IntPtr hwnd, uint ownPid, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return false;

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == ownPid) return false;

        // Hidden UWP windows / windows on other virtual desktops.
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        // Bounds without the invisible Windows 10/11 shadow.
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0)
        {
            if (!Native.GetWindowRect(hwnd, out r)) return false;
        }

        bounds = r.ToRectangle();
        return bounds.Width > 4 && bounds.Height > 4;
    }
}
