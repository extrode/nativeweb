using JauntyQ.Generated;

using Npgsql;

using NativeWeb.Scaffold;

// The generated row type for a whole-table projection is named after the table,
// so JauntyQ.Generated.Article and NativeWeb.Sample.Article are two different
// types with one name. C# would resolve the bare name to this file's own
// namespace and compile either way, which is exactly the kind of quiet
// resolution that makes a later reader mistrust every mapping below. Aliased so
// each side of the mapping says which type it is.
using Row = JauntyQ.Generated.Article;

namespace NativeWeb.Sample;

/// <summary>
/// The same six methods as <see cref="MemoryStore{T}"/>, satisfied with JauntyQ
/// queries against PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// This class is the point of the seam. Nothing above it changes: no generated
/// endpoint, no emitted form, no route, no view. <c>Program.cs</c> registers this
/// instead of <see cref="ArticleStore"/> when a connection string is configured,
/// and the admin UI cannot tell the difference. The scaffolding generator never
/// learns that a database exists.
/// </para>
/// <para>
/// Every method opens its own connection from the shared
/// <see cref="NpgsqlDataSource"/> and disposes it before returning, which is why
/// this can be a singleton: it holds no per-request state, and the pool -- not
/// the DI lifetime -- is what bounds connections.
/// </para>
/// </remarks>
internal sealed class JauntyArticleStore(NpgsqlDataSource dataSource) : IScaffoldStore<Article>
{
    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="searchFields"/> arrives carrying Title, Author and Body --
    /// the model's <c>[ScaffoldSearch]</c> set -- and this implementation does not
    /// read it, because the columns are named in the SQL: <c>List.sql</c>'s WHERE
    /// clause is <c>title ILIKE ... OR COALESCE(author, '') ILIKE ... OR body
    /// ILIKE ...</c>, and JauntyQ compiles that text rather than composing it. The
    /// parameter is still the right shape for the seam: a store that builds its
    /// predicate at runtime needs the list, and one whose query is a fixed
    /// statement has the same fact in the .sql file. Adding a fourth
    /// <c>[ScaffoldSearch]</c> to the model means editing List.sql and Count.sql to
    /// match; nothing here can do that for you.
    /// </remarks>
    public async ValueTask<IReadOnlyList<Article>> ListAsync(string? search, IReadOnlyList<string> searchFields, int skip, int take, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<Row> rows = await db.Articles.ListAsync(PatternFor(search), take, skip, ct).ConfigureAwait(false);

        var articles = new List<Article>(rows.Count);
        foreach (Row row in rows)
        {
            articles.Add(new Article
            {
                Id = row.Id,
                Title = row.Title,
                Author = row.Author,
                Body = row.Body,
                Published = row.Published,
                ViewCount = row.ViewCount,
                CreatedAt = row.CreatedAt,
            });
        }

        return articles;
    }

    /// <inheritdoc/>
    /// <remarks>Same fixed-SQL reasoning as <see cref="ListAsync"/>.</remarks>
    public async ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        Articles.Result.Count? total = await db.Articles.CountAsync(PatternFor(search), ct).ConfigureAwait(false);

        // COUNT(*) is bigint. The seam returns int because it feeds a pager, and a
        // table with more than two billion rows has a bigger problem than paging.
        return total is null ? 0 : (int)Math.Min(total.Total, int.MaxValue);
    }

    public async ValueTask<Article?> GetAsync(string id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        Row? row = await db.Articles.GetByIdAsync(id, ct).ConfigureAwait(false);

        return row is null
            ? null
            : new Article
            {
                Id = row.Id,
                Title = row.Title,
                Author = row.Author,
                Body = row.Body,
                Published = row.Published,
                ViewCount = row.ViewCount,
                CreatedAt = row.CreatedAt,
            };
    }

    public async ValueTask<string> CreateAsync(Article item, CancellationToken ct)
    {
        // Same key shape as the in-memory store, so switching stores does not change
        // what a URL looks like. Eight hex characters is a demo's choice: the PK
        // constraint turns a collision into a failed insert rather than a lost row,
        // which is the right failure, but a real consumer wants a full uuid.
        string key = Guid.NewGuid().ToString("n")[..8];

        // Stamped here for the same reason ArticleStore.OnCreate stamps it: the
        // generator marks CreatedAt read-only, so no form can carry it and the store
        // is the only thing left that can set it.
        item.Id = key;
        item.CreatedAt = DateTime.UtcNow;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Articles.InsertAsync(
            key, item.Title, item.Author, item.Body, item.Published, item.ViewCount, item.CreatedAt, ct)
            .ConfigureAwait(false);

        return key;
    }

    public async ValueTask<bool> UpdateAsync(string id, Article item, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        // Rows affected, not a prior existence check: a SELECT-then-UPDATE pair
        // answers the wrong question under concurrency, and UPDATE already reports
        // whether it matched.
        int affected = await db.Articles.UpdateAsync(
            item.Title, item.Author, item.Body, item.Published, item.ViewCount, id, ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        int affected = await db.Articles.DeleteAsync(id, ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// Turns a search box's contents into the ILIKE pattern the queries take.
    /// </summary>
    /// <remarks>
    /// The escaping is the part that matters. Without it a search for <c>%</c>
    /// matches every row, <c>_</c> matches any single character, and a trailing
    /// backslash makes the pattern malformed -- all three are things a user can type
    /// into the scaffolded search box by accident. PostgreSQL's ILIKE takes
    /// backslash as its default escape character, so each of the three is escaped
    /// with one.
    /// </remarks>
    internal static string PatternFor(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? "%"
            : "%" + search
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal)
              + "%";
}
