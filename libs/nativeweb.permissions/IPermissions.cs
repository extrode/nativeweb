using System.Security.Claims;

namespace NativeWeb.Permissions;

/// <summary>
/// The one call application code makes to ask whether someone may do something.
/// </summary>
/// <remarks>
/// Injected, not static, because the answer depends on the store the host
/// registered. A scaffolded model needs no call at all -- the generated routes are
/// gated for it -- so this is for hand-written endpoints, for an extra permission
/// that gates nothing by itself, and for a model with no admin.
/// </remarks>
public interface IPermissions
{
    /// <summary>
    /// True when <paramref name="user"/> holds <paramref name="permission"/>.
    /// </summary>
    /// <param name="user">The current principal.</param>
    /// <param name="permission">
    /// A generated constant, e.g. <c>ArticlePermissions.Change</c>. A permission
    /// this application does not declare is never held.
    /// </param>
    /// <param name="resourceId">
    /// Which row, where the question is about one. <b>Accepted and carried, and not
    /// consulted by either shipped store.</b> It is here so that a store which
    /// grants per row can be written later without changing a single call site;
    /// today every decision is at model level. A store that ignores it answers the
    /// same for every row, which is the documented behaviour, not a bug in the
    /// caller.
    /// </param>
    /// <param name="ct">Cancels the store call, if one is made.</param>
    ValueTask<bool> AllowsAsync(
        ClaimsPrincipal user,
        Permission permission,
        string? resourceId = null,
        CancellationToken ct = default);
}
