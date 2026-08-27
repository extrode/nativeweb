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
  repo it replaces for the first time and found `apps/web` is not a drop-in replacement. Whether
  NativeWeb replaces all of epass.io or only its browser surface is undecided, and nothing below
  can be scheduled until it is.
  Telemetry is no longer part of this: it is
  on by default as of 2026-08-17, so there is no flag left to flip during the switch. What is
  left is pointing `OTEL_EXPORTER_OTLP_ENDPOINT` at a collector, which needs a collector to
  exist — an operator decision, not work.
- Behind it, the `work/todo.md` items that need a decision rather than work: the skeleton's
  release process (whether NativeWeb versions and publishes itself), the session key type
  (JauntyQ-wide), and pinning the skeleton's two framework checkouts to tags, which cannot happen
  until either framework repo publishes one. The Node 20 → 24 action bump that used to sit here is
  **done** (2026-08-20) — see `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.
- Also unscheduled and worth naming: `work/milestones.md` is still the empty placeholder row. It
  has never been filled in, so "where this project is going" is recorded nowhere but in prose.
