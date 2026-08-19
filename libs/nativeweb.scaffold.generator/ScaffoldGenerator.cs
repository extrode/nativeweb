using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NativeWeb.Scaffold.Generator;

// Turns a [Scaffold]-decorated model into a working CRUD route group.
//
// Why a generator and not reflection: NativeAOT bans the reflection every
// scaffolding framework in every other ecosystem is built on -- no
// Activator.CreateInstance, no PropertyInfo.SetValue, no runtime model discovery.
// The measured consequence (brainstorm 001 sec.12) is that the platform's own
// declarative form binding is closed too: complex-type [FromForm] does not
// compile under AOT, and [AsParameters] binds every form field to null in
// silence. What does work is a type that binds itself through a compile-time
// static call, so that is what this emits.
[Generator]
public sealed class ScaffoldGenerator : IIncrementalGenerator
{
    private const string ScaffoldAttribute = "NativeWeb.Scaffold.ScaffoldAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ScaffoldModel?> models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ScaffoldAttribute,
                predicate: static (node, _) => true,
                transform: static (ctx, _) => Build(ctx));

        context.RegisterSourceOutput(
            models.Where(static m => m is not null).Collect(),
            static (spc, all) => Emit(spc, all!));
    }

    private static ScaffoldModel? Build(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        AttributeData scaffold = ctx.Attributes[0];
        var model = new ScaffoldModel
        {
            Namespace = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            TypeName = type.Name,
        };

        model.Display = NamedArg(scaffold, "Display") ?? Pluralize(SpaceCamelCase(type.Name));
        model.Singular = SpaceCamelCase(type.Name);
        model.Route = NamedArg(scaffold, "Route") ?? Pluralize(type.Name.ToLowerInvariant());

        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "NativeWeb.Scaffold.AuthAttribute")
            {
                model.Auth = new AuthModel
                {
                    Role = NamedArg(attribute, "Role"),
                    Policy = NamedArg(attribute, "Policy"),
                    Read = NamedArg(attribute, "Read"),
                    Write = NamedArg(attribute, "Write"),
                    Delete = NamedArg(attribute, "Delete"),
                };
            }
        }

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IPropertySymbol property || property.DeclaredAccessibility != Accessibility.Public || property.IsStatic)
            {
                continue;
            }

            var pm = new PropertyModel
            {
                Name = property.Name,
                TypeName = property.Type.ToDisplayString().TrimEnd('?'),
                IsNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated
                    || property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T,
            };

            pm.Label = SpaceCamelCase(property.Name);

            foreach (AttributeData attribute in property.GetAttributes())
            {
                switch (attribute.AttributeClass?.ToDisplayString())
                {
                    case "NativeWeb.Scaffold.ScaffoldListAttribute":
                        pm.InList = true;
                        pm.Label = NamedArg(attribute, "Label") ?? pm.Label;
                        break;
                    case "NativeWeb.Scaffold.ScaffoldSearchAttribute":
                        pm.Searchable = true;
                        break;
                    case "NativeWeb.Scaffold.ScaffoldMultilineAttribute":
                        pm.Multiline = true;
                        break;
                    case "NativeWeb.Scaffold.ScaffoldReadOnlyAttribute":
                        pm.ReadOnly = true;
                        break;
                    case "NativeWeb.Scaffold.ScaffoldKeyAttribute":
                        pm.IsKey = true;
                        break;
                    default:
                        break;
                }
            }

            // A key is never writable through a form. Emitting an editable input for
            // it would be a mass-assignment hole that looks like a feature.
            if (pm.IsKey)
            {
                pm.ReadOnly = true;
                model.KeyProperty = pm.Name;
            }

            model.Properties.Add(pm);
        }

        return model;
    }

    private static string? NamedArg(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> arg in attribute.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is string s && s.Length > 0)
            {
                return s;
            }
        }

        return null;
    }

    private static string SpaceCamelCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
            }

            sb.Append(i == 0 ? char.ToUpperInvariant(name[i]) : name[i]);
        }

        return sb.ToString();
    }

    // Enough pluralisation for a heading, and no more. Anything cleverer is a
    // guess the consumer can override with [Scaffold(Display = "...")].
    private static string Pluralize(string word)
    {
        if (word.EndsWith("y", System.StringComparison.Ordinal) && word.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(word[word.Length - 2])))
        {
            return word.Substring(0, word.Length - 1) + "ies";
        }

        if (word.EndsWith("s", System.StringComparison.Ordinal)
            || word.EndsWith("x", System.StringComparison.Ordinal)
            || word.EndsWith("ch", System.StringComparison.Ordinal)
            || word.EndsWith("sh", System.StringComparison.Ordinal))
        {
            return word + "es";
        }

        return word + "s";
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<ScaffoldModel> models)
    {
        if (models.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (ScaffoldModel model in models)
        {
            if (model.KeyProperty.Length == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MissingKey, Location.None, model.TypeName));
                continue;
            }

            spc.AddSource(
                model.TypeName + ".Scaffold.g.cs",
                SourceText.From(ScaffoldEmitter.Emit(model), Encoding.UTF8));
        }

        spc.AddSource(
            "ScaffoldRuntime.g.cs",
            SourceText.From(ScaffoldEmitter.EmitRuntime(models.Where(m => m.KeyProperty.Length > 0).ToList()), Encoding.UTF8));
    }
}

internal static class Diagnostics
{
    internal static readonly DiagnosticDescriptor MissingKey = new(
        id: "NWS001",
        title: "Scaffolded model has no key",
        messageFormat: "'{0}' is decorated [Scaffold] but no property carries [ScaffoldKey]. Add it to the property that identifies a row.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
