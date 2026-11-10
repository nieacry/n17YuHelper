namespace N17YuHelper.Models;

/// <summary>
/// Свежий каталог моделей из репозитория (catalog.json). При запуске применяется сохранённая копия
/// (catalog.json в папке данных), через полминуты в фоне скачивается новая. Без интернета — встроенный список.
/// </summary>
internal static class CatalogUpdater
{
    public const string Url = "https://raw.githubusercontent.com/nieacry/n17YuHelper/main/catalog.json";

    private static string CachePath => Path.Combine(AppPaths.DataDir, "catalog.json");

    /// <summary>Применить сохранённый каталог (быстро, при запуске).</summary>
    public static void LoadCached()
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            var error = ModelCatalog.Apply(File.ReadAllText(CachePath), "catalog.json " + File.GetLastWriteTime(CachePath).ToString("yyyy-MM-dd"));
            if (error is not null) Log.Info("Saved model catalog ignored: " + error);
        }
        catch (Exception ex)
        {
            Log.Error("Read catalog.json", ex);
        }
    }

    /// <summary>Скачать свежий каталог и применить его, если он правильный.</summary>
    public static async Task RefreshAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"n17YuHelper/{TrayApp.VersionText}");
            var json = await http.GetStringAsync(Url);
            var error = ModelCatalog.Apply(json, "catalog.json " + DateTime.Now.ToString("yyyy-MM-dd"));
            if (error is not null)
            {
                Log.Info("Downloaded model catalog ignored: " + error);
                return;
            }
            await File.WriteAllTextAsync(CachePath, json);
            Log.Info($"Model catalog updated: {ModelCatalog.All.Count} models, llama.cpp {ModelCatalog.LlamaCppTag}");
        }
        catch (Exception ex)
        {
            Log.Info("Model catalog not updated: " + ex.Message);
        }
    }
}
