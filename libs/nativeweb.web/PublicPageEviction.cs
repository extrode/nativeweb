using Microsoft.AspNetCore.OutputCaching;

namespace NativeWeb.Web;

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
/// reinventing it.
/// </summary>
public static class PublicPageEviction
{
    /// <summary>
    /// Evicts this user's public pages whatever the response turns out to be.
    /// Deliberately not narrowed to "the write succeeded", and not even to a
    /// non-error status: a failed write re-renders its form with a 200, and a
    /// handler that throws after committing produces a 500 over a change that
    /// really did land. Filtering on the status would skip exactly that second
    /// case. Over-evicting costs one re-render of two pages; under-evicting
    /// serves stale public data for the whole TTL, invisibly, and then heals
    /// itself before anyone reports it.
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
            http.Response.OnStarting(static state => EvictAsync((HttpContext)state), http);

            return await next(context);
        });
    }

    /// <summary>
    /// The eviction itself, separated from the filter plumbing so its two
    /// invisible properties — that it runs whatever the status, and that it
    /// does not take the client's cancellation token — can be asserted
    /// directly. Both are unobservable from the response, and a missed
    /// eviction heals itself within the TTL, so neither would ever be caught
    /// by a flow test.
    /// </summary>
    public static Task EvictAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? username = context.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Task.CompletedTask;
        }

        IOutputCacheStore store = context.RequestServices.GetRequiredService<IOutputCacheStore>();

        // CancellationToken.None, not RequestAborted: the write has already
        // committed by the time this runs, so tying the invalidation to the
        // client's connection means a user who closes the tab as the response
        // flushes leaves their own change committed and their public page
        // stale for the whole TTL, with nothing to retry it.
        return store.EvictByTagAsync(PublicPageCache.OwnerTag(username), CancellationToken.None).AsTask();
    }
}
