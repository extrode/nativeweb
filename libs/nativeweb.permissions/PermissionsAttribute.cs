namespace NativeWeb.Permissions;

/// <summary>
/// Marks a model as having permissions. Four are produced for it without being
/// named -- view, add, change and delete -- and any extras are named here.
/// </summary>
/// <remarks>
/// <para>Django gets these from a model simply existing, because every model runs
/// through a metaclass that can see it. A source generator cannot: a plain class is
/// invisible until an attribute marks it. So one marker is irreducible, and this is
/// it -- the compile-time equivalent of Django's <c>Meta</c>.</para>
/// <para>Separate from <see cref="NativeWeb.Scaffold.ScaffoldAttribute"/> on
/// purpose. Declaring what a model's permissions ARE and generating an admin that
/// enforces them are different jobs, and a model may do the first without the
/// second: a hand-written endpoint checks a permission through
/// <see cref="IPermissions"/> whether the model has screens or not.</para>
/// <para>Separate from <see cref="NativeWeb.Scaffold.AuthAttribute"/> too, and in
/// the other direction. That attribute's slots hold role and policy names and
/// continue to hold nothing else; a permission name is never valid in one, because
/// a slot that sometimes holds a role and sometimes a permission cannot be read
/// without already knowing which.</para>
/// <code>
/// [Permissions("publish")]
/// [Scaffold(Display = "Articles")]
/// public sealed class Article { }
/// </code>
/// <para>That yields <c>ArticlePermissions.View</c>, <c>.Add</c>, <c>.Change</c>,
/// <c>.Delete</c> and <c>.Publish</c>. The four gate the generated routes with no
/// further declaration. An extra gates nothing by itself -- it exists to be checked
/// by hand.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PermissionsAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="extras">
    /// Extra permission names beyond the four. Each must match
    /// <c>[a-z][a-z0-9_]*</c> and must not be one of <c>view</c>, <c>add</c>,
    /// <c>change</c> or <c>delete</c>; the generator reports a diagnostic otherwise.
    /// </param>
    public PermissionsAttribute(params string[] extras) => Extras = extras;

    /// <summary>The extra permission names as written.</summary>
    public string[] Extras { get; }
}
