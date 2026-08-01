# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V7 (spec 003)** — `feat/perf-ops` (merged 7e5b308): Brotli for text/html only with
  `EnableForHttps` and a documented, review-hardened BREACH acceptance (per-render secret
  inventory incl. consent code + TOTP enroll secret recorded as a load-bearing invariant);
  OTel metrics+traces (AspNetCore/Runtime/Npgsql-by-name, OTLP) behind
  `{NativeWeb,DevMarks}:Telemetry:Enabled` — in-proc boot tests plus the CI budget step
  launching the real AOT exe with the flag ON; `Max Auto Prepare=20` both apps
  (configurable; must be 0 behind transaction-pooling PgBouncer); FrozenDictionary
  `AssetUrls`; `StripSymbols`/`OptimizationPreference=Speed` in the app csprojs (NETSDK1207
  if put in Directory.Build.props); `scripts/ci/check-publish-budgets.ps1` size +
  time-to-/healthz gates, record-only until baselined (local: web 36,886,016 B / ~560 ms,
  devmarks 30,439,936 B / ~550 ms). Suites green (web 335/225/49, devmarks 196/69/12).
