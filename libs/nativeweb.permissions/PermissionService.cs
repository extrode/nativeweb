using System.Security.Claims;

namespace NativeWeb.Permissions;

/// <summary>
/// The shipped <see cref="IPermissions"/>. Reads the request's held set and asks
/// whether the permission is in it.
/// </summary>
public sealed class PermissionService : IPermissions
{
    private readonly IPermissionContext _context;

    /// <summary>Creates the decision service over the request's permission context.</summary>
    public PermissionService(IPermissionContext context) => _context = context;

    /// <inheritdoc/>
    public async ValueTask<bool> AllowsAsync(
        ClaimsPrincipal user,
        Permission permission,
        string? resourceId = null,
        CancellationToken ct = default)
    {
        // An uninitialised default(Permission) matches nothing. Checked before the
        // store is touched, so a caller that reached one through an uninitialised
        // field gets a refusal rather than a lookup for the empty name.
        if (permission.IsEmpty)
        {
            return false;
        }

        IReadOnlySet<string> held = await _context.HeldAsync(user, ct).ConfigureAwait(false);
        return held.Contains(permission.Name);
    }
}
