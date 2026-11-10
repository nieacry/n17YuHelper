using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using N17YuHelper.Voice;

namespace N17YuHelper.UI;

/// <summary>
/// Запись голоса: плашка внизу экрана с уровнем звука и таймером. Фокус не забирает —
/// диктовать можно прямо в окно программы. Закончить: та же клавиша ещё раз, кнопка ■ или пауза в речи.
/// Esc — отменить.
/// </summary>
internal sealed class RecordingToast : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const int WM_HOTKEY = 0x0312;
    private const int EscHotkeyId = 0xB0E5;

    private readonly VoiceSettings _settings;
    private readonly AudioRecorder _recorder;
    private readonly TaskCompletionSource<byte[]?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Windows.Forms.Timer _ui = new() { Interval = 50 };
    private readonly float[] _levels = new float[48];
    private int _levelPos;
    private readonly string _hint;
    private bool _hover;
    private bool _finished;

    // Определение конца речи (в фоновом потоке записи).
    private float _noise = -1;
    private bool _heardSpeech;
    private DateTime _lastSpeech = DateTime.UtcNow;
    private readonly DateTime _started = DateTime.UtcNow;
    private volatile float _lastLevel;

    public RecordingToast(VoiceSettings settings, string hint)
    {
        _settings = settings;
        _hint = hint;
        _recorder = new AudioRecorder { Device = settings.Microphone };
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(440, 68);
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        Font = Theme.UiFont(9.5f);
        HandleCreated += (_, _) =>
        {
            Theme.ApplyDwm(Handle);
            RegisterHotKey(Handle, EscHotkeyId, 0x4000 /*MOD_NOREPEAT*/, (uint)Keys.Escape);
        };
        _ui.Tick += (_, _) => OnTick();
        _recorder.Level += OnLevel;
    }

    /// <summary>Записывает, пока не закончат. Возвращает PCM 16 кГц или null, если отменили.</summary>
    public Task<byte[]?> RecordAsync()
    {
        try
        {
            _recorder.Start();
        }
        catch (Exception ex)
        {
            _done.TrySetException(ex);
            return _done.Task;
        }
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 56);
        Show();
        // После показа размер уже учитывает масштаб экрана — ставим точно по центру снизу.
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - (int)(56 * DeviceDpi / 96f));
        _ui.Start();
        return _done.Task;
    }

    public bool IsRecording => !_finished;

    /// <summary>Закончить запись и отдать звук на распознавание.</summary>
    public void Finish() => Complete(cancel: false);

    /// <summary>Отменить запись.</summary>
    public void Cancel() => Complete(cancel: true);

    private void Complete(bool cancel)
    {
        if (_finished) return;
        _finished = true;
        _ui.Stop();
        byte[] pcm;
        try { pcm = _recorder.Stop(); }
        catch (Exception ex) { _done.TrySetException(ex); Close(); return; }
        Close();
        _done.TrySetResult(cancel ? null : pcm);
    }

    private void OnLevel(float rms)
    {
        _lastLevel = rms;
        // Шум — медленно подстраиваемый минимум; речь — заметно громче шума.
        if (_noise < 0) _noise = rms;
        else if (rms < _noise) _noise = rms;
        else _noise += (rms - _noise) * 0.002f;
        float speech = Math.Max(0.02f, _noise * 3.5f);
        if (rms > speech)
        {
            _heardSpeech = true;
            _lastSpeech = DateTime.UtcNow;
        }
    }

    private void OnTick()
    {
        _levels[_levelPos++ % _levels.Length] = _lastLevel;
        var now = DateTime.UtcNow;
        double silence = (now - _lastSpeech).TotalSeconds;
        double elapsed = (now - _started).TotalSeconds;
        if (_settings.AutoStopSilenceSeconds > 0 && _heardSpeech && silence > _settings.AutoStopSilenceSeconds) { Finish(); return; }
        if (!_heardSpeech && elapsed > 15) { Finish(); return; } // ничего не сказали — распознавать нечего
        if (elapsed > Math.Max(5, _settings.MaxSeconds)) { Finish(); return; }
        Invalidate();
    }

    // ---------------- рисование ----------------

    private float S(float v) => v * DeviceDpi / 96f;

    private RectangleF StopRect => new(ClientSize.Width - S(52), (ClientSize.Height - S(36)) / 2, S(36), S(36));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

        // Микрофон: круг акцента, «дышит» в такт голосу.
        float level = Math.Clamp(_lastLevel * 8, 0, 1);
        float cx = S(34), cy = ClientSize.Height / 2f;
        float halo = S(18) + S(8) * level;
        using (var b = new SolidBrush(Color.FromArgb(60, Theme.Accent))) g.FillEllipse(b, cx - halo, cy - halo, halo * 2, halo * 2);
        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, cx - S(16), cy - S(16), S(32), S(32));
        using (var icon = new Font(Theme.IconFontName, 12f))
            TextRenderer.DrawText(g, "", icon, new Rectangle((int)(cx - S(16)), (int)(cy - S(16)), (int)S(32), (int)S(32)),
                Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // Текст и таймер.
        var t = DateTime.UtcNow - _started;
        float x = S(64);
        using (var bold = Theme.UiFont(10f, FontStyle.Bold))
            TextRenderer.DrawText(g, $"Listening…  {(int)t.TotalMinutes}:{t.Seconds:00}", bold, new Point((int)x, (int)S(12)), Theme.Text);
        using (var small = Theme.UiFont(8.5f))
            TextRenderer.DrawText(g, _hint, small, new Point((int)x, (int)S(38)), Theme.TextDim);

        // Уровень звука: столбики справа от заголовка.
        float barsRight = StopRect.Left - S(14), barW = S(3), gap = S(2);
        int n = (int)Math.Min(_levels.Length, (barsRight - S(230)) / (barW + gap));
        using (var b = new SolidBrush(Theme.Accent))
        {
            for (int i = 0; i < n; i++)
            {
                float v = Math.Clamp(_levels[(_levelPos - n + i + _levels.Length * 4) % _levels.Length] * 10, 0.05f, 1f);
                float h = S(18) * v;
                float bx = barsRight - (n - i) * (barW + gap);
                g.FillRectangle(b, bx, S(22) - h / 2, barW, Math.Max(S(2), h));
            }
        }

        // Кнопка «закончить».
        var r = StopRect;
        using (var b = new SolidBrush(_hover ? Theme.Hover : Theme.SurfaceAlt)) g.FillEllipse(b, r);
        using (var b = new SolidBrush(Theme.Danger))
            g.FillRectangle(b, r.X + r.Width / 2 - S(6), r.Y + r.Height / 2 - S(6), S(12), S(12));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool h = StopRect.Contains(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        Cursor = h ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover) { _hover = false; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && StopRect.Contains(e.Location)) Finish();
    }

    // ---------------- окно без фокуса ----------------

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

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = MA_NOACTIVATE;
            return;
        }
        if (m.Msg == WM_HOTKEY && (int)m.WParam == EscHotkeyId)
        {
            Cancel();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (IsHandleCreated) UnregisterHotKey(Handle, EscHotkeyId);
        base.OnFormClosed(e);
        if (!_finished) Cancel();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ui.Dispose();
            _recorder.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
