# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Plan 009 — sample content and a public-site visual refresh. Done on
  `feat/024-sample-content-and-visual-refresh`, 2026-09-03.** Eight demo articles seeded on both
  store paths and held to one dataset by `SeedParityTests`; the sample overrides the framework
  design tokens in its own layer, measured against WCAG AA by `tools/contrast-check.ts`; a hero,
  article cards, and a publication date where the creation date used to be. Plan:
  `docs/plans/2026-09-03-009-sample-content-and-visual-refresh.md`.
- Spec 023 merged into `dev` on 2026-09-03 (see `previous.md`) and pushed. Next in the agreed chain of sample examples: (c) forms — contact, upload, two-step;
  then (d) ops — two roles, an audit trail, custom 404/500, `security.txt` on nw-dev.
- **nw-dev runs `8f5f625`, deployed 2026-09-03 by the operator.** Promoted to blue from
  `4591470`, which was 113 commits behind, so specs 021, 022 and 023 all reached the host in one
  release: `/version` `0.1.0+8f5f625126bd78c2850c4521da4707e0773cb342`, `/readyz` 200 `ready`,
  `/healthz` and `/` 200. Migrations 0002, 0003 and 0004 applied there (0001 was already in
  place), so the server carries the status enum, `published_at` and `categories`. Transport was
  `incus file push` from `incus-host` into `nativeweb-dev` rather than the `--host` ssh
  path. `../justsmtp.com` deployed NativeWeb as part of its own deployment, which makes it the
  second host running framework code from this branch.
- **The local sample databases are still on the pre-0004 schema.**
  `nativeweb_sample_dev` and `nativeweb_sample_test` have not had
  `0004_status_dates_categories.sql`, so every SQL-backed run here needs
  `ConnectionStrings__Sample` pointed at `nativeweb_sample_023rehearsal_test` until they do.
  Applying it needs a yes.
- **Every build depends on a JauntyQ pin.** `../jauntyq`'s current branch no longer carries
  `src/`, so `JauntyQRoot=C:/home/code/extrode.com/jauntyq/.worktrees/nativeweb-build-2a005cb`
  (a detached worktree at 2a005cb) has to be set for any `dotnet` command; the core now lives in
  the public `extrode/jauntyq`. Where the pin should point long-term is an open question.
- **Spec 021 — response formats and content negotiation. Done on
  `feat/021-response-formats-and-negotiation`, 2026-09-02.** `FormatResults` in
  `libs/nativeweb.render` answers text, Markdown, JSON (through a caller's `JsonTypeInfo<T>`),
  XML, CSV, Atom, RSS and a sitemap, each buffered with a declared length, one write and the
  escaping done; `MapNegotiated` in `libs/nativeweb.web` maps `pattern` and `pattern.{format}`
  for GET and HEAD, chooses by extension, then Accept (quality, then specificity, then header
  order, `q=0` a veto), then the first offer, and refuses with a `text/plain` 404 or 406 naming
  the offers; `PublicPageCachePolicy` keys on the chosen format and refuses a body of another
  type. `apps/sample` publishes `/articles` as HTML, JSON and CSV, `/feed` as Atom and RSS,
  `/sitemap.xml`, `/robots.txt` and `/about` as Markdown or text. Two-lens review found 27
  candidates: 13 confirmed and fixed (the chooser let a `q=0` range through and ignored
  specificity, Atom without an author anywhere, U+2028 and U+2029 slipping past the CSV formula
  guard, ragged CSV rows, a sitemap priority rounded away, HEAD answering 405, the cache policy
  widening the default policy's refusal for `Authorization`, and the listing page being cached
  and served to a stranger with no antiforgery cookie, measured through `SampleFactory`), 8
  refuted by running the code, 6 noted in `work/todo.md`. Every new test shown red with its
  code broken. Final counts: render 219, web 164, scaffold integration 117, repo 146, sample
  unit 109, sample integration 8, e2e see `previous.md`. The decision that reverses "no
  negotiation layer" is `docs/decisions/2026-09-02-009-response-formats-in-the-framework.md`.
- **Spec 020 — a deployed sample whose admin can be reached. Done on
  `feat/020-deployed-sample-signin`, 2026-09-02.** Found when the user opened nw-dev's admin and
  saw Tags alone: `Article` is `[Auth(Role = "admin")]`, the only sign-in was Development-only,
  and nw-dev runs Production. `apps/sample/AdminSignIn.cs` mounts a passphrase form outside
  Development, read from `Sample:Admin:Passphrase`; Production exits 78 without it. Unit 105
  green (3 red with the comparison broken), integration green in the scaffold tier and 8 in the
  sample tier (3 red with the 401 and the rate limit removed), the Production child process exits
  78 naming the key, e2e runs the NativeAOT binary as Production over https on port 5351 through a third
  collection fixture. The passphrase is the sample's, not the framework's: the skeleton signs in
  through the OAuth relying party and `deploy.sh` gates the key on the `Sample` prefix only.
  Two-lens review found 18 candidates, 15 acted on (the refusal page for a visitor who already
  holds a session, control characters in the return path, a quoted-blank passphrase passing the
  deploy preflight, the provisioner seeding every prefix, the doc row invisible to the parity
  test, the acceptance criterion naming a `--check` flag that does not exist), 3 noted in
  `work/todo.md`, 0 refuted. Final counts: unit 109, repo 146, scaffold integration 96, sample
  integration 8, e2e 21. **nw-dev still needs the operator to add `Sample__Admin__Passphrase` to
  `/etc/nativeweb/secrets.env` by hand before its next deploy**; the provisioner only seeds a
  file it creates.
- **Spec 019 — mutation residue and baseline. Done on
  `chore/019-mutation-residue-and-baseline`, 2026-09-02.** `tools/stryker-config.json` with
  `ignore-methods: ["ConfigureAwait"]` and `additional-timeout: 30000`, passed by the triage tool
  through an extracted, tested `strykerArgs`. `SweepLeakedFeedRowsAsync` in both tiers' database
  helpers, run once per class through an `IClassFixture` before the feed tests insert: the cleanup
  script's predicate plus `created_at < UtcNow.AddYears(5).AddMinutes(-30)`, computed in C# so a
  leap year cannot differ from the tests' own arithmetic, and age-gated because a sweep by prefix
  alone would delete a parallel Stryker host's live rows. A three-row test pins it and goes red
  without the gate. Baseline re-recorded from a full run: scaffold 88.30% (151/8/12, 56 s),
  sample-unit 27.81% (83/13/205), sample-integration 43.05% (129/103/69, timeouts 39 → 1);
  scaffold's total rose 166 → 171 because `aa15806` changed the cursor codec after the old
  recording. The
  sample-integration drop of 7.25 points is the timeouts becoming survivors, not weaker tests;
  the run left zero future-dated rows. Two-lens review: 16 candidate findings, 7 fixed (a
  collapsed shell continuation in the header, the one-hour feed offset that made the sweep window
  ninety minutes, a test that never exercised the prefix clause and published its rows, a wrong
  filter count in the changelog, a duplicated output path, a test not bound to `TARGETS`), the
  rest refuted by measurement or accepted as the tier's charter. Integration suite 82 green, e2e
  feed tests 5 green after a
  `dotnet build-server shutdown` cleared an MSB4216 task-host failure in the fixture's publish.
- **Spec 008 closed, 2026-09-02, `docs/008-close`.** The three criteria the 2026-08-25 review
  could not flip were amended rather than the code changed, on the user's call: R1 now asks for a
  configuration section and no hand reading (the `AddSignIn(IConfiguration)` overload the review
  said was missing has existed since spec 016, and the skeleton wires sign-in in three lines),
  R11 names the `__Host-signin` default, R12 names the one-time sign-out and the address that is
  no longer echoed. `Status: done`; the spec's "Closed" section carries the R-by-R evidence.
- **Spec 018 — library documentation and the adoption path. Done, on
  `docs/018-library-docs-and-adoption` (2026-09-02).** Six library READMEs (`nativeweb.web`,
  `.render`, `.forms`, `.security`, `.mail`, `.signin.testing`), an adoption guide for an existing
  ASP.NET Core application, and `docs/02-reference/configuration.md` rewritten from 302 lines about
  a departed application to 120 about the keys the binders read. Closes backlog gaps **G6** and
  **G7**.

  **The guarantee, not just the documents.** `configuration.md` had been guarded by a test that was
  deleted along with `apps/web`, which is how it stayed live at `/docs` for months while describing
  nothing. `tests/nativeweb.repo.tests.unit/DocsClaimsTests.cs` replaces it in the repository tier,
  so it cannot leave with any one application: every repository path and relative link in the
  documented set resolves, every capitalised symbol named appears somewhere in the sources, and the
  `Key("...")` literals are extracted from `NativeWebOptions.cs` at run time and matched against the
  reference **both ways**. No hand-maintained claim list — a list goes stale the way the document
  does. Library READMEs are swept, so a new library is covered the day it gets one.

  **It caught real defects, including one of mine.** `configuration.md` named `AddNativeWebSignIn`;
  the library declares `AddSignIn`. The check it cannot make — a symbol that exists but in a
  comment — was found by reading: two delegated READMEs documented `NativeWeb:ContactKey` and
  `NativeWeb:Smtp` as settings, and that prefix left with `apps/web`. Both libraries read no
  configuration at all.

  **The adoption guide was proved by running it**, not by reading it: a stock `dotnet new web`
  project taken through every step in `tmp/adopt-probe/`, serving a generated template page with
  `/healthz` 200, `/readyz` 503 and the full security-header set. That run found **five** things the
  guide had wrong or missing — a JauntyQ checkout is required as a second sibling, the templates and
  locales need declaring as `AdditionalFiles`, `wwwroot/pages/error.html` is required or the host
  exits 78, `UseNativeWebPipeline` takes the locale list rather than the options, and generated code
  lands in the `RootNamespace` where a `using` will not reach it. All five are now in the guide.

  **Then a fable review of the merged branch found the guard weaker than it reads**, and
  `fix/019-docs-guard-gaps` closed it (2026-09-02). Four ways `DocsClaimsTests` could stay green
  over drift: the reverse key check saw no sign-in key, because sign-in keys are written bare
  rather than section-relative and the extractor wanted `{Section}:`; `PresentTenseDocs` was
  filtered through `File.Exists`, so renaming a document silently narrowed the guard instead of
  failing it; `EveryLibraryReadme_IsCovered` built both sides of its assertion from the same
  enumeration and could not fail, and is now `EveryRuntimeLibrary_HasAReadme`, which is the check a
  sweep of READMEs cannot make; and symbol claims were a substring search, so `Take` passed on
  `TakeAsync`. Tightening the last one found a false claim already in the tree — the scaffold
  README named a `RequireForm()` filter that does not exist, passing on `RequireFormAntiforgery`.

  **The extractors now have a canary.** They match five call shapes, and a key read through a sixth
  would bind while the reference never named it, with every other check green — the same failure
  one level down. Any key-shaped string literal in either binder must now be reachable by an
  extractor or listed in `NotKeys` with a reason. Six mutations were run and each went red
  distinctly, including a `section.GetValue<bool>("...")` read that no extractor matches.

  The review also refuted one of its own leads (`scripts/cleanup/015-framework-web-seam.ps1` exists)
  and corrected three documentation claims: the `AddNativeWebKestrel` row named timeouts it does not
  register, the `MSB9008` sentence read as if MSB9008 were the `CS0246` pile, and the adoption guide
  never said `KnownProxies` must be set behind a proxy — omitting it builds, boots, serves every
  page and collapses every client into one rate-limit partition, with nothing failing or warning.
  Spec 018's fourth acceptance criterion claimed a test fails whenever a named type or path moves;
  that absolute is not met and now states its limits in place.

- **`ForwardedHeaders:ForwardLimit` is bound again. Done on
  `fix/forwarded-headers-forward-limit` (2026-09-02).** A regression, not a gap: `apps/web`
  bound the key and took the binding with it on 2026-08-31, while `provision.sh` kept seeding
  `2` into every `--edge` host. `AddNativeWebForwardedHeaders` therefore ran at ASP.NET Core's
  default of 1, stopped the `X-Forwarded-For` walk on the edge proxy, and gave every client in
  the world one rate-limit partition and one wrong entry in the request log, with nothing
  failing.

  **The decision was the user's and it went out to three models first** — `think` (fable),
  `glm-think` (glm-5.3) and `qwen-review` (qwen3.8-max), each scoped to the same four files and
  told nothing about the others. All three picked binding the key over deleting the seeded line,
  and all three gave the same mechanism: the trust check gates which hops may be unwound, the
  limit caps how many are, and they are independent. That retires the "one more spoofable hop"
  reasoning the backlog entry carried since the 015 review — the walk stops at the first address
  that is not a known proxy however high the limit goes, which is now its own test. qwen added
  that `seed_config` never overwrites, so deleting the line would have left it on every
  provisioned host and needed a cleanup script; fable named `README.md:123` as a second place
  documenting the dead key. The one fact none of them could check, `Caddyfile.edge`'s
  `X-Forwarded-For` handling, was checked here before starting: it trusts the edge and appends,
  so the two-hop chain is the one that arrives.

  Bound with a named startup error rather than a silent fallback, since an operator who writes a
  value would otherwise never learn it was ignored. `configuration.md` carries a real row where
  its "Not read, despite appearances" section was, and `DocsClaimsTests.DocumentedAsUnread` is
  now empty — that test is what turned red on the change, which is the guard working. Measured:
  web-unit 102 passed (95 before), repo-facts 145, sample builds with 0 warnings. Mutation:
  dropping `forwarded.ForwardLimit = options.ForwardLimit` fails the two-hop tests and nothing
  else.

- **`ScaffoldCursor.Encode` no longer substitutes for a lone surrogate. Done on
  `fix/cursor-encode-strict-utf8` (2026-09-02).** The other half of the U+FFFD defect spec 018's
  property tests found on the decode side: `Encoding.UTF8.GetBytes` replaced a lone surrogate in
  `Property`, `SortValue` or `Key`, so the token decoded to a different cursor and the reader
  landed on the wrong window with nothing reporting it. `Write` became `TryWrite` over
  `Utf8.FromUtf16` with `replaceInvalidSequences: false`; `Encode` returns the empty string, the
  refusal an over-long part already took. The surrogate cases are a `Fact` rather than a `Theory`
  on purpose — xUnit's argument serialization replaces a lone surrogate before the test sees it,
  so as `InlineData` all three arrive well-formed and assert nothing. Mutation: allowing
  replacement again goes red.

- **The departed `NativeWeb:` prefix, out of the libraries. Done on `fix/stale-nativeweb-prefix`
  (2026-09-02).** The backlog note above became work: `SmtpOptions.Validate` threw
  `NativeWeb:Smtp:FromAddress` at an operator whose configuration says `Sample:Smtp:*`, and four
  `ContactProtector` comments named `NativeWeb:ContactKey`. Neither library binds configuration,
  so both were keys nothing reads. Messages are section-relative now, the comments name no key,
  and `DepartedPrefixTests` sweeps every `.cs` and `.md` under `libs/` for the prefix in key shape
  — prose like "for NativeWeb: a source-generated ..." is not a setting, so an uppercase letter
  after the colon is what matches. Two mutations, each red: the prefix back in a comment, and the
  old message restored.

- **Plan 008 — mutation-driven test strategy for the paging seam. Done and merged into `dev`,
  unpushed (2026-09-01).** `test/018-paging-property-tests`, prompted by the user's
  `testing-strategy-handover.md` and by the fact below: 017 shipped eight defects and **no test
  failed on any of them**. The gap was assertions, not coverage, which only a mutation run
  measures — so Stryker.NET 4.16.0 ran *first*, before anything was written, and named it.
  **`libs/nativeweb.scaffold` 75.46% → 85.54%**, `DemoStore.cs` 70.0% → 76.0%,
  `apps/sample` at the integration tier 44.74% → 50.29%. 55 tests across five files; CsCheck
  4.8.0 in the test tier only.

  **The worst single finding: all fifteen mutants in `ScaffoldAccess.CanReadAsync` survived
  because nothing executed it.** Its only two tests asserted that the *generated source contained
  the call* — a substring match over a file, which is the lying-test shape. Five branches of the
  admin index's authorization decision, never run.

  **One production change, caused by a test.** `ScaffoldCursor.TryDecode` accepted a tampered
  token: `Encoding.UTF8.GetString` substitutes U+FFFD rather than refusing malformed bytes, so
  the decoder reported a cursor this release did not write as valid, lossily. `TryRead` now uses
  `Utf8.ToUtf16` with `replaceInvalidSequences: false`. No behaviour change for a reader.

  **A review then found two properties that passed for the wrong reason, and the score did not
  move for either** — both killed their mutants while asserting less than their names claimed.
  That is the limit of a mutation score, written up in the plan: it measures whether a test *can*
  fail, not whether it fails for the stated reason.

  **The triage tool then broke an e2e test, and the test deserved it.** Stryker kills a host it
  judges hung, the killed process skips the `finally` that removes its fixture rows, and the feed
  suites date theirs five years out — so five future-dated rows survived a 15:57 run and
  `Feed_UnderNativeAot_OrdersNewestFirst` went red at 16:2x on a branch that does not touch the
  feed. It asserted `titles.First`/`Nth(1)`, an absolute DOM slot, which is a claim about every
  other row in `articles`; the integration tier asserting the same fact relatively was unaffected.
  Rewritten to relative positions. A re-run at 17:10 passed under the old assertion too, because
  the leak only bites for the hour after it happens — which is what makes it a flake worth naming
  rather than a mystery. `scripts/cleanup/018-paging-property-tests.ps1` listed the rows and the
  operator ran it on 2026-09-01; all five are gone.

  `tools/mutation-triage.ts` re-runs the three targets and compares against
  `tools/mutation-baseline.json` with a 1-point tolerance. Manual, not CI — the integration
  target drifted 17 → 39 timeouts between two runs of the same commit under different machine
  load, and timeouts score as killed.

- **Spec 017 — scaffold list sorting and cursor pagination. Done and merged into `dev`, unpushed
  (2026-09-01).** All seventeen tasks are `[x]` and the spec is `Status: done`. The seam, the
  generator, three stores, eight keyset `.sql` files, three composite indexes, and all three test
  tiers. **15 suites, 1,412 tests, 0 failures**; the NativeAOT publish gate exits 0 with no trim or
  AOT warnings, at 28.4 MB. Closes backlog gaps **G5** and **G2**; the breaking-change log is
  `docs/02-reference/breaking-changes.md`, linked from the README, and the four docs and two
  handovers that still described the old seam say what closed it.

  Four decisions taken before the spec: no structured filtering, the seam breaks rather than being
  shimmed, sortable columns are declared by `[ScaffoldSort]`, and the numbered pager becomes
  Previous/Next with the total kept.

  **Eight defects, and not one of them failed a test on its own.** Two came from writing the unit
  tier: `Base64Url.TryDecodeFromChars` throws on a character outside the alphabet, so a mangled
  `?after=` was a 500 until `IsValid` went in front of it; and the cursor's key was written in
  display form, which rounds a timestamp to the minute. Six more came from the two-lens review of
  the finished branch, three of them one shape — **a store that declines a cursor and starts over
  must return one row FEWER than asked**, because the list trims a backward page from its tail
  using the direction of the cursor it issued and cannot know the store ignored it. Both stores
  dropped the first row of page one. That is now a third stated obligation on
  `IScaffoldStore.ListAsync`. The others: a `CreatedAt` cursor that would not parse reset its
  position but not its mirror and landed the reader on the last page; a cursor past the end
  rendered no pager at all; and `?sort=` naming an unsortable column kept the requested `?dir=`.

  **Measured and left alone**, in `work/todo.md`: the author keyset boundary lands in `Filter`, not
  `Index Cond` — `EXPLAIN` on `nativeweb_sample_test` — because a disjunction over `IS NULL` is not
  a rangeable start point, so paging a *nullable* column walks the index from the top. A row-value
  comparison cannot express it. Non-null sort columns are unaffected.

  **The cleanup script has been run by the operator (2026-09-01):**
  `scripts/cleanup/017-scaffold-sorting-and-cursor-pagination.ps1 -e -DropTestRows
  -RemoveSupersededQuery`. It removed nine scratch files (833.5 KB), **100,980** rows from
  `articles` in `nativeweb_sample_test`, and the superseded
  `db/sample/queries/Articles/List.sql`. The row figure is the one to remember: the dry run a day
  earlier listed 4,242, and the 018 property tests plus three Stryker targets re-running the suite
  once per mutant added the rest. The removal is on `chore/019-remove-superseded-list-query`.

- **Phase 3 of the readiness plan.** T11 (the scaffold defect batch) and T12 (deploy and database
  hygiene) are both done. T12: the deploy chain publishes `apps/sample` by default and names no
  project past `build.sh`; `db/migrate.sh` was retargeted at `db/sample/migrations` rather than
  retired, with the superuser extensions step made optional; and the publish-budget launch now sets
  `Sample__Telemetry__Enabled`, so the OpenTelemetry registration runs inside the AOT image again —
  it had been set under the wrong configuration section since `apps/web` left, and nothing was red.
  Nothing in `scripts/deploy/` has still ever run on a real host: that is T15, below. **D2 is
  answered (2026-08-31): the generated admin stays open by default** — an unguarded model is
  reachable anonymously, and no consumer breaks. Plan 007 is therefore complete: phases 1, 2 and 3
  are all built and merged, and what is left of it is the two decisions, both now taken (D1: ship
  without auth for now; D2: open by default).

- **The seven review findings from the phase-3 branch are fixed** (`fix/021-review-findings`,
  2026-08-31). The two that would have been noticed by an operator rather than a test: the deploy
  preflight demanded ten keys under a prefix no binary binds, nine of which no code reads, so it
  proved nothing while the settings it named stayed unset; and the data protection key ring was
  composed relative to a working directory that is read-only under `ProtectSystem=strict`, which
  cost every form POST a 400 after each deploy. The rest are the scaffold generator emitting
  uncompilable assignments for unrecognised types and for read-only members, `DemoStore` reporting
  404 for a row a concurrent save had replaced, and `skeleton/db/migrate.sh` shipping without the
  run lock and atomic wrap this repo's runner has. Five tests that asserted a substring existed
  somewhere in a file were rewritten to parse the structure they name, each mutation-checked.
  **A second Fable pass over those fixes found six more**, three of which were breaks rather than
  overstatements: the remote deploy path never read the stamp the first fix added, so every
  `--host` and `--incus` deploy would have failed at the migrate step; the migration runner looked
  for `0002_extensions.sql` while the skeleton ships `0001`, so every fresh stamped product would
  have failed its first migration as the app role; and the key-directory guard tested for an empty
  home, which the system account it was written for does not produce. All fixed and
  mutation-checked. **A third pass over those** found five more, including two the second pass
  introduced: the new probe created the directory it tested, which a privileged process does
  happily, and taking the first glob match reinstated the app-role failure for a second extensions
  migration. 648 tests green across the eight suites in `NativeWeb.slnx`. Three review rounds on
  one branch is the record worth keeping: each round's fixes carried defects of their own, and
  only the third came back with nothing that breaks a deploy.

- **The four coverage gaps the 021 audit named are built** (`test/021-coverage-gaps`, 2026-08-31),
  and two of them came back with an answer the audit did not have. The generator's language-version
  floor is **C# 11**, not the C# 10 the audit proposed as the gate: the emitted source needs a raw
  string literal and a static abstract interface member, and the second is not ours to lower, so a
  consumer below 11 could not bind a minimal-API parameter at all. The `[ScaffoldSearch]` gap did
  not exist -- emitting an empty `SearchFields` set already turns two integration tests red, which
  is a fact measured by mutation rather than an entry taken on trust. The other two are new tests:
  the generated update handler's second 404 is now executed through a stub store rather than read
  out of the emitted source, and a refused configuration is proved at the process boundary with
  exit 78 and the key named on stderr. That last one could not be driven through the key-directory
  branch it was written for: `GetFolderPath(UserProfile)` reads the process token on Windows, not
  `USERPROFILE`, so a malformed `BotTraps` value carries the same fact instead. 657 tests green
  across the eight suites. Two things found on the way, both on `work/todo.md`. The first is now
  **fixed** (`fix/static-error-shell-exit`, 2026-08-31): `StaticErrorShell.Load` threw out of
  `Main` instead of taking `FailStartup`'s exit -- the modal-block failure the guard exists to
  prevent -- and the `IWebHostEnvironment` overload now routes a missing or unreadable shell to
  exit 78 with the path on stderr, proved at the process boundary and mutation-checked. 658 green.
  The second is not ours to run: six untracked test directories are `bin`/`obj` residue of the
  departed identity provider, listed in `scripts/cleanup/021-coverage-gaps.ps1`.

- **T15 of spec 011 — one third done, 2026-09-01.** `provision.sh` and `deploy.sh` have now run
  for real: `nativeweb-dev` serves `apps/sample` at `nw-dev.example.com`, two releases landed
  blue/green in 1.774 s and 1.888 s, and the `NOT YET MEASURED` figures in
  `docs/02-reference/deployment.md` are measured. **R1 is met; R16 and R17 are not.** The restore
  drill cannot start because no backup exists: every key in `/etc/nativeweb/backup.env` on that
  host is present and empty, so the 03:17 timer fails nightly on
  `BACKUP_S3_BUCKET: set BACKUP_S3_BUCKET`. Filling it needs a bucket, credentials and an age
  keypair the operator generates — a session must not, since the private key decrypts every
  backup. R15 stays conditional on `BACKUP_HEARTBEAT_URL`. `nativeweb-prod` is untouched and out
  of scope.

- ~~**The epass.io production cutover.**~~ **Closed 2026-08-31 as a NativeWeb concern** —
  `docs/decisions/2026-08-31-008-epass-cutover-closed.md`. Checked against the real sibling
  checkout that day: epass.io has built and merged its own browser surface, consumes only
  `libs/nativeweb.render` and `libs/nativeweb.security` from here, uses nothing the identity
  provider's departure deleted from either, and its red CI has three causes all in that repo.
  Nothing is owed to it but a push of `dev`, which is the user's call. **The term is retired
  here; do not reopen it from an older document.**
- Behind it, the `work/todo.md` items that need a decision rather than work: the skeleton's
  release process (whether NativeWeb versions and publishes itself), the session key type
  (JauntyQ-wide), and pinning the skeleton's two framework checkouts to tags, which cannot happen
  until either framework repo publishes one. The Node 20 → 24 action bump that used to sit here is
  **done** (2026-08-20) — see `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.
- `work/milestones.md` was filled on 2026-08-30 with six destinations, so "where this project is
  going" now has a record. Three of the six are `in progress`; T15 above is milestone 3.
