using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NativeWeb.Scaffold;

/// <summary>
/// Whether the current request may read a scaffolded model, evaluated the same way
/// the route's own authorization would evaluate it.
/// </summary>
/// <remarks>
/// <para>
/// This exists for the admin index, and only for it. Every per-model route carries
/// authorization metadata the framework enforces before the handler runs; the index
/// is a list of links to those routes, so it has nothing of its own to enforce and
/// used to render every link to every caller. A model guarded by
/// <c>[Auth(Role = "admin")]</c> still had its display name, its URL and its
/// existence disclosed to an anonymous visitor -- who then got a 401 on the click,
/// having already learned what the site keeps.
/// </para>
/// <para>
/// A link is emitted only when this returns true, which makes the index show what
/// the caller could reach and nothing else. The evaluation mirrors
/// <c>ScaffoldEmitter.Authorization</c> exactly, because a second rule that agrees
/// today is a second rule that can disagree later: policy wins over role, role means
/// the role, and bare <c>[Auth]</c> means any authenticated identity.
/// </para>
/// <para>
/// A host that registered no authentication has no authenticated identity, so a
/// guarded model's link is hidden rather than shown-and-then-refused. That is the
/// same answer the route gives.
/// </para>
/// </remarks>
public static class ScaffoldAccess
{
    /// <summary>
    /// True when <paramref name="http"/>'s user satisfies the model's read
    /// requirement. Both arguments null means the model declared no
    /// <c>[Auth]</c> and the routes are open.
    /// </summary>
    /// <param name="http">The current request.</param>
    /// <param name="policy">The <c>[Auth(Policy = ...)]</c> name, if any.</param>
    /// <param name="role">The role the read operation resolves to, if any.</param>
    /// <param name="requiresAuthentication">
    /// True when the model carries <c>[Auth]</c> at all. Distinguishes "open" from
    /// "any authenticated user", which are both a null policy and a null role.
    /// </param>
    public static async ValueTask<bool> CanReadAsync(
        HttpContext http, string? policy, string? role, bool requiresAuthentication)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (!requiresAuthentication)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(policy))
        {
            IAuthorizationService? authorization = http.RequestServices.GetService<IAuthorizationService>();

            // No authorization services registered and a policy demanded: the route
            // itself would fail to run at all. Hiding the link is the honest answer.
            if (authorization is null)
            {
                return false;
            }

            AuthorizationResult result = await authorization
                .AuthorizeAsync(http.User, policy)
                .ConfigureAwait(false);

            return result.Succeeded;
        }

        if (http.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return string.IsNullOrEmpty(role) || http.User.IsInRole(role);
    }
}
