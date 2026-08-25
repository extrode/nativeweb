# 010 — Plan

Spec: ./010-spec.md

## Approach

**Four documents, one of which already exists and is half-written.** The onboarding gap is not a
shortage of prose — there are 2,227 lines of it — but the absence of a *sequence*. So the shape is
a single ordered path with one explicit fork, rather than a pile of new topic documents that would
have the same discoverability problem as the eight already there.

`README.md` becomes the front page it was templated to be and hands off immediately.
`docs/getting-started.md` carries the shared steps — prerequisites, clone, build, run, test — and
then forks once, visibly, into the two audiences' paths. `docs/tutorial-first-feature.md` teaches
the vertical slice. `docs/README.md` is the index that makes the existing eight reachable.

**The tutorial's feature is built for real, in this branch, in `apps/sample`.** The alternative —
writing steps a reader performs against a repo where the result does not exist — was rejected
because it has no defence against drift: prose describing code that is not compiled rots silently
and nothing fails. Building it means the tutorial's endpoint, template, query and three tiers of
test are compiled and run by CI forever, so the day the framework changes underneath the tutorial,
the suite goes red rather than the documentation going quietly wrong. The cost is that a reader
cannot literally run the steps *in this checkout*, since the result is already present; the
tutorial states that in its opening and directs them to follow along in their own stamped product.
That trade is worth taking: a tutorial that is wrong is worse than one you read before you type.

**The feature is a hand-written public page, not another scaffolded entity.** R14 forbids restating
what the reference docs already hold, and `libs/nativeweb.scaffold/README.md` covers the scaffolded
path in 173 lines. The undocumented path is the hand-written one — Minimal API endpoint, render
template, JauntyQ query — which is what `apps/web` is almost entirely made of and what a stamped
product will mostly contain. Building it in `apps/sample` rather than `apps/web` keeps a reader
following the tutorial away from the IdP, per the spec's stated inference.

**Verification is by execution and dated, not automated.** An automated docs-command harness was
considered and rejected for now: it would be a new test tier for four documents, and the tutorial's
own three tiers already give CI-enforced drift protection over the part most likely to rot. The
prose commands get a recorded verification date, OS and toolchain versions instead (R13).

## Tech / dependencies

No new dependencies. Everything below already exists in the repo; versions read from the repo, not
recalled:

| Thing | Version / value | Read from |
|---|---|---|
| .NET SDK | `10.0.100`, `rollForward: latestFeature` | `global.json` |
| Target framework | `net10.0` | `Directory.Build.props:5` |
| Language | `LangVersion latest` (C# 14 per `AGENTS.md`) | `Directory.Build.props:6` |
| Database | PostgreSQL | `AGENTS.md`, `db/README.md` |
| Browser tier | Playwright, chromium, installed via `playwright.ps1 install --with-deps chromium` | `.github/workflows/ci.yml:203` |
| JauntyQ | local checkout, probed at `..\..\extrode-legacy-org.com\jauntyq` and one level deeper | `Directory.Build.props:47-53` |

**One prerequisite is load-bearing and currently undocumented anywhere a newcomer would look:**
`EnsureJauntyQRoot` (`Directory.Build.props:79-81`) fails the build outright unless a JauntyQ
checkout is found or `-p:JauntyQUsePackages=true` is passed, and per the JauntyQ license constraint
in `AGENTS.md` that checkout cannot be redistributed. CI passes `-p:JauntyQUsePackages=true`
(`ci.yml:24`) while `work/todo.md` records that package-mode restore has no reachable feed. Those
two facts cannot both be simply true, and T2 below exists to settle which it is by measurement
before either is written down.

## Changes by area

- **`README.md`** — fill the four placeholders (lines 3, 9, 15, 16): one sentence on what NativeWeb
  is and who it is for, the build command, the test command, and the run command. Add the
  prerequisites, including the JauntyQ one. Link onward to `docs/getting-started.md` and
  `docs/README.md`. The existing `## Layout` and `## Stamping a new product` sections stay as they
  are. Covers **R1, R2, R3**.

- **`docs/getting-started.md`** (new) — shared sections first: prerequisites with verified
  versions, clone, build, **run with no database at all** (both `apps/web` and `apps/sample` select
  an in-memory store when the connection string is absent — `apps/sample/Program.cs:52-56`,
  `db/sample/README.md`, `docs/configuration.md:43`; this is the strongest onboarding fact in the
  repo and nothing currently tells a newcomer about it), then the three test tiers with what each
  covers. Then one explicit fork, under its own heading, into **Path A — your own product** (from
  `tools/new-product.ps1` through database creation, `db/migrate.sh`, running and a green suite in
  the stamped product) and **Path B — working on `apps/web`** (the identity schema, the
  configuration keys that matter, the IdP's own tiers). Covers **R4, R5, R6, R7**.

- **`docs/tutorial-first-feature.md`** (new) — the vertical slice, one layer per section, each
  saying why the code belongs there and linking to the governing reference document rather than
  restating it: the Minimal API endpoint, the render template, the JauntyQ query and its migration,
  then a test at each of the three tiers. Covers **R8, R9, R10, R14**.

- **`apps/sample/`** — the tutorial's feature, built. A read-only public page listing recent
  articles: one endpoint, one template, one `.sql` query under `db/sample/queries/`, and a
  `ProjectReference` to `libs/nativeweb.render` and `libs/nativeweb.render.generator` mirroring how
  `apps/web` wires them (`nativeweb.web.csproj:73-74`). This is the one production-code change in
  the spec, and it is deliberate: it is what makes the tutorial drift-proof. Covers **R8, R9**.

- **`tests/nativeweb.sample.tests.unit/`, `tests/nativeweb.scaffold.tests.integration/`,
  `tests/nativeweb.scaffold.tests.e2e/`** — one test per tier for the tutorial's feature. These are
  the three projects that already cover `apps/sample` (`SampleFactory.cs`, `SampleJauntyFactory.cs`,
  `SampleProcessFixture.cs`), so no new test project is created. Covers **R8**.

- **`docs/README.md`** (new) — the index. Every human-facing Markdown document under `docs/`, each
  with the question it answers, grouped by what the reader is trying to do. Also the place the
  repo's own layout rule asks for, since `docs/` holds subdirectories whose purpose is not obvious
  from their names. Covers **R11**.

- **`work/status/current.md`, `CHANGELOG.md`, `work/tasklist.md`** — the visibility files, in the
  branch's first and last commits per the implement flow.

## Data / interfaces

One new table-less addition: a query file under `db/sample/queries/` that JauntyQ turns into typed
C#, reading the existing `articles` table from `db/sample/migrations/0001_articles.sql`. **No new
migration and no schema change**, so `db/sample/schema/sample.schema.json` does not need re-pulling
and the JauntyQ CLI is not a prerequisite for the tutorial's reader. If the chosen query needs a
column that does not exist, the feature is changed to fit the table rather than the table changed
to fit the feature.

The public page is read-only and unauthenticated: no antiforgery surface, no new configuration key,
no change to `docs/configuration.md`.

## Risks & mitigations

- **The clean-clone build may not be reproducible for anyone but this machine.** JauntyQ is
  local-checkout-only by licence and package mode may have no feed. *Mitigation:* T2 measures both
  modes before anything is written. Whatever the answer, it is documented as found — R15 requires
  the divergence to be surfaced, and a prerequisite a reader cannot satisfy is far better stated
  than discovered at the first `dotnet build`.
- **Documenting the in-memory degrade path could be read as an invitation to run without a
  database.** *Mitigation:* it is presented as the first checkpoint on the way to a real database,
  not as a supported mode, and the sentence says which store is selected and why.
- **Adding `nativeweb.render` to `apps/sample` widens what that app demonstrates.** *Mitigation:*
  accepted deliberately and recorded here; the sample already hosts the JauntyQ generator, and a
  demo that shows only the scaffolded path under-represents the framework. Watched for: no
  regression in the scaffolding tiers, which is what T7 re-runs.
- **A tutorial touching `apps/sample` could break the scaffolding demo.** *Mitigation:* the feature
  is additive and read-only; all existing sample and scaffold tiers are re-run before merge.
- **Writing a step from memory rather than from a run.** This is the failure mode the whole spec
  exists to prevent. *Mitigation:* R12 is enforced per task, not at the end — a command is pasted
  into a document from the terminal that ran it.
- **No security surface.** The tutorial's page is read-only, unauthenticated and adds no
  configuration; `docs/security-baseline.md` is linked, not modified.

## Coverage check

| R# | Covered by |
|---|---|
| R1 | `README.md` — one sentence, no placeholders |
| R2 | `README.md` build/test commands; verified by the clean-clone run (T2) |
| R3 | `README.md` prerequisites, versions read from `global.json` / `Directory.Build.props` |
| R4 | `docs/getting-started.md` shared sections |
| R5 | `docs/getting-started.md` test-tier section |
| R6 | `docs/getting-started.md` explicit fork heading |
| R7 | `docs/getting-started.md` Path A, through to a green suite in the stamped product |
| R8 | `apps/sample` feature + three tiers in the three existing test projects |
| R9 | Feature scoped to one endpoint, one template, one query; no deferred layer |
| R10 | `docs/tutorial-first-feature.md` per-layer rationale and links |
| R11 | `docs/README.md` |
| R12 | Enforced per task; T2 and T7 are the measuring tasks |
| R13 | Verification block in each new document, dated with OS and toolchain versions |
| R14 | Links rather than restatement; the hand-written path was chosen *because* the scaffolded one is already documented |
| R15 | Read-back task (T9) against `AGENTS.md`, `docs/constitution.md`, `docs/conventions.md` |

**No requirement is uncovered.**

## Note on routing

The design above was done inline rather than in a `think` worker. The decisions turned on facts
already established in this session — the in-memory degrade path, which test projects cover
`apps/sample`, the `EnsureJauntyQRoot` gate, the CI package-mode contradiction — and per the
delegation policy a worker that must rebuild that context is the wrong instrument. `/implement`
routes its tasks normally.
