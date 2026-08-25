using JauntyQ.Generated;

using Npgsql;

using NativeWeb.Scaffold;

using Row = JauntyQ.Generated.Article;

namespace NativeWeb.Sample;

/// <summary>
/// One published article as the public page needs it: the three fields it shows
/// and the key it links by, and nothing else.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="Article"/>. That type is the scaffolding's model —
/// its shape is decided by what the admin UI edits, and handing it to a public
/// page means the page silently gains a field the day someone adds one to the
/// form. A read model that names exactly what it renders cannot drift that way.
/// </remarks>
public readonly record struct PublishedArticle(string Id, string Title, string? Author, DateTime CreatedAt);

/// <summary>
/// The public page's data access, behind a seam for the same reason
/// <see cref="IScaffoldStore{T}"/> is: <c>Program.cs</c> decides which
/// implementation answers, and the endpoint above it never learns whether a
/// database exists.
/// </summary>
public interface IPublishedArticles
{
    /// <summary>Published articles, newest first, at most <paramref name="take"/> of them.</summary>
    ValueTask<IReadOnlyList<PublishedArticle>> RecentAsync(int take, CancellationToken ct);
}

/// <summary>
/// The no-database implementation, over whatever <see cref="IScaffoldStore{T}"/>
/// is registered. Filters and sorts in memory because the seam it reads through
/// offers no way to express either — which is the honest cost of the in-memory
/// path and the reason the JauntyQ one exists.
/// </summary>
internal sealed class MemoryPublishedArticles(IScaffoldStore<Article> store) : IPublishedArticles
{
    // The store pages, so an unfiltered read needs a bound. This one is arbitrary
    // and only has to exceed the seed set; a real consumer would not be reading a
    // public feed through a paging admin seam at all.
    private const int ScanLimit = 1000;

    public async ValueTask<IReadOnlyList<PublishedArticle>> RecentAsync(int take, CancellationToken ct)
    {
        IReadOnlyList<Article> all = await store.ListAsync(null, 0, ScanLimit, ct).ConfigureAwait(false);

        return all
            .Where(a => a.Published)
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .Take(take)
            .Select(a => new PublishedArticle(a.Id, a.Title, a.Author, a.CreatedAt))
            .ToList();
    }
}

/// <summary>
/// The PostgreSQL implementation, over the generated
/// <c>db.Articles.ListPublishedAsync</c> from
/// <c>db/sample/queries/Articles/ListPublished.sql</c>.
/// </summary>
/// <remarks>
/// The filter and the ordering are in the SQL, not here. That is the whole
/// difference from <see cref="MemoryPublishedArticles"/>: the database does the
/// work it is good at, and this class does nothing but map a row to a read model.
/// Singleton-safe for the same reason <see cref="JauntyArticleStore"/> is — a
/// connection per call, no per-request state.
/// </remarks>
internal sealed class JauntyPublishedArticles(NpgsqlDataSource dataSource) : IPublishedArticles
{
    public async ValueTask<IReadOnlyList<PublishedArticle>> RecentAsync(int take, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<Row> rows = await db.Articles.ListPublishedAsync(take, ct).ConfigureAwait(false);

        var articles = new List<PublishedArticle>(rows.Count);
        foreach (Row row in rows)
        {
            articles.Add(new PublishedArticle(row.Id, row.Title, row.Author, row.CreatedAt));
        }

        return articles;
    }
}
