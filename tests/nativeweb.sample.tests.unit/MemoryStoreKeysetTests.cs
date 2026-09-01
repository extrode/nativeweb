using NativeWeb.Sample;
using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The reference store's keyset paging. It is the twin of the JauntyQ store's SQL,
/// so what it has to agree about is the order itself: where a null sorts, that the
/// key breaks every tie, and that walking a page forward and back returns the same
/// rows rather than a window shifted by one.
/// </summary>
public sealed class MemoryStoreKeysetTests
{
    private static readonly string[] Sortable = ["Id", "Title", "Author", "CreatedAt"];

    private static ArticleStore Seeded(params (string Id, string Title, string? Author)[] rows)
    {
        var store = new ArticleStore();

        foreach ((string id, string title, string? author) in rows)
        {
            store.Seed(new Article { Id = id, Title = title, Author = author, Body = "b" });
        }

        return store;
    }

    private static async Task<List<string>> KeysAsync(
        ArticleStore store,
        ScaffoldSortSpec sort,
        ScaffoldCursor? after,
        int take)
    {
        IReadOnlyList<Article> rows = await store.ListAsync(null, [], sort, Sortable, after, take, CancellationToken.None);
        return [.. rows.Select(r => r.Id)];
    }

    private static ScaffoldCursor CursorFor(Article row, ScaffoldSortSpec sort, bool backward) =>
        new(
            sort.Property,
            sort.Direction,
            sort.Property switch
            {
                "Title" => ScaffoldValues.CursorText(row.Title),
                "Author" => ScaffoldValues.CursorText(row.Author),
                "CreatedAt" => ScaffoldValues.CursorText(row.CreatedAt),
                _ => ScaffoldValues.CursorText(row.Id),
            },
            row.Id,
            backward);

    private static async Task<List<string>> WalkAsync(ArticleStore store, ScaffoldSortSpec sort, int take)
    {
        List<string> seen = [];
        ScaffoldCursor? after = null;

        while (true)
        {
            IReadOnlyList<Article> page =
                await store.ListAsync(null, [], sort, Sortable, after, take, CancellationToken.None);

            if (page.Count == 0)
            {
                return seen;
            }

            seen.AddRange(page.Select(r => r.Id));
            after = CursorFor(page[^1], sort, false);
        }
    }

    [Fact]
    public async Task WithNoSort_TheOrderIsTheKey()
    {
        ArticleStore store = Seeded(("c", "Third", "x"), ("a", "First", "x"), ("b", "Second", "x"));

        Assert.Equal(["a", "b", "c"], await KeysAsync(store, new ScaffoldSortSpec("Id", ScaffoldSortDirection.Ascending), null, 10));
    }

    [Fact]
    public async Task Descending_IsTheAscendingOrderReversed()
    {
        ArticleStore store = Seeded(("a", "Beta", "x"), ("b", "Alpha", "x"), ("c", "Gamma", "x"));

        Assert.Equal(
            ["b", "a", "c"],
            await KeysAsync(store, new ScaffoldSortSpec("Title", ScaffoldSortDirection.Ascending), null, 10));
        Assert.Equal(
            ["c", "a", "b"],
            await KeysAsync(store, new ScaffoldSortSpec("Title", ScaffoldSortDirection.Descending), null, 10));
    }

    [Fact]
    public async Task RowsSharingASortValue_AreOrderedByKey()
    {
        ArticleStore store = Seeded(("c", "Same", "x"), ("a", "Same", "x"), ("b", "Same", "x"));

        Assert.Equal(
            ["a", "b", "c"],
            await KeysAsync(store, new ScaffoldSortSpec("Title", ScaffoldSortDirection.Ascending), null, 10));
    }

    [Fact]
    public async Task AWalkOverEqualSortValues_ReturnsEachRowOnce()
    {
        ArticleStore store = Seeded(
            ("a", "Same", "x"),
            ("b", "Same", "x"),
            ("c", "Same", "x"),
            ("d", "Same", "x"),
            ("e", "Same", "x"));

        Assert.Equal(
            ["a", "b", "c", "d", "e"],
            await WalkAsync(store, new ScaffoldSortSpec("Title", ScaffoldSortDirection.Ascending), 2));
    }

    [Fact]
    public async Task ANullSortValue_SortsLastAscendingAndFirstDescending()
    {
        ArticleStore store = Seeded(("a", "One", "zeta"), ("b", "Two", null), ("c", "Three", "alpha"));

        Assert.Equal(
            ["c", "a", "b"],
            await KeysAsync(store, new ScaffoldSortSpec("Author", ScaffoldSortDirection.Ascending), null, 10));
        Assert.Equal(
            ["b", "a", "c"],
            await KeysAsync(store, new ScaffoldSortSpec("Author", ScaffoldSortDirection.Descending), null, 10));
    }

    [Fact]
    public async Task AWalkAcrossTheNullBoundary_ReturnsEveryRowOnce()
    {
        ArticleStore store = Seeded(
            ("a", "One", "zeta"),
            ("b", "Two", null),
            ("c", "Three", "alpha"),
            ("d", "Four", null),
            ("e", "Five", "mu"));

        Assert.Equal(
            ["c", "e", "a", "b", "d"],
            await WalkAsync(store, new ScaffoldSortSpec("Author", ScaffoldSortDirection.Ascending), 2));
    }

    [Fact]
    public async Task Backward_ReturnsTheRowsImmediatelyBeforeTheCursor()
    {
        ArticleStore store = Seeded(
            ("a", "One", "x"),
            ("b", "Two", "x"),
            ("c", "Three", "x"),
            ("d", "Four", "x"),
            ("e", "Five", "x"));

        var sort = new ScaffoldSortSpec("Id", ScaffoldSortDirection.Ascending);
        IReadOnlyList<Article> second = await store.ListAsync(
            null,
            [],
            sort,
            Sortable,
            CursorFor(new Article { Id = "b" }, sort, false),
            2,
            CancellationToken.None);

        Assert.Equal(["c", "d"], second.Select(r => r.Id));

        Assert.Equal(["a", "b"], await KeysAsync(store, sort, CursorFor(second[0], sort, true), 2));
    }

    [Fact]
    public async Task BackwardFromTheSecondRow_ReturnsWhatIsThereRatherThanAFullPage()
    {
        ArticleStore store = Seeded(("a", "One", "x"), ("b", "Two", "x"), ("c", "Three", "x"));
        var sort = new ScaffoldSortSpec("Id", ScaffoldSortDirection.Ascending);

        Assert.Equal(
            ["a"],
            await KeysAsync(store, sort, CursorFor(new Article { Id = "b" }, sort, true), 10));
    }

    [Fact]
    public async Task ACursorOnADeletedRow_LandsOnTheFirstPage()
    {
        ArticleStore store = Seeded(("a", "One", "x"), ("b", "Two", "x"), ("c", "Three", "x"));
        var sort = new ScaffoldSortSpec("Id", ScaffoldSortDirection.Ascending);

        Assert.Equal(
            ["a", "b"],
            await KeysAsync(store, sort, CursorFor(new Article { Id = "gone" }, sort, false), 2));
    }

    [Fact]
    public async Task ABackwardCursorOnADeletedRow_LandsOnAPageTheCallerWillNotTrim()
    {
        ArticleStore store = Seeded(("a", "One", "x"), ("b", "Two", "x"), ("c", "Three", "x"));
        var sort = new ScaffoldSortSpec("Id", ScaffoldSortDirection.Ascending);

        Assert.Equal(
            ["a", "b"],
            await KeysAsync(store, sort, CursorFor(new Article { Id = "gone" }, sort, true), 3));
    }

    [Fact]
    public async Task TextOrdering_IsOrdinalRatherThanTheHostsLocale()
    {
        ArticleStore store = Seeded(("a", "Zebra", "x"), ("b", "apple", "x"), ("c", "Apple", "x"));

        Assert.Equal(
            ["c", "a", "b"],
            await KeysAsync(store, new ScaffoldSortSpec("Title", ScaffoldSortDirection.Ascending), null, 10));
    }

    [Fact]
    public async Task AnUnknownSortProperty_FallsBackToTheKeyOrder()
    {
        ArticleStore store = Seeded(("c", "Third", "x"), ("a", "First", "x"), ("b", "Second", "x"));

        Assert.Equal(
            ["a", "b", "c"],
            await KeysAsync(store, new ScaffoldSortSpec("Nonexistent", ScaffoldSortDirection.Ascending), null, 10));
    }

    [Fact]
    public async Task Sorting_AppliesAfterTheSearchFilter()
    {
        ArticleStore store = Seeded(("a", "Beta", "x"), ("b", "Alpha", "x"), ("c", "Betamax", "x"));

        IReadOnlyList<Article> rows = await store.ListAsync(
            "Beta",
            ["Title"],
            new ScaffoldSortSpec("Title", ScaffoldSortDirection.Descending),
            Sortable,
            null,
            10,
            CancellationToken.None);

        Assert.Equal(["c", "a"], rows.Select(r => r.Id));
    }
}
