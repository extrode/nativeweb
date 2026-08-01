using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Xunit;

namespace __PRODUCT__.Tests.E2e;

// Launches the real apps/__product__ process (not an in-memory TestServer) so
// Playwright's actual browser has a real socket to navigate to.
public sealed class __PRODUCT__ProcessFixture : IAsyncLifetime
{
    // https, not http: the session cookie a product adds is __Host- prefixed
    // and therefore Secure-always, and Chromium accepts the Secure attribute
    // from insecure localhost but refuses the __Host- prefix there -- over
    // http every signed-in flow would silently lose its session. The tier
    // serves https from the start so that day needs no fixture change. The
    // fixture mints its own self-signed cert below; browser contexts set
    // IgnoreHTTPSErrors and raw clients use E2eHttp.
    public string BaseUrl { get; } = "https://localhost:5350";

    private const string CertPassword = "__product__-e2e";

    private Process? _process;

    private string? _certPath;

    public async Task InitializeAsync()
    {
        string repoRoot = FindRepoRoot();
        string csproj = Path.Combine(repoRoot, "apps", "__product__", "__product__.csproj");

        await RunAsync("dotnet", $"publish \"{csproj}\" -c Release", repoRoot).ConfigureAwait(false);

        string exePath = PublishedExePath(repoRoot);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Expected Release build output at {exePath}.", exePath);
        }

        // A real deployment runs this exe from inside its own publish/ folder;
        // without an explicit WorkingDirectory here it inherits the test
        // runner's CWD instead, which breaks ContentRootPath-relative lookups
        // (wwwroot).
        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
        // Self-minted cert rather than `dotnet dev-certs`: deterministic on any
        // machine and on CI, no user cert store involved, nothing to trust.
        _certPath = CreateSelfSignedPfx();
        startInfo.EnvironmentVariables["ASPNETCORE_Kestrel__Certificates__Default__Path"] = _certPath;
        startInfo.EnvironmentVariables["ASPNETCORE_Kestrel__Certificates__Default__Password"] = CertPassword;
        // Playwright submits forms machine-fast, which would trip the bot-trap
        // MinAge canary check; the trap fields themselves stay live and are
        // still posted for real by the browser.
        startInfo.EnvironmentVariables["__PRODUCT____BotTraps__MinAgeSeconds"] = "0";
        // The whole suite shares one loopback rate-limit partition, so the
        // production windows would throttle the tests, not a client. Mirrors
        // the integration factory.
        startInfo.EnvironmentVariables["__PRODUCT____RateLimit__General__PermitLimit"] = "100000";
        startInfo.EnvironmentVariables["__PRODUCT____RateLimit__CspReport__PermitLimit"] = "100000";

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start apps/__product__.");

        // The redirected streams must be drained for the app's whole lifetime:
        // the request-logging middleware writes a line per request, and once
        // the OS pipe buffer fills an undrained stream blocks the app on its
        // next log write, hanging the suite at an arbitrary point.
        _process.OutputDataReceived += static (_, _) => { };
        _process.ErrorDataReceived += static (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using HttpClient client = E2eHttp.CreateClient();
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(new Uri($"{BaseUrl}/healthz")).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet -- keep polling until the deadline.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        // xUnit doesn't call DisposeAsync when InitializeAsync throws -- kill
        // the spawned tree, release the Process handle and delete the minted
        // key pair ourselves, or the port (and a private key in %TEMP%) stays
        // behind on every failed startup.
        _process.Kill(entireProcessTree: true);
        _process.Dispose();
        DeleteCert();
        throw new TimeoutException("apps/__product__ did not report healthy within 60 seconds.");
    }

    public Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }

        _process?.Dispose();
        DeleteCert();

        return Task.CompletedTask;
    }

    // The fixture's own scratch: a throwaway key pair minted for this run.
    // Best-effort -- if the killed process's handle release races the delete,
    // a leftover throwaway PFX must not eclipse the failure that got us here
    // (or fail the whole collection on the success path).
    private void DeleteCert()
    {
        if (_certPath is null)
        {
            return;
        }

        try
        {
            File.Delete(_certPath);
        }
        catch (IOException)
        {
        }
    }

    private static string CreateSelfSignedPfx()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        // serverAuth EKU -- some TLS stacks refuse a server cert without it.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        string path = Path.Combine(Path.GetTempPath(), $"__product__-e2e-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, CertPassword));
        return path;
    }

    private static async Task RunAsync(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName} {arguments}.");
        string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments} exited with {process.ExitCode}.\n{stdout}\n{stderr}");
        }
    }

    // Reads the RID off the running test host so the fixture doesn't silently
    // look in the wrong publish folder on a non-Windows runner.
    private static string PublishedExePath(string repoRoot) =>
        Path.Combine(repoRoot, "apps", "__product__", "bin", "Release", "net10.0", RuntimeInformation.RuntimeIdentifier, "publish",
            OperatingSystem.IsWindows() ? "__product__.exe" : "__product__");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "__product__.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate __product__.slnx above {AppContext.BaseDirectory}.");
    }
}
