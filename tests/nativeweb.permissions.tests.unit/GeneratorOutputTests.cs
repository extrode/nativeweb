using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

public sealed class GeneratorOutputTests
{
    [Fact]
    public void MarkedModel_GetsTheFourConstantsWithoutDeclaringThem()
    {
        (_, string source, _) = GeneratorHarness.Run(
            "Sample", GeneratorHarness.Attributes, GeneratorHarness.ArticleModel);

        Assert.Contains("public static class ArticlePermissions", source, StringComparison.Ordinal);
        Assert.Contains("View = new global::NativeWeb.Permissions.Permission(\"sample:article.view\")", source, StringComparison.Ordinal);
        Assert.Contains("Add = new global::NativeWeb.Permissions.Permission(\"sample:article.add\")", source, StringComparison.Ordinal);
        Assert.Contains("Change = new global::NativeWeb.Permissions.Permission(\"sample:article.change\")", source, StringComparison.Ordinal);
        Assert.Contains("Delete = new global::NativeWeb.Permissions.Permission(\"sample:article.delete\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstantsClass_IsInTheModelsOwnNamespace()
    {
        (_, string source, _) = GeneratorHarness.Run(
            "Sample", GeneratorHarness.Attributes, GeneratorHarness.ArticleModel);

        Assert.Contains("namespace Demo;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredExtra_AppearsAlongsideTheFour()
    {
        const string model = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("publish")]
                public sealed class Article { }
            }
            """;

        (_, string source, _) = GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, model);

        Assert.Contains("Publish = new global::NativeWeb.Permissions.Permission(\"sample:article.publish\")", source, StringComparison.Ordinal);
        Assert.Contains("View = new", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitScope_WinsOverTheAssemblyName()
    {
        const string scope = """[assembly: NativeWeb.Permissions.PermissionScope("blog")]""";

        (_, string source, _) = GeneratorHarness.Run(
            "Sample", GeneratorHarness.Attributes, scope, GeneratorHarness.ArticleModel);

        Assert.Contains("\"blog:article.view\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sample:article.view\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoAssembliesWithTheSameModelName_ProduceDifferentPermissionNames()
    {
        (_, string first, _) = GeneratorHarness.Run(
            "Blog", GeneratorHarness.Attributes, GeneratorHarness.ArticleModel);
        (_, string second, _) = GeneratorHarness.Run(
            "Shop", GeneratorHarness.Attributes, GeneratorHarness.ArticleModel);

        Assert.Contains("\"blog:article.view\"", first, StringComparison.Ordinal);
        Assert.Contains("\"shop:article.view\"", second, StringComparison.Ordinal);
        Assert.DoesNotContain("\"blog:", second, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_ListsEveryConstantOfEveryModel()
    {
        const string second = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions]
                public sealed class Tag { }
            }
            """;

        (_, string source, _) = GeneratorHarness.Run(
            "Sample", GeneratorHarness.Attributes, GeneratorHarness.ArticleModel, second);

        Assert.Contains("internal static class PermissionCatalog", source, StringComparison.Ordinal);
        Assert.Contains("global::Demo.ArticlePermissions.View,", source, StringComparison.Ordinal);
        Assert.Contains("global::Demo.TagPermissions.Delete,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoMarkedType_EmitsNothingAtAll()
    {
        const string plain = """
            namespace Demo
            {
                public sealed class Article { }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _, int files) =
            GeneratorHarness.Run("Sample", GeneratorHarness.Attributes, plain);

        Assert.Equal(0, files);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void NoPermissionsAttributeInTheCompilation_EmitsNothingAtAll()
    {
        const string plain = """
            namespace Demo
            {
                public sealed class Article { }
            }
            """;

        (_, _, int files) = GeneratorHarness.Run("Sample", plain);

        Assert.Equal(0, files);
    }

    [Fact]
    public void EmittedSource_IsSyntacticallyValid()
    {
        Compilation updated = GeneratorHarness.Compile(
            "Sample", GeneratorHarness.Attributes, GeneratorHarness.ArticleModel);

        Assert.DoesNotContain(
            updated.SyntaxTrees.SelectMany(tree => tree.GetDiagnostics()),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RemovingADeclaration_MakesEveryUseSiteFailToCompile()
    {
        const string useSite = """
            namespace Demo
            {
                public static class Caller
                {
                    public static object Uses() => ArticlePermissions.Publish;
                }
            }
            """;

        const string withExtra = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions("publish")]
                public sealed class Article { }
            }
            """;

        const string withoutExtra = """
            using NativeWeb.Permissions;

            namespace Demo
            {
                [Permissions]
                public sealed class Article { }
            }
            """;

        Compilation declared = GeneratorHarness.Compile(
            "Sample", GeneratorHarness.Attributes, withExtra, useSite);
        Compilation removed = GeneratorHarness.Compile(
            "Sample", GeneratorHarness.Attributes, withoutExtra, useSite);

        Assert.DoesNotContain(declared.GetDiagnostics(), IsMissingMember);
        Assert.Contains(removed.GetDiagnostics(), IsMissingMember);
    }

    private static bool IsMissingMember(Diagnostic diagnostic) =>
        diagnostic.Id == "CS0117" && diagnostic.GetMessage().Contains("Publish", StringComparison.Ordinal);
}
