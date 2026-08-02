using Microsoft.AspNetCore.OutputCaching;

using NativeWeb.Render;

namespace __PRODUCT__.Middleware;

/// <summary>
/// The output-cache policy for the anonymous public pages (spec 006 R2, R8).
/// Layered on top of the framework's default policy rather than replacing it,
/// so the two guard the shared store independently: the default refuses to
/// store any response that sets a cookie or answers an authenticated request,
/// and this one refuses to so much as look in the store for a request that
/// carried an identity cookie.
///
/// The key is (path, page, negotiated locale) and nothing else. Query keys are
/// an allowlist of one, so a crawler appending utm_source cannot mint a fresh
/// entry per campaign; the locale is taken from the negotiated value rather
/// than the raw Accept-Language, which collapses every hostile spelling of a
/// header onto the small set of catalogs actually shipped.
///
/// Pattern-copied from apps/manzil and kept structurally identical to it.
/// </summary>
internal sealed class PublicPageCachePolicy : IOutputCachePolicy
{
    /// <summary>The registered policy name the public endpoints attach by.</summary>
    public const string Name = "public-pages";

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        HttpContext http = context.HttpContext;

        // Presence of a personalizing cookie means this request cannot share a
        // response with anyone: no lookup (it must not be served someone else's
        // page) and no storage (its own response may be personal). Both
        // directions matter -- allowing only one would leave a poisoning path
        // open.
        bool shareable = !PublicPageCache.CarriesIdentityCookie(http.Request)
            && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method));

        context.EnableOutputCaching = shareable;
        context.AllowCacheLookup = shareable;
        context.AllowCacheStorage = shareable;

        // Lock so a cold key under load renders once and the rest wait, rather
        // than every concurrent miss hitting the database for the same page.
        context.AllowLocking = true;

        context.CacheVaryByRules.QueryKeys = "page";
        context.CacheVaryByRules.VaryByValues["locale"] = http.GetLocale();
        context.ResponseExpirationTimeSpan = TimeSpan.FromSeconds(PublicPageCache.MaxAgeSeconds);

        // Tag by owner so one write clears every page of that owner's profile
        // and every collection under it in a single eviction. Per-page tagging
        // would be wrong as well as fiddly: adding one row reshuffles the
        // pagination of all of them. Route values are available because
        // UseOutputCache sits after UseRouting. "/" carries no username and
        // needs no tag -- it renders no user's data, so no write can stale it,
        // which is every route the skeleton itself ships.
        if (http.Request.RouteValues.TryGetValue("username", out object? username)
            && username is string name
            && name.Length > 0)
        {
            context.Tags.Add(PublicPageCache.OwnerTag(name));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Only a rendered page is storable. 404 is called out because it is the
        // tempting one and the wrong one: a "not here" answer that a later
        // write turns into a real page would sit stale in front of it -- and
        // ErrorPageMiddleware fills bodiless 404s outside this cache anyway, so
        // what got stored would not be the served page.
        //
        // A clamped out-of-range page is refused for a different reason: the
        // body is page 1's, but the key carries whatever number was asked for,
        // so storing it lets a crawler fill the store with duplicates of one
        // page. The handler is the only party that knows the real page count,
        // so it says so rather than the policy guessing a cap.
        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK
            || context.HttpContext.Items.ContainsKey(PublicPageCache.OutOfRangePageKey))
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
