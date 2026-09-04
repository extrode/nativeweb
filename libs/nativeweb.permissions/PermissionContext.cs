using System.Collections.Frozen;
using System.Security.Claims;

namespace NativeWeb.Permissions;

/// <summary>
/// The shipped <see cref="IPermissionContext"/>: one store call per request,
/// intersected with the registry, held for the rest of the request.
/// </summary>
/// <remarks>
/// The answer is held per principal, not per instance. Scoped to one request there
/// is normally only ever one, and caching on the instance alone was how this was
/// first written -- but it made the <c>user</c> parameter a lie: a second call with
/// a different principal got the first one's permissions. A request that
/// re-authenticates, or a handler that asks about a principal it constructed, is
/// answered about the principal it passed.
/// </remarks>
public sealed class PermissionContext : IPermissionContext
{
    private static readonly FrozenSet<string> None = FrozenSet<string>.Empty;

    private readonly IPermissionStore _store;
    private readonly PermissionRegistry _registry;
    private ClaimsPrincipal? _principal;
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

        if (_held is not null && ReferenceEquals(_principal, user))
        {
            return _held;
        }

        _principal = user;

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
