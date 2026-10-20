# 010 — Tasks

Plan: ./010-plan.md

Ordered by dependency. T1 and T2 come first because every other task writes down what they
measure, and a document written before them would be written from memory — the one thing R12
forbids.

- [x] **T1 Measure the environment and the required versions** — files: none written; findings
  recorded in this file's "Measured" section — covers: R3, R13 — done when: the OS version, the
  installed .NET SDK, PostgreSQL and Playwright versions are captured from the tools themselves,
  and the *required* versions are read from `global.json`, `Directory.Build.props` and
  `.github/workflows/ci.yml` rather than recalled. Any disagreement between required and installed
  is written down, not reconciled silently.

- [x] **T2 Settle the JauntyQ prerequisite by measurement** — files: none written; findings
  recorded below — covers: R2, R12 — done when: a clone of this repository into `tmp/` (a
  directory that has never held it) has been built in checkout mode and in
  `-p:JauntyQUsePackages=true` mode, and it is known which of the two a newcomer can actually use.
  `work/todo.md` says package mode has no reachable feed while `ci.yml:24` builds with it; this
  task resolves that contradiction with a command, and the losing mode is documented as
  unavailable rather than omitted.

- [x] **T3 Fill the README's four placeholders** — files: `README.md` — covers: R1, R2, R3 —
  done when: lines 3, 9, 15 and 16 carry real content, no `<...>` placeholder remains, the
  prerequisites section states the versions T1 measured and the JauntyQ fact T2 settled, and the
  build/test/run commands are the ones actually run in T2. Links onward to
  `docs/getting-started.md` and `docs/README.md`.

- [x] **T4 Write the shared half of getting-started** — files: `docs/getting-started.md` —
  covers: R4, R5, R6 — done when: prerequisites, clone, build, run-with-no-database, and the three
  test tiers are written, each command having been executed in the form written; the in-memory
  degrade path is presented as the first checkpoint rather than a supported mode; and the fork into
  the two audiences appears under its own heading with the divergence stated in the text.

- [x] **T5 Write Path A and prove it by stamping a product** — files: `docs/getting-started.md` —
  covers: R7, R12 — done when: a product has actually been stamped into `tmp/` with
  `tools/new-product.ps1`, its database created, its migrations applied, the app started and its
  tests run green — and the steps written down are the ones that did that. A step performed but not
  written is a failure of this task.

- [x] **T6 Build the tutorial's feature in the sample app** — files:
  `apps/sample/nativeweb.sample.csproj`, one new endpoint and template under `apps/sample/`, one
  new `.sql` under `db/sample/queries/` — covers: R8, R9 — done when: a read-only public page
  listing recent articles serves real rows through a JauntyQ-generated query and the render layer,
  the render project references mirror `nativeweb.web.csproj:73-74`, and no migration or schema
  re-pull was needed. If the query needs a column `0001_articles.sql` does not have, the feature
  changes to fit the table.

- [x] **T7 Cover the feature at all three tiers, and re-run everything it could have broken** —
  files: `tests/nativeweb.sample.tests.unit/`, `tests/nativeweb.scaffold.tests.integration/`,
  `tests/nativeweb.scaffold.tests.e2e/` — covers: R8, R12 — done when: one test per tier asserts
  the feature, **each new test has been perturbed and proved red before being kept**, and the
  existing sample and scaffold tiers still pass. Counts before and after are recorded.

- [x] **T8 Write the tutorial** — files: `docs/tutorial-first-feature.md` — covers: R8, R9, R10,
  R14 — done when: one section per layer — endpoint, template, query, and a test at each tier —
  each saying why the code belongs there and linking to the governing reference document instead of
  restating it; the opening states that the finished result is already in the repo and tells the
  reader to follow along in their own product; and no layer is deferred.

- [x] **T9 Write the docs index** — files: `docs/README.md` — covers: R11 — done when: every
  human-facing Markdown document under `docs/` is reachable from it, each entry says what question
  that document answers, and the entries are grouped by what the reader is trying to do rather than
  by filename.

- [x] **T10 Verification blocks and the read-back** — files: all four documents — covers: R13,
  R14, R15 — done when: each new document carries a dated verification block naming the OS and
  toolchain it was verified on; the new prose has been read against `AGENTS.md`,
  `docs/constitution.md` and `docs/conventions.md` with any divergence recorded rather than
  smoothed over; and no passage duplicates one of the eight existing reference documents.

## Analyze — requirement coverage

| R# | Tasks |
|---|---|
| R1 | T3 |
| R2 | T2, T3 |
| R3 | T1, T3 |
| R4 | T4 |
| R5 | T4 |
| R6 | T4 |
| R7 | T5 |
| R8 | T6, T7, T8 |
| R9 | T6, T8 |
| R10 | T8 |
| R11 | T9 |
| R12 | T2, T5, T7, T10 |
| R13 | T1, T10 |
| R14 | T8, T10 |
| R15 | T10 |

**No orphans:** every task traces to a "Changes by area" entry in the plan. **No gaps:** all
fifteen requirements have at least one task. Note that the tracker for this work is this file
alone — the session's Task tools are not available, so `[ ]` → `[x]` here is the only record and
is updated per task rather than in a batch.

## Measured

### T1 — environment, 2026-08-25

| Thing | Installed | Required | Read from |
|---|---|---|---|
| OS | Windows 11 Pro 10.0.26200.8457 | — | `cmd /c ver` |
| .NET SDK | 10.0.203 (also 9.0.311, 9.0.313) | `10.0.100`, `rollForward: latestFeature` | `dotnet --list-sdks` / `global.json` |
| Target framework | — | `net10.0`, `LangVersion latest` | `Directory.Build.props:5-6` |
| PostgreSQL (psql) | 18.2 | unpinned | `psql --version` |
| PowerShell | 7.6.4 | — | `$PSVersionTable` |
| Microsoft.Playwright | 1.49.0 | pinned | `Directory.Packages.props:67` |
| xunit | 2.9.3 | pinned | `Directory.Packages.props:59` |

Installed and required agree; `10.0.203` satisfies `10.0.100` under `latestFeature`.

### T2 — the JauntyQ prerequisite, 2026-08-25

Measured against a fresh `git clone` into `tmp/clean-clone`, a directory that had never held this
repository. Four builds:

| # | Command | Result |
|---|---|---|
| 1 | `dotnet build` (solution, checkout mode) | **FAILED**, 2 × `error MSB3202` — `../jauntyq/src/JauntyQ.{Generator,Runtime}.csproj` not found |
| 2 | `dotnet build -p:JauntyQUsePackages=true` (solution) | **FAILED**, the same two `MSB3202` |
| 3 | `dotnet build apps/web` (per-project, no flag) | **succeeded** |
| 4 | `dotnet build` (solution) with `jauntyq` cloned as a sibling | **succeeded** |
| 5 | `dotnet test tests/nativeweb.web.tests.unit --no-build` in that clone | **496 passed, 0 failed, 0 skipped**, 1 s |

**The contradiction in `work/todo.md` is resolved, and both halves of it were wrong.**

- **Package mode does not rescue the solution build.** `NativeWeb.slnx` lists the two JauntyQ
  projects unconditionally, so `-p:JauntyQUsePackages=true` still hits `MSB3202`. The flag governs
  `Directory.Build.props`; it has no effect on solution membership. CI already knows this and works
  around it by `dotnet sln NativeWeb.slnx remove` before the build, restoring the file afterwards
  (`ci.yml:135-139`, `ci.yml:184`).
- **"No reachable feed" is out of date.** `nuget.config` declares `jauntyq-github`
  (`https://nuget.pkg.github.com/extrode-legacy-org/index.json`) with package source mapping confined to
  `JauntyQ.*`, and documents the local `gh auth token` recipe. The feed exists; it needs
  credentials, which is a different problem and a solvable one.
- **This is the same defect as the "worktree cannot build `NativeWeb.slnx`" item**, now known to be
  wider than worktrees: it affects *any* checkout that is not a literal sibling of `jauntyq`.
  Per-project builds are unaffected because `Directory.Build.props:47-53` probes two depths.

**Consequence for the documentation:** the front page cannot say the build command is
`dotnet build`. The verified newcomer path is a sibling `jauntyq` checkout, and the licence in
`AGENTS.md` restricts who can have one — so that restriction is stated rather than glossed.

### T3 — what writing the front page turned up, 2026-08-25

Three facts that only running the commands could have produced. All from the clean clone.

| Command | Result |
|---|---|
| `dotnet run --project apps/sample` | starts with **no database**; `/signin`, `/admin/articles` answer (302) on `http://localhost:5399` |
| `dotnet run --project apps/web` (default HTTP profile) | `/healthz` **200**, `/` **500** |
| `dotnet run --project apps/web --urls https://localhost:5444` | `/` **200**, `/healthz` **200** |
| `dotnet build apps/web -p:JauntyQRoot=C:\nope` | fails with the friendly `Directory.Build.props:81` message, not `CS0246` |
| `dotnet build apps/web -p:JauntyQUsePackages=true` | **succeeded** — but from the local NuGet cache |

- **`apps/web` is HTTPS-only in practice and its only launch profile is HTTP.** The 500 is
  `InvalidOperationException: AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current
  request is not an SSL request`. `/healthz` answering 200 makes this easy to misdiagnose as a
  working app with one broken page. Noted in `work/todo.md` as a defect; the README documents the
  `--urls` workaround.
- **Package mode's apparent success is a trap.** `~/.nuget/packages` already holds
  `jauntyq.generator` and `jauntyq.runtime` at 0.1.0, 0.2.0 and 0.3.0, so the cache stood in for the
  feed — exactly the failure mode `nuget.config`'s own comment warns about. The README says so
  rather than claiming package mode works unauthenticated.
- **A first draft of the README claimed `dotnet build apps/web` "needs neither sibling nor flag".**
  That was wrong and was caught by measuring it: it needs a JauntyQ checkout like everything else,
  just one findable by the two-depth probe rather than at the exact sibling path. Corrected before
  the task was ticked.

### T4 — the full suite, 2026-08-25

Every tier run on this branch before any of the tutorial's code existed, so this is the baseline
T7 is compared against.

| Project | Facts |
|---|---|
| `web.tests.unit` | 496 |
| `web.tests.integration` | 298 |
| `web.tests.e2e` | 64 |
| `render.tests.unit` | 157 |
| `scaffold.tests.unit` | 64 |
| `scaffold.tests.integration` | 24 |
| `scaffold.tests.e2e` | 9 |
| `sample.tests.unit` | 8 |
| `signin.epass.tests.unit` | 184 |
| `signin.epass.tests.integration` | 34 |
| `signin.epass.tests.e2e` | 3 |
| **Total** | **1,341 — 0 failed, 0 skipped** |

Three counts have drifted upward from `work/status/current.md`'s 2026-08-25 figures without this
branch touching them: `scaffold.tests.integration` 17 → 24, `signin.epass.tests.unit` 177 → 184,
`signin.epass.tests.integration` 32 → 34. Recorded, not chased — nothing here changed them.

**One failure, and it was mine.** `SdkPinTests.NoWorkflowInstallsAnSdk` went red at 495/496 because
it scans for workflow files across the whole repository and found `tmp/jauntyq/.github/workflows/ci.yml`
— the scratch clone T2 created. **The test scans gitignored scratch**, which means any checkout
placed under `tmp/` turns the suite red with a message about `setup-dotnet` that has nothing to do
with the real cause. Noted in `work/todo.md`; removing the scratch restored 496/496.

### T5 — stamping a product, 2026-08-25

Stamped `Probe`/`probe` into `tmp/probe` with `tools/new-product.ps1 -Execute` (168 files, git init,
`main` + `dev`), created `probe_app` and `probe_dev`, applied `db/migrate.sh`, built and ran all
three tiers. **It did not come out green, and both failures were real.**

| Attempt | unit | integration | e2e |
|---|---|---|---|
| As documented by the stamped README | 47 | **1 failed** of 7 | **2 failed** of 2 |
| With the two environment variables below | 47 | 7 | 2 |

- **`Readyz_ReportsReady` expected `OK`, got `ServiceUnavailable`.** The integration tier reads
  `Probe__Db__ConnectionString` from the environment (`ProbeFactory.cs:52`, `TestDatabase.cs:19`)
  and nothing in the stamped README says so.
- **The e2e tier failed at `dotnet publish … exited with 1`,** reporting *"Skipping project
  …\NativeWeb\libs\nativeweb.security\… because it was not found"*. Cause: the fixture shells out to
  its own `dotnet publish`, and the `-p:NativeWebRoot=` / `-p:JauntyQRoot=` passed on the outer
  command line **do not cross into a child process**. Exporting them as environment variables works,
  because MSBuild reads bare environment variables as properties.

Both are now written into Path A. Neither is a defect this spec fixes — the stamped README could
say both, and that is a note for `work/todo.md` rather than a change inside 010's scope.

One caveat on the method, recorded so a later reader can reproduce it. `tmp/jauntyq` sits *inside*
this repository's tree, so MSBuild's Central Package Management discovery walked up into
NativeWeb's own `Directory.Packages.props` and failed jauntyq's projects with `NU1008`. That is an
artefact of putting the scratch sibling under `tmp/`, not a real defect: in the true layout the two
are siblings under `extrode-legacy-org.com/`, which holds no props file. A stopper
`tmp/Directory.Packages.props` setting `ManagePackageVersionsCentrally=false` reproduces the real
arrangement, and build 4 above is with that stopper in place.

### T6/T7 — blocked on the sibling JauntyQ checkout, 2026-08-25

T6's query sorts `ORDER BY created_at DESC, id`, and `articles` has one index, the `id` primary
key. The generator reports:

```
CSC : error JNT8007: ORDER BY articles.created_at has no supporting index:
      this query sorts at runtime. Add an index whose leading column is
      created_at, or accept the sort.
```

Three facts established before raising it:

| Question | Answer | Evidence |
| --- | --- | --- |
| Is JNT8007 an error in JauntyQ? | No — `DiagnosticSeverity.Warning`. It is fatal here only because `TreatWarningsAsErrors` is repo-wide. | `JauntyDiagnostics.cs:654-660` |
| Why does `List.sql` pass? | It sorts on `id`, and the check short-circuits on the primary key. | `QueryValidator.Part2.cs:162-195` |
| Does `-- @allow-unindexed` suppress it? | No. It covers filter columns (JNT8004) only. Adding it produced JNT8007 **still**, plus `JNT8012: "declared but no filter column on this query is unindexed, so it suppresses nothing"`. | run 2026-08-25; directive reverted |

Raised with the user, who took it upstream. **The sibling checkout is now mid-edit on exactly
that** — `git -C ../jauntyq status` shows `Directives/DirectiveModel.cs`,
`Directives/DirectiveParser.cs` and `JauntyQGenerator.Part2.cs` dirty, with a `suppressedSort`
local in Part2 — and it does not currently compile (`CS0103: suppressedAnything`,
`CS0219: suppressedFilter`, `CS0219: suppressedSort`). So **nothing in `apps/sample` can be built
or tested until that lands**, including tiers unrelated to this feature.

Confirmed as the only blocker, not as cover for a second one: with the query's diagnostic
suppressed the sample built clean — `Build succeeded. 0 Warning(s) 0 Error(s)`. That was done with
`-p:NoWarn=JNT8007` first, which is **wrong** and is recorded so it is not repeated: a
command-line `NoWarn` replaces every project's own `NoWarn`, including the generator project's,
which then fails with 26 × `RS2008`. Scoping it to `apps/sample/nativeweb.sample.csproj` is the
form that works. Reverted; nothing in the working tree carries it.

T7's unit tier is written (`tests/nativeweb.sample.tests.unit/PublicArticlesTests.cs`, 8 facts:
four on `MemoryPublishedArticles` ordering/filtering/truncation, four on `ArticlesPage` rendering
and encoding). **It has never been executed, so it is not ticked and the perturbation proof is
not done.** Both remain owed. The integration and e2e tiers are not yet written.

### T6/T7 — unblocked and done, 2026-08-25

JauntyQ shipped `-- @allow-sort <reason>` (`bf37907`), mirroring `@allow-unindexed`: mandatory
reason, bare form rejected with JNT3008, and the same self-expiring JNT8012 gate. The sibling
checkout is clean and the generator builds.

**The query carries the directive rather than a new index.** On a growing table the index is the
right answer and `db/migrations/0023, 0033, 0034, 0048` are that; `articles` is three seeded rows
in a demo, where PostgreSQL would ignore an index at that size. That also keeps the plan's promise
that T6 needs no migration and no schema re-pull.

Self-expiry verified rather than assumed: putting `-- @allow-sort` on `List.sql`, which sorts on
the primary key, produced

```
CSC : error JNT8012: -- @allow-sort is declared but no ORDER BY column on this query is
      unindexed, so it suppresses nothing. The index it was waiting for probably exists now:
      remove the directive. (Stated reason: probe: ...)
```

Reverted afterwards.

#### Counts

| Tier | Before | After |
|---|---|---|
| `nativeweb.sample.tests.unit` | 8 | **16** |
| `nativeweb.scaffold.tests.integration` | 24 | **31** |
| `nativeweb.scaffold.tests.e2e` | 9 | **13** |
| `nativeweb.scaffold.tests.unit` | 64 | 64 (untouched) |

0 failed, 0 skipped throughout.

#### Perturbation record

Every one of the 19 new facts was green on its first run, so every one was perturbed. Each
perturbation below failed **exactly** its intended test unless noted.

| Perturbation | Tier | Test proved |
|---|---|---|
| `.Where(a => a.Published)` → `.Where(a => true)` | unit, integration | omits-unpublished (both) |
| `OrderByDescending` → `OrderBy` | unit, integration | orders-newest-first (both) |
| `ThenBy` → `ThenByDescending` | unit | tiebreak-by-id |
| `.Take(take)` moved above the sort | unit | takes-the-newest |
| empty state → empty `<ul>` | unit | says-so-rather-than-empty-list |
| `encoder.Encode(article.Title)` → raw | unit, integration, e2e | encoding, at all three |
| author span always rendered | unit | omits-the-author-element |
| `yyyy-MM-dd` → `dd-MM-yyyy` | unit | shows-title-author-and-date |
| `WHERE published` → `WHERE TRUE` | integration, e2e | hides-drafts over PostgreSQL |
| `ORDER BY … DESC` → `ASC` | integration, e2e | orders-newest-first over PostgreSQL |
| `row.Author` → `row.Author ?? "null"` | integration | renders-a-row-with-no-author |
| `.RequireAuthorization()` on the endpoint | integration, e2e | reachable-without-signing-in |

One method note. Perturbing `WHERE` and `ORDER BY` **together** failed four integration tests
rather than two: `ORDER BY ASC` pushed the future-dated rows off the twenty-row page, so two tests
failed for a reason unrelated to what they assert. That is a false positive for the perturbation
method, not for the tests — a perturbation that fails a test through an unintended route proves
nothing about it. Re-run one at a time, each failed alone.

And one claim checked rather than trusted: the `.RequireAuthorization()` run failed in 96 ms, fast
enough to suspect the host had not started. It had — the message was
`Assert.EndsWith() Failure … String: "http://localhost:5348/admin"`, the browser having followed
`/articles` → `/signin` → `/admin`. That is precisely the observation the integration tier cannot
make, since it runs with `AllowAutoRedirect = false`, and it is why the e2e tier carries an
anonymous-visit fact at all.

### T8/T10 — the tutorial and the read-back, 2026-08-25

`docs/tutorial-first-feature.md` covers nine sections and defers no layer: the read model and its
seam, the query, the renderer, the endpoint, the wiring, one section per test tier, and a ninth on
perturbation. Each links to the governing reference rather than restating it —
`native-web-architecture.md` for the renderer's shape, `security-baseline.md` for the encoding,
`progressive-enhancement.md` for the enhancement layer this page does not use,
`libs/nativeweb.scaffold/README.md` for what scaffolding can express, `db/sample/README.md` and
`libs/nativeweb.data/README.md` for the generator, `configuration.md` for the connection-string key.
The opening names the finished files, tells the reader to run `/articles` first, and tells Path A
readers to build the equivalent in their own product rather than editing this repo.

Two claims in it were checked rather than written from memory: all four migrations cited as
JNT8007 precedent (`0023`, `0033`, `0034`, `0048`) do name that diagnostic in their own comments,
and every relative link on the page resolves.

**Counts re-measured on 2026-08-25**, after the tutorial feature landed: sample-unit 16,
scaffold-integration 31, scaffold-e2e 13, all green. `docs/getting-started.md`'s table and total
(1,341 → 1,360) were corrected to match; the other eight projects' numbers are from the earlier
full-suite verification run and this branch does not touch them.

#### Verification blocks

| Document | Block |
|---|---|
| `README.md` | already carried one under "Build and test" — date, OS, SDK, PostgreSQL, PowerShell |
| `docs/getting-started.md` | added; the file's opening line promised one and there was none |
| `docs/tutorial-first-feature.md` | added, with the three tier counts |
| `docs/README.md` | added, recording the two divergences below |

#### Read-back against AGENTS.md, constitution.md, conventions.md

Three divergences found. All three are recorded where a reader meets them, not smoothed over:

1. **`docs/constitution.md` is an unfilled template.** Placeholder bullets, `Last reviewed:
   <date>`. `docs/README.md` lists it as binding, which is what it is *for*; the index now says
   plainly that it does not currently bind anything. Filling it is a governance act and was
   deliberately not done inside a documentation task.
2. **Cleanup script naming.** `conventions.md` § Cleanup policy says
   `scripts/cleanup/cleanup-<milestone>.ps1`; the global `~/.claude/AGENTS.md` says
   `scripts/cleanup/<work>.ps1`, and the generated scripts follow the global form
   (`010-documentation-and-tutorials.ps1`). The `--execute` interface both describe is followed
   exactly — only the filename prefix differs. Noted in `docs/README.md`.
3. **The tutorial's page carries no `data-nw-*` and does not load `boot.js`.**
   `conventions.md` § Progressive enhancement makes the enhancement layer the rule for
   browser-facing markup. `GET /articles` has no interaction at all, so there is nothing to
   enhance and no attribute to normalize; the tutorial says so explicitly and states that the
   moment the page gains an interaction `progressive-enhancement.md` becomes mandatory. Recorded
   rather than quietly exempted.

One correction made rather than recorded: `db/sample/README.md` § "The tests do not truncate" was
made stale by T7's `DeleteByIdPrefixAsync`. It now names the public-feed tests as the one
exception and says why they are not the shared TRUNCATE the section rules out.

#### No duplicated passages

Checked against the eight reference documents. The tutorial states *why a decision was made in
this feature* and links out for the rule; the one place it comes closest is the JNT8007 discussion,
which has no reference document in this repository at all — the `JNT` codes are JauntyQ's and
documented upstream, and `template-diagnostics.md` covers the unrelated `NWT` family. The tutorial
says exactly that rather than implying a local reference exists.

### Fable's read-back, 2026-08-25

`review-deep` was pointed at the four documents with one instruction: check every factual claim
against the repository. It confirmed the quoted snippets, all 19 test names, the arithmetic, the
ports (5399, 5348), `ConnectionStrings:Sample`, the `MSB3202`/`EnsureJauntyQRoot` text, `global.json`,
the `NWT001`–`NWT016` range, the 430/247 line counts, the constitution placeholder and the four
JNT8007 migrations — and found **three real defects, all fixed rather than noted**:

1. **A broken link in a document whose footer claims every reference was checked.**
   `docs/getting-started.md` linked `db/README.md` as `../../db/README.md`, which from `docs/`
   resolves above the repository root. Now `../db/README.md`. The one broken link in the four
   documents, and the most embarrassing possible place for it.
2. **The per-project build claim was wrong outside this machine.** `README.md` said
   `Directory.Build.props` "probes two depths … so `dotnet build apps/web` works from a nested
   location where the solution build will not." Both probes hard-code an ancestor named
   `extrode-legacy-org.com` (`Directory.Build.props:47-48`), while `NativeWeb.slnx` uses the name-agnostic
   `../jauntyq/...`. For any reader whose source root is called something else the relationship
   **inverts**: the solution build works and the per-project build stops at `EnsureJauntyQRoot`.
   Both `README.md` and `docs/getting-started.md` now state the constraint. The build-file fix is
   on `work/todo.md`, deliberately not taken on a documentation branch.
3. **The cleanup-naming caveat was itself inaccurate.** `docs/README.md` said the generated
   scripts follow `<NNN>-<slug>.ps1`; five of thirty do and twenty-five are plain `<slug>.ps1`.
   Corrected to say the directory is not internally consistent, and that the `--execute` interface
   is the part all thirty do follow.

Finding 2 is the one that justifies the exercise. It is not a typo — it is a claim that was true
every time it was tested, because it was only ever tested on the machine whose parent directory
makes it true.

**One test result needed re-measuring, not fixing.** The first `nativeweb.scaffold.tests.e2e` run
failed all 13 facts at exactly `00:15:00` — the fixture's NativeAOT publish budget — while the
review worker was saturating the CPU. Re-run alone on the same commit: **13/13 green in 2 s**. The
tier is not flaky; it has no headroom under load. Noted on `work/todo.md`.
