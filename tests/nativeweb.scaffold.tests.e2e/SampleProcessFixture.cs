using System.Diagnostics;
using System.Runtime.InteropServices;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// Publishes <c>apps/sample</c> and runs the real binary, so Playwright has a
/// socket to navigate to.
/// </summary>
/// <remarks>
/// <para>
/// The scaffolding tiers below this one both stop short of a browser: the unit
/// tier asserts on emitted source, and the integration tier drives an in-process
/// <c>WebApplicationFactory</c> with an <see cref="HttpClient"/>. Neither has a
/// JavaScript engine, and the scaffolded admin UI has exactly one behaviour that
/// needs one — the delete form's <c>onsubmit="return confirm(…)"</c>. An
/// HttpClient POSTs straight past it, so before this tier nothing in the repo
/// could tell whether that guard worked, or even whether it was still there.
/// </para>
/// <para>
/// Published rather than <c>dotnet run</c>, for the same reason
/// <c>AppProcessFixture</c> gives at length: a build leaves a stale
/// <c>publish/</c> untouched, so a plain build can launch a binary older than
/// the source. Publishing also means the generated endpoints are exercised
/// through NativeAOT, which is the configuration a consumer actually ships.
/// </para>
/// <para>
/// http, not https. The web tier had to move to https because its session cookie
/// carries the <c>__Host-</c> prefix, which Chromium refuses on an insecure
/// origin. The sample's cookie is a plain <c>nw-sample</c>, so that constraint
/// does not apply here and a self-minted certificate would be moving parts with
/// nothing to buy.
/// </para>
/// </remarks>
public sealed class SampleProcessFixture : IAsyncLifetime
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(15);

    private Process? _process;

    private readonly LogTail _log = new();

    /// <summary>Where the sample is listening. 5347 to stay clear of the web e2e tier's 5344.</summary>
    public string BaseUrl { get; } = "http://localhost:5347";

    /// <summary>The sample's most recent log lines, oldest first.</summary>
    public string LogTail => _log.ToString();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        string repoRoot = FindRepoRoot();
        string csproj = Path.Combine(repoRoot, "apps", "sample", "nativeweb.sample.csproj");

        await RunAsync("dotnet", $"publish \"{csproj}\" -c Release", repoRoot, PublishTimeout).ConfigureAwait(false);

        string exePath = Path.Combine(
            repoRoot, "apps", "sample", "bin", "Release", "net10.0", RuntimeInformation.RuntimeIdentifier, "publish",
            OperatingSystem.IsWindows() ? "nativeweb.sample.exe" : "nativeweb.sample");

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Expected Release publish output at {exePath}.", exePath);
        }

        var startInfo = new ProcessStartInfo(exePath)
        {
            // A real deployment runs the exe from inside its own publish folder;
            // inheriting the test runner's CWD breaks ContentRootPath-relative lookups.
            WorkingDirectory = Path.GetDirectoryName(exePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = BaseUrl;
        // /signin grants the admin role for the asking and is gated to Development,
        // and it is the only way into /admin. Without this the whole tier gets 302s
        // to a login page that does not exist outside Development.
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start apps/sample.");

        // Everything after Process.Start is inside this try. xUnit does not call
        // DisposeAsync when InitializeAsync throws, so an escape from here leaves the
        // sample holding port 5347 for every subsequent run — the same trap
        // AppProcessFixture documents, and the reason the readiness loop below catches
        // broadly rather than only HttpRequestException.
        try
        {
            // Redirecting a stream obliges someone to drain it: the OS pipe buffer is a
            // few KB, and once it fills the app blocks forever on its next Console write.
            // It stops answering without exiting, so the suite hangs rather than fails.
            _process.OutputDataReceived += (_, e) => _log.Add(e.Data);
            _process.ErrorDataReceived += (_, e) => _log.Add(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // The sample has no /healthz, so readiness is "/ answers at all". It
            // redirects to /admin, which then redirects to /signin, so the status is not
            // the interesting part — being listening is.
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using HttpResponseMessage response =
                        await client.GetAsync(new Uri($"{BaseUrl}/")).ConfigureAwait(false);
                    return;
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    // Not listening yet, or accepted the connection and stalled.
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"apps/sample did not answer within 60 seconds. Last {LogTail.Length} chars of log:\n{StopAndReadLog()}");
        }
        catch
        {
            StopAndReadLog();
            throw;
        }
    }

    /// <inheritdoc/>
    public Task DisposeAsync()
    {
        StopAndReadLog();
        return Task.CompletedTask;
    }

    private string StopAndReadLog()
    {
        if (_process is { HasExited: false })
        {
            // entireProcessTree: the published binary is the whole app here, but a
            // half-killed tree is what leaves the port held.
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10_000);
        }

        _process?.Dispose();
        _process = null;
        return _log.ToString();
    }

    private static async Task RunAsync(string file, string arguments, string workingDirectory, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {file}.");

        var tail = new LogTail();
        process.OutputDataReceived += (_, e) => tail.Add(e.Data);
        process.ErrorDataReceived += (_, e) => tail.Add(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"`{file} {arguments}` exceeded {timeout}. Log tail:\n{tail}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"`{file} {arguments}` exited {process.ExitCode}. Log tail:\n{tail}");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NativeWeb.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate NativeWeb.slnx above {AppContext.BaseDirectory}.");
    }
}

/// <summary>
/// A bounded ring of log lines. Bounded because the alternative is holding a
/// publish's entire output in memory to print twenty lines of it on failure.
/// </summary>
internal sealed class LogTail
{
    internal const int Capacity = 200;

    private readonly Queue<string> _lines = new(Capacity);

    internal void Add(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_lines)
        {
            if (_lines.Count == Capacity)
            {
                _lines.Dequeue();
            }

            _lines.Enqueue(line);
        }
    }

    public override string ToString()
    {
        lock (_lines)
        {
            return string.Join(Environment.NewLine, _lines);
        }
    }
}
