using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NativeWeb.Permissions.Generator;

namespace NativeWeb.Permissions.Tests.Unit;

/// <summary>
/// Drives the permissions generator over a source string and hands back what it
/// emitted and what it complained about.
/// </summary>
/// <remarks>
/// The attributes are declared inline rather than referenced, so a test compilation
/// needs nothing but the BCL. The generator matches by metadata name, not by
/// assembly identity, which is also what lets the same trick prove R15: a
/// compilation that simply omits <see cref="Attributes"/> has no
/// <c>[Permissions]</c> type at all.
/// </remarks>
internal static class GeneratorHarness
{
    internal const string Attributes = """
        namespace NativeWeb.Permissions
        {
            public readonly struct Permission
            {
                public Permission(string name) => Name = name;
                public string Name { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PermissionsAttribute : System.Attribute
            {
                public PermissionsAttribute(params string[] extras) => Extras = extras;
                public string[] Extras { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class PermissionScopeAttribute : System.Attribute
            {
                public PermissionScopeAttribute(string scope) => Scope = scope;
                public string Scope { get; }
            }
        }
        """;

    internal const string ArticleModel = """
        using NativeWeb.Permissions;

        namespace Demo
        {
            [Permissions]
            public sealed class Article
            {
                public string Id { get; set; } = "";
            }
        }
        """;

    private static List<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

    internal static (ImmutableArray<Diagnostic> Diagnostics, string Source, int FileCount) Run(
        string assemblyName, params string[] sources)
    {
        List<MetadataReference> references = PlatformReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new PermissionsGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        GeneratedSourceResult[] generated =
            [.. driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources)];

        string emitted = string.Join("\n", generated.Select(source => source.SourceText.ToString()));
        return (diagnostics, emitted, generated.Length);
    }

    /// <summary>
    /// Runs the generator and asserts the emitted source is syntactically valid,
    /// returning the compilation so a caller can look for a specific error.
    /// </summary>
    internal static Compilation Compile(string assemblyName, params string[] sources)
    {
        List<MetadataReference> references = PlatformReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create(new PermissionsGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        return updated;
    }
}
