# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **Spec 003 close-out** — both repos green across all three suites (NativeWeb 349+75/236/49,
  manzil.my 217/81/12); zero-warning NativeAOT publishes (web 36,918,272 B / 881 ms,
  manzil 30,483,968 B / 977 ms to `/healthz`); web baseline recorded as the CI budget
  (`scripts/ci/publish-budgets.json`: 42.5 MB / 5000 ms ceilings); screenshots verified
  (manzil en+ms home + sign-in, traps invisible; IdP about + 404 shell); CHANGELOG entry
  under [Unreleased]; archive pointer `work/archive/2026-08-01-architecture-v2.md`;
  `scripts/cleanup/architecture-v2.ps1` written (never run). Spec 003 fully shipped.
