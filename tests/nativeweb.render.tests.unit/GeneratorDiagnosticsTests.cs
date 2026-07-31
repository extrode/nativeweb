using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using NativeWeb.Render.Generator;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

/// <summary>
/// R12: every template mistake is a build error. Drives the generator directly
/// through <see cref="CSharpGeneratorDriver"/> and asserts the NWT diagnostics.
/// </summary>
public sealed class GeneratorDiagnosticsTests
{
    private sealed class TestAdditionalText(string path, string source) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(source);
    }

    private static ImmutableArray<Diagnostic> Run(params (string Path, string Source)[] files)
    {
        var compilation = CSharpCompilation.Create(
            "render-diagnostics-tests",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var driver = CSharpGeneratorDriver.Create(
            generators: [new RenderTemplateGenerator().AsSourceGenerator()],
            additionalTexts: files.Select(f => (AdditionalText)new TestAdditionalText(f.Path, f.Source)));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);
        return diagnostics;
    }

    private static void AssertSingleDiagnostic(string expectedId, params (string Path, string Source)[] files)
    {
        ImmutableArray<Diagnostic> diagnostics = Run(files);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void ValidTemplateSet_ProducesNoDiagnostics()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/layout/page.html", "<!--#template\n@param string Title\n--><!DOCTYPE html><html lang=\"en\"><head><title>{{Title}}</title></head><body>{{slot Main}}</body></html>"),
            ("templates/home.html", "<!--#template\n@layout layout/page\n@param string Message\n--><h1>{{Message}}</h1>"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnclosedHeader_ReportsNwt001() =>
        AssertSingleDiagnostic("NWT001", ("templates/broken.html", "<!--#template\n@param string X\n<p>never closed</p>"));

    [Fact]
    public void UnknownDirective_ReportsNwt002() =>
        AssertSingleDiagnostic("NWT002", ("templates/broken.html", "<!--#template\n@bogus thing\n--><p>x</p>"));

    [Fact]
    public void MalformedParam_ReportsNwt003() =>
        AssertSingleDiagnostic("NWT003", ("templates/broken.html", "<!--#template\n@param OnlyOneToken\n--><p>x</p>"));

    [Fact]
    public void MissingLayout_ReportsNwt004() =>
        AssertSingleDiagnostic("NWT004", ("templates/page.html", "<!--#template\n@layout layout/absent\n--><p>x</p>"));

    [Fact]
    public void MissingPartial_ReportsNwt005() =>
        AssertSingleDiagnostic("NWT005", ("templates/page.html", "<p>{{> shared/absent}}</p>"));

    [Fact]
    public void PartialArgumentMismatch_ReportsNwt006()
    {
        // Missing required arg and an unknown arg in one call.
        AssertSingleDiagnostic(
            "NWT006",
            ("templates/shared/field.html", "<!--#template\n@param string Label\n--><span>{{Label}}</span>"),
            ("templates/page.html", "<p>{{> shared/field Wrong=\"x\"}}</p>"));
    }

    [Fact]
    public void FillWithoutMatchingSlot_ReportsNwt007()
    {
        AssertSingleDiagnostic(
            "NWT007",
            ("templates/layout/page.html", "<div>{{slot Main}}</div>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n-->{{fill Ghost}}<p>x</p>{{/fill}}"));
    }

    [Fact]
    public void FillWithoutLayout_ReportsNwt007() =>
        AssertSingleDiagnostic("NWT007", ("templates/page.html", "{{fill Head}}<p>x</p>{{/fill}}"));

    [Fact]
    public void UnclosedIfBlock_ReportsNwt008() =>
        AssertSingleDiagnostic("NWT008", ("templates/page.html", "<!--#template\n@param bool Flag\n-->{{if Flag}}<p>x</p>"));

    [Fact]
    public void StrayCloser_ReportsNwt008() =>
        AssertSingleDiagnostic("NWT008", ("templates/page.html", "<p>x</p>{{/if}}"));

    [Fact]
    public void TwoTemplatesCollidingOnClassName_ReportNwt009() =>
        AssertSingleDiagnostic(
            "NWT009",
            ("templates/sign-in.html", "<p>a</p>"),
            ("templates/sign_in.html", "<p>b</p>"));

    [Fact]
    public void ParamTypeConflictWithLayout_ReportsNwt010()
    {
        AssertSingleDiagnostic(
            "NWT010",
            ("templates/layout/page.html", "<!--#template\n@param string Title\n--><div>{{slot Main}}</div>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n@param int Title\n--><p>x</p>"));
    }

    [Fact]
    public void RequiredSlotUnfilled_ReportsNwt011()
    {
        AssertSingleDiagnostic(
            "NWT011",
            ("templates/layout/page.html", "<div>{{slot Main}}</div><aside>{{slot Sidebar}}</aside>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n--><p>x</p>"));
    }

    [Fact]
    public void OptionalSlotUnfilled_IsFine()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/layout/page.html", "<div>{{slot Main}}</div><aside>{{slot? Sidebar}}</aside>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n--><p>x</p>"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ExpressionWithStatementCharacters_ReportsNwt012() =>
        AssertSingleDiagnostic("NWT012", ("templates/page.html", "<!--#template\n@param string X\n--><p>{{X; System.IO.File.Delete(\"x\")}}</p>"));

    [Fact]
    public void LayoutCalledAsPartial_ReportsNwt006()
    {
        AssertSingleDiagnostic(
            "NWT006",
            ("templates/layout/page.html", "<div>{{slot Main}}</div>"),
            ("templates/page.html", "<p>{{> layout/page}}</p>"));
    }

    [Fact]
    public void FileOutsideTemplatesDirectory_IsIgnored()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(("wwwroot/pages/landing.html", "{{if Broken}}"));

        Assert.Empty(diagnostics);
    }
}
