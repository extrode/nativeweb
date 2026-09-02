using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// Spec 023 in Chromium against the published binary: the checkbox list and
/// the multi-select edit one relation from its two sides, the category
/// dropdown fills a list cell, and Archived is a 404 on the public site.
/// </summary>
/// <remarks>
/// The public pages are read with an <c>Authorization</c> header on the
/// browser context, for the reason the integration tier's
/// <c>AdminRelationsTests</c> gives: the output cache holds a public page for
/// sixty seconds and never caches an authorised request, so every read here
/// sees the store as the edit just left it. Chromium has a cache of its own,
/// and the same sixty seconds in the response header would let it answer a
/// second visit to a tag page without asking the server; routing every
/// request through Playwright switches that cache off.
/// </remarks>
[Collection(ScaffoldE2eFixture.Name)]
public sealed class AdminRelationsTests(InMemorySampleFixture app, BrowserFixture browsers)
{
    private async Task<IPage> SignedInPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                BaseURL = app.BaseUrl,
                ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer uncached" },
            });

        await context.RouteAsync("**/*", route => route.ContinueAsync());

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/signin");
        return page;
    }

    // Creates a Published article whose slug is its title, and answers its key.
    private static async Task<(string Title, string Id)> CreatePublishedAsync(IPage page, params string[] tags)
    {
        string title = $"e2e-rel-{Guid.NewGuid():N}";

        await page.GotoAsync("/admin/articles/new");
        await page.FillAsync("input[name=title]", title);
        await page.FillAsync("input[name=author]", "playwright");
        await page.FillAsync("textarea[name=body]", "Created through the generated form.");
        await page.SelectOptionAsync("select[name=status]", "Published");
        foreach (string tag in tags)
        {
            await page.CheckAsync($"input[name=tags][value={tag}]");
        }

        await page.ClickAsync("form.nws-form button[type=submit]");

        string path = new Uri(page.Url).AbsolutePath;
        Assert.Matches("/admin/articles/[^/]+$", path);
        return (title, path[(path.LastIndexOf('/') + 1)..]);
    }

    [Fact]
    public async Task TickTwoTags_FollowTheTagLink_ThenDeselectTheArticleFromTheTagSide()
    {
        IPage page = await SignedInPageAsync();
        (string title, string id) = await CreatePublishedAsync(page);

        await page.CheckAsync("input[name=tags][value=aot]");
        await page.CheckAsync("input[name=tags][value=htmx]");
        await page.ClickAsync("form.nws-form button[type=submit]");
        await page.GotoAsync($"/admin/articles/{id}");
        await Assertions.Expect(page.Locator("input[name=tags][value=htmx]")).ToBeCheckedAsync();

        await page.GotoAsync($"/articles/{title}");
        await page.ClickAsync("a.tag[href='/tags/htmx']");
        Assert.EndsWith("/tags/htmx", page.Url, StringComparison.Ordinal);
        await Assertions.Expect(page.GetByText(title)).ToBeVisibleAsync();

        await page.GotoAsync("/admin/tags/htmx");
        IReadOnlyList<string> held = await page.EvalOnSelectorAllAsync<string[]>(
            "select[name=articles] option:checked", "options => options.map(o => o.value)");
        Assert.Contains(id, held);
        await page.SelectOptionAsync("select[name=articles]", held.Where(h => h != id).ToArray());
        await page.ClickAsync("form.nws-form button[type=submit]");

        await page.GotoAsync($"/admin/articles/{id}");
        await Assertions.Expect(page.Locator("input[name=tags][value=htmx]")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("input[name=tags][value=aot]")).ToBeCheckedAsync();

        await page.GotoAsync("/tags/htmx");
        await Assertions.Expect(page.GetByText(title)).ToHaveCountAsync(0);
        await page.GotoAsync("/tags/aot");
        await Assertions.Expect(page.GetByText(title)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PickACategory_ThenArchive_AndThePublicPageIs404()
    {
        IPage page = await SignedInPageAsync();
        (string title, string id) = await CreatePublishedAsync(page);

        IResponse? live = await page.GotoAsync($"/articles/{title}");
        Assert.Equal(200, live!.Status);

        await page.GotoAsync($"/admin/articles/{id}");
        await page.SelectOptionAsync("select[name=categoryKey]", "guides");
        await page.ClickAsync("form.nws-form button[type=submit]");

        await page.GotoAsync($"/admin/articles?q={Uri.EscapeDataString(title)}");
        ILocator row = page.Locator("table.nws-table tbody tr").Filter(new LocatorFilterOptions { HasText = title });
        await Assertions.Expect(row.Locator("td", new LocatorLocatorOptions { HasText = "Guides" })).ToHaveCountAsync(1);

        await page.GotoAsync($"/admin/articles/{id}");
        await page.SelectOptionAsync("select[name=status]", "Archived");
        await page.ClickAsync("form.nws-form button[type=submit]");
        await page.GotoAsync($"/admin/articles/{id}");
        await Assertions.Expect(page.Locator("select[name=status]")).ToHaveValueAsync("Archived");

        IResponse? archived = await page.GotoAsync($"/articles/{title}");
        Assert.Equal(404, archived!.Status);
    }
}
