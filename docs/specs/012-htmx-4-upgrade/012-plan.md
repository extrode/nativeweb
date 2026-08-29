# 012 — Plan
Spec: ./012-spec.md

## Approach

Translate rather than redesign, with one deliberate feature adoption. The upgrade is a
three-file code change plus a documentation sweep: the vendored library, the enhancement layer
(`libs/nativeweb.assets/boot.js`, mirrored to two served copies), and the layout that carries the
antiforgery glue and the config meta. Everything else in the repo touches htmx only through
`data-nw-*`, which htmx never sees, or through `HX-*` request and response headers, which 4.0
leaves unchanged.

The translation is not a rename sweep, because the payload shape changed underneath the names.
Verified against the 4.0.0 source (`tmp/htmx4/htmx.js`, downloaded from the project's own
distribution and version-checked at `version = '4.0.0'`): every lifecycle event now carries
`{ctx}` and nothing else. `detail.elt`, `detail.target`, `detail.headers` and
`detail.requestConfig` do not exist in 4.0 — the equivalents live at `ctx.sourceElement`,
`ctx.target`, `ctx.request.headers` and `ctx.sourceEvent`. So every one of the six listeners in
scope is rewritten at the body, not just at the name.

The alternative considered and rejected was staging the move behind
`implicitInheritance: true` plus a compat shim, landing "on the new file" and "on the new
behaviour" as separate merges. Rejected because the inheritance surface here is nearly empty —
the audit is cheap, and the flag would be a permanent fixture in a template other projects copy.
Rollback is preserved instead by keeping the 2.0.10 file in the tree under its version, so revert
is a content swap rather than a re-download (R11).

Morph is adopted by exception, not by default. `htmx.config.defaultSwap` stays `innerHTML` and no
global morph is configured; individual targets opt in through `data-nw-swap`. The rule is that a
target may morph only if it carries no credential input and does not re-render itself as the
error surface for its own submission — which excludes every auth form and leaves the admin and
settings list regions as the candidates (R8).

## Tech / dependencies

- **htmx 4.0.0**, released 2026-08-28. Vendored from `https://unpkg.com/htmx.org@4.0.0/dist/`,
  which serves both `htmx.min.js` (36,716 B) and unminified `htmx.js` (102,533 B). The minified
  4.0.0 build is **28% smaller than the vendored 2.0.10** (51,238 B). Published under the `next`
  dist-tag; 2.x holds `latest` until early 2027.
- **No new runtime dependency.** `hx-alpine-compat` exists at `dist/ext/hx-alpine-compat.js`
  (HTTP 200 verified) and is evaluated in its own task, defaulting to *not adopted* absent a
  demonstrated conflict (R15).
- **No package manager.** The upstream `npx htmx.org@4.0.0 upgrade-check` tool is not used: npm
  is barred by AGENTS.md, and it reads templates for `hx-*` attributes that this repo does not
  have — every `hx-*` attribute here is derived at runtime by `boot.js` from `data-nw-*` (R17).
- Existing test stack unchanged: xUnit, `WebApplicationFactory` against real PostgreSQL, Playwright.

## Changes by area

### `libs/nativeweb.assets/boot.js` — the enhancement layer (R2, R3, R4)

Source-verified contract for each listener:

| Today (2.0.10) | 4.0.0 | Notes from the source |
|---|---|---|
| `htmx:beforeProcessNode` | `htmx:before:process` | Dispatched on the scanned root with **no detail** (`htmx.js:928`). `event.target` is still the root, so `sweep(event.target, …)` survives as-is. |
| `htmx:beforeRequest`, `detail.elt` | `htmx:before:request`, `detail.ctx.sourceElement` | `htmx.js:579`. |
| `detail.requestConfig.triggeringEvent.submitter` | `detail.ctx.request.submitter` | `htmx.js:506` assigns the submitter into `ctx.request` *before* `htmx:config:request` fires, so it is present for the whole lifecycle. This **simplifies** `busyControl()` — the two-step reach through `requestConfig` collapses to one field. |
| `htmx:afterRequest` | `htmx:after:request` (`:591`) plus new `htmx:finally:request` (`:623`) | Evaluate moving restore to `finally:request`, which is the later and broader hook. |
| `htmx:beforeSwap`, `detail.target` | `htmx:before:swap`, `detail.ctx.target` | Detail is `{ctx, tasks}` (`:1270`). |
| `htmx:afterSwap` | `htmx:after:swap`, `detail.ctx.target` | **Behavioural change:** `:1300` dispatches on `ctx.sourceElement`, where htmx 2 dispatched on each swapped-in element. `boot.js:218`'s fallback `target = event.target` therefore no longer yields the live replacement content and must be re-derived from `ctx`. |

Two documented 2.0.10 dependencies to re-verify rather than assume, both flagged in the file's own
comments:

- **The `defer` ordering gate** (`boot.js:81`, which says "re-verify on any bump"): boot must still
  register its `DOMContentLoaded` listener before htmx registers its own.
- **The microtask re-`process()`** (`boot.js:96–104`): it exists because htmx 2 snapshotted the
  node list before firing the event. In 4.0 `ctx` reads `hx-target`/`hx-swap` from the element at
  *request* time (`htmx.js:385–393`), not at process time, so the workaround may be unnecessary.
  It is removed only if a test proves it redundant, not on reading.

`hx-disable` is **not** introduced to replace the disable half of the busy layer: 4.0 sets no
`aria-busy` and does nothing for focus, so the trade is one attribute repeated per form against
fifteen lines of shared JavaScript — against spec 004's whole purpose.

### `apps/web/Render/Layout.cs` and `skeleton/apps/__product__/templates/layout/page.html` (R5, R6, R7, R9)

- `htmx:configRequest` → `htmx:config:request`; `event.detail.headers[k] = v` →
  `event.detail.ctx.request.headers[k] = v`. Verified a **plain object**, not a `Headers`
  instance — `htmx.js:493` assigns `ctx.request.headers["HX-Target"] = …` by index, so the glue
  keeps its current form and only moves (R5).
- `htmx-config` meta: `includeIndicatorStyles` → `includeIndicatorCSS` (`htmx.js:204`). The old
  key is silently ignored in 4.0, which would re-enable the injected indicator stylesheet that
  `components.css:321` documents as deliberately off.
- `noSwap` set to `[204, 304, "4xx", "5xx"]`. Verified at `htmx.js:2254–2258`: the matcher tests
  the status against `"404"`, `"40x"` and `"4xx"` in turn, so the string forms work alongside the
  numeric defaults. Without this, 4.0 swaps the body of every guard rejection — the 415 content-type
  and 400 antiforgery guards in `AdminEndpoints.cs` and `OAuthEndpoints.cs` — into the page, where
  htmx 2 did nothing (R7). Validation errors are unaffected either way: they already return
  `200 OK` with a fragment.
- `defaultTimeout` is newly 60,000 ms with no 2.x equivalent. Audit browser-reachable endpoints;
  set an allowance only if one is found (R9).
- No `implicitInheritance` — left at its `false` default (R6).

### Inheritance audit (R6)

Every `hx-*` attribute in this repo is derived per-element by `boot.js` onto the element that
owns the `data-nw-*`, so descendants inherit nothing by construction. The one real candidate is
`OAuthAuthorizePage.cs:17`, where two forms share the `#oauth-consent` target. The audit
enumerates each rendered page, and anything genuinely relying on a parent gets `:inherited`
appended to the attribute name (`htmx.js:287–296`). Expected outcome is zero changes; the task is
to prove that, not to assume it.

### Vendored assets (R1, R10, R11)

`apps/web/wwwroot/vendor/htmx/htmx.min.js` and
`skeleton/apps/__product__/wwwroot/vendor/htmx/htmx.min.js` are byte-identical today (md5
`6b99da76b7f7c677e7571a1047a92204`) and stay so. 2.0.10 is retained beside them under a
version-bearing name for one-file rollback. `LICENSE` is refreshed from the 4.0.0 distribution.
The three `boot.js` copies (`libs/nativeweb.assets/`, `apps/web/wwwroot/vendor/nativeweb/`,
`skeleton/…/vendor/nativeweb/`) stay in lockstep.

### Morph adoption (R8)

Candidates are non-credential regions where preserved scroll and uncommitted input is a win:
the independently-swappable realm and application regions in `AdminPage.cs` and the section
targets in `SettingsPage.cs`. Excluded by rule: `SignInPage`, `RegisterPage`,
`RegisterVerifyPage`, `PassphraseChangePage`, `PassphraseResetPage`, `PassphraseResetVerifyPage`,
`TwoFactorVerifyPage`, `TwoFactorEnrollPage`, `WebAuthnEnrollPage`, and the `OAuthAuthorizePage`
consent forms — each either carries a credential or is its own validation-error surface. Adoption
is per target via `data-nw-swap="innerMorph"`; `boot.js` passes the value through untouched, so no
enhancement-layer change is needed.

### Tests (R14)

- Unit: the render tests that assert `data-nw-*` and derived-attribute markup.
- Integration: unchanged — `HX-Request`/`HX-Redirect` header handling is untouched by 4.0.
- E2E: the regression net. `ProgressiveEnhancementTests` already asserts derived attributes, the
  busy triple and focus recovery; new assertions cover the guard-response non-swap (R7), the
  credential field cleared after a failed submit (R8), and morph preserving scroll on an adopted
  target (R8). Each new assertion is mutation-checked.

### Documentation (R12, R13)

`docs/01-architecture/progressive-enhancement.md` (16 references, including the event names at
lines 136–138), `docs/01-architecture/native-web-architecture.md` §15 — ticked or declined per
item, with the correction that `noSwap` takes an array including numeric codes, not bare `'4xx'`
strings, and that the beta-era `htmax.min.js` filename warning is void — plus
`docs/02-reference/security-baseline.md` (the removal of localStorage history caching is a
net improvement worth recording), `docs/conventions.md`, `docs/00-quick-start/tutorial-first-feature.md`,
`README.md`, `skeleton/AGENTS.md`, `AGENTS.md`, and `CHANGELOG.md` under `## [Unreleased]`.
The generated `dist/docs-site/` is rebuilt by its existing tooling, not hand-edited.

### Benchmark (R16)

Re-run §14's suite against 4.0 and record throughput and RSS beside the 2.x figures. The 28%
smaller payload predicts no regression, but `fetch()` replacing `XMLHttpRequest` is exactly the
kind of change that moves a tail latency without moving a mean.

## Data / interfaces

No schema, endpoint, or wire-format change. The `HX-Request`, `HX-Redirect` and `HX-Target`
headers keep their names and meanings in 4.0 (`HX-Target` confirmed at `htmx.js:493`), so every
server-side branch on them — `Layout.PageOrFragment`, `Redirects`, `ErrorPageMiddleware`,
`ForcedPassphraseResetMiddleware`, `SecurityHeadersMiddleware` — is untouched.

The one contract that changes is browser-internal: the enhancement layer's dependency on the
htmx event payload, restated above as `ctx`.

## Risks & mitigations

- **A day-old major with no compatibility layer.** Mitigated by R11: 2.0.10 stays in the tree and
  revert is a content swap. The e2e suite runs real browsers and is the gate.
- **`after:swap` moving to `sourceElement`** silently strands focus recovery instead of erroring —
  it would degrade accessibility quietly. Mitigated by re-deriving the target from `ctx` and by
  the existing focus-recovery e2e test, mutation-checked so it cannot pass vacuously.
- **Guard responses swapping into the page (R7).** Left unhandled, a 415 or 400 body from an
  antiforgery or content-type guard would render inside the page — an information-disclosure
  shape, not just a cosmetic one. `noSwap` is set in the same change as the library swap, never
  after.
- **Morph retaining a credential.** The reason auth forms are excluded by rule rather than by
  case-by-case judgement. The exclusion is asserted by test, not left to review.
- **The 60-second default timeout** silently aborting a slow endpoint that previously ran to
  completion. R9's audit is a task, not a note.
- **Three `boot.js` copies drifting.** Mitigated by a byte-identity check in the same task that
  edits them.
- **`hx-disable` meaning-flip.** In 4.0 `hx-disable` disables during a request and the old
  "don't process this subtree" meaning moved to `hx-ignore` (`htmx.js:~#ignore`). Neither
  attribute appears in this repo, so this is a documentation-only item — recorded because the
  silent reuse of a name is a trap for anyone copying the skeleton.

## Coverage check

| R# | Covered by |
|---|---|
| R1 | Vendored assets |
| R2 | boot.js — normalization rows; `before:process` detail finding |
| R3 | boot.js — `before:request` / `after:request` / `finally:request` rows |
| R4 | boot.js — `before:swap` / `after:swap` rows, incl. the dispatch-target change |
| R5 | Layout — `config:request`, headers verified a plain object |
| R6 | Inheritance audit |
| R7 | Layout — `noSwap`; Risks; E2E guard-response assertion |
| R8 | Morph adoption; E2E credential-cleared and scroll-preserved assertions |
| R9 | Layout — `defaultTimeout` audit |
| R10 | Vendored assets — byte-identity check |
| R11 | Vendored assets — 2.0.10 retained |
| R12 | Documentation — §15 ticked or declined per item |
| R13 | Documentation |
| R14 | Tests |
| R15 | Tech/dependencies — `hx-alpine-compat` evaluated, default not adopted |
| R16 | Benchmark |
| R17 | Tech/dependencies — no npm, upgrade-check tool not used |

No requirement uncovered.
