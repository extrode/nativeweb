using Microsoft.Playwright;

using Npgsql;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// The scaffolded admin UI driven by a browser, against a NativeAOT binary, over
/// PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Three things are true here that are true nowhere else in the repo, and each of
/// them is a way this could fail in production while every other tier stayed
/// green:
/// </para>
/// <list type="number">
/// <item>JauntyQ-generated data access running under NativeAOT rather than a
/// JIT-compiled test host, so anything the trimmer removes shows up here.</item>
/// <item>The confirm dialog on the delete form, which no HttpClient can raise, in
/// front of a delete that reaches a real table.</item>
/// <item>A row's whole journey from a keystroke to a committed row, asserted by
/// reading the table directly rather than by re-rendering the page that wrote it.</item>
/// </list>
/// <para>
/// The store discipline from <see cref="ScaffoldE2eFixture"/> applies doubly:
/// this database outlives the run.
/// </para>
/// </remarks>
[Collection(ScaffoldE2eFixture.Name)]
public sealed class JauntyBackedAdminTests(JauntySampleFixture app, BrowserFixture browsers)
{
    private const string ListPath = "/admin/articles";

    private async Task<IPage> SignedInPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl });

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/signin");
        return page;
    }

    private static async Task<string> CreateArticleAsync(IPage page)
    {
        string title = $"e2e-jq-{Guid.NewGuid():N}";

        await page.GotoAsync($"{ListPath}/new");
        await page.FillAsync("input[name=title]", title);
        await page.FillAsync("input[name=author]", "playwright");
        await page.FillAsync("textarea[name=body]", "Created through the generated form.");
        await page.ClickAsync("form.nws-form button[type=submit]");

        // Create redirects to the key the store returned, so the browser is now on
        // the new row's page. Asserted here rather than in each caller, and then the
        // browser goes back to the list the callers expect.
        Assert.Matches($"{ListPath}/[^/]+$", new Uri(page.Url).AbsolutePath);

        await page.GotoAsync(ListPath);

        return title;
    }

    /// <summary>
    /// Puts the page on a list filtered to exactly one row: this test's own.
    /// </summary>
    /// <remarks>
    /// The in-memory tier can click straight through from a create, because its
    /// store starts nearly empty and the new row is on the first page. This table is
    /// shared with the integration tier and accumulates across runs, and the
    /// scaffolded list is paged and ordered by key -- so a freshly created row with
    /// a random key is somewhere in the middle of page eleven. Filtering first is
    /// also what a person would do, which is the better reason.
    /// </remarks>
    private static async Task ShowOnlyAsync(IPage page, string title) =>
        await page.GotoAsync($"{ListPath}?q={Uri.EscapeDataString(title)}");

    private static ILocator DeleteButtonFor(IPage page, string title) =>
        page.Locator("table.nws-table tbody tr")
            .Filter(new LocatorFilterOptions { HasText = title })
            .Locator("button.nws-btn--danger");

    /// <summary>
    /// A row typed into a browser ends up in PostgreSQL, with the values the form
    /// carried and the timestamp the store stamped.
    /// </summary>
    [Fact]
    public async Task Create_FromTheBrowser_LandsInPostgres()
    {
        IPage page = await SignedInPageAsync();
        string title = await CreateArticleAsync(page);

        Assert.EndsWith(ListPath, page.Url, StringComparison.Ordinal);

        (string? Author, DateTime CreatedAt)? row = await SampleE2eDatabase.FindByTitleAsync(title);

        Assert.NotNull(row);
        Assert.Equal("playwright", row!.Value.Author);
        Assert.InRange(
            row.Value.CreatedAt.ToUniversalTime(),
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddMinutes(5));
    }

    /// <summary>
    /// The confirm dialog in front of a real DELETE. Dismissing it must leave the
    /// row in the table, not merely on the page.
    /// </summary>
    /// <remarks>
    /// This is the fact the whole tier is built around, stated against persistent
    /// storage. Its in-memory twin can only say a dictionary entry survived; this
    /// one says nothing was committed, which is the claim a user cancelling a delete
    /// is actually relying on.
    /// </remarks>
    [Fact]
    public async Task Delete_WhenTheConfirmDialogIsDismissed_CommitsNothing()
    {
        IPage page = await SignedInPageAsync();
        string title = await CreateArticleAsync(page);

        bool asked = false;
        page.Dialog += async (_, dialog) =>
        {
            asked = true;
            Assert.Equal("Delete this record?", dialog.Message);
            await dialog.DismissAsync();
        };

        await ShowOnlyAsync(page, title);
        await DeleteButtonFor(page, title).ClickAsync();

        Assert.True(asked, "The delete form did not raise a confirm dialog at all.");
        Assert.Equal(1, await SampleE2eDatabase.CountByTitleAsync(title));
    }

    /// <summary>
    /// The other half: accepting the dialog removes the row from the table.
    /// </summary>
    [Fact]
    public async Task Delete_WhenTheConfirmDialogIsAccepted_RemovesTheRowFromPostgres()
    {
        IPage page = await SignedInPageAsync();
        string title = await CreateArticleAsync(page);

        Assert.Equal(1, await SampleE2eDatabase.CountByTitleAsync(title));

        bool asked = false;
        page.Dialog += async (_, dialog) =>
        {
            asked = true;
            await dialog.AcceptAsync();
        };

        await ShowOnlyAsync(page, title);
        await DeleteButtonFor(page, title).ClickAsync();

        Assert.True(asked, "The delete form did not raise a confirm dialog at all.");
        Assert.Equal(0, await SampleE2eDatabase.CountByTitleAsync(title));
    }

    /// <summary>
    /// The edit form round-trips through a real UPDATE, and the read-only timestamp
    /// survives it unchanged.
    /// </summary>
    /// <remarks>
    /// Two independent defences keep it, and this fact guards the second one:
    /// Update.sql's SET list, which never names the column. Adding
    /// <c>created_at = NOW()</c> there fails this test and only this test, which is
    /// how that was confirmed. See the comment in that file for the other.
    /// </remarks>
    [Fact]
    public async Task Edit_FromTheBrowser_UpdatesTheRowAndLeavesCreatedAtAlone()
    {
        IPage page = await SignedInPageAsync();
        string title = await CreateArticleAsync(page);

        DateTime createdBefore = (await SampleE2eDatabase.FindByTitleAsync(title))!.Value.CreatedAt;

        await ShowOnlyAsync(page, title);
        await page.Locator("table.nws-table tbody tr")
            .Filter(new LocatorFilterOptions { HasText = title })
            .Locator("a.nws-btn--sm")
            .First.ClickAsync();

        await page.FillAsync("input[name=author]", "edited-in-a-browser");
        await page.ClickAsync("form.nws-form button[type=submit]");

        (string? Author, DateTime CreatedAt)? row = await SampleE2eDatabase.FindByTitleAsync(title);

        Assert.Equal("edited-in-a-browser", row!.Value.Author);
        Assert.Equal(createdBefore, row.Value.CreatedAt);
    }

    /// <summary>
    /// Search submitted from the browser, filtering rows that live in PostgreSQL.
    /// </summary>
    /// <remarks>
    /// The search term is a full unique title, so this asserts on rows this test
    /// created and cannot be thrown off by anything a previous run left behind.
    /// </remarks>
    [Fact]
    public async Task Search_FiltersRowsHeldInPostgres()
    {
        IPage page = await SignedInPageAsync();
        string title = await CreateArticleAsync(page);
        string other = await CreateArticleAsync(page);

        await page.FillAsync("input[name=q]", title);
        await page.ClickAsync("form.nws-search button[type=submit]");

        Assert.Contains("q=", page.Url, StringComparison.Ordinal);
        await Assertions.Expect(page.GetByText(title).First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText(other)).ToHaveCountAsync(0);
    }
}

/// <summary>
/// Raw Npgsql access to the articles table, for asserting on what the browser
/// caused to be committed.
/// </summary>
/// <remarks>
/// Nothing here truncates, and nothing assumes an empty table: every test above
/// mints a unique title. The database is shared with the integration tier, which
/// keeps the same discipline for the same reason.
/// </remarks>
internal static class SampleE2eDatabase
{
    internal static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Sample")
        ?? "Host=localhost;Database=nativeweb_sample_test;Username=nativeweb_app";

    internal static async Task<(string? Author, DateTime CreatedAt)?> FindByTitleAsync(string title)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT author, created_at FROM articles WHERE title = @t", connection);
        command.Parameters.AddWithValue("t", title);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetDateTime(1));
    }

    internal static async Task<int> CountByTitleAsync(string title)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM articles WHERE title = @t", connection);
        command.Parameters.AddWithValue("t", title);

        return (int)(long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>
    /// Inserts a row with <c>published</c> and <c>created_at</c> chosen by the
    /// caller, for arranging a public feed the browser then has to see.
    /// </summary>
    /// <remarks>
    /// The admin tests above never need this: they create rows through the form,
    /// which is the thing under test. <see cref="PublicFeedTests"/> is asserting
    /// on a read-only page, so its rows have to arrive some other way.
    /// </remarks>
    internal static async Task InsertAsync(string id, string title, bool published, DateTime createdAt)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO articles (id, slug, title, author, body, published, view_count, created_at)
            VALUES (@id, @id, @title, 'playwright', 'body', @published, 0, @created_at)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("published", published);
        command.Parameters.AddWithValue("created_at", createdAt);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Removes exactly the rows one test created, by id prefix.</summary>
    /// <remarks>
    /// The exception to "nothing here truncates", and for the same reason the
    /// integration tier has one: the public feed shows the newest twenty, so its
    /// tests date rows into the future to be certain they are on the page, and a
    /// future-dated row left behind would displace the next run's.
    /// </remarks>
    internal static async Task DeleteByIdPrefixAsync(string idPrefix)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM articles WHERE id LIKE @p || '%'", connection);
        command.Parameters.AddWithValue("p", idPrefix);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Removes the future-dated feed rows a killed test host left behind, and
    /// nothing a running one could still be using. The same predicate and the
    /// same reasoning as the integration tier's
    /// <c>SampleTestDatabase.SweepLeakedFeedRowsAsync</c>: the database is
    /// shared, so either tier's leak lands on the other's feed page, and a row
    /// minted more than thirty minutes ago cannot belong to a live test.
    /// </summary>
    internal static async Task SweepLeakedFeedRowsAsync()
    {
        DateTime cutoff = DateTime.UtcNow.AddYears(5).AddMinutes(-30);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            DELETE FROM articles
            WHERE (id LIKE 'feed-%' OR id LIKE 'e2e-%')
              AND created_at > now()
              AND created_at < @cutoff
            """,
            connection);
        command.Parameters.AddWithValue("cutoff", cutoff);

        await command.ExecuteNonQueryAsync();
    }
}
