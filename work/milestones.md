# Milestones — NativeWeb

Where this project is going, coarse. A milestone is a destination worth naming, not a task: it
survives many sprints, it is the thing you would tell someone who asked "what is this for", and
it changes rarely. If an entry here moves week to week, it belongs in `todo.md` or `status/`.

This is the one file in `work/` that is allowed to go a month without an edit. Editing it
means the plan changed, which is worth noticing.

**Distinction from the neighbours.** `milestones.md` is where we are going. `todo.md` is
everything not yet scheduled. `status/current.md` is what is being worked on right now. A single
item legitimately appears in all three at different times in its life, and should be in exactly
one of them at any given moment.

**The destination, in one sentence.** Another application — ours first, someone else's after —
starts from this repo and reaches production without reimplementing the parts that are hard to get
right. Everything below is a step in that, ordered by what blocks what.

| # | Milestone | Target | Status | Notes |
|---|-----------|--------|--------|-------|
| 1 | **The template is built, not just written** | — | in progress | `skeleton/` is in no solution, so nothing compiles it; ten drifts from `apps/web` accumulated undetected and were found by an audit rather than by CI. `SkeletonParityTests` (2026-08-30) pins the text; the destination is a stamped product built and tested on every CI run. |
| 2 | **A stamped product reaches production unedited** | — | planned | `tools/new-product.ps1` output builds, tests and deploys with no hand-editing of the template. Gated on 1 and 3. |
| 3 | **Deployable by an operator who did not write it** | — | in progress | Spec 011 shipped the toolchain and the runbook; T15 is unexecuted, so R1, R16 and R17 are unmet and every elapsed time in `docs/02-reference/deployment.md` reads `NOT YET MEASURED`. A person's task, on a real host. |
| 4 | **First real dependent: the epass.io browser surface** | — | in progress | Decided 2026-08-29 in the epass.io repo (`docs/decisions/2026-08-29-003-native-web-browser-surface.md`): browser surface only, DB-free, over HTTP. The port is built there; this repo's job is to be a stable upstream. |
| 5 | **The sign-in library has a second consumer** | — | planned | `libs/nativeweb.signin.epass` has exactly one (justsmtp.com), which is why spec 008's R1, R11 and R12 are arguable rather than measurable. A second consumer settles them by evidence. |
| 6 | **Consumable without a source checkout** | — | planned | Today every consumer needs this repo *and* a JauntyQ checkout beside it, and JauntyQ may not be redistributed (`AGENTS.md`). Until that is resolved, "other apps can use it" means other **Extrode** apps. Whether NativeWeb versions and publishes itself is still an open decision in `todo.md`. |

Status is one of `planned`, `in progress`, `shipped`, `dropped`. A dropped milestone stays in the
table with a note saying why: the reason it was dropped is the most useful thing on the row, and
deleting it means re-litigating the decision in six months.
