using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NativeWeb.Permissions;

/// <summary>
/// Refuses a request whose subject does not hold one permission. What the
/// scaffolding appends to every generated route of a model that declares
/// permissions, and what a hand-written endpoint adds to gate itself.
/// </summary>
/// <remarks>
/// <para>An endpoint filter rather than an authorization policy, and the ordering
/// is why. Authorization middleware runs before any endpoint filter, so a model
/// carrying both <c>[Permissions]</c> and <c>[Auth(Role = "editor")]</c> has the
/// role decided first and this filter only ever sees a principal that already
/// passed it. The AND the spec asks for is the ordering, not code. A manufactured
/// policy could not do that: <c>[Auth(Policy = ...)]</c> already owns the single
/// policy slot, a policy name cannot carry a resource identity, and it would drag
/// in <c>AddAuthorization</c> on a host that registered no authentication at all.</para>
/// <para>An inline check at the top of the handler loses for a different reason:
/// the gate would stop being endpoint metadata, so neither a test nor the admin
/// index could see it, and it would be duplicated into seven handler bodies per
/// model.</para>
/// <para>The permission is a <see cref="Permission"/> and the generated call site
/// passes a constant, never a string. A permission that stops being declared takes
/// its constant with it, so the generated route fails to compile rather than
/// quietly gating on a name nobody can be granted.</para>
/// </remarks>
public sealed class PermissionEndpointFilter : IEndpointFilter
{
    private readonly Permission _permission;
    private readonly string? _resourceRouteValue;

    /// <summary>Creates a filter requiring one permission.</summary>
    /// <param name="permission">The permission, normally a generated constant.</param>
    /// <param name="resourceRouteValue">
    /// The route parameter naming the row this request is about, if any. The
    /// scaffolding passes <c>id</c> on its per-row routes and nothing on its list
    /// and create routes. Carried into the decision; not consulted by either
    /// shipped store.
    /// </param>
    public PermissionEndpointFilter(Permission permission, string? resourceRouteValue = null)
    {
        _permission = permission;
        _resourceRouteValue = resourceRouteValue;
    }

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;
        IPermissions? permissions = http.RequestServices.GetService<IPermissions>();

        if (permissions is null)
        {
            throw new InvalidOperationException(
                "This endpoint is gated by a permission, but the permission layer is not registered. "
                + "Call services.AddPermissions(PermissionCatalog.All) and register an IPermissionStore.");
        }

        string? resourceId = _resourceRouteValue is null
            ? null
            : http.Request.RouteValues.TryGetValue(_resourceRouteValue, out object? value) ? value?.ToString() : null;

        bool allowed = await permissions
            .AllowsAsync(http.User, _permission, resourceId, http.RequestAborted)
            .ConfigureAwait(false);

        if (allowed)
        {
            return await next(context).ConfigureAwait(false);
        }

        // 401 when nobody is signed in, so a browser can be sent to sign in; 403
        // when someone is and simply lacks the grant, because signing in again
        // will not help. The same distinction the authorization middleware makes,
        // for the same reason.
        return http.User.Identity?.IsAuthenticated == true
            ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : Results.StatusCode(StatusCodes.Status401Unauthorized);
    }
}
