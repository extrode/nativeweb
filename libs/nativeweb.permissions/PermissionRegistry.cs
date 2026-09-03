using System.Collections.Frozen;

namespace NativeWeb.Permissions;

/// <summary>
/// Every permission this application declares. Passed to
/// <see cref="PermissionsServiceCollectionExtensions.AddPermissions"/> at startup
/// and consulted on every decision.
/// </summary>
/// <remarks>
/// <para>This is the half of R14 the compiler cannot do. Removing a declaration
/// makes its constant stop existing, so no code can name it -- but a grant row in
/// the database naming it survives, and would otherwise keep granting something
/// nobody can revoke through a constant that no longer compiles. A subject's grants
/// are intersected with this registry, so a name that is not declared grants
/// nothing, and the access admin lists the row as undeclared with a revoke control
/// beside it. Removing the row itself is a cleanup script, never automatic.</para>
/// <para>The generator emits one <c>PermissionCatalog.All</c> per assembly and the
/// host passes it in. It is not discovered: a static registry is process-wide, and
/// two hosts in one process is what an integration suite is, so discovery would let
/// one host's declarations answer the other host's questions. Passing it makes the
/// scope of a catalog the same as the scope of a service provider.</para>
/// </remarks>
public sealed class PermissionRegistry
{
    /// <summary>Creates a registry over the declared permissions.</summary>
    /// <param name="declared">
    /// Usually the generated <c>PermissionCatalog.All</c>. May be empty, which
    /// makes every decision refuse -- correct for an application that declares
    /// nothing and gates nothing.
    /// </param>
    public PermissionRegistry(IEnumerable<Permission> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        Declared = [.. declared];
        Names = Declared.Select(permission => permission.Name).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The declared permissions, in the order given.</summary>
    public IReadOnlyList<Permission> Declared { get; }

    /// <summary>The declared names, for the intersection every decision does.</summary>
    public IReadOnlySet<string> Names { get; }

    /// <summary>True when <paramref name="name"/> is declared by this application.</summary>
    public bool Contains(string name) => Names.Contains(name);
}
