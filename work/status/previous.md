# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **Spec 004: progressive enhancement + formatted HTML** — `data-nw-*` layer (boot.js,
  vendored + parity-tested in both apps), generator pretty-printer, both apps retrofitted;
  the IdP gained true no-JS support (hidden antiforgery fields everywhere via
  `Layout.AntiforgeryField`, real 302s for plain browsers via `Redirects.To`). Both repos
  green (NativeWeb 92+351/236/54, manzil 218/81/19); zero-warning publishes (web 35.2 MB of
  the 42.5 MB budget, manzil 29.1 MB); view-source screenshots confirm indented output from
  both published binaries; two Fable review rounds on boot.js, one each on B4/B5, all
  findings fixed. Archive pointer `work/archive/2026-08-02-progressive-enhancement.md`;
  `scripts/cleanup/progressive-enhancement.ps1` written (never run).
