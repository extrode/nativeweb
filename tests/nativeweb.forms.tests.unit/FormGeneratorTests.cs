using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NativeWeb.Forms.Generator;

using Xunit;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// Drives the form generator directly and asserts on what it emits.
/// </summary>
/// <remarks>
/// <para>
/// Emitted source is the right layer for what matters here. That the content-type
/// check is the <b>first</b> statement, and that only declared members are read, are
/// properties of every model the generator will ever see; an integration test can
/// only check the one model it happens to have.
/// </para>
/// <para>
/// The attributes are declared inline rather than referenced, so the test
/// compilation needs nothing but the BCL — the generator matches by metadata name,
/// not by assembly identity. That also means the emitted code is not compiled here:
/// it references ASP.NET Core types this compilation does not have. Compiling it is
/// T7's job (<c>apps/sample</c>, through a real AOT publish), which is the gate
/// §14's failure was invisible to <c>dotnet build</c> for.
/// </para>
/// </remarks>
public sealed class FormGeneratorTests
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
        }
        """;

    private const string RegisterModel = """
        using NativeWeb.Forms;

        namespace Demo
        {
            [Form]
            public sealed partial record RegisterForm(
                string? FirstName,
                string? Username,
                [FormField("confirmPassphrase")] string? Confirm);
        }
        """;

    [Fact]
    public void A_well_formed_model_reports_nothing()
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Attributes, RegisterModel);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// <c>IFormModel</c>, and NOT the platform's <c>IBindableFromHttpContext</c>.
    /// Measured 2026-08-30: the Request Delegate Generator runs in the same pass and
    /// cannot see this partial, so it recorded <c>hasBindAsync: false</c> and bound
    /// the model as a JSON body — a form POST was then answered 415 by routing.
    /// </summary>
    [Fact]
    public void The_emitted_partial_implements_the_binding_interface()
    {
        (_, string emitted) = Run(Attributes, RegisterModel);

        Assert.Contains(
            "partial record RegisterForm : global::NativeWeb.Forms.IFormModel<RegisterForm>",
            emitted,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IBindableFromHttpContext", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// R2, and the ordering is the requirement. Binding runs upstream of the whole
    /// endpoint-filter pipeline, so a check that lands after the read is not a check.
    /// </summary>
    [Fact]
    public void The_content_type_check_precedes_the_form_read()
    {
        (_, string emitted) = Run(Attributes, RegisterModel);

        int guard = emitted.IndexOf("FormBinding.RequireFormContentType(context)", StringComparison.Ordinal);
        int read = emitted.IndexOf("ReadFormAsync", StringComparison.Ordinal);

        Assert.True(guard >= 0, "the emitted binder does not call the content-type guard");
        Assert.True(read >= 0, "the emitted binder does not read the form");
        Assert.True(guard < read, "the content-type guard is emitted after the form read");
    }

    [Fact]
    public void Every_declared_member_is_bound_by_its_wire_name()
    {
        (_, string emitted) = Run(Attributes, RegisterModel);

        Assert.Contains("FirstName: global::NativeWeb.Forms.FormBinding.Field(form, \"firstName\")", emitted, StringComparison.Ordinal);
        Assert.Contains("Username: global::NativeWeb.Forms.FormBinding.Field(form, \"username\")", emitted, StringComparison.Ordinal);
    }

    /// <summary>The wire name a <c>[FormField]</c> gives wins over the convention.</summary>
    [Fact]
    public void A_form_field_attribute_overrides_the_derived_name()
    {
        (_, string emitted) = Run(Attributes, RegisterModel);

        Assert.Contains("Confirm: global::NativeWeb.Forms.FormBinding.Field(form, \"confirmPassphrase\")", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("\"confirm\")", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// R4. The declaration is the contract: a field the model does not name is never
    /// read, so it cannot reach the handler.
    /// </summary>
    [Fact]
    public void An_undeclared_field_is_not_read()
    {
        (_, string emitted) = Run(Attributes, RegisterModel);

        Assert.DoesNotContain("isAdmin", emitted, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, CountOf(emitted, "FormBinding.Field(form,"));
    }

    /// <summary>
    /// R6. §14 measured that the Request Delegate Generator does not run over another
    /// generator's output, so an emitted <c>Map*</c> binds the reflection overload and
    /// fails an AOT publish. This generator avoids that by emitting no route at all.
    /// </summary>
    [Fact]
    public void Nothing_emitted_registers_a_route()
    {
        // Comments stripped first: the emitted doc names MapFormPost as the caller,
        // and a substring match would read that as a route registration.
        string code = WithoutComments(Run(Attributes, RegisterModel).Source);

        Assert.DoesNotContain("MapPost", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MapFormPost", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGroup", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IEndpointRouteBuilder", code, StringComparison.Ordinal);
    }

    private static string WithoutComments(string emitted) =>
        string.Join(
            "\n",
            emitted.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void A_model_in_the_global_namespace_still_emits()
    {
        const string source = """
            using NativeWeb.Forms;

            [Form]
            public sealed partial record Bare(string? Name);
            """;

        (ImmutableArray<Diagnostic> diagnostics, string emitted) = Run(Attributes, source);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain("namespace ", emitted, StringComparison.Ordinal);
        Assert.Contains("partial record Bare", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_record_class_emits_a_partial_class()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed partial class Plain
                {
                    public Plain(string? name) => Name = name;
                    public string? Name { get; }
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, string emitted) = Run(Attributes, source);

        Assert.Empty(diagnostics);
        Assert.Contains("partial class Plain", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_model_with_no_fields_still_binds()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed partial class Empty
                {
                    public Empty() { }
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, string emitted) = Run(Attributes, source);

        Assert.Empty(diagnostics);
        Assert.Contains("return new Empty();", emitted, StringComparison.Ordinal);
        // The guard is not optional just because there is nothing to read.
        Assert.Contains("RequireFormContentType(context)", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_models_do_not_collide_on_a_hint_name()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace A { [Form] public sealed partial record Same(string? X); }
            namespace B { [Form] public sealed partial record Same(string? X); }
            """;

        (ImmutableArray<Diagnostic> diagnostics, string emitted) = Run(Attributes, source);

        Assert.Empty(diagnostics);
        Assert.Equal(2, CountOf(emitted, "partial record Same"));
    }

    // --- R8: a malformed model diagnoses rather than emitting broken code. ---

    [Fact]
    public void A_model_that_is_not_partial_reports_NWF001()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed record Sealed(string? Name);
            }
            """;

        AssertOnlyDiagnostic("NWF001", Run(Attributes, source));
    }

    [Fact]
    public void A_model_with_two_constructors_reports_NWF002()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed partial class Ambiguous
                {
                    public Ambiguous(string? a) { }
                    public Ambiguous(string? a, string? b) { }
                }
            }
            """;

        AssertOnlyDiagnostic("NWF002", Run(Attributes, source));
    }

    [Fact]
    public void A_model_with_no_public_constructor_reports_NWF002()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed partial class Hidden
                {
                    private Hidden(string? a) { }
                }
            }
            """;

        AssertOnlyDiagnostic("NWF002", Run(Attributes, source));
    }

    [Theory]
    [InlineData("int Count")]
    [InlineData("string Name")]
    [InlineData("System.DateTime When")]
    [InlineData("bool? Agreed")]
    public void A_member_that_is_not_a_nullable_string_reports_NWF003(string member)
    {
        string source = $$"""
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed partial record Wrong({{member}});
            }
            """;

        AssertOnlyDiagnostic("NWF003", Run(Attributes, source));
    }

    [Fact]
    public void Two_members_binding_one_field_report_NWF004()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed partial record Collide(
                    string? Name,
                    [FormField("name")] string? Other);
            }
            """;

        AssertOnlyDiagnostic("NWF004", Run(Attributes, source));
    }

    [Fact]
    public void A_nested_model_reports_NWF005()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                public static partial class Outer
                {
                    [Form]
                    public sealed partial record Inner(string? Name);
                }
            }
            """;

        AssertOnlyDiagnostic("NWF005", Run(Attributes, source));
    }

    [Fact]
    public void A_generic_model_reports_NWF005()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed partial record Boxed<T>(string? Name);
            }
            """;

        AssertOnlyDiagnostic("NWF005", Run(Attributes, source));
    }

    /// <summary>
    /// A diagnosed model must produce no source at all. Emitting a half-built binder
    /// would bury the real message under compiler errors in a file nobody wrote.
    /// </summary>
    [Fact]
    public void A_diagnosed_model_emits_nothing()
    {
        const string source = """
            using NativeWeb.Forms;

            namespace Demo
            {
                [Form]
                public sealed record Sealed(string? Name);
            }
            """;

        (_, string emitted) = Run(Attributes, source);

        Assert.Equal("", emitted);
    }

    private static void AssertOnlyDiagnostic(
        string id,
        (ImmutableArray<Diagnostic> Diagnostics, string Source) result)
    {
        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0;
        int at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string Source) Run(params string[] sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "form-generator-tests",
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
