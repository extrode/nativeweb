# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V4 (spec 003)** — `feat/devmarks-templates` (merged fe55bf7): all 16 DevMarks
  string-builder pages replaced by compiled templates + full en/ms catalogs (English
  byte-identical), endpoints rewritten via the R42 scripts kept under `tools/`, old Render
  pages deleted. New axe-core e2e gate (Deque.AxeCore.Playwright) over every e2e-covered
  page caught four real WCAG failures (light --t3 on --bg, color-only auth links, active
  nav at 4.1:1, private badge at 4.37:1), all fixed. Review (3 opus workers): 13+10+13
  findings, the conversion-scope ones fixed in-branch, parity/pre-existing ones deferred
  to work/todo.md. 196 unit + 56 integration + 8 e2e green, zero-warning build.
