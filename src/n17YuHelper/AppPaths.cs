namespace N17YuHelper;

/// <summary>
/// Where the app keeps its data.
///
/// Portable mode (dev folder, unpacked zip): everything next to the exe, as before.
/// Installed app (Velopack: …\n17YuHelper\current\n17YuHelper.exe, with Update.exe next to current):
/// the current folder is replaced as a whole on every update, so settings, history, logs,
/// models and llama.cpp live one level up — in %LocalAppData%\n17YuHelper.
/// </summary>
internal static class AppPaths
{
    public const string PortableMarker = "portable.txt";

    /// <summary>Folder with the exe.</summary>
    public static string ExeDir { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>The app was installed by the installer (not started from a plain folder).</summary>
    public static bool IsInstalled { get; } = DetectInstalled();

    /// <summary>Data folder: settings.json, history.json, logs, models, runtime.</summary>
    public static string DataDir { get; } = IsInstalled ? Directory.GetParent(ExeDir)!.FullName : ExeDir;

    public static string LogsDir => Path.Combine(DataDir, "logs");

    private static bool DetectInstalled()
    {
        try
        {
            // Portable mode was requested explicitly, or settings already sit next to the exe — keep the old behaviour.
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
