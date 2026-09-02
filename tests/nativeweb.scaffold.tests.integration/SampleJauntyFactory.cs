using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Npgsql;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// <c>apps/sample</c> with a connection string configured, so
/// <c>IScaffoldStore&lt;Article&gt;</c> resolves to the JauntyQ store instead of
/// the in-memory one.
/// </summary>
/// <remarks>
/// <para>
/// The whole configuration difference is one key. That is the claim these tests
/// exist to check: the generated endpoints, forms and routes are identical either
/// way, and swapping the persistence out is a registration, not a regeneration.
/// </para>
/// <para>
/// Tags are still in-memory in this configuration, which is why
/// <see cref="ScaffoldedCrudTests"/>'s tag facts keep working unchanged.
/// </para>
/// </remarks>
internal sealed class SampleJauntyFactory : WebApplicationFactory<NativeWeb.Sample.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // /signin is Development-only, and it is the only way in.
        builder.UseEnvironment("Development");

        // UseSetting, not ConfigureAppConfiguration. Program.cs reads the connection
        // string off builder.Configuration before Build() runs, and under
        // CreateSlimBuilder the factory's in-memory collection is not reliably there
        // by then -- the same finding NativeWebFactory records at length, reached
        // here by watching four tests pass against the in-memory store while
        // claiming to test PostgreSQL. An environment variable would also work and
        // is what CI uses, but it is process-wide: it would quietly move
        // ScaffoldedCrudTests onto the database too, and those tests assert on
        // seeded rows that only exist in memory.
        builder.UseSetting("ConnectionStrings:Sample", SampleTestDatabase.ConnectionString);
    }

    internal HttpClient CreateFollowingNothing() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A client already carrying the admin session cookie.</summary>
    internal async Task<HttpClient> CreateSignedInAsync()
    {
        HttpClient client = CreateFollowingNothing();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/signin", UriKind.Relative));
        return client;
    }
}

/// <summary>
/// Side-channel access to the articles table, for asserting on what the endpoints
/// actually wrote.
/// </summary>
/// <remarks>
/// <para>
/// Raw Npgsql rather than the generated accessors, for the same reason the web
/// tier's <c>TestDatabase</c> is: a test that reads through the same generated
/// query the code under test writes through cannot tell a mapping bug from a
/// symmetrical pair of them.
/// </para>
/// <para>
/// <b>Nothing here truncates.</b> Every test mints a unique title and asserts only
/// on rows it created, so runs accumulate rows in <c>nativeweb_sample_test</c> and
/// that is fine -- it is a demo table in a database that exists for this suite. A
/// shared TRUNCATE would be the thing that breaks the moment two runs overlap.
/// </para>
/// </remarks>
internal static class SampleTestDatabase
{
    /// <summary>
    /// CI's environment variable wins; the fallback is the local dev database
    /// created from <c>db/sample/migrations/</c>. See db/sample/README.md.
    /// </summary>
    internal static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Sample")
        ?? "Host=localhost;Database=nativeweb_sample_test;Username=nativeweb_app";

    /// <summary>Reads one row's columns straight out of PostgreSQL, or null.</summary>
    internal static async Task<(string Title, string? Author, bool Published, DateTime CreatedAt)?> FindByTitleAsync(
        string title)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT title, author, published, created_at FROM articles WHERE title = @t", connection);
        command.Parameters.AddWithValue("t", title);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetDateTime(3));
    }

    /// <summary>The slug of the one row carrying this title, or null. Spec 022.</summary>
    internal static Task<string?> SlugByTitleAsync(string title) =>
        ScalarStringAsync("SELECT slug FROM articles WHERE title = @p", title);

    /// <summary>A tag's colour by key, or null when there is no such tag. Spec 022.</summary>
    internal static Task<string?> TagColorAsync(string slug) =>
        ScalarStringAsync("SELECT color FROM tags WHERE slug = @p", slug);

    private static async Task<string?> ScalarStringAsync(string sql, string parameter)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p", parameter);

        object? value = await command.ExecuteScalarAsync();
        return value is string text ? text : null;
    }

    /// <summary>How many rows carry this exact title. Used to prove a delete landed.</summary>
    internal static async Task<int> CountByTitleAsync(string title)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM articles WHERE title = @t", connection);
        command.Parameters.AddWithValue("t", title);

        return (int)(long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>Inserts a row directly, for arranging state the endpoints then read.</summary>
    internal static async Task InsertAsync(string id, string title, string? author, string body)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO articles (id, slug, title, author, body, published, view_count, created_at)
            VALUES (@id, @id, @title, @author, @body, TRUE, 0, NOW())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("author", (object?)author ?? DBNull.Value);
        command.Parameters.AddWithValue("body", body);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts a row with <c>published</c> and <c>created_at</c> chosen by the
    /// caller, for arranging a feed the public page then has to order and filter.
    /// </summary>
    /// <remarks>
    /// The overload above fixes both — <c>TRUE</c> and <c>NOW()</c> — which is
    /// right for the admin CRUD facts and useless for a page whose entire job is
    /// deciding which rows those two columns admit.
    /// </remarks>
    internal static async Task InsertAsync(
        string id, string title, string? author, string body, bool published, DateTime createdAt)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO articles (id, slug, title, author, body, published, view_count, created_at)
            VALUES (@id, @id, @title, @author, @body, @published, 0, @created_at)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("author", (object?)author ?? DBNull.Value);
        command.Parameters.AddWithValue("body", body);
        command.Parameters.AddWithValue("published", published);
        command.Parameters.AddWithValue("created_at", createdAt);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Removes exactly the rows one test created, by id prefix.</summary>
    /// <remarks>
    /// <para>
    /// The one place in this suite that deletes, and it is not the TRUNCATE the
    /// class remarks rule out — it names the ids the caller minted and touches
    /// nothing else, so two overlapping runs cannot collide.
    /// </para>
    /// <para>
    /// The public feed is why it has to exist at all. Every other test here
    /// asserts on a row it can find by unique title, at any position, so
    /// accumulated rows are harmless. <c>GET /articles</c> shows the newest
    /// twenty, so a feed test has to date its rows into the future to be sure
    /// they appear — and future-dated rows left behind would push the *next*
    /// run's rows out of the page. Left alone, this suite would break itself
    /// after ten runs.
    /// </para>
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
    /// nothing a running one could still be using.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>finally</c> above is only reached by a process that finishes.
    /// Stryker kills a host it judges hung, and the feed suites date their rows
    /// five years out so they are certain to be on the page — which puts a
    /// leaked row at the top of the public feed for every run after, in both
    /// tiers. Measured 2026-09-01: five such rows, and an e2e feed test failing
    /// on them an hour later.
    /// </para>
    /// <para>
    /// The predicate is the cleanup script's — a feed-suite prefix and a future
    /// date — plus an age gate, because Stryker runs test hosts in parallel
    /// against this one database and a sweep by prefix alone would delete a
    /// sibling's live rows. A feed row's <c>created_at</c> is
    /// <c>UtcNow.AddYears(5)</c> plus at most two minutes, so the mint time is
    /// recoverable from it, and a row minted more than thirty minutes ago cannot
    /// belong to a test that is still running. The cutoff is computed here with
    /// the same <c>AddYears</c> the tests use rather than a PostgreSQL interval,
    /// which can differ from it by a day across a leap year.
    /// </para>
    /// </remarks>
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
