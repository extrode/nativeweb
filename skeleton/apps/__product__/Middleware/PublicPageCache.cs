using System.Text;

namespace __PRODUCT__.Middleware;

/// <summary>
/// The one place the "this response is a shareable public page" contract lives
/// (spec 006). Three parties need to agree on it and they run at different
/// points in the pipeline, so they agree by calling into here rather than by
/// each restating the rule:
///
/// <list type="bullet">
/// <item>a public route declares itself with <see cref="AsPublicPage"/>,
/// supplying the CSP built from its own template's generated script hashes;</item>
/// <item><see cref="SecurityHeadersMiddleware"/> turns that into the CSP and
/// Cache-Control headers;</item>
/// <item>the output cache policy uses <see cref="CarriesIdentityCookie"/> to
/// decide whether to touch the shared store at all.</item>
/// </list>
///
/// Pattern-copied from apps/manzil and kept structurally identical to it, so
/// fixes travel between the two by diff.
/// </summary>
internal static class PublicPageCache
{
    /// <summary>
    /// Endpoint metadata marking a route as publicly cacheable, carrying the CSP
    /// its template's generated hashes produce.
    ///
    /// Metadata on the route, deliberately, rather than something the handler
    /// parks in HttpContext.Items: on a cache hit the handler never runs, so an
    /// Items-based mark is absent exactly when a stored response is replayed.
    /// The headers would then be recomputed as if the page were an ordinary
    /// dynamic one — a nonce CSP over a body that has no nonce, which blocks
    /// every inline script on every cache hit. Routing has run by the time the
    /// cache middleware answers, so metadata is present either way, and the
    /// headers become a function of (route, cookies): the same inputs the cache
    /// key and the bypass use, so a replay and a fresh render cannot disagree.
    /// </summary>
    public sealed record PublicPageCspMetadata(string Csp);

    /// <summary>
    /// How long a stored public page may be served without revalidation. The
    /// same number bounds the in-process entry and the downstream max-age, so a
    /// shared proxy can never hold a page longer than this app would.
    ///
    /// <b>This is the freshness guarantee, not a backstop.</b> Writes evict by
    /// tag immediately, but eviction only reaches this process's store: a
    /// downstream proxy or a browser that has already been handed the response
    /// keeps serving it for the rest of the window regardless. So a change is
    /// visible to its own author at once and to everyone else within this many
    /// seconds — shorten it before publishing anything where that gap matters.
    /// </summary>
    public const int MaxAgeSeconds = 60;

    /// <summary>
    /// Set by a paginated handler on <see cref="HttpContext.Items"/> when the
    /// requested page was outside the real range and it clamped. The policy
    /// reads it and declines to store — otherwise ?page=999999 mints one entry
    /// per number for a body identical to page 1, and the key allowlist cannot
    /// stop it because "page" is a key it must allow.
    ///
    /// Items is the right place here and only here: storage is decided on a
    /// miss, and on a miss the handler ran. The <b>header</b> decisions above
    /// run on hits too, which is why they read route metadata instead.
    ///
    /// The skeleton ships no paginated public route; the constant is here so a
    /// product that adds one has the hook rather than an arbitrary page cap
    /// invented at the wrong layer. Only the handler knows the real page count.
    /// </summary>
    public const string OutOfRangePageKey = "public-page-out-of-range";

    /// <summary>
    /// Whether the request carries a cookie that can make a response personal.
    /// Deliberately keyed on <b>presence, never on validity</b>: a forged or
    /// expired cookie costs its sender a full uncached render, which is the
    /// safe direction, whereas validating here would mean a response could be
    /// judged shareable on the strength of an auth check that the endpoint has
    /// not run yet.
    ///
    /// The skeleton has no accounts, so the flash cookie is the only one that
    /// personalizes a page. <b>A product that grows sign-in adds its session
    /// cookie to this test</b> — it is the single line that keeps one visitor's
    /// page out of everyone else's cache.
    /// </summary>
    public static bool CarriesIdentityCookie(HttpRequest request) =>
        request.Cookies.ContainsKey(Flash.CookieName);

    /// <summary>
    /// The eviction tag for everything publicly readable about one owner: their
    /// profile, every page of it, and every collection under it. Lowercased so
    /// one canonical tag clears the entry whatever casing the path carried.
    /// </summary>
    public static string OwnerTag(string username) =>
        "pub:" + username.ToLowerInvariant();

    /// <summary>
    /// Declares a route publicly cacheable and fixes the CSP its anonymous
    /// responses carry. Attaching the policy and the CSP together keeps a route
    /// from being cached without the matching headers, or vice versa.
    /// </summary>
    public static RouteHandlerBuilder AsPublicPage(this RouteHandlerBuilder builder, string csp)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .CacheOutput(PublicPageCachePolicy.Name)
            .WithMetadata(new PublicPageCspMetadata(csp));
    }

    /// <summary>
    /// The public CSP this response should carry, or null when it is not an
    /// anonymous public page. Both halves matter: the route must be declared
    /// public AND the request must have carried no identity cookie, because a
    /// visitor with a personal cookie renders today's nonce page.
    /// </summary>
    public static string? ResolveCsp(HttpContext context) =>
        !CarriesIdentityCookie(context.Request)
            ? context.GetEndpoint()?.Metadata.GetMetadata<PublicPageCspMetadata>()?.Csp
            : null;

    /// <summary>
    /// The nonce-free CSP for a page whose inline scripts are authorized by
    /// build-generated hashes. Identical to the standard policy except that
    /// 'nonce-…' is replaced by the hash sources in script-src and dropped
    /// outright from style-src (no template emits an inline style). Nothing is
    /// widened: a hash source authorizes exactly one byte string, where the
    /// nonce it replaces authorized any script the page carried.
    /// </summary>
    public static string Csp(IReadOnlyList<string> scriptHashes)
    {
        ArgumentNullException.ThrowIfNull(scriptHashes);

        var csp = new StringBuilder("default-src 'self'; script-src 'self'");
        foreach (string hash in scriptHashes)
        {
            csp.Append(" '").Append(hash).Append('\'');
        }

        // form-action stays 'self' rather than 'none': the anonymous page
        // carries no form today, but tightening it is a separate decision from
        // this spec's, and 'self' is already what every other page uses.
        return csp
            .Append("; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; ")
            .Append("report-uri /csp-report; report-to csp")
            .ToString();
    }
}
