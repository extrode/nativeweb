using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
public abstract class SampleProcessFixture : IAsyncLifetime
{
    /// <summary>
    /// Ceiling on the one NativeAOT publish this tier runs.
    /// </summary>
    /// <remarks>
    /// Thirty, not fifteen, and the same number as
    /// <c>tests/nativeweb.web.tests.e2e/AppProcessFixture.cs</c> for the same
    /// reason — that fixture was raised first, and this one was left behind.
    /// <para>
    /// Measured: on 2026-08-25 all thirteen facts in this tier failed at exactly
    /// <c>00:15:00</c> with a publish that had not finished, while a review worker
    /// was saturating the CPU. Re-run alone on the same commit, 13/13 green in 2 s.
    /// So the tier is not flaky — it had no headroom under load, and a CI runner
    /// sharing a host looks exactly like that machine did.
    /// </para>
    /// <para>
    /// The ceiling still has to exist: without it a hung link waits on whatever
    /// the CI job's own timeout happens to be, and the job dies saying nothing
    /// about what it was doing. But it has to be a number no healthy build can
    /// reach, because the failure it catches is rare and a false trip costs a
    /// whole green suite.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The publish, run once for the whole tier however many fixtures want it.
    /// </summary>
    /// <remarks>
    /// Both fixtures launch the same binary, and xUnit initializes the collection's
    /// fixtures concurrently -- putting them in one collection does not serialize
    /// them, which is what the first attempt assumed. Two `dotnet publish` runs
    /// against one project then race for the same intermediate files, and ILC fails
    /// with "the process cannot access the file ... .sourcelink", several layers
    /// away from anything that mentions parallelism.
    ///
    /// Lazy&lt;Task&gt; rather than a lock: the second fixture awaits the first
    /// publish's result instead of running its own no-op one, and a failure is
    /// observed by both.
    /// </remarks>
    private static readonly Lazy<Task<string>> PublishedExe = new(PublishAsync);

    /// <summary>
    /// Names the lock the two e2e tiers hold while publishing <c>apps/sample</c>.
    /// </summary>
    /// <remarks>
    /// The Lazy above serializes this process's own two fixtures. It cannot serialize the
    /// other tier: <c>tests/nativeweb.signin.tests.e2e</c> publishes the same project, and
    /// `dotnet test` runs test assemblies in separate processes concurrently, so the same
    /// `.sourcelink` contention returns one level up. A lock file under the repository's own
    /// `tmp/` is what crosses the process boundary; whichever tier arrives second waits until the first
    /// tier has EXITED, then publishes against no running image.
    /// </remarks>
    private const string PublishLock = "sample-publish.lock";

    private Process? _process;

    private readonly LogTail _log = new();

    private readonly string? _connectionString;

    private readonly string _environment;

    private readonly IReadOnlyDictionary<string, string> _extraEnvironment;

    private readonly bool _https;

    /// <param name="port">
    /// One port per store, because both run for the whole tier: the same published
    /// binary launched twice, differing only in configuration.
    /// </param>
    /// <param name="connectionString">
    /// Null selects the in-memory store, exactly as an unset key does in production.
    /// </param>
    /// <param name="environment">
    /// <c>ASPNETCORE_ENVIRONMENT</c>. Development by default, because that is where the
    /// one-click <c>/signin</c> lives and the admin suites go in through it.
    /// </param>
    /// <param name="extraEnvironment">
    /// Any further variables the process needs, such as the passphrase a Production
    /// instance refuses to start without (spec 020).
    /// </param>
    /// <param name="https">
    /// Bind https with a certificate minted for the run. Outside Development the
    /// antiforgery and session cookies are <c>Secure</c>, the antiforgery system refuses
    /// to mint a token for a request it does not consider TLS, and the cookie
    /// challenge redirects to an absolute URL on the request's scheme -- so a
    /// Production instance has to be reached the way Caddy reaches it, over TLS.
    /// </param>
    protected SampleProcessFixture(
        int port,
        string? connectionString,
        string environment = "Development",
        IReadOnlyDictionary<string, string>? extraEnvironment = null,
        bool https = false)
    {
        BaseUrl = https ? $"https://localhost:{port}" : $"http://localhost:{port}";
        _connectionString = connectionString;
        _environment = environment;
        _extraEnvironment = extraEnvironment ?? new Dictionary<string, string>();
        _https = https;
    }

    /// <summary>Where this instance of the sample is listening.</summary>
    public string BaseUrl { get; }

    /// <summary>The sample's most recent log lines, oldest first.</summary>
    public string LogTail => _log.ToString();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        string exePath = await PublishedExe.Value.ConfigureAwait(false);

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
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = _environment;

        foreach ((string name, string value) in _extraEnvironment)
        {
            startInfo.EnvironmentVariables[name] = value;
        }

        if (_https)
        {
            (string path, string password) = Certificate();
            startInfo.EnvironmentVariables["ASPNETCORE_Kestrel__Certificates__Default__Path"] = path;
            startInfo.EnvironmentVariables["ASPNETCORE_Kestrel__Certificates__Default__Password"] = password;
        }

        // An environment variable rather than a command-line argument: this is the
        // mechanism a deployment uses, and the published binary reads it through the
        // same configuration provider either way. Absent, the app selects the
        // in-memory store -- which is the in-memory fixture, not a fallback.
        if (_connectionString is not null)
        {
            startInfo.EnvironmentVariables["ConnectionStrings__Sample"] = _connectionString;
        }
        else
        {
            // Cleared, not merely left unset. UseShellExecute=false copies the
            // parent environment, so a runner that exports ConnectionStrings__Sample
            // for the JauntyQ tiers would hand it to this process too -- and the
            // in-memory fixture would quietly start talking to PostgreSQL, where the
            // rows its tests assert on do not exist. "Unset here" has to mean unset
            // in the child, not unset in this file.
            startInfo.EnvironmentVariables.Remove("ConnectionStrings__Sample");
        }

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

            // /healthz, which apps/sample gained on 2026-08-31 so the bench harness had a
            // no-render floor to measure. It is outside the IsDevelopment() block and returns
            // 200 with no body. Readiness is still "it answered at all" rather than the status
            // code: this only needs to know the listener is up.
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using HttpResponseMessage response =
                        await client.GetAsync(new Uri($"{BaseUrl}/healthz")).ConfigureAwait(false);
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

    /// <summary>Publishes apps/sample Release and returns the path to the binary.</summary>
    /// <summary>
    /// Holds the cross-process publish lock, releasing it on dispose.
    /// </summary>
    /// <remarks>
    /// A file rather than a <see cref="Mutex"/>, and that is not a preference. A Windows mutex
    /// has thread affinity and this publish is awaited, so the continuation lands on another
    /// thread-pool thread and <c>ReleaseMutex</c> throws
    /// "Object synchronization method was called from an unsynchronized block of code" --
    /// measured, on the first run of this tier. A file handle is owned by the process.
    /// </remarks>
    private static async Task<FileStream> HoldPublishLockAsync(string repoRoot)
    {
        string directory = Path.Combine(repoRoot, "tmp");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, PublishLock);
        DateTime deadline = DateTime.UtcNow + PublishTimeout;

        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                // The other tier is publishing. Nothing to do but wait for it.
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The cross-process publish lock, held for the LIFETIME OF THIS TEST PROCESS. Never disposed:
    /// the operating system closes the handle when the assembly's process exits, which is the
    /// point at which the published exe stops running and the other tier may safely rewrite it.
    /// </summary>
    /// <remarks>
    /// Releasing it after the publish and before the launch was measured to fail. Both e2e tiers
    /// publish apps/sample and `dotnet test` runs assemblies concurrently, so the second tier's
    /// publish tried to overwrite an exe the first tier had running; Windows refuses to write a
    /// running image and the fixture died at startup -- 4 failures in 23 ms, passing on its own the
    /// moment the tiers did not overlap. The reading that the second publish is incremental and
    /// writes nothing holds only while the binary is already current, which is not true of the run
    /// that changed the code.
    /// </remarks>
    private static FileStream? _publishLock;

    private static async Task<string> PublishAsync()
    {
        string repoRoot = FindRepoRoot();
        string csproj = Path.Combine(repoRoot, "apps", "sample", "nativeweb.sample.csproj");

        _publishLock = await HoldPublishLockAsync(repoRoot).ConfigureAwait(false);

        await RunAsync("dotnet", $"publish \"{csproj}\" -c Release", repoRoot, PublishTimeout)
            .ConfigureAwait(false);

        string exePath = Path.Combine(
            repoRoot, "apps", "sample", "bin", "Release", "net10.0", RuntimeInformation.RuntimeIdentifier, "publish",
            OperatingSystem.IsWindows() ? "nativeweb.sample.exe" : "nativeweb.sample");

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Expected Release publish output at {exePath}.", exePath);
        }

        return exePath;
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

    /// <summary>
    /// Mints a self-signed certificate for <c>localhost</c> and writes it where Kestrel can
    /// read it, returning the path and the password guarding the private key.
    /// </summary>
    /// <remarks>
    /// The same shape as the sign-in tier's: under the repository's own <c>tmp/</c>, never in
    /// a trust store, and the browser is told to ignore it rather than the machine being told
    /// to believe it.
    /// </remarks>
    private static (string Path, string Password) Certificate()
    {
        string directory = Path.Combine(FindRepoRoot(), "tmp", "scaffold-e2e");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "kestrel.pfx");
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));

        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));

        return (path, password);
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

/// <summary>The demo with no database configured: the in-memory store.</summary>
public sealed class InMemorySampleFixture() : SampleProcessFixture(5347, null);

/// <summary>
/// The same binary with a connection string, so <c>IScaffoldStore&lt;Article&gt;</c>
/// resolves to the JauntyQ store.
/// </summary>
/// <remarks>
/// This fixture is the only place in the repo where JauntyQ-generated code runs
/// under NativeAOT. The integration tier exercises the same queries, but through a
/// JIT-compiled test host; a data layer that works there and trims away here would
/// fail for the first time in production.
/// </remarks>
public sealed class JauntySampleFixture() : SampleProcessFixture(5348, SampleE2eDatabase.ConnectionString);

/// <summary>
/// The same binary as Production sees it: no one-click sign-in, the passphrase form
/// instead, and a secret it would refuse to start without (spec 020).
/// </summary>
/// <remarks>
/// Over https, for the reason the base constructor gives. Port 5351: the sign-in tier
/// runs its own instance of the sample on 5349 and 5350, and <c>dotnet test</c> runs
/// the two tiers' assemblies at the same time.
/// </remarks>
public sealed class PassphraseSampleFixture() : SampleProcessFixture(
    5351,
    null,
    "Production",
    new Dictionary<string, string> { ["Sample__Admin__Passphrase"] = Secret },
    https: true)
{
    /// <summary>What the fixture hands the process, and what the tests type.</summary>
    public const string Secret = "correct horse battery staple";
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
