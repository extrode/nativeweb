# Security baseline

The security posture an app built on NativeWeb inherits, laid out as an ASVS-derived checklist.
Each row names where the control lives so a reviewer can verify it rather than trust this
document. Rows marked ☐ are known open, with the reason.

Applications add their own controls on top. `skeleton/docs/security-baseline.md` is the copy a
stamped product starts from. Account storage, passphrase policy, two-factor and an OAuth
authorization server are not part of NativeWeb: `libs/nativeweb.signin` is a relying party only
([decision record](../decisions/2026-08-31-007-idp-leaves-nativeweb.md)), so those controls belong to whichever
identity provider an app signs in against.

## V2 — Authentication

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Passphrases hashed with Argon2id, fixed cost parameters, `NeedsRehash` when the cost rises | `libs/nativeweb.security/Passphrases.cs` |
| ☑ | Opaque bearer tokens: 32 CSPRNG bytes, only the SHA-256 is stored | `libs/nativeweb.security/OpaqueTokens.cs` |
| ☑ | Sign-in by OAuth authorization code with PKCE (`S256`) | `libs/nativeweb.signin/OAuthClient.cs` |
| ☑ | Bot defense: 4 honeypots and a signed time canary on every human form, per-endpoint fake successes | `libs/nativeweb.security/BotTraps.cs` |

## V3 — Session management

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Opaque server-side sessions (no stateful token on the client), single-delete revocation | `libs/nativeweb.signin/ISessionStore.cs` |
| ☑ | `__Host-` prefixed cookie: Secure, `Path=/`, no Domain, HttpOnly, SameSite=Lax. Not configurable; a cookie name without the prefix fails at startup | `libs/nativeweb.signin/SessionManager.cs`, `SignInOptions.cs` |
| ☑ | Sign-out deletes the server row and expires the cookie with matching attributes | `libs/nativeweb.signin/SignInEndpoints.cs` |
| ☑ | Fixed session lifetime, 14 days by default | `SignInOptions.SessionLifetime` |
| ☑ | Antiforgery token on every state-changing form post, rejected before the body is read | `libs/nativeweb.web/AntiforgeryValidationMiddleware.cs`, `libs/nativeweb.forms` |

## V5 — Input handling

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Request-body ceiling of 128 KB at Kestrel (`MaxRequestBodyBytes`); endpoints raise it explicitly where needed | `libs/nativeweb.web/NativeWebPipeline.cs`, `NativeWebOptions.cs` |
| ☑ | Request timeout, 30 s by default | `NativeWebPipeline.AddNativeWebRequestTimeouts` |
| ☑ | Rate limiting: per-IP policies `general` (120 permits) and `csp-report` (10). Endpoints opt in with `RequireRateLimiting` | `NativeWebPipeline.cs`, `NativeWebOptions.cs` |
| ☑ | HTML output encoded by default; raw output only through the grep-able `Html.Raw` | `libs/nativeweb.render/Html.cs` |
| ☑ | No reflection-based JSON deserialization of attacker input (AOT source-generated contexts only); `/csp-report` reads raw bytes | repo-wide |

## V13/V14 — HTTP security configuration

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | CSP: `default-src 'self'`, per-request nonce for scripts and styles, `frame-ancestors 'none'`, `base-uri 'none'`, `form-action 'self'` | `libs/nativeweb.web/SecurityHeadersMiddleware.cs` |
| ☑ | Static and error pages get a stricter nonce-free CSP: `default-src 'none'`, no `script-src`, `form-action 'none'` | `SecurityHeadersMiddleware.StaticPageCsp`, set by `StaticPagesMiddleware` and `ErrorPageMiddleware` |
| ☑ | CSP violation reporting: `report-uri` and `Reporting-Endpoints` to a rate-limited `POST /csp-report`, logged raw, never deserialized | `libs/nativeweb.web/CspReportEndpoint.cs` |
| ☑ | HSTS for one year with `includeSubDomains`, outside Development only | `SecurityHeadersMiddleware.cs` |
| ☑ | `Permissions-Policy` denying unused powerful features | `SecurityHeadersMiddleware.cs` |
| ☑ | `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` | `SecurityHeadersMiddleware.cs` |
| ☑ | `no-store` and `Pragma: no-cache` on sensitive content types and secret-bearing redirects, including `HX-Redirect` | `SecurityHeadersMiddleware.cs` |
| ☑ | `security.txt` (RFC 9116) at `/.well-known/`, served only when `NativeWeb:SecurityContact` names a mailbox. Unset serves no file, by design | `libs/nativeweb.web/SecurityTxtEndpoint.cs` |
| ☐ | `Expires` in `security.txt` is a source literal and needs a yearly bump; currently `2027-08-01` | `SecurityTxtEndpoint.cs` |

## V7/V16 — Logging and auditing

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Structured trap events on every honeypot trip | `BotTraps.LogTripped` |
| ☑ | Request logging without bodies or credentials; query strings are redacted | `libs/nativeweb.web/RequestLoggingMiddleware.cs`, `QueryRedactingProcessor.cs` |

## V10/V1 — Dependencies and build

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | CI CVE gate: `dotnet list package --vulnerable --include-transitive` fails the build on any finding, or when the audit confirms nothing | `.github/workflows/ci.yml` |
| ☑ | `TreatWarningsAsErrors` repo-wide, including trim and AOT analyzers | `Directory.Build.props` |
| ☑ | Central package versions; htmx and Alpine.js vendored, no CDN | `Directory.Packages.props`, `apps/sample/wwwroot/vendor` |
| ☑ | No page HTML cached in `localStorage`. htmx 2 kept a snapshot cache there, which put rendered authenticated markup in origin-scoped storage that survives sign-out. htmx 4 removed it, so back-navigation re-fetches. The `hx-history-cache` extension restores the old behaviour and is deliberately not vendored | `apps/sample/wwwroot/vendor/htmx` |
| ☑ | Guard responses are not swapped into the page. htmx 4 swaps 4xx/5xx bodies into the target by default; `noSwap: [204, 304, "4xx", "5xx"]` keeps a 415 or 400 guard body from rendering inside an authenticated page | `htmx-config` meta in `templates/layout/page.html` |

## Known-open items

- **security.txt contact address.** A deployment that wants a `security.txt` must set
  `NativeWeb:SecurityContact`, and nothing warns when it has not. Deliberately not a startup
  guard: refusing to boot without a security contact would be a larger claim than this baseline
  makes.
- **Canary replay breadth.** The bot-trap canary is app-bound, not session, path or single-use.
  This is an accepted bypass, documented on `BotTraps.IssueCanary`.
- **Collector address.** When `Telemetry:Enabled` is true and `OTEL_EXPORTER_OTLP_ENDPOINT` is
  unset, the exporter targets `localhost:4317` and fails in the background. `deploy.sh` refuses to
  promote in that state; see [deployment.md](deployment.md).
