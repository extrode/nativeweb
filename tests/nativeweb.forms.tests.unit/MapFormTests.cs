using System.Text;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

using Xunit;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// The form model these facts bind. Its binder is emitted by the generator, so this
/// tier exercises the generated code and the mapping helper together — the same two
/// halves the integration tier drives through Kestrel.
/// </summary>
[Form]
public sealed partial record Contact(
    string? Name,
    [FormField("email")] string? Address);

/// <summary>
/// <c>MapFormPost</c> and its siblings: the whole routing surface of spec 014.
/// </summary>
/// <remarks>
/// <para>
/// What these hold is that the two guards are attached <b>by the mapping call</b> and
/// cannot be left off. Both failures they prevent are silent, and both were measured:
/// a route with no antiforgery metadata answers a tokenless POST 200 OK (§12), and a
/// model bound by RDG instead of by <c>MapFormPost</c> is treated as a JSON body, so a
/// form POST is answered 415 by routing before any handler runs (2026-08-30).
/// </para>
/// <para>
/// The endpoint's <see cref="RequestDelegate"/> is invoked directly rather than over a
/// socket. That is the unit-level half on purpose: it reaches the binder and the guard
/// with no server in the way, and <c>DeclarativeFormTests</c> covers the same routes
/// through the real pipeline where the middleware is what answers.
/// </para>
/// </remarks>
public sealed class MapFormTests
{
    [Fact]
    public async Task A_mapped_form_route_binds_every_declared_field()
    {
        Contact? bound = null;

        HttpContext context = await InvokeAsync(
            app => app.MapFormPost<Contact>("/c", (form, _) =>
            {
                bound = form;
                return Task.FromResult(Results.Text("ok"));
            }),
            Form(("Name", "Ada"), ("email", "ada@example.com")));

        Assert.NotNull(bound);
        Assert.Equal("Ada", bound.Name);
        Assert.Equal("ada@example.com", bound.Address);
        Assert.Equal(200, context.Response.StatusCode);
    }

    /// <summary>
    /// R4: a field the model does not declare cannot reach the handler. The binder
    /// reads the declared members by name and nothing else, so an extra field is not
    /// merely ignored — there is nowhere for it to go.
    /// </summary>
    [Fact]
    public async Task A_field_the_model_does_not_declare_is_not_read()
    {
        Contact? bound = null;

        await InvokeAsync(
            app => app.MapFormPost<Contact>("/c", (form, _) =>
            {
                bound = form;
                return Task.FromResult(Results.Text("ok"));
            }),
            Form(("Name", "Ada"), ("isAdmin", "true")));

        Assert.NotNull(bound);
        Assert.Equal("Ada", bound.Name);
        Assert.Null(bound.Address);
    }

    /// <summary>
    /// A member the request does not carry binds to null rather than failing. A form
    /// is a user-supplied bag of strings; a missing field is a validation question,
    /// not a binding error.
    /// </summary>
    [Fact]
    public async Task A_missing_field_binds_to_null()
    {
        Contact? bound = null;

        await InvokeAsync(
            app => app.MapFormPost<Contact>("/c", (form, _) =>
            {
                bound = form;
                return Task.FromResult(Results.Text("ok"));
            }),
            Form(("Name", "Ada")));

        Assert.NotNull(bound);
        Assert.Null(bound.Address);
    }

    /// <summary>
    /// The 415, raised inside the binder because argument binding runs upstream of the
    /// whole endpoint-filter pipeline — a filter cannot protect a read that has already
    /// happened.
    /// </summary>
    [Fact]
    public async Task A_request_that_carries_no_form_is_rejected_before_the_handler_runs()
    {
        bool handlerRan = false;

        BadHttpRequestException thrown = await Assert.ThrowsAsync<BadHttpRequestException>(
            () => InvokeAsync(
                app => app.MapFormPost<Contact>("/c", (_, _) =>
                {
                    handlerRan = true;
                    return Task.FromResult(Results.Text("ok"));
                }),
                context =>
                {
                    context.Request.ContentType = "application/json";
                    context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
                }));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, thrown.StatusCode);
        Assert.False(handlerRan);
    }

    /// <summary>
    /// Attached by the mapping call, so an application using <c>MapFormPost</c> never
    /// writes <c>RequireFormAntiforgery</c> and cannot forget it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryVerb))]
    public void Every_mapped_form_route_carries_the_antiforgery_metadata(string method)
    {
        Endpoint endpoint = Map(app => MapVerb(app, method, "/c"));

        IAntiforgeryMetadata? metadata = endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>();

        Assert.NotNull(metadata);
        Assert.True(metadata.RequiresValidation);
    }

    [Theory]
    [MemberData(nameof(EveryVerb))]
    public void Each_verb_maps_that_verb_and_no_other(string method)
    {
        Endpoint endpoint = Map(app => MapVerb(app, method, "/c"));

        HttpMethodMetadata? methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.NotNull(methods);
        Assert.Equal([method], methods.HttpMethods);
    }

    /// <summary>
    /// <b>The metadata that decides which rejection wins.</b> Routing's
    /// <c>ConsumesMatcherPolicy</c> reads it and answers a JSON POST 415 before
    /// <c>UseAntiforgery()</c> runs, so a tokenless JSON POST is a 415 and not a 400.
    /// Measured 2026-08-30: without it, three of the register endpoints' existing
    /// integration assertions flipped from 415 to 400.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryVerb))]
    public void Every_mapped_form_route_declares_what_it_consumes(string method)
    {
        Endpoint endpoint = Map(app => MapVerb(app, method, "/c"));

        IAcceptsMetadata? accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();

        Assert.NotNull(accepts);
        Assert.Equal(
            ["application/x-www-form-urlencoded", "multipart/form-data"],
            accepts.ContentTypes);
        Assert.False(accepts.IsOptional);
    }

    public static TheoryData<string> EveryVerb() =>
        new() { HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete };

    [Fact]
    public void A_null_route_builder_is_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(
            () => FormEndpointRouteBuilderExtensions.MapFormPost<Contact>(
                null!, "/c", (_, _) => Task.FromResult(Results.Text("ok"))));
    }

    [Fact]
    public void A_null_handler_is_a_programming_error()
    {
        WebApplication app = WebApplication.CreateBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => app.MapFormPost<Contact>("/c", null!));
    }

    private static IEndpointConventionBuilder MapVerb(
        IEndpointRouteBuilder app,
        string method,
        string pattern)
    {
        Func<Contact, HttpContext, Task<IResult>> handler =
            (_, _) => Task.FromResult(Results.Text("ok"));

        return method switch
        {
            "POST" => app.MapFormPost(pattern, handler),
            "PUT" => app.MapFormPut(pattern, handler),
            "PATCH" => app.MapFormPatch(pattern, handler),
            "DELETE" => app.MapFormDelete(pattern, handler),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unmapped verb."),
        };
    }

    private static Endpoint Map(Action<IEndpointRouteBuilder> configure)
    {
        WebApplication app = WebApplication.CreateBuilder().Build();
        configure(app);

        EndpointDataSource source = Assert.Single(((IEndpointRouteBuilder)app).DataSources);
        return Assert.Single(source.Endpoints);
    }

    private static async Task<HttpContext> InvokeAsync(
        Action<IEndpointRouteBuilder> configure,
        Action<HttpContext> request)
    {
        WebApplication app = WebApplication.CreateBuilder().Build();
        configure(app);

        EndpointDataSource source = Assert.Single(((IEndpointRouteBuilder)app).DataSources);
        Endpoint endpoint = Assert.Single(source.Endpoints);

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = new MemoryStream();
        request(context);

        Assert.NotNull(endpoint.RequestDelegate);

        // Awaited rather than continued: the 415 surfaces as an exception out of the
        // binder, and a continuation would turn it into a cancellation and hide it.
        await endpoint.RequestDelegate(context);
        return context;
    }

    // A urlencoded body, written the way a browser writes one, so the binder is
    // reading a real form rather than a pre-built IFormCollection handed to it.
    private static Action<HttpContext> Form(params (string Name, string Value)[] fields) =>
        context =>
        {
            string body = string.Join(
                "&",
                fields.Select(f => Uri.EscapeDataString(f.Name) + "=" + Uri.EscapeDataString(f.Value)));

            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
        };
}
