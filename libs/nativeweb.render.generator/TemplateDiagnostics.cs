using Microsoft.CodeAnalysis;

namespace NativeWeb.Render.Generator;

/// <summary>
/// Every template mistake is a build error, not a runtime blank (spec 003 R12).
/// </summary>
internal static class TemplateDiagnostics
{
    private const string Category = "NativeWeb.Render";

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HeaderMalformed = Error(
        "NWT001", "Malformed template header",
        "Template '{0}': {1}");

    public static readonly DiagnosticDescriptor UnknownDirective = Error(
        "NWT002", "Unknown template directive",
        "Template '{0}': unknown directive '{1}' (expected @layout or @param)");

    public static readonly DiagnosticDescriptor ParamMalformed = Error(
        "NWT003", "Malformed @param",
        "Template '{0}': '@param {1}' must be '@param <type> <name>'");

    public static readonly DiagnosticDescriptor LayoutNotFound = Error(
        "NWT004", "Layout not found",
        "Template '{0}': @layout '{1}' does not match any template file");

    public static readonly DiagnosticDescriptor PartialNotFound = Error(
        "NWT005", "Partial not found",
        "Template '{0}': partial '{{{{> {1}}}}}' does not match any template file");

    public static readonly DiagnosticDescriptor PartialArgMismatch = Error(
        "NWT006", "Partial arguments do not match its @param list",
        "Template '{0}': partial '{1}' {2}");

    public static readonly DiagnosticDescriptor SlotMisused = Error(
        "NWT007", "Slot or fill misused",
        "Template '{0}': {1}");

    public static readonly DiagnosticDescriptor UnbalancedBlock = Error(
        "NWT008", "Unbalanced template block",
        "Template '{0}': {1}");

    public static readonly DiagnosticDescriptor DuplicateTemplate = Error(
        "NWT009", "Duplicate template class",
        "Templates '{0}' and '{1}' both compile to class '{2}' — rename one");

    public static readonly DiagnosticDescriptor ParamConflict = Error(
        "NWT010", "Parameter conflicts with layout",
        "Template '{0}': @param '{1}' is declared as '{2}' here but '{3}' in layout '{4}'");

    public static readonly DiagnosticDescriptor RequiredSlotUnfilled = Error(
        "NWT011", "Required slot not filled",
        "Template '{0}': layout '{1}' requires slot '{2}' — add '{{{{fill {2}}}}}…{{{{/fill}}}}' (or mark the slot optional with '{{{{slot? {2}}}}}')");

    public static readonly DiagnosticDescriptor ExpressionRejected = Error(
        "NWT012", "Template expression rejected",
        "Template '{0}': expression '{1}' contains '{2}' — template expressions are values, not statements; move logic into the calling C#");

    public static readonly DiagnosticDescriptor UnknownLocaleKey = Error(
        "NWT013", "Unknown locale key",
        "Template '{0}': '{{{{t:{1}}}}}' is not defined in locales/en.json — every key must exist in the English root catalog");

    public static readonly DiagnosticDescriptor OrphanLocaleKey = Error(
        "NWT014", "Orphan locale override",
        "Locale '{0}': key '{1}' does not exist in en.json — overrides can only refine keys the English root defines (typo?)");

    public static readonly DiagnosticDescriptor LocaleMalformed = Error(
        "NWT015", "Malformed locale catalog",
        "Locale file '{0}': {1}");

    public static readonly DiagnosticDescriptor LooseAttributeInterpolation = Error(
        "NWT016", "Interpolation outside a quoted attribute value",
        "Template '{0}': {1} — TemplateWriter encodes '<', '>', '&', '\"' and '\\'' only, which is safe in text and inside a quoted attribute value and nowhere else; quote the attribute value");
}
