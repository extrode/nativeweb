using Microsoft.AspNetCore.Http;

namespace NativeWeb.Forms;

/// <summary>
/// The handful of runtime helpers a generated <c>BindAsync</c> calls.
/// </summary>
/// <remarks>
/// <para>
/// Kept small on purpose: everything that can be generated is generated, and what
/// lives here is what the emitted code would otherwise repeat verbatim in every
/// form model in every consuming application.
/// </para>
/// <para>
/// <b>Why the content-type check lives in the binder and not in a filter.</b>
/// Measured twice. Brainstorm 001 §12 finding 4: argument binding runs upstream of
/// the entire endpoint-filter pipeline, so a guard written as an
/// <c>AddEndpointFilter</c> cannot protect the bind — by the time it runs, the form
/// has already been read. Re-confirmed 2026-08-30 from the other direction: with a
/// route's gated services removed, a JSON POST answered 415 rather than the 503 its
/// first-chained filter would have produced, because the rejection had already
/// happened during binding. This is the same structural fact that put antiforgery
/// in middleware rather than in a filter.
/// </para>
/// </remarks>
public static class FormBinding
{
    /// <summary>
    /// Throws 415 unless the request carries a form body. Called as the first
    /// statement of every generated binder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BadHttpRequestException"/> rather than a returned result, because
    /// a binder has no way to short-circuit the pipeline with a status — it returns
    /// a value or it throws. <c>ErrorPageMiddleware</c> already surfaces this
    /// exception at its own status code, so the 415 reaches the client as a 415.
    /// </para>
    /// <para>
    /// Without it, a <c>BindAsync</c> that reads <c>Request.Form</c> on a JSON body
    /// throws a bare <see cref="InvalidOperationException"/> from
    /// <c>FormFeature</c>, which escapes as a 500 — measured, §12 variant 5. A
    /// server error for what is a client's malformed request.
    /// </para>
    /// </remarks>
    /// <param name="context">The request being bound.</param>
    /// <exception cref="BadHttpRequestException">The request is not a form post.</exception>
    public static void RequireFormContentType(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.HasFormContentType)
        {
            throw new BadHttpRequestException(
                "Expected a form content type (application/x-www-form-urlencoded or multipart/form-data).",
                StatusCodes.Status415UnsupportedMediaType);
        }
    }

    /// <summary>
    /// One declared field, or <see langword="null"/> when the form does not carry it.
    /// </summary>
    /// <remarks>
    /// Absent and empty are deliberately NOT distinguished here, and the existing
    /// endpoints are why: they treat a missing field and a blank one the same way,
    /// reporting "this is required" for both. A generated binder that returned
    /// <c>""</c> for one and <see langword="null"/> for the other would push that
    /// distinction into every handler for no caller that wants it.
    /// </remarks>
    /// <param name="form">The already-read form collection.</param>
    /// <param name="name">The wire name of the field.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static string? Field(IFormCollection form, string name)
    {
        ArgumentNullException.ThrowIfNull(form);

        return form.TryGetValue(name, out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}
