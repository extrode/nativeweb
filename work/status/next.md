# Next

Exactly **one** task, the next one to start, pulled from `../tasklist.md` when the rotation
advances. The full task list for this body of work is `../tasklist.md`; the unscheduled backlog is
`../todo.md`; where the project is going is `../milestones.md`.

- **The epass.io production cutover.** Sign-off gated. Telemetry is no longer part of this: it is
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
