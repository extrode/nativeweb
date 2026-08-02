# Built-in UX defaults (spec 005) — technical plan

> Approved 2026-08-02. Records the plan-mode design for `005-spec.md` (R1–R12).

## Context

Spec 005 adds the UX layer every LNW app should get for free: busy feedback, flash messages,
keyboard polish, native+field-level validation, `/version`, and folds the v2 conventions doc
into the main progressive-enhancement doc. Scope decisions taken with the user 2026-08-02:
error pages + /version + busy states + flash + keyboard nav + validation in; typed IDs deferred
(JauntyQ converter dependency, noted in `work/todo.md`); RSS is document-only (apps build their
own media types on the Minimal-API result primitives — already proven by security.txt, favicon
bytes, transfer export); refactor-as-you-touch is a requirement (R11); perf tuning is a separate
follow-up body of work.

**Exploration findings that narrow the spec:**
- **R1 (error pages) is already done** in both apps AND the skeleton: `ErrorPageMiddleware` +
  `StaticErrorShell` (spec 003 R39) render designed pages for 400/401/403/404/405/413/429/500,
  HX-aware (fragment for htmx, static shell for navigations), render-layer-independent
  (`manzil/Middleware/ErrorPageMiddleware.cs`, `apps/web/Program.cs:536`). R1 becomes
  verify-only; the spec's Problem paragraph gets a one-line correction in the first commit.
- **R5 is half done**: skip links + `main tabindex="-1"` + `:focus-visible` rings exist in both
  apps and the skeleton (`apps/web/Render/Layout.cs:97`, `manzil templates/layout/page.html:58`,
  `apps/web/wwwroot/css/components.css:16-66`). Remaining: access keys and the focus-after-swap
  gap (R6).
- **R7/R8 groundwork exists in manzil**: `templates/shared/form-field.html` already renders
  `required`, `aria-invalid`, `aria-describedby`, and a `role=alert` field error — every caller
  just passes `Error=""` (deferred V4 finding). The work is threading real values, not building
  the mechanism.
- Apps' csprojs set no `<Version>` (libs pin 0.1.0); `/version` needs a build-time stamp.
- The established cross-app sharing pattern for C# is **pattern-copy, kept structurally
  identical** (ErrorPageMiddleware's own comment); only boot.js has a canonical-file+vendored
  mechanism. Flash follows the pattern-copy convention.

## Design

### 1. boot.js additions — busy states (R3) + focus-after-swap (R6)
Extend `libs/nativeweb.assets/boot.js` (canonical), re-vendor byte-identical to the three
copies (apps/web, skeleton, manzil), parity tests already exist and stay.

- **Busy**: on `htmx:beforeRequest`, resolve the triggering element (`evt.detail.elt`); if it is
  (or contains) a submit control, set `disabled`, `aria-busy="true"`, and class `nw-busy` on it.
  On `htmx:afterRequest` (fires on success, error, and abort alike) restore. `beforeRequest`
  fires after form serialization, so disabling cannot drop the submitter's value. No per-form
  markup; zero work when idle. Small CSS addition per app: `.nw-busy` gets a reduced-opacity +
  spinner treatment consistent with each app's look.
- **Focus**: on `htmx:afterSwap`, if `document.activeElement` collapsed to `<body>` (the swap
  removed the focused element), focus the first `[autofocus]` inside the swapped content, else
  the swapped root itself via `tabindex="-1"` (same technique the layouts' `main` already uses).
  Never steal focus that survived the swap.

### 2. `/version` (R2)
- `Directory.Build.props` (apps only, or per-app csproj): set `<Version>` and an MSBuild target
  that stamps `SourceRevisionId` from `git rev-parse --short HEAD` at build (falls back to
  "dev" when git is unavailable), giving `InformationalVersion` = `x.y.z+<sha>`.
- Endpoint: `app.MapGet("/version", …)` beside `/healthz` in both apps + skeleton, returning
  `Results.Text` of `AssemblyInformationalVersionAttribute` read once at startup (AOT-safe,
  attribute read — no reflection over members). Unauthenticated, same rate-limit group and
  middleware exemptions as `/healthz`, added to `robots.txt` disallow.

## 3. Flash messages (R4)
One mechanism covers both transports because success navigation is already unified through
`Redirects.To` (real 302 no-JS, `HX-Redirect` full navigation enhanced) — either way the next
request is a fresh page load:

- `Flash.Set(context, Kind, message)` writes a `__Host-flash` cookie (Secure, Path=/, HttpOnly,
  SameSite=Strict, session-lifetime; value = `kind:message`, size-capped). Both apps' session
  cookies are already `__Host-`, so the prefix works everywhere the apps run.
- The layout render path (`Layout.PageOrFragment` in the IdP; the page-render helper feeding
  `layout/page.html` in manzil, via a new optional flash slot in the template) reads the request
  cookie, expires it in the response, and renders the existing banner partials
  (`shared/banner-ok.html` / `banner-error.html`; IdP `Render/Banner.cs`). Read-once: reload
  after the flashed page shows nothing.
- `Flash.cs` is pattern-copied between the apps and the skeleton, structurally identical.
- Adopt at the nine IdP `Redirects.To` success sites and manzil's settings/sign-in-adjacent
  success redirects where a banner is currently dropped on the floor or hand-rolled.

### 4. Keyboard (R5 remainder)
- **Access keys**: 2–3 primary actions per app carry `accesskey` + `aria-keyshortcuts` + the
  combo in `title`: manzil — new bookmark, search field, settings; IdP — settings, admin (when
  authorized). Exact letters chosen at implementation avoiding browser-reserved combos; recorded
  in the conventions doc.
- Verify (not build — already present): tab order, skip link first, visible focus, in e2e.

### 5. Validation (R7, R8)
- **Native constraints (R7)**: `form-field.html` gains optional `MaxLength`/`MinLength`/
  `Pattern`/`Inputmode` params emitted only when non-empty; manzil callers pass values mirroring
  the server rules (username/passphrase/tag/URL lengths). IdP C# form strings get the same
  attributes matching their server checks. Server validation is untouched and remains
  authoritative.
- **Field-level server errors (R8)**: thread real per-field errors through manzil register +
  settings (the `Error` param exists; today always `""`) and the IdP's multi-field forms
  (settings profile, register, admin create-forms) via each form's existing error parameter
  plumbing. **Constraint: sign-in and other credential-failure paths keep the generic
  page-level banner** — enumeration defense forbids field-specific "which part was wrong"
  errors there. Non-field failures keep banners everywhere.

### 6. Refactor-as-you-touch (R11) — named targets
Only code the above passes through; untouched files stay untouched:
- manzil: the four structurally-identical `ValidateAsync` copies (Auth/Bookmarks/Tags/Transfer
  endpoint files) → one shared helper; fixes the known check-order nit (antiforgery before
  `HasFormContentType` → wrong 400 instead of 415) as part of the extraction.
- IdP: the `BotTraps.Fields(context), Layout.AntiforgeryField(context, antiforgery)` pair passed
  at ~28 render call sites → a single `FormChrome` value built once per request and passed as
  one argument (also shrinks the surface of the "empty-default antiforgery" regression class
  noted in todo).
- Flash adoption replaces the per-endpoint hand-rolled success-banner plumbing it supersedes.

### 7. Docs (R9, R12)
Fold `docs/progressive-enhancement-2.md` into `docs/progressive-enhancement.md`: adopted
conventions merged in place (browser-native-first list, busy/flash/validation conventions,
any-content-type rule R12 with security.txt/favicon/export as the examples), rejected items
recorded with reasons (bare attributes, `data-lnw-method` + dual routes, `#errors` container,
route DSL, `RequestContext`). **`docs/progressive-enhancement-2.md` is then deleted** (its
content lives on in the main doc; approval of this plan authorizes that deletion).
Cross-link from `docs/conventions.md`. Spec 005's Problem paragraph corrected re R1.

## Branch sequence (each off `dev`, code+tests, Fable review before merge, `--no-ff`)

| # | Repo | Branch | Delivers |
|---|------|--------|----------|
| B1 | NativeWeb | `feat/boot-ux` | boot.js busy+focus, re-vendor (web+skeleton), CSS, `/version` + build stamp (web+skeleton), parity/unit/integration/e2e |
| B2 | NativeWeb | `feat/idp-ux-defaults` | IdP flash + adoption, validation attrs, field errors, access keys, `FormChrome` refactor, tests |
| B3 | manzil.my | `feat/ux-defaults` | vendor boot.js, `/version`, flash (pattern-copy), form-field params + real field errors, access keys, `ValidateAsync` extraction, tests |
| B4 | NativeWeb | `docs/pe-doc-fold` | doc fold + v2 deletion, conventions cross-link, spec R1 correction |
| — | both | close-out | tasks ticked, CHANGELOG, archive, cleanup script |

B1 first (boot.js and version are what B2/B3 vendor and adopt). B2 and B3 are independent after
B1. B4 last so the doc records what actually shipped.

## Verification
- Unit: flash parse/roundtrip, version-string read, `FormChrome`, extracted `ValidateAsync`
  (including the 415-order fix), boot.js vendored-copy parity (existing tests keep passing).
- Integration: `/version` 200 + non-empty in both apps; flash cookie set on success redirect and
  expired on next render; validation attributes present in rendered forms; field-level error
  markup (`aria-invalid`, `role=alert`) on a failing register/settings POST; sign-in failure
  still renders only the generic banner.
- e2e (Playwright): busy — intercept the route with a delayed fulfillment, assert the submit is
  `disabled`/`aria-busy` mid-flight and restored after; flash — action → banner once → reload →
  gone, JS-on and JS-off; keyboard — tab-only sign-in/CRUD walk, skip-link first stop, focus
  inside swapped content after an error swap; native validation — JS off, empty required field
  never reaches the server.
- Both repos: all three suites green per branch and at end state; zero-warning NativeAOT publish
  within existing size budgets; suites' existing enhanced-flow and no-JS tests unchanged.
- Perf tuning explicitly not in this plan — next body of work after 005 ships.
