namespace NativeWeb.Permissions;

/// <summary>
/// A store refused a write because only it could know the write was invalid --
/// a group name already in use, a group that has since been deleted.
/// </summary>
/// <remarks>
/// The same shape as <c>ScaffoldRefusedException</c> and for the same reason: the
/// admin cannot check these in advance without a snapshot that is stale the moment
/// it is taken, so the store refuses and the admin renders the refusal as a form
/// error rather than letting it become a 500.
/// </remarks>
public sealed class PermissionConflictException : Exception
{
    /// <summary>Creates the exception with a message the admin may show an operator.</summary>
    public PermissionConflictException(string message) : base(message) { }

    /// <summary>Creates the exception with an inner cause.</summary>
    public PermissionConflictException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Creates the exception with the default message.</summary>
    public PermissionConflictException() : base("The store refused the change.") { }
}
