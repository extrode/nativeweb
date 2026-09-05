using Microsoft.Playwright;

using NativeWeb.Scaffold.Tests.E2e;

using Xunit;

namespace NativeWeb.Permissions.Tests.E2e;

/// <summary>
/// The published sample on its own port, with no database configured.
/// </summary>
/// <remarks>
/// In-memory on purpose, and not because a database would be inconvenient. This
/// test revokes and re-grants a permission held by <c>demo</c>, which is the one
/// subject the Development sign-in issues; against <c>nativeweb_sample_test</c>
/// that write is visible to every other suite touching the same rows, and the
/// scaffolding e2e tier drives <c>demo</c> through the same admin at the same time.
/// An in-memory store lives and dies with this process, so the mutation cannot
/// reach anything else.
/// </remarks>
public sealed class PermissionSampleFixture() : SampleProcessFixture(5352, null);

/// <summary>One browser for this tier.</summary>
public sealed class PermissionBrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    /// <summary>The launched browser.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        Browser = await _playwright.Chromium
            .LaunchAsync(new BrowserTypeLaunchOptions { Headless = true })
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await Browser.CloseAsync().ConfigureAwait(false);
        _playwright?.Dispose();
    }
}

/// <summary>
/// Serial, because every test shares one process and one store.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PermissionE2eFixture
    : ICollectionFixture<PermissionSampleFixture>, ICollectionFixture<PermissionBrowserFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "PermissionE2e";
}

/// <summary>
/// R12 and R13 through a browser against the published NativeAOT binary: a
/// permission moved in the access admin changes what the generated list draws.
/// </summary>
[Collection(PermissionE2eFixture.Name)]
public sealed class AccessAdminFlowTests(PermissionSampleFixture sample, PermissionBrowserFixture browser)
{
    private const string DeletePermission = "sample:article.delete";
    private const string SubjectPath = "/admin/access/subjects/demo";

    private const string DeleteControl = "form[data-nws-confirm]";

    [Fact]
    public async Task RevokingAndRegrantingDelete_MovesTheDeleteControlOffAndBackOntoTheList()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync($"{sample.BaseUrl}/signin");

        Assert.True(await DeleteControlIsDrawnAsync(page), sample.LogTail);

        await page.GotoAsync($"{sample.BaseUrl}{SubjectPath}");
        await page.Locator(
            $"form[action$='/grants/revoke']:has(input[value='{DeletePermission}']) button").ClickAsync();

        Assert.False(await DeleteControlIsDrawnAsync(page), sample.LogTail);

        await page.GotoAsync($"{sample.BaseUrl}{SubjectPath}");
        await page.Locator("form[action$='/grants'] select[name='permission']")
            .SelectOptionAsync(DeletePermission);
        await page.Locator("form[action$='/grants'] button").ClickAsync();

        Assert.True(await DeleteControlIsDrawnAsync(page), sample.LogTail);
    }

    /// <summary>
    /// R8 through the published NativeAOT binary: the same subject, the same route,
    /// admitted and then refused with the status the filter promises.
    /// </summary>
    /// <remarks>
    /// A GET, because the refusal has to be observable after the grant is gone and
    /// the only POST the admin offers is inside a form the revocation removes. The
    /// create route carries the same filter as the delete one.
    /// </remarks>
    [Fact]
    public async Task TheCreateRoute_Serves200WithAddAndRefuses403Without()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync($"{sample.BaseUrl}/signin");

        IResponse? admitted = await page.GotoAsync($"{sample.BaseUrl}/admin/articles/new");
        Assert.Equal(200, admitted?.Status);

        await page.GotoAsync($"{sample.BaseUrl}{SubjectPath}");
        await page.Locator(
            "form[action$='/grants/revoke']:has(input[value='sample:article.add']) button").ClickAsync();

        IResponse? refused = await page.GotoAsync($"{sample.BaseUrl}/admin/articles/new");
        Assert.Equal(403, refused?.Status);

        await page.GotoAsync($"{sample.BaseUrl}{SubjectPath}");
        await page.Locator("form[action$='/grants'] select[name='permission']")
            .SelectOptionAsync("sample:article.add");
        await page.Locator("form[action$='/grants'] button").ClickAsync();
    }

    [Fact]
    public async Task TheAccessAdmin_IsUnreachableWithoutSigningIn()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        IResponse? response = await page.GotoAsync($"{sample.BaseUrl}{SubjectPath}");

        Assert.NotNull(response);
        Assert.DoesNotContain(DeletePermission, await page.ContentAsync(), StringComparison.Ordinal);
    }

    private async Task<bool> DeleteControlIsDrawnAsync(IPage page)
    {
        await page.GotoAsync($"{sample.BaseUrl}/admin/articles");

        return await page.Locator(DeleteControl).CountAsync() > 0;
    }
}
