using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace NativeWeb.Forms.Generator;

// One field of a declared form: the constructor parameter the emitted binder
// fills, and the wire name it reads from.
internal sealed class FormFieldModel
{
    public string ParameterName { get; set; } = "";

    public string WireName { get; set; } = "";
}

// A [Form]-decorated type, parsed into the shape the emitter needs. Built only
// when the declaration is usable; every rejection is a diagnostic instead, which
// is R8 -- a malformed model must not produce code that fails to compile with a
// message pointing at generated source the author never wrote.
internal sealed class FormModel
{
    public string Namespace { get; set; } = "";

    public string TypeName { get; set; } = "";

    public string TypeKeyword { get; set; } = "record";

    public List<FormFieldModel> Fields { get; } = new();

    public string FullyQualifiedName =>
        Namespace.Length == 0 ? TypeName : Namespace + "." + TypeName;

    // Returns null and reports, or returns a model and reports nothing. The
    // caller emits for exactly the non-null results.
    public static FormModel? Build(INamedTypeSymbol type, Location location, List<Diagnostic> diagnostics)
    {
        if (type.TypeKind != TypeKind.Class || type.IsStatic)
        {
            // IBindableFromHttpContext<TSelf> is constrained to reference types, so a
            // struct cannot implement it at all. [Form]'s AttributeUsage already
            // blocks the obvious cases; this catches what it cannot.
            diagnostics.Add(Diagnostic.Create(FormDiagnostics.UnsupportedDeclaration, location, type.Name));
            return null;
        }

        if (type.ContainingType is not null || type.IsGenericType)
        {
            // A nested type would need every containing declaration emitted as
            // partial too, and a generic one would need the binder instantiated per
            // construction. Both are diagnosable now and supportable later; emitting
            // code that does not compile is neither.
            diagnostics.Add(Diagnostic.Create(FormDiagnostics.UnsupportedDeclaration, location, type.Name));
            return null;
        }

        if (!IsPartial(type))
        {
            diagnostics.Add(Diagnostic.Create(FormDiagnostics.NotPartial, location, type.Name));
            return null;
        }

        IMethodSymbol[] candidates = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsImplicitlyDeclared)
            .ToArray();

        // A record's compiler-generated copy constructor takes the record itself and
        // is not a shape anything can bind; it is never a candidate.
        candidates = candidates
            .Where(c => !(c.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, type)))
            .ToArray();

        if (candidates.Length != 1)
        {
            diagnostics.Add(Diagnostic.Create(
                FormDiagnostics.NoSingleConstructor, location, type.Name, candidates.Length));
            return null;
        }

        var model = new FormModel
        {
            Namespace = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            TypeName = type.Name,
            TypeKeyword = type.IsRecord ? "record" : "class",
        };

        bool usable = true;
        var seen = new Dictionary<string, string>();

        foreach (IParameterSymbol parameter in candidates[0].Parameters)
        {
            Location where = parameter.Locations.FirstOrDefault() ?? location;

            if (!IsNullableString(parameter.Type))
            {
                // Every value in a form arrives as text, and a field the client omits
                // arrives not at all. `string?` is the only member type that can state
                // both facts; a conversion belongs in the handler, where a bad one can
                // be reported to the person who typed it.
                diagnostics.Add(Diagnostic.Create(
                    FormDiagnostics.UnsupportedMemberType,
                    where,
                    parameter.Name,
                    parameter.Type.ToDisplayString()));
                usable = false;
                continue;
            }

            string wire = WireName(parameter);

            if (seen.TryGetValue(wire, out string? first))
            {
                diagnostics.Add(Diagnostic.Create(
                    FormDiagnostics.DuplicateWireName, where, wire, first, parameter.Name));
                usable = false;
                continue;
            }

            seen[wire] = parameter.Name;
            model.Fields.Add(new FormFieldModel { ParameterName = parameter.Name, WireName = wire });
        }

        return usable ? model : null;
    }

    // [FormField("...")] wins; otherwise the parameter name with its first letter
    // lowercased, which is what the hand-written endpoints already post.
    private static string WireName(IParameterSymbol parameter)
    {
        foreach (AttributeData attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == FormGenerator.FormFieldAttribute
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string given
                && given.Length > 0)
            {
                return given;
            }
        }

        string name = parameter.Name;
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static bool IsNullableString(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_String
        && type.NullableAnnotation != NullableAnnotation.NotAnnotated;

    private static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }

        return false;
    }
}

internal static class FormDiagnostics
{
    internal static readonly DiagnosticDescriptor NotPartial = new(
        id: "NWF001",
        title: "Form model is not partial",
        messageFormat: "'{0}' is decorated [Form] but is not declared partial. The generated binder is emitted onto the same type, so it needs a partial declaration to extend.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NoSingleConstructor = new(
        id: "NWF002",
        title: "Form model has no single accessible constructor",
        messageFormat: "'{0}' is decorated [Form] but has {1} accessible constructors. A form model binds through exactly one, normally a record's primary constructor.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedMemberType = new(
        id: "NWF003",
        title: "Form member type cannot be bound",
        messageFormat: "'{0}' is of type '{1}'. A form field arrives as text and may be absent, so a bound member must be declared 'string?'; convert it in the handler.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateWireName = new(
        id: "NWF004",
        title: "Two form members bind the same field",
        messageFormat: "The form field '{0}' is bound by both '{1}' and '{2}'. Give one of them a distinct [FormField] name.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedDeclaration = new(
        id: "NWF005",
        title: "Form model declaration is not supported",
        messageFormat: "'{0}' cannot carry a generated binder. A form model must be a non-generic, non-nested, non-static reference type, because IBindableFromHttpContext<TSelf> is constrained to reference types and the binder is emitted as a partial of the type itself.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MissingAntiforgery = new(
        id: "NWF010",
        title: "Form model bound by a plain route",
        messageFormat: "The route '{0}' takes the form model '{1}' as a handler parameter. Bind it with MapFormPost (or MapFormPut/Patch/Delete) instead: a plain {2} binds the model as a JSON body and carries no antiforgery metadata.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two measured failures, both silent. (1) The Request Delegate Generator runs in the same pass as this one and cannot see the generated binder, so it emits hasBindAsync:false and treats the model as a JSON body -- a form POST is then answered 415 by routing itself, before any handler runs (2026-08-30). (2) A route whose model binds itself carries no antiforgery metadata, so UseAntiforgery() skips the endpoint and a POST with no token at all is answered 200 OK (brainstorm 001 §12). MapFormPost closes both by construction.");
}
