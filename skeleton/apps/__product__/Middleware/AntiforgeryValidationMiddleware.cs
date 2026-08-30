using Microsoft.AspNetCore.Antiforgery;

namespace __PRODUCT__.Middleware;

// Turns a failed antiforgery check into a 400 before the endpoint ever runs.
//
// UseAntiforgery() does NOT reject a bad token itself: it validates, catches its
// own AntiforgeryValidationException, records the outcome on
// IAntiforgeryValidationFeature, and calls the next middleware anyway. The
// contract is that whatever reads the form afterwards checks that feature first.
// The RDG-generated argument binder does not -- it goes straight to
// Request.HasFormContentType, and FormFeature then throws a bare
// InvalidOperationException ("This form is being accessed with an invalid
// anti-forgery token"). That escapes as an unhandled 500.
//
// Measured, not assumed: without this middleware a form POST with no token
// returns 500 instead of 400. It cannot be an endpoint filter -- argument binding
// runs before filters do, so the throw happens upstream of every filter on the
// route.
//
// Catching InvalidOperationException in ErrorPageMiddleware would also turn the
// 500 into a 400, and was rejected: that type is thrown by genuine server bugs
// too, and mapping it to a client error would hide them.
//
// Must be registered immediately after UseAntiforgery(). The feature is absent on
// requests with no antiforgery metadata (every GET, and any endpoint that opted
// out), so those pass straight through.
//
// Skeleton copy -- the product owns this file after stamping. NativeWeb.Scaffold
// ships the same guard as UseAntiforgeryRejection; a stamped product that takes
// the scaffold dependency can drop this file and call that instead.
internal sealed class AntiforgeryValidationMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        IAntiforgeryValidationFeature? validation = context.Features.Get<IAntiforgeryValidationFeature>();

        if (validation is { IsValid: false })
        {
            // Body left to ErrorPageMiddleware, which renders the shared HTML
            // error shell for any bare >= 400 -- and deliberately no log entry:
            // an expired tab is routine and a bot spraying tokenless POSTs is
            // the client's doing, so neither is an application fault.
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
