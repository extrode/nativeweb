using Microsoft.AspNetCore.OutputCaching;

namespace __PRODUCT__.Middleware;

/// <summary>
/// Clears an owner's cached public pages after a write that could have changed
/// them (spec 006 R6).
///
/// One filter attached per write route rather than an EvictByTagAsync call
/// hand-placed in each handler's success path: the failure mode that matters is
/// forgetting one, and a forgotten eviction is invisible for up to the TTL and
/// then fixes itself, which is precisely the kind of bug that never gets
/// reported. A filter is one line at the route and cannot be half-applied
/// inside a branchy handler.
///
/// The skeleton has no accounts and so no write route to attach this to; it
/// ships here so a product that grows one has the mechanism rather than
/// reinventing it. Pattern-copied from apps/manzil, structurally identical.
/// </summary>
internal static class PublicPageEviction
{
    /// <summary>
    /// Evicts this user's public pages once the response status is known.
    /// Deliberately not narrowed to "the write succeeded": a failed write
    /// re-renders its form with a 200, so some evictions clear pages that had
    /// not changed. That costs one re-render of two pages, whereas the opposite
    /// error serves stale public data for up to the TTL.
    /// </summary>
    public static RouteHandlerBuilder EvictsPublicPages(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddEndpointFilter(static async (context, next) =>
        {
            HttpContext http = context.HttpContext;

            // Registered before the result executes, so the response has not
            // started yet and the callback still sees the final status. Running
            // here rather than after the handler also means the eviction lands
            // before the client is told the write is done, so the redirect that
            // follows a save cannot race its own invalidation.
            http.Response.OnStarting(static state =>
            {
                var ctx = (HttpContext)state;
                if (ctx.Response.StatusCode >= StatusCodes.Status400BadRequest)
                {
                    return Task.CompletedTask;
                }

                string? username = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Task.CompletedTask;
                }

                IOutputCacheStore store = ctx.RequestServices.GetRequiredService<IOutputCacheStore>();
                return store.EvictByTagAsync(PublicPageCache.OwnerTag(username), ctx.RequestAborted).AsTask();
            }, http);

            return await next(context);
        });
    }
}
