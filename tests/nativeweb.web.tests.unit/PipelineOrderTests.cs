using System.Net;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// Spec 006 R8 and spec 015 R5: the rate limiter runs before the output cache.
///
/// The constraint is stated in a comment in the skeleton's <c>Program.cs</c> and
/// asserted nowhere, which is the shape of every defect this spec exists to
/// prevent — a comment survives a move, an ordering does not. Asserted
/// behaviourally rather than by enumerating middleware types, because what
/// matters is the consequence: a cache hit spends a permit. A test that read the
/// registration list would pass over a pipeline that had the order right and the
/// wiring wrong.
/// </summary>
public sealed class PipelineOrderTests
{
    [Fact]
    public async Task ACacheHitStillSpendsARateLimitPermit()
    {
        // One permit, so the second request is refused if — and only if — the
        // limiter saw it. Swap UseRateLimiter and UseOutputCache in
        // PublicPagePipeline and this fails: the second request is answered 200
        // from the store, never reaching the limiter, and a public route's limit
        // becomes a no-op for exactly the traffic it exists to bound.
        await using PublicPagePipeline pipeline = await PublicPagePipeline.WithRateLimitAsync(permitLimit: 1);

        (HttpResponseMessage first, _) = await pipeline.GetAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, pipeline.Renders);

        (HttpResponseMessage second, _) = await pipeline.GetAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        // And it was refused before the handler, not after: the store holds an
        // entry that a cache-first pipeline would have served.
        Assert.Equal(1, pipeline.Renders);
    }

    [Fact]
    public async Task WithPermitsToSpareTheStoredEntryIsStillServed()
    {
        // The control. Without it the test above passes over a pipeline whose
        // cache never stored anything, where the 429 would be true for the wrong
        // reason.
        await using PublicPagePipeline pipeline = await PublicPagePipeline.WithRateLimitAsync(permitLimit: 2);

        (HttpResponseMessage first, string firstBody) = await pipeline.GetAsync();
        (HttpResponseMessage second, string secondBody) = await pipeline.GetAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, pipeline.Renders);
        Assert.Equal(firstBody, secondBody);
    }
}
