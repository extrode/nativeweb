using JauntyQ.Generated;

using Npgsql;

using NativeWeb.Scaffold;

using Row = JauntyQ.Generated.Article;

namespace NativeWeb.Sample;

/// <summary>One tag as a public page names and links it (spec 022).</summary>
public readonly record struct TagRef(string Slug, string Name);

/// <summary>
/// One published article as a public list shows it: the fields it shows, the
/// slug it links by, its tags, and nothing else.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="Article"/>. That type is the scaffolding's model —
/// its shape is decided by what the admin UI edits, and handing it to a public
/// page means the page silently gains a field the day someone adds one to the
/// form. A read model that names exactly what it renders cannot drift that way.
/// </remarks>
public readonly record struct PublishedArticle(
    string Id,
    string Slug,
    string Title,
    string? Author,
    DateTime CreatedAt,
    IReadOnlyList<TagRef> Tags,
    DateOnly? PublishedAt = null)
{
    /// <summary>
    /// The instant the public site dates this article by (spec 023 R4): the
    /// publication date at midnight UTC, or the creation time for a row that has
    /// none. A method, not a property, so the JSON document does not grow a
    /// second date that is only ever derived from the two it has.
    /// </summary>
    public DateTime PublishedOn() =>
        PublishedAt is DateOnly date ? date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : CreatedAt;
}

/// <summary>
/// One published article as its own page shows it (spec 022 R3): the list's
/// fields plus the body. Not the view count, which the public page never shows.
/// </summary>
public readonly record struct ArticleDetail(
    string Slug,
    string Title,
    string? Author,
    DateTime CreatedAt,
    string Body,
    IReadOnlyList<TagRef> Tags);

/// <summary>One published article as the sitemap lists it (spec 022 R8).</summary>
public readonly record struct SitemapArticle(string Slug, DateTime CreatedAt);

/// <summary>
/// The public pages' data access, behind a seam for the same reason
/// <see cref="IScaffoldStore{T}"/> is: <c>Program.cs</c> decides which
/// implementation answers, and the endpoint above it never learns whether a
/// database exists. Every method answers for published articles only; an
/// unpublished row is indistinguishable from a missing one through this seam.
/// </summary>
public interface IPublishedArticles
{
    /// <summary>Published articles, newest first, at most <paramref name="take"/> of them.</summary>
    ValueTask<IReadOnlyList<PublishedArticle>> RecentAsync(int take, CancellationToken ct);

    /// <summary>The published article at this slug, or null when there is none.</summary>
    ValueTask<ArticleDetail?> BySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Adds one view to the published article at this slug (spec 022 R6). True
    /// when a published row was counted; false, and nothing counted, otherwise.
    /// Two calls arriving together both count.
    /// </summary>
    ValueTask<bool> CountViewAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Published articles carrying this tag, newest first, at most
    /// <paramref name="take"/> of them. Empty for a tag with none, and for a tag
    /// that does not exist: whether the tag exists is <see cref="IPublicTags"/>'s
    /// question.
    /// </summary>
    ValueTask<IReadOnlyList<PublishedArticle>> ByTagAsync(string tagSlug, int take, CancellationToken ct);

    /// <summary>Every published article, newest first, as the sitemap lists it.</summary>
    ValueTask<IReadOnlyList<SitemapArticle>> SitemapAsync(CancellationToken ct);
}

/// <summary>
/// The no-database implementation, over whatever <see cref="IScaffoldStore{T}"/>
/// pair is registered and the <see cref="MemoryRelation"/> between them. Filters
/// and sorts in memory because the seam it reads through offers no way to express
/// either — which is the honest cost of the in-memory path and the reason the
/// JauntyQ one exists.
/// </summary>
/// <remarks>
/// The view count is an increment under a lock on the <see cref="Article"/>
/// instance the store holds, so two visits arriving together both count. One
/// gap, said here and in <c>work/todo.md</c>: the admin form posts the count
/// back as a value, so a view counted between the edit form's GET and its POST
/// is overwritten by the count the form carried. The SQL path has the same
/// window, since <c>Update.sql</c> writes the posted value too; closing it
/// means taking the field out of the form, which is the next spec's admin work.
/// </remarks>
internal sealed class MemoryPublishedArticles(
    IScaffoldStore<Article> store,
    IScaffoldStore<Tag> tags,
    MemoryRelation relation) : IPublishedArticles
{
    // The store pages, so an unfiltered read needs a bound. This one is arbitrary
    // and only has to exceed the seed set; a real consumer would not be reading a
    // public feed through a paging admin seam at all.
    private const int ScanLimit = 1000;

    private readonly Lock _views = new();

    public async ValueTask<IReadOnlyList<PublishedArticle>> RecentAsync(int take, CancellationToken ct)
    {
        IReadOnlyList<Article> published = await PublishedAsync(ct).ConfigureAwait(false);
        Func<string, IReadOnlyList<TagRef>> tagsOf = await TagsOfAsync(ct).ConfigureAwait(false);

        return published.Take(take).Select(a => Listed(a, tagsOf)).ToList();
    }

    public async ValueTask<ArticleDetail?> BySlugAsync(string slug, CancellationToken ct)
    {
        Article? found = await FindAsync(slug, ct).ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        Func<string, IReadOnlyList<TagRef>> tagsOf = await TagsOfAsync(ct).ConfigureAwait(false);
        return new ArticleDetail(
            found.Slug ?? "", found.Title, found.Author, found.CreatedAt, found.Body, tagsOf(found.Id));
    }

    public async ValueTask<bool> CountViewAsync(string slug, CancellationToken ct)
    {
        Article? found = await FindAsync(slug, ct).ConfigureAwait(false);
        if (found is null)
        {
            return false;
        }

        lock (this._views)
        {
            found.ViewCount++;
        }

        return true;
    }

    public async ValueTask<IReadOnlyList<PublishedArticle>> ByTagAsync(string tagSlug, int take, CancellationToken ct)
    {
        IReadOnlyList<Article> published = await PublishedAsync(ct).ConfigureAwait(false);
        Func<string, IReadOnlyList<TagRef>> tagsOf = await TagsOfAsync(ct).ConfigureAwait(false);

        return published
            .Where(a => relation.TagsOf(a.Id).Contains(tagSlug, StringComparer.Ordinal))
            .Take(take)
            .Select(a => Listed(a, tagsOf))
            .ToList();
    }

    public async ValueTask<IReadOnlyList<SitemapArticle>> SitemapAsync(CancellationToken ct)
    {
        IReadOnlyList<Article> published = await PublishedAsync(ct).ConfigureAwait(false);

        return published.Select(a => new SitemapArticle(a.Slug ?? "", a.CreatedAt)).ToList();
    }

    // No search term, so no search fields; no sort and no cursor, so the store's
    // default key order from the beginning. The empty lists are what the seam
    // takes when the caller is filtering and ordering nothing -- this one sorts
    // here, in memory, because that is what reading a public feed through a
    // paging admin seam costs. Newest publication date first, a row without one
    // dated by its creation day (spec 023 R4), then creation time, then the key
    // breaking ties, as ListPublished.sql orders. Only Published is public: a
    // Draft and an Archived row are equally absent here (R3).
    private async ValueTask<IReadOnlyList<Article>> PublishedAsync(CancellationToken ct)
    {
        IReadOnlyList<Article> all = await store.ListAsync(null, [], null, [], null, ScanLimit, ct).ConfigureAwait(false);

        return all
            .Where(a => a.Status == ArticleStatus.Published)
            .OrderByDescending(a => a.PublishedAt ?? DateOnly.FromDateTime(a.CreatedAt))
            .ThenByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }

    private async ValueTask<Article?> FindAsync(string slug, CancellationToken ct)
    {
        IReadOnlyList<Article> all = await store.ListAsync(null, [], null, [], null, ScanLimit, ct).ConfigureAwait(false);

        return all.FirstOrDefault(a => a.Status == ArticleStatus.Published && string.Equals(a.Slug, slug, StringComparison.Ordinal));
    }

    // Tag slugs the relation holds resolve against the tag store, by name order,
    // so a tag deleted through the admin drops off every article at once and the
    // relation needs no cascade of its own.
    private async ValueTask<Func<string, IReadOnlyList<TagRef>>> TagsOfAsync(CancellationToken ct)
    {
        IReadOnlyList<Tag> all = await tags.ListAsync(null, [], null, [], null, ScanLimit, ct).ConfigureAwait(false);
        var bySlug = all.ToDictionary(
            t => t.Slug, t => new TagRef(t.Slug, t.Name), StringComparer.Ordinal);

        return articleId => relation.TagsOf(articleId)
            .Where(bySlug.ContainsKey)
            .Select(slug => bySlug[slug])
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Slug, StringComparer.Ordinal)
            .ToList();
    }

    private static PublishedArticle Listed(Article a, Func<string, IReadOnlyList<TagRef>> tagsOf) =>
        new(a.Id, a.Slug ?? "", a.Title, a.Author, a.CreatedAt, tagsOf(a.Id), a.PublishedAt);
}

/// <summary>
/// The PostgreSQL implementation, over the generated <c>db.Articles.*</c> and
/// <c>db.ArticleTags.*</c> methods from <c>db/sample/queries/</c>.
/// </summary>
/// <remarks>
/// The filter and the ordering are in the SQL, not here. That is the whole
/// difference from <see cref="MemoryPublishedArticles"/>: the database does the
/// work it is good at, and this class does nothing but map rows to read models.
/// A list and its tags are two queries over the same window rather than one
/// join, so a page of twenty articles is two round trips whatever its tags, and
/// an article's tags are grouped here by id. Singleton-safe for the same reason
/// <see cref="JauntyArticleStore"/> is — a connection per call, no per-request
/// state.
/// </remarks>
internal sealed class JauntyPublishedArticles(NpgsqlDataSource dataSource) : IPublishedArticles
{
    public async ValueTask<IReadOnlyList<PublishedArticle>> RecentAsync(int take, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<Row> rows = await db.Articles.ListPublishedAsync(take, ct).ConfigureAwait(false);
        List<ArticleTags.Result.ListForWindow> links =
            await db.ArticleTags.ListForWindowAsync(null, take, ct).ConfigureAwait(false);

        return Listed(rows, links, r => (r.Id, r.Slug, r.Title, r.Author, r.CreatedAt));
    }

    public async ValueTask<ArticleDetail?> BySlugAsync(string slug, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        Row? row = await db.Articles.GetBySlugAsync(slug, ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        List<ArticleTags.Result.ListForArticle> links =
            await db.ArticleTags.ListForArticleAsync(slug, ct).ConfigureAwait(false);

        return new ArticleDetail(
            row.Slug,
            row.Title,
            row.Author,
            row.CreatedAt,
            row.Body,
            links.Select(l => new TagRef(l.Slug, l.Name)).ToList());
    }

    public async ValueTask<bool> CountViewAsync(string slug, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        int affected = await db.Articles.CountViewAsync(slug, ct).ConfigureAwait(false);
        return affected > 0;
    }

    public async ValueTask<IReadOnlyList<PublishedArticle>> ByTagAsync(string tagSlug, int take, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        // A projection type rather than the row, because the query reads through a
        // join; the columns are the article's, in the table's order.
        List<Articles.Result.ListPublishedByTag> rows =
            await db.Articles.ListPublishedByTagAsync(tagSlug, take, ct).ConfigureAwait(false);
        List<ArticleTags.Result.ListForWindow> links =
            await db.ArticleTags.ListForWindowAsync(tagSlug, take, ct).ConfigureAwait(false);

        return Listed(rows, links, r => (r.Id, r.Slug, r.Title, r.Author, r.CreatedAt));
    }

    public async ValueTask<IReadOnlyList<SitemapArticle>> SitemapAsync(CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<Articles.Result.ListPublishedForSitemap> rows =
            await db.Articles.ListPublishedForSitemapAsync(ct).ConfigureAwait(false);

        return rows.Select(r => new SitemapArticle(r.Slug, r.CreatedAt)).ToList();
    }

    // The links come back ordered by article then tag name, which is the order
    // each article's list keeps; grouping by id is all that is left to do.
    private static List<PublishedArticle> Listed<TRow>(
        List<TRow> rows,
        List<ArticleTags.Result.ListForWindow> links,
        Func<TRow, (string Id, string Slug, string Title, string? Author, DateTime CreatedAt)> fields)
    {
        Dictionary<string, List<TagRef>> tagsById = new(StringComparer.Ordinal);
        foreach (ArticleTags.Result.ListForWindow link in links)
        {
            if (!tagsById.TryGetValue(link.ArticleId, out List<TagRef>? list))
            {
                list = [];
                tagsById[link.ArticleId] = list;
            }

            list.Add(new TagRef(link.Slug, link.Name));
        }

        var articles = new List<PublishedArticle>(rows.Count);
        foreach (TRow row in rows)
        {
            (string id, string slug, string title, string? author, DateTime createdAt) = fields(row);
            IReadOnlyList<TagRef> tags = tagsById.TryGetValue(id, out List<TagRef>? found) ? found : [];
            articles.Add(new PublishedArticle(id, slug, title, author, createdAt, tags));
        }

        return articles;
    }
}
