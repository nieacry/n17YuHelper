using Velopack;
using Velopack.Sources;

namespace N17YuHelper;

/// <summary>
/// Online updates (Velopack + GitHub Releases) — installed app only.
/// Checks a minute after startup and then every 6 hours; a new version is downloaded in the background.
/// Whether to install it is decided by TrayApp (right away when the app is idle, or on the user's command).
/// A portable copy updates the old way: with n17YuHelper.update.exe next to the exe (see Updater).
/// </summary>
internal sealed class OnlineUpdater : IDisposable
{
    public const string RepoUrl = "https://github.com/nieacry/n17YuHelper";
    public static string ReleasesUrl => RepoUrl + "/releases";
    public static string ReleaseUrl(string version) => $"{RepoUrl}/releases/tag/v{version}";

    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly Func<AppSettings> _settings;
    private readonly UpdateManager? _manager;
    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _checking;

    /// <summary>Downloaded version ready to install.</summary>
    public UpdateInfo? Ready { get; private set; }

    public string? ReadyVersion => Ready?.TargetFullRelease.Version.ToString();

    /// <summary>A new version was downloaded.</summary>
    public event Action<string>? UpdateDownloaded;

    /// <summary>Whether online updates work (the app was installed by the installer).</summary>
    public bool Supported => _manager is not null;

    public OnlineUpdater(Func<AppSettings> settings)
    {
        _settings = settings;
        if (AppPaths.IsInstalled)
        {
            try
            {
                var m = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
                if (m.IsInstalled) _manager = m;
            }
            catch (Exception ex)
            {
                Log.Error("Online updates are unavailable", ex);
            }
        }
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = (int)Interval.TotalMilliseconds;
            if (_settings().CheckUpdatesAutomatically) await CheckAsync(manual: false);
        };
        _timer.Interval = (int)FirstCheck.TotalMilliseconds;
        if (_manager is not null) _timer.Start();
        Log.Info(_manager is not null ? $"Online updates: on ({RepoUrl})" : "Online updates: off (portable copy)");
    }

    /// <summary>Checks for and downloads a new version. Returns a message for the user.</summary>
    public async Task<string> CheckAsync(bool manual)
    {
        if (_manager is null)
            return "This is a portable copy, it does not update itself.\nDownload the installer from GitHub to get automatic updates.";
        if (Ready is not null) return $"Version {ReadyVersion} is downloaded and ready to install.";
        if (_checking) return "Already checking for updates...";
        _checking = true;
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                if (manual) Log.Info("Update check: up to date");
                return $"You have the latest version ({TrayApp.VersionText}).";
            }
            string version = info.TargetFullRelease.Version.ToString();
            Log.Info($"Update check: {version} available, downloading");
            await _manager.DownloadUpdatesAsync(info);
            Ready = info;
            Log.Info($"Update {version} downloaded");
            UpdateDownloaded?.Invoke(version);
            return $"Version {version} is downloaded and ready to install.";
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            return "Could not check for updates: " + ex.Message;
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>
    /// Starts installing the downloaded version: Velopack waits for the app to exit, installs the update and starts it again.
    /// After true the app must exit right away.
    /// </summary>
    public bool StartApplyAndRestart()
    {
        if (_manager is null || Ready is null) return false;
        try
        {
            Log.Info($"Installing update {ReadyVersion}");
            _manager.WaitExitThenApplyUpdates(Ready.TargetFullRelease, silent: true, restart: true, restartArgs: ["--updated"]);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Install update", ex);
            return false;
        }
    }

    public void Dispose() => _timer.Dispose();
}
