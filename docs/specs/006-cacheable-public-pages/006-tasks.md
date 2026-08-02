# 006 — Tasks
Plan: ./006-plan.md

Build order. T1–T2 land in NativeWeb (`feat/render-script-hashes`); T3–T9 in manzil.my
(`feat/cacheable-public-pages`); T10 back in NativeWeb (skeleton parity); T11 closes out.
Each branch: code + tests together, Fable review before merge, `--no-ff` into `dev`.

- [ ] T1 AOT gate — files: manzil.my `apps/manzil/Program.cs` (probe wiring), publish build —
      covers: R2 (risk gate) — done when: a NativeAOT publish with `AddOutputCache`/
      `UseOutputCache` wired completes with zero warnings; if it fails, STOP and surface the
      fallback decision (hand-rolled keyed store) before continuing.
- [ ] T2 Inline-script hash constants — files: NativeWeb `libs/nativeweb.render.generator/*`,
      `tests/nativeweb.render.tests.unit/*` — covers: R3 — done when: the generator emits a
      SHA-256 (base64) constant per inline `<script>` body from the template's exact bytes, and
      a unit test proves the constant matches an independent recomputation.
- [ ] T3 PublicPageCsp + header rules — files: manzil.my
      `apps/manzil/Middleware/SecurityHeadersMiddleware.cs`, unit tests — covers: R1, R2, R3 —
      done when: public-flagged responses carry `script-src 'self' 'sha256-…'×2`,
      `style-src 'self'`, and `Cache-Control: public, max-age=60` + `Vary: Cookie,
      Accept-Language` only when the request bore neither `__Host-Manzil.Session` nor
      `__Host-nw-flash` (else today's `no-store`); unit tests cover all four cookie
      combinations and the hash-lockstep check against built templates.
- [ ] T4 Anonymous chrome + layout guards — files: manzil.my `apps/manzil/Render/PageModels.cs`,
      `apps/manzil/templates/layout/page.html` — covers: R1, R7 — done when: an anonymous
      factory mints no antiforgery pair (no `Set-Cookie`), empty Nonce/Token/Canary; the layout
      omits `nonce=` attributes and the antiforgery `<meta>` when empty; signed-in renders are
      byte-identical to today; BotCanary confirmed unrendered on `/` and `/u/*`; doc comment
      fixed.
- [ ] T5 OutputCache policy + wiring — files: manzil.my `apps/manzil/Program.cs` (+ policy
      type) — covers: R2, R8 — done when: bypass (no read/write) on either named cookie; key =
      path + `page` query + negotiated locale; junk query params key nothing;
      `UseOutputCache` sits after `UseRateLimiter` and `UseLocaleNegotiation`; unit tests cover
      the bypass predicate and locale normalization.
- [ ] T6 Public endpoints serve the anonymous variant — files: manzil.my
      `apps/manzil/Public/PublicEndpoints.cs`, `Program.cs` `MapGet("/")` — covers: R1, R5,
      R7 — done when: cookie-free requests render the anonymous chrome and are cacheable;
      cookie-bearing requests render exactly today's page; 404s are never cached and the miss
      path is unchanged; existing enumeration suite passes unchanged in meaning.
- [ ] T7 Eviction from write endpoints — files: manzil.my `apps/manzil/Bookmarks/*`, `Tags/*`,
      `Transfer/*`, `Auth/SettingsEndpoints.cs` — covers: R6 — done when: every successful
      write that can change a public page evicts tag `pub:{username,lowercase}`; a visibility
      flip is visible to the next anonymous request with no TTL wait.
- [ ] T8 Integration tests — files: manzil.my `tests/manzil.tests.integration/*` — covers: R9
      (and asserts R1, R2, R4, R5, R6, R7, R8) — done when: double-GET is byte-identical with
      one DB render and no secrets/cookies; poisoning attempts fail in both directions; flash
      flow bypasses then rejoins the cache; locales get distinct bodies; sign-in/register stay
      `no-store` with a live antiforgery pair; eviction immediate and TTL bound holds with a
      test-shortened TTL; `?page=` extremes and junk params behave as designed.
- [ ] T9 E2E tests — files: manzil.my `tests/manzil.tests.e2e/*` — covers: R9 — done when:
      Playwright sees zero CSP violations and `data-theme` set on a cached anonymous page;
      signed-in browse of own `/u/*` shows signed-in chrome; sign-out flash renders once.
- [ ] T10 Skeleton parity — files: NativeWeb `skeleton/apps/__product__/*` — covers: plan's
      pattern-copy convention — done when: middleware/layout/policy changes are mirrored
      structurally identical and the skeleton builds zero-warning.
- [ ] T11 Close-out — files: spec status, `work/`, CHANGELOG — covers: acceptance — done when:
      k6 confirms anonymous `/u/*` cache hits at the static floor; `006-spec.md` Status: done;
      tasklist archived per convention; cleanup script written.
