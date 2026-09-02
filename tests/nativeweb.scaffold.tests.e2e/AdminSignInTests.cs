using System.Text.RegularExpressions;

using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// The passphrase sign-in as an operator meets it on a deployed host (spec 020): the
/// binary running as Production, a real browser, the admin reached through the form.
/// </summary>
[Collection(ScaffoldE2eFixture.Name)]
public sealed class AdminSignInTests(PassphraseSampleFixture app, BrowserFixture browsers)
{
    private async Task<IPage> NewPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl, IgnoreHTTPSErrors = true });

        return await context.NewPageAsync();
    }

    [Fact]
    public async Task TheAdminIsBehindTheFormAndTheRightPassphraseOpensIt()
    {
        IPage page = await NewPageAsync();

        await page.GotoAsync("/admin");
        await Assertions.Expect(page.Locator("a[href='/admin/articles']")).ToHaveCountAsync(0);

        await page.GotoAsync("/admin/articles");
        Assert.Equal("/signin", new Uri(page.Url).AbsolutePath);

        await page.FillAsync("input[name=passphrase]", "not it");
        await page.ClickAsync("form.signin button[type=submit]");
        await Assertions.Expect(page.Locator("p[role=alert]")).ToHaveTextAsync("Not accepted.");
        Assert.Equal("/signin", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator("input[name=return]")).ToHaveValueAsync("/admin/articles");

        await page.FillAsync("input[name=passphrase]", PassphraseSampleFixture.Secret);
        await page.ClickAsync("form.signin button[type=submit]");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/admin/articles$"));
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Articles");

        await page.GotoAsync("/admin");
        await Assertions.Expect(page.Locator("a[href='/admin/articles']")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("a[href='/admin/tags']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task SigningOutFromTheSignInPageLocksTheAdminAgain()
    {
        IPage page = await NewPageAsync();

        await page.GotoAsync("/signin");
        await page.FillAsync("input[name=passphrase]", PassphraseSampleFixture.Secret);
        await page.ClickAsync("form.signin button[type=submit]");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/admin$"));

        await page.GotoAsync("/signin");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Signed in as operator");
        await page.ClickAsync("form[action='/signout'] button[type=submit]");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/admin$"));
        await Assertions.Expect(page.Locator("a[href='/admin/articles']")).ToHaveCountAsync(0);

        await page.GotoAsync("/admin/articles");
        Assert.Equal("/signin", new Uri(page.Url).AbsolutePath);
    }
}
