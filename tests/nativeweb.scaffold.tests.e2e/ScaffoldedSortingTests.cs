using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// Sorting and cursor paging driven by a real browser against the NativeAOT binary.
/// </summary>
/// <remarks>
/// <para>
/// Two things live only here. A sort header is a link a person clicks, and whether
/// the one they clicked is the one that reordered the list is a question about
/// rendered markup and a real navigation, not about a query string a test built.
/// And <c>aria-sort</c> is the whole of what a screen reader gets, because the
/// direction marker beside the label is <c>aria-hidden</c> -- an assertion over an
/// HTML string cannot tell that the attribute is on the element the reader lands on.
/// </para>
/// <para>
/// Rows are inserted in SQL rather than created through the form: paging needs more
/// than a page of them, and twenty-five form round trips would be minutes of browser
/// time to arrange state that is not itself under test.
/// </para>
/// </remarks>
[Collection(ScaffoldE2eFixture.Name)]
public sealed class ScaffoldedSortingTests(JauntySampleFixture app, BrowserFixture browsers)
{
    private const string ListPath = "/admin/articles";
    private const int PageSize = 20;

    private static string Marker() => $"e2esort{Guid.NewGuid():N}";

    private static string Title(string marker, int n) => $"{marker}-t{n:D2}";

    private static async Task SeedAsync(string marker, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await SampleE2eDatabase.InsertAsync($"{marker}-{i:D2}", Title(marker, i), true, DateTime.UtcNow.AddMinutes(-i));
        }
    }

    private async Task<IPage> SignedInPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl });

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/signin");
        return page;
    }

    private static ILocator TitleCells(IPage page) =>
        page.Locator("table.nws-table tbody tr td:first-child");

    // By position, not by accessible name. The column order is declared in
    // apps/sample/Models.cs and is part of what the generator emits, so an index is a
    // fact about the model rather than a guess about how a name is computed.
    private const int TitleColumn = 0;
    private const int ViewsColumn = 4;

    private static ILocator Header(IPage page, int column) =>
        page.Locator("table.nws-table thead th").Nth(column);

    private static async Task<List<string>> TitlesAsync(IPage page) =>
        [.. await TitleCells(page).AllTextContentsAsync()];

    [Fact]
    public async Task ClickingASortableHeader_ReordersTheListAndTogglesOnASecondClick()
    {
        string marker = Marker();
        await SeedAsync(marker, 4);

        IPage page = await SignedInPageAsync();
        await page.GotoAsync($"{ListPath}?q={marker}");

        ILocator header = Header(page, TitleColumn);
        await Assertions.Expect(header).ToHaveTextAsync("Title");

        // Web-first assertions throughout, because each of these follows a real
        // navigation: they re-query until the new document has rendered, where a plain
        // Assert.Equal reads whichever document happened to be current.
        await header.Locator("a").ClickAsync();
        await Assertions.Expect(TitleCells(page))
            .ToHaveTextAsync([.. Enumerable.Range(0, 4).Select(i => Title(marker, i))]);
        await Assertions.Expect(header).ToHaveAttributeAsync("aria-sort", "ascending");

        await header.Locator("a").ClickAsync();
        await Assertions.Expect(TitleCells(page))
            .ToHaveTextAsync([.. Enumerable.Range(0, 4).Select(i => Title(marker, i)).Reverse()]);
        await Assertions.Expect(header).ToHaveAttributeAsync("aria-sort", "descending");
    }

    // Views is [ScaffoldList] and not [ScaffoldSort], which is how a reader tells
    // which columns can order the list. An inert heading is the feature.
    [Fact]
    public async Task AListedColumnThatIsNotSortable_OffersNoLink()
    {
        IPage page = await SignedInPageAsync();
        await page.GotoAsync(ListPath);

        await Assertions.Expect(Header(page, ViewsColumn)).ToHaveTextAsync("Views");
        await Assertions.Expect(Header(page, ViewsColumn).Locator("a")).ToHaveCountAsync(0);

        await Assertions.Expect(Header(page, TitleColumn).Locator("a")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task NextThenPrevious_ReturnsToTheSameRows()
    {
        string marker = Marker();
        await SeedAsync(marker, PageSize + 5);

        IPage page = await SignedInPageAsync();
        await page.GotoAsync($"{ListPath}?q={marker}&sort=Title&dir=asc");

        List<string> first = await TitlesAsync(page);
        Assert.Equal(PageSize, first.Count);

        await page.Locator("nav.nws-pager a[rel=next]").ClickAsync();
        await Assertions.Expect(TitleCells(page)).ToHaveCountAsync(5);
        List<string> second = await TitlesAsync(page);
        Assert.Empty(first.Intersect(second, StringComparer.Ordinal));

        await page.Locator("nav.nws-pager a[rel=prev]").ClickAsync();
        await Assertions.Expect(TitleCells(page)).ToHaveCountAsync(PageSize);

        Assert.Equal(first, await TitlesAsync(page));
    }

    [Fact]
    public async Task ACopiedUrl_ReproducesSearchSortAndPositionInAFreshContext()
    {
        string marker = Marker();
        await SeedAsync(marker, PageSize + 5);

        IPage page = await SignedInPageAsync();
        await page.GotoAsync($"{ListPath}?q={marker}&sort=Title&dir=desc");
        await page.Locator("nav.nws-pager a[rel=next]").ClickAsync();
        await Assertions.Expect(page.Locator("nav.nws-pager a[rel=prev]")).ToHaveCountAsync(1);

        string url = page.Url;
        List<string> shown = await TitlesAsync(page);

        IBrowserContext other = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl });
        IPage copy = await other.NewPageAsync();
        await copy.GotoAsync("/signin");
        await copy.GotoAsync(url);

        Assert.Equal(shown, await TitlesAsync(copy));
        await Assertions.Expect(Header(copy, TitleColumn)).ToHaveAttributeAsync("aria-sort", "descending");
    }

    // A GET form submits its own controls and nothing else, so without the hidden
    // sort and dir fields a search would silently drop the reader's ordering.
    [Fact]
    public async Task Searching_KeepsTheOrderingTheReaderChose()
    {
        string marker = Marker();
        await SeedAsync(marker, 4);

        IPage page = await SignedInPageAsync();
        await page.GotoAsync($"{ListPath}?sort=Title&dir=desc");

        await page.FillAsync("form.nws-search input[name=q]", marker);
        await page.Locator("form.nws-search button[type=submit]").ClickAsync();

        await Assertions.Expect(TitleCells(page))
            .ToHaveTextAsync([.. Enumerable.Range(0, 4).Select(i => Title(marker, i)).Reverse()]);
    }
}
