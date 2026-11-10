namespace N17YuHelper;

/// <summary>Простой журнал в logs\yuhelper.log (в папке данных, см. AppPaths).</summary>
internal static class Log
{
    private static readonly object Sync = new();
    private const long MaxSize = 1024 * 1024;

    private static string Dir => AppPaths.LogsDir;
    private static string FilePath => Path.Combine(Dir, "yuhelper.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxSize)
                {
                    var old = Path.Combine(Dir, "yuhelper.old.log");
                    File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Журнал не должен ронять программу.
        }
    }
}
