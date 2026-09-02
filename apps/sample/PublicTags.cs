using JauntyQ.Generated;

using Npgsql;

using NativeWeb.Scaffold;

using TagRow = JauntyQ.Generated.Tag;

namespace NativeWeb.Sample;

/// <summary>One tag as the public listing shows it (spec 022 R5).</summary>
public readonly record struct TagSummary(string Slug, string Name, int PublishedCount);

/// <summary>
/// The tag pages' data access, the second public read seam beside
/// <see cref="IPublishedArticles"/> and chosen in <c>Program.cs</c> the same way.
/// </summary>
public interface IPublicTags
{
    /// <summary>
    /// Every tag, by name, with how many published articles carry it. A tag with
    /// none is a row with zero, not a missing row; an unpublished article counts
    /// for nothing.
    /// </summary>
    ValueTask<IReadOnlyList<TagSummary>> ListAsync(CancellationToken ct);

    /// <summary>The tag at this slug, or null when there is none.</summary>
    ValueTask<TagRef?> GetAsync(string slug, CancellationToken ct);
}

/// <summary>
/// The no-database implementation, over the registered tag and article stores
/// and the <see cref="MemoryRelation"/> between them. Counts in memory for the
/// reason <see cref="MemoryPublishedArticles"/> sorts in memory.
/// </summary>
internal sealed class MemoryPublicTags(
    IScaffoldStore<Tag> tags,
    IScaffoldStore<Article> articles,
    MemoryRelation relation) : IPublicTags
{
    private const int ScanLimit = 1000;

    public async ValueTask<IReadOnlyList<TagSummary>> ListAsync(CancellationToken ct)
    {
        IReadOnlyList<Tag> all = await tags.ListAsync(null, [], null, [], null, ScanLimit, ct).ConfigureAwait(false);
        IReadOnlyList<Article> rows = await articles.ListAsync(null, [], null, [], null, ScanLimit, ct).ConfigureAwait(false);

        // Published articles only, and only ones the store still holds: the
        // relation keeps a link to a deleted article, and this is where it stops
        // counting.
        var published = rows.Where(a => a.Status == ArticleStatus.Published).Select(a => a.Id).ToList();

        return all
            .Select(t => new TagSummary(
                t.Slug,
                t.Name,
                published.Count(id => relation.TagsOf(id).Contains(t.Slug, StringComparer.Ordinal))))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Slug, StringComparer.Ordinal)
            .ToList();
    }

    public async ValueTask<TagRef?> GetAsync(string slug, CancellationToken ct)
    {
        Tag? found = await tags.GetAsync(slug, ct).ConfigureAwait(false);

        return found is null ? null : new TagRef(found.Slug, found.Name);
    }
}

/// <summary>
/// The PostgreSQL implementation, over <c>db.Tags.ListWithCountsAsync</c> and
/// <c>db.Tags.GetByIdAsync</c>. The counting is the SQL's; see
/// <c>db/sample/queries/Tags/ListWithCounts.sql</c> for why an unpublished
/// article neither counts nor drops the tag.
/// </summary>
internal sealed class JauntyPublicTags(NpgsqlDataSource dataSource) : IPublicTags
{
    public async ValueTask<IReadOnlyList<TagSummary>> ListAsync(CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        List<Tags.Result.ListWithCounts> rows = await db.Tags.ListWithCountsAsync(ct).ConfigureAwait(false);

        return rows.Select(r => new TagSummary(r.Slug, r.Name, (int)r.PublishedCount)).ToList();
    }

    public async ValueTask<TagRef?> GetAsync(string slug, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        TagRow? row = await db.Tags.GetByIdAsync(slug, ct).ConfigureAwait(false);

        return row is null ? null : new TagRef(row.Slug, row.Name);
    }
}
