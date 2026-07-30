# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **T2 Accounts (spec 001, DevMarks)** — register/sign-in/sign-out/settings, opaque-cookie DB
  sessions (14-day sliding), lockout via LockoutPolicy, offline+HIBP breach checking, uniform
  sign-in failures (R3), real no-JS forms. 51 unit / 16 integration / 4 e2e green. Merged to
  `dev` alongside the T4 metadata-fetcher and T8 codec components.
