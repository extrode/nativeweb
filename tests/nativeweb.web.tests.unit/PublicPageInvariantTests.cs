using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// Spec 006's triangle, asserted about the composition rather than about any one
/// of its three parts. The skeleton's own unit tier already tests
/// <c>PublicPageCache</c>, <c>PublicPageEviction</c> and the rest in isolation;
/// what nothing tested is the property that exists only when the cache, the
/// anonymous chrome and antiforgery are assembled in one order — which is the
/// property spec 015 puts at risk by moving them.
///
/// These run before the move (015 T1) and again after it (015 T4) without a
/// line changing, so a green run afterwards means the same facts rather than
/// new ones.
/// </summary>
public sealed class PublicPageInvariantTests
{
    [Fact]
    public async Task AnonymousRendersAreByteStable()
    {
        // No cache in front, so the second body is the second render's own
        // output. With the cache present this would assert only that a store
        // returns what it was given.
        await using PublicPagePipeline pipeline = await PublicPagePipeline.WithoutCacheAsync();

        (_, string first) = await pipeline.GetAsync();
        (_, string second) = await pipeline.GetAsync();

        Assert.Equal(2, pipeline.Renders);
        // The render counter is the one field that must differ; every other byte
        // of the two renders has to match, which is what makes them shareable.
        Assert.Equal(WithoutRenderCount(first), WithoutRenderCount(second));
    }

    [Fact]
    public async Task AnonymousResponsesMintNoTokenAndSetNoCookie()
    {
        await using PublicPagePipeline pipeline = await PublicPagePipeline.WithoutCacheAsync();

        (HttpResponseMessage response, string body) = await pipeline.GetAsync();

        // A Set-Cookie is both a per-response byte and a response the output
        // cache refuses to store, so this one header failing takes the whole
        // spec-006 arrangement down with it.
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains("""<p id="token"></p>""", body, StringComparison.Ordinal);
        Assert.Contains("""<p id="nonce"></p>""", body, StringComparison.Ordinal);
        Assert.Contains("""<p id="canary"></p>""", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondAnonymousRequestIsServedFromTheStore()
    {
        // The control for the two tests below. Without it, "the cookie-carrying
        // request rendered afresh" would be satisfied by a cache that never
        // stored anything at all.
        await using PublicPagePipeline pipeline = await PublicPagePipeline.WithCacheAsync();

        (_, string first) = await pipeline.GetAsync();
        (_, string second) = await pipeline.GetAsync();

        Assert.Equal(1, pipeline.Renders);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ARequestCarryingAnIdentityCookieIsNotServedTheSharedEntry()
    {
        await using PublicPagePipeline pipeline = await PublicPagePipeline.WithCacheAsync();

        (_, string anonymous) = await pipeline.GetAsync();
        Assert.Equal(1, pipeline.Renders);

        (_, string personal) = await pipeline.GetAsync(cookie: PublicPagePipeline.IdentityCookie());

        // It rendered rather than being handed the stored anonymous page.
        Assert.Equal(2, pipeline.Renders);
        Assert.NotEqual(anonymous, personal);
        Assert.DoesNotContain("""<p id="token"></p>""", personal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIdentityBearingResponseIsNotWrittenIntoTheSharedEntry()
    {
        await using PublicPagePipeline pipeline = await PublicPagePipeline.WithCacheAsync();

        (_, string personal) = await pipeline.GetAsync(cookie: PublicPagePipeline.IdentityCookie());
        Assert.Equal(1, pipeline.Renders);

        (_, string anonymous) = await pipeline.GetAsync();

        // The anonymous request had to render: the cookie-carrying response was
        // refused storage, so there was nothing for it to be served. The reverse
        // — finding it already stored — is the poisoning path the policy exists
        // to close, and would show up here as Renders == 1.
        Assert.Equal(2, pipeline.Renders);
        Assert.NotEqual(personal, anonymous);
        Assert.Contains("""<p id="token"></p>""", anonymous, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoLocalesOccupyDifferentEntries()
    {
        await using PublicPagePipeline pipeline = await PublicPagePipeline.WithCacheAsync();

        (_, string english) = await pipeline.GetAsync(acceptLanguage: "en");
        (_, string other) = await pipeline.GetAsync(acceptLanguage: PublicPagePipeline.OtherLocale);

        Assert.Equal(2, pipeline.Renders);
        Assert.Contains("Skip to content", english, StringComparison.Ordinal);
        Assert.Contains("Aller au contenu", other, StringComparison.Ordinal);

        // And each key holds its own entry rather than the later request
        // evicting the earlier one.
        (_, string englishAgain) = await pipeline.GetAsync(acceptLanguage: "en");
        Assert.Equal(2, pipeline.Renders);
        Assert.Equal(english, englishAgain);
    }

    /// <summary>
    /// The body with the render counter blanked. Every other byte is what
    /// byte-stability is a claim about.
    /// </summary>
    private static string WithoutRenderCount(string body)
    {
        int start = body.IndexOf("""<p id="render">""", StringComparison.Ordinal);
        Assert.True(start >= 0, "the body should carry a render counter");
        int end = body.IndexOf("</p>", start, StringComparison.Ordinal);
        return string.Concat(body.AsSpan(0, start), body.AsSpan(end));
    }
}
