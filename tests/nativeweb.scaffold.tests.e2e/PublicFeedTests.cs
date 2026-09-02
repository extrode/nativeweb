using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// <c>GET /articles</c> in a real browser, against the NativeAOT binary, over
/// PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The page carries no JavaScript, so the usual argument for this tier — that
/// only a browser can drive a dialog or a script — does not apply. What applies
/// instead is the first of the three reasons
/// <see cref="JauntyBackedAdminTests"/> gives: **this is the only place the
/// feature runs inside a NativeAOT binary.** Everything below it is a JIT-hosted
/// test process. `ListPublishedAsync` is generated code doing reflection-free
/// mapping, `HtmlEncoder.Default` is resolved at runtime, and either could work
/// perfectly under the JIT and be trimmed out of the published binary. A green
/// integration tier says nothing about that; only a request to the real
/// executable does.
/// </para>
/// <para>
/// The second reason is the anonymous visit. The integration tier asserts a 200
/// with <c>AllowAutoRedirect = false</c>, which is the right way to observe a
/// guard but the wrong way to observe its absence — a browser *follows*
/// redirects, so if <c>/articles</c> ever gained an authorization filter, this is
/// the tier that would land on <c>/signin</c> instead and say so.
/// </para>
/// <para>
/// Rows are dated into the future and removed afterwards, for the reason
/// <see cref="SampleE2eDatabase.DeleteByIdPrefixAsync"/> records.
/// </para>
/// </remarks>
[Collection(ScaffoldE2eFixture.Name)]
public sealed class PublicFeedTests(JauntySampleFixture app, BrowserFixture browsers) : IClassFixture<LeakedFeedRowsSweep>
{
    /// <summary>A context that has never visited <c>/signin</c> and holds no cookie.</summary>
    private async Task<IPage> AnonymousPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl });

        return await context.NewPageAsync();
    }

    [Fact]
    public async Task Feed_UnderNativeAot_ShowsPublishedRowsFromPostgresAndHidesDrafts()
    {
        string prefix = $"e2e-{Guid.NewGuid():N}"[..15];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            await SampleE2eDatabase.InsertAsync($"{prefix}-p", $"{prefix}-published", true, future);

            // Newer than the published row, so a missing WHERE would put it first
            // on the page rather than merely somewhere on it.
            await SampleE2eDatabase.InsertAsync($"{prefix}-d", $"{prefix}-draft", false, future.AddMinutes(1));

            IPage page = await AnonymousPageAsync();
            await page.GotoAsync("/articles");

            await Assertions.Expect(page.GetByText($"{prefix}-published")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText($"{prefix}-draft")).ToHaveCountAsync(0);
        }
        finally
        {
            await SampleE2eDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }

    /// <summary>
    /// The ordering, read off the rendered DOM rather than out of the HTML string:
    /// the two rows are two <c>li</c> elements, and the newer one is above the
    /// older one.
    /// </summary>
    /// <remarks>
    /// Positions relative to each other, not absolute DOM slots. This ran green
    /// for a month asserting <c>titles.First</c> and <c>titles.Nth(1)</c>, then
    /// failed on 2026-09-01 without the feed changing: the integration tier dates
    /// its own feed rows five years out into the same table, this assembly runs in
    /// parallel with it, and a leaked row landed between the two rows below. An
    /// absolute slot is a claim about every other row in <c>articles</c>, which is
    /// not what this test is about and not something it can control.
    /// </remarks>
    [Fact]
    public async Task Feed_UnderNativeAot_OrdersNewestFirst()
    {
        string prefix = $"e2e-{Guid.NewGuid():N}"[..15];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            // Inserted oldest first, so page order cannot be insertion order.
            await SampleE2eDatabase.InsertAsync($"{prefix}-1", $"{prefix}-older", true, future);
            await SampleE2eDatabase.InsertAsync($"{prefix}-2", $"{prefix}-newer", true, future.AddHours(1));

            IPage page = await AnonymousPageAsync();
            await page.GotoAsync("/articles");

            ILocator titles = page.Locator("li.articles__item h2.articles__title");
            await Assertions.Expect(titles.GetByText($"{prefix}-newer")).ToBeVisibleAsync();

            List<string> rendered = [.. (await titles.AllTextContentsAsync()).Select(t => t.Trim())];
            int newer = rendered.IndexOf($"{prefix}-newer");
            int older = rendered.IndexOf($"{prefix}-older");

            Assert.True(newer >= 0, $"The newer row should be on the page. Titles: {string.Join(", ", rendered)}");
            Assert.True(older >= 0, $"The older row should be on the page. Titles: {string.Join(", ", rendered)}");
            Assert.True(newer < older, $"The newer row should render above the older one. Titles: {string.Join(", ", rendered)}");
        }
        finally
        {
            await SampleE2eDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }

    /// <summary>
    /// A browser with no session reaches the page and stays on it. Every
    /// scaffolded article route would have bounced to <c>/signin</c> by now.
    /// </summary>
    [Fact]
    public async Task Feed_IsReachedByABrowserWithNoSession()
    {
        IPage page = await AnonymousPageAsync();
        await page.GotoAsync("/articles");

        // The URL first: a redirect to /signin would still render a page, and
        // asserting only on the heading would not notice which page.
        Assert.EndsWith("/articles", page.Url, StringComparison.Ordinal);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Articles" }))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// The encoding fact, asserted where it finally means something: not that the
    /// bytes are escaped, but that the browser parsed them as text and did not
    /// execute them.
    /// </summary>
    /// <remarks>
    /// The unit and integration tiers both check the HTML for <c>&amp;lt;script&amp;gt;</c>,
    /// which is a claim about a string. Only here is there a real HTML parser and
    /// a real script engine to be wrong about it — an injected <c>script</c>
    /// element would be in the DOM, and the string assertions below it would have
    /// passed all the way up.
    /// </remarks>
    [Fact]
    public async Task Feed_UnderNativeAot_DoesNotExecuteMarkupFromTheDatabase()
    {
        string prefix = $"e2e-{Guid.NewGuid():N}"[..15];

        try
        {
            await SampleE2eDatabase.InsertAsync(
                $"{prefix}-x",
                $"{prefix}<script>window.__pwned = true</script>",
                true,
                DateTime.UtcNow.AddYears(5));

            IPage page = await AnonymousPageAsync();
            await page.GotoAsync("/articles");

            // No script element entered the DOM from the title...
            Assert.Equal(0, await page.Locator("li.articles__item script").CountAsync());

            // ...and nothing ran.
            Assert.False(await page.EvaluateAsync<bool>("() => window.__pwned === true"));

            // The title is on the page, as text.
            await Assertions.Expect(page.GetByText($"{prefix}<script>", new PageGetByTextOptions { Exact = false }))
                .ToBeVisibleAsync();
        }
        finally
        {
            await SampleE2eDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }

    /// <summary>
    /// The filter swaps the results in place rather than navigating: htmx posts
    /// the form, the server answers with the <c>#results</c> fragment alone, and
    /// the surrounding document survives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing below this tier can assert it. The integration tier can send
    /// <c>HX-Request</c> by hand and check the fragment came back, which proves the
    /// server half; it cannot prove htmx sent the header, found the target, or
    /// applied the swap, because there is no htmx in a
    /// <c>WebApplicationFactory</c>. Every failure this catches — a stale
    /// <c>hx-target</c>, a vendored library that did not load, the antiforgery
    /// header glue missing so the POST answers 400 — leaves the server tier green.
    /// </para>
    /// <para>
    /// The sentinel is the whole point of the last assertion. A full-page POST
    /// would also end with the right rows on screen and would look identical to a
    /// swap in every other check here; only a value hung off the window before the
    /// submit can tell the two apart, because a navigation would discard it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Feed_FilterSwapsTheResultsWithoutNavigating()
    {
        string prefix = $"e2e-{Guid.NewGuid():N}"[..15];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            await SampleE2eDatabase.InsertAsync($"{prefix}-k", $"{prefix}-keep", true, future);
            await SampleE2eDatabase.InsertAsync($"{prefix}-d", $"{prefix}-drop", true, future.AddMinutes(1));

            IPage page = await AnonymousPageAsync();
            await page.GotoAsync("/articles");
            await Assertions.Expect(page.GetByText($"{prefix}-drop")).ToBeVisibleAsync();

            await page.EvaluateAsync("() => { window.__survived = true; }");

            await page.FillAsync("#q", $"{prefix}-keep");
            await page.ClickAsync(".articles__filter button[type=submit]");

            await Assertions.Expect(page.GetByText($"{prefix}-keep")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText($"{prefix}-drop")).ToHaveCountAsync(0);

            // The swap target survived its own replacement: hx-swap="outerHTML"
            // replaces #results with a fragment whose own root carries id="results",
            // so a second filter still has somewhere to land.
            Assert.Equal(1, await page.Locator("#results").CountAsync());

            // Still the same document, and still the same URL.
            Assert.True(await page.EvaluateAsync<bool>("() => window.__survived === true"));
            Assert.EndsWith("/articles", page.Url, StringComparison.Ordinal);
        }
        finally
        {
            await SampleE2eDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }
}
