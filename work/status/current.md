# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **`feat/enable-telemetry`** — the user's 2026-08-17 decision to turn telemetry on, recorded in
  `docs/plans/2026-08-17-004-enable-telemetry.md`. Gate inverts to opt-out, resource gains a
  service identity, `QueryRedactingProcessor` pins the query-value invariant the package would
  otherwise let a host switch off, and `NpgsqlTelemetryTests` holds the four driver defaults that
  stand between this IdP and a collector. Ten perturbations, all red.
- Suite counts: **468 unit / 274 integration / 154 render / 62 e2e**, all green. NativeAOT publish
  clean: 35.2 MB, 586 ms to `/healthz`, budgets exit 0.
