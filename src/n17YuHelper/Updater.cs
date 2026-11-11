using System.Diagnostics;

namespace N17YuHelper;

/// <summary>
/// Update without closing the app by hand: the new version is put next to it as
/// n17YuHelper.update.exe, the app notices it and restarts on command:
/// a small cmd script waits for the exit, swaps the exe and starts it again.
/// </summary>
internal sealed class Updater : IDisposable
{
    public const string UpdateFileName = "n17YuHelper.update.exe";

    private readonly FileSystemWatcher? _watcher;
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 1500 };
    private long _lastSize = -1;

    /// <summary>A new version is next to the exe and ready to install.</summary>
    public event Action? UpdateReady;

    public static string Dir => AppContext.BaseDirectory;
    public static string UpdatePath => Path.Combine(Dir, UpdateFileName);
    public static bool HasUpdate => File.Exists(UpdatePath);

    public Updater()
    {
        _settle.Tick += (_, _) => CheckSettled();
        try
        {
            _watcher = new FileSystemWatcher(Dir, UpdateFileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, _) => OnFileEvent();
            _watcher.Changed += (_, _) => OnFileEvent();
            _watcher.Renamed += (_, _) => OnFileEvent();
        }
        catch (Exception ex)
        {
            Log.Error("Update watcher", ex);
        }
    }

    // FileSystemWatcher events come from another thread: start the timer via the UI thread.
    private void OnFileEvent() =>
        SynchronizationContextHolder.Post(() => { _lastSize = -1; _settle.Stop(); _settle.Start(); });

    /// <summary>Check at startup: the update may already be there.</summary>
    public void CheckNow()
    {
        if (HasUpdate) OnFileEvent();
    }

    /// <summary>Wait until the file stops growing (copying finished).</summary>
    private void CheckSettled()
    {
        try
        {
            if (!HasUpdate) { _settle.Stop(); return; }
            long size = new FileInfo(UpdatePath).Length;
            if (size > 0 && size == _lastSize)
            {
                _settle.Stop();
                Log.Info($"Update found: {UpdateFileName} ({size} bytes)");
                UpdateReady?.Invoke();
            }
            _lastSize = size;
        }
        catch (IOException)
        {
            // the file is still being written
        }
    }

    /// <summary>Starts swapping the exe and restarting. After the call the app must exit right away.</summary>
    public static bool StartApplyAndRestart()
    {
        try
        {
            string exe = Environment.ProcessPath ?? Path.Combine(Dir, "n17YuHelper.exe");
            int pid = Environment.ProcessId;
            string script = Path.Combine(Path.GetTempPath(), $"n17YuHelper-update-{pid}.cmd");
            File.WriteAllText(script,
                $"""
                @echo off
                :wait
                tasklist /FI "PID eq {pid}" 2>nul | find "{pid}" >nul && (timeout /t 1 /nobreak >nul & goto wait)
                move /Y "{UpdatePath}" "{exe}" >nul
                start "" "{exe}" --updated
                del "%~f0"
                """);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Dir,
            });
            Log.Info("Restarting to apply the update");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Apply update", ex);
            return false;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _settle.Dispose();
    }
}

/// <summary>UI thread context for events from background threads.</summary>
internal static class SynchronizationContextHolder
{
    public static SynchronizationContext? Context { get; set; }
    public static void Post(Action a) => Context?.Post(_ => a(), null);
}
