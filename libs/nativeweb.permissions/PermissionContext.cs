using System.Collections.Frozen;
using System.Security.Claims;

namespace NativeWeb.Permissions;

/// <summary>
/// The shipped <see cref="IPermissionContext"/>: one store call per subject,
/// intersected with the registry, held for the rest of the request.
/// </summary>
/// <remarks>
/// <para>The answer is held per subject, not per instance. Scoped to one request
/// there is normally only ever one, and caching on the instance alone was how this
/// was first written -- but it made the <c>user</c> parameter a lie: a second call
/// with a different principal got the first one's permissions. A request that
/// re-authenticates, or a handler that asks about a principal it constructed, is
/// answered about the principal it passed.</para>
/// <para>The key is the resolved subject rather than the principal reference. Two
/// principals for one subject share the read, which reference identity would
/// duplicate, and a <see cref="ClaimsIdentity"/> whose claims are changed in place
/// is answered about the subject it now carries, which reference identity would get
/// wrong.</para>
/// <para>Subject and held set are one field, written once, because they are one
/// answer. Written separately they tear: the subject is known before the store call
/// and the set only after it, so two overlapping calls on one instance could leave
/// the second subject beside the first subject's permissions, which is the defect
/// this memo exists to avoid rather than to cause.</para>
/// </remarks>
public sealed class PermissionContext : IPermissionContext
{
    private static readonly FrozenSet<string> None = FrozenSet<string>.Empty;

    private readonly IPermissionStore _store;
    private readonly PermissionRegistry _registry;
    private Answer? _answer;

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

        string? subject = user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(subject))
        {
            return None;
        }

        Answer? held = _answer;

        if (held is not null && string.Equals(held.Subject, subject, StringComparison.Ordinal))
        {
            return held.Held;
        }

        // R6: the local record is created here, on first sight of a subject, and
        // holds the opaque string and nothing else. Idempotent, so the cost after
        // the first request for a subject is one round trip the store can make
        // free.
        await _store.EnsureSubjectAsync(subject, ct).ConfigureAwait(false);

        IReadOnlyCollection<string> granted = await _store.GrantedAsync(subject, ct).ConfigureAwait(false);

        IReadOnlySet<string> declared = granted.Count == 0
            ? None
            : granted.Where(_registry.Contains).ToFrozenSet(StringComparer.Ordinal);

        _answer = new Answer(subject, declared);

        return declared;
    }

    private sealed record Answer(string Subject, IReadOnlySet<string> Held);
}
