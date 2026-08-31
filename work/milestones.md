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
| 1 | **The template is built, not just written** | — | in progress | Corrected 2026-08-31: this read "`skeleton/` is in no solution, so nothing compiles it", and the second half was wrong. CI's `skeleton-stamp` job (`.github/workflows/ci.yml:309`) stamps a product with `tools/new-product.ps1 -Execute` and builds and tests all three tiers of it in package mode. What is missing is narrower and still real — **no local build compiles it**, so a drift is a CI-only finding, and `SkeletonParityTests` no longer exists: it was written against `apps/web` and left with the identity provider. The destination is unchanged. |
| 2 | **A stamped product reaches production unedited** | — | planned | `tools/new-product.ps1` output builds, tests and deploys with no hand-editing of the template. Gated on 1 and 3. |
| 3 | **Deployable by an operator who did not write it** | — | in progress | Spec 011 shipped the toolchain and the runbook; T15 is unexecuted, so R1, R16 and R17 are unmet and every elapsed time in `docs/02-reference/deployment.md` reads `NOT YET MEASURED`. A person's task, on a real host. |
| 4 | **First real dependent: the epass.io browser surface** | — | shipped | Reached 2026-08-31 ([decision 008](../docs/decisions/2026-08-31-008-epass-cutover-closed.md)). epass.io built and merged the surface against this repo and consumes exactly two projects from it, `libs/nativeweb.render` and `libs/nativeweb.security`; the identity provider's departure deleted nothing either uses. "Be a stable upstream" is a condition that holds, not a task with an end — so this milestone closes rather than staying open forever. |
| 5 | **The platform stays general** | — | shipped | Decided 2026-08-30, done 2026-08-31. It read "`nativeweb.signin.epass*` is 6 of the 26 projects in `NativeWeb.slnx` and 6,388 lines"; the identity provider left that day ([decision 007](../docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md)) and `NativeWeb.slnx` is **18 projects**, none provider-specific. epass.io owns epass. The line to hold is Django's: `contrib.auth` ships, the OAuth2/OIDC server does not. |
| 6 | **Consumable without a source checkout** | — | planned | Today every consumer needs this repo *and* a JauntyQ checkout beside it, and JauntyQ may not be redistributed (`AGENTS.md`). Until that is resolved, "other apps can use it" means other **Extrode** apps. Whether NativeWeb versions and publishes itself is still an open decision in `todo.md`. |

Status is one of `planned`, `in progress`, `shipped`, `dropped`. A dropped milestone stays in the
table with a note saying why: the reason it was dropped is the most useful thing on the row, and
deleting it means re-litigating the decision in six months.
