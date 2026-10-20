using System.Collections.Frozen;
using System.Security.Claims;

using Microsoft.Extensions.Logging;

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
public sealed partial class PermissionContext : IPermissionContext
{
    private static readonly FrozenSet<string> None = FrozenSet<string>.Empty;

    private readonly IPermissionStore _store;
    private readonly PermissionRegistry _registry;
    private readonly PermissionSubjectResolver _subjects;
    private readonly ILogger<PermissionContext>? _logger;
    private Answer? _answer;
    private bool _reportedAbsent;

    /// <summary>Creates the context.</summary>
    /// <param name="store">Where grants live.</param>
    /// <param name="registry">Everything this application declares.</param>
    /// <param name="subjects">
    /// How a principal is named. Omitted, the subject is the
    /// <see cref="ClaimTypes.NameIdentifier"/> claim, which is what
    /// <see cref="PermissionSubjectOptions"/> defaults to.
    /// </param>
    /// <param name="logger">
    /// Where an authenticated principal carrying no subject is reported. Omitted,
    /// that state is silent, which is the state spec 025 exists to end -- so a host
    /// wiring this by hand rather than through <c>AddPermissions</c> should pass one.
    /// </param>
    public PermissionContext(
        IPermissionStore store,
        PermissionRegistry registry,
        PermissionSubjectResolver? subjects = null,
        ILogger<PermissionContext>? logger = null)
    {
        _store = store;
        _registry = registry;
        _subjects = subjects ?? new PermissionSubjectResolver(new PermissionSubjectOptions());
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlySet<string>> HeldAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        string? subject = _subjects.Of(user);

        if (subject is null)
        {
            // Only when the principal is authenticated, and once. An anonymous
            // request has no subject by definition and is the ordinary state of a
            // public page; warning about it would bury the case this is for, which
            // is a signed-in caller the permission layer cannot name and therefore
            // refuses everything.
            if (!_reportedAbsent && user.Identity?.IsAuthenticated == true)
            {
                _reportedAbsent = true;

                if (_logger is not null)
                {
                    NoSubject(_logger, _subjects.Source, user.Identity.AuthenticationType ?? "(none)");
                }
            }

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

    // What was looked for and which scheme authenticated the caller, never the claim
    // values: the subject is an identifier for a person, and a diagnostic that logged
    // it would hand every product on this layer a retention question of its own.
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "A principal authenticated by '{Scheme}' carries no permission subject in {Source}. "
            + "Every permission decision for this request refuses. Configure "
            + "AddPermissions(catalog, options => ...) if the subject arrives under another name.")]
    private static partial void NoSubject(ILogger logger, string source, string scheme);

    private sealed record Answer(string Subject, IReadOnlySet<string> Held);
}
