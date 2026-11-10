namespace N17YuHelper;

/// <summary>Где был выделен текст: окно и программа (для замены текста обратно).</summary>
public sealed record SourceWindow(IntPtr Handle, string ProcessName);

/// <summary>
/// Заменяет выделенный текст в исходной программе: возвращает ей фокус,
/// вставляет новый текст через буфер обмена (Ctrl+V или Shift+Insert в терминалах)
/// и возвращает прежнее содержимое буфера.
/// </summary>
public static class TextReplacer
{
    public static async Task<bool> ReplaceAsync(SourceWindow source, string text, AppSettings settings)
    {
        if (source.Handle == IntPtr.Zero) return false;

        var backup = ClipboardBackup.Capture();
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Log.Error("Replace: clipboard is busy", ex);
            return false;
        }

        Native.ForceForeground(source.Handle);
        await Task.Delay(120);
        if (Native.GetForegroundWindow() != source.Handle)
        {
            // Не удалось вернуться в окно — текст остаётся в буфере, пользователь вставит сам.
            Log.Info($"Replace: could not activate {source.ProcessName}, text left in clipboard");
            return false;
        }

        bool terminal = settings.CopyWithCtrlInsertProcesses.Contains(source.ProcessName, StringComparer.OrdinalIgnoreCase);
        SendPaste(terminal);

        // Даём программе забрать текст из буфера, потом возвращаем прежний буфер.
        await Task.Delay(400);
        backup.Restore();
        Log.Info($"Replace: pasted {text.Length} chars into {source.ProcessName}");
        return true;
    }

    private static void SendPaste(bool shiftInsert)
    {
        const byte VK_V = 0x56, VK_INSERT = 0x2D;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        if (shiftInsert)
        {
            Native.keybd_event(Native.VK_SHIFT, 0, 0, UIntPtr.Zero);
            Native.keybd_event(VK_INSERT, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            Native.keybd_event(VK_INSERT, 0, KEYEVENTF_EXTENDEDKEY | Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Native.keybd_event(Native.VK_SHIFT, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        else
        {
            Native.keybd_event(Native.VK_CONTROL, 0, 0, UIntPtr.Zero);
            Native.keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            Native.keybd_event(VK_V, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Native.keybd_event(Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    /// <summary>Запоминает активное окно (вызывать до показа своих окон).</summary>
    public static SourceWindow CaptureForeground()
    {
        var hwnd = Native.GetForegroundWindow();
        string name = "";
        try
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch { /* процесс мог закрыться */ }
        return new SourceWindow(hwnd, name);
    }
}
