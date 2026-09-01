using NativeWeb.Sample;
using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The second store over <c>MemoryStore&lt;T&gt;</c>, which is where the generic half of
/// it is proved: a key that is not called <c>Id</c>, a single searchable field, and a
/// sort mapping that falls back to the key. Every mutant in this store's two overrides
/// survived the suite until spec 018, because nothing here had a test at all.
/// </summary>
public sealed class TagStoreTests
{
    private static readonly string[] Sortable = ["Slug", "Name"];

    private static readonly string[] Searchable = ["Name"];

    private static TagStore Seeded()
    {
        var store = new TagStore();

        store.Seed(new Tag { Slug = "c-sharp", Name = "C#" });
        store.Seed(new Tag { Slug = "aot", Name = "Native AOT" });
        store.Seed(new Tag { Slug = "htmx", Name = "HTMX" });

        return store;
    }

    private static async Task<List<string>> SlugsAsync(
        TagStore store,
        ScaffoldSortSpec sort,
        ScaffoldCursor? after = null,
        int take = 10,
        string? search = null)
    {
        IReadOnlyList<Tag> rows =
            await store.ListAsync(search, Searchable, sort, Sortable, after, take, CancellationToken.None);

        return [.. rows.Select(t => t.Slug)];
    }

    [Fact]
    public async Task TheKeyIsTheSlug()
    {
        Tag? found = await Seeded().GetAsync("htmx", CancellationToken.None);

        Assert.Equal("HTMX", found?.Name);
    }

    [Fact]
    public async Task SortingByName_OrdersByNameNotSlug() =>
        Assert.Equal(
            ["c-sharp", "htmx", "aot"],
            await SlugsAsync(Seeded(), new ScaffoldSortSpec("Name", ScaffoldSortDirection.Ascending)));

    [Fact]
    public async Task SortingByNameDescending_IsThatOrderReversed() =>
        Assert.Equal(
            ["aot", "htmx", "c-sharp"],
            await SlugsAsync(Seeded(), new ScaffoldSortSpec("Name", ScaffoldSortDirection.Descending)));

    [Fact]
    public async Task SortingBySlug_OrdersByTheKey() =>
        Assert.Equal(
            ["aot", "c-sharp", "htmx"],
            await SlugsAsync(Seeded(), new ScaffoldSortSpec("Slug", ScaffoldSortDirection.Ascending)));

    [Fact]
    public async Task AnUnmappedSortProperty_FallsBackToTheKey() =>
        Assert.Equal(
            ["aot", "c-sharp", "htmx"],
            await SlugsAsync(Seeded(), new ScaffoldSortSpec("Nonexistent", ScaffoldSortDirection.Ascending)));

    [Fact]
    public async Task SearchingMatchesTheNameField() =>
        Assert.Equal(
            ["aot"],
            await SlugsAsync(
                Seeded(),
                new ScaffoldSortSpec("Slug", ScaffoldSortDirection.Ascending),
                search: "native"));

    [Fact]
    public async Task SearchingDoesNotMatchTheSlug() =>
        Assert.Empty(
            await SlugsAsync(
                Seeded(),
                new ScaffoldSortSpec("Slug", ScaffoldSortDirection.Ascending),
                search: "c-sharp"));

    [Fact]
    public async Task ACursorPagesByTheNameOrder()
    {
        var sort = new ScaffoldSortSpec("Name", ScaffoldSortDirection.Ascending);
        var after = new ScaffoldCursor("Name", ScaffoldSortDirection.Ascending, "C#", "c-sharp", false);

        Assert.Equal(["htmx", "aot"], await SlugsAsync(Seeded(), sort, after));
    }

    [Fact]
    public async Task CountIgnoresPagingAndCountsTheFilter()
    {
        TagStore store = Seeded();

        Assert.Equal(3, await store.CountAsync(null, Searchable, CancellationToken.None));
        Assert.Equal(1, await store.CountAsync("HTM", Searchable, CancellationToken.None));
    }

    [Fact]
    public async Task ANegativeTakeIsRefused() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await Seeded().ListAsync(
                null,
                Searchable,
                new ScaffoldSortSpec("Slug", ScaffoldSortDirection.Ascending),
                Sortable,
                null,
                -1,
                CancellationToken.None));
}
