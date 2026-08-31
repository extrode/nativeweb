using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Security.Tests.Unit;

/// <summary>
/// The four honeypots plus the signed render-timestamp canary (R21-R24),
/// exercised against a real <see cref="IDataProtectionProvider"/> and a
/// controllable clock rather than mocks.
/// </summary>
public sealed class BotTrapTests
{
    private static DefaultHttpContext CreateContext(TimeProvider clock, BotTrapOptions? options = null)
    {
        var services = new ServiceCollection();
        // Ephemeral so each context gets its own master key, never the
        // machine's persisted keyring — required for the "different
        // provider" test and keeps every test hermetic.
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        if (options is not null)
        {
            services.AddSingleton(options);
        }

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static Dictionary<string, StringValues> DefaultFields(string canary) => new()
    {
        [BotTraps.HiddenFilledName] = BotTraps.HiddenFilledValue,
        [BotTraps.HiddenEmptyName] = string.Empty,
        [BotTraps.OffscreenFilledName] = BotTraps.OffscreenFilledValue,
        [BotTraps.OffscreenEmptyName] = string.Empty,
        [BotTraps.CanaryName] = canary,
    };

    [Fact]
    public void BotTrapOptions_Defaults_AreThreeSecondsAndTwentyFourHours()
    {
        var options = new BotTrapOptions();

        Assert.Equal(TimeSpan.FromSeconds(3), options.MinAge);
        Assert.Equal(TimeSpan.FromHours(24), options.MaxAge);
    }

    [Fact]
    public void Inspect_AllFieldsAtDefaultsWithAgedCanary_IsClean()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);
        string canary = BotTraps.IssueCanary(context);
        clock.UtcNow += TimeSpan.FromSeconds(5);

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(DefaultFields(canary)));

        Assert.Equal(BotTrapVerdict.Clean, verdict);
        Assert.False(verdict.Tripped);
        Assert.Null(verdict.Reason);
    }

    [Theory]
    [InlineData(BotTraps.HiddenFilledName, "http://evil.example", true)]
    [InlineData(BotTraps.HiddenFilledName, "", true)]
    [InlineData(BotTraps.HiddenFilledName, null, false)]
    [InlineData(BotTraps.OffscreenFilledName, "http://evil.example", true)]
    [InlineData(BotTraps.OffscreenFilledName, "", true)]
    [InlineData(BotTraps.OffscreenFilledName, null, false)]
    [InlineData(BotTraps.HiddenEmptyName, "x", true)]
    [InlineData(BotTraps.OffscreenEmptyName, "x", true)]
    public void Inspect_HoneypotTampered_TripsWithFieldReason(string fieldName, string? value, bool includeField)
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);
        string canary = BotTraps.IssueCanary(context);
        clock.UtcNow += TimeSpan.FromSeconds(5);

        Dictionary<string, StringValues> fields = DefaultFields(canary);
        if (includeField)
        {
            fields[fieldName] = value!;
        }
        else
        {
            fields.Remove(fieldName);
        }

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(fields));

        Assert.True(verdict.Tripped);
        Assert.Equal($"honeypot:{fieldName}", verdict.Reason);
    }

    // A missing field is indistinguishable from an empty one to form[key].ToString(),
    // so the two "trips when filled" honeypots stay clean when the bot omits them
    // entirely — unlike the two pre-filled ones, which trip on any deviation from
    // their default, missing included.
    [Theory]
    [InlineData(BotTraps.HiddenEmptyName)]
    [InlineData(BotTraps.OffscreenEmptyName)]
    public void Inspect_EmptyHoneypotFieldMissing_IsIndistinguishableFromEmptyAndStaysClean(string fieldName)
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);
        string canary = BotTraps.IssueCanary(context);
        clock.UtcNow += TimeSpan.FromSeconds(5);

        Dictionary<string, StringValues> fields = DefaultFields(canary);
        fields.Remove(fieldName);

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(fields));

        Assert.Equal(BotTrapVerdict.Clean, verdict);
    }

    [Fact]
    public void Inspect_CanaryFieldMissing_TripsCanaryMissing()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);

        Dictionary<string, StringValues> fields = DefaultFields(string.Empty);
        fields.Remove(BotTraps.CanaryName);

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(fields));

        Assert.True(verdict.Tripped);
        Assert.Equal("canary:missing", verdict.Reason);
    }

    [Fact]
    public void Inspect_GarbageCanary_TripsInvalidOrExpired()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(DefaultFields("not-a-real-canary-token")));

        Assert.True(verdict.Tripped);
        Assert.Equal("canary:invalid-or-expired", verdict.Reason);
    }

    [Fact]
    public void Inspect_CanaryFromDifferentProtectionProvider_TripsInvalidOrExpired()
    {
        var issuingClock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext issuingContext = CreateContext(issuingClock);
        string foreignCanary = BotTraps.IssueCanary(issuingContext);

        var inspectingClock = new FixedTimeProvider { UtcNow = issuingClock.UtcNow + TimeSpan.FromSeconds(5) };
        HttpContext inspectingContext = CreateContext(inspectingClock);

        BotTrapVerdict verdict = BotTraps.Inspect(inspectingContext, new FormCollection(DefaultFields(foreignCanary)));

        Assert.True(verdict.Tripped);
        Assert.Equal("canary:invalid-or-expired", verdict.Reason);
    }

    [Fact]
    public void Inspect_CanarySubmittedBeforeMinAge_TripsTooFast()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);
        string canary = BotTraps.IssueCanary(context);
        clock.UtcNow += TimeSpan.FromSeconds(1);

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(DefaultFields(canary)));

        Assert.True(verdict.Tripped);
        Assert.Equal("canary:too-fast", verdict.Reason);
    }

    [Fact]
    public void Inspect_CustomMinAge_BoundaryIsExclusive()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var options = new BotTrapOptions { MinAge = TimeSpan.FromSeconds(10) };
        HttpContext context = CreateContext(clock, options);
        string canary = BotTraps.IssueCanary(context);

        clock.UtcNow += TimeSpan.FromMilliseconds(9_999);
        BotTrapVerdict tooSoon = BotTraps.Inspect(context, new FormCollection(DefaultFields(canary)));
        Assert.Equal("canary:too-fast", tooSoon.Reason);

        clock.UtcNow += TimeSpan.FromMilliseconds(1);
        BotTrapVerdict atBoundary = BotTraps.Inspect(context, new FormCollection(DefaultFields(canary)));
        Assert.Equal(BotTrapVerdict.Clean, atBoundary);
    }

    [Fact]
    public void Inspect_CanaryIssuedBeforeMaxAge_TripsInvalidOrExpired()
    {
        // The expiry baked into the token is issue-time + MaxAge, computed
        // from the injected clock; Unprotect checks it against the real
        // wall clock, so issuing far in the past guarantees it has passed.
        var clock = new FixedTimeProvider { UtcNow = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        HttpContext context = CreateContext(clock);
        string canary = BotTraps.IssueCanary(context);

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(DefaultFields(canary)));

        Assert.True(verdict.Tripped);
        Assert.Equal("canary:invalid-or-expired", verdict.Reason);
    }

    [Fact]
    public void Inspect_HoneypotAndCanaryBothFail_ReportsHoneypotReason()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);

        Dictionary<string, StringValues> fields = DefaultFields("garbage-canary");
        fields[BotTraps.HiddenFilledName] = "tampered";

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(fields));

        Assert.True(verdict.Tripped);
        Assert.Equal($"honeypot:{BotTraps.HiddenFilledName}", verdict.Reason);
    }

    [Fact]
    public void IssueCanary_NullContext_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => BotTraps.IssueCanary(null!));

    [Fact]
    public void Inspect_NullContext_ThrowsArgumentNullException()
    {
        var form = new FormCollection(new Dictionary<string, StringValues>());

        Assert.Throws<ArgumentNullException>(() => BotTraps.Inspect(null!, form));
    }

    [Fact]
    public void Inspect_NullForm_ThrowsArgumentNullException()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);

        Assert.Throws<ArgumentNullException>(() => BotTraps.Inspect(context, null!));
    }

    [Fact]
    public void Fields_ContainsAllFieldNamesAndOffscreenMarkup()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);

        string markup = BotTraps.Fields(context);

        Assert.Contains($"name=\"{BotTraps.HiddenFilledName}\"", markup);
        Assert.Contains($"name=\"{BotTraps.HiddenEmptyName}\"", markup);
        Assert.Contains($"name=\"{BotTraps.OffscreenFilledName}\"", markup);
        Assert.Contains($"name=\"{BotTraps.OffscreenEmptyName}\"", markup);
        Assert.Contains($"name=\"{BotTraps.CanaryName}\"", markup);
        Assert.Contains($"class=\"{BotTraps.OffscreenClass}\"", markup);
        Assert.Contains("aria-hidden=\"true\"", markup);
    }

    [Fact]
    public void Fields_CanaryValue_IsHtmlEncoded()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);

        string markup = BotTraps.Fields(context);

        Match match = Regex.Match(markup, $"name=\"{BotTraps.CanaryName}\" value=\"([^\"]*)\"");
        Assert.True(match.Success);
        string rawValue = match.Groups[1].Value;

        Assert.Equal(HtmlEncoder.Default.Encode(WebUtility.HtmlDecode(rawValue)), rawValue);
    }

    [Fact]
    public void Fields_MarkupRoundTrip_IsJudgedCleanAfterMinAgeElapses()
    {
        var clock = new FixedTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        HttpContext context = CreateContext(clock);

        string markup = BotTraps.Fields(context);

        var fields = new Dictionary<string, StringValues>();
        foreach (Match match in Regex.Matches(markup, "name=\"([^\"]+)\" value=\"([^\"]*)\""))
        {
            fields[match.Groups[1].Value] = WebUtility.HtmlDecode(match.Groups[2].Value);
        }

        Assert.Equal(5, fields.Count);

        clock.UtcNow += TimeSpan.FromSeconds(5);

        BotTrapVerdict verdict = BotTraps.Inspect(context, new FormCollection(fields));

        Assert.Equal(BotTrapVerdict.Clean, verdict);
    }

    [Fact]
    public void LogTripped_TrippedVerdict_WritesOneRecordWithReasonAndPath()
    {
        var provider = new CapturingLoggerProvider();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        ILogger logger = factory.CreateLogger("BotTraps");

        BotTraps.LogTripped(logger, BotTrapVerdict.Trip("honeypot:contact_website"), "/signup");

        string message = Assert.Single(provider.Messages);
        Assert.Contains("honeypot:contact_website", message);
        Assert.Contains("/signup", message);
    }

    [Fact]
    public void LogTripped_CleanVerdict_WritesNothing()
    {
        var provider = new CapturingLoggerProvider();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        ILogger logger = factory.CreateLogger("BotTraps");

        BotTraps.LogTripped(logger, BotTrapVerdict.Clean, "/signup");

        Assert.Empty(provider.Messages);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<string> _messages;

        public CapturingLogger(List<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _messages.Add(formatter(state, exception));
    }
}
