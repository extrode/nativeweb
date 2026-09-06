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
    private const string ScaffoldRootAttribute = "NativeWeb.Scaffold.ScaffoldRootAttribute";
    private const string PermissionsAttribute = "NativeWeb.Permissions.PermissionsAttribute";
    internal const string DefaultRoot = "/admin";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ScaffoldModel?> models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ScaffoldAttribute,
                predicate: static (node, _) => true,
                transform: static (ctx, _) => Build(ctx));

        // The assembly-level [ScaffoldRoot]. Projected to a string so the pipeline
        // caches on the value: keeping the Compilation itself in the provider would
        // invalidate every downstream step on any edit at all.
        IncrementalValueProvider<string?> root = context.CompilationProvider
            .Select(static (compilation, _) => ReadRoot(compilation));

        context.RegisterSourceOutput(
            models.Where(static m => m is not null).Collect().Combine(root),
            static (spc, pair) => Emit(spc, pair.Left!, pair.Right));
    }

    // The raw attribute value, or null when the assembly does not carry one.
    // Validation happens at emit time, where a diagnostic can be reported.
    private static string? ReadRoot(Compilation compilation)
    {
        foreach (AttributeData attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ScaffoldRootAttribute
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string path)
            {
                return path;
            }
        }

        return null;
    }

    // "/manage/" -> "/manage"; "/" -> ""; absent -> "/admin". An empty result is
    // valid and means the site root, so the caller cannot treat "" as failure --
    // hence the separate bool.
    internal static bool TryNormalizeRoot(string? raw, out string normalized)
    {
        normalized = DefaultRoot;

        if (raw is null)
        {
            return true;
        }

        string trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            return false;
        }

        if (trimmed.IndexOf('?') >= 0 || trimmed.IndexOf('#') >= 0 || trimmed.IndexOf(' ') >= 0)
        {
            return false;
        }

        normalized = trimmed.TrimEnd('/');
        return true;
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

        model.Unsupported = UnsupportedReason(type);
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
                    PublicRead = NamedFlag(attribute, "PublicRead"),
                };
            }

            // By metadata name, with no reference to the permissions library. A
            // consumer that scaffolds and declares no permissions has no such type
            // in its compilation, so this is never true and the emitted output is
            // what it was before spec 024 -- which is R15, held by construction
            // rather than by a comparison somebody has to remember to run.
            if (attribute.AttributeClass?.ToDisplayString() == PermissionsAttribute)
            {
                model.HasPermissions = true;
            }
        }

        foreach (List<IPropertySymbol> group in PropertyGroups(type))
        {
            IPropertySymbol property = group[0];
            // An indexer has no name a form field could carry -- property.Name is
            // "this[]" -- and a property with no setter at all cannot appear on the
            // left of the emitted object initializer. Both used to reach the emitter:
            // a model with `public string Summary => Title + "..."` got
            // `Summary = ...` emitted against it, CS0200 in the consumer's build.
            if (property.IsIndexer || property.SetMethod is null)
            {
                continue;
            }

            // An accessor the generated file cannot reach is worse than no accessor
            // at all, and used to be skipped in the same silence: the emitted Build
            // ends in `new() { ... }`, so a property missing from that initializer is
            // left at its default and the store writes the default back over the
            // stored value on every save. { get; private set; } lost its column's
            // contents on each edit with nothing reported anywhere.
            //
            // The getter is asked the same question, because the emitter reads
            // `existing.Name` and `row.Name` as well as assigning: `{ private get;
            // set; }` and a set-only property are CS0271 and CS0154 inside generated
            // source, which names the generated file and not the declaration.
            if (property.GetMethod is null || !CanReach(property.GetMethod) || !CanReach(property.SetMethod))
            {
                model.Unreachable.Add(property.Name);
                continue;
            }

            var pm = new PropertyModel
            {
                Name = property.Name,
                TypeName = property.Type.ToDisplayString().TrimEnd('?'),
                IsNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated
                    || property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T,
            };

            // Status? is Nullable<Status>; the enum is the type argument.
            ITypeSymbol bare = property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && property.Type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable
                ? nullable.TypeArguments[0]
                : property.Type;
            if (bare.TypeKind == TypeKind.Enum)
            {
                pm.IsEnum = true;
                pm.TypeName = bare.ToDisplayString();
                foreach (ISymbol enumMember in bare.GetMembers())
                {
                    if (enumMember is IFieldSymbol { HasConstantValue: true } field)
                    {
                        pm.EnumMembers.Add(field.Name);
                    }
                }
            }

            pm.Label = SpaceCamelCase(property.Name);

            // internal and protected internal are reachable from the generated file
            // -- it is emitted into the same assembly -- so the value is carried from
            // `existing` rather than lost. Never bound from the form, though: what the
            // author kept off the public surface is not what a posted field reaches.
            //
            // The setter alone answers for the property as well. C# permits an
            // accessor modifier only where it is stricter than the declaration, so an
            // internal property cannot have a public setter and asking both was a
            // condition no input could tell apart -- which is how the mutation that
            // dropped half of it stayed green.
            pm.ReadOnly = property.SetMethod.DeclaredAccessibility != Accessibility.Public;

            // Base declarations first, because this loop is last-writer-wins for the
            // values it reads -- [ScaffoldList(Label = ...)] restated on an override
            // has to be the one that lands, and reading most-derived first made the
            // hidden base label overwrite it.
            foreach (AttributeData attribute in Enumerable.Reverse(group).SelectMany(static declaration => declaration.GetAttributes()))
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
                    case "NativeWeb.Scaffold.ScaffoldSortAttribute":
                        pm.Sortable = true;
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
                    case "NativeWeb.Scaffold.ScaffoldReferenceAttribute":
                        pm.Reference = TargetOf(attribute);
                        break;
                    case "NativeWeb.Scaffold.ScaffoldRelationAttribute":
                        pm.Relation = TargetOf(attribute);
                        pm.MultiSelect = attribute.NamedArguments.Any(a => a.Key == "Control" && a.Value.Value is int control && control == 1);
                        break;
                    default:
                        break;
                }
            }

            // A type with no form conversion is displayed and carried through, never
            // assigned from the form. ScaffoldEmitter.Convert is the one table that
            // decides, so a type can never be editable here and unconvertible there.
            // After the attribute loop, not before it: a relation's list type converts
            // only because the attribute says what the list holds.
            pm.ReadOnly = pm.ReadOnly || ScaffoldEmitter.Convert(pm) is null;

            // A key is never writable through a form. Emitting an editable input for
            // it would be a mass-assignment hole that looks like a feature.
            if (pm.IsKey)
            {
                pm.ReadOnly = true;
                model.KeyProperties.Add(pm.Name);

                // The key is the default order and the tiebreaker under every other
                // one, so a list can always be asked for it whether or not the model
                // marked it. Setting it here rather than special-casing the emitter
                // keeps one answer to "is this name sortable" for both the allow-list
                // the store receives and the headers that are rendered as links.
                pm.Sortable = true;
            }

            model.Properties.Add(pm);
        }

        return model;
    }

    // Every public instance property the model exposes, inherited ones included, as
    // one group per name: the most-derived declaration first, then every base
    // declaration it overrides or hides. Groups come out in base-declaration order.
    //
    // type.GetMembers() returns only what the type itself declares, which made a
    // base class invisible to the whole generator: no column, no input, and --
    // because the emitter builds an object initializer over exactly this list --
    // no assignment either, so every save through the scaffold reset an inherited
    // property to its default. Nothing reported it; the row just changed.
    //
    // A group rather than a winner, because the scaffold attributes are all
    // Inherited = false and Roslyn does not copy them onto an override. Reading the
    // winner alone would drop them: a base [ScaffoldReadOnly] property overridden
    // without restating the attribute would become editable, which is the
    // mass-assignment hole the attribute exists to close, arrived at silently.
    //
    // A group sits where its base declaration sits, not where the override does.
    // The order is read by the emitter -- the first four properties are the default
    // columns when nothing carries [ScaffoldList], and the first listed string
    // labels a reference dropdown -- so overriding a property must not move it.
    private static List<List<IPropertySymbol>> PropertyGroups(INamedTypeSymbol type)
    {
        var levels = new List<List<IPropertySymbol>>();
        for (INamedTypeSymbol? current = type;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            var level = new List<IPropertySymbol>();
            foreach (ISymbol member in current.GetMembers())
            {
                if (member is IPropertySymbol property
                    && CanReach(property)
                    && !property.IsStatic)
                {
                    level.Add(property);
                }
            }

            levels.Add(level);
        }

        var byName = new Dictionary<string, List<IPropertySymbol>>(System.StringComparer.Ordinal);
        foreach (List<IPropertySymbol> level in levels)
        {
            foreach (IPropertySymbol property in level)
            {
                if (!byName.TryGetValue(property.Name, out List<IPropertySymbol>? group))
                {
                    group = [];
                    byName[property.Name] = group;
                }

                group.Add(property);
            }
        }

        levels.Reverse();
        var emitted = new HashSet<string>(System.StringComparer.Ordinal);
        var groups = new List<List<IPropertySymbol>>();
        foreach (List<IPropertySymbol> level in levels)
        {
            foreach (IPropertySymbol property in level)
            {
                if (emitted.Add(property.Name))
                {
                    groups.Add(byName[property.Name]);
                }
            }
        }

        return groups;
    }

    // Why the model cannot be emitted as declared, or null.
    //
    // Everything the emitter writes names the model as Namespace.TypeName and
    // builds a row with `new()`, so a declaration neither of those can express is
    // refused here rather than emitted as source that does not compile. A generic
    // type loses its type arguments in that name and a nested one loses its
    // containing declaration -- CS0305 and CS0246 -- and a type with no reachable
    // parameterless constructor is CS7036 at `new()`, which is what [Scaffold] on
    // a positional record used to produce. All of them named the generated file
    // and none of them named the cause.
    private static string? UnsupportedReason(INamedTypeSymbol type)
    {
        if (type.IsGenericType)
        {
            return "it is generic, and the emitted routes name the model without its type arguments";
        }

        if (type.ContainingType is not null)
        {
            return "it is nested, and the emitted routes name the model by its namespace and short name alone";
        }

        if (type.IsFileLocal)
        {
            return "it is file-local, and the generated routes are emitted from another file";
        }

        if (type.IsStatic || type.IsAbstract)
        {
            return "it is static or abstract, and a row is built with `new()`";
        }

        if (type.TypeKind != TypeKind.Class)
        {
            return "it is not a class, and the emitted routes hold a row in a `Model?` that a value type reads as Nullable<T>";
        }

        if (!type.InstanceConstructors.Any(static c => c.Parameters.Length == 0 && CanReach(c)))
        {
            return "it has no parameterless constructor the generated file can reach, and a row is built with `new()`. A positional record is the usual way to arrive here";
        }

        return null;
    }

    // Whether the generated file, emitted into the consumer's own assembly, can
    // reach this symbol at all. private, protected and private protected cannot be
    // reached from an unrelated class in the same assembly; the rest can.
    //
    // Asked of a property, an accessor and a constructor alike, because the answer
    // is the same question in all three places and getting it different in one of
    // them is how `protected internal` came to be assignable and not constructible.
    private static bool CanReach(ISymbol symbol) =>
        symbol.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Internal
            or Accessibility.ProtectedOrInternal;

    // The typeof(...) argument of [ScaffoldReference] or [ScaffoldRelation], as the
    // same fully qualified name ScaffoldModel.FullyQualifiedName builds, so the two
    // can be matched at emit time. Null when the argument is not a type at all.
    private static string? TargetOf(AttributeData attribute) =>
        attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol target
            ? target.ToDisplayString()
            : null;

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

    private static bool NamedFlag(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> arg in attribute.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is bool flag)
            {
                return flag;
            }
        }

        return false;
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

    private static void Emit(SourceProductionContext spc, ImmutableArray<ScaffoldModel> models, string? rawRoot)
    {
        if (models.IsDefaultOrEmpty)
        {
            return;
        }

        if (!TryNormalizeRoot(rawRoot, out string root))
        {
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.BadRoot, Location.None, rawRoot));
            return;
        }

        // Everything is emitted into one namespace as TypeName + "Scaffold", so two
        // [Scaffold] types sharing a short name across namespaces collide twice
        // over: on the hint name, which throws inside the driver, and on the
        // emitted class name, which is CS0101 in the consumer's build. Neither
        // failure names the cause.
        var byShortName = new Dictionary<string, List<string>>();
        foreach (ScaffoldModel model in models)
        {
            if (!byShortName.TryGetValue(model.TypeName, out List<string>? sharing))
            {
                sharing = [];
                byShortName[model.TypeName] = sharing;
            }

            sharing.Add(model.FullyQualifiedName);
        }

        // Everything a model can be refused for on its own, decided before anything
        // is resolved against it. A refused model is not a reference target either:
        // the referrer would otherwise pass ChoicesResolve and emit a control
        // reading TargetScaffold.SortFields against a class that was never
        // generated -- CS0103 beside the one diagnostic that names the real fault.
        var reportedCollision = new HashSet<string>();
        var candidates = new List<ScaffoldModel>();
        foreach (ScaffoldModel model in models)
        {
            List<string> sharing = byShortName[model.TypeName];
            if (sharing.Count > 1)
            {
                if (reportedCollision.Add(model.TypeName))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DuplicateTypeName,
                        Location.None,
                        model.TypeName,
                        string.Join(", ", sharing.OrderBy(static n => n, System.StringComparer.Ordinal))));
                }

                continue;
            }

            if (model.Unsupported is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UnsupportedDeclaration, Location.None, model.TypeName, model.Unsupported));
                continue;
            }

            if (model.Unreachable.Count > 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UnreachableProperty,
                    Location.None,
                    model.TypeName,
                    string.Join(", ", model.Unreachable)));
                continue;
            }

            if (model.KeyProperties.Count == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MissingKey, Location.None, model.TypeName));
                continue;
            }

            if (model.KeyProperties.Count > 1)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DuplicateKey,
                    Location.None,
                    model.TypeName,
                    string.Join(", ", model.KeyProperties)));
                continue;
            }

            candidates.Add(model);
        }

        // Refusing a model can refuse the one that refers to it, and that one can
        // refuse a third, so this runs to a fixpoint rather than once. Silently:
        // the diagnostics come afterwards, against the set that survived, so a
        // model is reported once and a cascaded refusal is reported against what
        // the compilation really ends up emitting rather than against a lookup
        // still holding names that were about to be dropped.
        var survivors = new List<ScaffoldModel>(candidates);
        var byName = new Dictionary<string, ScaffoldModel>();
        foreach (ScaffoldModel model in survivors)
        {
            byName[model.FullyQualifiedName] = model;
        }

        for (bool dropped = true; dropped;)
        {
            dropped = false;
            foreach (ScaffoldModel model in survivors.ToList())
            {
                if (!ChoicesResolve(spc, model, byName, report: false))
                {
                    survivors.Remove(model);
                    byName.Remove(model.FullyQualifiedName);
                    dropped = true;
                }
            }
        }

        foreach (ScaffoldModel model in candidates)
        {
            if (!byName.ContainsKey(model.FullyQualifiedName))
            {
                ChoicesResolve(spc, model, byName, report: true);
            }
        }

        var emitted = new List<ScaffoldModel>();
        foreach (ScaffoldModel model in survivors)
        {
            spc.AddSource(
                model.TypeName + ".Scaffold.g.cs",
                SourceText.From(ScaffoldEmitter.Emit(model, root, byName), Encoding.UTF8));
            emitted.Add(model);
        }

        // Only the classes that exist get mapped: a model refused above would
        // otherwise surface as a CS0103 cascade beside the one diagnostic that
        // names the real fault.
        spc.AddSource(
            "ScaffoldRuntime.g.cs",
            SourceText.From(ScaffoldEmitter.EmitRuntime(emitted, root), Encoding.UTF8));
    }

    // Every [ScaffoldReference] and [ScaffoldRelation] on the model names a target
    // this compilation scaffolds, keyed by a string, on a property of the one type
    // the control binds to. Reported here rather than in Build because the target
    // is another model, and only the collected set can say whether it exists.
    private static bool ChoicesResolve(
        SourceProductionContext spc,
        ScaffoldModel model,
        Dictionary<string, ScaffoldModel> byName,
        bool report)
    {
        bool ok = true;
        foreach (PropertyModel p in model.Properties)
        {
            string? targetName = p.Reference ?? p.Relation;
            if (targetName is null)
            {
                continue;
            }

            string where = model.TypeName + "." + p.Name;
            if (!byName.TryGetValue(targetName, out ScaffoldModel? target) || target.KeyProperty.Length == 0)
            {
                Report(spc, report, Diagnostics.TargetNotScaffolded, where, targetName);
                ok = false;
                continue;
            }

            if (target.Properties.First(t => t.Name == target.KeyProperty).TypeName != "string")
            {
                Report(spc, report, Diagnostics.TargetKeyNotString, where, targetName);
                ok = false;
            }

            if (p.Reference is not null && !(p.TypeName == "string" && p.IsNullable))
            {
                Report(spc, report, Diagnostics.ReferenceNotNullableString, where, p.TypeName);
                ok = false;
            }

            if (p.Relation is not null && p.TypeName != "System.Collections.Generic.IReadOnlyList<string>")
            {
                Report(spc, report, Diagnostics.RelationNotKeyList, where, p.TypeName);
                ok = false;
            }
        }

        return ok;
    }

    private static void Report(SourceProductionContext spc, bool report, DiagnosticDescriptor descriptor, string where, string what)
    {
        if (report)
        {
            spc.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, where, what));
        }
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

    internal static readonly DiagnosticDescriptor BadRoot = new(
        id: "NWS002",
        title: "Scaffold root is not a usable path",
        messageFormat: "[assembly: ScaffoldRoot(\"{0}\")] is not a usable prefix. It must begin with '/' and contain no query, fragment or space.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TargetNotScaffolded = new(
        id: "NWS003",
        title: "Reference or relation target is not scaffolded",
        messageFormat: "'{0}' refers to '{1}', which this assembly does not emit a scaffold for -- it is not a [Scaffold] model, or it carries no [ScaffoldKey], or it carries more than one, or its short name collides with another model's. The choices are read through the target's own IScaffoldStore, so the target has to be scaffolded too.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ReferenceNotNullableString = new(
        id: "NWS004",
        title: "[ScaffoldReference] needs a string? property",
        messageFormat: "'{0}' carries [ScaffoldReference] but is '{1}'. The property holds the target's key or nothing, so it must be string?.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RelationNotKeyList = new(
        id: "NWS005",
        title: "[ScaffoldRelation] needs an IReadOnlyList<string> property",
        messageFormat: "'{0}' carries [ScaffoldRelation] but is '{1}'. The property holds the target's keys, so it must be IReadOnlyList<string>.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateKey = new(
        id: "NWS007",
        title: "Scaffolded model has more than one key",
        messageFormat: "'{0}' carries [ScaffoldKey] on more than one property ({1}). Exactly one property identifies a row, and every route the scaffold emits is built from it.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateTypeName = new(
        id: "NWS008",
        title: "Two scaffolded models share a short name",
        messageFormat: "'{0}' is the short name of more than one [Scaffold] model ({1}). The generated classes share one namespace, so the short names have to differ; rename one of the types.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedDeclaration = new(
        id: "NWS009",
        title: "Scaffolded model cannot be emitted as declared",
        messageFormat: "'{0}' is decorated [Scaffold] but cannot be scaffolded: {1}. Declare it as a top-level, non-generic class with a parameterless constructor.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnreachableProperty = new(
        id: "NWS010",
        title: "Scaffolded model has a property the generated code cannot reach",
        messageFormat: "'{0}' has a settable property the generated code cannot read or assign ({1}). Every row is rebuilt with `new()` on save, so a property missing from that initializer is written back as its default and whatever was stored is lost. Make both accessors public or internal; where the declaration is not yours to change -- a base type in another assembly -- scaffold a type of your own instead.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TargetKeyNotString = new(
        id: "NWS006",
        title: "Reference or relation target is not keyed by a string",
        messageFormat: "'{0}' refers to '{1}', whose [ScaffoldKey] is not a string. A choice is posted as the target's key, and the property that holds it is a string.",
        category: "NativeWeb.Scaffold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
