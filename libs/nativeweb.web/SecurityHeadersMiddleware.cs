using System.Security.Cryptography;

namespace NativeWeb.Web;

// Per-request security headers + a per-request CSP nonce. The nonce is stashed in
// HttpContext.Items under ItemKey so the Render layer can stamp it onto inline
// <script>/<style> tags; the CSP only trusts scripts/styles carrying it.
// Headers are set via OnStarting so they land before the response body begins.
// Skeleton copy -- the product owns this file after stamping.
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
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
    public const string StaticPageCsp =
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

            // Spec 006. Derived from the matched route's metadata plus this
            // request's cookies, never from anything the handler left behind:
            // this callback also runs when the output cache replays a stored
            // response, and on that path the handler never executed. An
            // Items-based mark would be absent precisely then, and the CSP would
            // be rebuilt as if the page were an ordinary dynamic one -- a nonce
            // CSP over a body that carries no nonce, blocking every inline
            // script on every cache hit.
            //
            // One expression drives both the CSP and the Cache-Control below, so
            // they cannot disagree; disagreement either way is the same broken
            // page.
            //
            // Status-gated as well, and that gate is load-bearing rather than
            // tidy: a rate-limited request to a public route is a 429 that has
            // reached the same endpoint metadata, and labelling it
            // "public, max-age=60" would let a shared proxy serve one client's
            // rejection to every anonymous visitor for the next minute. The
            // output cache already refuses to store a non-200 (ServeResponseAsync);
            // this is the same rule stated in the headers, so the two agree
            // about what is shareable.
            string? publicCsp = ctx.Response.StatusCode == StatusCodes.Status200OK
                ? PublicPageCache.ResolveCsp(ctx)
                : null;

            // report-uri is the legacy directive, report-to the modern one keyed
            // through Reporting-Endpoints; browsers use whichever they support,
            // both landing on POST /csp-report. base-uri and form-action do NOT
            // fall back to default-src: without them an HTML-injection foothold
            // can rebase relative URLs via <base href> or exfiltrate credentials
            // through an injected <form action=...>. form-action is 'self' because
            // every form a stamped product starts with posts to its own origin. If
            // one ever submits somewhere else -- an OAuth consent screen whose
            // decision POST 303s to a client's redirect_uri is the case that found
            // this -- 'self' will block it, because the directive is enforced
            // against a submission's whole redirect chain and not only its first
            // hop. Widen it for that response, not globally.
            headers["Content-Security-Policy"] = staticPage
                ? StaticPageCsp
                : publicCsp
                ?? $"default-src 'self'; script-src 'self' 'nonce-{n}'; style-src 'self' 'nonce-{n}'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; report-uri /csp-report; report-to csp";
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
            // blanket header here would silently overwrite it.
            // Static pages are the deliberate exception (R38): they hold no
            // session state, so the static pipeline's revalidation caching
            // (no-cache + ETag) must survive rather than be forced to no-store.
            //
            // A public page (spec 006) is the second exception, and it is the
            // one this comment anticipated. It opts back in explicitly, and
            // only when the request itself proved it was anonymous: the route
            // metadata alone is not enough, because a visitor carrying a
            // personalizing cookie must still get no-store even though the
            // endpoint reached the same template. Vary is declared for a shared
            // cache downstream -- Cookie because presence of one flips the whole
            // variant, Accept-Language because the negotiated locale changes the
            // body.
            if (publicCsp is not null)
            {
                headers["Cache-Control"] = $"public, max-age={PublicPageCache.MaxAgeSeconds}";

                // Append, never assign. UseResponseCompression sets
                // Vary: Accept-Encoding from its own OnStarting callback, and
                // those fire in reverse registration order, so an assignment
                // here lands last and erases it -- after which a shared cache
                // can hand a brotli body to a client that never asked for one.
                headers.Append("Vary", "Cookie, Accept-Language");
            }
            else if (!staticPage
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
    public static bool IsSensitiveContentType(string? contentType)
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
    public static bool IsSensitiveRedirect(int statusCode, IHeaderDictionary headers)
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
