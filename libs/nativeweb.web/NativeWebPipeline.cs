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
    /// <param name="builder">The host being configured.</param>
    /// <param name="options">Everything the eleven steps below read.</param>
    /// <param name="productAssembly">
    /// The product's own assembly, for the version telemetry reports. Pass
    /// <c>typeof(Program).Assembly</c>; null falls back to the entry assembly,
    /// which is the product everywhere except under a test host. See
    /// <see cref="InformationalVersion(Assembly?)"/>.
    /// </param>
    public static WebApplicationBuilder AddNativeWebWeb(
        this WebApplicationBuilder builder, NativeWebOptions options, Assembly? productAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        builder.Services.AddSingleton(options);

        builder.AddNativeWebStaticWebAssets();
        builder.AddNativeWebKestrel(options);
        builder.Services.AddNativeWebRequestTimeouts(options);
        builder.Services.AddNativeWebDataSource(options);
        builder.Services.AddNativeWebForwardedHeaders(options);
        builder.Services.AddNativeWebResponseCompression();
        builder.Services.AddNativeWebTelemetry(options, productAssembly);
        builder.Services.AddNativeWebDataProtection(options);
        builder.Services.AddNativeWebAntiforgery(options, builder.Environment);
        builder.Services.AddNativeWebBotTraps(options);
        builder.Services.AddNativeWebRateLimiting(options);
        builder.Services.AddNativeWebOutputCache();

        return builder;
    }

    /// <summary>
    /// The static web assets manifest, in Development only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CreateSlimBuilder</c> does not call this and <c>CreateBuilder</c> does,
    /// which is the whole of the bug it fixes. The framework's own browser assets
    /// live in <c>libs/nativeweb.web/wwwroot/_nativeweb/</c> and reach a product
    /// through <c>NativeWeb.Web.targets</c>; running from source, the product's
    /// <c>WebRootFileProvider</c> is its own <c>wwwroot</c> directory and nothing
    /// maps <c>/_nativeweb/</c> anywhere, so every framework stylesheet answered
    /// 500 under <c>dotnet run</c> while the published app served them from its
    /// own <c>wwwroot</c> and looked fine. An unstyled page in Development and a
    /// styled one in production is the worst shape a defect can take: the
    /// environment that is looked at least often is the one that is correct.
    /// </para>
    /// <para>
    /// Development only, and not because the call is expensive. The manifest is a
    /// build artefact beside the assembly, and a published app that found a stale
    /// one would serve files from whatever source tree built it.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host being configured.</param>
    public static WebApplicationBuilder AddNativeWebStaticWebAssets(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Environment.IsDevelopment())
        {
            builder.WebHost.UseStaticWebAssets();
        }

        return builder;
    }

    /// <summary>
    /// User secrets in Development, inserted <b>below</b> the environment
    /// variables rather than appended above them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Appending is what <c>AddUserSecrets</c> does by default, and it makes the
    /// store outrank a real environment variable — which is backwards, and was
    /// measured to bite: with a connection string exported, an integration
    /// fixture wrote its rows to the database the variable named while the app
    /// under test read the one in user secrets, and no failure said why.
    /// </para>
    /// <para>
    /// Development-only for the same class of reason: registered
    /// unconditionally, any production box that has ever run
    /// <c>dotnet user-secrets set</c> silently overrides its own deployment
    /// configuration.
    /// </para>
    /// </remarks>
    /// <typeparam name="TProgram">
    /// A type from the product's assembly — the entry-point class — which is how
    /// the user-secrets id is found.
    /// </typeparam>
    public static WebApplicationBuilder AddNativeWebUserSecrets<TProgram>(this WebApplicationBuilder builder)
        where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        int before = builder.Configuration.Sources.Count;
        builder.Configuration.AddUserSecrets<TProgram>(optional: true);
        StartupConfigGuards.DemoteBelowEnvironmentVariables(builder.Configuration.Sources, before);

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

        // A factory, not the built instance: the container disposes what it
        // creates and nothing else, so registering the object directly leaves the
        // pool undrained at shutdown and in-flight connections closed by the
        // socket rather than by Npgsql.
        string connectionString = connection.ConnectionString;
        return services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
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

            // Both are needed and they gate different things: KnownProxies below
            // decides which hops may be unwound, this caps how many are, and the
            // framework default of 1 stops on the outermost proxy of a two-hop
            // chain however well-trusted the rest of it is.
            forwarded.ForwardLimit = options.ForwardLimit;

            foreach (IPAddress proxy in options.KnownProxies)
            {
                forwarded.KnownProxies.Add(proxy);
            }
        });
    }

    /// <summary>
    /// Brotli for rendered HTML, registered but <b>not</b> used by
    /// <see cref="UseNativeWebPipeline"/>: a host with no compressing proxy in
    /// front of it adds <c>app.UseResponseCompression()</c> and this is the
    /// configuration that middleware picks up. Fingerprinted static assets ship
    /// precompressed through <c>MapStaticAssets</c> and never reach this mime
    /// list. <c>EnableForHttps</c> is a deliberate BREACH acceptance.
    /// </summary>
    /// <remarks>
    /// The paved deployment compresses at Caddy instead, which is 44% faster on
    /// the same page and offers zstd. The measurement is bottleneck 4 in
    /// <c>docs/02-reference/performance.md</c>; the directive is in
    /// <c>scripts/deploy/caddy/Caddyfile</c>.
    /// </remarks>
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
    /// <param name="services">The container being configured.</param>
    /// <param name="options">Service name and whether telemetry runs at all.</param>
    /// <param name="productAssembly">
    /// Where the reported service version is read from; null falls back to the
    /// entry assembly. See <see cref="InformationalVersion(Assembly?)"/> for why
    /// the entry assembly is not always the product's.
    /// </param>
    public static IServiceCollection AddNativeWebTelemetry(
        this IServiceCollection services, NativeWebOptions options, Assembly? productAssembly = null)
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
                serviceVersion: InformationalVersion(productAssembly)))
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
    /// <param name="services">The container being configured.</param>
    /// <param name="options">Where the product-scoped cookie name comes from.</param>
    /// <param name="environment">
    /// Decides the cookie's <c>Secure</c> policy, and nothing else.
    /// <see cref="CookieSecurePolicy.Always"/> everywhere except Development,
    /// where it is <see cref="CookieSecurePolicy.SameAsRequest"/>.
    /// <para>
    /// Not a knob, and not configuration: the environment is the one input that
    /// cannot be set to the weak value in production by editing a settings file.
    /// Always unconditionally is what this was until 2026-08-31, and it makes a
    /// plain-http host unusable rather than insecure —
    /// <c>GetAndStoreTokens</c> throws outright over http under that policy, so
    /// <c>dotnet run</c>, the Playwright tier and every local loopback host answer
    /// 500 on the first page that renders a form. Behind TLS, which is every
    /// deployment this framework ships (see scripts/deploy), the two policies are
    /// identical.
    /// </para>
    /// </param>
    public static IServiceCollection AddNativeWebAntiforgery(
        this IServiceCollection services, NativeWebOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        CookieSecurePolicy secure = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        return services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = "RequestVerificationToken";
            // The __Host- prefix and the Secure attribute travel together: a
            // browser rejects a __Host- cookie that is not Secure outright, so
            // keeping the prefix under SameAsRequest would not weaken the cookie,
            // it would drop it and turn every form post into a failed antiforgery
            // check. The prefix comes back with the policy.
            // Derived from the one name rather than composed a second way: the
            // Development cookie is the production cookie without its prefix, so
            // renaming the property renames both.
            antiforgery.Cookie.Name = secure == CookieSecurePolicy.Always
                ? options.AntiforgeryCookieName
                : options.AntiforgeryCookieName.Replace("__Host-", string.Empty, StringComparison.Ordinal);
            antiforgery.Cookie.SecurePolicy = secure;
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
    /// <param name="authentication">
    /// Whatever establishes <c>HttpContext.User</c> — typically
    /// <c>UseAuthentication()</c> and <c>UseAuthorization()</c> — or null for an
    /// app with no sign-in.
    /// <para>
    /// A parameter rather than something the caller appends afterwards, because
    /// the position is the whole point and it is not a position a caller can
    /// reach from outside. Antiforgery tokens are bound to the identity that
    /// minted them, and the validating middleware compares against
    /// <c>HttpContext.User</c> as it stands when it runs. Registered after this
    /// pipeline, authentication runs after that comparison, the user is
    /// anonymous, and every authenticated form post is a 400 — measured on
    /// 2026-08-31 against apps/sample: ten create/update/delete tests failed with
    /// BadRequest and no other symptom.
    /// </para>
    /// </param>
    public static WebApplication UseNativeWebPipeline(
        this WebApplication app,
        IReadOnlyList<string> supportedLocales,
        Action<IApplicationBuilder>? authentication = null)
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

        // No UseResponseCompression here, and that is a measured decision rather
        // than an omission (2026-08-31, A/B in docs/02-reference/performance.md).
        // Compressing in-process cost apps/sample 59% of its throughput on a
        // 3,271-byte page, and the level made no difference -- the cost is the
        // per-response codec setup, not the bytes. Through the proxy every
        // product is deployed behind, Caddy's `encode zstd gzip` served the same
        // page at 9,002 req/s against 6,255 for the app compressing it: the edge
        // is 44% faster and gets zstd, which .NET has no provider for.
        //
        // Two structural reasons on top of the number. Compressing here strips
        // the Content-Length that TemplateResults declares, which is worth 35% on
        // its own; and this step sits above UseOutputCache, so every cache hit
        // would be recompressed -- the cache never recovers the codec cost for
        // exactly the public traffic it exists to serve.
        //
        // A host with no compressing proxy in front of it adds the ASP.NET
        // response-compression middleware itself; AddNativeWebWeb still
        // registers the options, so that one line is the whole opt-in.
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

        // Before UseAntiforgery, and that is the reason this hook exists at all —
        // see the parameter's remarks.
        authentication?.Invoke(app);

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
    /// <param name="productAssembly">
    /// The assembly <c>/version</c> reports. Pass <c>typeof(Program).Assembly</c>;
    /// null falls back to the entry assembly.
    /// </param>
    public static WebApplication MapNativeWebEndpoints(
        this WebApplication app, NativeWebOptions options, Assembly? productAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);

        Assembly? version = productAssembly ?? Assembly.GetEntryAssembly();

        app.MapCspReportEndpoint();
        app.MapSecurityTxtEndpoint(options.SecurityContact);

        // Liveness: the process is up.
        app.MapGet("/healthz", () => Results.Text("ok"));
        app.MapGet("/version", () => Results.Text(InformationalVersion(version)));

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
    /// <param name="assembly">
    /// The product's assembly, or null to fall back to the entry assembly.
    /// </param>
    /// <remarks>
    /// Takeable rather than always <see cref="Assembly.GetEntryAssembly"/>,
    /// because under a test the entry assembly is never the product: a stamped
    /// product's integration tier reported <c>/version</c> as <c>18.7.0</c>,
    /// testhost's own version, and the regex asserting <c>Version+shortsha</c>
    /// was the only thing that noticed. Under xunit.v3 the answer changed
    /// without becoming right - each test project is its own executable now, so
    /// the entry assembly is the test assembly rather than a shared host.
    /// <para>
    /// A product passes <c>typeof(Program).Assembly</c> — a static reference the
    /// compiler resolves, which is why this is a parameter and not a lookup. It
    /// was <c>Assembly.Load(new AssemblyName(environment.ApplicationName))</c>
    /// until 2026-08-31. That form does work under NativeAOT (measured: the
    /// published binary answered <c>/version</c> with the stamped value, and the
    /// AOT analyzers `IsAotCompatible` turns on repo-wide flagged nothing),
    /// because the assembly is in the image and there is nothing to load. It
    /// went anyway: a name lookup that catches four exception types and falls
    /// back is a silent wrong answer waiting for a rename, and a parameter is
    /// the same fact stated where the compiler can check it.
    /// </para>
    /// </remarks>
    public static string InformationalVersion(Assembly? assembly = null) =>
        (assembly ?? Assembly.GetEntryAssembly())
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
