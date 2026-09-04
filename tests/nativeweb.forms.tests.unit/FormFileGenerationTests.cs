using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NativeWeb.Forms.Generator;

using Xunit;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// The file half of the generator, spec 027 R1 and R5.
/// </summary>
/// <remarks>
/// The attributes and <c>UploadedFile</c> are declared inline for the reason
/// <see cref="FormGeneratorTests"/> gives: the generator matches by metadata name, so
/// the test compilation needs nothing but the BCL, and the emitted source is asserted
/// rather than compiled. It is compiled for real by <c>apps/sample</c>, which is what
/// the integration and e2e tiers publish.
/// </remarks>
public sealed class FormFileGenerationTests
{
    private const string Attributes = """
        namespace NativeWeb.Forms
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class FormAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class FormFieldAttribute : System.Attribute
            {
                public FormFieldAttribute(string name) => Name = name;
                public string Name { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class FormFileAttribute : System.Attribute
            {
                public long MaxBytes { get; set; }
                public string[]? Accept { get; set; }
            }

            public sealed class UploadedFile { }
        }
        """;

    [Fact]
    public void A_required_file_field_binds_through_RequiredFile()
    {
        (_, string emitted) = Run(Attributes, Model("[FormFile(MaxBytes = 1024)] UploadedFile Avatar"));

        Assert.Contains("FormBinding.RequiredFile(form, \"avatar\", null, 1024L)", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void An_optional_file_field_binds_through_File()
    {
        (_, string emitted) = Run(Attributes, Model("[FormFile(MaxBytes = 1024)] UploadedFile? Avatar"));

        Assert.Contains("FormBinding.File(form, \"avatar\", null, 1024L)", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiredFile", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void The_declared_accept_set_reaches_the_binder()
    {
        (_, string emitted) = Run(
            Attributes,
            Model("[FormFile(MaxBytes = 1024, Accept = new[] { \"image/png\", \"image/jpeg\" })] UploadedFile Avatar"));

        Assert.Contains(
            "new string[] { \"image/png\", \"image/jpeg\" }", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void The_declared_ceiling_bounds_the_body_before_the_read()
    {
        (_, string emitted) = Run(Attributes, Model("[FormFile(MaxBytes = 4096)] UploadedFile Avatar"));

        int limit = emitted.IndexOf("LimitBody(context, MaxBodyBytes)", StringComparison.Ordinal);
        int read = emitted.IndexOf("ReadLimitedFormAsync", StringComparison.Ordinal);

        Assert.Contains("public static long MaxBodyBytes => 69632L;", emitted, StringComparison.Ordinal);
        Assert.True(limit >= 0, "the declared ceiling was not emitted");
        Assert.True(read >= 0, "the limited read was not emitted");
        Assert.True(limit < read, "the ceiling must be installed before the form is read");
    }

    /// <summary>
    /// One body carries every part at once, so the ceiling is the sum of the declared
    /// field limits and not the largest of them: at the largest, a post carrying both
    /// files within their own limits is answered 413.
    /// </summary>
    [Fact]
    public void Two_file_fields_bound_the_body_by_their_sum()
    {
        (_, string emitted) = Run(
            Attributes,
            Model("[FormFile(MaxBytes = 8192)] UploadedFile Avatar, [FormFile(MaxBytes = 1024)] UploadedFile? Banner"));

        Assert.Contains("public static long MaxBodyBytes => 74752L;", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Zero means "the host's own limit is the right one", which is what MapForm reads
    /// to decide whether to attach the size-limit metadata at all. A text-only form has
    /// no size of its own to declare and must not raise the host ceiling.
    /// </summary>
    [Fact]
    public void A_text_only_model_declares_no_body_ceiling()
    {
        (_, string emitted) = Run(Attributes, Model("string? Username"));

        Assert.Contains("public static long MaxBodyBytes => 0L;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void The_multipart_guard_is_emitted_only_for_a_model_carrying_a_file()
    {
        (_, string withFile) = Run(Attributes, Model("[FormFile(MaxBytes = 1024)] UploadedFile Avatar"));
        (_, string textOnly) = Run(Attributes, Model("string? Username"));

        Assert.Contains("RequireMultipart(context)", withFile, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireMultipart", textOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("LimitBody", textOnly, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pre-027 guard still runs first on a file model. It admits urlencoded, which
    /// is why RequireMultipart follows it rather than replacing it: a JSON body has to
    /// be answered by the guard that names the whole content-type family.
    /// </summary>
    [Fact]
    public void The_form_content_type_guard_still_runs_first()
    {
        (_, string emitted) = Run(Attributes, Model("[FormFile(MaxBytes = 1024)] UploadedFile Avatar"));

        int form = emitted.IndexOf("RequireFormContentType", StringComparison.Ordinal);
        int multipart = emitted.IndexOf("RequireMultipart", StringComparison.Ordinal);

        Assert.True(form >= 0 && multipart > form);
    }

    [Fact]
    public void A_file_field_and_a_text_field_are_both_bound()
    {
        (_, string emitted) = Run(
            Attributes,
            Model("string? Caption, [FormFile(MaxBytes = 1024)] UploadedFile Avatar"));

        Assert.Contains("FormBinding.Field(form, \"caption\")", emitted, StringComparison.Ordinal);
        Assert.Contains("FormBinding.RequiredFile(form, \"avatar\", null, 1024L)", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_renamed_file_field_reads_the_declared_wire_name()
    {
        (_, string emitted) = Run(
            Attributes,
            Model("[FormField(\"profile_picture\"), FormFile(MaxBytes = 1024)] UploadedFile Avatar"));

        Assert.Contains("\"profile_picture\"", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_field_with_no_FormFile_attribute_is_NWF006()
    {
        AssertOnlyDiagnostic("NWF006", Run(Attributes, Model("UploadedFile Avatar")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_ceiling_of_zero_or_less_is_NWF006(string maxBytes)
    {
        AssertOnlyDiagnostic(
            "NWF006", Run(Attributes, Model("[FormFile(MaxBytes = " + maxBytes + ")] UploadedFile Avatar")));
    }

    [Fact]
    public void An_empty_accept_entry_is_NWF006()
    {
        AssertOnlyDiagnostic(
            "NWF006",
            Run(Attributes, Model("[FormFile(MaxBytes = 1024, Accept = new[] { \"image/png\", \"  \" })] UploadedFile Avatar")));
    }

    [Fact]
    public void A_FormFile_attribute_on_a_text_member_is_NWF006()
    {
        AssertOnlyDiagnostic(
            "NWF006", Run(Attributes, Model("[FormFile(MaxBytes = 1024)] string? Username")));
    }

    /// <summary>
    /// A member that is neither a string nor a file is still NWF003, whose message
    /// names <c>string?</c>. Widening the bindable set to include files must not
    /// change what an <c>int</c> is told.
    /// </summary>
    [Fact]
    public void An_unbindable_member_type_is_still_NWF003()
    {
        AssertOnlyDiagnostic("NWF003", Run(Attributes, Model("int Count")));
    }

    [Fact]
    public void A_model_rejected_by_NWF006_emits_nothing()
    {
        (_, string emitted) = Run(Attributes, Model("UploadedFile Avatar"));

        Assert.Equal("", emitted);
    }

    /// <summary>
    /// A wire name is an arbitrary string constant, and a quote in one used to reach
    /// the emitted literal unescaped: <c>SymbolDisplay.FormatLiteral(value, quote:
    /// false)</c> escapes the backslash and the newline and leaves the quote alone,
    /// measured 2026-09-04.
    /// </summary>
    /// <param name="wireName">The declared name.</param>
    [Theory]
    [InlineData("a\\\"b")]
    [InlineData("a\\b")]
    [InlineData("a\nb")]
    public void A_wire_name_carrying_a_delimiter_still_emits_parseable_source(string wireName)
    {
        (_, string emitted) = Run(
            Attributes,
            Model("[FormField(\"" + wireName + "\")] string? Caption"));

        Assert.DoesNotContain(
            CSharpSyntaxTree.ParseText(emitted).GetDiagnostics(),
            d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// The emitter ends in <c>new T(...)</c>, so an abstract model would fail as CS0144
    /// inside generated source the author never wrote. NWF005 rejects it at the
    /// declaration instead.
    /// </summary>
    [Fact]
    public void An_abstract_model_is_refused_by_NWF005()
    {
        AssertOnlyDiagnostic(
            "NWF005",
            Run(
                Attributes,
                """
                using NativeWeb.Forms;

                namespace Demo
                {
                    [Form]
                    public abstract partial record UploadForm(string? Caption);
                }
                """));
    }

    private static string Model(string parameters) => """
        using NativeWeb.Forms;

        namespace Demo
        {
            [Form]
            public sealed partial record UploadForm(PARAMETERS);
        }
        """.Replace("PARAMETERS", parameters, StringComparison.Ordinal);

    private static void AssertOnlyDiagnostic(
        string id,
        (ImmutableArray<Diagnostic> Diagnostics, string Source) result)
    {
        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string Source) Run(params string[] sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "form-file-generator-tests",
            syntaxTrees: sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new FormGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        string emitted = string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

        return (diagnostics, emitted);
    }
}
