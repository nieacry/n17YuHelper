using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace N17YuHelper.Models;

public sealed record DownloadProgress(string Stage, long Done, long Total, double BytesPerSecond)
{
    public double Fraction => Total > 0 ? Math.Clamp(Done / (double)Total, 0, 1) : 0;
}

/// <summary>
/// Downloading models and llama-server from inside the app: resume (.part + Range),
/// retries on connection loss, progress and cancellation.
/// </summary>
public static class Downloader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"n17YuHelper/{TrayApp.VersionText}");
        return c;
    }

    /// <summary>Downloads all files of a package (and its audio part, if any) that are not there yet.</summary>
    public static Task DownloadPackageAsync(ModelPackage package, string modelsDir, IProgress<DownloadProgress> progress, CancellationToken ct) =>
        DownloadFilesAsync(package.Name, package.AllFiles, modelsDir, progress, ct);

    /// <summary>Downloads the files that are not there yet, with overall progress.</summary>
    public static async Task DownloadFilesAsync(string name, IReadOnlyList<ModelFile> files, string modelsDir, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        long total = files.Sum(f => f.Size);
        long doneBefore = 0;
        foreach (var file in files)
        {
            var dest = Path.Combine(modelsDir, file.RelativePath);
            if (File.Exists(dest))
            {
                doneBefore += file.Size;
                continue;
            }
            long offset = doneBefore;
            await DownloadFileAsync(file.Url, dest, file.Size,
                new Progress<DownloadProgress>(p => progress.Report(new DownloadProgress(
                    p.Stage == "Verifying" ? $"Checking {name}" : $"Downloading {name}", offset + p.Done, total, p.BytesPerSecond))), ct, file.Sha256);
            doneBefore += file.Size;
        }
        Log.Info($"Downloaded {name} to {modelsDir}");
    }

    /// <summary>
    /// Downloads a file with resume: first to .part, at the end checks the size and SHA256 (if known)
    /// and renames it. A damaged file is deleted so the next attempt downloads it again.
    /// </summary>
    public static async Task DownloadFileAsync(string url, string dest, long expectedSize, IProgress<DownloadProgress> progress, CancellationToken ct, string? sha256 = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var part = dest + ".part";

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, part, expectedSize, progress, ct);
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && attempt < 5 && !ct.IsCancellationRequested)
            {
                Log.Info($"Download interrupted ({ex.Message}), retry {attempt}");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }

        if (expectedSize > 0 && new FileInfo(part).Length != expectedSize)
            throw new IOException($"Downloaded size does not match for {Path.GetFileName(dest)}. Try again.");
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            string actual = await Sha256Async(part, progress, ct);
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                Log.Info($"SHA256 mismatch for {Path.GetFileName(dest)}: expected {sha256}, got {actual}");
                throw new IOException($"{Path.GetFileName(dest)} was damaged while downloading (checksum mismatch). Download it again.");
            }
        }
        File.Move(part, dest, overwrite: true);
    }

    private static async Task DownloadOnceAsync(string url, string part, long expectedSize, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (expectedSize > 0 && existing == expectedSize) return;
        if (expectedSize > 0 && existing > expectedSize) { File.Delete(part); existing = 0; }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (existing > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            // The server does not support resume — start over.
            existing = 0;
        }
        resp.EnsureSuccessStatusCode();

        long total = expectedSize > 0 ? expectedSize : (resp.Content.Headers.ContentLength ?? 0) + existing;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);

        var buffer = new byte[1 << 20];
        long done = existing;
        var sw = Stopwatch.StartNew();
        long speedMark = done;
        double speed = 0;
        var lastReport = TimeSpan.Zero;

        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (sw.Elapsed - lastReport > TimeSpan.FromMilliseconds(250))
            {
                double secs = (sw.Elapsed - lastReport).TotalSeconds;
                speed = speed == 0 ? (done - speedMark) / secs : speed * 0.7 + (done - speedMark) / secs * 0.3;
                speedMark = done;
                lastReport = sw.Elapsed;
                progress.Report(new DownloadProgress("Downloading", done, total, speed));
            }
        }
        progress.Report(new DownloadProgress("Downloading", done, total, speed));
    }

    /// <summary>File SHA256 (lowercase hex) with "Verifying" progress.</summary>
    public static async Task<string> Sha256Async(string path, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan | FileOptions.Asynchronous);
        long total = fs.Length, done = 0;
        var buffer = new byte[4 << 20];
        var sw = Stopwatch.StartNew();
        int read;
        while ((read = await fs.ReadAsync(buffer, ct)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            done += read;
            if (sw.ElapsedMilliseconds > 250)
            {
                progress.Report(new DownloadProgress("Verifying", done, total, 0));
                sw.Restart();
            }
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }

    // ---------------- llama-server ----------------

    /// <summary>Downloads and unpacks the llama.cpp build for this graphics card.</summary>
    public static async Task<string> InstallRuntimeAsync(GpuInfo gpu, string runtimeDir, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        progress.Report(new DownloadProgress("Looking for the llama.cpp build", 0, 0, 0));
        var (tag, assets) = await FindRuntimeAssetsAsync(gpu.LlamaBuild, ModelCatalog.LlamaCppTag, ct);

        var temp = Path.Combine(Path.GetTempPath(), "n17YuHelper-runtime");
        Directory.CreateDirectory(temp);
        long total = assets.Sum(a => a.Size);
        long before = 0;
        var zips = new List<string>();
        foreach (var (name, url, size, sha) in assets)
        {
            var zip = Path.Combine(temp, name);
            long offset = before;
            await DownloadFileAsync(url, zip, size, new Progress<DownloadProgress>(p =>
                progress.Report(new DownloadProgress($"Downloading llama.cpp {tag}", offset + p.Done, total, p.BytesPerSecond))), ct, sha);
            zips.Add(zip);
            before += size;
        }

        progress.Report(new DownloadProgress("Unpacking", total, total, 0));
        Directory.CreateDirectory(runtimeDir);
        foreach (var zip in zips)
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, runtimeDir, overwriteFiles: true), ct);
            File.Delete(zip);
        }
        await File.WriteAllTextAsync(Path.Combine(runtimeDir, "VERSION.txt"),
            $"{tag}\n{gpu.LlamaBuild}\n{string.Join("\n", assets.Select(a => a.Name))}\n", ct);

        if (!File.Exists(Path.Combine(runtimeDir, "llama-server.exe")))
            throw new IOException("llama-server.exe was not found after unpacking.");
        Log.Info($"Installed llama.cpp {tag} ({gpu.LlamaBuild}) to {runtimeDir}");
        return tag;
    }

    /// <summary>Installed llama.cpp version (first line of VERSION.txt) or null.</summary>
    public static string? InstalledRuntime(string runtimeDir)
    {
        var exe = Path.Combine(runtimeDir, "llama-server.exe");
        if (!File.Exists(exe)) return null;
        var v = Path.Combine(runtimeDir, "VERSION.txt");
        try { return File.Exists(v) ? File.ReadLines(v).FirstOrDefault()?.Trim() ?? "installed" : "installed"; }
        catch { return "installed"; }
    }

    /// <summary>
    /// Finds the Windows llama.cpp build of the right kind. If the catalog sets a tested tag — use it;
    /// if it is missing (or the tag is empty) — the newest release with a Windows build ("latest" sometimes has only sources,
    /// so the last 30 are checked). File SHA256 comes from GitHub's digest field, if present.
    /// </summary>
    private static async Task<(string Tag, List<(string Name, string Url, long Size, string? Sha256)> Assets)> FindRuntimeAssetsAsync(string build, string pinnedTag, CancellationToken ct)
    {
        var releases = new JsonArray();
        if (!string.IsNullOrWhiteSpace(pinnedTag))
        {
            try
            {
                var one = await GetJsonAsync($"https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/{Uri.EscapeDataString(pinnedTag)}", ct);
                if (one is not null) releases.Add(one);
            }
            catch (Exception ex) when (ex is HttpRequestException)
            {
                Log.Info($"llama.cpp {pinnedTag} not found ({ex.Message}), looking for the latest build");
            }
        }
        if (releases.Count == 0 && await GetJsonAsync("https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=30", ct) is JsonArray latest)
            foreach (var r in latest.ToList()) { latest.Remove(r); releases.Add(r); }

        string mainPattern = build switch
        {
            "cuda-13" => @"^llama-.+-bin-win-cuda-13\.\d+-x64\.zip$",
            "cuda-12" => @"^llama-.+-bin-win-cuda-12\.\d+-x64\.zip$",
            "vulkan" => @"^llama-.+-bin-win-vulkan-x64\.zip$",
            _ => @"^llama-.+-bin-win-cpu-x64\.zip$",
        };
        string? cudartPattern = build switch
        {
            "cuda-13" => @"^cudart-llama-bin-win-cuda-13\.\d+-x64\.zip$",
            "cuda-12" => @"^cudart-llama-bin-win-cuda-12\.\d+-x64\.zip$",
            _ => null,
        };

        foreach (var rel in releases)
        {
            if (rel?["assets"] is not JsonArray assets) continue;
            (string, string, long, string?)? Find(string pattern)
            {
                foreach (var a in assets)
                {
                    var name = a?["name"]?.GetValue<string>() ?? "";
                    if (!System.Text.RegularExpressions.Regex.IsMatch(name, pattern)) continue;
                    var digest = a!["digest"]?.GetValue<string>();
                    string? sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
                    return (name, a["browser_download_url"]!.GetValue<string>(), a["size"]?.GetValue<long>() ?? 0, sha);
                }
                return null;
            }

            var main = Find(mainPattern);
            var cudart = cudartPattern is null ? null : Find(cudartPattern);
            if (main is null || (cudartPattern is not null && cudart is null)) continue;

            var list = new List<(string, string, long, string?)> { main.Value };
            if (cudart is { } c) list.Add(c);
            return (rel["tag_name"]?.GetValue<string>() ?? "?", list);
        }
        if (!string.IsNullOrWhiteSpace(pinnedTag))
        {
            Log.Info($"llama.cpp {pinnedTag} has no Windows {build} build, looking for the latest one");
            return await FindRuntimeAssetsAsync(build, "", ct);
        }
        throw new InvalidOperationException($"No Windows {build} build found in llama.cpp releases.");
    }

    private static async Task<JsonNode?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    public static long FreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root is null ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }
}
