using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace NativeWeb.Permissions.Generator;

// Turns a [Permissions]-decorated model into a static class of Permission
// constants, and the assembly into one catalog of everything it declares.
//
// Two things follow from the constants being compile-time. A permission that stops
// being declared takes its constant with it, so every use site fails to compile
// rather than silently gating on a name nobody can be granted -- that is the half
// of R14 the compiler does. And a name is never typed by hand at a call site, so a
// typo is a build error rather than a permission that is checked, never granted,
// and therefore always refused.
[Generator]
public sealed class PermissionsGenerator : IIncrementalGenerator
{
    private const string PermissionsAttribute = "NativeWeb.Permissions.PermissionsAttribute";
    private const string PermissionScopeAttribute = "NativeWeb.Permissions.PermissionScopeAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<PermissionModel?> models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PermissionsAttribute,
                predicate: static (node, _) => true,
                transform: static (ctx, _) => Build(ctx));

        // The assembly's scope, projected to values so the pipeline caches on them.
        // Holding the Compilation itself would invalidate every downstream step on
        // any edit at all -- the same reason ScaffoldGenerator projects its root.
        IncrementalValueProvider<ScopeSource> scope = context.CompilationProvider
            .Select(static (compilation, _) => ReadScope(compilation));

        context.RegisterSourceOutput(
            models.Where(static model => model is not null).Collect().Combine(scope),
            static (spc, pair) => Emit(spc, pair.Left!, pair.Right));
    }

    // The declared scope and the assembly name, unvalidated. Validation happens at
    // emit time, where a diagnostic can be reported.
    private static ScopeSource ReadScope(Compilation compilation)
    {
        foreach (AttributeData attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == PermissionScopeAttribute
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string declared)
            {
                return new ScopeSource(declared, compilation.AssemblyName);
            }
        }

        return new ScopeSource(null, compilation.AssemblyName);
    }

    private static PermissionModel? Build(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        ImmutableArray<string>.Builder extras = ImmutableArray.CreateBuilder<string>();
        AttributeData declaration = ctx.Attributes[0];

        // params string[] arrives as one array-typed constructor argument. A
        // declaration with no extras has either zero arguments or an empty array,
        // depending on how it was written.
        if (declaration.ConstructorArguments.Length == 1)
        {
            TypedConstant argument = declaration.ConstructorArguments[0];

            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (TypedConstant element in argument.Values)
                {
                    if (element.Value is string extra)
                    {
                        extras.Add(extra);
                    }
                }
            }
        }

        return new PermissionModel(
            type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            type.ToDisplayString(),
            extras.ToImmutable());
    }

    private static void Emit(
        SourceProductionContext spc, ImmutableArray<PermissionModel?> models, ScopeSource scopeSource)
    {
        // R15: nothing marked, nothing emitted. An application that declares no
        // permissions gets no generated file at all, not an empty one.
        if (models.Length == 0)
        {
            return;
        }

        string? scope;

        if (scopeSource.Declared is not null)
        {
            if (!PermissionNaming.IsValidScope(scopeSource.Declared))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PermissionsDiagnostics.ScopeNotUsable, Location.None, scopeSource.Declared));
                return;
            }

            scope = scopeSource.Declared;
        }
        else
        {
            scope = PermissionNaming.DefaultScope(scopeSource.AssemblyName);

            if (scope is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PermissionsDiagnostics.NoUsableScope, Location.None, scopeSource.AssemblyName ?? "(none)"));
                return;
            }
        }

        // Keyed on the emitted segment, not on the type name. The segment is
        // lower-cased, so Article and ARTICLE differ as type names and collide as
        // permission names; keying on the type name let both through, each emitting
        // sample:article.*, and one grant then gated two models.
        var seenTypeNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var catalog = new List<string>();

        foreach (PermissionModel model in models.Where(model => model is not null).Select(model => model!))
        {
            string segment = PermissionNaming.ModelSegment(model.TypeName);

            if (seenTypeNames.TryGetValue(segment, out string? other))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PermissionsDiagnostics.ModelNameCollides, Location.None, other, model.FullName));

                // Emitted for the same reason a declined model's constants are, and
                // it is why the hint name carries the namespace: two colliding
                // models share a type name, so a hint name built from the type name
                // alone is a duplicate and the generator throws instead of
                // reporting. The constants class itself does not collide, because
                // the namespaces differ.
                Constants(spc, model, scope, PermissionNaming.CoreActions.ToImmutableArray());
                continue;
            }

            seenTypeNames.Add(segment, model.FullName);

            bool usable = TryActions(spc, model, out ImmutableArray<string> actions);

            // Emitted even when the model was declined, and this is the whole reason
            // TryActions returns its usable actions rather than nothing. A declined
            // model used to emit no file at all, so the scaffolding's generated
            // routes -- which reference XPermissions.View by name and cannot see this
            // generator's verdict -- lost the type they name. The author then got one
            // real NWP00x error buried under a CS0234 cascade from files they cannot
            // open, and the cascade is what they read first. The build still fails, on
            // the diagnostic, which is the outcome the diagnostic is for.
            //
            // NWP003 and NWP004 are the two this cannot cover. They are the scope
            // itself being unusable, and every permission name begins with the scope,
            // so there is no name to emit a constant for. Those two return above,
            // before any model is visited, and the cascade they produce is the price
            // of not inventing a scope.
            Constants(spc, model, scope, actions);

            if (!usable)
            {
                // Out of the catalog on purpose: a declined model's permissions are
                // not grantable, and AddPermissions would otherwise register names
                // the author was just told to fix.
                continue;
            }

            string constantsClass = PermissionNaming.ConstantsClassName(model.TypeName);
            string qualified = model.Namespace.Length == 0
                ? "global::" + constantsClass
                : "global::" + model.Namespace + "." + constantsClass;

            foreach (string action in actions)
            {
                catalog.Add(qualified + "." + PermissionNaming.ConstantName(action));
            }
        }

        // Emitted even when every model was declined, and empty rather than absent.
        // R15 is that an assembly declaring nothing gets no file; this assembly
        // declared something and was refused, so the author's own
        // AddPermissions(PermissionCatalog.All) must still compile. Otherwise a
        // single-model application answers one NWP00x with a CS0103 in a file the
        // author did open, which is the cascade this generator stopped emitting.
        spc.AddSource("PermissionCatalog.g.cs", PermissionsEmitter.Catalog(catalog));
    }

    // The hint name carries the namespace, so two models with one type name are two
    // files. Roslyn throws on a duplicate hint name, which would turn a reported
    // collision into a crashed generator.
    private static void Constants(
        SourceProductionContext spc, PermissionModel model, string scope, ImmutableArray<string> actions)
    {
        string hint = model.Namespace.Length == 0
            ? model.TypeName + "Permissions.g.cs"
            : model.Namespace + "." + model.TypeName + "Permissions.g.cs";

        spc.AddSource(hint, PermissionsEmitter.Constants(model, scope, actions));
    }

    // The four, then the declared extras that are usable, in declaration order.
    // False when any extra was rejected, in which case a diagnostic has already been
    // reported. The out parameter is still populated: the caller emits the constants
    // for what survived, so a declined model costs one diagnostic rather than one
    // diagnostic and a CS0234 for every generated reference to its constants class.
    private static bool TryActions(
        SourceProductionContext spc, PermissionModel model, out ImmutableArray<string> actions)
    {
        ImmutableArray<string>.Builder built = ImmutableArray.CreateBuilder<string>();
        built.AddRange(PermissionNaming.CoreActions);

        // Two actions can differ as strings and still emit one identifier, because
        // ConstantName drops underscores: publish_draft and publish__draft are both
        // PublishDraft. So is view_ against the core view, and all against the All
        // list the class always emits. The string checks below cannot see any of
        // them, and the result is CS0102 inside a .g.cs the author cannot edit.
        var taken = new Dictionary<string, string>(StringComparer.Ordinal) { ["All"] = "the list of every permission this model declares" };

        foreach (string core in PermissionNaming.CoreActions)
        {
            taken[PermissionNaming.ConstantName(core)] = "the core permission '" + core + "'";
        }

        bool usable = true;

        foreach (string extra in model.Extras)
        {
            if (PermissionNaming.IsCoreAction(extra))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PermissionsDiagnostics.ExtraCollidesWithCore, Location.None, model.FullName, extra));
                usable = false;
                continue;
            }

            if (!PermissionNaming.IsValidAction(extra))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PermissionsDiagnostics.ExtraNotUsable, Location.None, model.FullName, extra));
                usable = false;
                continue;
            }

            if (built.Contains(extra))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PermissionsDiagnostics.DuplicateExtra, Location.None, model.FullName, extra));
                usable = false;
                continue;
            }

            string identifier = PermissionNaming.ConstantName(extra);

            if (taken.TryGetValue(identifier, out string owner))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    PermissionsDiagnostics.ExtraCollidesWithIdentifier,
                    Location.None,
                    model.FullName,
                    extra,
                    identifier,
                    owner));
                usable = false;
                continue;
            }

            taken.Add(identifier, "the extra permission '" + extra + "'");
            built.Add(extra);
        }

        actions = built.ToImmutable();
        return usable;
    }
}

// The assembly scope as declared and as it would default, before either is
// validated. Equality is by value so the incremental pipeline caches on it; a
// record would say that in one line and netstandard2.0 has no IsExternalInit, so
// this generator writes it out the way ScaffoldModel does.
internal sealed class ScopeSource : System.IEquatable<ScopeSource>
{
    internal ScopeSource(string? declared, string? assemblyName)
    {
        Declared = declared;
        AssemblyName = assemblyName;
    }

    internal string? Declared { get; }

    internal string? AssemblyName { get; }

    public bool Equals(ScopeSource? other) =>
        other is not null && Declared == other.Declared && AssemblyName == other.AssemblyName;

    public override bool Equals(object? obj) => Equals(obj as ScopeSource);

    public override int GetHashCode() =>
        (Declared?.GetHashCode() ?? 0) * 397 ^ (AssemblyName?.GetHashCode() ?? 0);
}

// One model that declares permissions.
internal sealed class PermissionModel
{
    internal PermissionModel(
        string ns, string typeName, string fullName, ImmutableArray<string> extras)
    {
        Namespace = ns;
        TypeName = typeName;
        FullName = fullName;
        Extras = extras;
    }

    internal string Namespace { get; }

    internal string TypeName { get; }

    internal string FullName { get; }

    internal ImmutableArray<string> Extras { get; }
}
