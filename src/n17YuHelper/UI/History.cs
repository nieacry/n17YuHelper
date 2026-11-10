namespace N17YuHelper.UI;

public sealed record HistoryItem(DateTime Time, string Kind, string Source, string Result);

/// <summary>
/// Последние результаты. Если включено — хранятся в history.json рядом с exe
/// (читаются при первом обращении, а не при запуске, чтобы не занимать память в трее).
/// </summary>
public sealed class HistoryStore(Func<AppSettings> settings)
{
    private List<HistoryItem>? _items;
    private readonly object _fileLock = new();
    private Task _pending = Task.CompletedTask;

    public event Action? Changed;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "history.json");

    public IReadOnlyList<HistoryItem> Items => Loaded();

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private List<HistoryItem> Loaded()
    {
        if (_items is not null) return _items;
        try { _pending.Wait(2000); } catch { /* ошибка записи уже в журнале */ }
        _items = [];
        if (settings().SaveHistory && File.Exists(FilePath))
        {
            try
            {
                lock (_fileLock)
                    _items = System.Text.Json.JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                Log.Error("Failed to read history.json", ex);
            }
        }
        return _items;
    }

    public void Add(string kind, string source, string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return;
        var items = Loaded();
        items.Insert(0, new HistoryItem(DateTime.Now, kind, source, result));
        int max = Math.Clamp(settings().HistoryLimit, 10, 5000);
        if (items.Count > max) items.RemoveRange(max, items.Count - max);
        Changed?.Invoke();
        Persist();
    }

    public void Remove(HistoryItem item)
    {
        if (Loaded().Remove(item))
        {
            Changed?.Invoke();
            Persist();
        }
    }

    public void Clear()
    {
        Loaded().Clear();
        Changed?.Invoke();
        Persist();
    }

    /// <summary>Записывает файл в фоне. Если хранение выключено — удаляет файл.</summary>
    private void Persist()
    {
        bool save = settings().SaveHistory;
        var snapshot = save ? Loaded().ToList() : null;
        _pending = Task.Run(() =>
        {
            lock (_fileLock)
            {
                try
                {
                    if (snapshot is null)
                    {
                        if (File.Exists(FilePath)) File.Delete(FilePath);
                        return;
                    }
                    var tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(snapshot, JsonOptions));
                    File.Move(tmp, FilePath, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to save history.json", ex);
                }
            }
        });
    }

    /// <summary>Настройку поменяли: выключили — удалить файл, включили — записать текущую историю.</summary>
    public void SettingsChanged() => Persist();

    /// <summary>Отпустить историю из памяти (она прочитается снова, когда понадобится).</summary>
    public void Unload()
    {
        if (settings().SaveHistory) _items = null;
    }
}

/// <summary>Окно истории: двойной клик или Enter — скопировать результат.</summary>
internal sealed class HistoryForm : Form
{
    private readonly HistoryStore _store;
    private readonly ListView _list;
    private readonly TextBox _detail;
    private readonly TextBox _search;
    private readonly Label _info;

    private readonly Func<bool> _saved;

    public HistoryForm(HistoryStore store, Icon icon, Func<bool> saved)
    {
        _store = store;
        _saved = saved;
        Text = "n17 YuHelper - History";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = Theme.UiFont(9.5f);
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(500, 300);
        KeyPreview = true;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
        };
        _list.Columns.Add("Time", 90);
        _list.Columns.Add("Action", 120);
        _list.Columns.Add("Source", 260);
        _list.Columns.Add("Result", 290);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        _list.DoubleClick += (_, _) => CopyResult();

        _detail = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = Theme.UiFont(10.5f),
        };
        var detailCard = new Card { Dock = DockStyle.Fill };
        detailCard.Controls.Add(_detail);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6 };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(detailCard);
        split.Panel2.Padding = new Padding(0, 4, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10, 8, 10, 10), FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(MakeButton("Copy result", CopyResult, ButtonKind.Accent));
        buttons.Controls.Add(MakeButton("Copy source", CopySource));
        buttons.Controls.Add(MakeButton("Delete", DeleteSelected));
        buttons.Controls.Add(MakeButton("Clear history", ClearAll));
        _info = new Label { AutoSize = true, Tag = "dim", Padding = new Padding(8, 8, 0, 0) };
        buttons.Controls.Add(_info);

        _search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search in history...", Font = Theme.UiFont(10f), Margin = new Padding(0, 0, 0, 6) };
        _search.TextChanged += (_, _) => Reload();
        var searchHost = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(0, 0, 0, 6) };
        searchHost.Controls.Add(_search);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 0) };
        body.Controls.Add(split);
        body.Controls.Add(searchHost);
        Controls.Add(body);
        Controls.Add(buttons);
        _list.BorderStyle = BorderStyle.FixedSingle;
        Theme.ApplyWindow(this);
        Theme.ApplyTo(this);
        Load += (_, _) => { try { split.SplitterDistance = (int)(split.Height * 0.55); } catch (InvalidOperationException) { } };

        _store.Changed += Reload;
        Reload();
    }

    private static ModernButton MakeButton(string text, Action onClick, ButtonKind kind = ButtonKind.Normal)
    {
        var b = new ModernButton { Text = text, Kind = kind };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void Reload()
    {
        if (IsDisposed) return;
        _list.BeginUpdate();
        _list.Items.Clear();
        string q = _search.Text.Trim();
        int total = _store.Items.Count;
        foreach (var it in _store.Items)
        {
            if (q.Length > 0 && !(it.Source.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                  it.Result.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                  it.Kind.Contains(q, StringComparison.OrdinalIgnoreCase))) continue;
            var row = new ListViewItem(it.Time.ToString("HH:mm:ss")) { Tag = it };
            row.SubItems.Add(it.Kind);
            row.SubItems.Add(OneLine(it.Source));
            row.SubItems.Add(OneLine(it.Result));
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        else _detail.Clear();
        _info.Text = (q.Length > 0 ? $"Found {_list.Items.Count} of {total}. " : $"{total} results. ") +
                     (_saved() ? "Saved in history.json, kept after restart." : "Kept in memory only, cleared when the app exits.");
    }

    private static string OneLine(string s)
    {
        s = s.Replace("\r", " ").Replace('\n', ' ');
        return s.Length > 120 ? s[..120] + "..." : s;
    }

    private HistoryItem? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as HistoryItem : null;

    private void ShowDetail()
    {
        if (Selected is not { } it) return;
        static string N(string t) => t.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _detail.Text = $"{it.Kind}  ·  {it.Time:yyyy-MM-dd HH:mm:ss}\r\n\r\n{N(it.Source)}\r\n\r\n→\r\n\r\n{N(it.Result)}";
    }

    private void CopyResult()
    {
        if (Selected is { } it) TrySetClipboard(it.Result);
    }

    private void DeleteSelected()
    {
        if (Selected is { } it) _store.Remove(it);
    }

    private void ClearAll()
    {
        if (_store.Items.Count == 0) return;
        if (MessageBox.Show(this, $"Delete all {_store.Items.Count} results from the history?", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
            _store.Clear();
    }

    private void CopySource()
    {
        if (Selected is { } it) TrySetClipboard(it.Source);
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { Log.Error("History copy", ex); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == Keys.Enter && ActiveControl == _list) { CopyResult(); return true; }
        if (keyData == Keys.Delete && ActiveControl == _list) { DeleteSelected(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); _search.SelectAll(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _store.Changed -= Reload;
        base.OnFormClosed(e);
    }
}
