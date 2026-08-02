# Tasklist — NativeWeb

Every task for the body of work currently underway. This file **stays put** while work moves
through it: the `status/` rotation is a three-deep sliding window over this list, not a copy of
it.

The unit is one body of work, not one task and not the whole project. "Migrate the storage layer"
is a tasklist. When that work is done the file is emptied and refilled for the next body of work,
and what shipped is summarised in `CHANGELOG.md`.

## How it feeds the rotation

```
tasklist.md        T1 [x]  T2 [x]  T3 [x]  T4 [ ]  T5 [ ]  T6 [ ]  T7 [ ]
                            |       |       |
                            v       v       v
status/          previous.md  current.md  next.md      <- one task each
```

On each task completion, in this order:
1. `current.md` -> `previous.md`
2. `next.md` -> `current.md`
3. the next unchecked task here -> `next.md`, and tick it off here when it lands

`/status roll` does exactly this. `tasklist.md` is not touched by the rotation except for the
tick.

## Relationship to the neighbours

- `todo.md` is the *unscheduled* backlog: things worth doing that nobody has committed to. A task
  arrives here from there, or from a spec, or from the user directly.
- For Durable-tier work this list is `docs/specs/<NNN>-*/<NNN>-tasks.md`. Point at it rather than
  duplicating it — two copies of a task list is one stale copy.
- `milestones.md` is where the project is going. A tasklist is how one milestone gets done.

## Current work: (none — spec 005 shipped)

Spec 005 (built-in UX defaults) shipped 2026-08-02 — archived to
`archive/2026-08-02-ux-defaults.md`, task breakdown at
`docs/specs/005-ux-defaults/005-tasks.md` (all ticked). Next body of work not yet chosen;
performance tuning is the agreed follow-up candidate.

Spec 004 (progressive enhancement layer + formatted HTML output) shipped 2026-08-02 —
archived to `archive/2026-08-02-progressive-enhancement.md`, task breakdown at
`docs/specs/004-progressive-enhancement/004-tasks.md` (all ticked).

Spec 003 (architecture v2 + manzil.my extraction) shipped 2026-08-01 — archived to
`archive/2026-08-01-architecture-v2.md`, task breakdown at
`docs/specs/003-architecture-v2/003-tasks.md` (all ticked).

Spec 002 (IdP hardening) shipped 2026-07-31 — archived to
`archive/2026-07-31-idp-hardening.md`. The sign-off-gated production cutover replacing
epass.io remains user-gated. Unscheduled items live in `todo.md`.
