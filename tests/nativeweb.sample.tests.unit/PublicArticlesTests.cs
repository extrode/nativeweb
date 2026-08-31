using NativeWeb.Render;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The two pure halves of <c>GET /articles</c>: the in-memory read model and the
/// compiled templates. Neither needs a database, a host or a browser.
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

    // ---- rendering, through the compiled templates ----
    //
    // These four assert the template output, not ArticlesPage.Render, since
    // 2026-08-31. They were written against the hand-built StringBuilder renderer
    // and kept passing after the endpoints stopped calling it, which is the exact
    // shape of a test that asserts nothing about what ships: the production path
    // could have rendered anything at all and the suite stayed green. Retargeted
    // rather than deleted, because every fact in them is still a fact worth
    // holding -- the encoding one especially.

    /// <summary>
    /// The empty feed is a normal state, and saying so beats an empty list, which
    /// reads as a broken page.
    /// </summary>
    [Fact]
    public void Results_WithNoArticles_SaysSoRatherThanRenderingAnEmptyList()
    {
        string html = Templates.Articles.Results.ToHtmlString(locale: Locales.En, Articles: []);

        Assert.Contains("No published articles yet.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<ul", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Results_WithAnArticle_ShowsTitleAuthorAndDate()
    {
        string html = Templates.Articles.Results.ToHtmlString(
            locale: Locales.En,
            Articles: [new PublishedArticle("a1", "Lean Native Web", "Sy", Utc(4))]);

        Assert.Contains("Lean Native Web", html, StringComparison.Ordinal);
        Assert.Contains("Sy", html, StringComparison.Ordinal);
        Assert.Contains("""<time class="articles__date" datetime="2026-01-04">""", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Results_WithNoAuthor_OmitsTheAuthorElementEntirely()
    {
        // Rather than rendering an empty span, which shows as stray punctuation or
        // a stray gap depending on the stylesheet.
        string html = Templates.Articles.Results.ToHtmlString(
            locale: Locales.En,
            Articles: [new PublishedArticle("a1", "Draft-less", null, Utc(4))]);

        Assert.DoesNotContain("articles__author", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The security fact, and the reason these tests survived the retarget. The
    /// title and the author come from a database an admin form writes to, so they
    /// are attacker-influenced input on a public page. The hand-written renderer
    /// called HtmlEncoder explicitly; the template compiler encodes every
    /// interpolation by default, and this asserts the default is real rather than
    /// assumed.
    /// </summary>
    [Fact]
    public void Results_EncodesTitleAndAuthor()
    {
        string html = Templates.Articles.Results.ToHtmlString(
            locale: Locales.En,
            Articles: [new PublishedArticle("a1", "<script>alert(1)</script>", "\"><img src=x>", Utc(4))]);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The full page carries the chrome and the filter form; the fragment carries
    /// neither. A swap that returned the whole document would nest a second
    /// &lt;html&gt; inside the page, which browsers paper over and nobody notices
    /// until the second swap.
    /// </summary>
    [Fact]
    public void IndexIsAWholeDocumentAndResultsIsAFragment()
    {
        string page = Templates.Articles.Index.ToHtmlString(
            locale: Locales.En,
            Title: "Articles",
            Query: "",
            AntiforgeryFieldName: "__RequestVerificationToken",
            AntiforgeryToken: "token",
            Articles: []);
        string fragment = Templates.Articles.Results.ToHtmlString(locale: Locales.En, Articles: []);

        Assert.Contains("<!DOCTYPE html>", page, StringComparison.Ordinal);
        Assert.Contains("articles__filter", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("articles__filter", fragment, StringComparison.Ordinal);
        // The swap target's id has to be on the fragment's own root, or
        // hx-swap="outerHTML" replaces the element and loses it on the next swap.
        Assert.Contains("id=\"results\"", fragment, StringComparison.Ordinal);
    }

    /// <summary>
    /// No token means no form: one that cannot be submitted is worse than none.
    /// </summary>
    [Fact]
    public void IndexWithoutATokenRendersNoForm()
    {
        string page = Templates.Articles.Index.ToHtmlString(
            locale: Locales.En,
            Title: "Articles",
            Query: "",
            AntiforgeryFieldName: "",
            AntiforgeryToken: "",
            Articles: []);

        Assert.DoesNotContain("<form", page, StringComparison.Ordinal);
    }
}
