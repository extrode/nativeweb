using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

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
    /// <para>
    /// Absent is <see langword="null"/> and present-but-blank is <c>""</c>, which is
    /// the distinction the form itself makes. No handler in this repo acts on it —
    /// they treat a missing field and a blank one the same way, reporting "this is
    /// required" for both — so it is preserved rather than flattened: throwing the
    /// distinction away here would put it beyond reach of a caller that later wants
    /// it, while keeping it costs a caller that does not want it a `?? ""`.
    /// </para>
    /// <para>
    /// <b>A field the request repeats arrives joined by commas</b>, because that is
    /// what <c>StringValues.ToString()</c> does: <c>a=1&amp;a=2</c> binds
    /// <c>"1,2"</c>. This matches how the framework binds a scalar
    /// <c>[FromForm] string</c>, so the conversion did not change it — but a handler
    /// treating a bound value as one user-supplied token should know that the client
    /// chooses how many it sends.
    /// </para>
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

    /// <summary>
    /// Throws 415 unless the request is <c>multipart/form-data</c>. Emitted as the
    /// second statement of a binder for a model that declares a file field.
    /// </summary>
    /// <remarks>
    /// <see cref="RequireFormContentType"/> admits <c>application/x-www-form-urlencoded</c>
    /// too, and it has to: that is what most forms post. A urlencoded body cannot
    /// carry a file at all, so a model with a file field bound from one would see
    /// every file member absent and report "this is required" — a 400 blaming the
    /// client for a field they could not have sent. 415 names the real fault.
    /// </remarks>
    /// <param name="context">The request being bound.</param>
    /// <exception cref="BadHttpRequestException">The request is not multipart.</exception>
    public static void RequireMultipart(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? contentType = context.Request.ContentType;

        if (contentType is null
            || !contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadHttpRequestException(
                "This form carries a file, so it must be posted as multipart/form-data.",
                StatusCodes.Status415UnsupportedMediaType);
        }
    }

    /// <summary>
    /// Bounds the request body before the form is read. Emitted ahead of the read for
    /// a model that declares a file field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A per-endpoint <see cref="FormFeature"/> is how form limits are set for one
    /// route: the default feature carries the host-wide <see cref="FormOptions"/>, and
    /// raising those for every request to accommodate one upload endpoint is the
    /// opposite of what a limit is for. Installed before the read, so the read sees it.
    /// </para>
    /// <para>
    /// <see cref="IHttpMaxRequestBodySizeFeature"/> is set as well where the server
    /// offers it and has not already started reading. Without it Kestrel's own 30 MB
    /// ceiling applies first on a large request, which is still a refusal but reports
    /// a status the declaration did not ask for.
    /// </para>
    /// </remarks>
    /// <param name="context">The request being bound.</param>
    /// <param name="maxBytes">The largest accepted body, from the declaration.</param>
    public static void LimitBody(HttpContext context, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        IHttpMaxRequestBodySizeFeature? size = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is not null && !size.IsReadOnly)
        {
            size.MaxRequestBodySize = maxBytes;
        }

        context.Features.Set<IFormFeature>(new FormFeature(
            context.Request,
            new FormOptions { MultipartBodyLengthLimit = maxBytes }));
    }

    /// <summary>
    /// Reads the form, turning an over-limit body into a 413 rather than a 500.
    /// Emitted in place of a bare read for a model with a file field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The limit installed by <see cref="LimitBody"/> is enforced by the multipart
    /// reader as it goes, so an oversize request is aborted partway through rather
    /// than buffered and then measured. What surfaces is
    /// <see cref="InvalidDataException"/>, which is not an HTTP status and escapes as
    /// a 500 — a server error for a client sending too much. Converted here.
    /// </para>
    /// <para>
    /// Kestrel raises <see cref="BadHttpRequestException"/> with its own 413 when the
    /// max body size is what tripped. That one is already the right status, so it
    /// passes through rather than being caught and rethrown.
    /// </para>
    /// </remarks>
    /// <param name="context">The request being bound.</param>
    /// <returns>The read form collection.</returns>
    /// <exception cref="BadHttpRequestException">The body exceeded the declared limit.</exception>
    public static async ValueTask<IFormCollection> ReadLimitedFormAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        }
        catch (InvalidDataException tooLarge)
        {
            throw new BadHttpRequestException(
                "The uploaded form is larger than this endpoint accepts.",
                StatusCodes.Status413PayloadTooLarge,
                tooLarge);
        }
    }

    /// <summary>
    /// One declared file field, or <see langword="null"/> when the form does not
    /// carry it.
    /// </summary>
    /// <remarks>
    /// A part present but empty is <see langword="null"/> too, and that is not the
    /// distinction <see cref="Field"/> preserves for text. A browser posts an empty
    /// part for a file input the person left alone, so treating zero bytes as "a file
    /// was sent" would make every such form carry one.
    /// </remarks>
    /// <param name="form">The already-read form collection.</param>
    /// <param name="name">The wire name of the field.</param>
    /// <param name="accept">The accepted content types, or null for any.</param>
    /// <returns>The file, or <see langword="null"/>.</returns>
    /// <exception cref="BadHttpRequestException">The content type is not accepted.</exception>
    public static FormFile? File(IFormCollection form, string name, string[]? accept)
    {
        ArgumentNullException.ThrowIfNull(form);

        IFormFile? file = form.Files.GetFile(name);

        if (file is null || file.Length == 0)
        {
            return null;
        }

        RequireAccepted(file.ContentType, accept, name);
        return new FormFile(file);
    }

    /// <summary>
    /// One declared file field the model requires.
    /// </summary>
    /// <remarks>
    /// 400, which is what a missing required text field gets from the handlers in
    /// this repo. A required file that is absent is a form the person did not finish,
    /// not a request the endpoint cannot understand.
    /// </remarks>
    /// <param name="form">The already-read form collection.</param>
    /// <param name="name">The wire name of the field.</param>
    /// <param name="accept">The accepted content types, or null for any.</param>
    /// <returns>The file.</returns>
    /// <exception cref="BadHttpRequestException">The field is absent, or not accepted.</exception>
    public static FormFile RequiredFile(IFormCollection form, string name, string[]? accept) =>
        File(form, name, accept)
        ?? throw new BadHttpRequestException(
            "The form field '" + name + "' is required and carried no file.",
            StatusCodes.Status400BadRequest);

    // The declared type carries parameters — "image/png; charset=binary" — and
    // comparing the whole header against a declaration of "image/png" would refuse a
    // legitimate post. Only the media type is compared, and case-insensitively,
    // because the header is.
    private static void RequireAccepted(string? contentType, string[]? accept, string name)
    {
        if (accept is null || accept.Length == 0)
        {
            return;
        }

        ReadOnlySpan<char> declared = (contentType ?? string.Empty).AsSpan();
        int parameters = declared.IndexOf(';');
        declared = (parameters < 0 ? declared : declared[..parameters]).Trim();

        foreach (string allowed in accept)
        {
            if (declared.Equals(allowed.AsSpan().Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new BadHttpRequestException(
            "The form field '" + name + "' accepts " + string.Join(", ", accept)
                + ", and the file was sent as '" + (contentType ?? "(none)") + "'.",
            StatusCodes.Status415UnsupportedMediaType);
    }
}
