using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The in-memory <see cref="IPublishedArticles"/> for the pages spec 022 adds:
/// one article by slug, the view it counts, one tag's articles and the sitemap.
/// <see cref="JauntyPublishedArticles"/> is absent for the reason
/// <see cref="PublicArticlesTests"/> gives; the integration tier proves it.
/// </summary>
public sealed class PublishedArticlesTests
{
    private readonly ArticleStore _articles = new();

    private readonly TagStore _tags = new();

    private readonly MemoryRelation _relation = new();

    private MemoryPublishedArticles Reader => new(this._articles, this._tags, this._relation);

    private static DateTime Utc(int day) => new(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

    private Article Seed(string id, int day, bool published = true, params string[] tags)
    {
        var article = new Article
        {
            Id = id,
            Slug = $"slug-{id}",
            Title = $"title-{id}",
            Author = "Sy",
            Body = "one\n\ntwo",
            Published = published,
            CreatedAt = Utc(day),
        };
        this._articles.Seed(article);
        foreach (string tag in tags)
        {
            this._relation.Link(id, tag);
        }

        return article;
    }

    private void SeedTags()
    {
        this._tags.Seed(new Tag { Slug = "aot", Name = "NativeAOT" });
        this._tags.Seed(new Tag { Slug = "htmx", Name = "HTMX" });
        this._tags.Seed(new Tag { Slug = "postgres", Name = "PostgreSQL" });
    }

    [Fact]
    public async Task BySlugAsync_ReturnsThePublishedArticleWithItsTagsByName()
    {
        SeedTags();
        Seed("a1", 1, tags: ["htmx", "aot"]);

        ArticleDetail? detail = await Reader.BySlugAsync("slug-a1", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("title-a1", detail.Value.Title);
        Assert.Equal("one\n\ntwo", detail.Value.Body);
        Assert.Equal(["HTMX", "NativeAOT"], detail.Value.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task BySlugAsync_AnswersNullForAnUnpublishedSlugAndForAMissingOne()
    {
        Seed("a1", 1, published: false);

        Assert.Null(await Reader.BySlugAsync("slug-a1", CancellationToken.None));
        Assert.Null(await Reader.BySlugAsync("slug-nowhere", CancellationToken.None));
    }

    [Fact]
    public async Task BySlugAsync_OmitsATagTheTagStoreNoLongerHolds()
    {
        SeedTags();
        Seed("a1", 1, tags: ["aot", "deleted"]);

        ArticleDetail? detail = await Reader.BySlugAsync("slug-a1", CancellationToken.None);

        Assert.Equal(["aot"], detail!.Value.Tags.Select(t => t.Slug));
    }

    [Fact]
    public async Task CountViewAsync_AddsOneToThePublishedRowAndReportsIt()
    {
        Article article = Seed("a1", 1);
        article.ViewCount = 7;

        bool counted = await Reader.CountViewAsync("slug-a1", CancellationToken.None);

        Assert.True(counted);
        Assert.Equal(8, article.ViewCount);
    }

    [Fact]
    public async Task CountViewAsync_CountsNothingForAnUnpublishedOrMissingSlug()
    {
        Article draft = Seed("a1", 1, published: false);
        MemoryPublishedArticles reader = Reader;

        Assert.False(await reader.CountViewAsync("slug-a1", CancellationToken.None));
        Assert.False(await reader.CountViewAsync("slug-nowhere", CancellationToken.None));
        Assert.Equal(0, draft.ViewCount);
    }

    [Fact]
    public async Task CountViewAsync_FiftyParallelCallsAddFifty()
    {
        Article article = Seed("a1", 1);
        MemoryPublishedArticles reader = Reader;

        await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => reader.CountViewAsync("slug-a1", CancellationToken.None).AsTask())));

        Assert.Equal(50, article.ViewCount);
    }

    [Fact]
    public async Task RecentAsync_CarriesEachArticlesSlugAndTags()
    {
        SeedTags();
        Seed("a1", 1, tags: ["aot", "htmx"]);
        Seed("a2", 2);

        IReadOnlyList<PublishedArticle> recent = await Reader.RecentAsync(10, CancellationToken.None);

        Assert.Equal(["slug-a2", "slug-a1"], recent.Select(a => a.Slug));
        Assert.Empty(recent[0].Tags);
        Assert.Equal(["htmx", "aot"], recent[1].Tags.Select(t => t.Slug));
    }

    [Fact]
    public async Task ByTagAsync_ListsPublishedArticlesWithTheTagNewestFirstAndAllOfTheirTags()
    {
        SeedTags();
        Seed("a1", 1, tags: ["aot", "htmx"]);
        Seed("a2", 2, tags: ["htmx"]);
        Seed("a3", 3, tags: ["aot"]);
        Seed("a4", 4, published: false, tags: ["htmx"]);

        IReadOnlyList<PublishedArticle> tagged = await Reader.ByTagAsync("htmx", 10, CancellationToken.None);

        Assert.Equal(["a2", "a1"], tagged.Select(a => a.Id));
        Assert.Equal(["htmx", "aot"], tagged[1].Tags.Select(t => t.Slug));
    }

    [Fact]
    public async Task ByTagAsync_TakesAtMostTheRequestedCount()
    {
        SeedTags();
        Seed("a1", 1, tags: ["aot"]);
        Seed("a2", 2, tags: ["aot"]);

        IReadOnlyList<PublishedArticle> tagged = await Reader.ByTagAsync("aot", 1, CancellationToken.None);

        Assert.Equal(["a2"], tagged.Select(a => a.Id));
    }

    [Fact]
    public async Task ByTagAsync_IsEmptyForATagWithNoPublishedArticleAndForAnUnknownTag()
    {
        SeedTags();
        Seed("a1", 1, published: false, tags: ["postgres"]);
        MemoryPublishedArticles reader = Reader;

        Assert.Empty(await reader.ByTagAsync("postgres", 10, CancellationToken.None));
        Assert.Empty(await reader.ByTagAsync("nowhere", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SitemapAsync_ListsEveryPublishedSlugNewestFirst()
    {
        Seed("a1", 1);
        Seed("a2", 3);
        Seed("a3", 2, published: false);

        IReadOnlyList<SitemapArticle> entries = await Reader.SitemapAsync(CancellationToken.None);

        Assert.Equal([("slug-a2", Utc(3)), ("slug-a1", Utc(1))], entries.Select(e => (e.Slug, e.CreatedAt)));
    }
}
