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
| 1 | **The template is built, not just written** | — | in progress | Corrected 2026-08-31: this read "`skeleton/` is in no solution, so nothing compiles it", and the second half was wrong. CI's `skeleton-stamp` job (`.github/workflows/ci.yml:309`) stamps a product with `tools/new-product.ps1 -Execute` and builds and tests all three tiers of it in mixed mode (NativeWeb by checkout, JauntyQ by packages; the job's `env:` block says why). A local build exists as of 2026-09-05: `tools/stamp-check.ts` stamps, builds and tests a product here in ~8 s, and its first run found the defect that stopped every stamped product from starting. What is still missing is narrower again — the integration tier needs the stamped product's database, so it is opt-in rather than part of the check. **Corrected again 2026-09-05:** this also read "`SkeletonParityTests` still does not exist", which was asking for a suite that has no job. It compared twenty C# files copied into `skeleton/` against their twin in `apps/web`; spec 015 moved those files into `libs/nativeweb.web` so there is one copy, and `apps/web` left with the identity provider so there is no twin. Eleven test classes in `tests/nativeweb.web.tests.unit` cover the behaviour instead, and spec 029 closed the last copied files — four `templates/shared/` partials — by framework delivery plus `FrameworkTemplateTests`. The destination is unchanged. |
| 2 | **A stamped product reaches production unedited** | — | planned | `tools/new-product.ps1` output builds, tests and deploys with no hand-editing of the template. Gated on 1 and 3. |
| 3 | **Deployable by an operator who did not write it** | — | shipped | Reached 2026-09-05. T15 ran end to end on `incus-host`: R1 on 2026-09-01, R16 and R17a on 2026-09-05, and every `NOT YET MEASURED` in `docs/02-reference/deployment.md` is now a number (backup 1 s, restore 1 s, verified serve 1 s, bare guest to serving 345 s). Three things the milestone does **not** claim, all unmet on the host: R14 and R17b, because the bucket is a MinIO container on the machine being backed up, so losing it loses every backup; and R15, whose dead-man's-switch is unset. All three are in the runbook's gap table. The milestone is about an operator being able to deploy, which holds; surviving the loss of the box is R17b and does not. |
| 4 | **First real dependent: the epass.io browser surface** | — | shipped | Reached 2026-08-31 ([decision 008](../docs/decisions/2026-08-31-008-epass-cutover-closed.md)). epass.io built and merged the surface against this repo and consumes exactly two projects from it, `libs/nativeweb.render` and `libs/nativeweb.security`; the identity provider's departure deleted nothing either uses. "Be a stable upstream" is a condition that holds, not a task with an end — so this milestone closes rather than staying open forever. |
| 5 | **The platform stays general** | — | shipped | Decided 2026-08-30, done 2026-08-31. It read "`nativeweb.signin.epass*` is 6 of the 26 projects in `NativeWeb.slnx` and 6,388 lines"; the identity provider left that day ([decision 007](../docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md)) and `NativeWeb.slnx` is **18 projects**, none provider-specific. epass.io owns epass. The line to hold is Django's: `contrib.auth` ships, the OAuth2/OIDC server does not. |
| 6 | **Consumable without a source checkout** | — | in progress | **Spec 028 built the release path on 2026-09-04 and has not run it.** One `VersionPrefix`, `tools/pack.ts` (13 of 13 packed and verified in 24.9 s), a tag-only `publish.yml`, and `docs/02-reference/versioning.md`. What is missing is a `v*` tag: nothing is on the feed, so the `NativeWeb.*` pins in `skeleton/Directory.Packages.props` still resolve to nothing and every consumer still builds against `dev`. The feed stays private — JauntyQ may not be redistributed (`AGENTS.md`) — so "other apps can use it" still means other **Extrode** apps, and that is the intended scope rather than a gap. Shipped when a stamped product restores `NativeWeb.*` from the feed with no NativeWeb checkout present. |

Status is one of `planned`, `in progress`, `shipped`, `dropped`. A dropped milestone stays in the
table with a note saying why: the reason it was dropped is the most useful thing on the row, and
deleting it means re-litigating the decision in six months.
