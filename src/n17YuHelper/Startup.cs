using Microsoft.Win32;

namespace N17YuHelper;

/// <summary>Автозапуск с Windows через HKCU\...\Run (без прав администратора).</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "n17YuHelper";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "n17YuHelper.exe");

    /// <summary>Путь, записанный в автозапуск, или null.</summary>
    public static string? RegisteredPath
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return (key?.GetValue(ValueName) as string)?.Trim('"');
        }
    }

    public static bool IsEnabled => RegisteredPath is not null;

    /// <summary>Автозапуск включён именно для этого exe.</summary>
    public static bool IsEnabledForThisExe =>
        RegisteredPath is { } p && string.Equals(Path.GetFullPath(p), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase);

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{ExePath}\"");
        Log.Info($"Start with Windows: on ({ExePath})");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.DeleteValue(ValueName, throwOnMissingValue: false);
        Log.Info("Start with Windows: off");
    }

    /// <summary>Если exe перенесли, а в автозапуске остался несуществующий путь — обновляем его.</summary>
    public static void FixStalePath()
    {
        try
        {
            if (RegisteredPath is { } p && !File.Exists(p)) Enable();
        }
        catch (Exception ex)
        {
            Log.Error("Start with Windows: could not update the path", ex);
        }
    }
}
