using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;

using NativeWeb.Render;
using NativeWeb.Security;

using Npgsql;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NativeWeb.Web;

/// <summary>
/// The two calls a NativeWeb application makes, and every step they perform.
/// </summary>
/// <remarks>
/// <para>
/// This was 489 lines of <c>Program.cs</c> in every stamped product until spec
/// 015. Nothing here is a product decision: the Kestrel body ceiling, the
/// request timeout, forwarded headers, the IPv6-/64 rate-limit partitioner,
/// data-protection key persistence, <c>__Host-</c> antiforgery hardening, the
/// OTel wiring, the readiness cache and the order of the eleven pipeline steps
/// are all framework knowledge that was being held as copied text.
/// </para>
/// <para>
/// <b>Every step is public and individually callable</b> (spec 015 R3). This
/// method hides nothing — a product that needs a different order writes its own
/// body out of the same pieces rather than forking the library. The house style
/// the scaffold already sets, applied here.
/// </para>
/// </remarks>
public static class NativeWebPipeline
{
    /// <summary>The named output-cache policy public routes attach with <c>AsPublicPage</c>.</summary>
    public const string PublicPagePolicy = PublicPageCachePolicy.Name;

    /// <summary>The rate-limit policy name for ordinary pages.</summary>
    public const string GeneralRateLimitPolicy = "general";

    /// <summary>The rate-limit policy name for <c>POST /csp-report</c>.</summary>
    public const string CspReportRateLimitPolicy = "csp-report";

    /// <summary>
    /// Everything the framework registers before <c>builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// Takes the builder rather than an <see cref="IServiceCollection"/> because
    /// two of the settings are Kestrel's and reach it through
    /// <see cref="WebApplicationBuilder.WebHost"/>. The plan sketched an
    /// <c>IServiceCollection</c> signature; the body-size limit is named in the
    /// spec's own problem statement as framework knowledge, and leaving it
    /// behind would have meant the product keeping the one line that bounds a
    /// flood.
    /// </remarks>
    public static WebApplicationBuilder AddNativeWebWeb(
        this WebApplicationBuilder builder, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        builder.Services.AddSingleton(options);

        builder.AddNativeWebKestrel(options);
        builder.Services.AddNativeWebRequestTimeouts(options);
        builder.Services.AddNativeWebDataSource(options);
        builder.Services.AddNativeWebForwardedHeaders(options);
        builder.Services.AddNativeWebResponseCompression();
        builder.Services.AddNativeWebTelemetry(options);
        builder.Services.AddNativeWebDataProtection(options);
        builder.Services.AddNativeWebAntiforgery(options);
        builder.Services.AddNativeWebBotTraps(options);
        builder.Services.AddNativeWebRateLimiting(options);
        builder.Services.AddNativeWebOutputCache();

        return builder;
    }

    /// <summary>
    /// Kestrel's https support and its request-body ceiling.
    /// <c>CreateSlimBuilder</c> ships without https; an
    /// <c>ASPNETCORE_URLS=https://…</c> binding needs it opted back in.
    /// </summary>
    public static WebApplicationBuilder AddNativeWebKestrel(
        this WebApplicationBuilder builder, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Limits.MaxRequestBodySize = options.MaxRequestBodyBytes);

        return builder;
    }

    /// <summary>No request runs forever; a handler that outlives the policy sees its token fire.</summary>
    public static IServiceCollection AddNativeWebRequestTimeouts(
        this IServiceCollection services, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        return services.AddRequestTimeouts(timeouts =>
            timeouts.DefaultPolicy = new RequestTimeoutPolicy { Timeout = options.RequestTimeout });
    }

    /// <summary>
    /// The shared, pooled <see cref="NpgsqlDataSource"/>, when a connection
    /// string is configured. Its absence is a running state and not a failure:
    /// nothing is registered, <c>/readyz</c> reports not-ready, and the
    /// DB-backed routes degrade to 503.
    /// </summary>
    public static IServiceCollection AddNativeWebDataSource(
        this IServiceCollection services, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return services;
        }

        NpgsqlConnectionStringBuilder connection = new(options.ConnectionString);
        if (options.CommandTimeoutSeconds is int commandTimeout)
        {
            connection.CommandTimeout = commandTimeout;
        }

        // The statements a NativeWeb app runs are a small fixed set
        // (JauntyQ-generated), so auto-preparing anything seen this often turns
        // nearly every query into a prepared execution with no explicit
        // Prepare() calls.
        connection.MaxAutoPrepare = options.MaxAutoPrepare;

        return services.AddSingleton(new NpgsqlDataSourceBuilder(connection.ConnectionString).Build());
    }

    /// <summary>
    /// Behind a proxy, <c>RemoteIpAddress</c> is the proxy's own socket peer
    /// unless <c>X-Forwarded-For</c> is applied — without which every client
    /// collapses into one rate-limit partition.
    /// </summary>
    public static IServiceCollection AddNativeWebForwardedHeaders(
        this IServiceCollection services, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        return services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (IPAddress proxy in options.KnownProxies)
            {
                forwarded.KnownProxies.Add(proxy);
            }
        });
    }

    /// <summary>
    /// Brotli for rendered HTML. Fingerprinted static assets ship precompressed
    /// through <c>MapStaticAssets</c> and never reach this mime list.
    /// <c>EnableForHttps</c> is a deliberate BREACH acceptance.
    /// </summary>
    public static IServiceCollection AddNativeWebResponseCompression(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddResponseCompression(static compression =>
        {
            compression.EnableForHttps = true;
            compression.MimeTypes = ["text/html"];
        });
    }

    /// <summary>
    /// Metrics and traces over OTLP, when enabled. Npgsql's ActivitySource and
    /// Meter are built into the driver; subscribing by name is the whole
    /// integration.
    /// </summary>
    public static IServiceCollection AddNativeWebTelemetry(
        this IServiceCollection services, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.TelemetryEnabled)
        {
            return services;
        }

        services.AddOpenTelemetry()
            // Without a service resource the collector sees
            // "unknown_service:<host process>", which makes two instances — or
            // this app beside any other — indistinguishable.
            .ConfigureResource(resource => resource.AddService(
                serviceName: options.TelemetryServiceName,
                serviceVersion: InformationalVersion()))
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
                // exists in 1.17.0 (verified in the assembly) and the matching
                // option is internal, so it cannot be pinned in code. "A token
                // in a query string never leaves this process" should not be
                // revocable by an environment variable on the host. Registered
                // before AddOtlpExporter so it runs before the batch processor
                // the exporter installs.
                .AddProcessor(new QueryRedactingProcessor())
                .AddOtlpExporter());

        return services;
    }

    /// <summary>
    /// A persisted key ring. Without one, every restart invalidates outstanding
    /// antiforgery tokens — a failed form submission after any deploy.
    /// </summary>
    public static IServiceCollection AddNativeWebDataProtection(
        this IServiceCollection services, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddDataProtection()
            .SetApplicationName(options.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeyDirectory));

        return services;
    }

    /// <summary>
    /// The CSRF cookie, hardened. <c>__Host-</c> (host-locked, path <c>/</c>, no
    /// <c>Domain</c>) and <c>Secure</c> pinned unconditionally rather than left
    /// at <c>SameAsRequest</c>: browsers treat <c>http://localhost</c> as a
    /// secure context, so an environment gate buys nothing and only creates a
    /// way to lose the flag.
    /// </summary>
    public static IServiceCollection AddNativeWebAntiforgery(
        this IServiceCollection services, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        return services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = "RequestVerificationToken";
            antiforgery.Cookie.Name = options.AntiforgeryCookieName;
            antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            antiforgery.Cookie.SameSite = SameSiteMode.Lax;
            antiforgery.Cookie.Path = "/";
        });
    }

    /// <summary>Bot-trap timing. The test tiers set the minimum age to 0.</summary>
    public static IServiceCollection AddNativeWebBotTraps(
        this IServiceCollection services, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        BotTrapOptions traps = new();
        if (options.BotTrapMinAge is TimeSpan minAge)
        {
            traps.MinAge = minAge;
        }

        return services.AddSingleton(traps);
    }

    /// <summary>
    /// Coarse fixed windows, partitioned by client IP. An IPv6 client routinely
    /// holds a whole /64, so keying on the full address hands one attacker 2^64
    /// fresh windows; v6 is bucketed to its /64 prefix and v4 (and v4-mapped v6)
    /// stays per-address.
    /// </summary>
    public static IServiceCollection AddNativeWebRateLimiting(
        this IServiceCollection services, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        int general = options.GeneralPermitLimit;
        int cspReport = options.CspReportPermitLimit;

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(GeneralRateLimitPolicy, http => ByIp(http, general));
            limiter.AddPolicy(CspReportRateLimitPolicy, http => ByIp(http, cspReport));
        });
    }

    /// <summary>
    /// The in-process store behind the public pages (spec 006). The named policy
    /// is the only one registered, so nothing is cached until a route says so
    /// with <c>AsPublicPage</c>.
    /// </summary>
    public static IServiceCollection AddNativeWebOutputCache(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddOutputCache(cache =>
            cache.AddPolicy(PublicPagePolicy, policy => policy.AddPolicy<PublicPageCachePolicy>()));
    }

    /// <summary>
    /// The ordered pipeline. <b>This order is the documented one and it is
    /// asserted by a test</b> (spec 015 R5), because a comment survives a move
    /// and an ordering does not.
    /// </summary>
    /// <param name="app">The application being composed.</param>
    /// <param name="supportedLocales">
    /// The product's catalog ids — <c>Locales.All</c>, which the render
    /// generator emits per product from its own <c>locales/*.json</c> and which
    /// a framework library therefore cannot name.
    /// </param>
    public static WebApplication UseNativeWebPipeline(
        this WebApplication app, IReadOnlyList<string> supportedLocales)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(supportedLocales);

        // Before anything that reads RemoteIpAddress or the scheme (the rate
        // limiter, request logging), so those see the real client.
        app.UseForwardedHeaders();

        // Error rendering wraps everything below so it can catch exceptions and
        // fill bodiless non-2xx responses; the security headers come next so
        // error pages carry the full set. Both sit ABOVE the rewrite and the
        // router, so a throw from either is answered with the error shell rather
        // than escaping to Kestrel as a bare 500. The headers survive being
        // registered this high because they are written from an OnStarting
        // callback, so the static-page marker set downstream is still readable
        // when the callback runs.
        app.UseMiddleware<ErrorPageMiddleware>(StaticErrorShell.Load(app.Environment));
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // Clean-URL rewrites must precede route matching, and WebApplication only
        // auto-inserts UseRouting at the front when it is never called
        // explicitly — so routing is pinned here, after the rewrite.
        app.UseMiddleware<StaticPagesMiddleware>();
        app.UseRouting();

        // Inside the headers middleware so compressed responses still carry the
        // full header set.
        app.UseResponseCompression();
        app.UseMiddleware<RequestLoggingMiddleware>();

        // Cookie -> Accept-Language -> en.
        app.UseLocaleNegotiation(new LocaleNegotiationOptions { SupportedLocales = supportedLocales });

        app.UseRateLimiter();
        // After the rate limiter deliberately (spec 006 R8): a cache hit must
        // still consume a permit, or the cache turns a public route's limit into
        // a no-op for exactly the traffic it exists to bound. After locale
        // negotiation because the cache key includes the negotiated locale.
        app.UseOutputCache();

        app.UseRequestTimeouts();
        app.UseAntiforgery();
        // Immediately after UseAntiforgery: that middleware records a failed
        // check on IAntiforgeryValidationFeature and continues rather than
        // rejecting, so without this the endpoint binds the form anyway and dies
        // as a 500.
        app.UseMiddleware<AntiforgeryValidationMiddleware>();

        return app;
    }

    /// <summary>
    /// The endpoints the framework owns: <c>/csp-report</c>, <c>security.txt</c>,
    /// <c>/healthz</c>, <c>/version</c> and <c>/readyz</c>.
    /// </summary>
    /// <param name="app">The application being composed.</param>
    /// <param name="options">Where the security contact and the readiness window come from.</param>
    public static WebApplication MapNativeWebEndpoints(this WebApplication app, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);

        app.MapCspReportEndpoint();
        app.MapSecurityTxtEndpoint(options.SecurityContact);

        // Liveness: the process is up.
        app.MapGet("/healthz", () => Results.Text("ok"));
        app.MapGet("/version", () => Results.Text(InformationalVersion()));

        app.MapNativeWebReadiness(options);

        return app;
    }

    /// <summary>
    /// <c>/readyz</c>: can this instance reach Postgres? 503 when the data source
    /// is unconfigured or unreachable, which is what gates load-balancer traffic.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT rate limited — a 429 here reads as unhealthy and would
    /// take a working instance out of rotation. The cache is what bounds the
    /// cost instead.
    /// </remarks>
    public static WebApplication MapNativeWebReadiness(this WebApplication app, NativeWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);

        // Built once, outside the handler: a cache has to outlive a request to
        // be a cache at all. The data source is resolved per probe rather than
        // captured, because it is absent until a connection string is configured
        // and "not ready" is the correct answer in that state.
        ReadinessCache readiness = new(
            options.ReadinessCacheWindow,
            async () =>
            {
                try
                {
                    // Resolved inside the try, not above it. GetService throws
                    // ObjectDisposedException once the host has begun shutting
                    // down, and an escaping exception here faults the task the
                    // whole burst is sharing — so every one of them would get a
                    // 500 where "not ready" is both true and the answer a
                    // balancer knows what to do with.
                    NpgsqlDataSource? dataSource = app.Services.GetService<NpgsqlDataSource>();
                    if (dataSource is null)
                    {
                        return false;
                    }

                    await using NpgsqlCommand command = dataSource.CreateCommand("SELECT 1");
                    await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                    return true;
                }
                catch (Exception)
                {
                    // Every failure is "not ready". There is deliberately no
                    // request token in scope: a probe shared by a burst must not
                    // observe one caller's lifetime. Npgsql's command timeout is
                    // what bounds this instead.
                    return false;
                }
            },
            // Monotonic, deliberately: a wall clock stepping backwards would
            // extend the window by the size of the step and keep reporting a
            // dead database as ready.
            () => TimeSpan.FromMilliseconds(Environment.TickCount64));

        app.MapGet("/readyz", async Task<IResult> () =>
            await readiness.IsReadyAsync().ConfigureAwait(false)
                ? Results.Text("ready")
                : Results.Text("not ready", "text/plain", statusCode: StatusCodes.Status503ServiceUnavailable));

        return app;
    }

    /// <summary>
    /// Which build this instance is. <c>InformationalVersion</c> is
    /// <c>Version+shortsha</c>, stamped by the csproj's <c>StampSourceRevision</c>
    /// target; the assembly-level attribute survives NativeAOT.
    /// </summary>
    public static string InformationalVersion() =>
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    /// <summary>
    /// The rate-limit partition key. IPv6 is bucketed to its /64; IPv4 and
    /// v4-mapped v6 stay per-address.
    /// </summary>
    public static string ClientIpKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPAddress? ip = context.Connection.RemoteIpAddress;
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

    private static RateLimitPartition<string> ByIp(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIpKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(60),
                PermitLimit = permitLimit,
                QueueLimit = 0,
            });
}
