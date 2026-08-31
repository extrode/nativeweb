using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

using __PRODUCT__.Middleware;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

// Spec 006 R6. The two properties that decide whether an eviction actually
// happens are invisible from the outside -- the response looks the same either
// way, and a missed eviction heals itself before anyone reports it -- so they
// are pinned here rather than inferred from a flow test. Pattern-copied across
// the LNW apps and kept structurally identical, so a fix travels by diff.
public class PublicPageEvictionTests
{
    // Records what it was asked to evict, and with which token. The token is
    // the point: an eviction handed the request's own cancellation token is one
    // the client can cancel by closing the tab, after the write has committed.
    private sealed class RecordingStore : IOutputCacheStore
    {
        public List<string> EvictedTags { get; } = [];

        public List<bool> TokenWasCancelled { get; } = [];

        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
        {
            EvictedTags.Add(tag);
            TokenWasCancelled.Add(cancellationToken.IsCancellationRequested);
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class AbortedLifetime(CancellationToken token) : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; } = token;

        public void Abort()
        {
        }
    }

    private static async Task<RecordingStore> EvictAsync(string username, int statusCode, bool clientDisconnected)
    {
        var store = new RecordingStore();
        var services = new ServiceCollection();
        services.AddSingleton<IOutputCacheStore>(store);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (username.Length != 0)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], authenticationType: "test"));
        }

        context.Response.StatusCode = statusCode;

        using var aborted = new CancellationTokenSource();
        if (clientDisconnected)
        {
            await aborted.CancelAsync();
            context.Features.Set<IHttpRequestLifetimeFeature>(new AbortedLifetime(aborted.Token));
        }

        // Called directly rather than through the filter: the filter's only job
        // is to register this on OnStarting, and driving a real endpoint filter
        // pipeline here would test the framework rather than the decision.
        await PublicPageEviction.EvictAsync(context);
        return store;
    }

    [Fact]
    public async Task ASuccessfulWrite_EvictsTheOwnersTag()
    {
        RecordingStore store = await EvictAsync("Sy", StatusCodes.Status302Found, clientDisconnected: false);

        Assert.Equal("pub:sy", Assert.Single(store.EvictedTags));
    }

    // The write has already committed when this runs, so a token the client can
    // cancel means a closed tab leaves the owner's own change invisible for the
    // whole TTL, with nothing to retry it.
    [Fact]
    public async Task AWriteWhoseClientDisconnected_StillEvicts()
    {
        RecordingStore store = await EvictAsync("Sy", StatusCodes.Status302Found, clientDisconnected: true);

        Assert.Equal("pub:sy", Assert.Single(store.EvictedTags));
        Assert.False(Assert.Single(store.TokenWasCancelled));
    }

    // A handler that throws after committing produces a 500 over a change that
    // really landed; a status filter would skip exactly that case.
    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public async Task EveryStatus_Evicts(int statusCode)
    {
        RecordingStore store = await EvictAsync("Sy", statusCode, clientDisconnected: false);

        Assert.Equal("pub:sy", Assert.Single(store.EvictedTags));
    }

    // No identity, nothing to evict -- and nothing to guess at either.
    [Fact]
    public async Task AnAnonymousRequest_EvictsNothing()
    {
        RecordingStore store = await EvictAsync("", StatusCodes.Status200OK, clientDisconnected: false);

        Assert.Empty(store.EvictedTags);
    }
}
