using NativeWeb.Render;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// What spec 022 R8 changed in the documents 021 already published: the feed
/// entry's link, the sitemap's entries and the CSV row.
/// </summary>
public sealed class PublicDocumentsTests
{
    private static readonly Uri Base = new("http://localhost/");

    private static DateTime Utc(int day) => new(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

    private static PublishedArticle Listed(string id, string slug, params TagRef[] tags) =>
        new(id, slug, $"Title {id}", "Sy", Utc(4), tags);

    [Fact]
    public void BuildFeed_LinksEachEntryToTheArticlesPage()
    {
        Feed feed = PublicDocuments.BuildFeed([Listed("a1", "lean-native-web")], Base);

        Assert.Equal("/articles/lean-native-web", feed.Entries[0].Link);
        Assert.Equal("http://localhost/articles/lean-native-web", feed.Entries[0].Id);
    }

    [Fact]
    public void SitemapEntries_ListsTheFixedPagesThenTagsThenArticlesWithTheirDates()
    {
        List<SitemapEntry> entries = PublicDocuments.SitemapEntries(
            [new SitemapArticle("newer", Utc(9)), new SitemapArticle("older", Utc(2))],
            [new TagSummary("aot", "NativeAOT", 1), new TagSummary("postgres", "PostgreSQL", 0)]);

        Assert.Equal(
            ["/", "/articles", "/tags", "/feed.atom", "/about", "/tags/aot", "/tags/postgres", "/articles/newer", "/articles/older"],
            entries.Select(e => e.Location));
        Assert.Equal(new DateTimeOffset(Utc(9)), entries[7].LastModified);
        Assert.Equal(new DateTimeOffset(Utc(2)), entries[8].LastModified);
        Assert.All(entries.Take(7), e => Assert.Null(e.LastModified));
    }

    [Fact]
    public void SitemapEntries_WithNothingPublished_IsTheFixedPagesAlone() =>
        Assert.Equal(5, PublicDocuments.SitemapEntries([], []).Count);

    [Fact]
    public void CsvRow_CarriesTheSlugAndTheTagSlugsSpaceJoined()
    {
        string[] row = PublicArticleEndpoints.CsvRow(
            Listed("a1", "lean-native-web", new TagRef("htmx", "HTMX"), new TagRef("aot", "NativeAOT")));

        Assert.Equal(["a1", "lean-native-web", "Title a1", "Sy", "2026-01-04T00:00:00Z", "", "htmx aot"], row);
    }

    [Fact]
    public void CsvRow_CarriesThePublicationDateAsADay()
    {
        string[] row = PublicArticleEndpoints.CsvRow(
            Listed("a1", "s") with { PublishedAt = new DateOnly(2026, 2, 9) });

        Assert.Equal("2026-02-09", row[5]);
    }

    [Fact]
    public void BuildFeed_DatesAnEntryByItsPublicationDayWhenItHasOne()
    {
        PublishedArticle dated = Listed("a1", "s") with { PublishedAt = new DateOnly(2026, 2, 9) };

        Feed feed = PublicDocuments.BuildFeed([dated, Listed("a2", "t")], Base);

        Assert.Equal(new DateTimeOffset(2026, 2, 9, 0, 0, 0, TimeSpan.Zero), feed.Entries[0].Updated);
        Assert.Equal(new DateTimeOffset(Utc(4)), feed.Entries[1].Updated);
    }

    [Fact]
    public void CsvRow_WithNoTags_HasAnEmptyLastCell() =>
        Assert.Equal("", PublicArticleEndpoints.CsvRow(Listed("a1", "s"))[^1]);
}
