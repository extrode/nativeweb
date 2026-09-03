using System.Collections.Frozen;
using System.Security.Claims;

namespace NativeWeb.Permissions;

/// <summary>
/// The shipped <see cref="IPermissionContext"/>: one store call per request,
/// intersected with the registry, held for the rest of the request.
/// </summary>
/// <remarks>
/// The subject is read once, on the first call, and the answer does not change
/// afterwards even if the principal does. That is safe because the instance is
/// scoped to one request and authentication has run before any endpoint filter or
/// handler asks; it is stated because a caller that resolved this before
/// authentication would get the anonymous answer for the whole request.
/// </remarks>
public sealed class PermissionContext : IPermissionContext
{
    private static readonly FrozenSet<string> None = FrozenSet<string>.Empty;

    private readonly IPermissionStore _store;
    private readonly PermissionRegistry _registry;
    private IReadOnlySet<string>? _held;

    /// <summary>Creates the context.</summary>
    public PermissionContext(IPermissionStore store, PermissionRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlySet<string>> HeldAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (_held is not null)
        {
            return _held;
        }

        string? subject = user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(subject))
        {
            _held = None;
            return _held;
        }

        // R6: the local record is created here, on first sight of a subject, and
        // holds the opaque string and nothing else. Idempotent, so the cost after
        // the first request for a subject is one round trip the store can make
        // free.
        await _store.EnsureSubjectAsync(subject, ct).ConfigureAwait(false);

        IReadOnlyCollection<string> granted = await _store.GrantedAsync(subject, ct).ConfigureAwait(false);

        _held = granted.Count == 0
            ? None
            : granted.Where(_registry.Contains).ToFrozenSet(StringComparer.Ordinal);

        return _held;
    }
}
