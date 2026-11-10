namespace N17YuHelper;

/// <summary>Результат регистрации одной горячей клавиши.</summary>
public sealed record HotkeyStatus(HotkeyEntry Entry, bool Registered, string? Problem);

/// <summary>
/// Регистрирует глобальные горячие клавиши через RegisterHotKey.
/// Клавиши привязываются к виртуальному коду, поэтому работают на любой раскладке.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private readonly Dictionary<int, HotkeyEntry> _registered = new();
    private int _nextId = 1;

    public event Action<HotkeyEntry>? Pressed;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams());
    }

    public IReadOnlyList<HotkeyStatus> RegisterAll(IEnumerable<HotkeyEntry> entries)
    {
        UnregisterAll();
        var result = new List<HotkeyStatus>();
        var used = new HashSet<(uint, uint)>();

        foreach (var entry in entries)
        {
            if (!entry.Enabled)
            {
                result.Add(new HotkeyStatus(entry, false, "disabled"));
                continue;
            }

            if (!TryParse(entry.Keys, out var mods, out var vk, out var parseError))
            {
                result.Add(new HotkeyStatus(entry, false, parseError));
                continue;
            }

            if (!used.Add((mods, vk)))
            {
                result.Add(new HotkeyStatus(entry, false, "duplicated in settings"));
                continue;
            }

            int id = _nextId++;
            if (Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, vk))
            {
                _registered[id] = entry;
                result.Add(new HotkeyStatus(entry, true, null));
                Log.Info($"Hotkey {entry.Keys} -> {entry.Name}");
            }
            else
            {
                result.Add(new HotkeyStatus(entry, false, "already used by another app or Windows"));
                Log.Info($"Failed to register {entry.Keys} (in use)");
            }
        }
        return result;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
            Native.UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _registered.TryGetValue((int)m.WParam, out var entry))
        {
            Pressed?.Invoke(entry);
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// Разбирает строку вида "Ctrl+Shift+Win+Q".
    /// Модификаторы: Ctrl, Shift, Alt, Win. Клавиша: буква, цифра, F1–F24, Space, PrintScreen и т.п.
    /// </summary>
    public static bool TryParse(string text, out uint modifiers, out uint vk, out string? error)
    {
        modifiers = 0;
        vk = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "no key set";
            return false;
        }

        Keys? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; continue;
                case "shift": modifiers |= Native.MOD_SHIFT; continue;
                case "alt": modifiers |= Native.MOD_ALT; continue;
                case "win" or "windows": modifiers |= Native.MOD_WIN; continue;
            }

            if (key is not null)
            {
                error = $"more than one main key in the combination: '{raw}'";
                return false;
            }

            var name = raw;
            if (name.Length == 1 && char.IsDigit(name[0])) name = "D" + name; // 1 → D1
            if (name.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PrtSc", StringComparison.OrdinalIgnoreCase)) name = "PrintScreen";

            if (Enum.TryParse<Keys>(name, ignoreCase: true, out var k) && k is not (Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin))
                key = k;
            else
            {
                error = $"unknown key '{raw}'";
                return false;
            }
        }

        if (key is null)
        {
            error = "no main key (modifiers only)";
            return false;
        }

        vk = (uint)key.Value;

        // Ctrl+Shift+Win+B перезапускает видеодрайвер Windows — не даём её назначить.
        if (key == Keys.B && modifiers == (Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_WIN))
        {
            error = "Ctrl+Shift+Win+B is reserved by Windows (graphics driver reset)";
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
