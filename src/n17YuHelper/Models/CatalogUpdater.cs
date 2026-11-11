namespace N17YuHelper.Models;

/// <summary>
/// Fresh model catalog from the repository (catalog.json). At startup the saved copy is applied
/// (catalog.json in the data folder); half a minute later a new one is downloaded in the background. Offline — the built-in list.
/// </summary>
internal static class CatalogUpdater
{
    public const string Url = "https://raw.githubusercontent.com/nieacry/n17YuHelper/main/catalog.json";

    private static string CachePath => Path.Combine(AppPaths.DataDir, "catalog.json");

    /// <summary>Apply the saved catalog (fast, at startup).</summary>
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

    /// <summary>Download a fresh catalog and apply it if it is valid.</summary>
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
