using System.Net;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The generated endpoints over PostgreSQL, with every assertion made through a
/// raw-SQL side channel rather than through the UI that wrote the row.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ScaffoldedCrudTests"/> already proves the endpoints work; it proves
/// it against an in-memory dictionary. What is unproven until here is that the
/// seam holds when the implementation behind it is real: that the SQL in
/// <c>db/sample/queries/Articles/</c> binds the parameters the store thinks it
/// does, in the order it thinks, and that the values survive the round trip
/// through the database's own types.
/// </para>
/// <para>
/// Every test mints a unique title and asserts only on rows carrying it. Nothing
/// truncates and nothing assumes an empty table, because this suite shares one
/// database with every previous run of itself.
/// </para>
/// </remarks>
public sealed class JauntyStoreTests
{
    private static string Unique() => $"jq-{Guid.NewGuid():N}";

    private static async Task<string> BodyAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// The write path, observed from outside both ends: a form post goes in, a
    /// PostgreSQL row comes out, and nothing in between is the code under test.
    /// </summary>
    [Fact]
    public async Task Create_ThroughTheGeneratedForm_WritesARowToPostgres()
    {
        string title = Unique();

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", title),
            new("author", "xunit"),
            new("body", "written through the scaffolded form"),
            new("published", "on"),
            new("viewCount", "7"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        (string Title, string? Author, bool Published, DateTime CreatedAt)? row =
            await SampleTestDatabase.FindByTitleAsync(title);

        Assert.NotNull(row);
        Assert.Equal("xunit", row!.Value.Author);
        Assert.True(row.Value.Published);

        // Stamped by the store, not by the form and not by the column default.
        // Generous window: this asserts "roughly now", not a clock comparison.
        Assert.InRange(row.Value.CreatedAt.ToUniversalTime(), DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
    }

    /// <summary>
    /// The read path, arranged in SQL so the row cannot have come from anything the
    /// app did earlier in this test.
    /// </summary>
    [Fact]
    public async Task List_ShowsARowInsertedDirectlyIntoPostgres()
    {
        string title = Unique();
        await SampleTestDatabase.InsertAsync(Guid.NewGuid().ToString("n")[..8], title, "dba", "arranged in SQL");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string html = await BodyAsync(client, $"/admin/articles?q={title}");

        Assert.Contains(title, html, StringComparison.Ordinal);
        Assert.Contains("dba", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The escaping in <c>JauntyArticleStore.PatternFor</c>, against real ILIKE.
    /// </summary>
    /// <remarks>
    /// The unit tier pins the pattern string; only this tier can say whether
    /// PostgreSQL agrees about what that string means. The decoy is what makes the
    /// test sharp: with the percent sign escaped, a search for "50% off" finds the
    /// literal row and not the decoy. Unescaped, the <c>%</c> becomes a wildcard,
    /// matches the decoy's "X", and both rows come back.
    /// </remarks>
    [Fact]
    public async Task Search_TreatsAPercentSignAsALiteral()
    {
        string token = Unique();
        string literal = $"50% off {token}";
        string decoy = $"50X off {token}";

        await SampleTestDatabase.InsertAsync(Guid.NewGuid().ToString("n")[..8], literal, null, "b");
        await SampleTestDatabase.InsertAsync(Guid.NewGuid().ToString("n")[..8], decoy, null, "b");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string html = await BodyAsync(client, $"/admin/articles?q={Uri.EscapeDataString(literal)}");

        Assert.Contains(literal, html, StringComparison.Ordinal);
        Assert.DoesNotContain(decoy, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// An underscore is ILIKE's single-character wildcard, and the scaffolded search
    /// box is a plain text input that will happily accept one.
    /// </summary>
    [Fact]
    public async Task Search_TreatsAnUnderscoreAsALiteral()
    {
        string token = Unique();
        string literal = $"view_count {token}";
        string decoy = $"viewXcount {token}";

        await SampleTestDatabase.InsertAsync(Guid.NewGuid().ToString("n")[..8], literal, null, "b");
        await SampleTestDatabase.InsertAsync(Guid.NewGuid().ToString("n")[..8], decoy, null, "b");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string html = await BodyAsync(client, $"/admin/articles?q={Uri.EscapeDataString(literal)}");

        Assert.Contains(literal, html, StringComparison.Ordinal);
        Assert.DoesNotContain(decoy, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// An edit leaves the read-only timestamp exactly as it was, byte for byte,
    /// after a round trip through PostgreSQL.
    /// </summary>
    /// <remarks>
    /// Two things keep the timestamp: the generator copies read-only properties off
    /// the stored row when it builds the updated object, and Update.sql never names
    /// the column. This fact guards the second -- break only the generator and this
    /// still passes, because the column is never written either way.
    /// <c>ScaffoldedCrudTests.Update_ChangesTheRowAndPreservesTheReadOnlyStamp</c>
    /// guards the first. Both were confirmed by breaking each in turn.
    ///
    /// What this tier adds over either is the round trip. A
    /// timestamptz read back into a DateTime can come back with a different Kind or
    /// a truncated fraction, and "preserved" would then be false in a way no
    /// in-memory test could see -- Assert.Equal on DateTime compares ticks.
    /// </remarks>
    [Fact]
    public async Task Update_DoesNotOverwriteTheReadOnlyTimestamp()
    {
        string id = Guid.NewGuid().ToString("n")[..8];
        string title = Unique();
        await SampleTestDatabase.InsertAsync(id, title, "before", "b");

        DateTime createdBefore = (await SampleTestDatabase.FindByTitleAsync(title))!.Value.CreatedAt;

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            $"/admin/articles/{id}",
            $"/admin/articles/{id}",
            new("title", title),
            new("author", "after"),
            new("body", "edited"),
            new("viewCount", "0"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        (string Title, string? Author, bool Published, DateTime CreatedAt)? row =
            await SampleTestDatabase.FindByTitleAsync(title);

        Assert.Equal("after", row!.Value.Author);
        Assert.Equal(createdBefore, row.Value.CreatedAt);
    }

    /// <summary>
    /// Both halves of the delete: the endpoint reports success, and the row is gone
    /// from the table rather than merely absent from a rendered page.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheRowFromPostgres()
    {
        string id = Guid.NewGuid().ToString("n")[..8];
        string title = Unique();
        await SampleTestDatabase.InsertAsync(id, title, null, "b");

        Assert.Equal(1, await SampleTestDatabase.CountByTitleAsync(title));

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles",
            $"/admin/articles/{id}/delete");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(0, await SampleTestDatabase.CountByTitleAsync(title));
    }

    /// <summary>
    /// The seam's own claim, stated as a test: swapping the store changes nothing
    /// above it. Both models read from PostgreSQL in this configuration (tags since
    /// spec 022, seeded by migration 0003), and their generated endpoints behave
    /// exactly as they do under <see cref="SampleFactory"/>.
    /// </summary>
    [Fact]
    public async Task TwoModelsWithDifferentStoresBothWorkThroughTheSameSeam()
    {
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        // Searched for rather than read off the first page: tags other tests
        // create outrank a three-letter key once enough of them accumulate.
        string tags = await BodyAsync(client, "/admin/tags?q=NativeAOT");
        Assert.Contains("NativeAOT", tags, StringComparison.Ordinal);

        // And the articles list renders from PostgreSQL in the same request pipeline.
        using HttpResponseMessage articles = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, articles.StatusCode);
    }
}
