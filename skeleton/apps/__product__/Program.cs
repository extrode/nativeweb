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
using OpenTelemetry.Resources;
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
        //
        // Development only, and inserted BELOW the environment-variables source
        // rather than appended above it. Appending is what AddUserSecrets does by
        // default, and it makes the store outrank a real environment variable --
        // which is backwards, and was measured to bite in the framework repo: with
        // the connection string exported, the integration tier's fixture wrote its
        // rows to the database the variable named while the app under test read the
        // one in user-secrets, and no failure said why. Unconditional registration
        // also means any production box that has ever run `dotnet user-secrets set`
        // silently overrides its own deployment configuration.
        if (builder.Environment.IsDevelopment())
        {
            int before = builder.Configuration.Sources.Count;
            builder.Configuration.AddUserSecrets<Program>(optional: true);
            StartupConfigGuards.DemoteBelowEnvironmentVariables(builder.Configuration.Sources, before);
        }

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

        // Spec 005 R2: which build is this instance. InformationalVersion is
        // Version+shortsha, stamped by the csproj's StampSourceRevision target;
        // read once here — the assembly-level attribute survives NativeAOT. Read
        // before the telemetry block because the service resource carries it.
        string informationalVersion = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        // R33: metrics + traces, off by default and enabled by flipping
        // __PRODUCT__:Telemetry:Enabled. OTLP endpoint/protocol come from the
        // standard OTEL_EXPORTER_OTLP_* env vars. Npgsql's ActivitySource and
        // Meter are built into the driver; subscribing by name is the whole
        // integration.
        if (string.Equals(builder.Configuration["__PRODUCT__:Telemetry:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            // Without a service resource the collector sees
            // "unknown_service:<host process>", which makes two instances -- or
            // this app beside any other -- indistinguishable. Measured on the
            // framework app before the change. OTEL_SERVICE_NAME is honoured
            // rather than overridden: an operator running staging and production
            // off one binary reaches for it first, and silently losing it would
            // be worse than having no default at all.
            string serviceName = builder.Configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } configuredServiceName
                ? configuredServiceName
                : "__PRODUCT__";

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(
                    serviceName: serviceName,
                    serviceVersion: informationalVersion))
                .WithMetrics(static metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Npgsql")
                    .AddOtlpExporter())
                .WithTracing(static tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddSource("Npgsql")
                    // Ours, in front of the exporter, because the package's own
                    // redaction is switchable from outside the app:
                    // OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION
                    // exists in 1.17.0 (verified in the assembly), and the
                    // matching option is internal so it cannot be pinned in
                    // code. "A token in a query string never leaves this
                    // process" should not be revocable by an environment
                    // variable on the host, nor by a package bump. Registered
                    // before AddOtlpExporter so it runs before the batch
                    // processor the exporter installs.
                    .AddProcessor(new QueryRedactingProcessor())
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

        builder.Services.AddAntiforgery(static options =>
        {
            options.HeaderName = "RequestVerificationToken";

            // The CSRF cookie gets the same treatment as every other cookie this
            // app sets: __Host- (host-locked, path /, no Domain, Secure enforced by
            // the browser) and Secure pinned unconditionally rather than left at
            // SameAsRequest. Unconditional because browsers treat http://localhost
            // as a secure context, so an environment gate buys nothing and only
            // creates a way to lose the flag.
            //
            // This was bare AddAntiforgery() until 2026-08-30. On framework
            // defaults the cookie is named .AspNetCore.Antiforgery.<hash>, carries
            // no __Host- prefix -- so a sibling subdomain can plant it -- and rides
            // plain http. The framework app fixed this in its 2026-08-19 audit and
            // the template never took the fix, which meant every product stamped
            // from it inherited the defaults.
            options.Cookie.Name = "__Host-__PRODUCT__-antiforgery";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
        });

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
                // Not `throw`: an exception out of Main fail-fasts under
                // NativeAOT and hangs the service on a modal error box on
                // Windows. See StartupConfigGuards.FailStartup.
                StartupConfigGuards.FailStartup($"__PRODUCT__:BotTraps:MinAgeSeconds must be a non-negative number; got '{minAgeRaw}'.");
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

        // Spec 006: the in-process store behind the public pages. The named
        // policy is the only one attached anywhere -- nothing is cached by
        // default, so a route becomes shareable only by saying so.
        builder.Services.AddOutputCache(options =>
            options.AddPolicy(PublicPageCachePolicy.Name, policy => policy.AddPolicy<PublicPageCachePolicy>()));

        WebApplication app = builder.Build();

        // Must run before anything that reads RemoteIpAddress/scheme (rate limiter,
        // request logging) so those see the real client, not the proxy hop.
        app.UseForwardedHeaders();

        // Error rendering wraps everything below so it can catch exceptions and
        // fill bodiless non-2xx responses; security headers come next so error
        // pages carry the full header set (spec 003 R39). Both sit ABOVE the
        // rewrite and the router, so a throw from either is answered with the
        // error shell and the security headers rather than escaping to Kestrel as
        // a bare 500 (2026-08-19 audit). The headers survive being registered this
        // high because they are written from an OnStarting callback rather than on
        // the way in, so the static-page marker set downstream is still readable
        // when the callback runs.
        app.UseMiddleware<ErrorPageMiddleware>(StaticErrorShell.Load(app.Environment));
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // Clean-URL rewrites for wwwroot/pages (spec 003 R38) must precede route
        // matching, and WebApplication only auto-inserts UseRouting at the front
        // of the pipeline when it is never called explicitly -- so routing is
        // pinned here, after the rewrite, and everything below stays between
        // routing and the terminal endpoint dispatch as before.
        app.UseMiddleware<StaticPagesMiddleware>();
        app.UseRouting();
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
        // After the rate limiter deliberately (spec 006 R8): a cache hit must
        // still consume a rate-limit permit, or the cache would turn a public
        // route's limit into a no-op for exactly the traffic it exists to bound.
        // After locale negotiation because the cache key includes the negotiated
        // locale.
        app.UseOutputCache();
        app.UseRequestTimeouts();
        app.UseAntiforgery();
        // Must sit immediately after UseAntiforgery: that middleware records a
        // failed check on IAntiforgeryValidationFeature and continues rather than
        // rejecting, so without this the endpoint binds the form anyway and dies
        // as a 500. See AntiforgeryValidationMiddleware.
        app.UseMiddleware<AntiforgeryValidationMiddleware>();

        app.MapStaticAssets();

        // Built once, right after MapStaticAssets -- endpoints are fully known here.
        var assets = new AssetUrls(app);
        IAntiforgery antiforgery = app.Services.GetRequiredService<IAntiforgery>();

        // The lambda's return type is stated because the RDG interceptor binds
        // before our template generator's output exists in its view of the
        // compilation; left inferred, the return type is unresolvable there.
        // Built once from the template's own generated hashes (spec 006), so the
        // CSP cannot drift from the scripts the page actually renders.
        string homeCsp = PublicPageCache.Csp(Templates.Home.Index.InlineScriptCspHashes);

        app.MapGet("/", IResult (HttpContext context) =>
        {
            // Cookie-free requests get the byte-stable anonymous variant and are
            // marked cacheable; a flash (or, in a product with accounts, a
            // session) renders today's page unchanged.
            Chrome chrome = PublicPageCache.CarriesIdentityCookie(context.Request)
                ? Chrome.For(context, assets, antiforgery)
                : Chrome.Anonymous(context, assets);
            // Every full-page render site takes the flash (read-once); never
            // take it in a fragment path, which would consume it invisibly.
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
        }).RequireRateLimiting("general").AsPublicPage(homeCsp);

        app.MapCspReportEndpoint();

        // Where a vulnerability report should go. Blank is valid and serves no
        // security.txt, which is the honest answer when nobody has named a
        // mailbox; a supplied-but-unusable value stops the app here rather than
        // shipping a file nobody can act on.
        if (!SecurityTxtEndpoint.TryNormalize(
                builder.Configuration[SecurityTxtEndpoint.ConfigurationKey],
                out string? securityContact))
        {
            // Same reason as the BotTraps guard above: report and exit, do not
            // throw out of Main. See StartupConfigGuards.FailStartup.
            StartupConfigGuards.FailStartup(
                $"{SecurityTxtEndpoint.ConfigurationKey} must be a bare email address or a mailto:, "
                + "https: or tel: URI on a single line -- for example security@example.com. Leave it "
                + "unset to serve no security.txt.");
        }

        app.MapSecurityTxtEndpoint(securityContact);

        // Liveness: process is up. Readiness: the database answers.
        app.MapGet("/healthz", () => Results.Text("ok"));

        // Read where the telemetry block is, above.
        app.MapGet("/version", () => Results.Text(informationalVersion));

        // /readyz is bounded by a cache rather than a rate limit -- see
        // ReadinessCache for why a 429 on a readiness probe is the wrong tool.
        // Configurable so a deployment that wants a tighter outage-detection
        // window can pay for it in database round trips.
        // Clamped, not merely validated as positive: readiness is what a balancer
        // uses to notice an outage, so a fat-fingered value must not be able to
        // make the app claim health for minutes after its database has gone.
        double readinessCacheSeconds = double.TryParse(builder.Configuration["__PRODUCT__:Readiness:CacheSeconds"], NumberStyles.Float, CultureInfo.InvariantCulture, out double readinessSeconds) && readinessSeconds > 0
            ? Math.Min(readinessSeconds, 30.0)
            : 1.0;

        // Built once, outside the handler: the cache has to outlive a request to
        // be a cache at all. The data source is resolved per probe rather than
        // captured, because it is absent until a connection string is configured
        // and "not ready" is the correct answer in that state (see the builder
        // block above), not a startup failure.
        var readiness = new ReadinessCache(
            TimeSpan.FromSeconds(readinessCacheSeconds),
            async () =>
            {
                try
                {
                    // Resolve inside the try, not above it. GetService throws
                    // ObjectDisposedException once the host has begun shutting
                    // down, and an escaping exception here faults the task the
                    // whole burst is sharing -- so every one of them would get a
                    // 500 from /readyz where "not ready" is both true and the
                    // answer a balancer knows what to do with.
                    NpgsqlDataSource? dataSource = app.Services.GetService<NpgsqlDataSource>();
                    if (dataSource is null)
                    {
                        return false;
                    }

                    await using NpgsqlCommand cmd = dataSource.CreateCommand("SELECT 1");
                    await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                    return true;
                }
                catch (Exception)
                {
                    // Every failure is "not ready". There is deliberately no
                    // request token in scope here (see ReadinessCache): a probe
                    // shared by a burst must not observe one caller's lifetime.
                    // Npgsql's command timeout is what bounds this instead.
                    return false;
                }
            },
            // Monotonic, deliberately: a wall clock stepping backwards would
            // extend the window by the size of the step and keep reporting a
            // dead database as ready. See ReadinessCache's clock param.
            () => TimeSpan.FromMilliseconds(Environment.TickCount64));

        // Readiness: can we reach Postgres? 503 "not ready" when the data source is
        // unconfigured (no connection string yet) or unreachable; gates load-balancer
        // traffic. Deliberately NOT rate limited -- a 429 here reads as unhealthy and
        // would take a working instance out of rotation; the cache is what bounds the
        // cost instead.
        app.MapGet("/readyz", async Task<IResult> () =>
            await readiness.IsReadyAsync().ConfigureAwait(false)
                ? Results.Text("ready")
                : Results.Text("not ready", "text/plain", statusCode: StatusCodes.Status503ServiceUnavailable));

        app.Run();
    }
}
