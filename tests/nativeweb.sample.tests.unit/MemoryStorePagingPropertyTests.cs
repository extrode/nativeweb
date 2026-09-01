using CsCheck;

using NativeWeb.Sample;
using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Sample.Tests.Unit;

/// <summary>
/// The reference store's paging held to properties against an independent model of the
/// order, rather than to examples. The model is written from the contract
/// <see cref="IScaffoldStore{T}"/> states -- nulls last ascending, the key breaks every
/// tie, descending is the ascending order reversed -- and never consults the store's
/// own comparator, so the two agreeing is evidence rather than a tautology.
/// <para>
/// Spec 017 shipped three defects that every example test here passed over, because
/// each needed a combination nobody enumerated: a cursor naming the first row, a
/// backward cursor deeper into the list than the page size, and a cursor naming a row
/// that no longer exists. A mutation run confirmed it afterwards -- <c>at &lt; 0</c>
/// mutated to <c>at &lt;= 0</c> and <c>at - start</c> to <c>at + start</c> both
/// survived the whole suite.
/// </para>
/// </summary>
public sealed class MemoryStorePagingPropertyTests
{
    private const long Iterations = 500;

    private static readonly string[] Sortable = ["Id", "Title", "Author", "CreatedAt"];

    private static readonly string[] Titles = ["Alpha", "Beta", "Beta", "Gamma", "alpha", ""];

    private static readonly string?[] Authors = [null, "ana", "bo", null, "Zed", "ana"];

    private sealed record Scenario(Article[] Rows, ScaffoldSortSpec Sort, int Take);

    private static readonly Gen<Scenario> Scenarios = Gen.Select(
        Gen.Int[0, 60].ArrayUnique[0, 14],
        Gen.OneOfConst("Id", "Title", "Author"),
        Gen.Enum<ScaffoldSortDirection>(),
        Gen.Int[1, 5],
        (seeds, property, direction, take) => new Scenario(
            [.. seeds.Select(s => new Article
            {
                Id = $"r{s:00}",
                Title = Titles[s % Titles.Length],
                Author = Authors[s % Authors.Length],
                Body = "b",
            })],
            new ScaffoldSortSpec(property, direction),
            take));

    private static string? SortValueOf(Article row, string property) => property switch
    {
        "Title" => row.Title,
        "Author" => row.Author,
        _ => row.Id,
    };

    private static List<Article> Model(Scenario s)
    {
        List<Article> ordered = [.. s.Rows];

        ordered.Sort((a, b) =>
        {
            string? x = SortValueOf(a, s.Sort.Property);
            string? y = SortValueOf(b, s.Sort.Property);

            int result = (x, y) switch
            {
                (null, null) => 0,
                (null, _) => 1,
                (_, null) => -1,
                _ => string.CompareOrdinal(x, y),
            };

            return result != 0 ? result : string.CompareOrdinal(a.Id, b.Id);
        });

        if (s.Sort.Direction == ScaffoldSortDirection.Descending)
        {
            ordered.Reverse();
        }

        return ordered;
    }

    private static ArticleStore Seeded(Scenario s)
    {
        var store = new ArticleStore();

        foreach (Article row in s.Rows)
        {
            store.Seed(row);
        }

        return store;
    }

    private static ScaffoldCursor CursorAt(Article row, ScaffoldSortSpec sort, bool backward) =>
        new(
            sort.Property,
            sort.Direction,
            ScaffoldValues.CursorText(SortValueOf(row, sort.Property)),
            row.Id,
            backward);

    private static List<string> Page(ArticleStore store, Scenario s, ScaffoldCursor? after, int? take = null) =>
    [
        .. store
            .ListAsync(null, [], s.Sort, Sortable, after, take ?? s.Take, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult()
            .Select(r => r.Id),
    ];

    private static List<string> Ids(IEnumerable<Article> rows) => [.. rows.Select(r => r.Id)];

    [Fact]
    public void TheFirstPageIsTheModelsFirstPage() =>
        Scenarios.Sample(
            s => Assert.Equal(Ids(Model(s).Take(s.Take)), Page(Seeded(s), s, null)),
            iter: Iterations);

    [Fact]
    public void AForwardCursorAtAnyRowReturnsTheRowsAfterIt() =>
        Gen.Select(Scenarios, Gen.Int[0, 20]).Sample(
            (s, pick) =>
            {
                List<Article> model = Model(s);

                if (model.Count == 0)
                {
                    return;
                }

                int at = pick % model.Count;

                Assert.Equal(
                    Ids(model.Skip(at + 1).Take(s.Take)),
                    Page(Seeded(s), s, CursorAt(model[at], s.Sort, false)));
            },
            iter: Iterations);

    [Fact]
    public void ABackwardCursorAtAnyRowReturnsTheRowsImmediatelyBeforeIt() =>
        Gen.Select(Scenarios, Gen.Int[0, 20]).Sample(
            (s, pick) =>
            {
                List<Article> model = Model(s);

                if (model.Count == 0)
                {
                    return;
                }

                int at = pick % model.Count;
                int from = Math.Max(0, at - s.Take);

                Assert.Equal(
                    Ids(model.Skip(from).Take(at - from)),
                    Page(Seeded(s), s, CursorAt(model[at], s.Sort, true)));
            },
            iter: Iterations);

    [Fact]
    public void APageIsNeverLongerThanTheRequestedTake() =>
        Gen.Select(Scenarios, Gen.Int[0, 20], Gen.Bool).Sample(
            (s, pick, backward) =>
            {
                List<Article> model = Model(s);
                ScaffoldCursor? after = model.Count == 0
                    ? null
                    : CursorAt(model[pick % model.Count], s.Sort, backward);

                Assert.True(Page(Seeded(s), s, after).Count <= s.Take);
            },
            iter: Iterations);

    [Fact]
    public void AForwardWalkVisitsEveryRowOnceInModelOrder() =>
        Scenarios.Sample(
            s =>
            {
                ArticleStore store = Seeded(s);
                List<Article> model = Model(s);
                List<string> seen = [];
                ScaffoldCursor? after = null;

                for (int guard = 0; guard <= model.Count + 1; guard++)
                {
                    List<string> page = Page(store, s, after);

                    if (page.Count == 0)
                    {
                        break;
                    }

                    seen.AddRange(page);
                    after = CursorAt(model[seen.Count - 1], s.Sort, false);
                }

                Assert.Equal(Ids(model), seen);
            },
            iter: Iterations);

    [Fact]
    public void ABackwardWalkFromTheEndVisitsEveryRowOnceInModelOrder() =>
        Scenarios.Sample(
            s =>
            {
                ArticleStore store = Seeded(s);
                List<Article> model = Model(s);

                if (model.Count == 0)
                {
                    return;
                }

                List<string> seen = [];
                ScaffoldCursor? after = CursorAt(model[^1], s.Sort, true);

                for (int guard = 0; guard <= model.Count + 1; guard++)
                {
                    List<string> page = Page(store, s, after);

                    if (page.Count == 0)
                    {
                        break;
                    }

                    seen.InsertRange(0, page);
                    after = CursorAt(model[model.Count - 1 - seen.Count], s.Sort, true);

                    if (seen.Count == model.Count - 1)
                    {
                        break;
                    }
                }

                seen.Add(model[^1].Id);

                Assert.Equal(Ids(model), seen);
            },
            iter: Iterations);

    [Fact]
    public void PreviousUndoesNext() =>
        Gen.Select(Scenarios, Gen.Int[0, 20]).Sample(
            (s, pick) =>
            {
                ArticleStore store = Seeded(s);
                List<Article> model = Model(s);

                if (model.Count == 0)
                {
                    return;
                }

                int at = pick % model.Count;
                List<string> forward = Page(store, s, CursorAt(model[at], s.Sort, false));

                if (forward.Count == 0)
                {
                    return;
                }

                Article landed = model.First(r => r.Id == forward[0]);

                Assert.Equal(
                    Ids(model.Skip(Math.Max(0, at + 1 - s.Take)).Take(Math.Min(s.Take, at + 1))),
                    Page(store, s, CursorAt(landed, s.Sort, true)));
            },
            iter: Iterations);

    [Fact]
    public void ACursorOnAMissingRowRendersTheFirstPage() =>
        Scenarios.Sample(
            s => Assert.Equal(
                Ids(Model(s).Take(s.Take)),
                Page(
                    Seeded(s),
                    s,
                    new ScaffoldCursor(s.Sort.Property, s.Sort.Direction, "gone", "r99", false))),
            iter: Iterations);

    [Fact]
    public void ABackwardCursorOnAMissingRowLeavesRoomForTheCallersTailTrim() =>
        Scenarios.Sample(
            s =>
            {
                List<string> page = Page(
                    Seeded(s),
                    s,
                    new ScaffoldCursor(s.Sort.Property, s.Sort.Direction, "gone", "r99", true));

                Assert.True(page.Count <= Math.Max(s.Take - 1, 0));
                Assert.Equal(Ids(Model(s).Take(page.Count)), page);
            },
            iter: Iterations);

    [Fact]
    public void ASearchWithNoSearchableFieldsFiltersNothing() =>
        Gen.Select(Scenarios, Gen.String[1, 6]).Sample(
            (s, search) =>
            {
                ArticleStore store = Seeded(s);
                IReadOnlyList<Article> rows = store
                    .ListAsync(search, [], s.Sort, Sortable, null, 50, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                Assert.Equal(Ids(Model(s)), Ids(rows));
            },
            iter: Iterations);

    [Fact]
    public void AnUnsortableTakeOfZeroReturnsNothing() =>
        Scenarios.Sample(
            s => Assert.Empty(Page(Seeded(s), s, null, 0)),
            iter: 200);
}
