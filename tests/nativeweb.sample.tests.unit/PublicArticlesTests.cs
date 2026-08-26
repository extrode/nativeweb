using NativeWeb.Sample.Render;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The two pure halves of <c>GET /articles</c>: the in-memory read model and the
/// renderer. Neither needs a database, a host or a browser.
/// </summary>
/// <remarks>
/// <para>
/// This is the tier the tutorial in <c>docs/00-quick-start/tutorial-first-feature.md</c> writes
/// first, and it is deliberately the one that carries the most facts. The
/// integration tier can only say the page came back 200 with the right words on
/// it; only here can the ordering rule and the encoding rule be stated one at a
/// time, so a failure names the rule that broke rather than the page that did.
/// </para>
/// <para>
/// <see cref="JauntyPublishedArticles"/> is absent on purpose. Its whole body is
/// one generated call and a row-to-record loop, so a unit test of it would be a
/// test of a mock; what it actually does is asserted against real PostgreSQL in
/// <c>tests/nativeweb.scaffold.tests.integration</c>.
/// </para>
/// </remarks>
public sealed class PublicArticlesTests
{
    private static ArticleStore StoreWith(params Article[] articles)
    {
        var store = new ArticleStore();
        foreach (Article article in articles)
        {
            store.Seed(article);
        }

        return store;
    }

    private static Article ArticleAt(string id, DateTime created, bool published = true, string? author = "Sy") =>
        new()
        {
            Id = id,
            Title = $"title-{id}",
            Author = author,
            Body = "body",
            Published = published,
            CreatedAt = created,
        };

    private static DateTime Utc(int day) => new(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RecentAsync_OmitsUnpublishedArticles()
    {
        // The one rule the page cannot be wrong about: a draft is not public.
        ArticleStore store = StoreWith(
            ArticleAt("a1", Utc(1)),
            ArticleAt("a2", Utc(2), published: false));

        IReadOnlyList<PublishedArticle> recent =
            await new MemoryPublishedArticles(store).RecentAsync(10, CancellationToken.None);

        Assert.Equal(["a1"], recent.Select(a => a.Id));
    }

    [Fact]
    public async Task RecentAsync_OrdersNewestFirst()
    {
        // Seeded out of order, so passing cannot be an accident of insertion order.
        ArticleStore store = StoreWith(
            ArticleAt("a1", Utc(2)),
            ArticleAt("a2", Utc(3)),
            ArticleAt("a3", Utc(1)));

        IReadOnlyList<PublishedArticle> recent =
            await new MemoryPublishedArticles(store).RecentAsync(10, CancellationToken.None);

        Assert.Equal(["a2", "a1", "a3"], recent.Select(a => a.Id));
    }

    /// <summary>
    /// The tiebreak, which is the fact the SQL side spells
    /// <c>ORDER BY created_at DESC, id</c> for.
    /// </summary>
    /// <remarks>
    /// <c>created_at</c> defaults to <c>NOW()</c>, so a seeded or scripted insert
    /// can give several rows the same timestamp. Without the second key the page
    /// order is whatever the store happens to enumerate, which is stable enough to
    /// look fine locally and unstable enough to reorder between requests in
    /// production.
    /// </remarks>
    [Fact]
    public async Task RecentAsync_WithIdenticalTimestamps_BreaksTheTieById()
    {
        DateTime same = Utc(5);
        ArticleStore store = StoreWith(
            ArticleAt("c", same),
            ArticleAt("a", same),
            ArticleAt("b", same));

        IReadOnlyList<PublishedArticle> recent =
            await new MemoryPublishedArticles(store).RecentAsync(10, CancellationToken.None);

        Assert.Equal(["a", "b", "c"], recent.Select(a => a.Id));
    }

    [Fact]
    public async Task RecentAsync_TakesAtMostTheRequestedCount_KeepingTheNewest()
    {
        ArticleStore store = StoreWith(
            ArticleAt("a1", Utc(1)),
            ArticleAt("a2", Utc(2)),
            ArticleAt("a3", Utc(3)));

        IReadOnlyList<PublishedArticle> recent =
            await new MemoryPublishedArticles(store).RecentAsync(2, CancellationToken.None);

        // Truncation happens after the sort, not before: asking for two must give
        // the two newest, not two arbitrary rows.
        Assert.Equal(["a3", "a2"], recent.Select(a => a.Id));
    }

    [Fact]
    public void Render_WithNoArticles_SaysSoRatherThanRenderingAnEmptyList()
    {
        string html = ArticlesPage.Render([]);

        Assert.Contains("No published articles yet.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<ul", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithAnArticle_ShowsTitleAuthorAndDate()
    {
        string html = ArticlesPage.Render([
            new PublishedArticle("a1", "Lean Native Web", "Sy", Utc(4)),
        ]);

        Assert.Contains("Lean Native Web", html, StringComparison.Ordinal);
        Assert.Contains("Sy", html, StringComparison.Ordinal);
        Assert.Contains("""<time class="articles__date" datetime="2026-01-04">""", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithNoAuthor_OmitsTheAuthorElementEntirely()
    {
        // Rather than rendering an empty span, which shows as stray punctuation or
        // a stray gap depending on the stylesheet.
        string html = ArticlesPage.Render([
            new PublishedArticle("a1", "Draft-less", null, Utc(4)),
        ]);

        Assert.DoesNotContain("articles__author", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The security fact. The title and the author come from a database an admin
    /// form writes to, so they are attacker-influenced input on a public page.
    /// </summary>
    [Fact]
    public void Render_EncodesTitleAndAuthor()
    {
        string html = ArticlesPage.Render([
            new PublishedArticle("a1", "<script>alert(1)</script>", "\"><img src=x>", Utc(4)),
        ]);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }
}
