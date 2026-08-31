# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Phase 3 of the readiness plan.** T11 (the scaffold defect batch) and T12 (deploy and database
  hygiene) are both done. T12: the deploy chain publishes `apps/sample` by default and names no
  project past `build.sh`; `db/migrate.sh` was retargeted at `db/sample/migrations` rather than
  retired, with the superuser extensions step made optional; and the publish-budget launch now sets
  `Sample__Telemetry__Enabled`, so the OpenTelemetry registration runs inside the AOT image again —
  it had been set under the wrong configuration section since `apps/web` left, and nothing was red.
  Nothing in `scripts/deploy/` has still ever run on a real host: that is T15, below. **D2 is
  answered (2026-08-31): the generated admin stays open by default** — an unguarded model is
  reachable anonymously, and no consumer breaks. Plan 007 is therefore complete: phases 1, 2 and 3
  are all built and merged, and what is left of it is the two decisions, both now taken (D1: ship
  without auth for now; D2: open by default).

- **T15 of spec 011 -- run the deploy scripts against a real host.** Ahead of the cutover, and
  not gated on it: `scripts/deploy/provision.sh` has never been executed, the restore drill has
  never been run, and every elapsed time in `docs/02-reference/deployment.md` reads
  `NOT YET MEASURED`. Until an operator walks a bare Debian host through provision → release →
  rollback → restore and writes the times back, three of spec 011's requirements (R1, R16, R17)
  are unmet and the scripts are untested against anything but `bash -n` and the unit tier.
  R15 is conditional too: `BACKUP_HEARTBEAT_URL` is unset, so a backup that stops running is not
  noticed. This is a person's task, not a session's.

- ~~**The epass.io production cutover.**~~ **Closed 2026-08-31 as a NativeWeb concern** —
  `docs/decisions/2026-08-31-008-epass-cutover-closed.md`. Checked against the real sibling
  checkout that day: epass.io has built and merged its own browser surface, consumes only
  `libs/nativeweb.render` and `libs/nativeweb.security` from here, uses nothing the identity
  provider's departure deleted from either, and its red CI has three causes all in that repo.
  Nothing is owed to it but a push of `dev`, which is the user's call. **The term is retired
  here; do not reopen it from an older document.**
- Behind it, the `work/todo.md` items that need a decision rather than work: the skeleton's
  release process (whether NativeWeb versions and publishes itself), the session key type
  (JauntyQ-wide), and pinning the skeleton's two framework checkouts to tags, which cannot happen
  until either framework repo publishes one. The Node 20 → 24 action bump that used to sit here is
  **done** (2026-08-20) — see `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.
- `work/milestones.md` was filled on 2026-08-30 with six destinations, so "where this project is
  going" now has a record. Three of the six are `in progress`; T15 above is milestone 3.
