# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **OAuth back-navigation walk** — `test/oauth-back-navigation`, 2026-08-30. The last unblocked
  item from 012's open questions. No defect: htmx's removed history cache never applied here, since
  nothing in `apps/web` sets `hx-push-url`, `hx-boost` or `hx-history`. The test pins what does
  matter — a re-issued authorize GET after the code is redeemed renders a fresh consent screen, and
  the request count proves it reached the server (**1 → 2**, mutation-checked). **4 OAuth e2e
  facts** (+1).

- **Sticky busy control** — `fix/busy-control-detached-source`, 2026-08-30. A submit control left
  disabled forever when the response swapped away the region that issued the request. Cause is
  **not** what the 012 review recorded: htmx reassigns `ctx.sourceElement` to the swap target while
  swapping (`FORM.auth-card` at `before:request`, `DIV.auth-board` at `finally:request`, measured),
  so `boot.js` read its busy reference off an element that never had one. The reference rides on
  the per-request `ctx` object now. **72 web e2e pass** (+1), 652 unit unchanged.
  - The backlog's proposed fix — bind at-target with `{once: true}` — was implemented first and
    **fails the regression test**, because htmx's `trigger` ends `isConnected ? element : document`
    and routes a detached source's event to `document`. Delegation was the thing that worked.
    Symptom was evidence; mechanism was a guess; one instrumented run contradicted both readings.

- **Scaffold e2e publish budget** — `fix/publish-budget-diagnostics`, 2026-08-30.
  `SampleProcessFixture` allowed the NativeAOT publish 15 minutes; `AppProcessFixture` in the web
  tier already allowed 30 for the same publish. Raised to 30. **13/13 pass, 2 s** from a detached
  worktree — the main tree cannot run this tier while a Debug `nativeweb.web` holds `apps/web/bin`.
  The `work/todo.md` entry that logged this named `AppProcessFixture`; the constant was in
  `SampleProcessFixture`, and the entry now says so.
  - Its first verification run looked hung for 45 minutes and was not: the background command
    piped through `tail -25`, which buffers to EOF, so the output file stayed at 0 bytes while the
    tier ran to completion. Redirect a long background run to a log file instead.

- **Unsupervised backlog run** — 2026-08-29, one branch per item, each merged to `dev` as it
  finished. `test/antiforgery-guard-agreement`: the two antiforgery guards are now held to
  agreeing across the three feature states, mutation-checked both ways (one guard drifts → fails;
  both drift together → still fails). **652 unit tests pass** (+3).

- **Stale `extrode-legacy-org` references** — `fix/stale-extrode-legacy-org-references`, 2026-08-29. Two survivors of the
  2026-08-26 org rename, each with the test gap that let it survive: `<Authors>extrode-legacy-org</Authors>` in
  **ten** packable csproj (now one central value in `Directory.Build.targets`, verified through
  `dotnet pack`), and `skeleton/nuget.config`'s `framework-github` feed host (now `extrode`, and
  `OrgReferenceTests` now scans that file too). **649 unit tests pass** (+2, both mutation-checked).
  CI stays red regardless: the JauntyQ 403 in `todo.md` is a package grant only the user can add.

- **Bench harness, third review pass** — `fix/012-bench-review`, 2026-08-29. Fable (`review-deep`)
  read the whole harness diff after the glm correctness and consequence lenses had already run and
  8 findings had been fixed. It found **5 more, 4 of them holes that let a check pass without
  checking**, all confirmed by reading the code and all now fixed with mutation-checked tests:
  the `--check` settings-mismatch escape (exit 2 now), a measured RSS of 0 passing the ceiling,
  `isBaseline` validating the envelope but not the numbers, and cert reuse checking expiry but not
  the chain. The fifth was a vacuous test of its own — the remint branch was asserted by a call
  with no assertion after it, on a pair that took the reuse path anyway.
  **95 harness tests / 0 failed**; `--check --skip-publish` exit 0, `--duration 5` exit 2.
  - The lesson worth keeping: two lenses plus adversarial verification did not find these. A
    third reader on a different model did, on code already declared clean.

- **Licensing** — `docs/isl-netc-licensing`, 2026-08-29. The repository is licensed: ISL-NETC 1.0
  verbatim in `LICENSE.md`, the Licensor override in `NOTICE.md`, ISL-CLA 1.1 adopted in
  `CONTRIBUTING.md` with optional §6 (no AI-generated contributions) opted in, `CONTRIBUTORS.md`
  as the signing sheet, and the packable libs packing the license and notice.
  **647 unit tests pass** (+6, `LicenseFilesTests`, each mutation-checked).
  - **What is still open: ISL-CLA 1.1 is a draft its own drafters say not to accept contributions
    under.** islamiclicense.org merged it to their `dev` on 2026-08-29; it publishes at
    <https://islamiclicense.org/isl-cla/1.1/CLA.md> once deployed, and 404s until then. The adoption
    here is recorded in advance and flagged not in force at the top of `CONTRIBUTING.md`. When it is
    marked final, delete that banner — nothing else in the file changes.
  - **The version moved 1.0 → 1.1 on 2026-08-29, `docs/isl-cla-1-1`.** The agreement had been
    amended twice in place under a fixed 1.0 — three required statements to **four**, then to
    **five** with **(e) the forum** and a second §9.2 placeholder `[FORUM]`. Told that adoption is
    by name *and version*, islamiclicense.org undid it: `ISL-CLA-1.0.md` is frozen back to its
    original four-item text and the revisions are `ISL-CLA-1.1.md`. This repository states five
    items, so it adopts **1.1**. The table labels (a)–(e); `LicenseFilesTests` asserts the version,
    (d) and (e).
  - **The license family moved to 1.1 the same day** — `[LICENSOR]` becomes a placeholder the
    adopter fills, retiring the `NOTICE.md` override. **Merged on their `dev`, not published**, so
    the live site still serves 1.0 and `/isl-cla/` still 404s. Their CHANGELOG says a 1.0 copy with
    a documented override "remains internally coherent", and the frozen 1.0 texts stay online at
    `/isl-x/1.0/LICENSE.md`. Bumping waits on their deploy; see `work/todo.md`.
  - Jurisdiction and Licensor are not invented: **Commonwealth of Virginia** and **Extrode LLC**,
    matching `jauntyq/LICENSE.md` §6.4 and `LICENSE-EULA.md` §7.4.
  - ISL-NETC 1.0 itself is in force (effective July 2026), so the outbound side needed no wait.
    Neither text has been read by counsel; both say of themselves that they should be.

- **Backlog sweep** — `chore/backlog-sweep`, merged to `dev` 2026-08-29, unpushed. Five `Now-ish`
  items from `work/todo.md`, each with tests and each mutation-checked. Two are worth more than
  their line in the backlog:
  - **`skeleton/Directory.Build.props` was not valid XML** (`--` inside an XML comment), so MSBuild
    refused to load it with `MSB4024` and **no stamped product had ever built** — it failed on its
    own README's first command, before compiling a line. Found by stamping one and running it,
    which is the only thing that would have found it: the suite covers `apps/` and `libs/`, whose
    build files MSBuild loads every run, while the skeleton's are copied as text and parsed by
    nobody until a product exists. `BuildFileXmlTests` now parses every tracked build file via
    `git ls-files`. Verified by stamping `Sectest` and building it: 0 warnings, 0 errors.
  - **The `security.txt` contact is configuration**, in `apps/web` and the skeleton both, and
    unset serves no file rather than one naming a mailbox the deployment does not own. The
    newline guard on the value is reachable rather than defensive padding, which mutation testing
    is how I know: `Uri.TryCreate` accepts `https://e.com/r\nContact: mailto:evil@x.net` as a
    valid absolute https URI, so without the guard that reaches the file.
  - Also: 20 cleanup scripts repointed off the pre-extrode path, `SdkPinTests` asking git rather
    than walking into gitignored scratch, and `apps/web`'s launch profile turned HTTPS.
  - **Suite after the sweep: 640 unit / 310 integration / 71 e2e, 0 failed**; solution build 0
    warnings, 0 errors. The +29 unit and +3 integration are this branch's own facts.

- **Spec 013 — continuous read-only audit** is merged to `dev`, unpushed, all fourteen
  tasks ticked, **253 tests pass / 0 fail** (`cd tools/audit && deno test -A -q`). A daily
  unattended read of one slice of this repository on audit-host.local, producing a deduplicated defect
  ledger that an interactive session triages with the user. Read-only by topology, not by
  instruction: audit-host audits a throwaway clone of a pushed mirror and holds no credential that
  reaches `origin`. Scope is `nativeweb` only.
  - **`013-spec.md` is `built`, not `done`, and R2 and R15 are why.** They are properties of
    audit-host after the user runs `scripts/audit/provision-audit-host.sh --execute` on it — the same shape of
    outstanding as spec 011's T15. The R-by-R table is in `013-spec.md`, "Acceptance review".
  - **R14 is met in two clauses of three, and the spec says so.** Nothing in the audit path opens
    a branch or writes to the repository, structurally, because the audit only writes to its own
    store. That `accepted` needs the user's agreement is a **record, not an enforcement
    boundary**: `--approved-by` is a flag anything with a shell can type. What holds is that the
    audit never calls `triage.ts`.
  - **A mutation test pushed a full mirror of this repository to audit-host.local**, unasked, on
    2026-08-29 at 14:49: the mutation removed the sync script's dry-run `exit 0` and the script
    went on to push for real. Nothing reached `origin`. The test now stubs `ssh` and asserts it
    was never invoked. The mirror is gated behind `-DropRemoteMirror` in
    `scripts/cleanup/013-continuous-audit.ps1` — keep it if the audit host is going ahead.
  - The audit's own first real run found **one genuine defect in this repo** and it is on
    `work/todo.md`, not fixed: `docs/01-architecture/native-web-architecture.md:133` points at
    `docs/99-design/design-decisions.md`, which no tracked file matches.
  - Operator guide: `docs/02-reference/continuous-audit.md`. Spec/plan/tasks at
    `docs/specs/013-continuous-audit/`.

- **R16 load harness** — `feat/012-bench-harness`, merged to `dev` 2026-08-29, unpushed.
  `tools/bench/` publishes `apps/web`, drives the published binary over https and averages three
  ten-second passes per scenario against a committed `baseline.json`. **60 facts, 0 failed**
  (`cd tools/bench && deno task test`), four of them mutation-checked.
  - **First baseline (bench-host, 16 cpu, win-x64, 2026-08-29):** `/healthz` 18,067.2 req/s, 0.83 ms p50,
    74.4 MB peak RSS; `home` — the full page render — 6,998.0 req/s, 2.11 ms p50, 60.9 MB. Zero
    errors in both.
  - **It has to serve https, and that was measured rather than assumed.** The first real run
    recorded a baseline in which every `GET /` answered 500: the antiforgery cookie is
    `CookieSecurePolicy.Always` and `Layout.Page` mints a token on every page, so over http the
    render throws. 245,106 errors, 0 req/s, and `--baseline` wrote the file without complaint. It
    now refuses a measurement with failed requests, zero throughput or no RSS reading, and
    `tools/bench/cert.ts` mints a throwaway CA and leaf with openssl — a CA *and* a leaf because
    Deno rejects a trust anchor offered as an end entity.
  - **Tolerances are 20% down on throughput and 25% up on RSS, not the 5% the plan proposed.**
    Eleven `home` passes across five sessions on one unchanged binary spread 16.3%, peak RSS 29%. A 5% band would fail
    on noise more often than on a regression — which is also why this is not a CI job.
  - **`012-spec.md` is `done`.** R16 is closed as *within tolerance of `baseline.json`* rather
    than as the §14 re-run it originally asked for, which is impossible: those figures came from
    two apps that are not in this repo and a sandbox that is gone. §14 now says the old table is
    frozen and points at `tools/bench/` for regression work. Plan:
    `docs/plans/2026-08-29-005-r16-load-harness.md`.
  - Post-012 web suite is unchanged by this branch: **641 unit / 310 integration / 71 e2e, 0
    failed**. The harness is Deno and adds no xUnit facts.

- **Spec 011 still has not reached `done`, and that is unchanged by 012.** T15 -- the provisioning
  walkthrough, the restore drill and the disaster-recovery run -- is executed by a person on a real
  host, and `011-spec.md` stays `draft` until its measured times are in
  `docs/02-reference/deployment.md`. That remains the first thing standing between this repo and a
  production host, ahead of the cutover decision in `next.md`.

- Spec 010 — documentation and tutorials — merged into `dev` on 2026-08-25
  and rolled to `previous.md`; all ten of its tasks are ticked and `010-spec.md` is `done`. Every
  numbered spec in `docs/specs/` is now `done` except 008, which stays `draft` for the reason
  below.
  - **What comes next is a decision, not a task.** `next.md` holds the epass.io production
    cutover, and it is gated on the whole-versus-partial scope question in
    `docs/architecture/002-epass-cutover-capability-gap.md`. Nothing under it can be scheduled
    until that is answered.
  - **Four unscheduled merges landed after 010 and are on `dev` at `1004220`**, none of them a
    tasklist entry, all of them pushed to `origin/dev` on 2026-08-27: `chore/move-to-extrode`
    (cleanup script for the source-root rename), `fix/jauntyq-root-name-agnostic`,
    `feat/docs-site` (the `docs/` restructure below), and `fix/skeleton-probe-case`. `dev` and
    `origin/dev` are level; there is no `main` branch yet, local or remote, because nothing has
    been released.

- **Spec 009 — three consumer seams for the sign-in library.**
  `feat/009-signin-consumer-seams`, merged to `dev` 2026-08-25, unpushed. The three gaps
  justsmtp.com found while migrating onto the library under 008 R12: no way to end a session
  mid-request, no way for a test to mint a ticket, no seam for a consumer's own
  address-plausibility check. All three closed, all three optional.
  - **R9 is not this repo's work and is deliberately unmet here.** justsmtp adopting the seams
    and deleting its three workarounds is a separate branch in a separate repository, sequenced
    after this merge. Until that lands, the seams are compiled rather than used.
  - **The justsmtp migration DID land** (2026-08-19), which retires the note that used to sit
    here. Spec 008's `Status: draft` is now simply stale — logged on `work/todo.md` rather than
    flipped, because confirming its fifteen acceptance criteria is a review of 008, not a line of
    009's work.
  - **A worktree cannot build `NativeWeb.slnx`.** Found on this branch. The
    `Directory.Build.props` half was fixed here (`JauntyQRoot` now probes two depths); the
    solution half needs a decision and is on `work/todo.md`. Per-project builds work with no flag.
- Scaffolding POC (brainstorm 001 T1.3) is merged; follow-ups on `work/todo.md`, none blocking.
  - **The two-lens review found eleven real defects in 009's own work**, all fixed on the branch
    before it merged, one dropped as refuted. Four were behavioural — a policy timeout escaping as
    a 500, `SignOutAsync` leaving `Context.User` authenticated, a second `OnAddressSubmitted`
    silently disabling the first, and `no-store` set by one sign-out path but not the other — and
    four were **prose on this branch asserting something the code did not do**, chiefly the
    testing package being called undeployable while `DevelopmentDependency` was missing. The
    R-by-R table in `009-spec.md` now reads R6 as met for the failure code and NOT for the
    response shape; that half is documentary and pinned by a test. Detail: `009-spec.md`,
    "What review found".
- Suite counts, **all eleven tiers re-measured on `dev` at `1004220` on 2026-08-27 — 1,373 facts,
  0 failed, 0 skipped**: 509 web-unit / 298 web-integration / 64 web-e2e / 157 render /
  16 sample-unit / 64 scaffold-unit / 31 scaffold-integration / 13 scaffold-e2e / 184 signin-unit /
  34 signin-integration / 3 signin-e2e. Solution build is 0 warnings, 0 errors.
  **A twelfth project, `nativeweb.signin.epass.tests.aot`, holds no tests and `dotnet test` skips
  it silently** — `IsTestProject=false`, by design. Its assertion is
  `dotnet publish tests/nativeweb.signin.epass.tests.aot -c Release`, which exits 0 with no IL2026
  and no IL3050. **It runs in CI as of 2026-08-29** (`chore/aot-gate-in-ci`), as a second step in
  the `aot-publish` job. It has to be a second publish: `apps/web` holds no ProjectReference to
  `nativeweb.signin.epass`, so the library's IL never reached the existing link and a green
  `aot-publish` said nothing about it. Between `dotnet test` collecting zero facts in silence and
  CI never linking the library, its only trigger had been someone remembering it.
  This supersedes the 1,308 measured on `feat/009-signin-consumer-seams` on 2026-08-25. The +65 is
  work merged after that branch, **not drift**: scaffold-integration 17→31 and scaffold-e2e 4→13
  from the scaffolding follow-ups, web-unit 494→509 and signin-unit 177→184 from the JauntyQ-root
  and skeleton-probe fixes, and 16 sample-unit that the 2026-08-25 breakdown omitted entirely.
  **These counts were measured, not perturbed** — the tiers were re-run as a state check, and no
  new fact was written on this pass.
- Spec statuses: 001–007 `done`, 009 `done`. **008 says `draft` and, after the 2026-08-25 review,
  should.** Twelve of its fifteen criteria are met and evidenced; R1, R11 and R12 each describe
  something the shipped code deliberately does differently, so flipping the status is the user's
  call rather than a correction. R-by-R table in `008-spec.md`, "Acceptance review".
- The AOT publish gate for the sign-in library measures exit 0, zero IL2026/IL3050, **16,786,944 B**
  exe (16.6 MB before 009); re-run after the review fixes. Note it only compiles natively when given a RID: `PublishAot=true` with
  no `-r` gives the analyser pass and a managed publish, which is what the tests.aot project does
  by default. 009's gate was verified both ways.
