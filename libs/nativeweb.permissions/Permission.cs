namespace NativeWeb.Permissions;

/// <summary>
/// One permission, named <c>{scope}:{model}.{action}</c>.
/// </summary>
/// <remarks>
/// <para>A struct wrapping a string rather than the string itself, and every API
/// that takes a permission takes this type. That is half of what makes removing a
/// declaration a build error: the generated constants are the only supply of
/// values, so a call site naming a permission that is no longer declared has
/// nothing to pass and fails as CS0117 at the constant, not silently at run time
/// against a grant nobody holds.</para>
/// <para>The other half is the catalog. A name can still be constructed by hand
/// through <see cref="Permission(string)"/>, which is the author's visible opt-out;
/// it is not a way around the catalog, because a name outside the catalog is
/// dropped when a subject's grants are resolved.</para>
/// </remarks>
public readonly struct Permission : IEquatable<Permission>
{
    /// <summary>Creates a permission from its full name.</summary>
    /// <param name="name">The full name, e.g. <c>sample:article.change</c>.</param>
    public Permission(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The full name. Never null or empty on a constructed value.</summary>
    public string Name { get; }

    /// <summary>True when this is the uninitialised <c>default</c>.</summary>
    /// <remarks>
    /// A <c>default(Permission)</c> can be reached through an uninitialised field or
    /// an array, so callers that must not treat it as a real permission check this.
    /// It matches no grant.
    /// </remarks>
    public bool IsEmpty => string.IsNullOrEmpty(Name);

    /// <inheritdoc/>
    public bool Equals(Permission other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Permission other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

    /// <inheritdoc/>
    public override string ToString() => Name ?? string.Empty;

    /// <summary>Equality by name, ordinal.</summary>
    public static bool operator ==(Permission left, Permission right) => left.Equals(right);

    /// <summary>Inequality by name, ordinal.</summary>
    public static bool operator !=(Permission left, Permission right) => !left.Equals(right);
}
