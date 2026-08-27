# docs/

Everything written down about NativeWeb, and which of it you need.

The repo has more reference material than onboarding path, which is the problem this index
exists to fix: the architecture spec is 430 lines and the configuration reference 247, and
neither tells you where to start. **Start with the two guides at the top and come back here
when you need a specific answer.**

## Start here

| Read | When |
| --- | --- |
| [`../README.md`](../README.md) | First. What this is, what you need installed, how to run it. |
| [`00-quick-start/getting-started.md`](00-quick-start/getting-started.md) | Clone to a green suite. Splits into two paths at "The fork" — pick yours below. |
| [`00-quick-start/tutorial-first-feature.md`](00-quick-start/tutorial-first-feature.md) | Then. Endpoint, renderer, JauntyQ query and all three test tiers, built for real in `apps/sample`. |

## Which path is yours

The two audiences share the clone and the build and diverge after it. Everything before "The
fork" in `00-quick-start/getting-started.md` applies to both.

**Stamping a new product** from `skeleton/` — your goal is your own application, and `apps/web`
is a worked example you read rather than code you maintain.

- [`00-quick-start/getting-started.md` § Path A](00-quick-start/getting-started.md) — `tools/new-product.ps1`, your own role and
  database, and the two environment variables the stamped product's own suite needs.
- [`../libs/nativeweb.scaffold/README.md`](../libs/nativeweb.scaffold/README.md) — decorate a
  model, get an admin UI. The fastest route from a table to a working screen.
- [`../libs/nativeweb.signin.epass/README.md`](../libs/nativeweb.signin.epass/README.md) — add
  "sign in with epass.io" rather than building authentication.
- [`02-reference/configuration.md`](02-reference/configuration.md) — written against `apps/web`, but the settings your
  stamped product reads are the same ones.

**Maintaining `apps/web`**, the epass.io IdP — your goal is the identity provider itself.

- [`00-quick-start/getting-started.md` § Path B](00-quick-start/getting-started.md) — the real database, `db/migrate.sh`, and
  the reading order for the rest of this list.
- [`02-reference/configuration.md`](02-reference/configuration.md) — every setting `apps/web` reads, its default, and what
  a wrong or absent value costs.
- [`02-reference/security-baseline.md`](02-reference/security-baseline.md) — the ASVS-derived checklist the IdP is held to.
- [`02-reference/deployment.md`](02-reference/deployment.md) — how it gets to a host and back off one. Read
  before touching production; two of its procedures have never been run.
- [`architecture/002-epass-cutover-capability-gap.md`](architecture/002-epass-cutover-capability-gap.md)
  — what `apps/web` does not yet do that the live epass.io does.

## Reference

Answers to specific questions. Not written to be read in order.

| Document | Answers |
| --- | --- |
| [`01-architecture/native-web-architecture.md`](01-architecture/native-web-architecture.md) | Why the stack is what it is: Minimal APIs, NativeAOT, server-rendered HTML, JauntyQ. The 430-line spec everything else assumes. |
| [`02-reference/configuration.md`](02-reference/configuration.md) | Every setting, default, and failure mode. |
| [`01-architecture/progressive-enhancement.md`](01-architecture/progressive-enhancement.md) | HTMX and Alpine conventions. Mandatory for every browser-facing page. |
| [`02-reference/template-diagnostics.md`](02-reference/template-diagnostics.md) | `NWT001`–`NWT016`: what each template build error means and how to fix it. |
| [`02-reference/security-baseline.md`](02-reference/security-baseline.md) | The security checklist and what has been verified against it. |
| [`02-reference/migration-lock-review.md`](02-reference/migration-lock-review.md) | How long `db/migrate.sh` holds its locks. Read before pointing it at a live database. |
| [`02-reference/deployment.md`](02-reference/deployment.md) | The production runbook: provisioning, blue/green release, rollback, backups, restore, disaster recovery. Carries its own list of what has never been executed. |
| [`handovers/admin-cms-handoff.md`](handovers/admin-cms-handoff.md) | The admin panel and CMS module design. |

### Library READMEs

Each library documents itself. These are reference, not tutorial.

- [`../libs/nativeweb.scaffold/README.md`](../libs/nativeweb.scaffold/README.md) — declarative CRUD scaffolding.
- [`../libs/nativeweb.signin.epass/README.md`](../libs/nativeweb.signin.epass/README.md) — the epass.io relying party.
- [`../libs/nativeweb.data/README.md`](../libs/nativeweb.data/README.md) — the JauntyQ data layer.
- [`../libs/nativeweb.assets/README.md`](../libs/nativeweb.assets/README.md) — vendored HTMX and Alpine. Not a .NET project.

## Rules

Binding. Read before changing anything, and before proposing a change to how work is done.

| Document | Holds |
| --- | --- |
| [`constitution.md`](constitution.md) | The WON'T-change decisions. Changing one is a governance act. |
| [`conventions.md`](conventions.md) | Mechanical how-we-work rules; project-specific deltas only. |
| [`../AGENTS.md`](../AGENTS.md) | The stack, the git workflow, the testing requirement. Read by every agent at session start. |

## History and work in flight

Read these when you want to know **why**, not **what**.

- [`decisions/`](decisions/) — dated architecture decision records. One question, one answer, the
  alternatives rejected.
- [`architecture/`](architecture/) — longer design notes that outlive a single decision.
- [`specs/`](specs/) — the Durable-tier trail: `NNN-spec.md` (what), `NNN-plan.md` (how),
  `NNN-tasks.md` (build order), one directory per feature.
- [`plans/`](plans/) — Standard-tier plans, dated. Smaller than a spec, still reviewable.
- [`brainstorms/`](brainstorms/) — direction-setting, deliberately unresolved.
- [`jauntyq/`](jauntyq/) — feature requests prepared for JauntyQ upstream. See its
  [README](jauntyq/README.md) for the format.
- [`lessons/`](lessons/) — [`for-ai.md`](lessons/for-ai.md) is mistake-preventing fact, read
  before starting work; [`for-humans.md`](lessons/for-humans.md) is context and dragons.
- [`../work/`](../work/) — what is being built right now: `tasklist.md`, `status/`, `todo.md`.
  The only part of this list that changes daily.

Two files here are superseded rather than current, kept because the reasoning in them is still
cited: [`brainstorms/002-native-web-architecture.md`](brainstorms/002-native-web-architecture.md) is
the draft that `01-architecture/native-web-architecture.md` replaced, and
[`design-prototypes/`](design-prototypes/) holds the epass v2 visual prototype rather than
prose.

---

**Verified.** Every link on this page was followed and resolves, on **2026-08-25** — Windows 11
(10.0.26200.8457). Two entries carry a caveat rather than a clean bill:

- [`constitution.md`](constitution.md) is **still the unfilled template** — placeholder bullets and
  `Last reviewed: <date>`. It is listed above as binding because that is what it is for, not
  because it currently binds anything. Filling it is a governance act, deliberately not done as
  part of a documentation task.
- [`conventions.md`](conventions.md) § Cleanup policy names
  `scripts/cleanup/cleanup-<milestone>.ps1`. **Nothing in
  [`../scripts/cleanup/`](../scripts/cleanup/) is named that way**, and the directory is not
  internally consistent either: five of its thirty scripts carry the `<NNN>-<slug>.ps1` form the
  global rule in `~/.claude/AGENTS.md` asks for, and the other twenty-five are plain `<slug>.ps1`
  from before it. The fixed `--execute` interface both documents describe *is* followed by all of
  them; only the filename is unsettled.
