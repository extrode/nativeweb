using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace NativeWeb.Forms.Generator;

// What a bound member is filled from. Text is every member the library had before
// spec 027; the two file kinds are that spec.
internal enum FormFieldKind
{
    Text,
    OptionalFile,
    RequiredFile,
}

// One field of a declared form: the constructor parameter the emitted binder
// fills, and the wire name it reads from.
internal sealed class FormFieldModel : IEquatable<FormFieldModel>
{
    public string ParameterName { get; set; } = "";

    public string WireName { get; set; } = "";

    public FormFieldKind Kind { get; set; } = FormFieldKind.Text;

    // Zero for a text field, and never zero for a file field: NWF006 rejects a file
    // declaration that does not state a ceiling, so the emitter can rely on it.
    public long MaxBytes { get; set; }

    public ImmutableArray<string> Accept { get; set; } = ImmutableArray<string>.Empty;

    public bool Equals(FormFieldModel? other) =>
        other is not null
        && ParameterName == other.ParameterName
        && WireName == other.WireName
        && Kind == other.Kind
        && MaxBytes == other.MaxBytes
        && Accept.SequenceEqual(other.Accept, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as FormFieldModel);

    public override int GetHashCode()
    {
        int hash = unchecked((ParameterName.GetHashCode() * 397) ^ WireName.GetHashCode());
        hash = unchecked((hash * 397) ^ (int)Kind);
        hash = unchecked((hash * 397) ^ MaxBytes.GetHashCode());

        foreach (string accepted in Accept)
        {
            hash = unchecked((hash * 397) ^ accepted.GetHashCode());
        }

        return hash;
    }
}

// A [Form]-decorated type, parsed into the shape the emitter needs. Built only
// when the declaration is usable; every rejection is a diagnostic instead, which
// is R8 -- a malformed model must not produce code that fails to compile with a
// message pointing at generated source the author never wrote.
internal sealed class FormModel : IEquatable<FormModel>
{
    public string Namespace { get; set; } = "";

    public string TypeName { get; set; } = "";

    public string TypeKeyword { get; set; } = "record";

    public List<FormFieldModel> Fields { get; } = new();

    // Structural, and it is what makes the incremental pipeline's cache work. A
    // reference-equality model compares unequal on every keystroke, so
    // RegisterSourceOutput re-runs and re-emits for an edit that changed nothing
    // about the form -- the generator would still be correct and would still be
    // doing the work of a non-incremental one.
    public bool Equals(FormModel? other)
    {
        if (other is null
            || Namespace != other.Namespace
            || TypeName != other.TypeName
            || TypeKeyword != other.TypeKeyword
            || Fields.Count != other.Fields.Count)
        {
            return false;
        }

        for (int i = 0; i < Fields.Count; i++)
        {
            if (!Fields[i].Equals(other.Fields[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as FormModel);

    public override int GetHashCode()
    {
        int hash = Namespace.GetHashCode();
        hash = unchecked((hash * 397) ^ TypeName.GetHashCode());
        hash = unchecked((hash * 397) ^ TypeKeyword.GetHashCode());

        foreach (FormFieldModel field in Fields)
        {
            hash = unchecked((hash * 397) ^ field.GetHashCode());
        }

        return hash;
    }

    // Returns null and reports, or returns a model and reports nothing. The
    // caller emits for exactly the non-null results.
    public static FormModel? Build(INamedTypeSymbol type, Location location, List<Diagnostic> diagnostics)
    {
        if (type.TypeKind != TypeKind.Class || type.IsStatic)
        {
            // IFormModel<TSelf> is constrained to reference types, so a struct cannot
            // implement it at all. [Form]'s AttributeUsage already
            // blocks the obvious cases; this catches what it cannot.
            diagnostics.Add(Diagnostic.Create(FormDiagnostics.UnsupportedDeclaration, location, type.Name));
            return null;
        }

        if (type.ContainingType is not null || type.IsGenericType || type.IsFileLocal)
        {
            // A nested type would need every containing declaration emitted as
            // partial too, and a generic one would need the binder instantiated per
            // construction. A file-local one cannot be extended from another file at
            // all, and the generated partial is always another file. All three are
            // diagnosable now and the first two supportable later; emitting code that
            // does not compile is neither.
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
        // Case-insensitively, because that is how the lookup behaves: IFormCollection
        // is keyed with OrdinalIgnoreCase, so two members whose wire names differ only
        // in case read the same posted field and the second silently shadows nothing.
        // Measured 2026-08-30 on /auth/register: a field posted as ConfirmPassphrase
        // bound a member whose wire name is confirmPassphrase.
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (IParameterSymbol parameter in candidates[0].Parameters)
        {
            Location where = parameter.Locations.FirstOrDefault() ?? location;

            FormFieldKind kind = FileKind(parameter.Type);
            AttributeData? fileAttribute = FileAttribute(parameter);

            if (kind == FormFieldKind.Text)
            {
                if (fileAttribute is not null)
                {
                    // [FormFile] says "bind this from the uploaded parts", and a
                    // `string?` cannot hold one. Silently ignoring the attribute would
                    // leave an author believing a limit is enforced that is not.
                    diagnostics.Add(Diagnostic.Create(
                        FormDiagnostics.UnsupportedFileDeclaration,
                        where,
                        parameter.Name,
                        "it is declared '" + parameter.Type.ToDisplayString()
                            + "'. Declare it 'FormFile' or 'FormFile?', or drop the attribute"));
                    usable = false;
                    continue;
                }

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
            }

            long maxBytes = 0;
            ImmutableArray<string> accept = ImmutableArray<string>.Empty;

            if (kind != FormFieldKind.Text)
            {
                if (fileAttribute is null)
                {
                    // A public upload with no declared ceiling is a way to fill a
                    // disk, so the ceiling is required rather than defaulted: a
                    // default here would be a number nobody chose, applied to every
                    // upload endpoint in every consuming application.
                    diagnostics.Add(Diagnostic.Create(
                        FormDiagnostics.UnsupportedFileDeclaration,
                        where,
                        parameter.Name,
                        "it carries no [FormFile]. Add [FormFile(MaxBytes = ...)] to state the largest body this endpoint accepts"));
                    usable = false;
                    continue;
                }

                maxBytes = FileMaxBytes(fileAttribute);

                if (maxBytes <= 0)
                {
                    diagnostics.Add(Diagnostic.Create(
                        FormDiagnostics.UnsupportedFileDeclaration,
                        where,
                        parameter.Name,
                        "its MaxBytes is " + maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + ". State a positive number of bytes"));
                    usable = false;
                    continue;
                }

                accept = FileAccept(fileAttribute);

                if (accept.Any(a => a.Trim().Length == 0))
                {
                    // An empty entry matches the request that declares no content
                    // type at all, so a set carrying one accepts what the author
                    // wrote the set to refuse.
                    diagnostics.Add(Diagnostic.Create(
                        FormDiagnostics.UnsupportedFileDeclaration,
                        where,
                        parameter.Name,
                        "its Accept list has an empty entry, which would accept a file declaring no content type. Remove it"));
                    usable = false;
                    continue;
                }
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
            model.Fields.Add(new FormFieldModel
            {
                ParameterName = parameter.Name,
                WireName = wire,
                Kind = kind,
                MaxBytes = maxBytes,
                Accept = accept,
            });
        }

        return usable ? model : null;
    }

    // FormFile is a reference type, so "required" is the annotation: `FormFile` is a
    // field the model says must be there, `FormFile?` one it says may be absent. That
    // is the same reading NWF003 gives `string?`, and it means a file field needs no
    // second attribute to say which it is.
    private static FormFieldKind FileKind(ITypeSymbol type)
    {
        if (type.ToDisplayString(NullableFree) != FormGenerator.FormFileType)
        {
            return FormFieldKind.Text;
        }

        return type.NullableAnnotation == NullableAnnotation.Annotated
            ? FormFieldKind.OptionalFile
            : FormFieldKind.RequiredFile;
    }

    private static readonly SymbolDisplayFormat NullableFree =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.None);

    // Both attribute targets, for the reason WireName reads both: on a record's
    // primary constructor `[property: FormFile(...)]` is legal and reads naturally.
    private static AttributeData? FileAttribute(IParameterSymbol parameter)
    {
        AttributeData? found = Find(parameter.GetAttributes());
        if (found is not null)
        {
            return found;
        }

        foreach (ISymbol member in parameter.ContainingType.GetMembers(parameter.Name))
        {
            if (member is IPropertySymbol property)
            {
                found = Find(property.GetAttributes());
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;

        static AttributeData? Find(ImmutableArray<AttributeData> attributes)
        {
            foreach (AttributeData attribute in attributes)
            {
                if (attribute.AttributeClass?.ToDisplayString() == FormGenerator.FormFileAttribute)
                {
                    return attribute;
                }
            }

            return null;
        }
    }

    private static long FileMaxBytes(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key == "MaxBytes" && named.Value.Value is long declared)
            {
                return declared;
            }
        }

        return 0;
    }

    private static ImmutableArray<string> FileAccept(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key != "Accept" || named.Value.Kind != TypedConstantKind.Array)
            {
                continue;
            }

            ImmutableArray<string>.Builder accepted = ImmutableArray.CreateBuilder<string>();
            foreach (TypedConstant element in named.Value.Values)
            {
                accepted.Add(element.Value as string ?? "");
            }

            return accepted.ToImmutable();
        }

        return ImmutableArray<string>.Empty;
    }

    // [FormField("...")] wins; otherwise the parameter name with its first letter
    // lowercased, which is what the hand-written endpoints already post.
    //
    // Both attribute targets are read. On a record's primary constructor the
    // attribute lands on the parameter by default, but `[property: FormField("x")]`
    // is legal, reads naturally, and is what 014-plan.md's own example wrote --
    // ignoring it would rename nothing and say nothing, which is the whole class of
    // failure this feature exists to remove.
    private static string WireName(IParameterSymbol parameter)
    {
        string? given = FormFieldName(parameter.GetAttributes());
        if (given is not null)
        {
            return given;
        }

        foreach (ISymbol member in parameter.ContainingType.GetMembers(parameter.Name))
        {
            if (member is IPropertySymbol property)
            {
                given = FormFieldName(property.GetAttributes());
                if (given is not null)
                {
                    return given;
                }
            }
        }

        string name = parameter.Name;
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string? FormFieldName(ImmutableArray<AttributeData> attributes)
    {
        foreach (AttributeData attribute in attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() == FormGenerator.FormFieldAttribute
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string given
                && given.Length > 0)
            {
                return given;
            }
        }

        return null;
    }

    // Annotated, not merely "not NotAnnotated". None is what a member declared in a
    // nullable-disabled file reports, and binding null into a member the author wrote
    // as a plain `string` is the silent version of the failure NWF003 exists to make
    // loud.
    private static bool IsNullableString(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_String
        && type.NullableAnnotation == NullableAnnotation.Annotated;

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
        messageFormat: "The form field '{0}' is bound by both '{1}' and '{2}'. Give one of them a distinct [FormField] name -- distinct in more than case, because the form collection is keyed case-insensitively.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedDeclaration = new(
        id: "NWF005",
        title: "Form model declaration is not supported",
        messageFormat: "'{0}' cannot carry a generated binder. A form model must be a non-generic, non-nested, non-file-local, non-static reference type, because IFormModel<TSelf> is constrained to reference types and the binder is emitted as a partial of the type itself, in another file.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedFileDeclaration = new(
        id: "NWF006",
        title: "File form field cannot be bound as declared",
        messageFormat: "The form field '{0}' cannot be bound as a file: {1}",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A file field is bound under a declared ceiling and an optional content-type set, and the emitted binder installs the ceiling on the request before the form is read. A declaration the emitter cannot honour has to fail the build: the alternatives are a public upload endpoint with no size limit, or an Accept set that silently admits what it was written to refuse.");

    internal static readonly DiagnosticDescriptor MissingAntiforgery = new(
        id: "NWF010",
        title: "Form model bound by a plain route",
        messageFormat: "The route '{0}' takes the form model '{1}' as a handler parameter. Bind it with MapFormPost (or MapFormPut/Patch/Delete) instead: a plain {2} binds the model as a JSON body and carries no antiforgery metadata.",
        category: "NativeWeb.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two measured failures, both silent. (1) The Request Delegate Generator runs in the same pass as this one and cannot see the generated binder, so it emits hasBindAsync:false and treats the model as a JSON body -- a form POST is then answered 415 by routing itself, before any handler runs (2026-08-30). (2) A route whose model binds itself carries no antiforgery metadata, so UseAntiforgery() skips the endpoint and a POST with no token at all is answered 200 OK (brainstorm 001 §12). MapFormPost closes both by construction.");
}
