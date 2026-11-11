namespace N17YuHelper;

/// <summary>
/// Gets the text selected in any app: sends Ctrl+C,
/// checks whether the clipboard changed and restores the previous clipboard contents.
/// Runs on the UI thread (the clipboard requires STA).
/// </summary>
public static class SelectionGrabber
{
    private static readonly int[] Modifiers =
        [Native.VK_CONTROL, Native.VK_SHIFT, Native.VK_MENU, Native.VK_LWIN, Native.VK_RWIN];

    /// <summary>Returns the selected text, or null if nothing is selected.</summary>
    public static async Task<string?> TryGetSelectedTextAsync(uint hotkeyVk, AppSettings settings)
    {
        int timeoutMs = settings.SelectionCopyTimeoutMs;

        // 0. Which app holds the selection.
        string process = ForegroundProcessName();
        if (settings.NeverCopyProcesses.Contains(process, StringComparer.OrdinalIgnoreCase))
        {
            Log.Info($"{process}: selection skipped (NeverCopyProcesses)");
            return null;
        }
        bool useCtrlInsert = settings.CopyWithCtrlInsertProcesses.Contains(process, StringComparer.OrdinalIgnoreCase);

        // 1. Wait until the user releases the keys. Otherwise Ctrl+C would become, e.g.,
        //    Win+Ctrl+C, and releasing Win might open Start.
        await WaitForKeysReleasedAsync(hotkeyVk, maxWaitMs: 2000);

        // 2. Save the clipboard.
        var saved = ClipboardBackup.Capture();
        uint seqBefore = Native.GetClipboardSequenceNumber();

        // 3. Ctrl+C (or Ctrl+Insert in terminals).
        SendCopy(useCtrlInsert ? (byte)0x2D /* VK_INSERT */ : (byte)0x43 /* C */);

        // 4. Wait until the app puts the text into the clipboard.
        string? text = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(15);
            if (Native.GetClipboardSequenceNumber() != seqBefore)
            {
                // Some apps write to the clipboard in several steps — let them finish.
                await Task.Delay(30);
                text = TryGetText();
                break;
            }
        }

        // 5. Restore the previous clipboard contents if it changed.
        if (Native.GetClipboardSequenceNumber() != seqBefore)
            saved.Restore();

        Log.Info(text is null
            ? $"{process}: no selected text ({sw.ElapsedMilliseconds} ms)"
            : $"{process}: got selected text, {text.Length} chars ({sw.ElapsedMilliseconds} ms)");

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static async Task WaitForKeysReleasedAsync(uint hotkeyVk, int maxWaitMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < maxWaitMs)
        {
            bool anyDown = Modifiers.Any(IsDown) || (hotkeyVk != 0 && IsDown((int)hotkeyVk));
            if (!anyDown) return;
            await Task.Delay(10);
        }
        Log.Info("Keys still held after 2 s, continuing");
    }

    private static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void SendCopy(byte key)
    {
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        uint ext = key == 0x2D ? KEYEVENTF_EXTENDEDKEY : 0; // Insert is an extended key
        Native.keybd_event(Native.VK_CONTROL, 0, 0, UIntPtr.Zero);
        Native.keybd_event(key, 0, ext, UIntPtr.Zero);
        Native.keybd_event(key, 0, ext | Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Native.keybd_event(Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static string ForegroundProcessName()
    {
        try
        {
            var hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static string? TryGetText()
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                return Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    ? Clipboard.GetText(TextDataFormat.UnicodeText)
                    : null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(20); // the clipboard is held by another app
            }
        }
        return null;
    }
}

/// <summary>A copy of the clipboard contents that can be put back.</summary>
public sealed class ClipboardBackup
{
    private readonly DataObject? _data;

    private ClipboardBackup(DataObject? data) => _data = data;

    public static ClipboardBackup Capture()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var src = Clipboard.GetDataObject();
                if (src is null) return new ClipboardBackup(null);

                var copy = new DataObject();
                int count = 0;
                foreach (var fmt in src.GetFormats(autoConvert: false))
                {
                    try
                    {
                        var value = src.GetData(fmt, autoConvert: false);
                        if (value is not null)
                        {
                            copy.SetData(fmt, false, value);
                            count++;
                        }
                    }
                    catch
                    {
                        // Some formats cannot be read — skip them.
                    }
                }
                return new ClipboardBackup(count > 0 ? copy : null);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(20);
            }
        }
        return new ClipboardBackup(null);
    }

    public void Restore()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (_data is null) Clipboard.Clear();
                else Clipboard.SetDataObject(_data, copy: true, retryTimes: 3, retryDelay: 30);
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to restore clipboard", ex);
                return;
            }
        }
    }
}
