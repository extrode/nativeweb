using NativeWeb.Sample;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The reference store's contract facts: the ones the generated endpoints now
/// depend on, rather than the ones the demo happens to exhibit.
/// </summary>
/// <remarks>
/// The generated update and delete handlers answer a <c>false</c> with 404, which
/// only means anything if a store returns false when the row is gone. This store
/// used to check <c>ContainsKey</c> and then assign through the indexer, so a row
/// deleted between the two came back and the handler reported success.
/// </remarks>
public sealed class DemoStoreTests
{
    private static ArticleStore StoreWith(string id, string title)
    {
        var store = new ArticleStore();
        store.Seed(new Article { Id = id, Title = title, Author = "xunit", Body = "b" });
        return store;
    }

    [Fact]
    public async Task UpdateAsync_OfARowThatIsThere_ReportsTrueAndWritesIt()
    {
        ArticleStore store = StoreWith("a1", "before");

        bool updated = await store.UpdateAsync(
            "a1",
            new Article { Title = "after", Author = "xunit", Body = "b" },
            CancellationToken.None);

        Assert.True(updated);

        Article? row = await store.GetAsync("a1", CancellationToken.None);
        Assert.Equal("after", row?.Title);
    }

    [Fact]
    public async Task UpdateAsync_OfARowThatIsNotThere_ReportsFalseAndCreatesNothing()
    {
        ArticleStore store = StoreWith("a1", "before");

        bool updated = await store.UpdateAsync(
            "missing",
            new Article { Title = "after", Author = "xunit", Body = "b" },
            CancellationToken.None);

        Assert.False(updated);
        Assert.Null(await store.GetAsync("missing", CancellationToken.None));
    }

    // The sequence the handler's 404 is for: the row is loaded for the edit form,
    // deleted, and the save arrives after. What this asserts is the outcome -- false,
    // and no row written back. It does not reach the interleaving the TryUpdate in
    // the store exists for, where the delete lands between a check and an assignment;
    // that window is a thread schedule, and no single-threaded test can force it.
    [Fact]
    public async Task UpdateAsync_AfterTheRowWasDeleted_DoesNotResurrectIt()
    {
        ArticleStore store = StoreWith("a1", "before");

        Article? loaded = await store.GetAsync("a1", CancellationToken.None);
        Assert.NotNull(loaded);

        Assert.True(await store.DeleteAsync("a1", CancellationToken.None));

        bool updated = await store.UpdateAsync(
            "a1",
            new Article { Title = "after", Author = "xunit", Body = "b" },
            CancellationToken.None);

        Assert.False(updated);
        Assert.Null(await store.GetAsync("a1", CancellationToken.None));
    }

    // False means "no such row" and nothing else. A single TryUpdate also fails when
    // a concurrent save replaced the value, and the generated handler maps false to
    // 404 -- so the loser of two admins saving the same row was told the record does
    // not exist while it was still in the list.
    [Fact]
    public async Task UpdateAsync_LosingToAConcurrentSave_StillReportsTheRowExists()
    {
        ArticleStore store = StoreWith("a1", "before");

        int reportedMissing = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            for (int i = 0; i < 500; i++)
            {
                bool updated = await store.UpdateAsync(
                    "a1",
                    new Article { Title = $"w{worker}-{i}", Author = "xunit", Body = "b" },
                    CancellationToken.None);

                if (!updated)
                {
                    Interlocked.Increment(ref reportedMissing);
                }
            }
        })));

        Assert.Equal(0, reportedMissing);
        Assert.NotNull(await store.GetAsync("a1", CancellationToken.None));
    }

    // A negative skip is a caller error the store names, because the database it
    // stands in for cannot absorb one: PostgreSQL's OFFSET -1 is an error, while
    // LINQ's Skip treats a negative count as zero and hides the defect.
    [Fact]
    public async Task ListAsync_WithANegativeSkip_Throws()
    {
        ArticleStore store = StoreWith("a1", "before");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.ListAsync(null, [], -1, 20, CancellationToken.None));
    }
}
