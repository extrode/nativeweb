# 006 — Plan
Spec: ./006-spec.md
Status: draft (awaiting user approval)
Created: 2026-08-02

## Approach

Anonymous `/` and `/u/*` become **byte-stable per (path, page, negotiated locale)**: the
anonymous render mints no antiforgery token, no CSP nonce, and emits no `Set-Cookie`; the two
build-stable inline scripts are authorized by **build-generated SHA-256 hashes** in a new
`PublicPageCsp` (sibling of the existing `StaticPageCsp` precedent in
`apps/manzil/Middleware/SecurityHeadersMiddleware.cs`). Responses are cached **in-process by
ASP.NET Core OutputCache** with a custom policy that bypasses (no read, no write) any request
bearing `__Host-Manzil.Session` or `__Host-nw-flash`, evicted **immediately by per-username tag**
from the write endpoints, with a **60s TTL/`max-age` backstop**. Signed-in and flash-bearing
responses keep today's `no-store`; the stored anonymous variant declares
`Cache-Control: public, max-age=60` + `Vary: Cookie, Accept-Language` so a future shared proxy
is correct (if fragmented) by default — proxy efficiency tuning is a cutover concern per the
spec's non-goals.

Alternatives rejected: **externalizing the inline scripts** (the theme bootstrap must run before
first paint; an external file is a render-blocking fetch and reintroduces a theme flash);
**keeping the nonce** (re-poisons every byte of the head — forfeits the design); **caching the
signed-in variant separately** (personalized responses are a spec non-goal and stay `no-store`);
**per-page eviction** (one bookmark write reshuffles pagination across all pages — per-username
is one eviction and correct); **caching `/u/*` 404s** (creates a stale-404 window after
registration/first-publish — a user-visible trap and a fifth distinguishable state — and
interacts badly with `ErrorPageMiddleware`, which fills bodiless 404s outside the cache).

## Tech / dependencies

- ASP.NET Core OutputCache middleware (in-box, `Microsoft.AspNetCore.OutputCaching`, shared
  framework — no new package). **Verify under NativeAOT with a publish build as the branch's
  first step**: believed AOT-supported for the in-memory store since .NET 8, not verified on
  .NET 10; `TreatWarningsAsErrors` makes the check cheap and decisive. If it fails AOT, the
  fallback is a small hand-rolled `IMemoryCache`-style keyed store behind the same policy
  predicate — same design, more code (recorded as the risk mitigation, not the plan).
- SHA-256 at build time via the existing template source generator
  (`libs/nativeweb.render.generator`), which already parses `templates/**/*.html`.
- No new external dependencies. No SQLite (decided 2026-08-02), no Redis.

## Changes by area

- `libs/nativeweb.render.generator` — emit a generated constant per inline `<script>` body
  (exact bytes, SHA-256, base64) for templates that request it; hashes are build-generated,
  never hand-maintained (R3).
- `apps/manzil/Middleware/SecurityHeadersMiddleware.cs` — new `PublicPageCsp` built from the
  generated hashes (`script-src 'self' 'sha256-…' 'sha256-…'`; `style-src 'self'` — audit
  confirms no inline styles, else hash or hoist them); selected per-route via an Items flag set
  by the public endpoints, applied in `OnStarting` on every response so a cache replay can never
  carry a stale CSP; `Cache-Control: public, max-age=60` only when the public flag is set AND
  the request carried neither named cookie, else today's `no-store` (R1, R2, R3).
- `apps/manzil/Render/PageModels.cs` — anonymous chrome factory: no `GetAndStoreTokens` call
  (which would also `Set-Cookie`), empty `Nonce`/`AntiforgeryToken`/`BotCanary`; fix the
  "for every page" doc comment (R1). Confirm first that `BotCanary` renders nowhere on `/` or
  `/u/*` (grounding says it doesn't — templates carry no bot-traps partial).
- `apps/manzil/templates/layout/page.html` — guard the `nonce=` attribute and the antiforgery
  `<meta>` on emptiness so one layout serves both variants; the sign-out form is already inside
  `{{if SignedIn}}`; both inline scripts stay on anonymous pages (the htmx hook is inert without
  the meta tag and holds no secret) (R1, R7).
- `apps/manzil/Program.cs` — `UseOutputCache` after `UseLocaleNegotiation` and after
  `UseRateLimiter` (every request rate-limited, hit or miss — R8); custom policy: bypass on
  either named cookie; cache key = path + `page` query key only (junk params like `utm_*`
  cannot mint entries) + negotiated locale from `GetLocale()` (collapses hostile
  `Accept-Language` to the shipped catalog set); `Vary: Cookie, Accept-Language` on stored
  responses (R2, R4-adjacent).
- `apps/manzil/Public/PublicEndpoints.cs` + `MapGet("/")` — anonymous requests use the
  anonymous chrome; signed-in requests render exactly today's nonce path (R7). 404s stay
  uncached; the miss path stays byte- and timing-identical to today (R5).
- Write endpoints (`Bookmarks`, `Tags`, `Transfer` import completion, `Settings` username/
  profile changes) — one `EvictByTagAsync("pub:{username,lowercased}")` per successful write;
  usernames are citext, so the canonical tag clears every path-casing variant (R6).
- Skeleton (`NativeWeb/skeleton/apps/__product__`) — pattern-copy of the middleware/layout
  changes, per the established structurally-identical convention.

## Data / interfaces

- Cache key: `(path, page-query-value, negotiated-locale)`; eviction tag `pub:{username}`
  (lowercase). No schema changes, no new endpoints, no API surface change.
- Header contract on anonymous `/`+`/u/*` 200s: `Cache-Control: public, max-age=60`,
  `Vary: Cookie, Accept-Language`, `Content-Security-Policy: <PublicPageCsp>`; body contains no
  `request-verification-token` meta, no `nonce=` attributes, and the response sets no cookies.
  All other responses: unchanged, including `no-store` on signed-in/flash/form pages.

## Risks & mitigations

- **OutputCache under NativeAOT unverified** → first task of the branch is a publish build;
  zero-warning gate decides; fallback keyed store designed above.
- **Hash drift from template edits** → hashes are generator-emitted from the built template
  bytes, and a unit test recomputes them against the served header; drift is a red build, not a
  silent CSP break.
- **Cache poisoning across variants** → bypass keys on cookie *presence* (never validated
  identity — a garbage cookie pays a full render but can never read or write the shared entry);
  anonymous entries are only written for cookie-free requests; `Set-Cookie` responses are
  additionally refused by OutputCache's own default guard (two independent guards).
- **Enumeration (R5)** → 404s uncached; miss path unchanged; existing enumeration suite must
  pass unchanged in meaning. Residual: 404 crawls still reach PostgreSQL — accepted, mitigated
  by the public rate-limit group; revisit only under measured flood.
- **Cache-fill via `?page=` extremes** → each distinct value keys an entry; bounded by the
  OutputCache size limit, the 60s TTL, and the public rate limit. Accepted residual, noted.
- **BREACH on authenticated pages** → unchanged posture: ASP.NET Core's request token is
  already a per-call masked representation of the session-scoped cookie token; asserted by a
  test rather than rebuilt (user decision 2026-08-02).
- **Compression of cache hits** → hits recompress per response (compression sits outside the
  cache): no secrets in anonymous bodies so no BREACH concern, sub-ms cost; measure once when
  benchmarking the ~1ms target.

## Coverage check

- R1 (no per-request secrets) → anonymous chrome factory + layout guards + no `Set-Cookie`.
- R2 (explicit cacheability/variance) → policy + `Cache-Control`/`Vary` contract.
- R3 (CSP without nonce, no weakening) → generated hashes, `PublicPageCsp`, lockstep test.
- R4 (antiforgery on all state-changing routes) → untouched; sign-in/register stay `no-store`
  with live token pairs (login-CSRF kept).
- R5 (enumeration defense survives) → 404s uncached, miss path byte-identical, suite unchanged.
- R6 (bounded freshness) → per-username eviction (immediate) + 60s TTL/max-age backstop.
- R7 (signed-in unchanged) → cookie-presence bypass; signed-in renders today's nonce path.
- R8 (rate limiting correctness) → `UseOutputCache` after `UseRateLimiter`.
- R9 (three test layers) → unit (hash lockstep, bypass predicate, locale normalization, header
  rules), integration (double-GET byte-identity + single DB render, both poisoning directions,
  flash flow, locale separation, enumeration, eviction + TTL bound, cache-fill probes), e2e
  (zero CSP violations + theme applied under hash CSP, signed-in chrome, sign-out flash).

No requirement uncovered.
