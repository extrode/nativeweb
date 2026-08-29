# Security baseline (spec 003, R29)

ASVS-derived checklist for the NativeWeb reference architecture, written as part of V6
(`feat/security-baseline`). Scope: `apps/web`, the IdP. The first product (DevMarks) was
extracted to the manzil.my repo in V10 and carries its own copy of this checklist; rows that
named its implementation now describe the IdP only.
Each row names where the control lives so a reviewer can verify it rather than trust
this document. Items marked ☐ are known-open, with the reason.

## V2 — Authentication

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Passphrases hashed with Argon2id, fixed cost parameters | `libs/nativeweb.security/Passphrases.cs` |
| ☑ | Breach checking on set + sign-in (offline common-password list + HIBP k-anonymity, kill-switch) | `CompositeBreachChecker`; `NativeWeb:BreachCheck:Hibp` |
| ☑ | Generic failure message; fake-identifier responses prevent user enumeration | `FakeIdentifierResponses`, sign-in/reset endpoints |
| ☑ | Timing parity: unknown-user and bot-trapped paths pay the same Argon2id cost | V5 review fixes in `SignInEndpoints` |
| ☑ | Account lockout / throttle on failed sign-ins | `LockoutPolicy` |
| ☑ | TOTP two-factor with recovery codes; WebAuthn (passkeys) incl. UserVerification step-up | `apps/web/Auth/TwoFactor*`, `WebAuthn*` |
| ☑ | Forced-passphrase-reset confinement, operator lift control | `ForcedPassphraseResetMiddleware`, admin endpoints |
| ☑ | Bot defense: 4 honeypots + signed time canary on every human form, per-endpoint fake successes | `libs/nativeweb.security/BotTraps.cs` (V5) |

## V3 — Session management

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Opaque server-side sessions (no stateful token on the client), single-DELETE revocation | `bff_store` |
| ☑ | `__Host-` prefixed cookie: Secure always, `Path=/`, no Domain, HttpOnly, SameSite=Lax | `Auth/SessionAuth.cs` (V6) |
| ☑ | Sign-out deletes the server row and expires the cookie with matching attributes | `SignInEndpoints` + `SessionAuth.DeleteSessionCookie` |
| ☑ | Fixed session lifetime 14 d | `SessionAuthDefaults.Lifetime` |
| ☑ | Antiforgery token on every state-changing form post | `AddAntiforgery` + per-endpoint validation |

## V4 — Access control

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Operator gate derived per request from the user row, not baked into the session | `SessionClaimTypes.Operator` |
| ☑ | Admin surface requires operator; audited (below) | `AdminEndpoints` |
| ☑ | OAuth: PKCE, exact redirect-URI match, pairwise subjects, client auth on machine endpoints | `apps/web/Auth/OAuth*` |

## V5 — Input handling

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Global request-body ceiling 128 KB at Kestrel; endpoints raise it explicitly where needed (none do today) | `Program.cs` (V6) |
| ☑ | Request timeout default 30 s | `Program.cs`, `AddRequestTimeouts` (V6) |
| ☑ | Rate limiting: per-IP fixed windows on auth/oauth/oauth-authorize/admin/csp-report | `Program.cs` |
| ☑ | HTML output encoded by default; raw output only via the grep-able `Html.Raw` | `libs/nativeweb.render` (V2) |
| ☑ | No reflection-based JSON deserialization of attacker input (AOT source-generated contexts only) | repo-wide; `/csp-report` reads raw bytes |

## V13/V14 — HTTP security configuration

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | CSP: default-src 'self', per-request nonce for scripts/styles, frame-ancestors 'none' | `SecurityHeadersMiddleware` |
| ☑ | Static pages (V8): stricter nonce-free CSP branch — default-src 'none', no script-src at all, form-action 'none'; error pages use the same branch | `SecurityHeadersMiddleware.StaticPageCsp`, marker set by `StaticPagesMiddleware`/`ErrorPageMiddleware` |
| ☑ | CSP violation reporting: report-uri + Reporting-Endpoints → rate-limited `POST /csp-report`, logged raw, never deserialized | `CspReportEndpoint.cs` (V6) |
| ☑ | HSTS 1 y includeSubDomains, non-Development only | `SecurityHeadersMiddleware` (V6) |
| ☑ | Permissions-Policy denying unused powerful features (WebAuthn's stay at default 'self') | `SecurityHeadersMiddleware` (V6) |
| ☑ | X-Content-Type-Options, X-Frame-Options, Referrer-Policy | `SecurityHeadersMiddleware` |
| ☑ | `no-store` + `Pragma: no-cache` on sensitive content types and secret-bearing redirects (incl. HX-Redirect) | `SecurityHeadersMiddleware` |
| ☑ | `security.txt` (RFC 9116) at /.well-known/ | `SecurityTxtEndpoint.cs` (V6) |
| ☐ | security.txt contact address is a placeholder (`security@epass.io`) — operator must confirm the real mailbox and bump `Expires` yearly | `SecurityTxtEndpoint.cs` |

## V7/V16 — Logging and auditing

| ✓ | Control | Where |
|---|---------|-------|
| ☑ | Append-only `audit_log` (trigger-rejected UPDATE/DELETE), one row per mutating admin action, written on success only | migration `0052`, `db/queries/AuditLog/Insert.sql`, `AdminEndpoints.WriteAuditAsync` (V6) |
| ☑ | Audit detail never carries secret material (rotate-secret logs the fact, not the secret) | `AdminEndpoints` |
| ☑ | Structured trap events on every honeypot trip | `BotTraps.LogTripped` |
| ☑ | Request logging without bodies or credentials | `RequestLoggingMiddleware` |

## V10/V1 — Dependencies and build

| ✓ | Control | Where |
|---|---------|-------|
| ▢ | CI CVE gate: `dotnet list package --vulnerable --include-transitive` fails the build on any finding or on a vacuous audit. Wired in V6 but unverifiable until the CI package-feed gap below is closed -- the job dies at restore before the gate runs | `.github/workflows/ci.yml` (V6) |
| ☑ | `TreatWarningsAsErrors` repo-wide incl. trim/AOT analyzers | `Directory.Build.props` |
| ☑ | Central package versions, vendored htmx/Alpine (no CDN) | `Directory.Packages.props`, `wwwroot/vendor` |
| ☑ | No page HTML cached in `localStorage`. htmx 2 kept a snapshot cache there to restore pages on back-navigation, which put rendered authenticated markup in origin-scoped storage that survives sign-out and is readable by any script that gets execution on the origin. **htmx 4.0 removed it** — back-navigation re-fetches instead. The `hx-history-cache` extension restores the old behaviour and is deliberately not vendored (spec 012 non-goal) | `wwwroot/vendor/htmx`, spec 012 |
| ☑ | Guard responses are not swapped into the page. htmx 4 swaps 4xx/5xx bodies into the target by default; `noSwap: [204, 304, "4xx", "5xx"]` keeps a 415 or 400 guard body from rendering inside an authenticated page | `Render/Layout.cs` htmx-config meta |

## Known-open items

- **security.txt contact addresses** — placeholders until the operator confirms real mailboxes
  (needs-decision, flagged in the V6 report).
- **Canary replay breadth** — the bot-trap canary is app-bound, not session/path/single-use;
  documented as an accepted bypass in `BotTraps.IssueCanary`.
- **CI package-mode feed gap** — the CVE gate runs against the package-mode restore, which CI
  cannot currently complete (known JauntyQ feed gap, tracked in memory/README).
- **Collector address** — telemetry is on by default (2026-08-17) but
  `OTEL_EXPORTER_OTLP_ENDPOINT` is unset, so it exports to `localhost:4317` and fails in the
  background. Where the collector lives is an operator decision still open.
