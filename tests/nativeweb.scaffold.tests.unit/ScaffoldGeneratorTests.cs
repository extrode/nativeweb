using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NativeWeb.Scaffold.Generator;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Unit;

/// <summary>
/// Drives the scaffold generator directly and asserts on what it emits.
/// </summary>
/// <remarks>
/// Asserting on emitted source rather than on runtime behaviour is the right layer
/// for the guarantees that matter here: that a read-only property has no input, and
/// that every mutating route carries the antiforgery metadata. Both are properties
/// of every model the generator will ever see, and an integration test can only
/// check the one model it happens to have.
/// </remarks>
public sealed class ScaffoldGeneratorTests
{
    // Declared inline rather than referenced, so the test compilation needs nothing
    // but the BCL. The generator matches by metadata name, not by assembly identity.
    private const string Attributes = """
        namespace NativeWeb.Scaffold
        {
            public sealed class ScaffoldAttribute : System.Attribute
            {
                public string? Display { get; init; }
                public string? Route { get; init; }
            }

            public sealed class AuthAttribute : System.Attribute
            {
                public string? Role { get; init; }
                public string? Policy { get; init; }
                public string? Read { get; init; }
                public string? Write { get; init; }
                public string? Delete { get; init; }
            }

            public sealed class ScaffoldListAttribute : System.Attribute { public string? Label { get; init; } }
            public sealed class ScaffoldSearchAttribute : System.Attribute { }
            public sealed class ScaffoldMultilineAttribute : System.Attribute { }
            public sealed class ScaffoldReadOnlyAttribute : System.Attribute { }
            public sealed class ScaffoldKeyAttribute : System.Attribute { }
        }
        """;

    private const string ArticleModel = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold(Display = "Articles", Route = "articles")]
            [Auth(Role = "admin", Delete = "owner")]
            public sealed class Article
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList][ScaffoldSearch] public string Title { get; set; } = "";
                [ScaffoldMultiline] public string Body { get; set; } = "";
                [ScaffoldList] public bool Published { get; set; }
                [ScaffoldList][ScaffoldReadOnly] public System.DateTime CreatedAt { get; set; }
            }
        }
        """;

    private static (ImmutableArray<Diagnostic> Diagnostics, string Source) Run(params string[] sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "scaffold-generator-tests",
            syntaxTrees: sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new ScaffoldGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        string emitted = string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

        return (diagnostics, emitted);
    }

    [Fact]
    public void ModelWithNoKey_ReportsNws001()
    {
        const string noKey = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Widget
                {
                    public string Name { get; set; } = "";
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Attributes, noKey);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NWS001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void WellFormedModel_ReportsNothing()
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Attributes, ArticleModel);

        Assert.Empty(diagnostics);
    }

    // The mass-assignment guarantee. The form record IS the allowlist: a property
    // that is not a constructor parameter of it cannot be bound by any request,
    // because there is no code path that reads it.
    [Fact]
    public void FormRecord_OmitsTheKeyAndEveryReadOnlyProperty()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("public sealed record ArticleForm(", source, StringComparison.Ordinal);
        Assert.Contains("string? Title", source, StringComparison.Ordinal);
        Assert.Contains("string? Body", source, StringComparison.Ordinal);
        Assert.Contains("string? Published", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string? Id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string? CreatedAt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("form[\"id\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("form[\"createdAt\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyProperties_AreCarriedFromTheExistingRowNotTheForm()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("CreatedAt = existing is null ? default! : existing.CreatedAt", source, StringComparison.Ordinal);
        Assert.Contains("Id = existing is null ? default! : existing.Id", source, StringComparison.Ordinal);
    }

    // Measured in brainstorm 001 sec.12: a BindAsync-bound handler carries no
    // antiforgery metadata of its own, so UseAntiforgery() skips it and a tokenless
    // POST is answered 200 OK. Every mutating route has to re-attach it, and the
    // reason to assert on the count is that "most of them" is the failure mode.
    [Fact]
    public void EveryMutatingRoute_CarriesAntiforgeryMetadata()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        int posts = Occurrences(source, "group.MapPost(");
        int guards = Occurrences(source, ".WithMetadata(RequiresAntiforgery.Instance)");

        Assert.Equal(3, posts);
        Assert.Equal(posts, guards);
    }

    // Binding runs before the endpoint-filter pipeline, so the content-type check
    // cannot live in a filter -- it has to be inside BindAsync or it does not run.
    [Fact]
    public void Binder_RejectsANonFormBodyItself()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("if (!context.Request.HasFormContentType)", source, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status415UnsupportedMediaType", source, StringComparison.Ordinal);
    }

    // Measured 2026-08-19: a NativeAOT publish failed IL2026/IL3050 on every
    // generated Map* call. The Request Delegate Generator only rewrites Map* calls
    // in the compilation's own syntax trees, and Roslyn does not feed one
    // generator's output to another -- so an emitted Map* always binds the
    // reflection-based Delegate overload. The RequestDelegate overloads carry
    // neither attribute. Every route must go through ScaffoldEndpoints.Handle.
    [Fact]
    public void EveryRoute_IsMappedAsARequestDelegateSoTheAotPublishStaysClean()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        // Six per model plus the admin index. Counting rather than pattern-matching
        // each route, because the failure mode is one route being missed.
        int routes = Occurrences(source, ".MapGet(") + Occurrences(source, ".MapPost(");
        Assert.Equal(7, routes);
        Assert.Equal(routes, Occurrences(source, "ScaffoldEndpoints.Handle("));
    }

    [Fact]
    public void RouteParameters_AreReadFromRouteValuesRatherThanBound()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("ScaffoldEndpoints.RouteValue(http, \"id\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewPage_IsEmittedAsASynchronousPageHandler()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("private static IResult NewPage(HttpContext http)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PerOperationRoles_OverrideTheModelWideRole()
    {
        (_, string source) = Run(Attributes, ArticleModel);

        Assert.Contains("Roles = \"admin\"", source, StringComparison.Ordinal);
        Assert.Contains("Roles = \"owner\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelWithNoAuthAttribute_EmitsNoAuthorizationCall()
    {
        const string open = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Tag
                {
                    [ScaffoldKey] public string Slug { get; set; } = "";
                    [ScaffoldList] public string Name { get; set; } = "";
                }
            }
            """;

        (_, string source) = Run(Attributes, open);

        Assert.Contains("MapTagScaffold", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthorization", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteAndDisplay_FallBackToThePluralisedTypeName()
    {
        const string bare = """
            using NativeWeb.Scaffold;

            namespace Demo
            {
                [Scaffold]
                public sealed class Category
                {
                    [ScaffoldKey] public string Id { get; set; } = "";
                    [ScaffoldList] public string Name { get; set; } = "";
                }
            }
            """;

        (_, string source) = Run(Attributes, bare);

        Assert.Contains("BasePath = \"/admin/categories\"", source, StringComparison.Ordinal);
        Assert.Contains("Display = \"Categories\"", source, StringComparison.Ordinal);
        Assert.Contains("Singular = \"Category\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmittedSource_CompilesWithNoErrors()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "scaffold-emit-tests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(Attributes), CSharpSyntaxTree.ParseText(ArticleModel)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create(new ScaffoldGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        // Only syntax is checked here: the test compilation deliberately has no
        // ASP.NET reference, so every unresolved framework symbol would be noise.
        // That the whole thing binds is proven by apps/sample building at all.
        Assert.DoesNotContain(
            updated.SyntaxTrees.SelectMany(t => t.GetDiagnostics()),
            d => d.Severity == DiagnosticSeverity.Error);
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
