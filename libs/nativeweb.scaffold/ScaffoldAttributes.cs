namespace NativeWeb.Scaffold;

/// <summary>
/// Marks a model for CRUD scaffolding. The generator emits a route group, the
/// list/create/edit/delete endpoints under it, and the HTML for each view.
/// </summary>
/// <remarks>
/// The declaration is the contract. A property the model does not declare cannot
/// be bound, listed, searched or written -- Rails' strong parameters, except the
/// compiler enforces it instead of a runtime allowlist.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldAttribute : Attribute
{
    /// <summary>Plural, human-facing name for headings and nav. Defaults to the type name.</summary>
    public string? Display { get; init; }

    /// <summary>URL segment under <c>/admin</c>. Defaults to the lower-cased type name.</summary>
    public string? Route { get; init; }
}

/// <summary>
/// Who may reach the scaffolded routes.
/// </summary>
/// <remarks>
/// Named <c>[Auth]</c> rather than <c>[Authorize]</c> for two reasons: it does not
/// collide with the ASP.NET attribute a consumer may also have in scope, and the
/// common cases stay short. The ladder is deliberate --
/// <c>[Auth]</c> for any signed-in user, <c>[Auth(Role = "admin")]</c> for the
/// 90% case, <c>[Auth(Policy = "...")]</c> as the escape hatch to a full ASP.NET
/// policy when the short forms run out. Per-operation overrides let a model be
/// readable by more people than it is writable by, which is the usual shape.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AuthAttribute : Attribute
{
    /// <summary>Role required for every operation unless a per-operation property overrides it.</summary>
    public string? Role { get; init; }

    /// <summary>Full ASP.NET authorization policy name. Wins over <see cref="Role"/>.</summary>
    public string? Policy { get; init; }

    /// <summary>Role required to view the list and detail pages.</summary>
    public string? Read { get; init; }

    /// <summary>Role required to create or edit.</summary>
    public string? Write { get; init; }

    /// <summary>Role required to delete.</summary>
    public string? Delete { get; init; }
}

/// <summary>Show this property as a column in the list view.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldListAttribute : Attribute
{
    /// <summary>Column heading. Defaults to the property name, de-camel-cased.</summary>
    public string? Label { get; init; }
}

/// <summary>Include this property in the list view's search filter.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldSearchAttribute : Attribute;

/// <summary>
/// Render this property as a multi-line textarea rather than a single-line input.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldMultilineAttribute : Attribute;

/// <summary>
/// Exclude this property from the create and edit forms. It still appears in the
/// list view if it carries <see cref="ScaffoldListAttribute"/>.
/// </summary>
/// <remarks>
/// This is the mass-assignment control. A read-only property (an id, a created-at
/// stamp, a computed total) must not be writable just because it is displayable,
/// and the failure mode of getting that wrong is silent.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldReadOnlyAttribute : Attribute;

/// <summary>Mark the property that identifies a row. Exactly one per model.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldKeyAttribute : Attribute;
