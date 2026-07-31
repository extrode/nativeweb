# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **H7 (spec 002, IdP hardening)** — `chore/db-role-rename`: neutral `nativeweb_app` role in
  `db/migrate.sh`, ci.yml and local dev (merged aa8829f). As built: created `nativeweb_app` +
  `REASSIGN OWNED` of `nativeweb_dev` instead of a cluster-wide rename, because `epass_app`
  also owns other projects' local databases. Last of the seven H-branches; H1–H6
  merged earlier the same day (4011ea4 H5, 2aff254 H6, see `git log dev`).
