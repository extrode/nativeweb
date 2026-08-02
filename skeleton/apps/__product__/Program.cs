using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;

using __PRODUCT__.Middleware;
using __PRODUCT__.Render;

using NativeWeb.Render;
using NativeWeb.Security;

using Npgsql;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace __PRODUCT__;

// Explicit entry-point class (no top-level statements, per spec 003 R8). Non-static
// because WebApplicationFactory<Program> in the integration tests needs Program as a
// type argument, which a static class cannot be.
public sealed class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

        // CreateSlimBuilder ships without Kestrel's https support; opt it back in
        // so an ASPNETCORE_URLS=https://... binding with a configured certificate
        // (Kestrel:Certificates:Default) works -- the e2e tier serves https, and a
        // deployment without a TLS-terminating proxy needs it too.
        builder.WebHost.UseKestrelHttpsConfiguration();

        // R28: every request body this app legitimately receives is an interactive
        // form post; Kestrel's 30 MB default is two orders of magnitude of free
        // flood room. 128 KB at the server edge bounds it before any handler runs.
        // An endpoint that ever needs more raises its own limit via
        // IHttpMaxRequestBodySizeFeature.
        builder.WebHost.ConfigureKestrel(static options =>
            options.Limits.MaxRequestBodySize = 128 * 1024);

        // R28: no request runs forever. 30s is generous for a page whose slowest
        // legitimate work is a handful of queries; a handler that outlives it sees
        // its CancellationToken fire.
        builder.Services.AddRequestTimeouts(static options =>
            options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(30) });

        // Local dev reads the connection string from user-secrets; CI and
        // production supply it as a real environment variable.
        builder.Configuration.AddUserSecrets<Program>(optional: true);

        // Shared, pooled NpgsqlDataSource. A box without the connection string still
        // builds and boots -- no data source is registered, /readyz reports not-ready,
        // and the DB-backed routes degrade to 503.
        string? connString = builder.Configuration["__PRODUCT__:Db:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(connString))
        {
            var connStringBuilder = new NpgsqlConnectionStringBuilder(connString);

            // CommandTimeout: honor an explicit override, otherwise leave Npgsql's
            // own default (30s) in place.
            if (int.TryParse(builder.Configuration["__PRODUCT__:Db:CommandTimeoutSeconds"], out int cmdTimeoutSeconds) && cmdTimeoutSeconds > 0)
            {
                connStringBuilder.CommandTimeout = cmdTimeoutSeconds;
            }

            // R35: statements this app runs are a small fixed set (JauntyQ-generated),
            // so auto-preparing anything seen 5+ times on a connection turns nearly
            // every query into a prepared execution with no explicit Prepare() calls.
            // Configurable: behind a transaction-pooling PgBouncer, auto-prepare must
            // be switched OFF (0) or prepared statements break mid-flight, and that
            // remediation cannot require a code change.
            connStringBuilder.MaxAutoPrepare =
                int.TryParse(builder.Configuration["__PRODUCT__:Db:MaxAutoPrepare"], out int maxAutoPrepare) && maxAutoPrepare >= 0
                    ? maxAutoPrepare
                    : 20;

            builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connStringBuilder.ConnectionString).Build());
        }

        // Behind a reverse proxy/LB, RemoteIpAddress is the proxy's own socket peer unless
        // X-Forwarded-For is applied -- without this, every client collapses into one
        // rate-limit partition (the proxy's IP). ASP.NET Core only trusts loopback by
        // default; production must set __PRODUCT__:ForwardedHeaders:KnownProxies to the real
        // proxy/LB address(es), or forwarded headers are ignored (safe default: no spoofing
        // via an untrusted hop, but rate limiting silently degrades to one shared partition
        // until configured).
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            string? knownProxies = builder.Configuration["__PRODUCT__:ForwardedHeaders:KnownProxies"];
            if (!string.IsNullOrWhiteSpace(knownProxies))
            {
                foreach (string ip in knownProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (IPAddress.TryParse(ip, out IPAddress? parsed))
                    {
                        options.KnownProxies.Add(parsed);
                    }
                }
            }
        });

        // R32: Brotli for the HTML the app renders; the fingerprinted static assets
        // ship precompressed via MapStaticAssets and never reach this middleware's
        // mime list. EnableForHttps is a deliberate BREACH acceptance -- rationale
        // and the boundaries of that acceptance live in docs/conventions.md.
        builder.Services.AddResponseCompression(static options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ["text/html"];
        });

        // R33: metrics + traces, off by default and enabled by flipping
        // __PRODUCT__:Telemetry:Enabled. OTLP endpoint/protocol come from the
        // standard OTEL_EXPORTER_OTLP_* env vars. Npgsql's ActivitySource and
        // Meter are built into the driver; subscribing by name is the whole
        // integration.
        if (string.Equals(builder.Configuration["__PRODUCT__:Telemetry:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddOpenTelemetry()
                .WithMetrics(static metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Npgsql")
                    .AddOtlpExporter())
                .WithTracing(static tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddSource("Npgsql")
                    .AddOtlpExporter());
        }

        // Antiforgery is the one place this app needs IDataProtection: without a
        // persisted key ring, every restart invalidates outstanding tokens (a bad form
        // submission after any deploy/recycle). The default lives under the user
        // profile so it survives a rebuild of the publish directory; override
        // __PRODUCT__:DataProtection:KeyDirectory to point at a persistent volume in
        // production.
        // Length check, not ??: appsettings documents the key with an empty
        // value, and DirectoryInfo("") throws at startup.
        string keyDirectory = builder.Configuration["__PRODUCT__:DataProtection:KeyDirectory"] is { Length: > 0 } configuredKeyDirectory
            ? configuredKeyDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".__product__", "keys");
        builder.Services.AddDataProtection()
            .SetApplicationName("__PRODUCT__")
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

        builder.Services.AddAntiforgery();

        // Bot-trap timing (R21). MinAge trips on sub-3s submits, so the test
        // tiers set __PRODUCT__:BotTraps:MinAgeSeconds=0 -- their submits are
        // machine-fast by construction.
        var botTrapOptions = new BotTrapOptions();
        if (builder.Configuration["__PRODUCT__:BotTraps:MinAgeSeconds"] is { Length: > 0 } minAgeRaw)
        {
            // Fail fast on garbage: a malformed or negative value would either
            // throw mid-request or silently disable the timing trap.
            if (!double.TryParse(minAgeRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double minAgeSeconds) || minAgeSeconds < 0)
            {
                throw new InvalidOperationException($"__PRODUCT__:BotTraps:MinAgeSeconds must be a non-negative number; got '{minAgeRaw}'.");
            }

            botTrapOptions.MinAge = TimeSpan.FromSeconds(minAgeSeconds);
        }
        builder.Services.AddSingleton(botTrapOptions);

        // R22: coarse fixed windows, all configurable so the test factory can lift
        // them out of the way. The skeleton has one surface and no identities to key
        // on, so "general" partitions by client IP; a product that grows a signed-in
        // surface adds its own user-keyed policies alongside it.
        static int Limit(WebApplicationBuilder b, string name, int fallback) =>
            int.TryParse(b.Configuration[$"__PRODUCT__:RateLimit:{name}:PermitLimit"], out int v) && v > 0 ? v : fallback;

        int generalPermitLimit = Limit(builder, "General", 120);
        // Spec 003 (R26): POST /csp-report. A browser hitting a genuinely broken page
        // fires a handful of reports; anything sustained is noise or abuse, and the
        // endpoint only feeds the log.
        int cspReportPermitLimit = Limit(builder, "CspReport", 10);

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Rate-limit partition key. An IPv6 client routinely holds a whole /64, so
            // keying on the full address hands one attacker 2^64 fresh fixed windows;
            // bucket v6 to its /64 prefix. IPv4 (and v4-mapped v6) stays per-address.
            static string ClientIpKey(HttpContext httpContext)
            {
                IPAddress? ip = httpContext.Connection.RemoteIpAddress;
                if (ip is null)
                {
                    return "unknown";
                }

                if (ip.IsIPv4MappedToIPv6)
                {
                    ip = ip.MapToIPv4();
                }

                if (ip.AddressFamily is not AddressFamily.InterNetworkV6)
                {
                    return ip.ToString();
                }

                Span<byte> bytes = stackalloc byte[16];
                ip.TryWriteBytes(bytes, out _);
                bytes[8..].Clear();
                return $"{new IPAddress(bytes)}/64";
            }

            static RateLimitPartition<string> ByIp(HttpContext httpContext, int permitLimit) =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientIpKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(60),
                        PermitLimit = permitLimit,
                        QueueLimit = 0,
                    });

            options.AddPolicy("general", httpContext => ByIp(httpContext, generalPermitLimit));
            options.AddPolicy("csp-report", httpContext => ByIp(httpContext, cspReportPermitLimit));
        });

        WebApplication app = builder.Build();

        // Must run before anything that reads RemoteIpAddress/scheme (rate limiter,
        // request logging) so those see the real client, not the proxy hop.
        app.UseForwardedHeaders();

        // Clean-URL rewrites for wwwroot/pages (spec 003 R38) must precede route
        // matching, and WebApplication only auto-inserts UseRouting at the front
        // of the pipeline when it is never called explicitly -- so routing is
        // pinned here, after the rewrite, and everything below stays between
        // routing and the terminal endpoint dispatch as before.
        app.UseMiddleware<StaticPagesMiddleware>();
        app.UseRouting();

        // Error rendering wraps everything below so it can catch exceptions and
        // fill bodiless non-2xx responses; security headers come next so error
        // pages carry the full header set (spec 003 R39).
        app.UseMiddleware<ErrorPageMiddleware>(StaticErrorShell.Load(app.Environment));
        app.UseMiddleware<SecurityHeadersMiddleware>();
        // Inside the headers middleware so compressed responses still carry the
        // full header set; only text/html is on the mime list. Static pages
        // (V8) are text/html and DO pass through here: in a published build
        // MapStaticAssets serves the precompressed variant (Content-Encoding
        // already set, so this skips), and where none exists (dev, tests)
        // they compress dynamically.
        app.UseResponseCompression();
        app.UseMiddleware<RequestLoggingMiddleware>();
        // Request locale: cookie -> Accept-Language -> en (spec 003 R16). A product
        // that grows sign-in and a stored user preference moves this after its
        // UseAuthentication call, so the hook can see the signed-in user.
        app.UseLocaleNegotiation(new LocaleNegotiationOptions { SupportedLocales = Locales.All });
        app.UseRateLimiter();
        app.UseRequestTimeouts();
        app.UseAntiforgery();

        app.MapStaticAssets();

        // Built once, right after MapStaticAssets -- endpoints are fully known here.
        var assets = new AssetUrls(app);
        IAntiforgery antiforgery = app.Services.GetRequiredService<IAntiforgery>();

        // The lambda's return type is stated because the RDG interceptor binds
        // before our template generator's output exists in its view of the
        // compilation; left inferred, the return type is unresolvable there.
        app.MapGet("/", IResult (HttpContext context) =>
        {
            var chrome = Chrome.For(context, assets, antiforgery);
            (string Ok, string Error) flash = Flash.Take(context);
            return Templates.Home.Index.Result(
                locale: chrome.Locale,
                Title: chrome.Locale["title.home"],
                Assets: chrome.Assets,
                Nonce: chrome.Nonce,
                AntiforgeryFieldName: chrome.AntiforgeryFieldName,
                AntiforgeryToken: chrome.AntiforgeryToken,
                BotCanary: chrome.BotCanary,
                FlashOk: flash.Ok,
                FlashError: flash.Error);
        }).RequireRateLimiting("general");

        app.MapCspReportEndpoint();
        app.MapSecurityTxtEndpoint();

        // Liveness: process is up. Readiness: the database answers.
        app.MapGet("/healthz", () => Results.Text("ok"));

        // Spec 005 R2: which build is this instance. InformationalVersion is
        // Version+shortsha, stamped by the csproj's StampSourceRevision target;
        // read once here — the assembly-level attribute survives NativeAOT.
        string informationalVersion = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        app.MapGet("/version", () => Results.Text(informationalVersion));
        app.MapGet("/readyz", async Task<IResult> (IServiceProvider services, CancellationToken ct) =>
        {
            NpgsqlDataSource? dataSource = services.GetService<NpgsqlDataSource>();

            if (dataSource is null)
                return Results.Text("not ready", "text/plain", statusCode: StatusCodes.Status503ServiceUnavailable);

            try
            {
                await using NpgsqlCommand cmd = dataSource.CreateCommand("SELECT 1");
                await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return Results.Text("ready");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return Results.Text("not ready", "text/plain", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        app.Run();
    }
}
