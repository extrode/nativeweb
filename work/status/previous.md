# Previous

The task completed most recently, kept for one rotation so the next one has context.

- **Spec 021 — response formats and content negotiation** —
  `feat/021-response-formats-and-negotiation`, 2026-09-02. Twelve tasks ticked, spec
  `Status: done`. Eight document formats from one call each in `libs/nativeweb.render`,
  `MapNegotiated` with a stated precedence and stated refusals in `libs/nativeweb.web`, the
  public-page cache keyed on the chosen format, every format published by `apps/sample` and
  proved in three tiers. Review: 27 candidates, 13 confirmed and fixed, 8 refuted, 6 noted.
  - **The finding that mattered most was a cache defect the plan had called a feature.** The
    plan put `/articles` under the public-page policy expecting its antiforgery cookie to keep it
    out of the cache. A returning visitor already holds the cookie, so their render sets none, is
    stored, and the next stranger receives a page whose token cannot match any cookie they hold.
    Measured through `SampleFactory`: the third visitor got no `Set-Cookie` at all. The listing
    is uncached now in every representation and a test holds it there; the feed's Atom and RSS
    representations show per-representation caching instead.
  - **The chooser was wrong twice at once**: a `q=0` range only vetoed an offer when it was the
    first match, and specificity never ranked, so `*/*, text/html;q=0` chose HTML. It ranks each
    offer by its most specific matching range now.

- **Spec 016 — the OAuth relying party** — `feat/016-oauth-relying-party`, 2026-09-01. All 27
  tasks ticked, spec `Status: done`. **1313 tests across 15 assemblies, 0 failed**; AOT publish
  gate 21 s with zero IL2xxx/IL3xxx. NativeWeb is the client half and never the server:
  authorization-code with PKCE, an opaque session cookie over an `ISessionStore` the product
  implements, silent refresh, sign-out, and a bearer-introspection filter, all behind a real
  authentication scheme — which is what a scaffolded route's `RequireAuthorization` needed and
  did not have.
  - **The pre-merge review found twelve defects and none of them was refuted.** Three rounds,
    each round's fixes reviewed again, every fix mutation-checked. The one that mattered most is
    login CSRF: `state` was remembered by the server alone, so an attacker could run the flow in
    their own browser, keep the callback URL and hand it to a victim, whose browser would then
    hold a session for the attacker's identity. The state is written to a short-lived `__Host-`
    cookie now and a callback that cannot show it is refused with `state_mismatch`.
  - **Three of the twelve were code that no caller reached.** `IntrospectionCache.Sweep` and
    `ISessionStore.SweepAsync` had no production caller, so expired sessions and stale verdicts
    accumulated for the life of the process; the sweeper swept login flows alone and is named
    `SignInSweeper` now for sweeping all three. The failure redirect pointed at `LoginPath`,
    which is the flow trigger, so a failure restarted the flow and looped the browser —
    `FailurePath` is a separate option and `Validate()` refuses it if it equals either route
    the library owns.
  - **Two concurrency defects, one measured through the refresh path.** The per-session gate was
    evicted on `SemaphoreSlim.CurrentCount == 1`, which reads the same whether the gate is idle
    or a thread holding a reference has not yet reached `WaitAsync`; the next arrival built a
    second gate for the same key and both refreshed at once. It is reference-counted under a
    lock now. And a CAS conflict returned the winner's record without re-checking whether the
    winner itself was stale — `ProviderTokens.From` defaults `ExpiresIn` to 0 when the provider
    omits `expires_in`, so a winner can land already inside `RefreshSkew`. Bounded to two
    attempts.
  - **`ClientSecretBasic` sent a raw `id:secret`.** RFC 6749 §2.3.1 requires both halves
    form-urlencoded before the base64, and a base64-shaped secret carries `+`, `/` and `=`, so
    any provider reading the header to the letter refused the exchange.
  - **A publish lock that outlived its publish.** Two e2e tiers publish `apps/sample`; a
    `dotnet publish` over a running exe fails on Windows, and the earlier reasoning that the
    second publish is incremental and writes nothing held only while the binary was already
    current. Measured red once — `Failed: 4, Passed: 0` in the full run against green alone.
    The lock is held for the test process's lifetime now.

- **Spec 015 — the framework web seam** — `feat/015-framework-web-seam`, 2026-08-31. T1–T10, all
  ten ticked. **546 facts across eight tiers, 0 failed, 0 skipped**; NativeAOT publish of
  `apps/sample` clean, 0 IL2xxx/IL3xxx; structure-audit 0 errors, 3 pre-existing naming warnings.
  Every new fact mutation-checked.
  - **A product's entry point was 487 lines of framework wiring, and a security fix to any of it
    could not reach a product already stamped.** Kestrel limits, forwarded headers, telemetry, data
    protection, antiforgery, bot traps, rate limiting, the output cache, the middleware order,
    `/healthz`, `/version`, `/readyz`, `/csp-report` and security.txt now live in
    `libs/nativeweb.web`. The skeleton's `Program.cs` is 39 code lines, and a ceiling test holds it
    there. Nothing is hidden: every step is a public extension method, and a product needing
    something between two of them writes the eleven calls out.
  - **Framework static assets are delivered by MSBuild, not copied into each product.**
    `build/NativeWeb.Web.targets` contributes `wwwroot/_nativeweb/**` as linked `Content`, which the
    static web assets pipeline fingerprints and precompresses into the consumer's publish output —
    measured against a scratch probe before the design was committed. The `/_nativeweb/` base path
    exists because two assets at one target path is a hard build error, not a precedence question.
  - **Adopting the seam exposed three defects in one afternoon, each now a test.** Antiforgery's
    `SecurePolicy.Always` 500s every form page over plain http, so `dotnet run` and Playwright both
    broke (36 integration failures); `UseAuthentication` after antiforgery validation makes every
    authenticated form post a 400 (10 more); and the scaffolded admin's inline `onsubmit` cannot run
    under a nonce CSP, which only the Playwright tier could see (4 e2e).
  - **The pipeline costs `articles` 66% of its throughput, and it is one middleware.** 18,958.6 →
    6,440.5 req/s, bisected to response compression: the level makes no difference, the codec does,
    and compressing strips the `Content-Length` that the 35% chunking fix had just added.
    `docs/02-reference/performance.md` bottleneck 4 has the four-variant table and the three ways
    out; `tools/bench/baseline.json` is re-recorded, because a baseline that is red on every run is
    not a gate.
  - **`Assembly.Load` by name is gone from the version lookup.** It does work under NativeAOT — the
    published binary answered `/version` with the stamped value — but a name lookup that catches
    four exception types and falls back is a silent wrong answer waiting for a rename. A product
    passes `typeof(Program).Assembly` now.
  - **The review found eleven things and three were worth fixing.** The data source was registered
    as a built instance, so the container never disposed it and the pool outlived the host; the
    Development antiforgery cookie name was composed a second way; and `Telemetry:Enabled` read
    `1`, `yes` and `on` as off. The rest are in `work/todo.md`, including one finding that
    contradicts a recorded decision and was left alone for it.

- **Follow-on, same day — `perf/016-compression-placement`.** The compression trade 015 left open,
  A/B'd against a real Caddy 2.11.4 rather than argued: the edge served the page at 9,002 req/s
  where the app compressing it managed 6,255, and the composed pipeline recovered `articles` from
  6,440.5 to **16,528.8 req/s**, putting the whole framework pipeline at 12.8% against Phase 1's
  close instead of 66%. `UseResponseCompression` is out of `UseNativeWebPipeline`;
  `encode zstd gzip` is in both deploy Caddyfiles, which validate; `AddNativeWebResponseCompression`
  stays public for a host with no proxy. **562 facts, 0 failed, 0 skipped.** Brotli is absent
  because stock Caddy has no encoder for it — `br` fails config validation outright.
