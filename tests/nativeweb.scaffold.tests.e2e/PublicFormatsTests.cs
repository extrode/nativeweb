using System.Text.Json;

using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// The sample's data documents against the NativeAOT binary (spec 021 R2, R9).
/// </summary>
/// <remarks>
/// Nothing here needs a browser to be observed, and that is not why the tier
/// exists for it. The JSON is written by a source-generated serializer, the
/// feed by <c>XmlWriter</c>, and both are the kind of code that works under the
/// JIT and is trimmed out of a published binary without a warning; the only
/// place that is proved otherwise is a request to the real executable. The
/// in-memory fixture is enough, because the rows are seeded and the claim is
/// about the writers, not the store.
/// </remarks>
[Collection(ScaffoldE2eFixture.Name)]
public sealed class PublicFormatsTests(InMemorySampleFixture app, BrowserFixture browsers)
{
    private async Task<IPage> AnonymousPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl });

        return await context.NewPageAsync();
    }

    [Fact]
    public async Task JsonListing_UnderNativeAot_SerializesTheSeededRows()
    {
        IPage page = await AnonymousPageAsync();

        IResponse? response = await page.GotoAsync("/articles.json");

        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.StartsWith("application/json", response.Headers["content-type"], StringComparison.Ordinal);

        using var document = JsonDocument.Parse(await response.TextAsync());
        string[] ids = document.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();

        Assert.Equal(["a8", "a7", "a6", "a5", "a2", "a1"], ids);
    }

    [Fact]
    public async Task AtomFeed_UnderNativeAot_LinksAgainstThePublicBase()
    {
        IPage page = await AnonymousPageAsync();

        IResponse? response = await page.GotoAsync("/feed.atom");

        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.StartsWith("application/atom+xml", response.Headers["content-type"], StringComparison.Ordinal);

        string body = await response.TextAsync();

        Assert.Contains("<feed xmlns=\"http://www.w3.org/2005/Atom\">", body, StringComparison.Ordinal);
        Assert.Contains($"href=\"{app.BaseUrl}/articles/binding-forms-under-nativeaot\"", body, StringComparison.Ordinal);
        Assert.Contains("<updated>2026-06-20T00:00:00Z</updated>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAcceptHeader_UnderNativeAot_PicksCsvOnThePlainRoute()
    {
        IPage page = await AnonymousPageAsync();

        IAPIResponse response = await page.APIRequest.GetAsync(
            "/articles",
            new APIRequestContextOptions { Headers = new Dictionary<string, string> { ["Accept"] = "text/csv" } });

        Assert.Equal(200, response.Status);
        Assert.StartsWith("text/csv", response.Headers["content-type"], StringComparison.Ordinal);
        Assert.Contains("Accept", response.Headers["vary"], StringComparison.Ordinal);
        Assert.StartsWith("id,slug,title,author,created,published,tags\r\n", await response.TextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnofferedExtension_UnderNativeAot_Is404NamingTheOffers()
    {
        IPage page = await AnonymousPageAsync();

        IAPIResponse response = await page.APIRequest.GetAsync("/articles.xml");

        Assert.Equal(404, response.Status);
        Assert.Equal("Offered: text/html, application/json, text/csv\n", await response.TextAsync());
    }

    [Fact]
    public async Task SitemapAndRobots_UnderNativeAot_NameEachOtherAbsolutely()
    {
        IPage page = await AnonymousPageAsync();

        IAPIResponse sitemap = await page.APIRequest.GetAsync("/sitemap.xml");
        IAPIResponse robots = await page.APIRequest.GetAsync("/robots.txt");

        Assert.Equal(200, sitemap.Status);
        Assert.Equal(200, robots.Status);
        Assert.Contains($"<loc>{app.BaseUrl}/articles</loc>", await sitemap.TextAsync(), StringComparison.Ordinal);
        Assert.Contains($"Sitemap: {app.BaseUrl}/sitemap.xml\n", await robots.TextAsync(), StringComparison.Ordinal);
    }
}
