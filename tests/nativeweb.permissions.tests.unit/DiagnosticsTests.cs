using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

public sealed class DiagnosticsTests
{
    [Fact]
    public void ExtraNamedLikeOneOfTheFour_ReportsNwp001()
    {
        const string model = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("delete")]
                public sealed class Article { }
            }
            """;

        Assert.Contains("NWP001", Ids(GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, model)));
    }

    [Theory]
    [InlineData("Publish")]
    [InlineData("publish.draft")]
    [InlineData("publish:draft")]
    [InlineData("2archive")]
    public void ExtraThatIsNotALowerCaseIdentifier_ReportsNwp002(string extra)
    {
        string model = $$"""
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("{{extra}}")]
                public sealed class Article { }
            }
            """;

        Assert.Contains("NWP002", Ids(GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, model)));
    }

    [Fact]
    public void AssemblyNameThatCannotFormAScope_ReportsNwp003()
    {
        Assert.Contains(
            "NWP003",
            Ids(GeneratorHarness.Run("1sample", GeneratorHarness.Attributes, GeneratorHarness.ArticleModel)));
    }

    [Fact]
    public void ExplicitScopeThatIsNotUsable_ReportsNwp004()
    {
        const string scope = """[assembly: NativeWeb.Permissions.PermissionScope("Blog Posts")]""";

        Assert.Contains(
            "NWP004",
            Ids(GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, scope, GeneratorHarness.ArticleModel)));
    }

    [Fact]
    public void ExtraDeclaredTwice_ReportsNwp005()
    {
        const string model = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("publish", "publish")]
                public sealed class Article { }
            }
            """;

        Assert.Contains("NWP005", Ids(GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, model)));
    }

    [Fact]
    public void TwoModelsWithTheSameTypeName_ReportNwp006()
    {
        const string first = """
            using NativeWeb.Permissions;

            namespace Blog
            {
                [Permissions]
                public sealed class Article { }
            }
            """;

        const string second = """
            using NativeWeb.Permissions;

            namespace Shop
            {
                [Permissions]
                public sealed class Article { }
            }
            """;

        Assert.Contains(
            "NWP006",
            Ids(GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, first, second)));
    }

    [Fact]
    public void TwoModelsWhoseNamesDifferOnlyInCase_ReportNwp006()
    {
        const string first = """
            using NativeWeb.Permissions;

            namespace Blog
            {
                [Permissions]
                public sealed class Article { }
            }
            """;

        const string second = """
            using NativeWeb.Permissions;

            namespace Shop
            {
                [Permissions]
                public sealed class ARTICLE { }
            }
            """;

        Assert.Contains(
            "NWP006",
            Ids(GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, first, second)));
    }

    [Fact]
    public void AValidDeclaration_ReportsNothing()
    {
        const string model = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("publish", "unpublish")]
                public sealed class Article { }
            }
            """;

        Assert.Empty(Ids(GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, model)));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("view_")]
    [InlineData("publish_draft", "publish__draft")]
    public void ExtrasThatEmitTheSameIdentifier_ReportNwp007(params string[] extras)
    {
        string declared = string.Join(", ", extras.Select(extra => $"\"{extra}\""));

        string model = $$"""
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions({{declared}})]
                public sealed class Article { }
            }
            """;

        Assert.Contains("NWP007", Ids(GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, model)));
    }

    [Fact]
    public void AnExtraThatWouldCollideWithTheAllList_EmitsNoUncompilableSource()
    {
        const string model = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("all")]
                public sealed class Article { }
            }
            """;

        Compilation compiled = GeneratorHarness.Compile("Sample", GeneratorHarness.Attributes, model);

        Assert.DoesNotContain(compiled.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0102");
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("Publish")]
    [InlineData("all")]
    public void ADeclinedModel_StillEmitsItsConstantsClass(string extra)
    {
        string model = $$"""
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("{{extra}}")]
                public sealed class Article { }
            }
            """;

        Compilation compiled = GeneratorHarness.Compile("Sample", GeneratorHarness.Attributes, model);

        Assert.Contains(
            compiled.SyntaxTrees,
            tree => tree.FilePath.EndsWith("ArticlePermissions.g.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("Publish")]
    [InlineData("all")]
    public void AReferenceToADeclinedModelsConstants_IsNotACascade(string extra)
    {
        string model = $$"""
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("{{extra}}")]
                public sealed class Article { }

                public static class Consumer
                {
                    public static Permission Gate() => ArticlePermissions.View;
                }
            }
            """;

        Compilation compiled = GeneratorHarness.Compile("Sample", GeneratorHarness.Attributes, model);

        Assert.DoesNotContain(
            compiled.GetDiagnostics(),
            diagnostic => diagnostic.Id is "CS0234" or "CS0103" or "CS0246");
    }

    [Fact]
    public void ADeclinedModel_IsKeptOutOfTheCatalog()
    {
        const string model = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("delete")]
                public sealed class Article { }
            }
            """;

        Compilation compiled = GeneratorHarness.Compile("Sample", GeneratorHarness.Attributes, model);

        SyntaxTree catalog = Assert.Single(
            compiled.SyntaxTrees,
            tree => tree.FilePath.EndsWith("PermissionCatalog.g.cs", StringComparison.Ordinal));

        Assert.DoesNotContain("ArticlePermissions.", catalog.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclinedModel_StillLeavesTheCatalogReferenceable()
    {
        const string model = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("delete")]
                public sealed class Article { }
            }
            """;

        const string consumer = """
            internal static class Host
            {
                internal static int Count() => PermissionCatalog.All.Count;
            }
            """;

        Compilation compiled =
            GeneratorHarness.Compile("Sample", GeneratorHarness.Attributes, model, consumer);

        Assert.DoesNotContain(
            compiled.GetDiagnostics(),
            diagnostic => diagnostic.Id is "CS0103" or "CS0234" or "CS0246" or "CS0826");
    }

    [Fact]
    public void CollidingModels_BothStillEmitTheirConstantsClass()
    {
        const string first = """
            using NativeWeb.Permissions;

            namespace Blog
            {
                [Permissions]
                public sealed class Article { }

                public static class BlogConsumer
                {
                    public static Permission Gate() => ArticlePermissions.View;
                }
            }
            """;

        const string second = """
            using NativeWeb.Permissions;

            namespace Shop
            {
                [Permissions]
                public sealed class Article { }

                public static class ShopConsumer
                {
                    public static Permission Gate() => ArticlePermissions.View;
                }
            }
            """;

        Compilation compiled = GeneratorHarness.Compile("Sample", GeneratorHarness.Attributes, first, second);

        Assert.DoesNotContain(
            compiled.GetDiagnostics(),
            diagnostic => diagnostic.Id is "CS0234" or "CS0103" or "CS0246");

        Assert.Equal(
            2,
            compiled.SyntaxTrees.Count(
                tree => tree.FilePath.EndsWith("ArticlePermissions.g.cs", StringComparison.Ordinal)));
    }

    [Fact]
    public void ADiagnostic_CannotBeLoweredOrSuppressed()
    {
        const string model = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("delete")]
                public sealed class Article { }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _, _) =
            GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, model);

        Diagnostic reported = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "NWP001");

        Assert.Contains(
            WellKnownDiagnosticTags.NotConfigurable,
            reported.Descriptor.CustomTags,
            StringComparer.Ordinal);
    }

    private static string[] Ids((ImmutableArray<Diagnostic> Diagnostics, string Source, int FileCount) run) =>
        [.. run.Diagnostics.Select(diagnostic => diagnostic.Id)];
}
