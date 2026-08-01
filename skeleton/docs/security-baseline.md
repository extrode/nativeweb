# Security baseline — __PRODUCT__ (spec 003, R29)

ASVS-derived checklist for __PRODUCT__, carried over from the NativeWeb skeleton it was stamped
from. **Most of these controls ship pre-wired**: the skeleton's pipeline already sets the headers,
the limits, the traps and the gates, so the work is not building them but *keeping them true* as
the product grows — a new form, a new endpoint, a new dependency is where each one quietly stops
holding.

Tick an item only when it has been verified in this repo, not because the skeleton shipped it.
Each item names where the control lives so a reviewer can check it rather than trust this file.

## HTTP security configuration (V13/V14)

- [ ] CSP is nonce-based — `default-src 'self'`, a per-request nonce on scripts and styles,
      `frame-ancestors 'none'`, and **no `unsafe-inline` / `unsafe-eval` anywhere**. Any new
      inline script or style takes the nonce; it does not get a CSP relaxation.
      (`Middleware/SecurityHeadersMiddleware.cs`)
- [ ] Static pages keep the stricter nonce-free branch — `default-src 'none'`, no `script-src`
      at all, `form-action 'none'` — and error pages use the same branch.
      (`SecurityHeadersMiddleware`, marker set by `StaticPagesMiddleware` / `ErrorPageMiddleware`)
- [ ] HSTS is sent in production (1 year, `includeSubDomains`) and only outside Development.
      (`SecurityHeadersMiddleware`)
- [ ] `Permissions-Policy` denies every powerful feature the product does not use; adding a
      feature means editing the policy deliberately, not deleting it.
      (`SecurityHeadersMiddleware`)
- [ ] `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` present on every response.
      (`SecurityHeadersMiddleware`)
- [ ] `POST /csp-report` stays wired, rate-limited, and logged raw — never deserialized.
      (`CspReportEndpoint.cs`, the `CspReport` rate-limit policy in `Program.cs`)
- [ ] `security.txt` contact is a **real monitored address**, not the stamped placeholder, and
      `Expires` is bumped yearly. (`SecurityTxtEndpoint.cs`)
- [ ] `robots.txt` and `sitemap.xml` name this product's real host, not the stamped placeholder,
      and staging does not advertise a production sitemap URL.
      (`wwwroot/robots.txt`, `wwwroot/sitemap.xml`)

## Sessions and forms

- [ ] When the product adds authentication, the session cookie is `__Host-` prefixed: Secure
      always, `Path=/`, no `Domain`, `HttpOnly`, `SameSite=Lax` — and sign-out both deletes the
      server-side row and expires the cookie with matching attributes.
- [ ] Sessions are opaque and server-side (no stateful token on the client), stored hashed at
      rest, revocable with a single DELETE.
- [ ] Every state-changing form post validates an antiforgery token — no exceptions, including
      htmx posts. (`AddAntiforgery` / `UseAntiforgery` in `Program.cs`,
      `templates/shared/antiforgery.html`)
- [ ] Every public form carries the bot traps (honeypots + signed time canary), and a tripped
      trap returns the **same fake success** the real path returns, paying the same cost —
      never a distinguishable error. (`templates/shared/bot-traps.html`, NativeWeb `BotTraps`)

## Input handling and abuse limits

- [ ] Global request-body ceiling at the edge (128 KB), raised only per-endpoint and only where
      a specific need is documented. (`Program.cs`, `Limits.MaxRequestBodySize`)
- [ ] Request timeouts configured, with a default that applies to every endpoint.
      (`Program.cs`, `AddRequestTimeouts`)
- [ ] Rate limits are IP-keyed fixed windows on every public, auth, and report endpoint; a new
      public route joins a policy rather than running unlimited. (`Program.cs`, `AddRateLimiter`)
- [ ] `ForwardedHeaders:KnownProxies` is set in production — otherwise `X-Forwarded-For` is
      ignored and every client shares one rate-limit partition. (`appsettings.json`, deploy units)
- [ ] HTML output is encoded by default; raw output only through the grep-able `Html.Raw`.
- [ ] No reflection-based JSON deserialization of attacker input — AOT source-generated contexts
      only.

## Errors and logging

- [ ] Error pages never leak stack traces, exception types, or framework detail in any
      environment reachable from outside. (`Middleware/ErrorPageMiddleware.cs`,
      `wwwroot/pages/error.html`)
- [ ] Request logging records no bodies and no credentials.
      (`Middleware/RequestLoggingMiddleware.cs`)
- [ ] When the product adds privileged writes (admin actions, role changes, secret rotation),
      each one writes an append-only audit row on success, and the detail never carries secret
      material.

## Dependencies and build

- [ ] The CI CVE gate stays green and stays non-vacuous — it fails on any finding *and* on an
      audit that produced no per-project confirmation. (`.github/workflows/ci.yml`)
      Note: this gate cannot run until the package-feed gap in that workflow's header is closed.
- [ ] All package versions are pinned centrally; no floating versions, no CDN references.
      (`Directory.Packages.props`, vendored `wwwroot/vendor`)
- [ ] `dotnet publish -c Release` produces a NativeAOT binary with **zero warnings** under
      `TreatWarningsAsErrors`, including trim/AOT analyzers. (`Directory.Build.props`)
- [ ] The e2e axe accessibility gate passes on every page the product adds, not just the stamped
      ones. (`tests/__product__.tests.e2e/SmokeFlowTests.cs`)

## Known-open items

Record here anything on the list that is deliberately not true yet, with the reason and who
decided. An unexplained unticked box is indistinguishable from an oversight.

- (nothing yet)
