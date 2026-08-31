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
}
