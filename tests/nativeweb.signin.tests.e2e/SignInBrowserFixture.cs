using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.SignIn.Tests.E2e;

/// <summary>One browser for the whole tier; a context per test keeps sessions apart.</summary>
public sealed class SignInBrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    /// <summary>The launched browser.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
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
/// One collection, run serially.
/// </summary>
/// <remarks>
/// One published process, one session store, one port. A fresh browser context per test is
/// what keeps the sessions apart — the process is shared and there is no reset, so nothing
/// here may assert on a store-wide total.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SignInE2eFixture
    : ICollectionFixture<SignInProcessFixture>,
      ICollectionFixture<SignInBrowserFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "SignInE2e";
}
