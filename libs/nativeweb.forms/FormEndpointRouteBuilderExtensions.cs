using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace NativeWeb.Forms;

/// <summary>
/// Maps a route that binds a <see cref="FormAttribute"/> model, with both guards
/// attached by construction.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole public surface of spec 014 on the routing side. What it buys
/// over a hand-written <c>MapPost</c> is two things that fail silently when they are
/// missing, both measured (brainstorm 001 §12):
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>The antiforgery metadata.</b> A route whose model binds itself carries none of
/// its own, so <c>UseAntiforgery()</c> skips the endpoint and a POST with no token
/// at all is answered <b>200 OK</b>. Here it is attached by the mapping call, so
/// there is nothing to forget.
/// </description></item>
/// <item><description>
/// <b>The content-type check.</b> Inside the generated binder, because argument
/// binding runs upstream of the entire endpoint-filter pipeline — a filter cannot
/// protect a read that has already happened.
/// </description></item>
/// </list>
/// <para>
/// <b>The route is a <see cref="RequestDelegate"/>, and the handler takes an
/// <see cref="HttpContext"/> rather than a service list. Both are forced rather than
/// chosen.</b> The <c>Map*(…, Delegate)</c> overloads carry
/// <c>RequiresUnreferencedCode</c> and <c>RequiresDynamicCode</c>, and the Request
/// Delegate Generator only rewrites the ones it can resolve — it cannot resolve a
/// call inside a generic method, so a generic helper written that way fails an AOT
/// build with IL2026 and IL3050 (measured here, 2026-08-30). The
/// <see cref="RequestDelegate"/> overloads carry neither attribute, because there is
/// nothing to bind: the delegate takes the context and reads what it needs. Same
/// reasoning, and the same conclusion, as <c>NativeWeb.Scaffold.ScaffoldEndpoints</c>.
/// </para>
/// </remarks>
public static class FormEndpointRouteBuilderExtensions
{
    /// <summary>Maps a POST that binds <typeparamref name="TForm"/>.</summary>
    /// <typeparam name="TForm">The <see cref="FormAttribute"/> model to bind.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <param name="handler">What to do with the bound form.</param>
    /// <returns>The route, for further configuration.</returns>
    public static IEndpointConventionBuilder MapFormPost<TForm>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<TForm, HttpContext, Task<IResult>> handler)
        where TForm : class, IFormModel<TForm> =>
        MapForm(endpoints, pattern, HttpMethods.Post, handler);

    /// <summary>Maps a PUT that binds <typeparamref name="TForm"/>.</summary>
    /// <inheritdoc cref="MapFormPost{TForm}"/>
    public static IEndpointConventionBuilder MapFormPut<TForm>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<TForm, HttpContext, Task<IResult>> handler)
        where TForm : class, IFormModel<TForm> =>
        MapForm(endpoints, pattern, HttpMethods.Put, handler);

    /// <summary>Maps a PATCH that binds <typeparamref name="TForm"/>.</summary>
    /// <inheritdoc cref="MapFormPost{TForm}"/>
    public static IEndpointConventionBuilder MapFormPatch<TForm>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<TForm, HttpContext, Task<IResult>> handler)
        where TForm : class, IFormModel<TForm> =>
        MapForm(endpoints, pattern, HttpMethods.Patch, handler);

    /// <summary>Maps a DELETE that binds <typeparamref name="TForm"/>.</summary>
    /// <inheritdoc cref="MapFormPost{TForm}"/>
    public static IEndpointConventionBuilder MapFormDelete<TForm>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<TForm, HttpContext, Task<IResult>> handler)
        where TForm : class, IFormModel<TForm> =>
        MapForm(endpoints, pattern, HttpMethods.Delete, handler);

    /// <summary>
    /// The one implementation. Every verb above is the same route with a different
    /// method name, and keeping them one call deep means the guards cannot come off
    /// one of them.
    /// </summary>
    /// <typeparam name="TForm">The <see cref="FormAttribute"/> model to bind.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="handler">What to do with the bound form.</param>
    /// <returns>The route, for further configuration.</returns>
    public static IEndpointConventionBuilder MapForm<TForm>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string method,
        Func<TForm, HttpContext, Task<IResult>> handler)
        where TForm : class, IFormModel<TForm>
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);

        RequestDelegate route = async context =>
        {
            // TForm.BindFormAsync is a static abstract call, resolved at compile time
            // against the concrete type. The 415 is raised inside it, before the form
            // is read.
            TForm form = await TForm.BindFormAsync(context).ConfigureAwait(false);
            IResult result = await handler(form, context).ConfigureAwait(false);
            await result.ExecuteAsync(context).ConfigureAwait(false);
        };

        return endpoints.MapMethods(pattern, [method], route)
            .RequireFormAntiforgery()
            .WithMetadata(new AcceptsMetadata(FormContentTypes));
    }

    // What the route consumes, and it is ordering rather than documentation.
    //
    // ConsumesMatcherPolicy reads this during routing and answers a request whose
    // content type is not on the list with a 415 -- before UseAntiforgery(), and so
    // before a tokenless JSON POST can be answered 400 instead. That is the order the
    // register endpoints already had, because the Request Delegate Generator attaches
    // the same metadata to a route with [FromForm] parameters; measured when it went
    // missing on 2026-08-30, three integration assertions flipped 415 to 400.
    //
    // A request carrying no content type at all still reaches the binder, because the
    // policy only rejects a content type it can see. The 415 for that one comes from
    // FormBinding.RequireFormContentType.
    private static readonly string[] FormContentTypes =
        ["application/x-www-form-urlencoded", "multipart/form-data"];
}
