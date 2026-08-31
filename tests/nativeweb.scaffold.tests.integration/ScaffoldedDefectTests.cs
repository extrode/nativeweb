using System.Net;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The T11 defect batch, through the real host: each fact fails against the
/// generator as it stood on 2026-08-31.
/// </summary>
/// <remarks>
/// These sit beside the generator's own emission tests rather than replacing them.
/// An emission test says the right call was written; these say the response a
/// browser gets changed, which is the part a consumer experiences. Both layers,
/// because a correct call in the wrong place passes the first and fails the second.
/// </remarks>
public sealed class ScaffoldedDefectTests
{
    // Defect: the admin index listed every model to everyone. Article is
    // [Auth(Role = "admin")] and Tag is open, so an anonymous visitor could reach
    // the index (Tag makes it legitimately anonymous) and read off the name and URL
    // of a model they would then be refused.
    [Fact]
    public async Task AdminIndex_HidesAGuardedModelFromAnAnonymousVisitor()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("/admin/articles", html, StringComparison.Ordinal);
        Assert.Contains("/admin/tags", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminIndex_ShowsTheGuardedModelToAnAdmin()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin", UriKind.Relative));

        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("/admin/articles", html, StringComparison.Ordinal);
        Assert.Contains("/admin/tags", html, StringComparison.Ordinal);
    }

    // Defect: every converter answers malformed input with a fallback, so "abc" in
    // the views field was stored as 0 and the response was the same 302 a good
    // submission gets.
    [Fact]
    public async Task Create_WithAMalformedNumber_ShowsAnErrorAndWritesNothing()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", "Malformed number"),
            new("author", "xunit"),
            new("body", "hello"),
            new("viewCount", "abc"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Views must be a whole number.", html, StringComparison.Ordinal);

        using HttpResponseMessage list = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));
        Assert.DoesNotContain("Malformed number", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // Defect: the rejected form was rebuilt from the converted item, so the visitor
    // read "Views must be a whole number." next to a field showing 0 -- the
    // converter's fallback, not what they typed -- under the heading "Edit Article",
    // for a row that had never existed. Both halves are one fix: the error path
    // hands the submitted form down and passes no row.
    [Fact]
    public async Task Create_Rejected_RedisplaysWhatWasTypedUnderTheCreateHeading()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", "Kept on rejection"),
            new("author", "xunit"),
            new("body", "hello"),
            new("viewCount", "abc"));

        string html = await response.Content.ReadAsStringAsync();

        // What was typed, in the fields, including the value that failed.
        Assert.Contains("value=\"Kept on rejection\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"abc\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"0\"", html, StringComparison.Ordinal);

        // And the page is still the create page: same action, no id in it.
        Assert.Contains("action=\"/admin/articles\"", html, StringComparison.Ordinal);
        Assert.Contains(">Create<", html, StringComparison.Ordinal);
    }

    // Blank is not malformed: the documented fallback still applies, so an omitted
    // number is 0 rather than an error. Stated as a fact because the fix above could
    // so easily have taken this with it.
    [Fact]
    public async Task Create_WithABlankNumber_StillTakesTheFallback()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", "Blank number"),
            new("author", "xunit"),
            new("body", "hello"),
            new("viewCount", ""));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        using HttpResponseMessage list = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));
        Assert.Contains("Blank number", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // Defect: DeleteAsync's false was discarded and the handler redirected, so
    // deleting a row that was never there looked exactly like deleting one that was.
    [Fact]
    public async Task Delete_OfARowThatIsNotThere_IsNotFound()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/tags",
            "/admin/tags/no-such-tag/delete");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OfARowThatIsThere_StillRedirects()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/tags",
            "/admin/tags/aot/delete");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/admin/tags", response.Headers.Location?.ToString());
    }

    // Defect: page was clamped low and not high, so (page - 1) * PageSize overflowed
    // to a negative skip on an anonymous GET. What a store does with a negative skip
    // is the store's business -- ArgumentOutOfRangeException from Skip(), a
    // PostgreSQL error from OFFSET -- and none of it should be reachable from a
    // query string.
    // 200000000 is the value that matters and it was chosen by arithmetic, not by
    // looking absurd: (200000000 - 1) * 20 is 3,999,999,980, which wraps a signed
    // int to -294,967,316. 2000000000 wraps too, but lands on a positive number, so
    // a test using only that one passes against the unclamped code and proves
    // nothing -- which is what the first cut of this test did.
    [Theory]
    [InlineData("200000000")]
    [InlineData("2000000000")]
    [InlineData("999999999999999999999")]
    [InlineData("-5")]
    [InlineData("not-a-number")]
    public async Task List_WithAnAbsurdPageNumber_IsStillAPage(string page)
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"/admin/tags?page={page}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Defect: [ScaffoldSearch] reached the placeholder text and nothing else. The
    // sample's in-memory store now matches on the fields the generator hands it, so
    // this searches Body -- a property that is searchable and NOT in the list view,
    // which is the combination a store re-declaring its own columns gets wrong.
    [Fact]
    public async Task Search_MatchesEverySearchableProperty()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage seeded = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", "Untitled"),
            new("author", "xunit"),
            new("body", "distinctive-body-term"),
            new("viewCount", "0"));

        Assert.Equal(HttpStatusCode.Found, seeded.StatusCode);

        using HttpResponseMessage hit = await client.GetAsync(
            new Uri("/admin/articles?q=distinctive-body-term", UriKind.Relative));
        Assert.Contains("Untitled", await hit.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using HttpResponseMessage miss = await client.GetAsync(
            new Uri("/admin/articles?q=nothing-matches-this", UriKind.Relative));
        Assert.DoesNotContain("Untitled", await miss.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
