# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V1 (spec 003)** — `chore/style-enforcement` (merged c9d0922): EnforceCodeStyleInBuild +
  editorconfig var/top-level/namespace rules at build severity; explicit sealed Program/Main
  in both apps; repo-wide dotnet format sweep (163 files); review fixes (2-space web assets,
  vendored-asset exclusion, dropped unshipped 'closed' modifier). All suites green
  (319/196/206/56 + e2e 44/7).
