using System.Security.Claims;

namespace NativeWeb.Permissions;

/// <summary>
/// Turns a principal into the subject string the store keys on, the one way this
/// application chose.
/// </summary>
/// <remarks>
/// A singleton, built once from <see cref="PermissionSubjectOptions"/>, because the
/// decision cannot vary by request: two requests resolving the same person to two
/// different subjects would grant against one row and read from another.
/// </remarks>
public sealed class PermissionSubjectResolver
{
    private readonly string _claimName;
    private readonly Func<ClaimsPrincipal, string?>? _resolve;

    /// <summary>Builds the resolver from what the host configured.</summary>
    /// <param name="options">The configured claim name, or function, or neither.</param>
    /// <exception cref="InvalidOperationException">
    /// Both a claim name and a function were configured. Thrown at registration, so
    /// the application does not start, rather than at the first gated request, where
    /// it would be a 403 with an ambiguity behind it.
    /// </exception>
    public PermissionSubjectResolver(PermissionSubjectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClaimName);

        // Against the default rather than against null: leaving ClaimName alone
        // while supplying a function is the ordinary way to use a function, and
        // treating that as a conflict would make the second option unreachable.
        if (options.Resolve is not null
            && !string.Equals(options.ClaimName, ClaimTypes.NameIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PermissionSubjectOptions configures both ClaimName ('{options.ClaimName}') and Resolve. "
                + "A subject comes from one of them. Clear whichever is not wanted.");
        }

        _claimName = options.ClaimName;
        _resolve = options.Resolve;
    }

    /// <summary>
    /// The subject <paramref name="user"/> carries, or null if it carries none.
    /// </summary>
    /// <remarks>
    /// Null for an unauthenticated principal without consulting either option: a
    /// public page has no subject and that is not a fault. Null for an empty or
    /// whitespace-only answer, so a blank claim cannot create a blank row. Anything
    /// else is returned as it came back.
    /// </remarks>
    /// <param name="user">The principal the request is being decided for.</param>
    public string? Of(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? subject = _resolve is null ? user.FindFirstValue(_claimName) : _resolve(user);

        return string.IsNullOrWhiteSpace(subject) ? null : subject;
    }

    /// <summary>
    /// What was looked for, for a diagnostic that must not name the value found.
    /// </summary>
    internal string Source => _resolve is null ? "the '" + _claimName + "' claim" : "the configured resolver";
}
