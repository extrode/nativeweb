using Microsoft.AspNetCore.Http;

namespace NativeWeb.Scaffold;

/// <summary>
/// The two helpers the generated route table is built from.
/// </summary>
/// <remarks>
/// <para><b>Why the generated endpoints are <see cref="RequestDelegate"/>s and not
/// route handlers.</b> Measured 2026-08-19: a NativeAOT publish of
/// <c>apps/sample</c> failed with IL2026 and IL3050 on every generated
/// <c>MapGet</c>/<c>MapPost</c>. The Request Delegate Generator only rewrites
/// <c>Map*</c> calls it can see in the compilation's <i>own</i> syntax trees, and
/// Roslyn does not feed one generator's output to another. So a <c>Map*</c> call
/// emitted by this generator always binds the reflection-based
/// <c>Map*(IEndpointRouteBuilder, string, Delegate)</c> overload, which carries
/// <c>RequiresUnreferencedCode</c> and <c>RequiresDynamicCode</c> and fails the
/// build under <c>TreatWarningsAsErrors</c>.</para>
/// <para>The <c>RequestDelegate</c> overloads carry neither attribute, because
/// there is nothing to bind: the delegate takes the <see cref="HttpContext"/> and
/// reads what it needs. The generator was already doing its own binding through
/// <c>IBindableFromHttpContext</c>, so it needs nothing the RDG offers. This is
/// the general lesson for any NativeWeb generator that emits endpoints, not a
/// quirk of scaffolding.</para>
/// </remarks>
public static class ScaffoldEndpoints
{
    /// <summary>Adapts a page handler to a <see cref="RequestDelegate"/>.</summary>
    public static RequestDelegate Handle(Func<HttpContext, Task<IResult>> handler) =>
        async context =>
        {
            IResult result = await handler(context).ConfigureAwait(false);
            await result.ExecuteAsync(context).ConfigureAwait(false);
        };

    /// <summary>
    /// A route value by name, or the empty string. Empty rather than null because
    /// every caller passes it straight to a store lookup, and "no such row" is the
    /// answer either way.
    /// </summary>
    public static string RouteValue(HttpContext context, string name) =>
        context.Request.RouteValues.TryGetValue(name, out object? value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;
}
