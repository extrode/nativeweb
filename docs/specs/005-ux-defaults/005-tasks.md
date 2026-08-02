# 005 — Tasks

Execution breakdown for `005-plan.md`. Each branch: code + tests, Fable review before merge,
merge to `dev` `--no-ff`, unpushed. Tick on merge.

## B1 — NativeWeb `feat/boot-ux`

- [x] boot.js (canonical `libs/nativeweb.assets/boot.js`): busy states — on
  `htmx:beforeRequest` disable + `aria-busy` + `.nw-busy` on the submit control, restore on
  `htmx:afterRequest`.
- [x] boot.js: focus-after-swap — on `htmx:afterSwap`, when focus collapsed to `<body>`, focus
  first `[autofocus]` in swapped content else the swapped root via `tabindex="-1"`.
- [x] Re-vendor byte-identical: `apps/web/wwwroot/vendor/nativeweb/boot.js`,
  `skeleton/apps/__product__/wwwroot/vendor/nativeweb/boot.js` (parity tests stay green).
- [x] `.nw-busy` CSS in apps/web + skeleton `components.css`.
- [x] `/version` endpoint beside `/healthz` (apps/web + skeleton), `robots.txt` disallow.
- [x] Build stamp: `<Version>` + git-SHA `SourceRevisionId` MSBuild target ("dev" fallback).
- [x] Tests: unit (version read), integration (`/version` 200 non-empty), e2e (busy mid-flight
  via delayed route, restored after; focus inside swapped content after error swap).
- [x] Fable review; merge `--no-ff`.

## B2 — NativeWeb `feat/idp-ux-defaults`

- [x] `Flash.cs` (`__Host-nw-flash`, read-once) + flash slot in `Layout.Page` (full-page path
  only; fragments never flash). Skeleton pattern-copy moved to B3 — the skeleton shares the
  manzil template shape (Chrome + page.html), not the IdP's Layout.cs, so the slot is designed
  once there.
- [x] Adopt flash at every success `Redirects.To` site where a banner was dropped: audit found
  exactly two qualify (passphrase-reset success -> /auth/signin, registration completion -> /);
  the rest are mid-flow steps, the external OAuth redirect, or pages with inline success
  rendering (passphrase change).
- [x] Native constraint attributes on IdP forms mirroring server rules (audit found nearly all
  already present; the gap was the realm-slug `pattern` + title, added).
- [x] Field-level errors on multi-field forms (settings profile incl. separate last-name check,
  register email/passphrase/confirm, admin realm name/slug + duplicate slug);
  sign-in/credential failures stay generic banners (enumeration defense), pinned by test.
- [x] Access keys: home-page Settings [Alt+S] and Admin [Alt+A] links, `aria-keyshortcuts` +
  `title`.
- [x] `FormChrome` refactor of the `BotTraps.Fields`/`AntiforgeryField` call-site pairs (17
  production sites + all render signatures); IdP `SwapFocusScript` removed (boot.js owns focus
  recovery), skeleton's inline afterSwap focus script removed for the same reason.
- [x] Tests all three layers (flash roundtrip units; once-then-gone integration + e2e; field
  errors unit/integration/e2e; sign-in stays generic). Exposed and fixed a real boot.js gap:
  disabling the focused submitter dropped focus to body before the swap, defeating the
  swap-removed-focus check — busy layer now tracks and restores stolen focus.
- [ ] Fable review; merge `--no-ff`.

## B3 — manzil.my `feat/ux-defaults`

- [ ] Vendor updated boot.js; parity test green.
- [ ] `/version` + build stamp; `robots.txt` disallow.
- [ ] `Flash.cs` pattern-copy + flash slot in `layout/page.html` render path.
- [ ] `form-field.html`: optional `MaxLength`/`MinLength`/`Pattern`/`Inputmode` params; callers
  pass server-rule values.
- [ ] Real field errors threaded through register + settings.
- [ ] Access keys (new bookmark, search, settings).
- [ ] `ValidateAsync` extraction (4 copies → 1 helper) incl. 415-order fix.
- [ ] `.nw-busy` CSS.
- [ ] Tests all three layers.
- [ ] Fable review; merge `--no-ff`.

## B4 — NativeWeb `docs/pe-doc-fold`

- [ ] Fold `progressive-enhancement-2.md` into `progressive-enhancement.md` (adopted +
  rejected-with-reasons + R12 content-type convention); delete the v2 file (plan-authorized).
- [ ] Cross-link from `docs/conventions.md`.
- [ ] Spec 005 Problem paragraph corrected (R1 pre-existed via spec 003 R39).
- [ ] Merge `--no-ff` (docs branch).

## Close-out

- [ ] All boxes above ticked; R1 verified (error-page tests green, nothing built).
- [ ] CHANGELOG `[Unreleased]`; tasklist archived; status rolled.
- [ ] `scripts/cleanup/ux-defaults.ps1` written (never run).
- [ ] Structure audit clean; suite counts + publish sizes recorded here.
