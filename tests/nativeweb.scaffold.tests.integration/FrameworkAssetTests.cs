using System.Net;
using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The framework's own browser assets, fetched the way a browser fetches them.
/// </summary>
/// <remarks>
/// <para>
/// These exist because every one of them answered 500 under <c>dotnet run</c>
/// from 2026-08-24 until 2026-09-03 and no suite noticed. The three framework
/// stylesheets live in <c>libs/nativeweb.web/wwwroot/_nativeweb/</c> and reach a
/// product through <c>NativeWeb.Web.targets</c>; a published app serves them out
/// of its own <c>wwwroot</c> and is fine, while a host running from source has
/// no mapping for <c>/_nativeweb/</c> unless the static web assets manifest is
/// loaded — and <c>CreateSlimBuilder</c>, unlike <c>CreateBuilder</c>, does not
/// load it. So production was styled and Development was not.
/// </para>
/// <para>
/// The generic sweep is the point rather than the four named paths. A test that
/// listed the assets would have to be edited whenever the layout gains one, and
/// the asset nobody remembered to add is the one that breaks; walking the markup
/// covers whatever the page actually asks for.
/// </para>
/// </remarks>
public sealed class FrameworkAssetTests
{
    private static readonly Regex LocalReference = new(
        "(?:href|src)=\"(?<url>/[^\"#?]*\\.(?:css|js))\"",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task EveryStylesheetAndScriptTheHomePageAsksForIsServed()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage page = await client.GetAsync(new Uri("/", UriKind.Relative));
        string html = await page.Content.ReadAsStringAsync();

        string[] urls = LocalReference.Matches(html)
            .Select(match => match.Groups["url"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(urls.Length >= 5, $"The home page linked only {urls.Length} local assets.");

        foreach (string url in urls)
        {
            using HttpResponseMessage asset = await client.GetAsync(new Uri(url, UriKind.Relative));
            byte[] body = await asset.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.NotEmpty(body);
        }
    }

    [Theory]
    [InlineData("/_nativeweb/css/tokens.css", "css/tokens.css")]
    [InlineData("/_nativeweb/css/base.css", "css/base.css")]
    [InlineData("/_nativeweb/css/components.css", "css/components.css")]
    [InlineData("/_nativeweb/boot.js", "boot.js")]
    public async Task AFrameworkAssetIsServedByteForByteFromTheLibraryThatOwnsIt(string url, string relativePath)
    {
        string source = Path.Combine(
            SampleStartupRefusalTests.RepoRoot(),
            "libs",
            "nativeweb.web",
            "wwwroot",
            "_nativeweb",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri(url, UriKind.Relative));
        byte[] served = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(await File.ReadAllBytesAsync(source), served);
    }
}
