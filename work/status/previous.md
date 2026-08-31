# Previous

The task completed most recently, kept for one rotation so the next one has context.

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
