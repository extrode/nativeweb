using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using NativeWeb.Forms.Generator;

using Xunit;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// The analyzer that closes both silent failures (spec 014 R7).
/// </summary>
/// <remarks>
/// <para>
/// Without it the feature is net-negative. A <c>[Form]</c> model handed to a plain
/// <c>Map*</c> fails twice over, and neither failure says anything. The Request
/// Delegate Generator cannot see the binder this generator emits, so it records
/// <c>hasBindAsync: false</c> and binds the model as a <b>JSON body</b> — measured
/// 2026-08-30, a form POST answered <b>415 by routing</b> before any of our code ran.
/// And the endpoint carries no antiforgery metadata of its own, so
/// <c>UseAntiforgery()</c> skips it and a POST with no token at all is answered
/// <b>200 OK</b> (brainstorm 001 §12).
/// </para>
/// <para>
/// So the rule these facts encode is narrow and total: <c>MapFormPost</c> (or its
/// verb siblings) is the only shape that works, and a <c>[Form]</c> model on any
/// plain <c>Map*</c> is an error whatever else is chained onto it. Every fact is
/// either "the hole is a build error" or "a route that is fine is left alone" — the
/// second half matters as much, because an analyzer that cries wolf gets suppressed.
/// </para>
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
                public static RouteHandlerBuilder MapPatch(this IEndpointRouteBuilder e, string pattern, System.Delegate handler) => new();
                public static RouteHandlerBuilder MapDelete(this IEndpointRouteBuilder e, string pattern, System.Delegate handler) => new();
                public static RouteHandlerBuilder MapMethods(this IEndpointRouteBuilder e, string pattern, string[] methods, System.Delegate handler) => new();
                public static RouteHandlerBuilder Map(this IEndpointRouteBuilder e, string pattern, System.Delegate handler) => new();
                public static RouteHandlerBuilder MapFallback(this IEndpointRouteBuilder e, System.Delegate handler) => new();
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

            public static class FormEndpointRouteBuilderExtensions
            {
                public static Microsoft.AspNetCore.Builder.RouteHandlerBuilder MapFormPost<TForm>(
                    this Microsoft.AspNetCore.Builder.IEndpointRouteBuilder e,
                    string pattern,
                    System.Func<TForm, object, object> handler) => new();
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
    public async Task A_form_route_mapped_the_plain_way_is_a_build_error()
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzeAsync("""
            app.MapPost("/auth/register", (Demo.RegisterForm form) => 0);
            """));

        Assert.Equal("NWF010", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// The message has to name the route, or the author has to guess which of a
    /// dozen registrations in the file it means — and it has to name the fix, which
    /// is not deducible from the defect.
    /// </summary>
    [Fact]
    public async Task The_message_names_the_route_the_model_and_the_fix()
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzeAsync("""
            app.MapPost("/auth/register", (Demo.RegisterForm form) => 0);
            """));

        string message = diagnostic.GetMessage();
        Assert.Contains("/auth/register", message, StringComparison.Ordinal);
        Assert.Contains("Demo.RegisterForm", message, StringComparison.Ordinal);
        Assert.Contains("MapFormPost", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fix, and the only one: <c>MapFormPost</c> binds through the generated
    /// <c>IFormModel</c> implementation and attaches the antiforgery metadata itself.
    /// </summary>
    [Fact]
    public async Task A_route_mapped_with_MapFormPost_reports_nothing()
    {
        Assert.Empty(await AnalyzeAsync("""
            app.MapFormPost<Demo.RegisterForm>("/auth/register", (form, http) => 0);
            """));
    }

    /// <summary>
    /// <b>Attaching the metadata by hand does not make a plain route acceptable, and
    /// this is the fact that changed on 2026-08-30.</b> The missing metadata was never
    /// the whole defect: with it attached the model is still bound as a JSON body, so
    /// a form POST is answered 415 by routing before the token is ever looked at.
    /// </summary>
    [Fact]
    public async Task Attaching_the_metadata_by_hand_does_not_excuse_a_plain_route()
    {
        Assert.Single(await AnalyzeAsync("""
            app.MapPost("/auth/register", (Demo.RegisterForm form) => 0).RequireFormAntiforgery();
            """));
    }

    /// <summary>
    /// Nor does burying it further down a chain, nor splitting it over two statements.
    /// Both were escape hatches under the old rule; neither fixes the binding.
    /// </summary>
    [Fact]
    public async Task Neither_a_longer_chain_nor_a_local_hides_the_route()
    {
        Assert.Single(await AnalyzeAsync("""
            app.MapPost("/chained", (Demo.RegisterForm form) => 0)
               .RequireFormAntiforgery()
               .RequireAuthorization()
               .WithName("register");
            """));

        Assert.Single(await AnalyzeAsync("""
            Microsoft.AspNetCore.Builder.RouteHandlerBuilder route =
                app.MapPost("/local", (Demo.RegisterForm form) => 0);
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
    /// A GET too. The antiforgery half of the defect does not apply to a GET, but the
    /// bound-as-a-JSON-body half does, so the route is broken either way and the
    /// message names the same fix.
    /// </summary>
    [Fact]
    public async Task A_get_route_is_reported_too()
    {
        Assert.Single(await AnalyzeAsync("""
            app.MapGet("/auth/register", (Demo.RegisterForm form) => 0);
            """));
    }

    /// <summary>
    /// Every mapping method the platform offers, including the three the first cut of
    /// the rule missed: <c>MapMethods</c>, the untyped <c>Map</c>, and
    /// <c>MapFallback</c>. A route the analyzer does not match is a route that binds
    /// the model as a JSON body and carries no antiforgery metadata, silently — which
    /// is the whole reason the rule is an error rather than a warning.
    /// </summary>
    [Fact]
    public async Task Every_mapping_method_is_covered()
    {
        Assert.Equal(7, (await AnalyzeAsync("""
            app.MapPost("/a", (Demo.RegisterForm form) => 0);
            app.MapPut("/b", (Demo.RegisterForm form) => 0);
            app.MapPatch("/c", (Demo.RegisterForm form) => 0);
            app.MapDelete("/d", (Demo.RegisterForm form) => 0);
            app.MapGet("/e", (Demo.RegisterForm form) => 0);
            app.MapMethods("/f", new[] { "POST" }, (Demo.RegisterForm form) => 0);
            app.Map("/g", (Demo.RegisterForm form) => 0);
            """)).Length);
    }

    /// <summary>
    /// <c>MapFallback</c> takes no pattern, so the message has no route to name. It is
    /// still a route that binds the model, and the rule still fires.
    /// </summary>
    [Fact]
    public async Task A_fallback_route_is_reported_too()
    {
        Assert.Single(await AnalyzeAsync("""
            app.MapFallback((Demo.RegisterForm form) => 0);
            """));
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
    /// One correct route must not cover for a broken one, which is why the analyzer
    /// reports at the <c>Map*</c> call rather than at the model.
    /// </summary>
    [Fact]
    public async Task One_correct_route_does_not_excuse_a_broken_one()
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzeAsync("""
            app.MapFormPost<Demo.RegisterForm>("/good", (form, http) => 0);
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
