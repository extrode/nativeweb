using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace NativeWeb.Scaffold;

/// <summary>
/// Pipeline wiring for scaffolding.
/// </summary>
public static class ScaffoldApplicationBuilderExtensions
{
    /// <summary>
    /// The middleware pipeline scaffolded routes expect, in the order they expect it.
    /// </summary>
    /// <remarks>
    /// <para><b>This method hides nothing.</b> It is exactly four calls, every one of
    /// them public and individually callable:</para>
    /// <code>
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseAntiforgery();
    /// app.UseAntiforgeryRejection();
    /// </code>
    /// <para>Write those four instead whenever the pipeline needs anything between
    /// them — a rate limiter, a tenant resolver, a request logger. The one line is a
    /// convenience for the common case, not a seam you have to go through.</para>
    /// <para>The order is the load-bearing part. Antiforgery reads endpoint metadata,
    /// so it must run after routing; the rejection guard reads what antiforgery
    /// recorded, so it must run after antiforgery.</para>
    /// </remarks>
    public static IApplicationBuilder UseScaffolding(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseAntiforgeryRejection();

        return app;
    }

    /// <summary>
    /// Turns a failed antiforgery validation into a 400, which
    /// <see cref="AntiforgeryApplicationBuilderExtensions.UseAntiforgery"/> does not
    /// do on its own.
    /// </summary>
    /// <remarks>
    /// <para>Measured, and the reason this exists: <c>UseAntiforgery()</c> validates
    /// but does not reject. It records the failure on
    /// <see cref="IAntiforgeryValidationFeature"/> and calls the next middleware
    /// anyway. The request then reaches the endpoint, the form reader throws a bare
    /// <see cref="InvalidOperationException"/>, and the caller gets a 500 where a 400
    /// belongs — an error that reads like a server fault for what is a rejected
    /// request.</para>
    /// <para>Must be registered after <c>UseAntiforgery()</c> and before the
    /// endpoints. Safe to register unconditionally: with no antiforgery middleware
    /// ahead of it the feature is absent and this is a no-op.</para>
    /// <para><b>A twin lives at <c>apps/web/Middleware/AntiforgeryValidationMiddleware.cs</c></b>,
    /// and it is kept separate on purpose (reviewed 2026-08-25): the identity provider
    /// would otherwise have to reference this CRUD-scaffolding library for one
    /// extension method. That file carries the full reasoning. Behaviour changes —
    /// a response body, a log entry, problem-details — belong in both, because nothing
    /// fails when they drift.</para>
    /// </remarks>
    public static IApplicationBuilder UseAntiforgeryRejection(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(static async (context, next) =>
        {
            if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false })
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }
}
