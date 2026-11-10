using Velopack;

namespace N17YuHelper;

internal static class Program
{
    /// <summary>Файл из командной строки (открыть и перевести после запуска).</summary>
    public static string? StartupFile { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack: служебные запуски установщика (установка / обновление / удаление) обрабатываются здесь и сразу завершаются.
        // При установке включаем автозапуск с Windows (программа живёт в трее), при удалении — убираем его.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => { try { Startup.Enable(); } catch { } })
            .OnBeforeUninstallFastCallback(_ => { try { if (Startup.IsEnabledForThisExe) Startup.Disable(); } catch { } })
            .Run();

        StartupFile = args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a)) is { } f ? Path.GetFullPath(f) : null;

        using var mutex = new Mutex(initiallyOwned: true, @"Local\n17YuHelper_SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Уже запущена: передаём ей файл (если есть) и выходим.
            if (StartupFile is not null && IpcWindow.SendFile(StartupFile)) return;
            MessageBox.Show("n17 YuHelper is already running. Look for its icon in the system tray.", "n17 YuHelper",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        UI.Theme.Init(AppSettings.PeekTheme());
#pragma warning disable WFO5001 // тёмный режим стандартных контролов (.NET 10)
        Application.SetColorMode(UI.Theme.ColorMode);
#pragma warning restore WFO5001
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unhandled exception (UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        Application.Run(new TrayApp(updated: args.Contains("--updated")));
    }
}
