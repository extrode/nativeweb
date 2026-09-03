using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NativeWeb.Scaffold.Generator;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Unit;

/// <summary>
/// What the scaffold generator emits for a model that declares
/// <c>[Permissions]</c>, and what it still emits for one that does not.
/// </summary>
/// <remarks>
/// The emitted source is the right layer for this. Where the filter sits relative to
/// <c>RequireAuthorization</c> decides whether the two gates AND or replace each
/// other, and that ordering is a property of every model the generator will ever
/// see; a runtime test can only ever exercise the one model it happens to have.
/// </remarks>
public sealed class PermissionGatingTests
{
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
                public bool PublicRead { get; init; }
            }

            public sealed class ScaffoldListAttribute : System.Attribute { public string? Label { get; init; } }
            public sealed class ScaffoldKeyAttribute : System.Attribute { }
        }

        namespace NativeWeb.Permissions
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PermissionsAttribute : System.Attribute
            {
                public PermissionsAttribute(params string[] extras) => Extras = extras;
                public string[] Extras { get; }
            }
        }
        """;

    private const string Gated = """
        using NativeWeb.Permissions;
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold(Display = "Articles", Route = "articles")]
            [Permissions]
            public sealed class Article
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList] public string Title { get; set; } = "";
            }
        }
        """;

    private const string Plain = """
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold(Display = "Articles", Route = "articles")]
            public sealed class Article
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList] public string Title { get; set; } = "";
            }
        }
        """;

    private const string GatedAndAuthed = """
        using NativeWeb.Permissions;
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold(Display = "Articles", Route = "articles")]
            [Auth(Role = "admin")]
            [Permissions]
            public sealed class Article
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList] public string Title { get; set; } = "";
            }
        }
        """;

    private const string PublicReadModel = """
        using NativeWeb.Permissions;
        using NativeWeb.Scaffold;

        namespace Demo
        {
            [Scaffold(Display = "Articles", Route = "articles")]
            [Auth(Role = "admin", PublicRead = true)]
            [Permissions]
            public sealed class Article
            {
                [ScaffoldKey] public string Id { get; set; } = "";
                [ScaffoldList] public string Title { get; set; } = "";
            }
        }
        """;

    [Theory]
    [InlineData("MapGet(\"/\"", "View", "")]
    [InlineData("MapGet(\"/new\"", "Add", "")]
    [InlineData("MapPost(\"/\"", "Add", "")]
    [InlineData("MapGet(\"/{id}\"", "Change", ", \"id\"")]
    [InlineData("MapPost(\"/{id}\"", "Change", ", \"id\"")]
    [InlineData("MapPost(\"/{id}/delete\"", "Delete", ", \"id\"")]
    public void EveryRoute_CarriesTheFilterForItsOwnAction(string route, string constant, string resource)
    {
        string source = Run(Attributes, Gated);

        string expected = "new global::NativeWeb.Permissions.PermissionEndpointFilter("
            + $"global::Demo.ArticlePermissions.{constant}{resource})";

        Assert.Contains(expected, RouteBlock(source, route), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFilterIsHandedTheGeneratedConstant_NeverAStringLiteral()
    {
        string source = Run(Attributes, Gated);

        Assert.DoesNotContain("PermissionEndpointFilter(\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sample:article", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthAndPermissions_BothLandOnTheRoute_WithTheRoleDecidedFirst()
    {
        string block = RouteBlock(Run(Attributes, GatedAndAuthed), "MapGet(\"/new\"");

        int role = block.IndexOf("RequireAuthorization", StringComparison.Ordinal);
        int filter = block.IndexOf("AddEndpointFilter", StringComparison.Ordinal);

        Assert.InRange(role, 0, int.MaxValue);
        Assert.InRange(filter, role + 1, int.MaxValue);
    }

    [Fact]
    public void PublicRead_LiftsBothChecksOffTheListRoute()
    {
        string source = Run(Attributes, PublicReadModel);
        string list = RouteBlock(source, "MapGet(\"/\"");

        Assert.DoesNotContain("RequireAuthorization", list, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionEndpointFilter", list, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicRead_LeavesTheWriteRoutesGated()
    {
        string write = RouteBlock(Run(Attributes, PublicReadModel), "MapGet(\"/new\"");

        Assert.Contains("RequireAuthorization", write, StringComparison.Ordinal);
        Assert.Contains("global::Demo.ArticlePermissions.Add", write, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeldSet_IsResolvedOncePerListRender()
    {
        string source = Run(Attributes, Gated);

        Assert.Equal(2, Occurrences(source, ".HeldAsync("));
        Assert.Equal(1, Occurrences(source, "bool canAdd = held.Contains(global::Demo.ArticlePermissions.Add.Name);"));
    }

    [Theory]
    [InlineData("if (canAdd)")]
    [InlineData("if (canChange)")]
    [InlineData("if (canDelete)")]
    public void EachListControl_IsGuardedByItsOwnPermission(string guard)
    {
        Assert.Contains(guard, Run(Attributes, Gated), StringComparison.Ordinal);
    }

    [Fact]
    public void TheIndexLink_IsGatedByTheViewPermission()
    {
        Assert.Contains(
            "ScaffoldAccess.CanReadAsync(http, null, null, false, global::Demo.ArticlePermissions.View.Name, held)",
            Run(Attributes, Gated),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnAuthedAndGatedIndexLink_AsksForBoth()
    {
        Assert.Contains(
            "ScaffoldAccess.CanReadAsync(http, null, \"admin\", true, global::Demo.ArticlePermissions.View.Name, held)",
            Run(Attributes, GatedAndAuthed),
            StringComparison.Ordinal);
    }

    [Fact]
    public void PublicRead_LeavesTheIndexLinkUnconditional()
    {
        Assert.DoesNotContain("CanReadAsync", Run(Attributes, PublicReadModel), StringComparison.Ordinal);
    }

    // R15. The permission layer is opt-in: a model that never declared it must emit
    // source that does not mention it, so a host that never references
    // NativeWeb.Permissions still compiles.
    [Fact]
    public void NoPermissionsAttribute_EmitsNothingFromThePermissionLayer()
    {
        string source = Run(Attributes, Plain);

        Assert.DoesNotContain("NativeWeb.Permissions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("held", source, StringComparison.Ordinal);
        Assert.DoesNotContain("canAdd", source, StringComparison.Ordinal);
    }

    // Syntax only, as in the sibling suite: the test compilation has no ASP.NET
    // reference, so every unresolved framework symbol would be noise. It is still
    // the check that matters here, because the control guards are braces the emitter
    // has to balance itself, and an unbalanced one is a syntax error.
    [Theory]
    [InlineData(Gated)]
    [InlineData(GatedAndAuthed)]
    [InlineData(PublicReadModel)]
    [InlineData(Plain)]
    public void TheEmittedSource_Parses(string model)
    {
        foreach (SyntaxTree tree in Emit(Attributes, model).SyntaxTrees)
        {
            Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        }
    }

    // The route's own `.Map...(` line through to the `;` that ends the chain: the
    // whole builder expression for one endpoint and nothing from the next.
    private static string RouteBlock(string source, string route)
    {
        int start = source.IndexOf(route, StringComparison.Ordinal);
        Assert.InRange(start, 0, int.MaxValue);

        int end = source.IndexOf("group.Map", start + route.Length, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
            at >= 0;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static Compilation Emit(params string[] sources)
    {
        CSharpGeneratorDriver
            .Create(new ScaffoldGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile(sources), out Compilation updated, out _);

        return updated;
    }

    private static string Run(params string[] sources)
    {
        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new ScaffoldGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile(sources), out _, out ImmutableArray<Diagnostic> diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
    }

    private static CSharpCompilation Compile(params string[] sources)
    {
        List<MetadataReference> references =
            [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(path => path.Length > 0)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];

        return CSharpCompilation.Create(
            "scaffold-permission-tests",
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
