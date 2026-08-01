# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V9 (spec 003)** — `feat/skeleton` (merged into `dev`): `skeleton/` tokenized
  (`__PRODUCT__`/`__product__`) product shell — full v2 pipeline, templates/locales,
  static pages, JauntyQ data lib, db runner, three test tiers with fixtures + smoke
  tests, ci.yml (package mode wired, feed-gapped), systemd unit + Windows service
  script, operating files, ASVS checklist. `tools/new-product.ps1` stamps it (dry-run
  default, `-Execute`). Six framework libs got pack metadata (private feed only;
  generator packs as analyzer). Dual review fixes: opt-in-only package mode + sentinel
  checkout probes, UMask/700 key dir in the systemd unit, NetworkService on Windows,
  drained fixture streams, axe 200-gate, PrivateAssets on generator refs, CI via
  migrate.sh + clang install + env-var flags to the inner publish. Verified by
  stamping a scratch product: 0-warning build, 15/5/2 green incl. axe through a real
  AOT publish (the gate caught a topbar contrast bug — fixed by seeding the product
  css layer).
