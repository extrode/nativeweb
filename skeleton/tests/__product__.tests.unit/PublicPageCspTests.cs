using NativeWeb.Web;

using Xunit;

namespace __PRODUCT__.Tests.Unit;

// What this product, specifically, puts in its public pages' CSP. The contract
// itself — what Csp() builds and when a request counts as anonymous — moved to
// the framework's own tier with the code (spec 015 T3); what cannot move is the
// join between a page's CSP and the hashes the render generator emitted from
// THIS product's templates/home/index.html. Spec 015 R6: per-product hashes stay
// with the product that owns the scripts.
//
// So this is the test that catches a page growing an inline script that its CSP
// does not authorize — the one failure mode a framework tier structurally
// cannot see.
public class PublicPageCspTests
{
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
