using System.Net;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// <c>GET /articles</c> through a real host, on both stores.
/// </summary>
/// <remarks>
/// <para>
/// The unit tier already states the ordering and encoding rules one at a time.
/// What is unproven until here is that the route exists, that it needs no
/// sign-in, and — for the JauntyQ half — that
/// <c>db/sample/queries/Articles/ListPublished.sql</c> binds its parameter and
/// applies its <c>WHERE</c> and <c>ORDER BY</c> the way the C# above it assumes.
/// A LINQ expression and a SQL statement can agree in the unit tier and disagree
/// against PostgreSQL, which is the whole reason this page has two
/// implementations and this file has two halves.
/// </para>
/// <para>
/// The JauntyQ half dates its rows into the future deliberately. The page shows
/// the twenty newest and <c>nativeweb_sample_test</c> accumulates rows across
/// runs, so a row dated <c>NOW()</c> is only reliably on the page while the suite
/// is young. Each test removes its own rows afterwards — see
/// <see cref="SampleTestDatabase.DeleteByIdPrefixAsync"/> for why that is not the
/// shared TRUNCATE this suite refuses to have.
/// </para>
/// </remarks>
public sealed class PublicFeedTests : IClassFixture<LeakedFeedRowsSweep>
{
    private static async Task<string> FeedAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri("/articles", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // --- the in-memory store, over the seeded demo rows -------------------------

    /// <summary>
    /// The route is open. Every scaffolded route on <see cref="NativeWeb.Sample.Article"/>
    /// redirects an anonymous visitor to <c>/signin</c>; this one must not, and
    /// nothing but a request proves which side of that line it landed on.
    /// </summary>
    [Fact]
    public async Task Feed_IsReachableWithoutSigningIn()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/articles", UriKind.Relative));

        // Not merely "not a redirect": a 200 with the page on it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The class arrived with the compiled layout on 2026-08-31; the assertion is
        // still "the page rendered its heading", not "the markup is byte-for-byte
        // what it was".
        Assert.Contains(
            """<h1 class="page__title">Articles</h1>""",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feed_InMemory_ShowsPublishedRowsAndHidesDrafts()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        string html = await FeedAsync(client);

        // Seed.Fill: a1 and a2 are published, a3 is not.
        Assert.Contains("Lean Native Web", html, StringComparison.Ordinal);
        Assert.Contains("Binding forms under NativeAOT", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Scaffolding without reflection", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feed_InMemory_OrdersNewestFirst()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        string html = await FeedAsync(client);

        // a2 is 2026-06-18, a1 is 2026-03-04.
        Assert.True(
            html.IndexOf("Binding forms under NativeAOT", StringComparison.Ordinal)
                < html.IndexOf("Lean Native Web", StringComparison.Ordinal),
            "The June article should render above the March one.");
    }

    // --- the JauntyQ store, over real PostgreSQL --------------------------------

    /// <summary>
    /// The claim the SQL exists to make: <c>WHERE published</c> is applied by
    /// PostgreSQL, not by the C# reading its output.
    /// </summary>
    [Fact]
    public async Task Feed_OverPostgres_ShowsPublishedRowsAndHidesDrafts()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-p", $"{prefix}-published", "xunit", "body", published: true, createdAt: future);
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-d", $"{prefix}-draft", "xunit", "body", published: false, createdAt: future.AddMinutes(1));

            using var factory = new SampleJauntyFactory();
            using HttpClient client = factory.CreateFollowingNothing();

            string html = await FeedAsync(client);

            Assert.Contains($"{prefix}-published", html, StringComparison.Ordinal);

            // The draft is NEWER than the published row, so if the filter were
            // missing it would be the first thing on the page rather than absent.
            Assert.DoesNotContain($"{prefix}-draft", html, StringComparison.Ordinal);
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }

    /// <summary>
    /// <c>ORDER BY created_at DESC</c> as PostgreSQL applies it. This is the fact
    /// the query carries <c>-- @allow-sort</c> for: there is no index behind this
    /// ordering, so it is a runtime sort, and a runtime sort that silently stopped
    /// happening would look exactly like a page.
    /// </summary>
    [Fact]
    public async Task Feed_OverPostgres_OrdersNewestFirst()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            // Inserted oldest-first, so the page order cannot be insertion order.
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-1", $"{prefix}-older", "xunit", "body", published: true, createdAt: future);
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-2", $"{prefix}-newer", "xunit", "body", published: true, createdAt: future.AddMinutes(2));

            using var factory = new SampleJauntyFactory();
            using HttpClient client = factory.CreateFollowingNothing();

            string html = await FeedAsync(client);

            int newer = html.IndexOf($"{prefix}-newer", StringComparison.Ordinal);
            int older = html.IndexOf($"{prefix}-older", StringComparison.Ordinal);

            Assert.True(newer >= 0 && older >= 0, "Both rows should be on the page.");
            Assert.True(newer < older, "The newer row should render above the older one.");
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }

    /// <summary>
    /// A null author survives the round trip through PostgreSQL and renders as an
    /// omission rather than as "null" or an exception.
    /// </summary>
    /// <remarks>
    /// <c>author</c> is the one nullable column the read model carries, so it is
    /// the one place a <c>GetString</c> on a DBNull would throw. The unit tier
    /// asserts the renderer's half of this; only here is the value actually null
    /// in a database rather than null in a record the test constructed.
    /// </remarks>
    [Fact]
    public async Task Feed_OverPostgres_RendersARowWithNoAuthor()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];

        try
        {
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-n", $"{prefix}-anon", null, "body", published: true,
                createdAt: DateTime.UtcNow.AddYears(5));

            using var factory = new SampleJauntyFactory();
            using HttpClient client = factory.CreateFollowingNothing();

            string html = await FeedAsync(client);

            Assert.Contains($"{prefix}-anon", html, StringComparison.Ordinal);
            Assert.DoesNotContain("null", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }

    /// <summary>
    /// The encoding fact, with the payload coming out of PostgreSQL rather than
    /// out of a literal in the test.
    /// </summary>
    [Fact]
    public async Task Feed_OverPostgres_EncodesATitleFromTheDatabase()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];

        try
        {
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-x", $"{prefix}<script>alert(1)</script>", "xunit", "body", published: true,
                createdAt: DateTime.UtcNow.AddYears(5));

            using var factory = new SampleJauntyFactory();
            using HttpClient client = factory.CreateFollowingNothing();

            string html = await FeedAsync(client);

            Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }
}
