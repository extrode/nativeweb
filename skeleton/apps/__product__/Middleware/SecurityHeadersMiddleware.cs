using System.Security.Cryptography;

namespace __PRODUCT__.Middleware;

// Per-request security headers + a per-request CSP nonce. The nonce is stashed in
// HttpContext.Items under ItemKey so the Render layer can stamp it onto inline
// <script>/<style> tags; the CSP only trusts scripts/styles carrying it.
// Headers are set via OnStarting so they land before the response body begins.
// Skeleton copy -- the product owns this file after stamping.
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    // Retrieval key for the CSP nonce (HttpContext.Items). Public so the Render
    // layer can read the same value this middleware generated.
    public const string ItemKey = "csp-nonce";

    // HSTS is a one-way door: once a browser has seen it, it refuses plain http
    // to this host until max-age expires. Correct for every deployed environment
    // (always behind TLS) and wrong for local http://localhost development, so
    // it is gated on the environment, not on the request scheme -- scheme-keyed
    // emission would silently skip it behind a TLS-terminating proxy.
    private readonly string? _hsts =
        environment.IsDevelopment() ? null : "max-age=31536000; includeSubDomains";

    // Deny-by-default for every powerful platform feature this app never uses.
    // Features not listed keep their spec default allowlist of 'self' -- if a
    // feature is ever needed, remove it from this list rather than widening one.
    private const string PermissionsPolicy =
        "accelerometer=(), camera=(), display-capture=(), geolocation=(), "
        + "gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), usb=()";

    // Static pages (spec 003 R38) carry no scripts and no forms, so their CSP
    // drops the nonces entirely and starts from 'none': linked stylesheets,
    // fonts and images from this origin are the whole allowlist, and
    // form-action 'none' bars an injected form from posting anywhere at all.
    internal const string StaticPageCsp =
        "default-src 'none'; img-src 'self'; style-src 'self'; font-src 'self'; "
        + "frame-ancestors 'none'; base-uri 'none'; form-action 'none'; "
        + "report-uri /csp-report; report-to csp";

    public Task InvokeAsync(HttpContext context)
    {
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[ItemKey] = nonce;

        context.Response.OnStarting(static state =>
        {
            (SecurityHeadersMiddleware middleware, HttpContext ctx) =
                ((SecurityHeadersMiddleware, HttpContext))state;
            IHeaderDictionary headers = ctx.Response.Headers;
            string n = (string)ctx.Items[ItemKey]!;
            bool staticPage = ctx.Items.ContainsKey(StaticPagesMiddleware.ItemKey);

            // report-uri is the legacy directive, report-to the modern one keyed
            // through Reporting-Endpoints; browsers use whichever they support,
            // both landing on POST /csp-report. base-uri and form-action do NOT
            // fall back to default-src: without them an HTML-injection foothold
            // can rebase relative URLs via <base href> or exfiltrate credentials
            // through an injected <form action=...>. Every form in this app posts
            // to its own origin, so form-action 'self' costs nothing.
            headers["Content-Security-Policy"] = staticPage
                ? StaticPageCsp
                : $"default-src 'self'; script-src 'self' 'nonce-{n}'; style-src 'self' 'nonce-{n}'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; report-uri /csp-report; report-to csp";
            headers["Reporting-Endpoints"] = "csp=\"/csp-report\"";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = PermissionsPolicy;
            if (middleware._hsts is not null)
            {
                headers["Strict-Transport-Security"] = middleware._hsts;
            }

            // Session-bound HTML is private data, so it must never land in a
            // shared cache or survive sign-out via the Back button. Keyed off the
            // content type rather than set unconditionally -- MapStaticAssets
            // serves fingerprinted CSS/JS with long-lived immutable caching, and a
            // blanket header here would silently overwrite it. Public pages will
            // opt back in to caching explicitly when they land.
            // Static pages are the deliberate exception (R38): they hold no
            // session state, so the static pipeline's revalidation caching
            // (no-cache + ETag) must survive rather than be forced to no-store.
            if (!staticPage
                && (IsSensitiveContentType(ctx.Response.ContentType)
                    || IsSensitiveRedirect(ctx.Response.StatusCode, headers)))
            {
                headers["Cache-Control"] = "no-store";
                headers["Pragma"] = "no-cache";
            }

            return Task.CompletedTask;
        }, (this, context));

        return next(context);
    }

    // "application/json", but also the "+json" structured suffix (RFC 6839), so
    // the first endpoint to return problem+json is covered by default rather than
    // silently dropping out of the no-store rule.
    internal static bool IsSensitiveContentType(string? contentType)
    {
        if (contentType is null)
        {
            return false;
        }

        // Strip parameters ("; charset=utf-8") before matching the suffix.
        ReadOnlySpan<char> mediaType = contentType.AsSpan();
        int separator = mediaType.IndexOf(';');
        if (separator >= 0)
        {
            mediaType = mediaType[..separator];
        }

        mediaType = mediaType.Trim();

        return mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || (mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                && mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    // A redirect has no body and so no content type, which drops it straight out
    // of the rule above -- but auth-flow redirects carry returnUrl in Location.
    // Enumerated rather than "any 3xx": 304 is a cache-validation response and
    // must not be told not to store, and 301/308 exist to be cached.
    internal static bool IsSensitiveRedirect(int statusCode, IHeaderDictionary headers)
    {
        // An htmx redirect is not an HTTP redirect: 200 with the target in
        // HX-Redirect. Same secret, same absence of a content type.
        if (headers.ContainsKey("HX-Redirect"))
        {
            return true;
        }

        return statusCode is StatusCodes.Status302Found
                or StatusCodes.Status303SeeOther
                or StatusCodes.Status307TemporaryRedirect
            && headers.ContainsKey("Location");
    }
}
