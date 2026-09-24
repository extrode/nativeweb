using Extrode.JauntyQ.Generated;

using Npgsql;

using NativeWeb.Scaffold;

using TagRow = Extrode.JauntyQ.Generated.Tag;

namespace NativeWeb.Sample;

/// <summary>
/// <see cref="IScaffoldStore{T}"/> for <see cref="Tag"/> over the generated
/// <c>db.Tags.*</c> methods from <c>db/sample/queries/Tags/</c> (spec 022 R4).
/// </summary>
/// <remarks>
/// <para>
/// The second store over generated queries, and the smaller one: <see cref="Tag"/>
/// sorts by its key or by name, searches on name alone, and has no nullable
/// sortable column, so the keyset arithmetic <see cref="JauntyArticleStore"/>
/// carries for <c>author</c> has no counterpart here. Until this class, tags were
/// an in-memory list even on the database path, and an admin's edit to one did
/// not survive a restart.
/// </para>
/// <para>
/// The key is minted here, eight hex characters like the article store's, and
/// not typed by the admin: <see cref="Tag.Slug"/> is a key that happens to read
/// well, and the generated form renders no input for a key. Singleton-safe for the
/// same reason the article store is, a connection per call and no per-request
/// state.
/// </para>
/// </remarks>
internal sealed class JauntyTagStore(NpgsqlDataSource dataSource) : IScaffoldStore<Tag>
{
    public async ValueTask<IReadOnlyList<Tag>> ListAsync(
        string? search,
        IReadOnlyList<string> searchFields,
        ScaffoldSortSpec? sort,
        IReadOnlyList<string> sortFields,
        ScaffoldCursor? after,
        int take,
        CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        string pattern = JauntyArticleStore.PatternFor(search);
        bool descending = sort?.Direction == ScaffoldSortDirection.Descending;
        bool backward = after?.Backward == true;
        bool mirrored = descending != backward;
        string afterId = after is null ? "" : after.Value.Key;
        string afterName = after?.SortValue ?? "";

        List<TagRow> rows = sort?.Property == nameof(Tag.Name)
            ? mirrored
                ? await db.Tags.ListByNameDescAsync(pattern, afterName, afterId, take, ct).ConfigureAwait(false)
                : await db.Tags.ListByNameAscAsync(pattern, afterName, afterId, take, ct).ConfigureAwait(false)
            : mirrored
                ? await db.Tags.ListByKeyDescAsync(pattern, afterId, take, ct).ConfigureAwait(false)
                : await db.Tags.ListByKeyAscAsync(pattern, afterId, take, ct).ConfigureAwait(false);

        var tags = new List<Tag>(rows.Count);
        foreach (TagRow row in rows)
        {
            tags.Add(Map(row));
        }

        if (backward)
        {
            tags.Reverse();
        }

        return tags;
    }

    public async ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        Tags.Result.Count? total = await db.Tags.CountAsync(JauntyArticleStore.PatternFor(search), ct).ConfigureAwait(false);
        return total is null ? 0 : (int)Math.Min(total.Total, int.MaxValue);
    }

    public async ValueTask<Tag?> GetAsync(string id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        TagRow? row = await db.Tags.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        // The tag's side of the relation (spec 023 R2), for the multi-select.
        Tag tag = Map(row);
        List<ArticleTags.Result.ListIdsForTag> links =
            await db.ArticleTags.ListIdsForTagAsync(id, ct).ConfigureAwait(false);
        tag.Articles = links.Select(l => l.ArticleId).ToList();

        return tag;
    }

    public async ValueTask<string> CreateAsync(Tag item, CancellationToken ct)
    {
        // A slug the caller supplied is the key (spec 023 R9), held to the alphabet
        // tags.slug checks; a blank one is generated. The form never carries the
        // key, so a typed slug reaches here from code. tags_pkey says whether it
        // is free, and a taken one is refused with the memory store's message.
        string? typed = string.IsNullOrWhiteSpace(item.Slug) ? null : item.Slug.Trim();
        if (typed is not null && !Slugs.IsValid(typed))
        {
            throw new ScaffoldRefusedException(Slugs.InvalidMessage);
        }

        string key = typed ?? Guid.NewGuid().ToString("n")[..8];
        item.Slug = key;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        using JauntyDb.Transaction tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await db.Tags.InsertAsync(key, item.Name, item.Color, ct).ConfigureAwait(false);
            await LinkAsync(db, key, item.Articles, ct).ConfigureAwait(false);
            tx.Commit();
            return key;
        }
        catch (PostgresException taken) when (IsSlugTaken(taken))
        {
            throw new ScaffoldRefusedException(Slugs.TakenMessage(key));
        }
        catch (PostgresException missing) when (JauntyArticleStore.IsMissingChoice(missing))
        {
            throw new ScaffoldRefusedException(JauntyArticleStore.MissingChoice(missing));
        }
    }

    public async ValueTask<bool> UpdateAsync(string id, Tag item, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        // The row and its links in one transaction, the links rewritten whole, as
        // JauntyArticleStore.UpdateAsync does from the other side (spec 023 R2).
        using JauntyDb.Transaction tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int affected = await db.Tags.UpdateAsync(item.Name, item.Color, id, ct).ConfigureAwait(false);
            if (affected == 0)
            {
                return false;
            }

            await db.ArticleTags.DeleteForTagAsync(id, ct).ConfigureAwait(false);
            await LinkAsync(db, id, item.Articles, ct).ConfigureAwait(false);
            tx.Commit();
            return true;
        }
        catch (PostgresException missing) when (JauntyArticleStore.IsMissingChoice(missing))
        {
            throw new ScaffoldRefusedException(JauntyArticleStore.MissingChoice(missing));
        }
    }

    private static async Task LinkAsync(JauntyDb db, string tagSlug, IReadOnlyList<string> articleIds, CancellationToken ct)
    {
        foreach (string articleId in articleIds)
        {
            await db.ArticleTags.InsertAsync(articleId, tagSlug, ct).ConfigureAwait(false);
        }
    }

    private static bool IsSlugTaken(PostgresException e) =>
        e.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(e.ConstraintName, "tags_pkey", StringComparison.Ordinal);

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        int affected = await db.Tags.DeleteAsync(id, ct).ConfigureAwait(false);
        return affected > 0;
    }

    private static Tag Map(TagRow row) => new()
    {
        Slug = row.Slug,
        Name = row.Name,
        Color = row.Color,
    };
}
