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

/// <summary>Ошибка настройки: нет llama-server.exe или файла модели.</summary>
public sealed class ModelSetupException(string message) : Exception(message);

/// <summary>Что и как запускать.</summary>
/// <param name="FallbackArgs">
/// Аргументы без необязательной части (например, без аудио-проектора): если с ней сервер не запустился
/// (старая сборка llama.cpp), пробуем ещё раз без неё.
/// </param>
public sealed record ServerLaunch(string Exe, IReadOnlyList<string> Args, string LogFile, IReadOnlyList<string> RequiredFiles,
    IReadOnlyList<string>? FallbackArgs = null);

/// <summary>
/// Один процесс llama-server с одной моделью: запуск по требованию, проверка готовности,
/// остановка (по таймеру простоя или по команде). Все процессы в Job Object —
/// при выходе программы они завершаются автоматически.
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

    /// <summary>Вызывается перед запуском (например, чтобы выгрузить другую модель в режиме «по очереди»).</summary>
    public Action? BeforeStart { get; set; }
    public ServerState State { get; private set; } = ServerState.Stopped;

    /// <summary>Сервер запущен без необязательной части (см. ServerLaunch.FallbackArgs).</summary>
    public bool UsedFallback { get; private set; }
    public Uri BaseUri => new($"http://127.0.0.1:{_port}/");
    public DateTime LastUsedUtc => new(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);

    /// <param name="launchFactory">Получает порт, возвращает параметры запуска.</param>
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

    /// <summary>Запускает сервер, если он не запущен, и ждёт готовности модели.</summary>
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

    /// <summary>Запускает llama-server и ждёт, пока модель загрузится (/health отвечает 200).</summary>
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
            catch (HttpRequestException) { /* сервер ещё не слушает порт */ }
            catch (TaskCanceledException) { /* медленный ответ во время загрузки */ }

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"llama-server did not become ready in 3 minutes.\n{TailOf(launch.LogFile, 6)}");
            await Task.Delay(150);
        }
    }

    /// <summary>Останавливает сервер и освобождает видеопамять.</summary>
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
        try { File.Delete(file); } catch { /* файл может быть занят — не важно */ }
    }

    private static string Quote(string a) => a.Contains(' ') ? $"\"{a}\"" : a;

    public void Dispose() => Stop("exit");
}
