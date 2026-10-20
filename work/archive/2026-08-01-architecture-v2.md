# Architecture v2 + manzil.my extraction (spec 003) — shipped 2026-08-01

Durable-tier work: the task breakdown of record is
`../../docs/specs/003-architecture-v2/003-tasks.md` (V1–V10, all ticked); this file is the
archive pointer, not a copy. Summarised in `../../CHANGELOG.md` under `## [Unreleased]`.

Close-out state:

- NativeWeb: 0-warning Release build; 349 + 75 unit / 236 integration / 49 e2e green;
  0-warning NativeAOT publish 36,918,272 B, 881 ms to `/healthz` (recorded as the CI budget
  baseline in `scripts/ci/publish-budgets.json`).
- manzil.my (fresh repo, extracted from DevMarks): 0-warning checkout-mode build;
  217 unit / 81 integration / 12 e2e green (axe + trap-visibility gates); 0-warning NativeAOT
  publish 30,483,968 B, 977 ms to `/healthz`.
- Screenshot check passed: manzil home + sign-in in en and ms (full translation, traps
  invisible), IdP static about + 404 shell.
- Cleanup candidates listed in `scripts/cleanup/architecture-v2.ps1` (written, never run).
