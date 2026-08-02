# Spec 005 — built-in UX defaults (shipped 2026-08-02)

Durable tier; the full task breakdown with results stays at
`docs/specs/005-ux-defaults/005-tasks.md` (all ticked — pointed at, not duplicated).

Branches, all merged into their repo's `dev` with `--no-ff`, unpushed:
- B1 NativeWeb `feat/boot-ux` — boot.js busy states + focus-after-swap, `/version` + build
  stamp (apps/web + skeleton), re-vendor, parity/unit/integration/e2e.
- B2 NativeWeb `feat/idp-ux-defaults` (ae0adf2) — IdP flash + adoption, field errors, native
  constraints, access keys, FormChrome refactor.
- B3 manzil.my `feat/ux-defaults` (2185711) — full manzil retrofit; suites 228/92/21.
- B3.5 NativeWeb `feat/skeleton-flash` (c1c960c) — skeleton flash slot (unplanned, deferred
  from B2).
- B4 NativeWeb `docs/pe-doc-fold` (99d6cb5) — v2 PE doc folded into
  `docs/progressive-enhancement.md` and deleted.

Cleanup candidates in `scripts/cleanup/ux-defaults.ps1` (dry-run default, never run by agents).
