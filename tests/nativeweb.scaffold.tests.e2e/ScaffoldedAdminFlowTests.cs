using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>
/// The scaffolded admin UI in a real browser.
/// </summary>
/// <remarks>
/// This tier earns its place on one thing the two below it cannot reach. The
/// generated delete button sits in a form carrying
/// <c>onsubmit="return confirm('Delete this record?')"</c>, and an
/// <see cref="System.Net.Http.HttpClient"/> has no JavaScript engine, so the
/// integration tier POSTs straight past that guard and would pass identically if
/// the attribute were deleted. Only a browser can say whether dismissing the
/// dialog actually stops the delete.
///
/// Nothing in <c>apps/sample</c> writes a route, a handler, a form or a view:
/// every URL and every element below was emitted from the two attributes on
/// <c>apps/sample/Models.cs</c>, so a failure here is a generator defect.
/// </remarks>
[Collection(ScaffoldE2eFixture.Name)]
public sealed class ScaffoldedAdminFlowTests(InMemorySampleFixture app, BrowserFixture browsers)
{
    private const string ListPath = "/admin/articles";

    /// <summary>A context already carrying the demo admin cookie.</summary>
    private async Task<IPage> SignedInPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl });

        IPage page = await context.NewPageAsync();

        // /signin grants the admin role and redirects to /admin. It is
        // Development-only, which is why the fixture pins the environment.
        await page.GotoAsync("/signin");
        return page;
    }

    /// <summary>Creates one article through the real form and returns its title.</summary>
    private static async Task<string> CreateArticleAsync(IPage page)
    {
        string title = $"e2e-{Guid.NewGuid():N}";

        await page.GotoAsync($"{ListPath}/new");
        await page.FillAsync("input[name=title]", title);
        await page.FillAsync("input[name=author]", "playwright");
        await page.FillAsync("textarea[name=body]", "Created through the generated form.");
        await page.ClickAsync("form.nws-form button[type=submit]");

        return title;
    }

    /// <summary>Locates the delete button in the row whose title cell matches.</summary>
    private static ILocator DeleteButtonFor(IPage page, string title) =>
        page.Locator("table.nws-table tbody tr")
            .Filter(new LocatorFilterOptions { HasText = title })
            .Locator("button.nws-btn--danger");

    /// <summary>
    /// The whole CRUD journey driven as a person would drive it: every navigation
    /// is a real click, every submit a real form post carrying the antiforgery
    /// field the page rendered.
    /// </summary>
    [Fact]
    public async Task CreateEditDelete_RoundTripsThroughTheGeneratedForms()
    {
        IPage page = await SignedInPageAsync();

        string title = await CreateArticleAsync(page);

        // Create redirected back to the list and the row is on it.
        Assert.EndsWith(ListPath, page.Url, StringComparison.Ordinal);
        await Assertions.Expect(page.GetByText(title)).ToBeVisibleAsync();

        // Edit through the row's own Edit link, not a hand-built URL: that the link
        // points at the right key is part of what is under test.
        await page.Locator("table.nws-table tbody tr")
            .Filter(new LocatorFilterOptions { HasText = title })
            .Locator("a.nws-btn--sm")
            .First.ClickAsync();

        await page.FillAsync("input[name=author]", "edited-by-playwright");
        await page.ClickAsync("form.nws-form button[type=submit]");

        await Assertions.Expect(page.GetByText("edited-by-playwright").First).ToBeVisibleAsync();

        // Delete, accepting the confirm dialog.
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await DeleteButtonFor(page, title).ClickAsync();

        await Assertions.Expect(page.GetByText(title)).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The reason this tier exists. Playwright dismisses dialogs by default, so
    /// this is what a person clicking "Cancel" gets — and the row must survive it.
    /// </summary>
    [Fact]
    public async Task Delete_WhenTheConfirmDialogIsDismissed_LeavesTheRowAlone()
    {
        IPage page = await SignedInPageAsync();
        string title = await CreateArticleAsync(page);

        bool asked = false;
        page.Dialog += async (_, dialog) =>
        {
            asked = true;
            Assert.Equal("Delete this record?", dialog.Message);
            await dialog.DismissAsync();
        };

        await DeleteButtonFor(page, title).ClickAsync();

        // Asserted before the row check: if the dialog never appeared, the row
        // surviving would prove nothing about the guard, and the failure should say
        // which of the two things went wrong.
        Assert.True(asked, "The delete form did not raise a confirm dialog at all.");

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText(title)).ToBeVisibleAsync();
    }

    /// <summary>
    /// The other half of the same claim. Without this, the test above would pass on
    /// a build where the delete route was broken outright.
    /// </summary>
    [Fact]
    public async Task Delete_WhenTheConfirmDialogIsAccepted_RemovesTheRow()
    {
        IPage page = await SignedInPageAsync();
        string title = await CreateArticleAsync(page);

        bool asked = false;
        page.Dialog += async (_, dialog) =>
        {
            asked = true;
            await dialog.AcceptAsync();
        };

        await DeleteButtonFor(page, title).ClickAsync();

        Assert.True(asked, "The delete form did not raise a confirm dialog at all.");

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText(title)).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The generated search form is a plain GET, so this is less about JavaScript
    /// than about the form's action and method surviving a real submit — a browser
    /// resolves both, where the integration tier builds the query string itself and
    /// therefore cannot be wrong about them.
    /// </summary>
    [Fact]
    public async Task Search_SubmittedFromTheBrowser_FiltersTheTable()
    {
        IPage page = await SignedInPageAsync();
        string title = await CreateArticleAsync(page);
        string other = await CreateArticleAsync(page);

        await page.FillAsync("input[name=q]", title);
        await page.ClickAsync("form.nws-search button[type=submit]");

        Assert.Contains("q=", page.Url, StringComparison.Ordinal);
        await Assertions.Expect(page.GetByText(title)).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText(other)).ToHaveCountAsync(0);
    }
}

// Deliberately not here: an "admin redirects to sign-in when signed out" fact.
// It cannot be written honestly in this tier. The cookie handler's LoginPath sends
// an anonymous visitor to /signin, and the sample's /signin grants the admin role
// for the asking and bounces straight to /admin — so a browser ends the navigation
// signed IN, and the only assertion available would be one that passes whether or
// not the guard exists. The authorization guards are covered where they can be
// observed: ScaffoldedGuardTests in the integration tier, which does not follow
// redirects.
