using Microsoft.Extensions.Configuration;

using NativeWeb.Sample;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// Which origin the demo's two channels use (spec 016 R28).
/// </summary>
/// <remarks>
/// The front channel is what the browser walks and what the redirect URI is registered under;
/// the back channel is what this app's own relying party calls server-to-server. They are one
/// origin for anyone running <c>dotnet run</c>, and the e2e tier is the reason they can differ:
/// it serves the browser over https for the <c>__Host-</c> cookie, over a certificate the app's
/// own HttpClient has no reason to trust. A fallback that quietly ignored the configured value,
/// or one that stopped falling back, would each be found only by a fifteen-minute publish.
/// </remarks>
public sealed class DemoProviderOriginTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Fact]
    public void SelfOrigin_TakesTheFirstBoundUrl()
    {
        IConfiguration configuration = Configuration(("urls", "https://localhost:5349;http://localhost:5350"));

        Assert.Equal("https://localhost:5349", DemoProvider.SelfOrigin(configuration));
    }

    [Fact]
    public void SelfOrigin_ResolvesAWildcardBindingToLocalhost()
    {
        Assert.Equal("http://localhost:5000", DemoProvider.SelfOrigin(Configuration(("urls", "http://*:5000/"))));
        Assert.Equal("http://localhost:5000", DemoProvider.SelfOrigin(Configuration(("urls", "http://+:5000"))));
    }

    [Fact]
    public void SelfOrigin_WithNothingBound_IsTheDefaultPort()
    {
        Assert.Equal("http://localhost:5000", DemoProvider.SelfOrigin(Configuration()));
    }

    [Fact]
    public void BackChannelOrigin_WithNothingConfigured_IsTheBrowsersOrigin()
    {
        IConfiguration configuration = Configuration(("urls", "https://localhost:5349;http://localhost:5350"));

        Assert.Equal(
            DemoProvider.SelfOrigin(configuration),
            DemoProvider.BackChannelOrigin(configuration));
    }

    [Fact]
    public void BackChannelOrigin_WhenConfigured_LeavesTheBrowsersOriginAlone()
    {
        IConfiguration configuration = Configuration(
            ("urls", "https://localhost:5349;http://localhost:5350"),
            ("Demo:BackChannelOrigin", "http://localhost:5350/"));

        Assert.Equal("http://localhost:5350", DemoProvider.BackChannelOrigin(configuration));
        Assert.Equal("https://localhost:5349", DemoProvider.SelfOrigin(configuration));
    }

    [Fact]
    public void BackChannelOrigin_WithAnEmptyValue_FallsBack()
    {
        // An unset environment variable and one set to nothing arrive the same way, and the
        // second is what a launch script produces when its own variable is unset.
        IConfiguration configuration = Configuration(
            ("urls", "https://localhost:5349"),
            ("Demo:BackChannelOrigin", ""));

        Assert.Equal("https://localhost:5349", DemoProvider.BackChannelOrigin(configuration));
    }

    [Fact]
    public void BothOrigins_RefuseANullConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => DemoProvider.SelfOrigin(null!));
        Assert.Throws<ArgumentNullException>(() => DemoProvider.BackChannelOrigin(null!));
    }
}
