# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **T7 Public pages (spec 001, DevMarks)** — `/u/{username}` + `/u/{username}/t/{tag}`, tag
  visibility toggle on `/tags`, R17 404-indistinguishability (body-equality tested both ways).
  Merged to dev as 775db4a. Suites: 196 unit / 55 integration / 6 e2e.
