using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

using Microsoft.AspNetCore.Http;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

public sealed class FeedTests
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly Uri Base = new("https://nw.example/");
    private static readonly DateTimeOffset When = new(2026, 9, 2, 10, 30, 0, TimeSpan.FromHours(2));

    private static Feed SampleFeed() => new(
        Id: "https://nw.example/feed.atom",
        Title: FormatResultsTests.HostileTitle,
        Link: "/articles",
        Updated: When,
        Author: "Sy",
        Subtitle: "Recent articles",
        Entries:
        [
            new FeedEntry("urn:a1", FormatResultsTests.HostileTitle, "/articles/a1", When, Summary: "First & foremost"),
            new FeedEntry("urn:a2", "Second", "https://other.example/x", When.AddDays(-1), Author: "Ann"),
        ]);

    private static async Task<(HttpResponse Response, XDocument Document)> ParseAsync(IResult result)
    {
        (HttpResponse response, byte[] body) = await FormatResultsTests.ExecuteAsync(result);
        return (response, XDocument.Parse(Encoding.UTF8.GetString(body)));
    }

    [Fact]
    public async Task Atom_CarriesTheRequiredElementsWithAbsoluteLinksAndRfc3339Dates()
    {
        (HttpResponse response, XDocument doc) = await ParseAsync(FormatResults.Atom(SampleFeed(), Base));

        Assert.Equal("application/atom+xml; charset=utf-8", response.ContentType);
        XElement feed = doc.Root!;
        Assert.Equal(Atom + "feed", feed.Name);
        Assert.Equal("https://nw.example/feed.atom", feed.Element(Atom + "id")?.Value);
        Assert.Equal(FormatResultsTests.HostileTitle, feed.Element(Atom + "title")?.Value);
        Assert.Equal("2026-09-02T08:30:00Z", feed.Element(Atom + "updated")?.Value);
        Assert.Equal("https://nw.example/articles", feed.Element(Atom + "link")?.Attribute("href")?.Value);
        Assert.Equal("Sy", feed.Element(Atom + "author")?.Element(Atom + "name")?.Value);

        XElement[] entries = feed.Elements(Atom + "entry").ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal("urn:a1", entries[0].Element(Atom + "id")?.Value);
        Assert.Equal(FormatResultsTests.HostileTitle, entries[0].Element(Atom + "title")?.Value);
        Assert.Equal("https://nw.example/articles/a1", entries[0].Element(Atom + "link")?.Attribute("href")?.Value);
        Assert.Equal("First & foremost", entries[0].Element(Atom + "summary")?.Value);
        Assert.Equal("https://other.example/x", entries[1].Element(Atom + "link")?.Attribute("href")?.Value);
        Assert.Equal("Ann", entries[1].Element(Atom + "author")?.Element(Atom + "name")?.Value);
        Assert.Equal("2026-09-01T08:30:00Z", entries[1].Element(Atom + "updated")?.Value);
    }

    [Fact]
    public async Task Rss_CarriesTheRequiredChannelElementsAndRfc822Dates()
    {
        (HttpResponse response, XDocument doc) = await ParseAsync(FormatResults.Rss(SampleFeed(), Base));

        Assert.Equal("application/rss+xml; charset=utf-8", response.ContentType);
        XElement rss = doc.Root!;
        Assert.Equal("rss", rss.Name.LocalName);
        Assert.Equal("2.0", rss.Attribute("version")?.Value);
        XElement channel = rss.Element("channel")!;
        Assert.Equal(FormatResultsTests.HostileTitle, channel.Element("title")?.Value);
        Assert.Equal("https://nw.example/articles", channel.Element("link")?.Value);
        Assert.Equal("Recent articles", channel.Element("description")?.Value);
        Assert.Equal("Wed, 02 Sep 2026 08:30:00 GMT", channel.Element("lastBuildDate")?.Value);
        Assert.Equal("Sy", channel.Element(DublinCore + "creator")?.Value);

        XElement[] items = channel.Elements("item").ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(FormatResultsTests.HostileTitle, items[0].Element("title")?.Value);
        Assert.Equal("urn:a1", items[0].Element("guid")?.Value);
        Assert.Equal("false", items[0].Element("guid")?.Attribute("isPermaLink")?.Value);
        Assert.Equal("Tue, 01 Sep 2026 08:30:00 GMT", items[1].Element("pubDate")?.Value);
        Assert.Equal("Ann", items[1].Element(DublinCore + "creator")?.Value);
    }

    [Fact]
    public async Task Rss_DescriptionFallsBackToTheTitle()
    {
        Feed feed = SampleFeed() with { Subtitle = null };

        (_, XDocument doc) = await ParseAsync(FormatResults.Rss(feed, Base));

        Assert.Equal(FormatResultsTests.HostileTitle, doc.Root?.Element("channel")?.Element("description")?.Value);
    }

    [Fact]
    public async Task Feeds_ResolveAgainstTheRequestWhenGivenOne()
    {
        DefaultHttpContext http = new();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("nw-dev.example.com");

        (_, XDocument doc) = await ParseAsync(FormatResults.Atom(SampleFeed(), http.Request));

        Assert.Equal(
            "https://nw-dev.example.com/articles",
            doc.Root?.Element(Atom + "link")?.Attribute("href")?.Value);
    }

    [Fact]
    public async Task Sitemap_ValidatesAgainstTheSitemapsOrgSchema()
    {
        SitemapEntry[] entries =
        [
            new("/articles", When, ChangeFrequency.Daily, 0.8),
            new("/about"),
            new("https://other.example/x", Priority: 1.0),
        ];

        (HttpResponse response, byte[] body) = await FormatResultsTests.ExecuteAsync(
            FormatResults.Sitemap(entries, Base));

        Assert.Equal("application/xml; charset=utf-8", response.ContentType);
        Assert.Equal(body.Length, response.ContentLength);

        XmlSchemaSet schemas = new();
        schemas.Add("http://www.sitemaps.org/schemas/sitemap/0.9", Path.Combine(AppContext.BaseDirectory, "sitemap.xsd"));
        var doc = XDocument.Parse(Encoding.UTF8.GetString(body));
        List<string> errors = [];
        doc.Validate(schemas, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XElement[] urls = doc.Root!.Elements(ns + "url").ToArray();
        Assert.Equal(3, urls.Length);
        Assert.Equal("https://nw.example/articles", urls[0].Element(ns + "loc")?.Value);
        Assert.Equal("daily", urls[0].Element(ns + "changefreq")?.Value);
        Assert.Equal("0.8", urls[0].Element(ns + "priority")?.Value);
        Assert.Null(urls[1].Element(ns + "lastmod"));
    }

    [Fact]
    public void Sitemap_RefusesAPriorityOutsideTheRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FormatResults.Sitemap([new SitemapEntry("/x", Priority: 1.5)], Base));
    }

    [Fact]
    public void Resolve_KeepsAnAbsoluteLinkAndRootsAPath()
    {
        Assert.Equal("https://nw.example/a/b", FeedWriter.Resolve(Base, "/a/b"));
        Assert.Equal("https://other.example/x", FeedWriter.Resolve(Base, "https://other.example/x"));
    }
}
