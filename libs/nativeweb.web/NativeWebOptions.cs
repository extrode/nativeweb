using System.Diagnostics.CodeAnalysis;
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
/// <c>MyApp:RateLimit:General:PermitLimit</c>, and so on.
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

    /// <summary>
    /// How many <c>X-Forwarded-For</c> entries may be unwound, which is the
    /// length of the proxy chain in front of the app. Defaults to ASP.NET Core's
    /// own 1.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="KnownProxies"/>, and both are needed: the trust
    /// check gates <i>which</i> hops may be unwound and this caps <i>how many</i>,
    /// so a chain longer than the cap stops early however well-trusted its hops
    /// are. Two hops is what <c>provision.sh --edge</c> builds — an edge proxy
    /// terminating TLS in front of the local Caddy that owns the blue/green swap
    /// — and at 1 the walk stops on the edge proxy's own address, which becomes
    /// every request's client IP: one rate-limit partition for the whole world,
    /// and no client attribution in the logs, with nothing failing to say so.
    /// <para>
    /// Raising it opens no spoofing surface. Every step is still gated by the
    /// trust check, so the walk stops at the first address that is not a known
    /// proxy whatever this is set to.
    /// </para>
    /// </remarks>
    public int ForwardLimit { get; init; } = 1;

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
    /// Where an uploaded file is stored. Defaults to an <c>uploads</c> directory
    /// beside the data-protection key ring, so a host that configures neither gets
    /// both under the same product directory and a host that configures the key
    /// ring gets uploads next to it without saying so twice.
    /// </summary>
    /// <remarks>
    /// Resolving the path is all the binder does. Creating it, and refusing to start
    /// when it cannot be written, is
    /// <see cref="TryPrepareUploadDirectory(string, out string?)"/>, which a host
    /// serving uploads calls once at startup -- for the reason
    /// <see cref="TryResolveKeyDirectory"/> gives for never probing a configured
    /// directory: binding configuration must not create directories as a side effect.
    /// </remarks>
    public required string UploadDirectory { get; init; }

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
    // `home` and `canPersist` are parameters rather than read inside, because
    // neither branch that matters can be produced on a developer's machine and an
    // untestable guard against a silent failure is two silent failures.
    //
    // Emptiness is not the test the host needs, which is what the first version of
    // this guard got wrong. A system account with no home directory still records
    // one in passwd -- /nonexistent, or a /home path that was never created -- so
    // UserProfile comes back non-empty, the guard did not fire, and the key ring
    // resolved under a directory nothing can write. Persistence has to be probed,
    // not inferred from the string.
    internal static bool TryResolveKeyDirectory(
        string? configured,
        string home,
        string applicationName,
        Func<string, bool> canPersist,
        [NotNullWhen(true)] out string? directory)
    {
        ArgumentNullException.ThrowIfNull(canPersist);

        // A configured directory is taken as given and never probed. It is an
        // operator's explicit statement, deploy.sh's preflight already checks it
        // sits inside ReadWritePaths, and probing it would mean every process that
        // binds these options creates a directory as a side effect of reading
        // configuration. The fallback is the one nobody chose, so it is the one
        // that has to be proved.
        if (configured is { Length: > 0 })
        {
            directory = configured;
            return true;
        }

        if (home.Length == 0)
        {
            directory = null;
            return false;
        }

        // The home directory itself is what gets probed, and it has to already
        // exist. A system account's passwd entry names a home that was never
        // created -- /nonexistent on Debian -- and a process with enough privilege
        // simply creates it, so a probe that CREATES the candidate returns true for
        // the very account this guard was written for and puts the key ring on an
        // ephemeral path nothing provisions or backs up. Existing-and-writable is
        // the question; the key ring makes its own subdirectory afterwards.
        if (!canPersist(home))
        {
            directory = null;
            return false;
        }

        directory = Path.Combine(home, "." + applicationName.ToLowerInvariant(), "keys");
        return true;
    }

    // Existence and writability, and nothing created that outlives the call. An
    // earlier version created the candidate directory to prove the parent allowed
    // it; that answered a different question -- a root process creates
    // /nonexistent happily -- and it raced when four test hosts bound options at
    // once, one deleting the directory while another wrote its probe into it. The
    // probe file's name is random for the same reason.
    internal static bool CanPersistKeys(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        string probe = Path.Combine(directory, $".nativeweb-probe-{Path.GetRandomFileName()}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    // Beside the key ring rather than inside it: the key ring's own files are the
    // only things that belong in that directory, and a host pointing
    // DataProtection:KeyDirectory at a mount it backs up would otherwise back up
    // every upload with it.
    internal static string ResolveUploadDirectory(string? configured, string keyDirectory)
    {
        if (configured is { Length: > 0 })
        {
            return configured;
        }

        string? parent = Path.GetDirectoryName(keyDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return parent is { Length: > 0 }
            ? Path.Combine(parent, "uploads")
            : Path.Combine(keyDirectory, "uploads");
    }

    /// <summary>
    /// Creates the upload directory when it is absent and proves it can be written.
    /// </summary>
    /// <remarks>
    /// Called once at startup by a host that serves uploads, so an unwritable
    /// directory is a refusal to start rather than a 500 on the first upload of the
    /// release. Absent is created because an upload directory holds nothing that
    /// outlives a deploy the way a key ring does, so there is no state to lose by
    /// making it; unwritable is fatal because nothing later can recover from it.
    /// </remarks>
    /// <param name="directory">The directory, from <see cref="UploadDirectory"/>.</param>
    /// <param name="error">What is wrong, when this returns false.</param>
    /// <returns>Whether the directory exists and can be written.</returns>
    public static bool TryPrepareUploadDirectory(string directory, [NotNullWhen(false)] out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            error = $"Uploads:Directory '{directory}' could not be created: {ex.Message}";
            return false;
        }

        if (!CanPersistKeys(directory))
        {
            error = $"Uploads:Directory '{directory}' exists but this process cannot write to it. "
                + "Every upload would fail after the deploy that made it read-only, with nothing reporting it.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryFromConfiguration(
        IConfiguration configuration,
        string applicationName,
        out NativeWebOptions? options,
        out string? error,
        string? section = null) =>
        TryFromConfiguration(
            configuration,
            applicationName,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            CanPersistKeys,
            out options,
            out error,
            section);

    // The environment is read by the public overload above and passed in here, so
    // the branch where the fallback key directory is unusable is reachable from a
    // test. It is the branch that matters -- it is what a system account gets --
    // and it cannot be produced on a developer's machine.
    internal static bool TryFromConfiguration(
        IConfiguration configuration,
        string applicationName,
        string home,
        Func<string, bool> canPersist,
        out NativeWebOptions? options,
        out string? error,
        string? section = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        // An argument fault, not a configuration one, so it throws rather than
        // returning an error: a blank section builds keys of the form
        // ":Db:ConnectionString", which match nothing, and the app boots with
        // every value defaulted and no connection string while reporting itself
        // only "not ready".
        if (section is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(section);
        }

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
                // Skipped, not fatal — a recorded decision, asserted by
                // AnUnparseableProxyIsSkippedRatherThanTakingTheAppDown: headers
                // from an untrusted hop are ignored, which is safe, so a typo
                // costs a degraded rate-limit partition and not an outage.
                if (IPAddress.TryParse(ip, out IPAddress? parsed))
                {
                    knownProxies.Add(parsed);
                }
            }
        }

        // Named error rather than a silent fallback to 1, unlike the proxy list
        // above: a skipped proxy costs a degraded rate-limit partition, and so
        // does a limit that quietly stays at 1, but here the operator wrote a
        // value and would have no way to learn it was ignored.
        int forwardLimit = 1;
        if (configuration[Key("ForwardedHeaders:ForwardLimit")] is { Length: > 0 } forwardLimitRaw
            && (!int.TryParse(forwardLimitRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out forwardLimit)
                || forwardLimit < 1))
        {
            error = $"{Key("ForwardedHeaders:ForwardLimit")} must be a positive whole number — the "
                + $"number of proxy hops in front of this app; got '{forwardLimitRaw}'.";
            return false;
        }

        // A named error rather than a silent false. "1", "yes" and "Enabled" are
        // all things an operator writes meaning on, and every one of them read as
        // off under an equality check against "true" — telemetry that an operator
        // believes is exporting and is not.
        bool telemetryEnabled = false;
        if (configuration[Key("Telemetry:Enabled")] is { Length: > 0 } telemetryRaw
            && !bool.TryParse(telemetryRaw, out telemetryEnabled))
        {
            error = $"{Key("Telemetry:Enabled")} must be true or false; got '{telemetryRaw}'.";
            return false;
        }

        // The dev default is a directory under the user's home, which is right for
        // every environment that has one. A systemd unit whose user has no home
        // directory does not: the path resolves somewhere nothing can write —
        // /nonexistent, or the release directory that ProtectSystem=strict makes
        // read-only. Persistence then fails, every restart remints the antiforgery
        // keys, and every form POST answers 400 while /readyz stays green.
        // Supplied-but-unusable stops the app here for the same reason the security
        // contact does, three checks up.
        if (!TryResolveKeyDirectory(
            configuration[Key("DataProtection:KeyDirectory")],
            home,
            applicationName,
            canPersist,
            out string? keyDirectory))
        {
            error = $"{Key("DataProtection:KeyDirectory")} names no directory this process can write, and "
                + "there is no usable home directory to fall back to, so the data protection key ring would "
                + "not survive a restart. Every form POST would answer 400 after each deploy with nothing "
                + "reporting it. Set it to a writable directory that outlives a release "
                + "— /var/lib/nativeweb/keys on a host provisioned by scripts/deploy/provision.sh.";
            return false;
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
            ForwardLimit = forwardLimit,
            TelemetryEnabled = telemetryEnabled,
            // Not under the product's section: OTEL_SERVICE_NAME is the standard
            // environment variable and lives at the configuration root.
            TelemetryServiceName = configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } serviceName
                ? serviceName
                : applicationName,
            // Length check, not ??: appsettings documents the key with an empty
            // value, and DirectoryInfo("") throws at startup.
            DataProtectionKeyDirectory = keyDirectory,
            UploadDirectory = ResolveUploadDirectory(configuration[Key("Uploads:Directory")], keyDirectory),
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
