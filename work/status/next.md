# Next

Exactly **one** task, the next one to start, pulled from `../tasklist.md` when the rotation
advances. The full task list for this body of work is `../tasklist.md`; the unscheduled backlog is
`../todo.md`; where the project is going is `../milestones.md`.

- **T15 of spec 011 -- run the deploy scripts against a real host.** Ahead of the cutover, and
  not gated on it: `scripts/deploy/provision.sh` has never been executed, the restore drill has
  never been run, and every elapsed time in `docs/02-reference/deployment.md` reads
  `NOT YET MEASURED`. Until an operator walks a bare Debian host through provision → release →
  rollback → restore and writes the times back, three of spec 011's requirements (R1, R16, R17)
  are unmet and the scripts are untested against anything but `bash -n` and the unit tier.
  R15 is conditional too: `BACKUP_HEARTBEAT_URL` is unset, so a backup that stops running is not
  noticed. This is a person's task, not a session's.

- **The epass.io production cutover.** Sign-off gated, and as of 2026-08-25 gated on something
  larger: `docs/architecture/002-epass-cutover-capability-gap.md` measured NativeWeb against the
  repo it replaces for the first time and found `apps/web` is not a drop-in replacement. That
  scope question was **answered 2026-08-29, in the epass.io repo**
  (`epass.io/docs/decisions/2026-08-29-003-native-web-browser-surface.md`): the browser surface
  only, DB-free, calling `apps/api` and `/internal/store` over HTTP, with `apps/api` remaining the
  sole writer to the identity database. The port is built in that repo, not this one; findings
  from here that reach it go to `epass.io/work/todo.md`.
  Telemetry is no longer part of this: it is
  on by default as of 2026-08-17, so there is no flag left to flip during the switch. What is
  left is pointing `OTEL_EXPORTER_OTLP_ENDPOINT` at a collector, which needs a collector to
  exist — an operator decision, not work.
- Behind it, the `work/todo.md` items that need a decision rather than work: the skeleton's
  release process (whether NativeWeb versions and publishes itself), the session key type
  (JauntyQ-wide), and pinning the skeleton's two framework checkouts to tags, which cannot happen
  until either framework repo publishes one. The Node 20 → 24 action bump that used to sit here is
  **done** (2026-08-20) — see `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.
- `work/milestones.md` was filled on 2026-08-30 with six destinations, so "where this project is
  going" now has a record. Three of the six are `in progress`; T15 above is milestone 3.
