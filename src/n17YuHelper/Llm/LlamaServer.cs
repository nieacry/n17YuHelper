using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace N17YuHelper.Llm;

public enum ServerState
{
    Stopped,
    Starting,
    Ready,
    Failed,
}

/// <summary>Configuration error: llama-server.exe or the model file is missing.</summary>
public sealed class ModelSetupException(string message) : Exception(message);

/// <summary>What to start and how.</summary>
/// <param name="FallbackArgs">
/// Arguments without the optional part (e.g. without the audio projector): if the server failed to start with it
/// (an old llama.cpp build), try again without it.
/// </param>
public sealed record ServerLaunch(string Exe, IReadOnlyList<string> Args, string LogFile, IReadOnlyList<string> RequiredFiles,
    IReadOnlyList<string>? FallbackArgs = null);

/// <summary>
/// One llama-server process with one model: started on demand, readiness check,
/// stop (idle timer or command). All processes are in a Job Object —
/// they are terminated automatically when the app exits.
/// </summary>
public sealed class LlamaServer : IDisposable
{
    private static readonly JobObject Job = new();
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly object _sync = new();
    private readonly Func<int, ServerLaunch> _launchFactory;
    private readonly int _configuredPort;
    private Process? _process;
    private Task? _startTask;
    private int _port;
    private long _lastUsedTicks = DateTime.UtcNow.Ticks;

    public string Name { get; }

    /// <summary>Called before starting (e.g. to unload the other model in one-at-a-time mode).</summary>
    public Action? BeforeStart { get; set; }
    public ServerState State { get; private set; } = ServerState.Stopped;

    /// <summary>The server was started without the optional part (see ServerLaunch.FallbackArgs).</summary>
    public bool UsedFallback { get; private set; }
    public Uri BaseUri => new($"http://127.0.0.1:{_port}/");
    public DateTime LastUsedUtc => new(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);

    /// <param name="launchFactory">Gets the port, returns the launch parameters.</param>
    public LlamaServer(string name, int port, Func<int, ServerLaunch> launchFactory)
    {
        Name = name;
        _configuredPort = port;
        _launchFactory = launchFactory;
    }

    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch { return false; }
        }
    }

    /// <summary>Starts the server if it is not running and waits until the model is ready.</summary>
    public Task EnsureReadyAsync(CancellationToken ct = default)
    {
        Task task;
        lock (_sync)
        {
            bool needStart = _startTask is null
                             || _startTask.IsFaulted
                             || _startTask.IsCanceled
                             || (_startTask.IsCompletedSuccessfully && !IsRunning);
            if (needStart)
                _startTask = Task.Run(StartAsync);
            task = _startTask!;
        }
        Touch();
        return task.WaitAsync(ct);
    }

    public void Touch() => Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);

    private async Task StartAsync()
    {
        try { BeforeStart?.Invoke(); }
        catch (Exception ex) { Log.Error($"[{Name}] BeforeStart", ex); }
        StopProcess();
        State = ServerState.Starting;
        var sw = Stopwatch.StartNew();

        try
        {
            _port = _configuredPort > 0 ? _configuredPort : GetFreePort();
            var launch = _launchFactory(_port);

            if (!File.Exists(launch.Exe))
                throw new ModelSetupException($"llama-server.exe not found: {launch.Exe}");
            foreach (var f in launch.RequiredFiles)
                if (!File.Exists(f))
                    throw new ModelSetupException($"Model file not found: {f}");

            Directory.CreateDirectory(Path.GetDirectoryName(launch.LogFile)!);
            UsedFallback = false;
            try
            {
                await RunAndWaitAsync(launch, launch.Args);
            }
            catch (InvalidOperationException ex) when (launch.FallbackArgs is not null)
            {
                Log.Info($"[{Name}] failed with the optional part, retrying without it: {ex.Message}");
                StopProcess();
                UsedFallback = true;
                await RunAndWaitAsync(launch, launch.FallbackArgs);
            }

            State = ServerState.Ready;
            Touch();
            Log.Info($"[{Name}] ready on port {_port} in {sw.Elapsed.TotalSeconds:0.0} s");
        }
        catch (Exception ex)
        {
            State = ServerState.Failed;
            Log.Error($"[{Name}] start failed", ex);
            StopProcess();
            throw;
        }
    }

    /// <summary>Starts llama-server and waits until the model is loaded (/health returns 200).</summary>
    private async Task RunAndWaitAsync(ServerLaunch launch, IReadOnlyList<string> args)
    {
        TryDelete(launch.LogFile);
        var psi = new ProcessStartInfo(launch.Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(launch.Exe)!,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Log.Info($"[{Name}] starting: \"{launch.Exe}\" {string.Join(' ', args.Select(Quote))}");
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start llama-server.");
        Job.Add(process);
        _process = process;

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
        while (true)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"llama-server exited with code {process.ExitCode}.\n{TailOf(launch.LogFile, 6)}");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var resp = await Http.GetAsync(new Uri(BaseUri, "health"), cts.Token);
                if (resp.StatusCode == HttpStatusCode.OK) break;
            }
            catch (HttpRequestException) { /* the server is not listening yet */ }
            catch (TaskCanceledException) { /* slow reply while loading */ }

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"llama-server did not become ready in 3 minutes.\n{TailOf(launch.LogFile, 6)}");
            await Task.Delay(150);
        }
    }

    /// <summary>Stops the server and frees GPU memory.</summary>
    public void Stop(string reason)
    {
        lock (_sync)
        {
            if (_process is null && State == ServerState.Stopped) return;
            Log.Info($"[{Name}] stopping ({reason})");
            StopProcess();
            _startTask = null;
            State = ServerState.Stopped;
        }
    }

    private void StopProcess()
    {
        var p = _process;
        _process = null;
        if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[{Name}] kill failed", ex);
        }
        finally
        {
            p.Dispose();
        }
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string TailOf(string file, int lines)
    {
        try
        {
            if (!File.Exists(file)) return "";
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var all = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("\n", all.TakeLast(lines)).Trim();
        }
        catch
        {
            return "";
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { /* the file may be locked — does not matter */ }
    }

    private static string Quote(string a) => a.Contains(' ') ? $"\"{a}\"" : a;

    public void Dispose() => Stop("exit");
}
