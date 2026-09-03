# docs/

Everything written down about NativeWeb, and which of it you need.

The repo has more reference material than onboarding path, which is the problem this index
exists to fix: the architecture spec runs past 500 lines and tells you nothing about where to
start. **Start with the guides at the top and come back here when you need a specific answer.**

## Start here

| Read | When |
| --- | --- |
| [`../README.md`](../README.md) | First. What this is, what you need installed, how to run it. |
| [`00-quick-start/getting-started.md`](00-quick-start/getting-started.md) | Clone to a green suite. Splits into two paths at "The fork" — pick yours below. |
| [`00-quick-start/tutorial-first-feature.md`](00-quick-start/tutorial-first-feature.md) | Then. Endpoint, renderer, JauntyQ query and all three test tiers, built for real in `apps/sample`. |
| [`00-quick-start/adopting-in-an-existing-app.md`](00-quick-start/adopting-in-an-existing-app.md) | Instead of the above, when you already have an ASP.NET Core application and want the framework underneath it. |

## Which path is yours

There used to be two audiences here, and one of them has gone: this repository held an identity
provider as well as the framework, and maintaining it was a whole reading path of its own. It left
on 2026-08-31 ([decision 007](decisions/2026-08-31-007-idp-leaves-nativeweb.md)), so **there is one
path now** — you are building your own application on the framework.

- [`00-quick-start/getting-started.md`](00-quick-start/getting-started.md) — clone, build, green
  suite, then `tools/new-product.ps1` for your own product, role and database.
- [`../libs/nativeweb.scaffold/README.md`](../libs/nativeweb.scaffold/README.md) — decorate a
  model, get an admin UI. The fastest route from a table to a working screen.
- [`../libs/nativeweb.signin/README.md`](../libs/nativeweb.signin/README.md) — sign users in
  against any conforming OAuth 2.1 / OIDC provider. Configuration keys, the seams, and what your
  product has to supply.
- [`02-reference/template-diagnostics.md`](02-reference/template-diagnostics.md) — `NWT001`-`NWT016`,
  what each template build error means and how to fix it.
- [`02-reference/configuration.md`](02-reference/configuration.md) — every setting, its default,
  and what a wrong or absent value costs.

`apps/sample` is the worked example you read: a real application, small enough to hold in your
head, exercising the scaffolding, forms and render libraries through a real entry point.

## Reference

Answers to specific questions. Not written to be read in order.

| Document | Answers |
| --- | --- |
| [`01-architecture/native-web-architecture.md`](01-architecture/native-web-architecture.md) | Why the stack is what it is: Minimal APIs, NativeAOT, server-rendered HTML, JauntyQ. The 430-line spec everything else assumes. |
| [`02-reference/configuration.md`](02-reference/configuration.md) | Every setting, default, and failure mode. |
| [`01-architecture/progressive-enhancement.md`](01-architecture/progressive-enhancement.md) | HTMX and Alpine conventions. Mandatory for every browser-facing page. |
| [`02-reference/template-diagnostics.md`](02-reference/template-diagnostics.md) | `NWT001`–`NWT016`: what each template build error means and how to fix it. |
| [`02-reference/security-baseline.md`](02-reference/security-baseline.md) | The security checklist and what has been verified against it. Written against the identity provider that has since left; read it as the posture a stamped product inherits. |
| [`02-reference/migration-lock-review.md`](02-reference/migration-lock-review.md) | How long `db/migrate.sh` holds its locks. Read before pointing it at a live database. |
| [`02-reference/performance.md`](02-reference/performance.md) | Every load measurement taken here, what changed between them, and the bottlenecks found — including the one that cost 35% of page throughput to a header nobody was setting. |
| [`02-reference/deployment.md`](02-reference/deployment.md) | The production runbook: provisioning, blue/green release, rollback, backups, restore, disaster recovery. Carries its own list of what has never been executed. |
| [`handovers/admin-cms-handoff.md`](handovers/admin-cms-handoff.md) | The admin panel and CMS module design. |
| [`handovers/epass-io-adoption-handoff.md`](handovers/epass-io-adoption-handoff.md) | For a downstream project building on this one: the licensing and JauntyQ constraints that reach it, the landmines, and what is built here but unproven. **Read first** — the other two handoffs assume it. |
| [`handovers/justsmtp-com-adoption-handoff.md`](handovers/justsmtp-com-adoption-handoff.md) | Why that tree cannot build, which library left under it, and what to reference instead. |
| [`handovers/ghosthive-com-adoption-handoff.md`](handovers/ghosthive-com-adoption-handoff.md) | Why its migration plan is stale, which of its two backends NativeWeb addresses, and what it is blocked on. |

### Library READMEs

Each library documents itself. These are reference, not tutorial.

- [`../libs/nativeweb.web/README.md`](../libs/nativeweb.web/README.md) — the pipeline: page shell, security headers, clean URLs, flash messages, asset URLs. **Read this one first** if you are wiring the framework into an application.
- [`../libs/nativeweb.render/README.md`](../libs/nativeweb.render/README.md) — source-generated templates and locale negotiation.
- [`../libs/nativeweb.forms/README.md`](../libs/nativeweb.forms/README.md) — `[Form]` binding, `MapFormPost`, and the two guards attached by construction.
- [`../libs/nativeweb.scaffold/README.md`](../libs/nativeweb.scaffold/README.md) — declarative CRUD scaffolding.
- [`../libs/nativeweb.permissions/README.md`](../libs/nativeweb.permissions/README.md) — per-model, per-operation permissions with groups, and the access admin that grants them.
- [`../libs/nativeweb.security/README.md`](../libs/nativeweb.security/README.md) — passphrases, opaque tokens, at-rest encryption, blind indexes, masking, bot traps.
- [`../libs/nativeweb.signin/README.md`](../libs/nativeweb.signin/README.md) — the OAuth relying party.
- [`../libs/nativeweb.signin.testing/README.md`](../libs/nativeweb.signin.testing/README.md) — the store conformance suites a consumer derives against its own implementation.
- [`../libs/nativeweb.mail/README.md`](../libs/nativeweb.mail/README.md) — the outbound e-mail seam.
- [`../libs/nativeweb.assets/README.md`](../libs/nativeweb.assets/README.md) — vendored HTMX and Alpine. Not a .NET project.

The three `.generator` projects have no README of their own: a consumer references one as an
analyzer and never calls anything it defines, so each is documented in the runtime library it
serves.

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
- [`architecture/`](architecture/) — longer design notes that outlive a single decision. Two are
  current rather than historical: [`003-framework-readiness.md`](architecture/003-framework-readiness.md)
  asks what the framework is missing from inside this repository, and
  [`004-consumer-readiness.md`](architecture/004-consumer-readiness.md) asks the same question by
  reading the three products that build on it.
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

**Enforced.** Every link and every repository path on this page is checked on each run of
`tests/nativeweb.repo.tests.unit` (`DocsClaimsTests`), so a rename that strands one of them turns
the suite red rather than waiting to be noticed. Two entries carry a caveat rather than a clean
bill:

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
