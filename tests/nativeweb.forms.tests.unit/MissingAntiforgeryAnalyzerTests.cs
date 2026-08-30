using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using NativeWeb.Forms.Generator;

using Xunit;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// The analyzer that closes §12's foot-gun (spec 014 R7).
/// </summary>
/// <remarks>
/// Without it the feature is net-negative: a route bound through
/// <c>IBindableFromHttpContext</c> compiles clean, binds every field, and answers
/// <b>200 OK to a form POST with no antiforgery token</b>. Every fact here is either
/// "the hole is a build error" or "a route that is fine is left alone" — the second
/// half matters as much, because an analyzer that cries wolf gets suppressed.
/// </remarks>
public sealed class MissingAntiforgeryAnalyzerTests
{
    // The routing surface the analyzer matches on, by name. Stubbed rather than
    // referenced so the test compilation needs nothing but the BCL.
    private const string Routing = """
        namespace Microsoft.AspNetCore.Builder
        {
            public interface IEndpointRouteBuilder { }

            public class RouteHandlerBuilder { }

            public static class EndpointRouteBuilderExtensions
            {
                public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder e, string pattern, System.Delegate handler) => new();
                public static RouteHandlerBuilder MapPut(this IEndpointRouteBuilder e, string pattern, System.Delegate handler) => new();
                public static RouteHandlerBuilder MapGet(this IEndpointRouteBuilder e, string pattern, System.Delegate handler) => new();
                public static RouteHandlerBuilder RequireAuthorization(this RouteHandlerBuilder b) => b;
                public static RouteHandlerBuilder WithName(this RouteHandlerBuilder b, string name) => b;
            }
        }

        namespace NativeWeb.Forms
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class FormAttribute : System.Attribute { }

            public static class FormRouteBuilderExtensions
            {
                public static Microsoft.AspNetCore.Builder.RouteHandlerBuilder RequireFormAntiforgery(
                    this Microsoft.AspNetCore.Builder.RouteHandlerBuilder b) => b;
            }
        }

        namespace Demo
        {
            [NativeWeb.Forms.Form]
            public sealed partial record RegisterForm(string? Username);

            public sealed record PlainRequest(string? Username);
        }
        """;

    [Fact]
    public async Task A_form_route_without_the_metadata_is_a_build_error()
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzeAsync("""
            app.MapPost("/auth/register", (Demo.RegisterForm form) => 0);
            """));

        Assert.Equal("NWF010", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// The message has to name the route, or the author has to guess which of a
    /// dozen registrations in the file it means.
    /// </summary>
    [Fact]
    public async Task The_message_names_the_route_and_the_model()
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzeAsync("""
            app.MapPost("/auth/register", (Demo.RegisterForm form) => 0);
            """));

        string message = diagnostic.GetMessage();
        Assert.Contains("/auth/register", message, StringComparison.Ordinal);
        Assert.Contains("Demo.RegisterForm", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_route_that_requires_it_reports_nothing()
    {
        Assert.Empty(await AnalyzeAsync("""
            app.MapPost("/auth/register", (Demo.RegisterForm form) => 0).RequireFormAntiforgery();
            """));
    }

    /// <summary>
    /// The call does not have to be last in the chain, and normally is not.
    /// </summary>
    [Fact]
    public async Task The_metadata_call_may_sit_anywhere_in_the_chain()
    {
        Assert.Empty(await AnalyzeAsync("""
            app.MapPost("/auth/register", (Demo.RegisterForm form) => 0)
               .RequireFormAntiforgery()
               .RequireAuthorization()
               .WithName("register");
            """));
    }

    /// <summary>
    /// Written over two statements, this is the same endpoint. Reporting it would be
    /// a wrong build error, which is the failure mode that gets an analyzer disabled.
    /// </summary>
    [Fact]
    public async Task A_route_held_in_a_local_and_configured_after_reports_nothing()
    {
        Assert.Empty(await AnalyzeAsync("""
            Microsoft.AspNetCore.Builder.RouteHandlerBuilder route =
                app.MapPost("/auth/register", (Demo.RegisterForm form) => 0);
            route.RequireFormAntiforgery();
            """));
    }

    [Fact]
    public async Task A_route_that_binds_no_form_model_is_not_the_analyzer_s_business()
    {
        Assert.Empty(await AnalyzeAsync("""
            app.MapPost("/api/thing", (Demo.PlainRequest body) => 0);
            """));
    }

    /// <summary>
    /// A form model on a GET is a different mistake. Reporting it as missing
    /// antiforgery would name the wrong problem.
    /// </summary>
    [Fact]
    public async Task A_get_route_is_not_reported()
    {
        Assert.Empty(await AnalyzeAsync("""
            app.MapGet("/auth/register", (Demo.RegisterForm form) => 0);
            """));
    }

    [Fact]
    public async Task Every_unsafe_verb_is_covered()
    {
        Assert.Equal(2, (await AnalyzeAsync("""
            app.MapPost("/a", (Demo.RegisterForm form) => 0);
            app.MapPut("/b", (Demo.RegisterForm form) => 0);
            """)).Length);
    }

    /// <summary>
    /// The model may be any parameter, not the first. A handler normally takes the
    /// form alongside an HttpContext and its services.
    /// </summary>
    [Fact]
    public async Task The_form_is_found_when_it_is_not_the_first_parameter()
    {
        Assert.Single(await AnalyzeAsync("""
            app.MapPost("/auth/register", (string other, Demo.RegisterForm form) => 0);
            """));
    }

    [Fact]
    public async Task A_method_group_handler_is_read_the_same_way()
    {
        Assert.Single(await AnalyzeAsync(
            """
            app.MapPost("/auth/register", Handle);
            """,
            "    private static int Handle(Demo.RegisterForm form) => 0;"));
    }

    /// <summary>
    /// One good route must not cover for a bad one, which is why the analyzer reports
    /// at the <c>Map*</c> call rather than at the model.
    /// </summary>
    [Fact]
    public async Task One_guarded_route_does_not_excuse_an_unguarded_one()
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzeAsync("""
            app.MapPost("/good", (Demo.RegisterForm form) => 0).RequireFormAntiforgery();
            app.MapPost("/bad", (Demo.RegisterForm form) => 0);
            """));

        Assert.Contains("/bad", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string body,
        string extraMembers = "")
    {
        string program = $$"""
            using Microsoft.AspNetCore.Builder;
            using NativeWeb.Forms;

            namespace Consumer
            {
                public static class Routes
                {
                    public static void Map(IEndpointRouteBuilder app)
                    {
            {{Indent(body)}}
                    }

            {{extraMembers}}
                }
            }
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "form-analyzer-tests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(Routing), CSharpSyntaxTree.ParseText(program)],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // A compile error would make any analyzer result meaningless — the operation
        // tree for a call that does not bind is not the tree the analyzer will see in
        // a real build.
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        CompilationWithAnalyzers analyzed = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MissingAntiforgeryAnalyzer()));

        return await analyzed.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }

    private static string Indent(string body) =>
        string.Join("\n", body.Split('\n').Select(line => "            " + line.TrimEnd('\r')));
}
