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

## Current work: (none — specs 011, 012 and 013 built, each with a requirement unmet)

Spec 013 (continuous read-only audit) shipped 2026-08-29 and is **not** `done`: all fourteen
tasks are ticked and 231 tests pass, but R2 (the schedule runs unattended and survives a reboot)
and R15 (the audit host holds no production credential) are properties of audit-host after the user runs
`scripts/audit/provision-audit-host.sh --execute` on it. Same shape of outstanding as spec 011's T15.
Task breakdown at `docs/specs/013-continuous-audit/013-tasks.md`; the operator guide it produced
is `docs/02-reference/continuous-audit.md`.

Spec 012 (htmx 2.0.10 → 4.0.0 upgrade) shipped 2026-08-29 and is **`done`** — this section read
"not done, R16 has no runnable harness" until 2026-08-30, which outlived the fix by a day. R16 was
closed by building one: `tools/bench/` with a committed `baseline.json`, restated as "throughput
and RSS within tolerance of the baseline". The figure is machine-bound and the harness is
record-only on a machine whose fingerprint does not match. Task breakdown at
`docs/specs/012-htmx-4-upgrade/012-tasks.md`; the two features
evaluated and declined are in `012-plan.md` and
`docs/decisions/2026-08-29-003-morph-swaps-not-adopted.md`.

Spec 011 (production deployment of `apps/web`) shipped 2026-08-27 and is **not** `done`: T1-T14
and T16 are ticked, T15 is executed by a person on a real host. Task breakdown at
`docs/specs/011-production-deployment/011-tasks.md`; the runbook it produced is
`docs/02-reference/deployment.md`.

Spec 010 (documentation and tutorials) shipped 2026-08-25 and is `done` — task breakdown at
`docs/specs/010-documentation-and-tutorials/010-tasks.md`.

Every numbered spec in `docs/specs/` is now `done` except 011 (above) and 008, which stays
`draft` deliberately —
**though not for the reason this file used to give.** All 19 of its tasks are ticked, and R12's
justsmtp.com half landed on 2026-08-19 (`eb06a3a` in that repo, merged into its `dev`), so the
handoff that was blocking it is spent.

The 2026-08-25 review found the real reason: three of the fifteen acceptance criteria describe
something the shipped code deliberately does differently, so amending the criterion or the code is
the user's call. R1's "under 10 lines" against a real 32-line wiring block, R11's "no
library-supplied defaults" against `CookieName`, and R12's "no behavioural change" against a
one-time forced re-login and a login page that no longer echoes the address. The R-by-R table and
the evidence are in `docs/specs/008-epass-signin-library/008-spec.md`, "Acceptance review".

Spec 009 (three consumer seams for the sign-in library) shipped 2026-08-25 and is `done`. Its own
R9 — justsmtp adopting the seams and deleting its three workarounds — is deliberately unmet here,
because it is a change to justsmtp.com.

Spec 008 (the epass sign-in relying-party library, `libs/nativeweb.signin.epass`) shipped
2026-08-19 — not archived, because the spec is not closed; task breakdown at
`docs/specs/008-epass-signin-library/008-tasks.md`.

Spec 007 (session tokens hashed at rest) shipped 2026-08-16 — task breakdown at
`docs/specs/007-session-token-at-rest/007-tasks.md`.

Spec 006 (cacheable public pages) shipped 2026-08-03 — task breakdown at
`docs/specs/006-cacheable-public-pages/006-tasks.md`.

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
