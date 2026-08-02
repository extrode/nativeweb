using Microsoft.AspNetCore.Http;

using __PRODUCT__.Middleware;

using Xunit;

namespace __PRODUCT__.Tests.Unit;

// Spec 006: the contract that decides whether a response is a shareable public
// page, and what CSP and caching headers it then carries. Pattern-copied across
// the LNW apps and kept structurally identical, so a fix travels by diff.
public class PublicPageCacheTests
{
    private static bool CarriesIdentityCookie(string? cookiePair)
    {
        var context = new DefaultHttpContext();
        if (cookiePair is not null)
        {
            context.Request.Headers.Cookie = cookiePair;
        }

        return PublicPageCache.CarriesIdentityCookie(context.Request);
    }

    [Fact]
    public void NoCookies_IsShareable() => Assert.False(CarriesIdentityCookie(null));

    [Fact]
    public void AnUnrelatedCookie_IsStillShareable() =>
        Assert.False(CarriesIdentityCookie("theme=dark"));

    // Presence, not validity: the whole point is that this runs before anything
    // has authenticated the request.
    [Fact]
    public void TheFlashCookie_MakesTheResponsePersonal() =>
        Assert.True(CarriesIdentityCookie("__Host-nw-flash=anything"));

    [Fact]
    public void AGarbageFlashCookie_IsStillTreatedAsPersonal() =>
        Assert.True(CarriesIdentityCookie("__Host-nw-flash=%%%not-a-flash%%%"));

    // The tag is written from the route's username on store and from the
    // signed-in user's stored spelling on evict. Those can differ by casing, so
    // it has to normalise or an eviction silently misses its own page.
    [Theory]
    [InlineData("Sy", "pub:sy")]
    [InlineData("SY", "pub:sy")]
    [InlineData("Mixed-Case-01", "pub:mixed-case-01")]
    public void OwnerTag_IsLowercasedAndPrefixed(string username, string expected) =>
        Assert.Equal(expected, PublicPageCache.OwnerTag(username));

    [Fact]
    public void OwnerTag_SeparatesDifferentOwners() =>
        Assert.NotEqual(PublicPageCache.OwnerTag("alice"), PublicPageCache.OwnerTag("alicia"));

    // Every hash the template generated has to reach script-src, or the script
    // it authorizes is blocked on a page that renders fine and does nothing.
    [Fact]
    public void Csp_CarriesEveryHashAsItsOwnSource()
    {
        string csp = PublicPageCache.Csp(["sha256-aaa", "sha256-bbb"]);

        Assert.Contains("script-src 'self' 'sha256-aaa' 'sha256-bbb';", csp, StringComparison.Ordinal);
    }

    // A nonce in a cacheable response would be a per-render byte in a shared
    // entry, and would authorize whatever script the page happened to carry.
    [Fact]
    public void Csp_HasNoNonceAnywhere()
    {
        string csp = PublicPageCache.Csp(["sha256-aaa"]);

        Assert.DoesNotContain("nonce-", csp, StringComparison.Ordinal);
        Assert.Contains("style-src 'self';", csp, StringComparison.Ordinal);
    }

    // The directives that do NOT fall back to default-src have to be spelled
    // out here too, or the public page is the one page missing them.
    [Theory]
    [InlineData("frame-ancestors 'none'")]
    [InlineData("base-uri 'none'")]
    [InlineData("form-action 'self'")]
    [InlineData("report-uri /csp-report")]
    public void Csp_KeepsTheNonInheritingDirectives(string directive) =>
        Assert.Contains(directive, PublicPageCache.Csp([]), StringComparison.Ordinal);

    // The generated array is the input; an empty one must still produce a valid
    // policy rather than a dangling "script-src 'self' ;".
    [Fact]
    public void Csp_WithNoHashes_IsStillWellFormed()
    {
        string csp = PublicPageCache.Csp([]);

        Assert.Contains("script-src 'self';", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("''", csp, StringComparison.Ordinal);
    }

    // The home page is declared public, so its own generated hashes must be the
    // ones the CSP carries -- this is the join that keeps the two from drifting.
    [Fact]
    public void TheHomeTemplatesHashes_AllReachItsCsp()
    {
        string[] hashes = Templates.Home.Index.InlineScriptCspHashes;
        Assert.NotEmpty(hashes);
        Assert.All(hashes, h => Assert.StartsWith("sha256-", h, StringComparison.Ordinal));

        string csp = PublicPageCache.Csp(hashes);
        Assert.All(hashes, h => Assert.Contains($"'{h}'", csp, StringComparison.Ordinal));
    }
}
