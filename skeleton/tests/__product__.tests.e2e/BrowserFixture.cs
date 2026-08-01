using Microsoft.Playwright;

using Xunit;

namespace __PRODUCT__.Tests.E2e;

public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync().ConfigureAwait(false);
        _playwright?.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class E2eFixture : ICollectionFixture<__PRODUCT__ProcessFixture>, ICollectionFixture<BrowserFixture>
{
    public const string Name = "E2e";
}
