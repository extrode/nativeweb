using System.Security.Claims;

namespace NativeWeb.Permissions;

/// <summary>
/// The permissions the current request's subject holds, resolved once and reused.
/// </summary>
/// <remarks>
/// <para>Scoped, and the memoisation is the point. Two things need the answer on a
/// scaffolded list page: the route's own gate, which runs before the handler, and
/// the HTML, which decides whether to draw a New button, an Edit link on each row
/// and a Delete form on each row. Asking the store per control would be a query per
/// row; asking it per model would be a query per link on the admin index. One call
/// per request answers all of them.</para>
/// <para>It also solves a shape problem. The scaffolding builds its list HTML in a
/// synchronous method, and any store-backed decision is asynchronous. A set
/// resolved before the builder runs is something a synchronous method can consult.</para>
/// <para><b>The decision is at model level, not row level.</b> The held set says
/// which permissions the subject has, not which rows they apply to, so an
/// application that later grants at resource level would see a visible Edit control
/// on a row the subject cannot change, and a refusal on the click. Fixing that
/// means a store call per row, which is the cost this design exists to avoid.</para>
/// </remarks>
public interface IPermissionContext
{
    /// <summary>
    /// The declared permission names <paramref name="user"/> holds, directly or
    /// through a group.
    /// </summary>
    /// <remarks>
    /// Resolved on the first call for a subject and reused for the rest of the
    /// request. With no subject claim the answer is the empty set and the store is
    /// not called at all. Names the store returns that this application does not
    /// declare are dropped.
    /// </remarks>
    /// <param name="user">The current principal.</param>
    /// <param name="ct">Cancels the store call, on the calls that make one.</param>
    ValueTask<IReadOnlySet<string>> HeldAsync(ClaimsPrincipal user, CancellationToken ct = default);
}
