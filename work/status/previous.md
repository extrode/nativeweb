# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **T6 Search + T8 Import/export (spec 001, DevMarks)** — the two parallel-worker slices, both
  merged to dev: search as 1c7daf4 (FTS via `search_match()` in migration 0006 because JauntyQ
  can't parse `@@`; EXPLAIN confirmed `bookmarks_search_idx` applies), import/export as b5dc22f
  (format-sniffing POST /import, idempotent dedup, byte-identical JSON round trip proven in
  tests). Suites at merge: 196 unit / 49 integration / 5 e2e, zero AOT warnings.
