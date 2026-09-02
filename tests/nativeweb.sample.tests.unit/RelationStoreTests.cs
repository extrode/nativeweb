using NativeWeb.Sample;
using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The memory stores as the authority on the article-tag relation and the
/// category reference (spec 023 R1, R2, R5, R7, R9): a posted key is checked
/// against the peer store, both sides of a link are written from either side, and
/// a delete cascades.
/// </summary>
public sealed class RelationStoreTests
{
    private readonly MemoryStores _stores = MemoryStores.Create();

    private ArticleStore Articles => this._stores.Articles;

    private TagStore Tags => this._stores.Tags;

    private CategoryStore Categories => this._stores.Categories;

    private MemoryRelation Relation => this._stores.Relation;

    public RelationStoreTests()
    {
        this.Categories.Seed(new Category { Key = "guides", Name = "Guides" });
        this.Tags.Seed(new Tag { Slug = "aot", Name = "NativeAOT" });
        this.Tags.Seed(new Tag { Slug = "htmx", Name = "HTMX" });
        this.Articles.Seed(new Article { Id = "a1", Title = "One" });
        this.Articles.Seed(new Article { Id = "a2", Title = "Two" });
    }

    private static Article ArticleWith(params string[] tags) =>
        new() { Title = "t", Body = "b", Tags = tags };

    [Fact]
    public async Task SavingAnArticle_WritesBothSidesOfTheRelation()
    {
        await this.Articles.UpdateAsync("a1", ArticleWith("aot", "htmx"), CancellationToken.None);

        Assert.Equal(["aot", "htmx"], this.Relation.TagsOf("a1"));
        Assert.Equal(["a1"], this.Relation.ArticlesOf("aot"));
        Assert.Equal(["a1"], this.Relation.ArticlesOf("htmx"));
    }

    [Fact]
    public async Task SavingATag_WritesBothSidesOfTheRelation()
    {
        await this.Tags.UpdateAsync("aot", new Tag { Name = "NativeAOT", Articles = ["a1", "a2"] }, CancellationToken.None);

        Assert.Equal(["a1", "a2"], this.Relation.ArticlesOf("aot"));
        Assert.Equal(["aot"], this.Relation.TagsOf("a1"));
        Assert.Equal(["aot"], this.Relation.TagsOf("a2"));
    }

    [Fact]
    public async Task SavingAnArticleWithFewerTags_RemovesTheDroppedLinkFromTheTagSide()
    {
        await this.Articles.UpdateAsync("a1", ArticleWith("aot", "htmx"), CancellationToken.None);

        await this.Articles.UpdateAsync("a1", ArticleWith("htmx"), CancellationToken.None);

        Assert.Empty(this.Relation.ArticlesOf("aot"));
        Assert.Equal(["a1"], this.Relation.ArticlesOf("htmx"));
    }

    [Fact]
    public async Task DeselectingFromTheTagSide_ShowsOnTheArticleSide()
    {
        await this.Articles.UpdateAsync("a1", ArticleWith("aot", "htmx"), CancellationToken.None);

        await this.Tags.UpdateAsync("aot", new Tag { Name = "NativeAOT", Articles = [] }, CancellationToken.None);

        Article? row = await this.Articles.GetAsync("a1", CancellationToken.None);
        Assert.Equal(["htmx"], row?.Tags);
    }

    [Fact]
    public async Task GetAsync_FillsTheRelationSideOfBothModels()
    {
        this.Relation.Link("a2", "htmx");

        Article? article = await this.Articles.GetAsync("a2", CancellationToken.None);
        Tag? tag = await this.Tags.GetAsync("htmx", CancellationToken.None);

        Assert.Equal(["htmx"], article?.Tags);
        Assert.Equal(["a2"], tag?.Articles);
    }

    [Fact]
    public async Task AnUnknownTag_IsRefusedAndTheRelationIsUntouched()
    {
        await this.Articles.UpdateAsync("a1", ArticleWith("aot"), CancellationToken.None);

        ScaffoldRefusedException refused = await Assert.ThrowsAsync<ScaffoldRefusedException>(
            () => this.Articles.UpdateAsync("a1", ArticleWith("aot", "nope"), CancellationToken.None).AsTask());

        Assert.Equal("Tags has a choice that does not exist.", refused.Message);
        Assert.Equal(["aot"], this.Relation.TagsOf("a1"));
    }

    [Fact]
    public async Task AnUnknownArticle_IsRefusedFromTheTagSide()
    {
        ScaffoldRefusedException refused = await Assert.ThrowsAsync<ScaffoldRefusedException>(
            () => this.Tags.UpdateAsync("aot", new Tag { Name = "x", Articles = ["a9"] }, CancellationToken.None).AsTask());

        Assert.Equal("Articles has a choice that does not exist.", refused.Message);
        Assert.Empty(this.Relation.ArticlesOf("aot"));
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefused()
    {
        ScaffoldRefusedException refused = await Assert.ThrowsAsync<ScaffoldRefusedException>(
            () => this.Articles.UpdateAsync(
                "a1", new Article { Title = "t", Body = "b", CategoryKey = "nope" }, CancellationToken.None).AsTask());

        Assert.Equal("Category has a choice that does not exist.", refused.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ABlankCategory_IsNone(string? posted)
    {
        await this.Articles.UpdateAsync(
            "a1", new Article { Title = "t", Body = "b", CategoryKey = posted }, CancellationToken.None);

        Article? row = await this.Articles.GetAsync("a1", CancellationToken.None);
        Assert.Null(row?.CategoryKey);
    }

    [Fact]
    public async Task AKnownCategory_IsKept()
    {
        await this.Articles.UpdateAsync(
            "a1", new Article { Title = "t", Body = "b", CategoryKey = "guides" }, CancellationToken.None);

        Article? row = await this.Articles.GetAsync("a1", CancellationToken.None);
        Assert.Equal("guides", row?.CategoryKey);
    }

    [Fact]
    public async Task DeletingACategory_LeavesItsArticlesWithNone()
    {
        await this.Articles.UpdateAsync(
            "a1", new Article { Title = "t", Body = "b", CategoryKey = "guides" }, CancellationToken.None);

        Assert.True(await this.Categories.DeleteAsync("guides", CancellationToken.None));

        Article? row = await this.Articles.GetAsync("a1", CancellationToken.None);
        Assert.Null(row?.CategoryKey);
    }

    [Fact]
    public async Task DeletingATag_RemovesItsLinksOnBothSides()
    {
        await this.Articles.UpdateAsync("a1", ArticleWith("aot", "htmx"), CancellationToken.None);

        Assert.True(await this.Tags.DeleteAsync("aot", CancellationToken.None));

        Assert.Equal(["htmx"], this.Relation.TagsOf("a1"));
        Assert.Empty(this.Relation.ArticlesOf("aot"));
    }

    [Fact]
    public async Task DeletingAnArticle_RemovesItsLinksOnBothSides()
    {
        await this.Articles.UpdateAsync("a1", ArticleWith("aot"), CancellationToken.None);

        Assert.True(await this.Articles.DeleteAsync("a1", CancellationToken.None));

        Assert.Empty(this.Relation.ArticlesOf("aot"));
        Assert.Empty(this.Relation.TagsOf("a1"));
    }

    [Fact]
    public async Task RecreatingADeletedTag_StartsWithNoArticles()
    {
        await this.Articles.UpdateAsync("a1", ArticleWith("aot"), CancellationToken.None);
        await this.Tags.DeleteAsync("aot", CancellationToken.None);

        string key = await this.Tags.CreateAsync(new Tag { Slug = "aot", Name = "Again" }, CancellationToken.None);

        Assert.Equal("aot", key);
        Tag? row = await this.Tags.GetAsync("aot", CancellationToken.None);
        Assert.Empty(row!.Articles);
    }

    [Fact]
    public async Task ATypedTagSlug_IsTheKey_AndABlankOneIsGenerated()
    {
        string typed = await this.Tags.CreateAsync(new Tag { Slug = " c-sharp ", Name = "C#" }, CancellationToken.None);
        string generated = await this.Tags.CreateAsync(new Tag { Name = "Blank" }, CancellationToken.None);

        Assert.Equal("c-sharp", typed);
        Assert.Equal(8, generated.Length);
        Assert.NotNull(await this.Tags.GetAsync("c-sharp", CancellationToken.None));
    }

    [Fact]
    public async Task ATypedTagSlugThatIsTaken_IsRefused()
    {
        ScaffoldRefusedException refused = await Assert.ThrowsAsync<ScaffoldRefusedException>(
            () => this.Tags.CreateAsync(new Tag { Slug = "aot", Name = "Dup" }, CancellationToken.None).AsTask());

        Assert.Equal(Slugs.TakenMessage("aot"), refused.Message);
        Assert.Equal("NativeAOT", (await this.Tags.GetAsync("aot", CancellationToken.None))?.Name);
    }

    [Fact]
    public async Task AStoreOnItsOwn_RefusesEveryKeyButAcceptsTheEmptySet()
    {
        var alone = new ArticleStore();
        alone.Seed(new Article { Id = "x", Title = "x" });

        Assert.True(await alone.UpdateAsync("x", ArticleWith(), CancellationToken.None));
        await Assert.ThrowsAsync<ScaffoldRefusedException>(
            () => alone.UpdateAsync("x", ArticleWith("aot"), CancellationToken.None).AsTask());
    }

    [Fact]
    public void Relation_ReplaceDeduplicatesAndKeepsPostedOrder()
    {
        this.Relation.ReplaceTagsOf("a1", ["htmx", "aot", "htmx"]);

        Assert.Equal(["htmx", "aot"], this.Relation.TagsOf("a1"));
        Assert.Equal(["a1"], this.Relation.ArticlesOf("htmx"));
    }

    [Fact]
    public void Relation_LinkTwiceIsOneLink()
    {
        this.Relation.Link("a1", "aot");
        this.Relation.Link("a1", "aot");

        Assert.Equal(["aot"], this.Relation.TagsOf("a1"));
        Assert.Equal(["a1"], this.Relation.ArticlesOf("aot"));
    }
}
