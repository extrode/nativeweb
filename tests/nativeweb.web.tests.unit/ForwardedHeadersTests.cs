using System.Net;

using Microsoft.AspNetCore.TestHost;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// What <c>RemoteIpAddress</c> becomes behind a proxy chain, which is the value
/// the rate limiter partitions on and the request log records.
///
/// Asserted through the real middleware rather than by reading back
/// <c>ForwardedHeadersOptions</c>, because the defect this covers was a
/// registration that set every field an inspection would have looked at and left
/// the one that decides the answer. <c>KnownProxies</c> gates which hops may be
/// unwound and <c>ForwardLimit</c> caps how many are, so a chain longer than the
/// cap stops early with every hop in it trusted and nothing failing.
/// </summary>
public sealed class ForwardedHeadersTests
{
    private const string Client = "203.0.113.7";
    private const string Edge = "198.51.100.9";

    private static NativeWebOptions Options(int forwardLimit) => new()
    {
        ApplicationName = "Demo",
        ConfigurationSection = "Demo",
        TelemetryServiceName = "Demo",
        DataProtectionKeyDirectory = Path.Combine(AppContext.BaseDirectory, "dp-keys"),
        UploadDirectory = Path.Combine(AppContext.BaseDirectory, "uploads"),
        KnownProxies = [IPAddress.Loopback, IPAddress.Parse(Edge)],
        ForwardLimit = forwardLimit,
    };

    /// <summary>
    /// The <c>provision.sh --edge</c> chain: client to edge proxy to the local
    /// Caddy to Kestrel, so the peer is loopback and the header carries two
    /// entries, oldest first.
    /// </summary>
    private static async Task<IPAddress?> RemoteIpBehindTwoHopsAsync(int forwardLimit)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddNativeWebForwardedHeaders(Options(forwardLimit));

        await using WebApplication app = builder.Build();
        app.UseForwardedHeaders();

        IPAddress? seen = null;
        app.Run(context =>
        {
            seen = context.Connection.RemoteIpAddress;
            return Task.CompletedTask;
        });

        await app.StartAsync();

        await app.GetTestServer().SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Headers["X-Forwarded-For"] = $"{Client}, {Edge}";
        });

        await app.StopAsync();
        return seen;
    }

    [Fact]
    public async Task AtTheDefaultLimitATwoHopChainStopsOnTheEdgeProxy()
    {
        Assert.Equal(IPAddress.Parse(Edge), await RemoteIpBehindTwoHopsAsync(forwardLimit: 1));
    }

    [Fact]
    public async Task AForwardLimitOfTwoReachesTheRealClient()
    {
        Assert.Equal(IPAddress.Parse(Client), await RemoteIpBehindTwoHopsAsync(forwardLimit: 2));
    }

    [Fact]
    public async Task ALimitPastTheChainDoesNotWalkIntoUntrustedEntries()
    {
        // The client is not a known proxy, so the walk stops on it whatever the
        // cap allows. Without this a reader could take the fix for "raise the
        // limit and the last entry wins", which is the spoofing shape.
        Assert.Equal(IPAddress.Parse(Client), await RemoteIpBehindTwoHopsAsync(forwardLimit: 8));
    }
}
