# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **T9 Hardening (spec 001, DevMarks)** — fixed-window rate limiting (auth/public by IP,
  save/import by user, `DevMarks:RateLimit:*` config, 429 tested), request-logging middleware,
  no-JS e2e flow (register/save/search/sign-out with JS off at the browser), small-screen pass
  (topbar wrap fix), `/tags` restyle, `apps/devmarks/README.md`, acceptance-criteria sweep,
  `scripts/cleanup/devmarks-app.ps1`. Merged to dev as 6329966. Suites: 196 unit /
  56 integration / 7 e2e; Release AOT publish clean. Spec 001 fully ticked.
