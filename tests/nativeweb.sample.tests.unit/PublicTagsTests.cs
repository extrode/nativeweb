using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The in-memory <see cref="IPublicTags"/> (spec 022 R5): every tag by name with
/// a count of published articles, and one tag by slug.
/// </summary>
public sealed class PublicTagsTests
{
    private readonly ArticleStore _articles = new();

    private readonly TagStore _tags = new();

    private readonly MemoryRelation _relation = new();

    private MemoryPublicTags Reader => new(this._tags, this._articles, this._relation);

    private void Seed(string id, bool published, params string[] tags)
    {
        this._articles.Seed(new Article { Id = id, Slug = id, Title = id, Status = published ? ArticleStatus.Published : ArticleStatus.Draft });
        foreach (string tag in tags)
        {
            this._relation.Link(id, tag);
        }
    }

    private void SeedTags()
    {
        this._tags.Seed(new Tag { Slug = "aot", Name = "NativeAOT" });
        this._tags.Seed(new Tag { Slug = "htmx", Name = "HTMX" });
        this._tags.Seed(new Tag { Slug = "postgres", Name = "PostgreSQL" });
    }

    [Fact]
    public async Task ListAsync_CountsPublishedArticlesOnlyAndKeepsATagWithNone()
    {
        SeedTags();
        Seed("a1", published: true, "aot", "htmx");
        Seed("a2", published: true, "aot");
        Seed("a3", published: false, "postgres");

        IReadOnlyList<TagSummary> tags = await Reader.ListAsync(CancellationToken.None);

        Assert.Equal(
            [("htmx", "HTMX", 1), ("aot", "NativeAOT", 2), ("postgres", "PostgreSQL", 0)],
            tags.Select(t => (t.Slug, t.Name, t.PublishedCount)));
    }

    [Fact]
    public async Task ListAsync_StopsCountingAnArticleTheStoreNoLongerHolds()
    {
        SeedTags();
        Seed("a1", published: true, "aot");
        await this._articles.DeleteAsync("a1", CancellationToken.None);

        IReadOnlyList<TagSummary> tags = await Reader.ListAsync(CancellationToken.None);

        Assert.Equal(0, tags.Single(t => t.Slug == "aot").PublishedCount);
    }

    [Fact]
    public async Task ListAsync_WithNoTags_IsEmpty() =>
        Assert.Empty(await Reader.ListAsync(CancellationToken.None));

    [Fact]
    public async Task GetAsync_ReturnsTheTagOrNull()
    {
        SeedTags();
        MemoryPublicTags reader = Reader;

        Assert.Equal(new TagRef("htmx", "HTMX"), await reader.GetAsync("htmx", CancellationToken.None));
        Assert.Null(await reader.GetAsync("nowhere", CancellationToken.None));
    }
}
