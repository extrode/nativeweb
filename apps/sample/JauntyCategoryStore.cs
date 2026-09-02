using JauntyQ.Generated;

using Npgsql;

using NativeWeb.Scaffold;

using CategoryRow = JauntyQ.Generated.Category;

namespace NativeWeb.Sample;

/// <summary>
/// <see cref="IScaffoldStore{T}"/> for <see cref="Category"/> over PostgreSQL
/// (spec 023 R5): the shape of <see cref="JauntyTagStore"/> with nothing on the
/// side, which is what makes it the smallest store in the tree. The article
/// form's category dropdown reads through this same seam, so a category that
/// exists here is a category the form offers.
/// </summary>
/// <remarks>
/// Deleting a category touches no article here: <c>articles.category_key</c>
/// is <c>ON DELETE SET NULL</c>, and the migration is where that rule lives.
/// Singleton-safe for the reason <see cref="JauntyArticleStore"/> is.
/// </remarks>
internal sealed class JauntyCategoryStore(NpgsqlDataSource dataSource) : IScaffoldStore<Category>
{
    public async ValueTask<IReadOnlyList<Category>> ListAsync(
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

        List<CategoryRow> rows = sort?.Property == nameof(Category.Name)
            ? mirrored
                ? await db.Categories.ListByNameDescAsync(pattern, afterName, afterId, take, ct).ConfigureAwait(false)
                : await db.Categories.ListByNameAscAsync(pattern, afterName, afterId, take, ct).ConfigureAwait(false)
            : mirrored
                ? await db.Categories.ListByKeyDescAsync(pattern, afterId, take, ct).ConfigureAwait(false)
                : await db.Categories.ListByKeyAscAsync(pattern, afterId, take, ct).ConfigureAwait(false);

        var categories = new List<Category>(rows.Count);
        foreach (CategoryRow row in rows)
        {
            categories.Add(Map(row));
        }

        if (backward)
        {
            categories.Reverse();
        }

        return categories;
    }

    public async ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        Categories.Result.Count? total =
            await db.Categories.CountAsync(JauntyArticleStore.PatternFor(search), ct).ConfigureAwait(false);

        return total is null ? 0 : (int)Math.Min(total.Total, int.MaxValue);
    }

    public async ValueTask<Category?> GetAsync(string id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        CategoryRow? row = await db.Categories.GetByIdAsync(id, ct).ConfigureAwait(false);

        return row is null ? null : Map(row);
    }

    public async ValueTask<string> CreateAsync(Category item, CancellationToken ct)
    {
        string key = Guid.NewGuid().ToString("n")[..8];
        item.Key = key;

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        await db.Categories.InsertAsync(key, item.Name, ct).ConfigureAwait(false);
        return key;
    }

    public async ValueTask<bool> UpdateAsync(string id, Category item, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        int affected = await db.Categories.UpdateAsync(item.Name, id, ct).ConfigureAwait(false);
        return affected > 0;
    }

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var db = new JauntyDb(connection);

        int affected = await db.Categories.DeleteAsync(id, ct).ConfigureAwait(false);
        return affected > 0;
    }

    private static Category Map(CategoryRow row) => new()
    {
        Key = row.Key,
        Name = row.Name,
    };
}
