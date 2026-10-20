namespace NativeWeb.Permissions;

/// <summary>
/// The scope every permission name in this assembly is prefixed with. Defaults to
/// the assembly's own name, lower-cased.
/// </summary>
/// <remarks>
/// <para>Applied once, to the assembly:</para>
/// <code>
/// [assembly: PermissionScope("sample")]
/// </code>
/// <para>Assembly-level for the same reason
/// <see cref="NativeWeb.Scaffold.ScaffoldRootAttribute"/> is: a permission name has
/// to be a compile-time constant, and a per-assembly prefix is what keeps two
/// applications that each define an <c>Article</c> from granting each other's rows.
/// A grant row is a string in a database, so the scope is the only thing standing
/// between two products sharing one.</para>
/// <para>What is deliberately NOT assembly-level is whether permissions are
/// enforced. A switch that changed every model's access posture from outside the
/// model would make the model unreadable on its own -- you could not look at
/// <c>Article</c> and know whether its routes were gated. Enforcement follows the
/// presence of <see cref="PermissionsAttribute"/> on the model and nothing
/// else.</para>
/// <para>Must match <c>[a-z][a-z0-9_.]*</c>. The separator between the scope and
/// the rest of the name is a colon, so a dot inside a scope stays unambiguous.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PermissionScopeAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="scope">The prefix, e.g. <c>sample</c>.</param>
    public PermissionScopeAttribute(string scope) => Scope = scope;

    /// <summary>The scope as written.</summary>
    public string Scope { get; }
}
