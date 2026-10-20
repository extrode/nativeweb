using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

using NativeWeb.Web;

using Xunit;

namespace __PRODUCT__.Tests.Integration;

// The stamped product's starting integration coverage: the whole pipeline
// answering on the surfaces the skeleton ships -- the rendered home page with
// its V6 header set (spec 003 R25-R26), the health probes, a V8 static page
// with its revalidation caching (R38), and the static error shell on an
// unmatched route (R39). Modeled on NativeWeb's StaticPagesTests +
// ResponseHardeningTests, condensed to one file: the product grows its own
// per-feature files beside this one rather than growing this one.
[Collection(__PRODUCT__Collection.Name)]
public sealed partial class SmokeTests(__PRODUCT__Factory factory)
{
    [Fact]
    public async Task HomePage_IsServedAsHtmlWithTheSecurityHeaderSet()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        // The load-bearing directives as literals, not a comparison against a
        // constant -- that would pass no matter how the constant was loosened.
        string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);

        // "/" is a public page (spec 006), so this anonymous request gets the
        // nonce-free CSP whose inline scripts are authorized by build-time
        // hashes. A nonce reappearing here means the page went back to being
        // uncacheable.
        Assert.DoesNotContain("nonce-", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("report-uri /csp-report", csp, StringComparison.Ordinal);

        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("camera=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")), StringComparison.Ordinal);
    }

    /// <summary>
    /// The lockstep: the CSP's hash sources recomputed from the response that
    /// was actually served, rather than compared against the constants the
    /// header was built from.
    /// </summary>
    /// <remarks>
    /// Comparing `InlineScriptCspHashes` to the header proves only that the
    /// header was built from that array — both sides come from the same
    /// build-time scan, so a scan that frames a body wrongly, misses a script or
    /// invents one agrees with itself and the suite stays green while the
    /// browser blocks the page. This parses the served HTML with AngleSharp,
    /// hashes what it finds, and asserts set equality with the sources in the
    /// header. Both directions matter here in a way they do not at the generator
    /// tier: a public page is served without a nonce, so an inline script with
    /// no hash is a script that does not run, and a hash with no script is an
    /// allowlist entry for bytes the page does not ship.
    ///
    /// <para>
    /// This is also the only layer that can see a script carried in by a
    /// partial. `InlineScriptHashes.Collect` walks the page's own nodes, so a
    /// partial-borne script is never hashed — a fact invisible to every
    /// generator-tier test, and a blank page here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task PublicPageCsp_AuthorizesExactlyTheInlineScriptsTheResponseCarries()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");
        string html = await response.Content.ReadAsStringAsync();
        string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        IHtmlDocument document = new HtmlParser().ParseDocument(html);
        var served = document.QuerySelectorAll("script")
            .Where(s => string.IsNullOrEmpty(s.GetAttribute("src")) && s.TextContent.Length > 0)
            .Select(s => "sha256-" + Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(s.TextContent.Replace("\r\n", "\n").Replace("\r", "\n")))))
            .ToList();

        // Not a vacuous pass: the layout ships inline scripts, so a page that
        // parsed to none would mean the response was not the page.
        Assert.NotEmpty(served);

        var authorized = CspHashPattern().Matches(csp).Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(
            served.OrderBy(h => h, StringComparer.Ordinal),
            authorized.OrderBy(h => h, StringComparer.Ordinal));
    }

    [GeneratedRegex(@"'(sha256-[A-Za-z0-9+/=]+)'")]
    private static partial Regex CspHashPattern();

    [Fact]
    public async Task Healthz_ReportsLive()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Version_ReturnsStampedInformationalVersion()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/version");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Version "+" shortsha from StampSourceRevision (or "+dev" without git).
        Assert.Matches(@"^\d+\.\d+\.\d+\+[0-9a-f]{7,}$|^\d+\.\d+\.\d+\+dev$", body);
    }

    // Readiness is the database answering, so this tier needs the real
    // Postgres the connection string points at -- the side-channel assert
    // first, so a failure here reads as "no database" rather than "app broken".
    [Fact]
    public async Task Readyz_ReportsReady()
    {
        Assert.True(await TestDatabase.CanConnectAsync());

        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ready", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AboutPage_IsServedAtItsCleanUrl_WithRevalidationCaching()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/about");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Headers.ETag);
        Assert.True(response.Headers.CacheControl?.NoCache, "Expected Cache-Control: no-cache on a static page.");
        Assert.True(response.Headers.CacheControl?.NoStore != true, "A static page must not be no-store.");
        // The static branch: no nonce anywhere, so an injected script has
        // nothing to borrow.
        Assert.Equal(SecurityHeadersMiddleware.StaticPageCsp,
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task UnknownPath_RendersTheStaticErrorShell()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/no-such-page-anywhere");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(">Not found</h1>", body, StringComparison.Ordinal);
        Assert.Contains("skip-link", body, StringComparison.Ordinal);
        Assert.Equal(SecurityHeadersMiddleware.StaticPageCsp,
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
