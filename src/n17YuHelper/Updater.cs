using System.Diagnostics;

namespace N17YuHelper;

/// <summary>
/// Обновление без ручного закрытия программы: новая версия кладётся рядом как
/// n17YuHelper.update.exe, программа это замечает и по команде перезапускается:
/// маленький cmd-скрипт ждёт выхода, подменяет exe и запускает его снова.
/// </summary>
internal sealed class Updater : IDisposable
{
    public const string UpdateFileName = "n17YuHelper.update.exe";

    private readonly FileSystemWatcher? _watcher;
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 1500 };
    private long _lastSize = -1;

    /// <summary>Новая версия лежит рядом и готова к установке.</summary>
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

    // События FileSystemWatcher приходят из другого потока: таймер запускаем через UI-поток.
    private void OnFileEvent() =>
        SynchronizationContextHolder.Post(() => { _lastSize = -1; _settle.Stop(); _settle.Start(); });

    /// <summary>Проверить при запуске: вдруг обновление уже лежит рядом.</summary>
    public void CheckNow()
    {
        if (HasUpdate) OnFileEvent();
    }

    /// <summary>Ждём, пока файл перестанет расти (копирование завершено).</summary>
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
            // файл ещё пишется
        }
    }

    /// <summary>Запускает подмену exe и перезапуск. После вызова программа должна сразу выйти.</summary>
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

/// <summary>Контекст UI-потока для событий из фоновых потоков.</summary>
internal static class SynchronizationContextHolder
{
    public static SynchronizationContext? Context { get; set; }
    public static void Post(Action a) => Context?.Post(_ => a(), null);
}
