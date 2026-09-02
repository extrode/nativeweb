using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// The public site of spec 022 walked in a real browser against the NativeAOT
/// binary: home, an article, one of its tags, and home again through the brand.
/// </summary>
/// <remarks>
/// The pages carry no JavaScript, so what this tier adds is the reason
/// <see cref="PublicFeedTests"/> gives: the templates, the readers and the
/// paragraph split all run here inside the published binary, where anything the
/// trimmer took would show as a missing heading. And every hop below is a click
/// on a link the previous page rendered, so a route that exists but is linked
/// nowhere fails here and nowhere else.
/// </remarks>
[Collection(ScaffoldE2eFixture.Name)]
public sealed class PublicSiteTests(InMemorySampleFixture app, BrowserFixture browsers)
{
    private async Task<IPage> AnonymousPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl });

        return await context.NewPageAsync();
    }

    [Fact]
    public async Task HomeToArticleToTagAndBackThroughTheBrand()
    {
        IPage page = await AnonymousPageAsync();

        await page.GotoAsync("/");
        await Assertions.Expect(page.Locator("h1.hero__title")).ToHaveTextAsync("Server-rendered HTML, one native binary");
        await Assertions.Expect(page.Locator("h2.articles__title a")).ToHaveCountAsync(6);

        await page.ClickAsync("h2.articles__title a:has-text(\"Lean Native Web\")");
        Assert.Equal("/articles/lean-native-web", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator("h1.page__title")).ToHaveTextAsync("Lean Native Web");
        await Assertions.Expect(page.Locator(".article__body p")).ToHaveCountAsync(1);

        await page.ClickAsync("a.tag:has-text(\"NativeAOT\")");
        Assert.Equal("/tags/aot", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator("h1.page__title")).ToHaveTextAsync("NativeAOT");
        await Assertions.Expect(page.Locator("h2.articles__title a")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator("h2.articles__title a", new PageLocatorOptions { HasTextString = "Lean Native Web" })).ToHaveCountAsync(1);

        await page.ClickAsync("a.topbar__brand");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator("h1.hero__title")).ToHaveTextAsync("Server-rendered HTML, one native binary");
    }

    [Fact]
    public async Task TheTagsNavLinkListsEveryTagWithItsCount()
    {
        IPage page = await AnonymousPageAsync();

        await page.GotoAsync("/");
        await page.ClickAsync("nav.topbar__nav a:has-text(\"Tags\")");

        Assert.Equal("/tags", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator("h1.page__title")).ToHaveTextAsync("Tags");
        await Assertions.Expect(page.Locator("ul.tags--index li")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("li:has(a.tag:has-text(\"PostgreSQL\")) .tags__count")).ToHaveTextAsync("0");
    }

    [Fact]
    public async Task ADraftSlugIsNotFound()
    {
        IPage page = await AnonymousPageAsync();

        IResponse? response = await page.GotoAsync("/articles/scaffolding-without-reflection");

        Assert.NotNull(response);
        Assert.Equal(404, response.Status);
    }
}
