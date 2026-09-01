using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// Keyset paging and sorting through the generated list, over PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The unit tier proves the cursor round-trips and that the in-memory store orders
/// the way it says. Neither reaches the thing this tier exists for: that the eight
/// static <c>.sql</c> files bind their parameters where the store thinks they do,
/// and that a row-value comparison against a <c>NULL</c> column behaves the way the
/// widened predicate in <c>ListByAuthorAsc.sql</c> claims. That defect is invisible
/// on a table small enough to fit one page, which is why every walk here spans
/// several.
/// </para>
/// <para>
/// Every test mints a marker, puts it in the body of each row it inserts, and lists
/// with <c>?q=</c> set to it. The database is shared with every previous run, so
/// nothing here may assume an empty table.
/// </para>
/// </remarks>
public sealed class JauntyKeysetTests
{
    private const int PageSize = 20;

    private static string Marker() => $"kq{Guid.NewGuid():N}";

    private static string Title(string marker, int n) => $"{marker}-t{n:D2}";

    private static async Task SeedAsync(string marker, int count, Func<int, string?> author)
    {
        for (int i = 0; i < count; i++)
        {
            await SampleTestDatabase.InsertAsync($"{marker}-{i:D2}", Title(marker, i), author(i), marker);
        }
    }

    private static async Task<string> ListAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static List<string> TitlesIn(string marker, string html)
    {
        List<string> found = [];

        foreach (Match m in Regex.Matches(html, $"{Regex.Escape(marker)}-t[0-9]{{2}}"))
        {
            if (found.Count == 0 || !string.Equals(found[^1], m.Value, StringComparison.Ordinal))
            {
                found.Add(m.Value);
            }
        }

        return found;
    }

    private static string? LinkWithRel(string html, string rel)
    {
        Match m = Regex.Match(html, $"<a rel=\"{rel}\" href=\"([^\"]+)\"");
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    // Bounded, and the bound is the point. A cursor that does not advance -- the shape
    // this suite exists to catch -- makes the Next link return the page it came from,
    // forever. Unbounded, that hangs the run instead of failing it, which is how a
    // defect gets reported as a flaky suite.
    private static async Task<List<string>> WalkAsync(HttpClient client, string marker, string first)
    {
        List<string> seen = [];
        string? path = first;

        for (int page = 0; path is not null; page++)
        {
            Assert.True(page < 32, $"walk did not terminate after {page} pages; the cursor is not advancing");

            string html = await ListAsync(client, path);
            seen.AddRange(TitlesIn(marker, html));
            path = LinkWithRel(html, "next");
        }

        return seen;
    }

    [Fact]
    public async Task ByTitle_TheTwoDirectionsAreMirrorImages()
    {
        string marker = Marker();
        await SeedAsync(marker, 5, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        List<string> up = TitlesIn(marker, await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=asc"));
        List<string> down = TitlesIn(marker, await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=desc"));

        Assert.Equal(5, up.Count);
        Assert.Equal(up, [.. up.Order(StringComparer.Ordinal)]);
        Assert.Equal(up, [.. Enumerable.Reverse(down)]);
    }

    [Fact]
    public async Task AWalkAcrossSeveralPages_ReturnsEachRowExactlyOnce()
    {
        string marker = Marker();
        await SeedAsync(marker, (PageSize * 2) + 3, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        List<string> walked = await WalkAsync(client, marker, $"/admin/articles?q={marker}&sort=Title&dir=asc");

        Assert.Equal((PageSize * 2) + 3, walked.Count);
        Assert.Equal(walked.Count, walked.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(walked, [.. walked.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task AWalkOverRowsSharingASortValue_ReturnsEachRowExactlyOnce()
    {
        string marker = Marker();
        await SeedAsync(marker, (PageSize * 2) + 3, _ => "one author for all of them");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        List<string> walked = await WalkAsync(client, marker, $"/admin/articles?q={marker}&sort=Author&dir=asc");

        Assert.Equal((PageSize * 2) + 3, walked.Count);
        Assert.Equal(walked.Count, walked.Distinct(StringComparer.Ordinal).Count());
    }

    // The one a smaller table hides. Under NULLS LAST the author-less rows are a
    // contiguous block at the end, and the page boundary falls inside it: a plain
    // (author, id) > (@a, @id) is NULL rather than true for every one of them, so
    // WHERE drops the lot and the walk ends early with rows never shown.
    [Fact]
    public async Task AWalkAcrossTheNullAuthorBoundary_ReturnsEveryRow()
    {
        string marker = Marker();
        int count = (PageSize * 2) + 3;
        await SeedAsync(marker, count, i => i % 2 == 0 ? null : $"author-{i:D2}");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        List<string> up = await WalkAsync(client, marker, $"/admin/articles?q={marker}&sort=Author&dir=asc");
        List<string> down = await WalkAsync(client, marker, $"/admin/articles?q={marker}&sort=Author&dir=desc");

        Assert.Equal(count, up.Count);
        Assert.Equal(count, up.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(count, down.Count);
        Assert.Equal(count, down.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(up, [.. Enumerable.Reverse(down)]);
    }

    [Fact]
    public async Task ByCreatedAt_PagesInTimestampOrder()
    {
        string marker = Marker();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int count = PageSize + 5;

        for (int i = 0; i < count; i++)
        {
            await SampleTestDatabase.InsertAsync(
                $"{marker}-{i:D2}",
                Title(marker, i),
                "xunit",
                marker,
                true,
                start.AddMinutes(i));
        }

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        List<string> walked = await WalkAsync(client, marker, $"/admin/articles?q={marker}&sort=CreatedAt&dir=asc");

        Assert.Equal(count, walked.Count);
        Assert.Equal([.. Enumerable.Range(0, count).Select(i => Title(marker, i))], walked);
    }

    // Two rows a second apart used to collide here, because the cursor carried the
    // display form of the timestamp, which is minute-resolution.
    [Fact]
    public async Task TimestampsInsideOneMinute_StillPageApart()
    {
        string marker = Marker();
        var start = new DateTime(2026, 2, 2, 3, 4, 0, DateTimeKind.Utc);
        int count = PageSize + 4;

        for (int i = 0; i < count; i++)
        {
            await SampleTestDatabase.InsertAsync(
                $"{marker}-{i:D2}",
                Title(marker, i),
                "xunit",
                marker,
                true,
                start.AddSeconds(i));
        }

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        List<string> walked = await WalkAsync(client, marker, $"/admin/articles?q={marker}&sort=CreatedAt&dir=asc");

        Assert.Equal([.. Enumerable.Range(0, count).Select(i => Title(marker, i))], walked);
    }

    [Fact]
    public async Task Previous_FromTheSecondPage_ReturnsTheFirstPage()
    {
        string marker = Marker();
        await SeedAsync(marker, PageSize + 5, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string firstPath = $"/admin/articles?q={marker}&sort=Title&dir=asc";
        string first = await ListAsync(client, firstPath);
        List<string> firstTitles = TitlesIn(marker, first);

        string second = await ListAsync(client, LinkWithRel(first, "next")!);
        Assert.Equal(5, TitlesIn(marker, second).Count);

        string back = await ListAsync(client, LinkWithRel(second, "prev")!);

        Assert.Equal(firstTitles, TitlesIn(marker, back));
    }

    [Fact]
    public async Task TheFirstPage_OffersNoPreviousAndTheLastNoNext()
    {
        string marker = Marker();
        await SeedAsync(marker, PageSize + 5, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string first = await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=asc");
        Assert.Null(LinkWithRel(first, "prev"));
        Assert.NotNull(LinkWithRel(first, "next"));

        string last = await ListAsync(client, LinkWithRel(first, "next")!);
        Assert.NotNull(LinkWithRel(last, "prev"));
        Assert.Null(LinkWithRel(last, "next"));
    }

    // What offset paging cannot do. A row inserted ahead of the reader shifts every
    // offset after it by one, so the row that was last on page one becomes first on
    // page two and one row is never shown.
    [Fact]
    public async Task ARowInsertedAheadOfTheReader_HidesNothingAlreadyPast()
    {
        string marker = Marker();
        await SeedAsync(marker, PageSize + 5, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string first = await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=asc");
        List<string> firstTitles = TitlesIn(marker, first);

        await SampleTestDatabase.InsertAsync($"{marker}-ins", $"{marker}-t00a", "xunit", marker);

        List<string> second = TitlesIn(marker, await ListAsync(client, LinkWithRel(first, "next")!));

        Assert.Equal([.. Enumerable.Range(PageSize, 5).Select(i => Title(marker, i))], second);
        Assert.Empty(firstTitles.Intersect(second, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("sort=Body&dir=asc")]
    [InlineData("sort=Nonexistent&dir=asc")]
    [InlineData("sort=id;%20drop%20table%20articles&dir=asc")]
    [InlineData("sort=Title&dir=sideways")]
    public async Task AnUnusableOrdering_RendersRatherThanFailing(string query)
    {
        string marker = Marker();
        await SeedAsync(marker, 3, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        Assert.Equal(3, TitlesIn(marker, await ListAsync(client, $"/admin/articles?q={marker}&{query}")).Count);
    }

    [Theory]
    [InlineData("mangled")]
    [InlineData("!!!!")]
    [InlineData("AQAAAAU")]
    public async Task AMangledCursor_RendersTheFirstPage(string after)
    {
        string marker = Marker();
        await SeedAsync(marker, PageSize + 5, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        List<string> clean = TitlesIn(marker, await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=asc"));
        List<string> mangled = TitlesIn(
            marker,
            await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=asc&after={after}"));

        Assert.Equal(clean, mangled);
    }

    // The store parses the timestamp, so an unusable one has to reset the direction as
    // well as the position. Only the mirrored query is wrong here, and it is wrong
    // quietly: it runs from the top and gets reversed, so the reader asking to step
    // back from a stale cursor lands on the LAST page instead of the first.
    [Theory]
    [InlineData("not-a-date")]
    [InlineData(null)]
    public async Task ABackwardCreatedAtCursorThatWillNotParse_RendersTheFirstPage(string? value)
    {
        string marker = Marker();
        await SeedAsync(marker, PageSize + 5, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string token = new ScaffoldCursor(
            "CreatedAt",
            ScaffoldSortDirection.Ascending,
            value,
            $"{marker}-00",
            true).Encode();

        List<string> clean = TitlesIn(marker, await ListAsync(client, $"/admin/articles?q={marker}&sort=CreatedAt&dir=asc"));
        List<string> stale = TitlesIn(
            marker,
            await ListAsync(client, $"/admin/articles?q={marker}&sort=CreatedAt&dir=asc&after={token}"));

        Assert.Equal(clean, stale);
    }

    // A cursor can land on nothing at all -- follow Next, and the rows behind it are
    // deleted before the request arrives. Built only from the page's own rows, both
    // tokens would be null, the pager would render as nothing, and the reader would
    // have no way back except editing the URL.
    [Fact]
    public async Task ACursorPastTheEnd_StillOffersAWayBack()
    {
        string marker = Marker();
        await SeedAsync(marker, 3, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string token = new ScaffoldCursor(
            "Title",
            ScaffoldSortDirection.Ascending,
            $"{marker}-tzz",
            $"{marker}-zz",
            false).Encode();

        string html = await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=asc&after={token}");

        Assert.Empty(TitlesIn(marker, html));
        Assert.NotNull(LinkWithRel(html, "prev"));
    }

    // A cursor is issued against one ordering. Carrying it to another would put the
    // reader at a position that means nothing there, so it is dropped.
    [Fact]
    public async Task ACursorCarriedToAnotherOrdering_IsDropped()
    {
        string marker = Marker();
        await SeedAsync(marker, PageSize + 5, i => $"author-{i:D2}");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string first = await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=asc");
        string next = LinkWithRel(first, "next")!;
        string token = Regex.Match(next, "after=([^&]+)").Groups[1].Value;

        List<string> byAuthor = TitlesIn(
            marker,
            await ListAsync(client, $"/admin/articles?q={marker}&sort=Author&dir=asc&after={token}"));

        Assert.Equal(PageSize, byAuthor.Count);
        Assert.Equal(Title(marker, 0), byAuthor[0]);
    }

    [Fact]
    public async Task TheReportedTotal_CountsEveryMatchingRowNotThePage()
    {
        string marker = Marker();
        int count = PageSize + 5;
        await SeedAsync(marker, count, _ => "xunit");

        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string first = await ListAsync(client, $"/admin/articles?q={marker}&sort=Title&dir=asc");

        Assert.Contains(
            count.ToString(CultureInfo.InvariantCulture) + " items",
            first,
            StringComparison.Ordinal);
    }
}
