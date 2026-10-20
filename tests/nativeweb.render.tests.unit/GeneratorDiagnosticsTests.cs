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
    public void ExplicitFillMain_ReplacesImplicitContent()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/layout/page.html", "<div>{{slot Main}}</div>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n-->\n{{fill Main}}<p>x</p>{{/fill}}\n"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void FillMainAlongsideImplicitContent_ReportsNwt007() =>
        AssertSingleDiagnostic(
            "NWT007",
            ("templates/layout/page.html", "<div>{{slot Main}}</div>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n--><p>implicit</p>{{fill Main}}<p>x</p>{{/fill}}"));

    [Fact]
    public void FillNestedInsideBlock_ReportsNwt007() =>
        AssertSingleDiagnostic(
            "NWT007",
            ("templates/layout/page.html", "<div>{{slot Main}}</div><aside>{{slot? Side}}</aside>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n@param bool Flag\n-->{{if Flag}}{{fill Side}}<p>x</p>{{/fill}}{{/if}}"));

    [Fact]
    public void LayoutChain_ReportsNwt001()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/layout/base.html", "<div>{{slot Main}}</div>"),
            ("templates/layout/page.html", "<!--#template\n@layout layout/base\n--><section>{{slot Main}}</section>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n--><p>x</p>"));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NWT001", diagnostic.Id);
    }

    [Fact]
    public void LayoutWithoutMainSlot_ReportsNwt007() =>
        AssertSingleDiagnostic(
            "NWT007",
            ("templates/layout/page.html", "<div>{{slot? Side}}</div>"),
            ("templates/page.html", "<!--#template\n@layout layout/page\n--><p>dropped</p>"));

    [Fact]
    public void PartialArgumentWithSpaces_ReportsNwt006()
    {
        AssertSingleDiagnostic(
            "NWT006",
            ("templates/shared/field.html", "<!--#template\n@param string Label\n--><span>{{Label}}</span>"),
            ("templates/page.html", "<p>{{> shared/field Label=\"Hello world\"}}</p>"));
    }

    [Fact]
    public void ReservedParamName_ReportsNwt001() =>
        AssertSingleDiagnostic("NWT001", ("templates/page.html", "<!--#template\n@param string writer\n--><p>x</p>"));

    [Fact]
    public void UnknownLocaleKey_ReportsNwt013() =>
        AssertSingleDiagnostic(
            "NWT013",
            ("locales/en.json", "{ \"app\": { \"title\": \"x\" } }"),
            ("templates/page.html", "<h1>{{t:app.missing}}</h1>"));

    [Fact]
    public void LocaleKeyUsedWithoutAnyCatalog_ReportsNwt013() =>
        AssertSingleDiagnostic("NWT013", ("templates/page.html", "<h1>{{t:app.title}}</h1>"));

    [Fact]
    public void OrphanOverrideKey_ReportsNwt014() =>
        AssertSingleDiagnostic(
            "NWT014",
            ("locales/en.json", "{ \"app\": { \"title\": \"x\" } }"),
            ("locales/ms.json", "{ \"app\": { \"tittle\": \"y\" } }"));

    [Fact]
    public void MalformedLocaleJson_ReportsNwt015() =>
        AssertSingleDiagnostic("NWT015", ("locales/en.json", "{ \"app\": [1, 2] }"));

    [Fact]
    public void MissingEnglishRootCatalog_ReportsNwt015() =>
        AssertSingleDiagnostic("NWT015", ("locales/ms.json", "{ \"app\": { \"title\": \"y\" } }"));

    [Fact]
    public void ReservedForLoopVariable_ReportsNwt008() =>
        AssertSingleDiagnostic(
            "NWT008",
            ("locales/en.json", "{ \"app\": { \"title\": \"x\" } }"),
            ("templates/page.html", "<!--#template\n@param string[] Items\n-->{{for locale in Items}}<p>{{t:app.title}}</p>{{/for}}"));

    [Fact]
    public void NestedUnderscoreKey_IsMetadataNotAStringKey()
    {
        // {"app": {"_dir": ...}} must not become a string key "app._dir"
        // that would then trip NWT014 as an orphan override.
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("locales/en.json", "{ \"app\": { \"title\": \"x\" } }"),
            ("locales/ms.json", "{ \"app\": { \"_note\": \"metadata\", \"title\": \"y\" } }"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnderscoreKeyWithObjectValue_ReportsNwt015() =>
        AssertSingleDiagnostic("NWT015", ("locales/en.json", "{ \"_meta\": { \"x\": \"y\" } }"));

    [Fact]
    public void WhitespaceInUnicodeEscape_ReportsNwt015() =>
        AssertSingleDiagnostic("NWT015", ("locales/en.json", "{ \"app\": \"\\u 41x\" }"));

    [Fact]
    public void InvalidDirOverride_ReportsNwt015() =>
        AssertSingleDiagnostic("NWT015", ("locales/en.json", "{ \"_dir\": \"sideways\", \"app\": \"x\" }"));

    [Fact]
    public void LocaleIdCollidingWithLocalesMember_ReportsNwt015() =>
        AssertSingleDiagnostic(
            "NWT015",
            ("locales/en.json", "{ \"app\": \"x\" }"),
            ("locales/all.json", "{ }"));

    [Fact]
    public void TwoLocaleIdsCollidingOnMemberName_ReportNwt015() =>
        AssertSingleDiagnostic(
            "NWT015",
            ("locales/en.json", "{ \"app\": \"x\" }"),
            ("locales/pt-br.json", "{ }"),
            ("locales/pt_br.json", "{ }"));

    [Fact]
    public void NonAsciiCatalogValue_IsEscapedInTheEmittedLiteral()
    {
        // U+2028 is a line terminator to the C# lexer: Quote must escape it (and
        // everything else outside printable ASCII) or the emitted Locales class
        // fails to compile with a cryptic CS error instead of an NWT diagnostic.
        var compilation = CSharpCompilation.Create(
            "render-diagnostics-tests",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var driver = CSharpGeneratorDriver.Create(
            generators: [new RenderTemplateGenerator().AsSourceGenerator()],
            additionalTexts: [new TestAdditionalText("locales/en.json", "{ \"app\": \"line one\\u2028line two \\u00e9\" }")]);

        GeneratorDriverRunResult result = driver.RunGenerators(compilation).GetRunResult();

        Assert.Empty(result.Diagnostics);
        string locales = Assert.Single(result.GeneratedTrees, t => t.FilePath.Contains("Locales", StringComparison.Ordinal)).ToString();
        Assert.Contains("\\u2028", locales, StringComparison.Ordinal);
        Assert.Contains("\\u00e9", locales, StringComparison.Ordinal);
        Assert.DoesNotContain('\u2028', locales);
        Assert.DoesNotContain('\u00e9', locales);
    }

    [Fact]
    public void FileOutsideTemplatesDirectory_IsIgnored()
    {
        ImmutableArray<Diagnostic> diagnostics = Run(("wwwroot/pages/landing.html", "{{if Broken}}"));

        Assert.Empty(diagnostics);
    }

    // ---- locations ----------------------------------------------------------

    /// <summary>
    /// Every NWT diagnostic used to carry <c>Location.None</c>, so the build log
    /// had nothing to click and the IDE hung the error on the project rather than
    /// the line. The expectations here are written out by hand from the fixture
    /// rather than recomputed from it, because a location derived the same way
    /// the generator derives it would agree with any offset, right or wrong.
    /// </summary>
    [Fact]
    public void Diagnostic_PointsAtTheTemplateFileAndTheOffendingLine()
    {
        // Line 0: "<!--#template"
        // Line 1: "@param string Title"
        // Line 2: "@bogus thing"          <- the error, at column 0
        // Line 3: "--><p>x</p>"
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/broken.html", "<!--#template\n@param string Title\n@bogus thing\n--><p>x</p>"));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NWT002", diagnostic.Id);

        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.Equal("templates/broken.html", span.Path);
        Assert.Equal(2, span.StartLinePosition.Line);
        Assert.Equal(0, span.StartLinePosition.Character);
        Assert.Equal(2, span.EndLinePosition.Line);
        Assert.Equal("@bogus thing".Length, span.EndLinePosition.Character);
    }

    [Fact]
    public void Diagnostic_InTheBody_CountsTheHeaderLinesToo()
    {
        // Line 0: "<!--#template"
        // Line 1: "-->"
        // Line 2: "<p>ok</p>"
        // Line 3: "<p>{{Bad; Statement}}</p>"   <- NWT012, token starts at column 3
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/broken.html", "<!--#template\n-->\n<p>ok</p>\n<p>{{Bad; Statement}}</p>"));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NWT012", diagnostic.Id);

        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.Equal("templates/broken.html", span.Path);
        Assert.Equal(3, span.StartLinePosition.Line);
        Assert.Equal(3, span.StartLinePosition.Character);
    }

    [Fact]
    public void Diagnostic_WithABomAndCrlfLineEndings_StillPointsAtTheRightLine()
    {
        // The BOM is one character the parser strips before anything else, and
        // CRLF puts a '\r' at the end of every line: both shift offsets, and a
        // location that ignored either would be off by one from the first line on.
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/broken.html", "﻿<!--#template\r\n@param string Title\r\n@bogus thing\r\n--><p>x</p>"));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.Equal(2, span.StartLinePosition.Line);
        Assert.Equal(0, span.StartLinePosition.Character);
        // The '\r' belongs to the line ending, not to the directive.
        Assert.Equal("@bogus thing".Length, span.EndLinePosition.Character);
    }

    // ---- @param types -------------------------------------------------------

    /// <summary>
    /// '@param' splits on the LAST space, so a type containing one silently
    /// swallowed the words before it and was copied into the generated method
    /// signature unchecked. The author saw a C# syntax error in a file they never
    /// wrote, naming a line number in generated code.
    /// </summary>
    [Theory]
    [InlineData("@param My Model model")]
    [InlineData("@param string Foo bar")]
    [InlineData("@param int* Count")]
    public void ParamTypeThatIsNotAPlausibleTypeName_ReportsNwt003(string declaration) =>
        AssertSingleDiagnostic("NWT003", ("templates/broken.html", $"<!--#template\n{declaration}\n--><p>x</p>"));

    /// <summary>
    /// The check is a plausibility filter, not a C# type parser: everything a
    /// real template writes must still pass. A name that is well-formed but does
    /// not exist stays the compiler's to reject — it words that better than we
    /// could, and duplicating the judgement here would only add a way to be wrong.
    /// </summary>
    [Theory]
    [InlineData("string")]
    [InlineData("string?")]
    [InlineData("string[]")]
    [InlineData("Html")]
    [InlineData("__PRODUCT__.Render.AssetUrls")]
    [InlineData("global::System.Collections.Generic.IReadOnlyList<string>")]
    [InlineData("IReadOnlyList<KeyValuePair<string,int>>")]
    public void PlausibleParamTypes_AreAccepted(string type)
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/page.html", $"<!--#template\n@param {type} Value\n--><p>ok</p>"));

        Assert.Empty(diagnostics);
    }

    // ---- NWT016: interpolation loose in a tag -------------------------------

    /// <summary>
    /// <c>TemplateWriter</c> encodes <c>&lt;&gt;&amp;"'</c> and nothing else, which
    /// its own comment says is safe "in text and QUOTED-attribute contexts, which
    /// are the only contexts templates may interpolate into". That invariant was
    /// asserted in prose and enforced nowhere: a space is not encoded, so a value
    /// in an unquoted attribute can close it and open <c>onfocus=…</c>, and the
    /// attribute-name position is the same escape by a shorter route.
    /// </summary>
    [Theory]
    [InlineData("<div class={{Value}}>x</div>")]
    [InlineData("<div class = {{Value}}>x</div>")]
    [InlineData("<div data-x=prefix{{Value}}>x</div>")]
    [InlineData("<div {{Value}}>x</div>")]
    [InlineData("<div class=\"a\" {{Value}}>x</div>")]
    [InlineData("<div title={{t:app.name}}>x</div>")]
    public void InterpolationLooseInATag_ReportsNwt016(string body) =>
        AssertSingleDiagnostic(
            "NWT016",
            ("locales/en.json", "{ \"app.name\": \"x\" }"),
            ("templates/page.html", $"<!--#template\n@param string Value\n-->{body}"));

    /// <summary>
    /// The negatives matter more than the positives here: a guard that fires on
    /// ordinary markup gets deleted rather than obeyed.
    /// </summary>
    [Theory]
    // The ordinary quoted cases, single and double.
    [InlineData("<div class=\"{{Value}}\">x</div>")]
    [InlineData("<div class='{{Value}}'>x</div>")]
    [InlineData("<div class=\"a {{Value}} b\">x</div>")]
    // Text content, which is the other safe context.
    [InlineData("<p>{{Value}}</p>")]
    [InlineData("<p>a {{Value}} b</p>")]
    // Quotes INSIDE the expression, inside a quoted attribute — what every real
    // layout writes, since Assets.Resolve takes a string literal.
    [InlineData("<img src=\"{{Value.Resolve(\"/img/logo.svg\")}}\" alt=\"\">")]
    [InlineData("<img src=\"{{Value.Resolve(\"/a.svg\")}}\" title=\"{{Value}}\">")]
    // The sharp edge, and the reason the scanner jumps over {{…}} wholesale
    // rather than counting quotes as it goes: an ODD number of quotes inside one
    // expression. The two lines above happen to balance, so they would survive a
    // The case the {{…}} skip actually exists for: markup-like text INSIDE an
    // expression. A scanner reading expression bytes as markup opens a tag at
    // the '<' of "<b class" that nothing ever closes, and then reports the next
    // interpolation — in plain text, in no tag at all — as loose.
    [InlineData("<p>{{Value.Replace(\"<b class\", \"\")}}{{Value}}</p>")]
    // A block token in the attribute-name position emits nothing itself, so it
    // is not an interpolation; its contents are separate tokens, checked on
    // their own. The real layout writes exactly this.
    [InlineData("<span{{if Value.Length != 0}} title=\"{{Value}}\"{{/if}}>x</span>")]
    // Interpolation after a comment that contains a tag-like fragment.
    [InlineData("<!-- <div class=x --><p>{{Value}}</p>")]
    // '<' that does not open a tag.
    [InlineData("<p>a < b and {{Value}}</p>")]
    public void InterpolationInASafeContext_IsAccepted(string body)
    {
        ImmutableArray<Diagnostic> diagnostics = Run(
            ("templates/page.html", $"<!--#template\n@param string Value\n-->{body}"));

        Assert.Empty(diagnostics);
    }
}
