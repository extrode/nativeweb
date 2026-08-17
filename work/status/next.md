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
  (JauntyQ-wide), and the Node 20 → 24 action bump (blocked upstream).
