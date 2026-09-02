using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

public sealed class ArticleStoreSlugTests
{
    private static Article Draft(string title, string? slug = null) =>
        new() { Title = title, Slug = slug, Body = "Body." };

    [Fact]
    public async Task Create_WithABlankSlug_DerivesItFromTheTitle()
    {
        var store = new ArticleStore();

        string id = await store.CreateAsync(Draft("Hello, World! (Again)"), CancellationToken.None);

        Article? saved = await store.GetAsync(id, CancellationToken.None);
        Assert.Equal("hello-world-again", saved?.Slug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_TreatsWhitespaceAsBlank(string? slug)
    {
        var store = new ArticleStore();

        string id = await store.CreateAsync(Draft("Whitespace", slug), CancellationToken.None);

        Assert.Equal("whitespace", (await store.GetAsync(id, CancellationToken.None))?.Slug);
    }

    [Fact]
    public async Task Create_WithTheSameTitleTwice_SuffixesTheSecond()
    {
        var store = new ArticleStore();

        await store.CreateAsync(Draft("Same title"), CancellationToken.None);
        string second = await store.CreateAsync(Draft("Same title"), CancellationToken.None);
        string third = await store.CreateAsync(Draft("Same title"), CancellationToken.None);

        Assert.Equal("same-title-2", (await store.GetAsync(second, CancellationToken.None))?.Slug);
        Assert.Equal("same-title-3", (await store.GetAsync(third, CancellationToken.None))?.Slug);
    }

    [Fact]
    public async Task Create_WithATypedSlug_KeepsItAndTrimsIt()
    {
        var store = new ArticleStore();

        string id = await store.CreateAsync(Draft("Anything", "  chosen-address "), CancellationToken.None);

        Assert.Equal("chosen-address", (await store.GetAsync(id, CancellationToken.None))?.Slug);
    }

    [Theory]
    [InlineData("Not A Slug")]
    [InlineData("under_score")]
    [InlineData("trailing-")]
    public async Task Create_WithAnInvalidSlug_IsRefusedAndSavesNothing(string slug)
    {
        var store = new ArticleStore();

        ScaffoldRefusedException refused = await Assert.ThrowsAsync<ScaffoldRefusedException>(
            async () => await store.CreateAsync(Draft("Anything", slug), CancellationToken.None));

        Assert.Equal(Slugs.InvalidMessage, refused.Message);
        Assert.Equal(0, await store.CountAsync(null, [], CancellationToken.None));
    }

    [Fact]
    public async Task Create_WithASlugAnotherArticleHolds_IsRefusedAndSavesNothing()
    {
        var store = new ArticleStore();
        await store.CreateAsync(Draft("First", "taken"), CancellationToken.None);

        ScaffoldRefusedException refused = await Assert.ThrowsAsync<ScaffoldRefusedException>(
            async () => await store.CreateAsync(Draft("Second", "taken"), CancellationToken.None));

        Assert.Equal("Slug 'taken' is already used.", refused.Message);
        Assert.Equal(1, await store.CountAsync(null, [], CancellationToken.None));
    }

    [Fact]
    public async Task Update_WithANewTitleAndABlankSlug_KeepsTheSlug()
    {
        var store = new ArticleStore();
        string id = await store.CreateAsync(Draft("Original title"), CancellationToken.None);

        Assert.True(await store.UpdateAsync(id, Draft("Edited title"), CancellationToken.None));

        Article? saved = await store.GetAsync(id, CancellationToken.None);
        Assert.Equal("Edited title", saved?.Title);
        Assert.Equal("original-title", saved?.Slug);
    }

    [Fact]
    public async Task Update_WithATypedSlug_ChangesIt()
    {
        var store = new ArticleStore();
        string id = await store.CreateAsync(Draft("Original title"), CancellationToken.None);

        Assert.True(await store.UpdateAsync(id, Draft("Original title", "new-address"), CancellationToken.None));

        Assert.Equal("new-address", (await store.GetAsync(id, CancellationToken.None))?.Slug);
    }

    [Fact]
    public async Task Update_MayKeepItsOwnSlugTyped()
    {
        var store = new ArticleStore();
        string id = await store.CreateAsync(Draft("Mine", "mine"), CancellationToken.None);

        Assert.True(await store.UpdateAsync(id, Draft("Mine, edited", "mine"), CancellationToken.None));
    }

    [Fact]
    public async Task Update_ToASlugAnotherArticleHolds_IsRefusedAndChangesNothing()
    {
        var store = new ArticleStore();
        await store.CreateAsync(Draft("First", "first"), CancellationToken.None);
        string second = await store.CreateAsync(Draft("Second", "second"), CancellationToken.None);

        ScaffoldRefusedException refused = await Assert.ThrowsAsync<ScaffoldRefusedException>(
            async () => await store.UpdateAsync(second, Draft("Second, edited", "first"), CancellationToken.None));

        Assert.Equal("Slug 'first' is already used.", refused.Message);
        Article? unchanged = await store.GetAsync(second, CancellationToken.None);
        Assert.Equal("Second", unchanged?.Title);
        Assert.Equal("second", unchanged?.Slug);
    }

    [Fact]
    public async Task Update_OfARowThatIsGone_IsFalseWithoutRunningTheRules()
    {
        var store = new ArticleStore();

        Assert.False(await store.UpdateAsync("missing", Draft("Anything", "Not A Slug"), CancellationToken.None));
    }

    [Fact]
    public async Task FiftyConcurrentCreatesOfOneTitle_GetFiftyDistinctSlugs()
    {
        var store = new ArticleStore();

        string[] ids = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () => await store.CreateAsync(Draft("Same title"), CancellationToken.None))));

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            Article? saved = await store.GetAsync(id, CancellationToken.None);
            Assert.True(slugs.Add(saved!.Slug!), $"duplicate slug {saved.Slug}");
        }

        Assert.Equal(50, slugs.Count);
    }
}
