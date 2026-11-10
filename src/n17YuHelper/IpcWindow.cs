using System.Runtime.InteropServices;

namespace N17YuHelper;

/// <summary>
/// Скрытое окно для связи между копиями программы: второй запуск с файлом в командной строке
/// («Отправить» в Проводнике) передаёт путь уже запущенной программе и сразу завершается.
/// </summary>
internal sealed class IpcWindow : NativeWindow, IDisposable
{
    private const string WindowName = "n17YuHelper_IPC";
    private const int WM_COPYDATA = 0x004A;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly IntPtr OpenFileMarker = new(0x4E17F11E);

    public event Action<string>? FileReceived;

    public IpcWindow()
    {
        CreateHandle(new CreateParams { Caption = WindowName, Parent = HWND_MESSAGE });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_COPYDATA)
        {
            var data = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
            if (data.dwData == OpenFileMarker && data.cbData > 0)
            {
                var path = Marshal.PtrToStringUni(data.lpData, data.cbData / 2).TrimEnd('\0');
                FileReceived?.Invoke(path);
                m.Result = 1;
                return;
            }
        }
        base.WndProc(ref m);
    }

    /// <summary>Передаёт путь к файлу запущенной копии. False — её окно не найдено.</summary>
    public static bool SendFile(string path)
    {
        var hwnd = FindWindowEx(HWND_MESSAGE, IntPtr.Zero, null, WindowName);
        if (hwnd == IntPtr.Zero) return false;
        var buffer = Marshal.StringToHGlobalUni(path + "\0");
        try
        {
            var data = new COPYDATASTRUCT { dwData = OpenFileMarker, cbData = (path.Length + 1) * 2, lpData = buffer };
            AllowSetForegroundWindow(-1); // наше окно должно суметь выйти на передний план
            return SendMessage(hwnd, WM_COPYDATA, IntPtr.Zero, ref data) != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => DestroyHandle();

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
