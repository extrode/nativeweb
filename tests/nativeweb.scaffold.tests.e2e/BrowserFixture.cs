using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.Scaffold.Tests.E2e;

/// <summary>One browser for the whole tier; a context per test keeps cookies apart.</summary>
public sealed class BrowserFixture : IAsyncLifetime
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
/// One collection, run serially, and serially for a reason rather than by habit.
/// </summary>
/// <remarks>
/// The integration tier stands up a fresh host per test, because the demo store is
/// an in-memory singleton and a create in one test would otherwise leak into the
/// next. This tier cannot do that: it drives one published process over one socket,
/// so every test in it shares one store, for the whole run, and there is no reset.
///
/// The consequence is a rule for anything added here: <b>arrange your own row and
/// assert on it by name.</b> Never assert on a total count, and never assume a
/// seeded row is still present — an earlier test may legitimately have deleted it.
/// Parallelization is off so at least the ordering is stable enough to debug.
///
/// Note what DisableParallelization does not do: it orders the tests, not the
/// fixtures. Both sample fixtures are initialized concurrently regardless, which is
/// why the publish is shared rather than run per fixture -- see
/// SampleProcessFixture.PublishedExe.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScaffoldE2eFixture
    : ICollectionFixture<InMemorySampleFixture>,
      ICollectionFixture<JauntySampleFixture>,
      ICollectionFixture<PassphraseSampleFixture>,
      ICollectionFixture<BrowserFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "ScaffoldE2e";
}
