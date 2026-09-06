using System.Collections.Generic;
using System.Linq;

namespace NativeWeb.Scaffold.Generator;

// The generator's view of one [Scaffold] model. Value-equatable so the
// incremental pipeline can cache on it -- a record with a plain List<T> would
// compare by reference and defeat that, hence the explicit Equals/GetHashCode.
internal sealed class ScaffoldModel
{
    public string Namespace { get; set; } = "";

    public string TypeName { get; set; } = "";

    public string Display { get; set; } = "";

    /// Singular human-facing name, for the "New X" / "Edit X" headings.
    public string Singular { get; set; } = "";

    public string Route { get; set; } = "";

    /// Every property carrying [ScaffoldKey], in declaration order. More than one
    /// is refused at emit time rather than resolved: the last one used to win in
    /// silence, against the attribute's own promise of exactly one.
    public List<string> KeyProperties { get; set; } = [];

    public string KeyProperty => KeyProperties.Count > 0 ? KeyProperties[0] : "";

    public AuthModel? Auth { get; set; }

    // Whether the model also carries [Permissions]. Read by metadata name, so a
    // consumer that scaffolds without the permissions library sets this false and
    // gets byte-identical output to before spec 024.
    public bool HasPermissions { get; set; }

    public List<PropertyModel> Properties { get; set; } = [];

    public string FullyQualifiedName =>
        string.IsNullOrEmpty(Namespace) ? TypeName : Namespace + "." + TypeName;

    public override bool Equals(object? obj) =>
        obj is ScaffoldModel other
        && Namespace == other.Namespace
        && TypeName == other.TypeName
        && Display == other.Display
        && Singular == other.Singular
        && Route == other.Route
        && KeyProperties.SequenceEqual(other.KeyProperties)
        && Equals(Auth, other.Auth)
        && HasPermissions == other.HasPermissions
        && Properties.SequenceEqual(other.Properties);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + Namespace.GetHashCode();
            hash = (hash * 31) + TypeName.GetHashCode();
            hash = (hash * 31) + Route.GetHashCode();
            foreach (PropertyModel p in Properties)
            {
                hash = (hash * 31) + p.GetHashCode();
            }

            return hash;
        }
    }
}

internal sealed class AuthModel
{
    public string? Role { get; set; }

    public string? Policy { get; set; }

    public string? Read { get; set; }

    public string? Write { get; set; }

    public string? Delete { get; set; }

    // Lifts every check off the read operation, permission included.
    public bool PublicRead { get; set; }

    // Read/Write/Delete fall back to Role, then to "any authenticated user".
    public string? RoleFor(ScaffoldOperation operation)
    {
        string? specific = operation switch
        {
            ScaffoldOperation.Read => Read,
            ScaffoldOperation.Write => Write,
            ScaffoldOperation.Delete => Delete,
            _ => null,
        };

        return specific ?? Role;
    }

    public override bool Equals(object? obj) =>
        obj is AuthModel other
        && Role == other.Role
        && Policy == other.Policy
        && Read == other.Read
        && Write == other.Write
        && Delete == other.Delete
        && PublicRead == other.PublicRead;

    public override int GetHashCode() => (Role, Policy, Read, Write, Delete, PublicRead).GetHashCode();
}

internal enum ScaffoldOperation
{
    Read,
    Write,
    Delete,
}

internal sealed class PropertyModel
{
    public string Name { get; set; } = "";

    public string Label { get; set; } = "";

    public string TypeName { get; set; } = "";

    public bool IsNullable { get; set; }

    public bool InList { get; set; }

    public bool Searchable { get; set; }

    public bool Sortable { get; set; }

    public bool Multiline { get; set; }

    public bool ReadOnly { get; set; }

    public bool IsKey { get; set; }

    public bool IsEnum { get; set; }

    /// The enum's member names in declaration order; empty unless IsEnum.
    public List<string> EnumMembers { get; set; } = [];

    /// The fully qualified name of the [ScaffoldReference] target, or null.
    public string? Reference { get; set; }

    /// The fully qualified name of the [ScaffoldRelation] target, or null.
    public string? Relation { get; set; }

    /// True when the relation renders as a multi-select rather than checkboxes.
    public bool MultiSelect { get; set; }

    /// A relation property holds many keys and binds as string[] rather than string?.
    public bool IsCollection => Relation is not null;

    // The form field name. Lower-camel, matching the HTML the generator emits, so
    // the binder and the markup cannot drift apart -- they are produced from this
    // one value.
    public string FieldName =>
        Name.Length == 0 ? Name : char.ToLowerInvariant(Name[0]) + Name.Substring(1);

    public override bool Equals(object? obj) =>
        obj is PropertyModel other
        && Name == other.Name
        && Label == other.Label
        && TypeName == other.TypeName
        && IsNullable == other.IsNullable
        && InList == other.InList
        && Searchable == other.Searchable
        && Sortable == other.Sortable
        && Multiline == other.Multiline
        && ReadOnly == other.ReadOnly
        && IsKey == other.IsKey
        && IsEnum == other.IsEnum
        && EnumMembers.SequenceEqual(other.EnumMembers)
        && Reference == other.Reference
        && Relation == other.Relation
        && MultiSelect == other.MultiSelect;

    // Sortable belongs in both members, not just Equals. The incremental pipeline
    // caches on this value, so a field the hash omits is a field whose change can
    // land in a bucket the comparer never re-runs -- adding [ScaffoldSort] then
    // regenerates nothing and the column stays unsortable with no error anywhere.
    public override int GetHashCode() =>
        (Name, TypeName, IsNullable, InList, Searchable, Sortable, Multiline, ReadOnly, IsKey).GetHashCode()
        ^ (IsEnum, string.Join(",", EnumMembers), Reference, Relation, MultiSelect).GetHashCode();
}
