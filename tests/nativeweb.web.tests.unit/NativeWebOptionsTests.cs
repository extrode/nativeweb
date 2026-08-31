using System.Net;

using Microsoft.Extensions.Configuration;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// Spec 015 R4: the options are bound by hand, so the binding is code that can
/// be wrong and therefore code that gets tested. <c>Configure&lt;T&gt;</c> would
/// have made this file unnecessary and the library un-AOT-able; this is the
/// other side of that trade.
/// </summary>
public sealed class NativeWebOptionsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    private static NativeWebOptions Bind(params (string Key, string Value)[] entries)
    {
        Assert.True(
            NativeWebOptions.TryFromConfiguration(Config(entries), "Demo", out NativeWebOptions? options, out string? error),
            error);
        return options!;
    }

    [Fact]
    public void AnEmptyConfiguration_BindsAndBoots()
    {
        // Missing is the normal case, not a failure: a box with no connection
        // string still boots, reports not-ready and degrades to 503.
        NativeWebOptions options = Bind();

        Assert.Null(options.ConnectionString);
        Assert.Null(options.CommandTimeoutSeconds);
        Assert.Null(options.SecurityContact);
        Assert.Null(options.BotTrapMinAge);
        Assert.Empty(options.KnownProxies);
        Assert.False(options.TelemetryEnabled);
        Assert.Equal(20, options.MaxAutoPrepare);
        Assert.Equal(120, options.GeneralPermitLimit);
        Assert.Equal(10, options.CspReportPermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(1), options.ReadinessCacheWindow);
    }

    [Fact]
    public void TheApplicationNameScopesTheCookieAndTheServiceName()
    {
        NativeWebOptions options = Bind();

        // Two products on one host must not share a key ring or read each
        // other's antiforgery cookie.
        Assert.Equal("__Host-Demo-antiforgery", options.AntiforgeryCookieName);
        Assert.Equal("Demo", options.TelemetryServiceName);
        Assert.EndsWith(Path.Combine(".demo", "keys"), options.DataProtectionKeyDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryKeySitsUnderTheProductsSection()
    {
        NativeWebOptions options = Bind(
            ("Demo:Db:ConnectionString", "Host=localhost"),
            ("Demo:Db:CommandTimeoutSeconds", "45"),
            ("Demo:Db:MaxAutoPrepare", "0"),
            ("Demo:Telemetry:Enabled", "TRUE"),
            ("Demo:RateLimit:General:PermitLimit", "7"),
            ("Demo:RateLimit:CspReport:PermitLimit", "3"),
            ("Demo:Readiness:CacheSeconds", "2.5"),
            ("Demo:SecurityContact", "security@example.com"),
            ("Demo:BotTraps:MinAgeSeconds", "0"),
            ("Demo:DataProtection:KeyDirectory", "/keys"),
            ("Demo:ForwardedHeaders:KnownProxies", "10.0.0.1, 10.0.0.2"));

        Assert.Equal("Host=localhost", options.ConnectionString);
        Assert.Equal(45, options.CommandTimeoutSeconds);
        // Zero has to survive: behind a transaction-pooling PgBouncer,
        // auto-prepare must be switchable off without a code change, and a
        // "> 0" guard here would silently restore the default of 20.
        Assert.Equal(0, options.MaxAutoPrepare);
        Assert.True(options.TelemetryEnabled);
        Assert.Equal(7, options.GeneralPermitLimit);
        Assert.Equal(3, options.CspReportPermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(2.5), options.ReadinessCacheWindow);
        Assert.Equal("mailto:security@example.com", options.SecurityContact);
        Assert.Equal(TimeSpan.Zero, options.BotTrapMinAge);
        Assert.Equal("/keys", options.DataProtectionKeyDirectory);
        Assert.Equal([IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")], options.KnownProxies);
    }

    [Fact]
    public void TheSectionCanBeNamedSeparatelyFromTheApplication()
    {
        Assert.True(NativeWebOptions.TryFromConfiguration(
            Config(("Other:RateLimit:General:PermitLimit", "5")),
            "Demo",
            out NativeWebOptions? options,
            out _,
            section: "Other"));

        Assert.Equal(5, options!.GeneralPermitLimit);
        Assert.Equal("__Host-Demo-antiforgery", options.AntiforgeryCookieName);
    }

    [Fact]
    public void OtelServiceNameIsReadFromTheRootAndWins()
    {
        // The standard environment variable, which an operator running staging
        // and production off one binary reaches for first.
        NativeWebOptions options = Bind(("OTEL_SERVICE_NAME", "demo-staging"));

        Assert.Equal("demo-staging", options.TelemetryServiceName);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    [InlineData("3,5")]
    public void AMalformedBotTrapAgeIsAStartupError(string value)
    {
        Assert.False(NativeWebOptions.TryFromConfiguration(
            Config(("Demo:BotTraps:MinAgeSeconds", value)), "Demo", out NativeWebOptions? options, out string? error));

        Assert.Null(options);
        Assert.Contains("Demo:BotTraps:MinAgeSeconds", error, StringComparison.Ordinal);
        Assert.Contains(value, error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnusableSecurityContactIsAStartupError()
    {
        // Blank is fine and means "serve no security.txt". A value that cannot
        // become an RFC 9116 URI is the one that stops the app, rather than
        // shipping a file nobody can act on.
        Assert.False(NativeWebOptions.TryFromConfiguration(
            Config(("Demo:SecurityContact", "not a contact")), "Demo", out NativeWebOptions? options, out string? error));

        Assert.Null(options);
        Assert.Contains("Demo:SecurityContact", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverlongReadinessWindowIsClamped()
    {
        // Readiness is what a balancer uses to notice an outage. A fat-fingered
        // value must not let the app claim health for minutes after its
        // database has gone.
        NativeWebOptions options = Bind(("Demo:Readiness:CacheSeconds", "3600"));

        Assert.Equal(NativeWebOptions.MaxReadinessCacheWindow, options.ReadinessCacheWindow);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("many")]
    public void AnUnusablePermitLimitFallsBackRatherThanOpeningOrClosingThePolicy(string value)
    {
        NativeWebOptions options = Bind(("Demo:RateLimit:General:PermitLimit", value));

        Assert.Equal(120, options.GeneralPermitLimit);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("Enabled")]
    [InlineData("on")]
    public void AnUnparseableTelemetryFlagIsAStartupError(string value)
    {
        // Every one of these is something an operator writes meaning on, and
        // every one of them read as off under the equality check this replaced.
        // Telemetry an operator believes is exporting and is not is worse than a
        // named error at startup, because nothing downstream ever says so.
        Assert.False(NativeWebOptions.TryFromConfiguration(
            Config(("Demo:Telemetry:Enabled", value)), "Demo", out NativeWebOptions? options, out string? error));

        Assert.Null(options);
        Assert.Contains("Demo:Telemetry:Enabled", error, StringComparison.Ordinal);
        Assert.Contains(value, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    public void TheTelemetryFlagStillTakesTheValuesItAlwaysTook(string value, bool expected)
    {
        Assert.Equal(expected, Bind(("Demo:Telemetry:Enabled", value)).TelemetryEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankSectionIsAnArgumentFaultAndNotADefaultedApp(string section)
    {
        // Blank builds keys of the form ":Db:ConnectionString", which match
        // nothing: the app would boot with every value defaulted, no connection
        // string, and no symptom beyond reporting itself not-ready.
        Assert.Throws<ArgumentException>(() => NativeWebOptions.TryFromConfiguration(
            Config(("Demo:Db:ConnectionString", "Host=localhost")),
            "Demo",
            out _,
            out _,
            section));
    }

    [Fact]
    public void AnUnparseableProxyIsSkippedRatherThanTakingTheAppDown()
    {
        // Deliberately not a startup error, and stated as a decision rather
        // than left implicit: forwarded headers from an untrusted hop are
        // ignored, which is safe, so a typo costs a degraded rate-limit
        // partition and not an outage.
        NativeWebOptions options = Bind(("Demo:ForwardedHeaders:KnownProxies", "10.0.0.1, nonsense, 10.0.0.3"));

        Assert.Equal([IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.3")], options.KnownProxies);
    }

    [Fact]
    public void AConfiguredKeyDirectory_IsUsedVerbatim()
    {
        Assert.True(NativeWebOptions.TryResolveKeyDirectory("/var/lib/nativeweb/keys", "/home/dev", "Demo", _ => true, out string? directory));
        Assert.Equal("/var/lib/nativeweb/keys", directory);
    }

    [Fact]
    public void NoConfiguredKeyDirectory_FallsBackUnderTheHomeDirectory()
    {
        Assert.True(NativeWebOptions.TryResolveKeyDirectory(null, "/home/dev", "Demo", _ => true, out string? directory));
        Assert.Equal(Path.Combine("/home/dev", ".demo", "keys"), directory);
    }

    // An empty home is what a process with no home directory at all gets.
    // Path.Combine drops the empty segment, so the fallback used to yield a RELATIVE
    // path, which resolves under WorkingDirectory -- read-only under
    // ProtectSystem=strict. The key ring could not persist, every restart reminted
    // the antiforgery keys, and every form POST answered 400 after each deploy with
    // /readyz green.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoConfiguredKeyDirectory_AndNoHome_IsRefusedRatherThanResolvedRelative(string? configured)
    {
        Assert.False(NativeWebOptions.TryResolveKeyDirectory(configured, "", "Demo", _ => true, out string? directory));
        Assert.Null(directory);
    }

    // The sharper case, and the one the emptiness test above does not reach: a
    // system account records a home in passwd that was never created --
    // /nonexistent on Debian -- so the string is not empty and the guard has to
    // prove the directory rather than infer it from the path.
    [Fact]
    public void AHomeDirectoryNothingCanWrite_IsRefusedEvenThoughThePathIsNotEmpty()
    {
        Assert.False(NativeWebOptions.TryResolveKeyDirectory(null, "/nonexistent", "Demo", _ => false, out string? directory));
        Assert.Null(directory);
    }

    // A configured directory is never probed: it is an operator's statement and
    // deploy.sh preflights it, and probing would make binding options create a
    // directory as a side effect of reading configuration.
    [Fact]
    public void AConfiguredKeyDirectory_IsTakenWithoutProbing()
    {
        Assert.True(NativeWebOptions.TryResolveKeyDirectory(
            "/var/lib/nativeweb/keys",
            "",
            "Demo",
            _ => throw new InvalidOperationException("a configured directory must not be probed"),
            out string? directory));

        Assert.Equal("/var/lib/nativeweb/keys", directory);
    }

    // Through the entry point, not the helper: what the helper's false has to
    // produce is a startup refusal carrying the key's name, and nothing asserted
    // that wiring. Reverting TryFromConfiguration to the old inline Path.Combine
    // left every other test in this file green.
    [Fact]
    public void AnUnusableFallbackKeyDirectory_StopsStartupWithANamedError()
    {
        Assert.False(NativeWebOptions.TryFromConfiguration(
            Config(),
            "Demo",
            "/nonexistent",
            _ => false,
            out NativeWebOptions? options,
            out string? error));

        Assert.Null(options);
        Assert.NotNull(error);
        Assert.Contains("Demo:DataProtection:KeyDirectory", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfiguredKeyDirectory_IsBoundThroughTheEntryPoint()
    {
        Assert.True(NativeWebOptions.TryFromConfiguration(
            Config(("Demo:DataProtection:KeyDirectory", "/var/lib/nativeweb/keys")),
            "Demo",
            out NativeWebOptions? options,
            out string? error));

        Assert.Null(error);
        Assert.Equal("/var/lib/nativeweb/keys", options?.DataProtectionKeyDirectory);
    }
}
