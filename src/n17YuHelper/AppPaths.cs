namespace N17YuHelper;

/// <summary>
/// Где лежат данные программы.
///
/// Переносной режим (папка разработки, распакованный zip): всё рядом с exe, как раньше.
/// Установленная программа (Velopack: …\n17YuHelper\current\n17YuHelper.exe, рядом с current лежит Update.exe):
/// папка current при каждом обновлении заменяется целиком, поэтому настройки, история, журналы,
/// модели и llama.cpp хранятся на уровень выше — в %LocalAppData%\n17YuHelper.
/// </summary>
internal static class AppPaths
{
    public const string PortableMarker = "portable.txt";

    /// <summary>Папка с exe.</summary>
    public static string ExeDir { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Программа установлена установщиком (а не запущена из обычной папки).</summary>
    public static bool IsInstalled { get; } = DetectInstalled();

    /// <summary>Папка данных: settings.json, history.json, logs, models, runtime.</summary>
    public static string DataDir { get; } = IsInstalled ? Directory.GetParent(ExeDir)!.FullName : ExeDir;

    public static string LogsDir => Path.Combine(DataDir, "logs");

    private static bool DetectInstalled()
    {
        try
        {
            // Явно попросили переносной режим или рядом с exe уже есть настройки — работаем по-старому.
            if (File.Exists(Path.Combine(ExeDir, PortableMarker)) || File.Exists(Path.Combine(ExeDir, "settings.json")))
                return false;
            var dir = new DirectoryInfo(ExeDir);
            return dir.Name.Equals("current", StringComparison.OrdinalIgnoreCase) &&
                   dir.Parent is not null &&
                   File.Exists(Path.Combine(dir.Parent.FullName, "Update.exe"));
        }
        catch
        {
            return false;
        }
    }
}
