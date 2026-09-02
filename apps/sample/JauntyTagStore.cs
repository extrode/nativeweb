using JauntyQ.Generated;

using Npgsql;

using NativeWeb.Scaffold;

using TagRow = JauntyQ.Generated.Tag;

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
        return row is null ? null : Map(row);
    }

    public async ValueTask<string> CreateAsync(Tag item, CancellationToken ct)
    {
        string key = Guid.NewGuid().ToString("n")[..8];
        item.Slug = key;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Tags.InsertAsync(key, item.Name, item.Color, ct).ConfigureAwait(false);
        return key;
    }

    public async ValueTask<bool> UpdateAsync(string id, Tag item, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        int affected = await db.Tags.UpdateAsync(item.Name, item.Color, id, ct).ConfigureAwait(false);
        return affected > 0;
    }

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
