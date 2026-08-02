# 005 — Tasks

Execution breakdown for `005-plan.md`. Each branch: code + tests, Fable review before merge,
merge to `dev` `--no-ff`, unpushed. Tick on merge.

## B1 — NativeWeb `feat/boot-ux`

- [ ] boot.js (canonical `libs/nativeweb.assets/boot.js`): busy states — on
  `htmx:beforeRequest` disable + `aria-busy` + `.nw-busy` on the submit control, restore on
  `htmx:afterRequest`.
- [ ] boot.js: focus-after-swap — on `htmx:afterSwap`, when focus collapsed to `<body>`, focus
  first `[autofocus]` in swapped content else the swapped root via `tabindex="-1"`.
- [ ] Re-vendor byte-identical: `apps/web/wwwroot/vendor/nativeweb/boot.js`,
  `skeleton/apps/__product__/wwwroot/vendor/nativeweb/boot.js` (parity tests stay green).
- [ ] `.nw-busy` CSS in apps/web + skeleton `components.css`.
- [ ] `/version` endpoint beside `/healthz` (apps/web + skeleton), `robots.txt` disallow.
- [ ] Build stamp: `<Version>` + git-SHA `SourceRevisionId` MSBuild target ("dev" fallback).
- [ ] Tests: unit (version read), integration (`/version` 200 non-empty), e2e (busy mid-flight
  via delayed route, restored after; focus inside swapped content after error swap).
- [ ] Fable review; merge `--no-ff`.

## B2 — NativeWeb `feat/idp-ux-defaults`

- [ ] `Flash.cs` (`__Host-flash`, read-once) + flash slot in `Layout.PageOrFragment`; skeleton
  pattern-copy.
- [ ] Adopt flash at the nine `Redirects.To` success sites.
- [ ] Native constraint attributes on IdP forms mirroring server rules.
- [ ] Field-level errors on multi-field forms (settings profile, register, admin);
  sign-in/credential failures stay generic banners (enumeration defense).
- [ ] Access keys (2–3 primary actions) + `aria-keyshortcuts` + `title`.
- [ ] `FormChrome` refactor of the ~28 `BotTraps.Fields`/`AntiforgeryField` call-site pairs.
- [ ] Tests all three layers (flash once-then-gone JS-on/off; sign-in stays generic).
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
