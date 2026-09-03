using Microsoft.CodeAnalysis;

namespace NativeWeb.Permissions.Generator;

// Everything this generator refuses to guess at. Each is an error rather than a
// warning: the alternative to stopping is emitting a permission name the author
// did not mean, which becomes a grant row in a database and outlives the build
// that produced it.
internal static class PermissionsDiagnostics
{
    internal static readonly DiagnosticDescriptor ExtraCollidesWithCore = new(
        id: "NWP001",
        title: "Extra permission name is one of the four",
        messageFormat: "'{0}' declares the extra permission '{1}', which is already one of the four every model gets. Remove it.",
        category: "NativeWeb.Permissions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ExtraNotUsable = new(
        id: "NWP002",
        title: "Extra permission name is not usable",
        messageFormat: "'{0}' declares the extra permission '{1}'. A permission name must match [a-z][a-z0-9_]*, because a dot separates the model from the action and a colon separates the assembly scope.",
        category: "NativeWeb.Permissions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NoUsableScope = new(
        id: "NWP003",
        title: "Assembly name cannot form a permission scope",
        messageFormat: "The assembly name '{0}' cannot be used as a permission scope. Add [assembly: PermissionScope(\"...\")] with a name matching [a-z][a-z0-9_.]*.",
        category: "NativeWeb.Permissions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ScopeNotUsable = new(
        id: "NWP004",
        title: "Permission scope is not usable",
        messageFormat: "[assembly: PermissionScope(\"{0}\")] is not a usable scope. It must match [a-z][a-z0-9_.]*.",
        category: "NativeWeb.Permissions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateExtra = new(
        id: "NWP005",
        title: "Extra permission name declared twice",
        messageFormat: "'{0}' declares the extra permission '{1}' more than once",
        category: "NativeWeb.Permissions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ModelNameCollides = new(
        id: "NWP006",
        title: "Two models produce the same permission names",
        messageFormat: "'{0}' and '{1}' have the same type name, so they would produce the same permission names within one assembly scope. Rename one, or move it to its own assembly.",
        category: "NativeWeb.Permissions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
