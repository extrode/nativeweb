using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The sample's non-HTML representations through a real host (spec 021 R8, R9):
/// the listing as JSON and CSV, the feed as Atom and RSS, the sitemap, the
/// robots file and the about document, on both stores.
/// </summary>
/// <remarks>
/// The unit tiers state what each writer emits and what the chooser picks.
/// What is unproven until here is that the sample wired them: that the routes
/// exist beside the page, that the same rows come out of the store as data,
/// that links resolve against the host a client used, and that nothing but
/// the page sets a cookie.
/// </remarks>
public sealed class PublicFormatsTests : IClassFixture<LeakedFeedRowsSweep>
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private static async Task<(HttpResponseMessage Response, string Body)> GetAsync(
        HttpClient client,
        string path,
        string? accept = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        HttpResponseMessage response = await client.SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync());
    }

    // --- the in-memory store, over the seeded demo rows -------------------------

    [Theory]
    [InlineData("/articles.json", "application/json")]
    [InlineData("/articles.csv", "text/csv")]
    [InlineData("/feed", "application/atom+xml")]
    [InlineData("/feed.atom", "application/atom+xml")]
    [InlineData("/feed.rss", "application/rss+xml")]
    [InlineData("/sitemap.xml", "application/xml")]
    [InlineData("/robots.txt", "text/plain")]
    [InlineData("/about", "text/markdown")]
    [InlineData("/about.md", "text/markdown")]
    [InlineData("/about.txt", "text/plain")]
    public async Task EveryDocumentAnswersItsMediaTypeWithoutACookie(string path, string mediaType)
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage response, string body) = await GetAsync(client, path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(body);
        Assert.Equal(Encoding.UTF8.GetByteCount(body), response.Content.Headers.ContentLength);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(response.Headers.Contains("Set-Cookie"), "A data document must not set a cookie.");
    }

    [Fact]
    public async Task TheListingPageIsRenderedForEveryVisitorNeverSharedFromACache()
    {
        using var factory = new SampleFactory();
        using HttpClient returning = factory.CreateFollowingNothing();
        using HttpClient stranger = factory.CreateFollowingNothing();

        (HttpResponseMessage first, _) = await GetAsync(returning, "/articles");
        (HttpResponseMessage second, string secondBody) = await GetAsync(returning, "/articles");
        (HttpResponseMessage third, string thirdBody) = await GetAsync(stranger, "/articles");

        Assert.True(first.Headers.Contains("Set-Cookie"));
        Assert.False(second.Headers.Contains("Set-Cookie"));
        Assert.True(third.Headers.Contains("Set-Cookie"), "The stranger got the returning visitor's page from a cache.");
        Assert.NotEqual(secondBody, thirdBody);
    }

    [Fact]
    public async Task ThePageStillSetsItsAntiforgeryCookieAndTheDataDoesNot()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage page, _) = await GetAsync(client, "/articles");
        (HttpResponseMessage json, _) = await GetAsync(client, "/articles", accept: "application/json");

        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.True(page.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", page.Headers.CacheControl?.ToString());
        Assert.Equal("application/json", json.Content.Headers.ContentType?.MediaType);
        Assert.False(json.Headers.Contains("Set-Cookie"));
        Assert.Contains("Accept", json.Headers.Vary);
    }

    [Fact]
    public async Task JsonListsThePublishedRowsAndHidesDrafts()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (_, string body) = await GetAsync(client, "/articles.json");

        using var document = JsonDocument.Parse(body);
        string[] ids = document.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();
        JsonElement first = document.RootElement[0];

        Assert.Equal(["a8", "a7", "a6", "a5", "a2", "a1"], ids);
        Assert.Equal("A note written last week and published today", first.GetProperty("title").GetString());
        Assert.Equal("a-note-written-last-week-and-published-today", first.GetProperty("slug").GetString());
        Assert.Equal("Sy", first.GetProperty("author").GetString());
        Assert.Equal("2026-09-02T07:10:00Z", first.GetProperty("createdAt").GetString());
        Assert.Equal("2026-08-24", first.GetProperty("publishedAt").GetString());
        Assert.Equal(
            ["htmx", "aot"],
            first.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("slug").GetString()));
        Assert.Equal(0, document.RootElement[2].GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task CsvListsThePublishedRowsWithAHeaderAndCrlf()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (_, string body) = await GetAsync(client, "/articles.csv");

        Assert.Equal(
            "id,slug,title,author,created,published,tags\r\n" +
            "a8,a-note-written-last-week-and-published-today,A note written last week and published today,Sy,2026-09-02T07:10:00Z,2026-08-24,htmx aot\r\n" +
            "a7,what-a-source-generator-owes-you,What a source generator owes you,Sy,2026-08-05T13:00:00Z,2026-08-06,aot\r\n" +
            "a6,notes-from-a-week-without-a-bundler,Notes from a week without a bundler,Dana Whitfield,2026-07-28T10:15:00Z,2026-07-29,\r\n" +
            "a5,reading-an-explain-before-adding-an-index,Reading an EXPLAIN before adding an index,Dana Whitfield,2026-07-02T16:45:00Z,2026-07-03,aot\r\n" +
            "a2,binding-forms-under-nativeaot,Binding forms under NativeAOT,Sy,2026-06-18T14:05:00Z,2026-06-20,\r\n" +
            "a1,lean-native-web,Lean Native Web,Sy,2026-03-04T09:30:00Z,2026-03-05,htmx aot\r\n",
            body);
    }

    [Fact]
    public async Task AnUnofferedExtensionIs404NamingTheOffers()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage response, string body) = await GetAsync(client, "/articles.xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Offered: text/html, application/json, text/csv\n", body);
    }

    [Fact]
    public async Task TheAtomFeedLinksAbsolutelyAgainstTheHostUsed()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage response, string body) = await GetAsync(client, "/feed");

        var feed = XDocument.Parse(body);
        XElement root = feed.Root!;
        XElement[] entries = root.Elements(Atom + "entry").ToArray();

        Assert.Equal(Atom + "feed", root.Name);
        Assert.Equal("http://localhost/feed", root.Element(Atom + "id")?.Value);
        Assert.Equal("http://localhost/articles", root.Element(Atom + "link")?.Attribute("href")?.Value);
        Assert.Equal(6, entries.Length);
        Assert.Equal("A note written last week and published today", entries[0].Element(Atom + "title")?.Value);
        Assert.Equal(
            "http://localhost/articles/a-note-written-last-week-and-published-today",
            entries[0].Element(Atom + "link")?.Attribute("href")?.Value);
        Assert.Equal("2026-08-24T00:00:00Z", entries[0].Element(Atom + "updated")?.Value);
        Assert.Equal("2026-08-24T00:00:00Z", root.Element(Atom + "updated")?.Value);
        Assert.StartsWith("public, max-age=", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRssFeedCarriesTheSameEntries()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (_, string body) = await GetAsync(client, "/feed", accept: "application/rss+xml");

        var rss = XDocument.Parse(body);
        XElement channel = rss.Root!.Element("channel")!;
        XElement[] items = channel.Elements("item").ToArray();

        Assert.Equal("2.0", rss.Root.Attribute("version")?.Value);
        Assert.Equal("http://localhost/articles", channel.Element("link")?.Value);
        Assert.Equal(6, items.Length);
        Assert.Equal("Lean Native Web", items[^1].Element("title")?.Value);
        Assert.Equal("http://localhost/articles/lean-native-web", items[^1].Element("link")?.Value);
        Assert.Equal("Thu, 05 Mar 2026 00:00:00 GMT", items[^1].Element("pubDate")?.Value);
    }

    [Fact]
    public async Task TheSitemapListsTheFixedPagesThenEveryTagThenEveryPublishedArticle()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (_, string body) = await GetAsync(client, "/sitemap.xml");

        var sitemap = XDocument.Parse(body);
        XElement[] urls = sitemap.Root!.Elements(Sitemap + "url").ToArray();
        string[] locations = urls.Select(u => u.Element(Sitemap + "loc")!.Value).ToArray();

        Assert.Equal(
            [
                "http://localhost/",
                "http://localhost/articles",
                "http://localhost/tags",
                "http://localhost/feed.atom",
                "http://localhost/about",
                "http://localhost/tags/htmx",
                "http://localhost/tags/aot",
                "http://localhost/tags/postgres",
                "http://localhost/articles/a-note-written-last-week-and-published-today",
                "http://localhost/articles/what-a-source-generator-owes-you",
                "http://localhost/articles/notes-from-a-week-without-a-bundler",
                "http://localhost/articles/reading-an-explain-before-adding-an-index",
                "http://localhost/articles/binding-forms-under-nativeaot",
                "http://localhost/articles/lean-native-web",
            ],
            locations);
        Assert.Equal("2026-03-04T09:30:00Z", urls[^1].Element(Sitemap + "lastmod")?.Value);
    }

    [Fact]
    public async Task RobotsKeepsCrawlersOutOfTheAdminAndNamesTheSitemap()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (_, string body) = await GetAsync(client, "/robots.txt");

        Assert.Equal(
            "User-agent: *\nDisallow: /admin\nDisallow: /version\nSitemap: http://localhost/sitemap.xml\n",
            body);
    }

    [Fact]
    public async Task AboutIsMarkdownByDefaultAndTextOnRequest()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (_, string markdown) = await GetAsync(client, "/about");
        (HttpResponseMessage viaAccept, string text) = await GetAsync(client, "/about", accept: "text/plain");

        Assert.StartsWith("# NativeWeb sample", markdown, StringComparison.Ordinal);
        Assert.Equal("text/plain", viaAccept.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("NativeWeb sample", text, StringComparison.Ordinal);
        Assert.DoesNotContain("# ", text, StringComparison.Ordinal);
    }

    // --- the JauntyQ store, over real PostgreSQL --------------------------------

    /// <summary>
    /// The same SQL the page reads through answers the data routes: a row that
    /// exists only in PostgreSQL appears in the JSON and in the feed, and a
    /// draft does not.
    /// </summary>
    [Fact]
    public async Task OverPostgres_JsonAndFeedShowThePublishedRowAndHideTheDraft()
    {
        string prefix = $"fmt-{Guid.NewGuid():N}"[..16];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-p", $"{prefix}-published", "xunit", "body", published: true, createdAt: future);
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-d", $"{prefix}-draft", "xunit", "body", published: false, createdAt: future.AddMinutes(1));

            using var factory = new SampleJauntyFactory();
            using HttpClient client = factory.CreateFollowingNothing();

            (HttpResponseMessage jsonResponse, string json) = await GetAsync(client, "/articles.json");
            (HttpResponseMessage feedResponse, string feed) = await GetAsync(client, "/feed");

            Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, feedResponse.StatusCode);

            using var document = JsonDocument.Parse(json);
            string[] titles = document.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("title").GetString()!)
                .ToArray();

            Assert.Contains($"{prefix}-published", titles);
            Assert.DoesNotContain($"{prefix}-draft", titles);

            string[] entryTitles = XDocument.Parse(feed).Root!
                .Elements(Atom + "entry")
                .Select(e => e.Element(Atom + "title")!.Value)
                .ToArray();

            Assert.Contains($"{prefix}-published", entryTitles);
            Assert.DoesNotContain($"{prefix}-draft", entryTitles);
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }
}
