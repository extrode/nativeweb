using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Xunit;

namespace NativeWeb.SignIn.Tests.E2e;

/// <summary>
/// Publishes <c>apps/sample</c> and runs the real binary, so Playwright has a socket to
/// complete the redirect round trip against (spec 016 R26).
/// </summary>
/// <remarks>
/// <para>
/// The tiers below this one both stop at an <see cref="HttpClient"/>: the library's own
/// integration tier drives a stub provider in-process, and the sample's drives the demo
/// provider through <c>WebApplicationFactory</c>. Neither is a browser, and three of the
/// things spec 016 asserts are browser behaviour rather than server behaviour — whether
/// Chromium accepts a <c>__Host-</c> cookie at all, whether it sends it back across the
/// provider redirect, and whether it stops sending it after sign-out. A
/// <see cref="System.Net.CookieContainer"/> is more permissive than a browser on every one
/// of those, so a green integration tier says nothing about them.
/// </para>
/// <para>
/// <b>https, over a certificate this fixture mints.</b> http on localhost was tried first, on
/// the reasoning that Chromium treats localhost as a trustworthy origin and would therefore
/// accept a <c>Secure</c> cookie there. It does not: all four facts failed with
/// <c>ERR_TOO_MANY_REDIRECTS</c>, because Chromium dropped the <c>__Host-</c> cookie on the
/// callback and every visit to <c>/account</c> began the flow again. That is the same
/// constraint <c>tests/nativeweb.scaffold.tests.e2e/SampleProcessFixture.cs</c> records for
/// the departed web tier, and it applies to localhost as much as to anywhere else.
/// </para>
/// <para>
/// The certificate is self-signed and Playwright is told to ignore it, rather than being put
/// in a trust store — a test may not change the machine. That leaves one problem: the app's
/// own relying party calls its own token endpoint, and its HttpClient has no reason to trust
/// a certificate a test minted. So the app is bound twice, and told to use the http binding
/// for the back channel. A real provider is a different host, so front channel and back
/// channel being separately addressable is the ordinary shape rather than a concession.
/// </para>
/// </remarks>
public sealed class SignInProcessFixture : IAsyncLifetime
{
    /// <summary>Ceiling on the one NativeAOT publish this tier runs.</summary>
    /// <remarks>
    /// Thirty, matching the scaffold tier, and for the reason recorded there: fifteen was
    /// reached by a healthy build on a loaded machine, and a false trip costs a whole suite.
    /// </remarks>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Names the lock the two e2e tiers hold while publishing <c>apps/sample</c>.
    /// </summary>
    /// <remarks>
    /// Both this tier and <c>nativeweb.scaffold.tests.e2e</c> publish the same project, and
    /// <c>dotnet test</c> runs test assemblies in separate processes concurrently. Two ILC
    /// runs over one project fight over the intermediates and fail on <c>.sourcelink</c>,
    /// several layers away from anything that mentions parallelism — which is the failure
    /// <c>SampleProcessFixture.PublishedExe</c> documents within one process. A
    /// lock file under the repository's own <c>tmp/</c> is what extends that guarantee across
    /// processes; the second tier waits, then runs an incremental no-op publish rather than a
    /// competing one.
    /// </remarks>
    private const string PublishLock = "sample-publish.lock";

    private Process? _process;

    private readonly LogTail _log = new();

    /// <summary>Where the browser goes: the origin the session cookie belongs to.</summary>
    /// <remarks>
    /// Assigned in <see cref="InitializeAsync"/>, not in a constructor. A free port is only
    /// free until something takes it, and the constructor runs before a publish that holds a
    /// cross-process lock and can take half an hour -- so choosing there would leave the
    /// widest possible window between the port being observed free and Kestrel binding it.
    /// </remarks>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Where the app's own relying party calls the demo provider.</summary>
    private string _backChannel = string.Empty;

    /// <summary>
    /// Two ports the operating system says are free, for the front and back channels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These were the literals 5349 and 5350 until 2026-09-05, and on this workstation the tier
    /// failed 4/4 with "An attempt was made to access a socket in a way forbidden by its access
    /// permissions". Nothing held them: `netsh interface ipv4 show excludedportrange protocol=tcp`
    /// lists both as excluded ranges, and a bind inside one is refused whether or not anything is
    /// listening. The exclusions are dynamic reservations rather than a deliberate registration of
    /// those two numbers, so which ports they cover is a property of the machine and its Hyper-V
    /// and WinNAT state, not something a literal here could be chosen to avoid. Asking for port 0
    /// fixes the class rather than the pair: the operating system does not allocate out of a range
    /// it has excluded.
    /// </para>
    /// <para>
    /// Both loopback addresses, because the app is given `localhost`, which resolves to
    /// 127.0.0.1 and ::1 and binds each. An IPv4-only probe would leave the ::1 half unchecked,
    /// and the two exclusion lists are separate -- `netsh interface ipv6 show excludedportrange`
    /// is its own -- so it would pass on this machine, where they agree, and miss the gap on one
    /// where they diverge.
    /// </para>
    /// <para>
    /// This was <c>TcpListener.Create(0)</c> until 2026-09-05, on the argument that dual-stack
    /// was the faithful shape. It is not: that binds the wildcard, and on Windows a wildcard
    /// bind coexists with a listener already on 127.0.0.1, so the operating system will hand out
    /// a port the probe can take and Kestrel cannot. Measured in the scaffold tier, where a
    /// deliberate break returning an occupied 5348 was accepted by a wildcard probe and refused
    /// by the loopback pair.
    /// </para>
    /// <para>
    /// Both listeners are held open until both ports are known. Allocated one at a time, the
    /// operating system is free to hand the second call the port the first just released, and the
    /// app would be told to bind one endpoint twice.
    /// </para>
    /// </remarks>
    internal static (int Front, int Back) FreePorts()
    {
        using TcpListener front = Probe();
        using TcpListener back = Probe();

        return (((IPEndPoint)front.LocalEndpoint).Port, ((IPEndPoint)back.LocalEndpoint).Port);
    }

    /// <summary>
    /// A started listener on a loopback port that ::1 will also accept, left open by the caller
    /// until every port it needs is known.
    /// </summary>
    /// <summary>Whether <c>::1</c> can be bound here, tried once and remembered.</summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="Socket.OSSupportsIPv6"/>, which is computed by creating an IPv6 socket and
    /// so answers whether the stack exists, not whether the loopback address can be bound. A
    /// machine with IPv6 switched off by policy answers true to the flag and then refuses every
    /// bind: the loop below would spend all 32 attempts failing for one cause and throw a message
    /// blaming port availability, sending whoever reads it after the exclusion ranges.
    /// </para>
    /// <para>
    /// Reported by justsmtp.com on 2026-09-05, in the same exchange that corrected the wildcard
    /// probe here. Tried once because the answer cannot change while the process runs, and a bind
    /// per attempt would pay for it 32 times over.
    /// </para>
    /// </remarks>
    internal static readonly bool LoopbackV6Bindable = ProbeLoopbackV6();

    private static bool ProbeLoopbackV6()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return false;
        }

        try
        {
            using TcpListener v6 = new(IPAddress.IPv6Loopback, 0);

            v6.Start();

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static TcpListener Probe()
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            TcpListener v4 = new(IPAddress.Loopback, 0);

            v4.Start();

            int port = ((IPEndPoint)v4.LocalEndpoint).Port;

            if (!LoopbackV6Bindable)
            {
                return v4;
            }

            try
            {
                using TcpListener v6 = new(IPAddress.IPv6Loopback, port);

                v6.Start();

                return v4;
            }
            catch (SocketException)
            {
                v4.Stop();
                v4.Dispose();
            }
        }

        throw new InvalidOperationException(
            "No port free on both loopback addresses after 32 attempts.");
    }

    /// <summary>The sample's most recent log lines, oldest first.</summary>
    public string LogTail => _log.ToString();

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        string exePath = await PublishAsync().ConfigureAwait(false);

        // After the publish, not before it: see BaseUrl.
        (int front, int back) = FreePorts();
        BaseUrl = $"https://localhost:{front}";
        _backChannel = $"http://localhost:{back}";

        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // https first: DemoProvider.SelfOrigin takes the first binding, and that is the origin
        // the browser walks and the redirect URI is registered under.
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $"{BaseUrl};{_backChannel}";
        startInfo.EnvironmentVariables["Demo__BackChannelOrigin"] = _backChannel;

        (string path, string password) = Certificate();
        startInfo.EnvironmentVariables["ASPNETCORE_Kestrel__Certificates__Default__Path"] = path;
        startInfo.EnvironmentVariables["ASPNETCORE_Kestrel__Certificates__Default__Password"] = password;

        // The whole sign-in demo — the provider, the relying party pointed at it, /account
        // and /welcome — is inside the app's IsDevelopment gate. Outside it there is nothing
        // here to drive.
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";

        // The store this tier asserts on is the session store, which is in-memory whatever
        // the database says. Cleared rather than left unset so a runner exporting it for the
        // JauntyQ tiers does not point this process at PostgreSQL for no reason.
        startInfo.EnvironmentVariables.Remove("ConnectionStrings__Sample");

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start apps/sample.");

        try
        {
            // A redirected stream that nobody drains fills its pipe buffer and blocks the app
            // on its next Console write: it stops answering without exiting, so the suite
            // hangs rather than fails.
            _process.OutputDataReceived += (_, e) => _log.Add(e.Data);
            _process.ErrorDataReceived += (_, e) => _log.Add(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Readiness is checked on the https binding, not the http one, because the https
            // binding is the one that can fail on its own: a certificate Kestrel will not load
            // takes the endpoint down while the plain binding comes up and answers.
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
                $"apps/sample did not answer within 60 seconds. Log tail:\n{StopAndReadLog()}");
        }
        catch
        {
            StopAndReadLog();
            throw;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        StopAndReadLog();

        return ValueTask.CompletedTask;
    }

    private string StopAndReadLog()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10_000);
        }

        _process?.Dispose();
        _process = null;
        return _log.ToString();
    }

    /// <summary>
    /// Mints a self-signed certificate for <c>localhost</c> and writes it where Kestrel can read
    /// it, returning the path and the password guarding the private key.
    /// </summary>
    /// <remarks>
    /// Under the repository's own <c>tmp/</c>, which is gitignored, and never in a trust store:
    /// this is a key that exists for one test run, and the browser is told to ignore it rather
    /// than the machine being told to believe it.
    /// </remarks>
    private static (string Path, string Password) Certificate()
    {
        string directory = Path.Combine(FindRepoRoot(), "tmp", "signin-e2e");
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
/// A bounded ring of log lines, so a failure prints the end of the app's output rather than
/// a publish's entire output being held to print twenty lines of it.
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
