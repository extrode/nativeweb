namespace NativeWeb.Scaffold;

/// <summary>
/// The URL prefix every scaffolded route sits under. Defaults to <c>/admin</c>.
/// </summary>
/// <remarks>
/// <para>Applied once, to the assembly:</para>
/// <code>
/// [assembly: ScaffoldRoot("/manage")]
/// </code>
/// <para>Compile-time rather than a <c>MapScaffolding(prefix)</c> parameter, so the
/// per-model base path stays a <c>const</c> and every emitted link, redirect and
/// form action is a literal. A runtime prefix would have to be threaded through
/// every generated method or held in static state, and static state is shared by
/// two hosts in one process — which is exactly what an integration suite is.</para>
/// <para>Must begin with <c>/</c> and carry no query or fragment. A trailing slash
/// is trimmed. <c>"/"</c> is legal and puts the scaffolding at the site root.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ScaffoldRootAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="path">The prefix, e.g. <c>/manage</c>.</param>
    public ScaffoldRootAttribute(string path) => Path = path;

    /// <summary>The prefix as written.</summary>
    public string Path { get; }
}

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

    /// <summary>
    /// URL segment under the scaffold root (<c>/admin</c> unless
    /// <see cref="ScaffoldRootAttribute"/> says otherwise). Defaults to the
    /// lower-cased, pluralised type name.
    /// </summary>
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

    /// <summary>
    /// Let anyone reach the list, with no authentication, no role and no
    /// permission.
    /// </summary>
    /// <remarks>
    /// <para>A typed flag rather than a sentinel in one of the string slots above,
    /// because those are documented to hold role names and a slot that sometimes
    /// holds a role and sometimes the word "everyone" cannot be read without
    /// knowing which.</para>
    /// <para>It lifts every check off the read operation, including the permission
    /// a model that carries <c>[Permissions]</c> would otherwise need. That is its
    /// whole purpose: a model whose list is public and whose writes are not.</para>
    /// <para>The write routes are unaffected, and so is the edit form at
    /// <c>/{id}</c> -- the scaffolding has no read-only detail page, so the only
    /// read operation is the list.</para>
    /// </remarks>
    public bool PublicRead { get; init; }
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

/// <summary>Let the list view be ordered by this property.</summary>
/// <remarks>
/// The set of marked properties is an allow-list, and that is the point of marking
/// rather than allowing any property: the sort property arrives from the query
/// string, so a scaffolded list that ordered by whatever name it was handed would
/// leave every store implementation to reject the unknown ones, and the one that
/// forgets has an attacker-chosen identifier reaching its data layer. The generated
/// endpoint rejects a name outside this set before the store is called, and passes
/// the set down as <c>sortFields</c> so an implementation does not re-declare it --
/// the same division <see cref="ScaffoldSearchAttribute"/> settled on.
/// <para>
/// The <see cref="ScaffoldKeyAttribute"/> property is always sortable, marked or
/// not, because it is the default order and the tiebreaker every other order is
/// resolved by. Marking it is allowed and changes nothing.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldSortAttribute : Attribute;

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

/// <summary>
/// This <c>string?</c> property holds the key of one row of another scaffolded
/// model, and the form offers that model's rows in a dropdown with a "(none)" choice.
/// </summary>
/// <remarks>
/// The target must carry <see cref="ScaffoldAttribute"/> in the same assembly and
/// be keyed by a string; the generator reports a diagnostic otherwise. The
/// generated handler reads the choices through the target's own
/// <see cref="IScaffoldStore{T}"/>, so a host that scaffolds a reference has to
/// register the target's store too. Whether a posted key exists is the store's
/// call, not the form's: a store refuses an unknown one with
/// <see cref="ScaffoldRefusedException"/>, the way it refuses anything else it alone
/// can know, and the emitted check against the loaded choices would only have been
/// a snapshot.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldReferenceAttribute(Type target) : Attribute
{
    /// <summary>The scaffolded model whose rows are the choices.</summary>
    public Type Target { get; } = target;
}

/// <summary>
/// This <c>IReadOnlyList&lt;string&gt;</c> property holds the keys of zero or more
/// rows of another scaffolded model, and the form offers that model's rows as a
/// checkbox list or, with <see cref="Control"/>, a multi-select.
/// </summary>
/// <remarks>
/// Same rules as <see cref="ScaffoldReferenceAttribute"/> for the target and for
/// who checks a posted key. Nothing ticked posts nothing, which binds as the empty
/// list and saves as "no links"; the store replaces the held set with the posted
/// one on every save rather than adding to it.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScaffoldRelationAttribute(Type target) : Attribute
{
    /// <summary>The scaffolded model whose rows are the choices.</summary>
    public Type Target { get; } = target;

    /// <summary>Which control renders the choices. Checkboxes unless said otherwise.</summary>
    public ScaffoldRelationControl Control { get; init; }
}

/// <summary>The two plain-HTML controls a <see cref="ScaffoldRelationAttribute"/> can render.</summary>
public enum ScaffoldRelationControl
{
    /// <summary>One checkbox per choice. Every choice is visible; suits a short, stable set.</summary>
    Checkboxes,

    /// <summary>A <c>&lt;select multiple&gt;</c>. Compact and keyboard-searchable; suits a long list.</summary>
    MultiSelect,
}
