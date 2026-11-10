namespace N17YuHelper.UI;

/// <summary>
/// Поле для ввода горячей клавиши, как в ShareX: щёлкнуть и нажать комбинацию.
/// Backspace/Delete — очистить, Esc — отменить ввод.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    private string _keys = "";
    private string _before = "";

    /// <summary>Пока поле в фокусе, глобальные клавиши программы надо отключить, иначе они перехватят нажатие.</summary>
    public event Action? CaptureStarted;
    public event Action? CaptureEnded;
    public event Action? KeysChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        ShortcutsEnabled = false;
        TextAlign = HorizontalAlignment.Center;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Keys
    {
        get => _keys;
        set
        {
            _keys = value ?? "";
            Text = _keys.Length == 0 ? "(none)" : _keys;
        }
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        _before = _keys;
        Text = "Press keys...";
        BackColor = Theme.IsDark ? Color.FromArgb(70, 62, 30) : Color.FromArgb(255, 250, 220);
        CaptureStarted?.Invoke();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Keys = _keys;
        BackColor = Theme.Surface;
        CaptureEnded?.Invoke();
    }

    protected override bool IsInputKey(System.Windows.Forms.Keys keyData) =>
        (keyData & System.Windows.Forms.Keys.KeyCode) != System.Windows.Forms.Keys.Tab;

    protected override bool ProcessCmdKey(ref Message msg, System.Windows.Forms.Keys keyData)
    {
        // Tab оставляем для перехода между полями, остальное ловим сами.
        if ((keyData & System.Windows.Forms.Keys.KeyCode) == System.Windows.Forms.Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        HandleKey(e.KeyCode, e.Control, e.Shift, e.Alt);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        // PrintScreen приходит только как KeyUp.
        if (e.KeyCode == System.Windows.Forms.Keys.PrintScreen)
            HandleKey(e.KeyCode, e.Control, e.Shift, e.Alt);
        e.Handled = true;
    }

    private void HandleKey(System.Windows.Forms.Keys key, bool ctrl, bool shift, bool alt)
    {
        switch (key)
        {
            case System.Windows.Forms.Keys.Escape:
                _keys = _before;
                Parent?.SelectNextControl(this, true, true, true, true);
                return;
            case System.Windows.Forms.Keys.Back or System.Windows.Forms.Keys.Delete when !ctrl && !shift && !alt && !WinDown():
                _keys = "";
                Text = "(none)";
                KeysChanged?.Invoke();
                return;
            case System.Windows.Forms.Keys.ControlKey or System.Windows.Forms.Keys.ShiftKey or System.Windows.Forms.Keys.Menu
                or System.Windows.Forms.Keys.LWin or System.Windows.Forms.Keys.RWin:
                // Пока нажаты только модификаторы — показываем, что уже зажато.
                Text = Format(ctrl, shift, alt, WinDown(), null) + "...";
                return;
        }

        _keys = Format(ctrl, shift, alt, WinDown(), key);
        Text = _keys;
        KeysChanged?.Invoke();
    }

    private static bool WinDown() =>
        (Native.GetAsyncKeyState(Native.VK_LWIN) & 0x8000) != 0 || (Native.GetAsyncKeyState(Native.VK_RWIN) & 0x8000) != 0;

    private static string Format(bool ctrl, bool shift, bool alt, bool win, System.Windows.Forms.Keys? key)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");
        if (key is { } k)
        {
            string name = k.ToString();
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name[1..];
            parts.Add(name);
        }
        return string.Join("+", parts);
    }
}
