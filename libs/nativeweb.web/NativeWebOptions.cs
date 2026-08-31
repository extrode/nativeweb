using System.Globalization;
using System.Net;

namespace NativeWeb.Web;

/// <summary>
/// Every setting the framework's pipeline reads, in one typed record.
/// </summary>
/// <remarks>
/// <para>
/// Bound by hand in <see cref="TryFromConfiguration"/>, one key at a time. Not
/// <c>Configure&lt;T&gt;</c> and not <c>IConfiguration.Get&lt;T&gt;</c>: both bind
/// by reflection over the target's properties, which NativeAOT cannot see and the
/// trim analyzer will not accept (spec 015 R4). Thirty lines of indexer reads is
/// the price, and it buys something as well as costing it — a malformed value has
/// a named error instead of a silent default.
/// </para>
/// <para>
/// The keys are the ones a stamped product's <c>Program.cs</c> read while the
/// pipeline was template text, unchanged, so an existing product's
/// <c>appsettings.json</c> keeps working. They sit under
/// <see cref="ConfigurationSection"/>, which defaults to the application name —
/// <c>Manzil:RateLimit:General:PermitLimit</c>, and so on.
/// </para>
/// </remarks>
public sealed record NativeWebOptions
{
    /// <summary>
    /// The product's PascalCase name. Names the antiforgery cookie, the
    /// data-protection application, the default telemetry service and the
    /// default key directory, so two products on one host cannot share a key
    /// ring or read each other's cookies.
    /// </summary>
    public required string ApplicationName { get; init; }

    /// <summary>The configuration section every key below sits under.</summary>
    public required string ConfigurationSection { get; init; }

    /// <summary>
    /// PostgreSQL connection string, or null. Null is a running state, not a
    /// failure: the app boots, <c>/readyz</c> reports not-ready and the
    /// DB-backed routes degrade to 503.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>Per-command timeout, or null to keep Npgsql's own 30s default.</summary>
    public int? CommandTimeoutSeconds { get; init; }

    /// <summary>
    /// Statements auto-prepared per connection. Must be settable to 0: behind a
    /// transaction-pooling PgBouncer, prepared statements break mid-flight, and
    /// that remediation cannot require a code change.
    /// </summary>
    public int MaxAutoPrepare { get; init; } = 20;

    /// <summary>
    /// Proxies whose <c>X-Forwarded-For</c> is trusted. Empty means only
    /// loopback is, which is safe (no spoofing through an untrusted hop) and
    /// degrades rate limiting to one shared partition behind a real proxy.
    /// </summary>
    public IReadOnlyList<IPAddress> KnownProxies { get; init; } = [];

    /// <summary>Whether metrics and traces are exported at all.</summary>
    public bool TelemetryEnabled { get; init; }

    /// <summary>
    /// The service name a collector sees. <c>OTEL_SERVICE_NAME</c> wins where it
    /// is set — an operator running staging and production off one binary
    /// reaches for it first, and silently overriding it would be worse than
    /// having no default.
    /// </summary>
    public required string TelemetryServiceName { get; init; }

    /// <summary>
    /// Where the data-protection key ring is persisted. Without one, every
    /// restart invalidates outstanding antiforgery tokens.
    /// </summary>
    public required string DataProtectionKeyDirectory { get; init; }

    /// <summary>
    /// How fast a form submission may arrive before the bot trap treats it as
    /// machine-driven, or null for the default. The test tiers set 0: their
    /// submits are machine-fast by construction.
    /// </summary>
    public TimeSpan? BotTrapMinAge { get; init; }

    /// <summary>Requests per minute per client IP on the general policy.</summary>
    public int GeneralPermitLimit { get; init; } = 120;

    /// <summary>Requests per minute per client IP on <c>POST /csp-report</c>.</summary>
    public int CspReportPermitLimit { get; init; } = 10;

    /// <summary>
    /// How long a readiness probe result is reused. Clamped to 30s on the way
    /// in: readiness is what a balancer uses to notice an outage, so a
    /// fat-fingered value must not let the app claim health for minutes after
    /// its database has gone.
    /// </summary>
    public TimeSpan ReadinessCacheWindow { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The RFC 9116 contact URI, already normalized, or null to serve no
    /// <c>security.txt</c>. Null is the honest answer when nobody has named a
    /// mailbox; a placeholder tells a researcher they are finished when they
    /// are not.
    /// </summary>
    public string? SecurityContact { get; init; }

    /// <summary>
    /// The Kestrel request-body ceiling. Every body this pipeline legitimately
    /// receives is an interactive form post, and Kestrel's 30 MB default is two
    /// orders of magnitude of free flood room.
    /// </summary>
    public long MaxRequestBodyBytes { get; init; } = 128 * 1024;

    /// <summary>The default request timeout. A handler that outlives it sees its token fire.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <c>__Host-</c> prefixed and product-scoped: host-locked, path <c>/</c>, no
    /// <c>Domain</c>, <c>Secure</c> enforced by the browser.
    /// </summary>
    public string AntiforgeryCookieName => $"__Host-{ApplicationName}-antiforgery";

    /// <summary>The largest readiness window that will be honoured.</summary>
    public static readonly TimeSpan MaxReadinessCacheWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Binds the options, or explains what is wrong with the configuration.
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing, and rather than exiting the process
    /// itself. An exception out of <c>Main</c> fail-fasts under NativeAOT and
    /// hangs the service on a modal error box on Windows (see
    /// <see cref="StartupConfigGuards.FailStartup"/>), and a binder that exits
    /// cannot be unit tested. <see cref="FromConfiguration"/> is the caller that
    /// turns a false here into that exit.
    /// </remarks>
    public static bool TryFromConfiguration(
        IConfiguration configuration,
        string applicationName,
        out NativeWebOptions? options,
        out string? error,
        string? section = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        options = null;
        error = null;

        string prefix = section ?? applicationName;
        string Key(string leaf) => $"{prefix}:{leaf}";

        // Timing trap. A malformed or negative value would either throw
        // mid-request or silently disable the trap, so it stops startup.
        TimeSpan? botTrapMinAge = null;
        if (configuration[Key("BotTraps:MinAgeSeconds")] is { Length: > 0 } minAgeRaw)
        {
            if (!double.TryParse(minAgeRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double minAgeSeconds)
                || minAgeSeconds < 0)
            {
                error = $"{Key("BotTraps:MinAgeSeconds")} must be a non-negative number; got '{minAgeRaw}'.";
                return false;
            }

            botTrapMinAge = TimeSpan.FromSeconds(minAgeSeconds);
        }

        // Blank is valid and serves no security.txt; supplied-but-unusable stops
        // the app rather than shipping a file nobody can act on.
        if (!SecurityTxtEndpoint.TryNormalize(configuration[Key(SecurityTxtEndpoint.ConfigurationKey)], out string? securityContact))
        {
            error = $"{Key(SecurityTxtEndpoint.ConfigurationKey)} must be a bare email address or a mailto:, "
                + "https: or tel: URI on a single line — for example security@example.com. Leave it "
                + "unset to serve no security.txt.";
            return false;
        }

        List<IPAddress> knownProxies = [];
        if (configuration[Key("ForwardedHeaders:KnownProxies")] is { Length: > 0 } proxies)
        {
            foreach (string ip in proxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IPAddress.TryParse(ip, out IPAddress? parsed))
                {
                    knownProxies.Add(parsed);
                }
            }
        }

        // Clamped rather than only validated as positive — see the property.
        var readiness = TimeSpan.FromSeconds(1);
        if (double.TryParse(configuration[Key("Readiness:CacheSeconds")], NumberStyles.Float, CultureInfo.InvariantCulture, out double readinessSeconds)
            && readinessSeconds > 0)
        {
            readiness = TimeSpan.FromSeconds(Math.Min(readinessSeconds, MaxReadinessCacheWindow.TotalSeconds));
        }

        options = new()
        {
            ApplicationName = applicationName,
            ConfigurationSection = prefix,
            ConnectionString = configuration[Key("Db:ConnectionString")] is { Length: > 0 } conn ? conn : null,
            CommandTimeoutSeconds = int.TryParse(configuration[Key("Db:CommandTimeoutSeconds")], out int commandTimeout) && commandTimeout > 0
                ? commandTimeout
                : null,
            MaxAutoPrepare = int.TryParse(configuration[Key("Db:MaxAutoPrepare")], out int maxAutoPrepare) && maxAutoPrepare >= 0
                ? maxAutoPrepare
                : 20,
            KnownProxies = knownProxies,
            TelemetryEnabled = string.Equals(configuration[Key("Telemetry:Enabled")], "true", StringComparison.OrdinalIgnoreCase),
            // Not under the product's section: OTEL_SERVICE_NAME is the standard
            // environment variable and lives at the configuration root.
            TelemetryServiceName = configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } serviceName
                ? serviceName
                : applicationName,
            // Length check, not ??: appsettings documents the key with an empty
            // value, and DirectoryInfo("") throws at startup.
            DataProtectionKeyDirectory = configuration[Key("DataProtection:KeyDirectory")] is { Length: > 0 } keyDirectory
                ? keyDirectory
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "." + applicationName.ToLowerInvariant(),
                    "keys"),
            BotTrapMinAge = botTrapMinAge,
            GeneralPermitLimit = PermitLimit(configuration, Key("RateLimit:General:PermitLimit"), 120),
            CspReportPermitLimit = PermitLimit(configuration, Key("RateLimit:CspReport:PermitLimit"), 10),
            ReadinessCacheWindow = readiness,
            SecurityContact = securityContact,
        };

        return true;
    }

    /// <summary>
    /// <see cref="TryFromConfiguration"/>, ending the process on a malformed
    /// value instead of returning false. The composition root's form.
    /// </summary>
    public static NativeWebOptions FromConfiguration(
        IConfiguration configuration, string applicationName, string? section = null)
    {
        if (!TryFromConfiguration(configuration, applicationName, out NativeWebOptions? options, out string? error, section))
        {
            StartupConfigGuards.FailStartup(error!);
        }

        return options!;
    }

    /// <summary>
    /// A permit limit must be positive to mean anything; 0 or a malformed value
    /// takes the default rather than opening the policy or closing it entirely.
    /// </summary>
    private static int PermitLimit(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out int value) && value > 0 ? value : fallback;
}
