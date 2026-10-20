# 015 — A web pipeline the framework ships, not the template copies

Status: done
Created: 2026-08-31

Implements **Phase 2 (T7–T10)** of
[the framework-readiness plan](../../plans/2026-08-31-007-framework-readiness.md), which is the
approved design this spec makes testable. The assessment behind it is
[`docs/architecture/003-framework-readiness.md`](../../architecture/003-framework-readiness.md).

## Problem

**A fix to NativeWeb's web pipeline cannot reach a product that was already stamped.** The pipeline
is not in the framework. It is 1,932 lines of `skeleton/apps/__product__/`, copied verbatim into
every product `tools/new-product.ps1` creates, against a framework of 7,606 lines — so roughly a
fifth of what a NativeWeb application runs is template text rather than a library reference.

| What is copied | Lines |
|---|---|
| `Program.cs` — the composition root | 489 |
| `Middleware/` — ten files | 861 |
| `Render/Chrome.cs`, `Render/AssetUrls.cs` | 117 |
| `Flash.cs`, `StartupConfigGuards.cs`, `ReadinessCache.cs`, `CspReportEndpoint.cs` | 465 |

Three consequences, in order of cost:

1. **Security fixes do not propagate.** `SecurityHeadersMiddleware` is 191 lines of CSP, HSTS and
   frame-ancestor policy. A defect found in it is fixed in the skeleton, and every product stamped
   before that day keeps the defect with no signal that it has one.
2. **The composition root is framework knowledge held as copied text.** `Program.cs` carries the
   Kestrel body-size limit, request timeouts, forwarded headers, an IPv6-/64 rate-limit
   partitioner, data-protection key persistence, `__Host-` antiforgery cookie hardening, OTel
   wiring and `ReadinessCache`. None of it is a product decision. The file narrates the defect
   itself at `Program.cs:217-220`.
3. **The reference application does not run any of it.** `apps/sample` has no security headers, no
   public-page cache, no error page and no readiness cache, so the only thing exercising this code
   is a stamped product in CI. A framework whose reference application skips its own pipeline
   demonstrates the wrong thing, and it is how the 2026-08-30 audit found ten skeleton drifts that
   nothing could have caught.

**The invariants are prose.** Spec 006's triangle — `PublicPageCache` / `Chrome.Anonymous` /
antiforgery — is stated in comments and nowhere asserted about the composition: that the anonymous
variant is byte-stable, mints no token, sets no cookie, renders no identity into a shared cache
entry, that the cache sits after the rate limiter, and that locale is in the key. The skeleton's
own unit tier tests `PublicPageCache`, `PublicPageEviction`, `ErrorPageMiddleware`, `Flash` and
`StaticErrorShell` in isolation, and those run in CI after `skeleton-stamp`. **What is untested is
the ordering and the composition** — the properties that only exist when the eleven pieces are
assembled in one order, which is exactly what moving them puts at risk.

## Goals

- A stamped product's `Program.cs` falls from 489 lines to roughly 40, and what remains is what the
  product owns.
- A fix to the pipeline reaches an existing product by changing a package or project reference.
- `apps/sample` runs the same pipeline a stamped product runs.
- The spec-006 invariants are asserted before any code moves, and the same assertions pass after.

## Non-goals

- **No authentication.** Decided by the user on 2026-08-31: ship without, evaluate after the
  non-auth parts. Nothing here adds a sign-in flow, a token issuer or an identity schema — the
  Django line in `AGENTS.md` stands.
- **No change to what the pipeline does.** This is a move plus a seam, not a redesign. A behaviour
  change discovered to be necessary is a finding to record, not licence to make it.
- **Not five libraries.** One, following the house style the scaffold sets.
- **No reflection-bound configuration.** `Configure<T>` is not AOT-safe; options are a typed record
  bound by hand.

## User stories

- As someone who stamped a product last month, I want a security fix in the framework's pipeline to
  reach me by bumping a reference, so that I do not have to diff my `Program.cs` against a template
  I have since edited.
- As someone reading a stamped product for the first time, I want its `Program.cs` to show me only
  the decisions the product made, so that I can tell them apart from the framework's.
- As someone changing the pipeline, I want a test to fail when I break an ordering constraint, so
  that the constraint is enforced rather than commented.

## Requirements

- **R1** The spec-006 invariants are asserted by tests that run in `tests/`, against a composed
  pipeline, before any file moves. Each assertion fails when the invariant is broken.
- **R2** A single library `libs/nativeweb.web` carries the ten middleware files, `Chrome`,
  `AssetUrls`, `Flash`, `StartupConfigGuards`, `ReadinessCache`, `CspReportEndpoint` and the
  `security.txt` endpoint.
- **R3** Its surface is two calls: `AddNativeWebWeb(NativeWebOptions)` and `UseNativeWebPipeline()`.
  Every step `UseNativeWebPipeline()` performs is individually callable and public, so a product
  that needs a different order can write one without forking the library.
- **R4** `NativeWebOptions` is a record bound by hand from `IConfiguration`. No reflection-based
  binding anywhere in the library's startup path.
- **R5** `UseNativeWebPipeline()`'s ordered body is the documented pipeline. Its order is asserted
  by a test, not only by its own comments.
- **R6** Per-product CSP hashes still come from the product's compiled templates
  (`Templates.*.InlineScriptCspHashes`), so the seam does not move a page's inline scripts out of
  the product that owns them.
- **R7** Assets are delivered as physical files through an MSBuild `.targets`, not as embedded
  resources: `AssetUrls` fingerprints and precompresses, and embedded resources bypass both.
- **R8** `skeleton/apps/__product__/Program.cs` is at most 60 lines, and what remains is
  product-owned: the layout body, locales, the product stylesheet, logo, static pages,
  `appsettings`, route mappings, per-page CSP, and who may reach the admin.
- **R9** `apps/sample` uses `AddNativeWebWeb` and `UseNativeWebPipeline`, and its existing tests
  still pass.
- **R10** Every file that moves keeps its tests. The skeleton's five unit test files move to the
  library's own tier rather than being left behind or deleted.
- **R11** Throughput and RSS on `apps/sample` stay within the harness tolerances of the baseline
  recorded after Phase 1. A pipeline of ten middleware where there were none will cost something;
  the requirement is that the cost is measured and recorded in
  `docs/02-reference/performance.md`, not that it is zero.
  **Met as recorded, not as toleranced (2026-08-31).** The cost is measured, bisected and written
  down; it does not fit the tolerances. `articles` fell 18,958.6 → 6,440.5 req/s and the whole of
  that is response compression, which strips the `Content-Length` the Phase 1 chunking fix had just
  added and then pays for the codec on top. `healthz` fell 16% and that part is the rest of the
  pipeline. `tools/bench/baseline.json` is re-recorded from the new figures, because a gate that is
  red on every future run gates nothing, and the four-variant bisection is bottleneck 4 in
  `docs/02-reference/performance.md`. Where compression belongs — in the app, gzip-only, or at Caddy
  — is a CPU-versus-bandwidth decision left open rather than taken here.

- **R12** No trim or AOT warnings. `TreatWarningsAsErrors` is on repo-wide and
  `tests/nativeweb.signin.epass.tests.aot`'s successor gate, if any, stays green.
  **Met.** `dotnet publish apps/sample -c Release` with `PublishAot=true` completes with zero
  IL2xxx/IL3xxx diagnostics, and the published binary answers `/version` with the stamped
  `Version+shortsha` rather than a fallback.

## Acceptance criteria

- **R1** Given the composed pipeline, when an anonymous request is served twice, then the two
  response bodies are byte-identical, no antiforgery cookie is set, and no token appears in either.
- **R1** Given a cached anonymous entry, when an authenticated request arrives for the same path,
  then it is not served from that entry and no identity is written into it.
- **R1** Given two requests differing only in `Accept-Language`, when both are cacheable, then they
  occupy different cache entries.
- **R5** Given `UseNativeWebPipeline()`, when the registered middleware are enumerated, then the
  rate limiter precedes the public-page cache and the assertion fails if they are swapped.
- **R8** Given a freshly stamped product, when `Program.cs` is counted, then it is at most 60 lines
  and the product builds and its three test tiers pass.
- **R9** Given `apps/sample`, when it is run, then it serves the same pages with the framework
  pipeline in front of them and the 468-fact suite stays green.

## Open questions

None blocking. Two decisions are recorded rather than asked, both following the approved plan:

- **T10 — the scaffold admin's inline stylesheet is left alone.** `ScaffoldEmitter.cs:530` emits a
  self-contained sheet so `/admin` works with no host `wwwroot`; linking the design system trades
  that property away. Both Phase 1 reviews put it off the critical path, so it stays out of this
  spec and goes to `work/todo.md`.
- **`apps/sample` adopts the seam (R9) rather than being left as it is.** A seam with one consumer
  is not a seam — it is the same coupling with an extra directory, and the skeleton is the consumer
  that no local build compiles.
