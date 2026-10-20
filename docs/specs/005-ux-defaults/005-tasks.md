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
- [x] Fable review ($3.00 warm, 31 turns: 2 LOW fixed/accepted, 2 INFO fixed) -- round also
  led to the outerHTML afterSwap detail.target fix in boot.js (recovery was dead code for
  outerHTML swaps; htmx settle autofocus had been masking it). Merged `--no-ff` (ae0adf2).

## B3 — manzil.my `feat/ux-defaults`

- [x] Vendor updated boot.js; parity test green.
- [x] `/version` + build stamp; `robots.txt` disallow.
- [x] `Flash.cs` pattern-copy + flash slot in `layout/page.html` render path (tuple API:
  Take returns (Ok, Error) empty strings feeding the banner partials' no-op path).
- [x] `form-field.html`: optional `MaxLength`/`MinLength`/`Pattern`/`Inputmode` params; callers
  pass server-rule values (username maxlength 39 + brace-free pattern, passphrase 12/128;
  sign-in fields carry none — enumeration defense).
- [x] Real field errors threaded through register + settings; flash adopted at 15 success
  sites incl. bot-trap fake-success paths (identical Set — R23 indistinguishability).
- [x] Access keys (new bookmark Alt+N, search Alt+S, settings Alt+O).
- [x] `FormGuard` extraction (six copies → 1 helper) incl. 415-order fix, pinned by test.
- [x] `.nw-busy` CSS.
- [x] Tests all three layers (unit 228, integration 92, e2e 21 — all green on the merged
  commit).
- [x] Fable review ($3.14 warm, 44 turns: 1 MED settings-trap latency parity + 4 LOW, all
  addressed). Merged `--no-ff` (2185711).

## B4 — NativeWeb `docs/pe-doc-fold`

- [x] Fold `progressive-enhancement-2.md` into `progressive-enhancement.md` (adopted +
  rejected-with-reasons + R12 content-type convention); delete the v2 file (plan-authorized).
- [x] Cross-link from `docs/conventions.md`.
- [x] Spec 005 Problem paragraph corrected (R1 pre-existed via spec 003 R39).
- [x] Fable review (1 HIGH lost browser-native-first content + 2 MED, all addressed —
  focus-recovery wording matched to shipped boot.js, four undispositioned v2 proposals
  recorded). Merged `--no-ff` (99d6cb5).

## B3.5 — NativeWeb `feat/skeleton-flash` (unplanned, deferred from B2)

- [x] Skeleton flash slot: `Flash.cs` (manzil-canon tuple API), banner partials,
  FlashOk/FlashError layout params, read-once Take at the home render, FlashTests.
  Verified by stamping a scratch product (25 unit tests green); surrogate back-off test
  back-ported to apps/web. Fable review (1 BLOCKER canon-contract wording + 1 MED + 2 LOW,
  all addressed). Merged `--no-ff` (c1c960c).

## Close-out

- [x] All boxes above ticked; R1 verified (error-page tests green, nothing built).
- [x] CHANGELOG `[Unreleased]`; tasklist archived; status rolled.
- [x] `scripts/cleanup/ux-defaults.ps1` written (never run).
- [x] Structure audit clean; suite counts + publish sizes recorded here.

Final state 2026-08-02: NativeWeb suites 92 unit / 368 integration / 237 e2e / 56 render at
B2 merge, plus the skeleton-flash additions (apps/web FlashTests 10, stamped-skeleton unit 25).
manzil suites 228 unit / 92 integration / 21 e2e, all green on the merged commit; manzil
NativeAOT Release publish 30,494,720 bytes (~29.1 MB), zero warnings. All merges into each
repo's `dev`, unpushed.
