# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V10 (spec 003)** — `feat/manzil-extraction` + `chore/remove-devmarks` (both merged into
  `dev`): `scripts/extract-manzil.ps1` (dry-run default, `-Execute`) created
  `C:\home\code\extrode-legacy-org.com\manzil.my` — full rename (nativeweb.devmarks* → manzil*,
  DevMarks → Manzil, devmarks_dev/app → manzil_dev/app), db/devmarks flattened to db/,
  skeleton scaffolding stamped, framework refs repointed to the `$(NativeWebRoot)`
  dual-mode split, fresh git history (main + dev). manzil.my: 0-warning checkout-mode
  build, 217 unit / 81 integration / 12 e2e green (axe + trap visibility through a real
  AOT publish). Review fixes: db/ path flattening in the rename map, partial-destination
  cleanup on failure, dry-run-exits-0 contract, LF-normalised patch anchors,
  skeleton/.gitattributes. Then DevMarks removed from NativeWeb (projects, slnx, CI
  steps, budgets entry, stale comments; security-baseline/conventions rescoped to the
  IdP) — NativeWeb still 0-warning, 349+75 unit / 236 integration / 49 e2e green.
