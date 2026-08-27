# Todo — NativeWeb

The unscheduled backlog: everything known to be worth doing that nobody has committed to a date.
Items enter here from anywhere (a review comment, an out-of-scope note, a bug found in passing)
and leave by being promoted into `status/next.md` or dropped with a reason.

Keep it honest rather than tidy. A backlog that only contains things someone intends to do is a
wish list; the value is in recording the ones nobody has got to yet.

## Now-ish

- **The sign-in library's AOT publish gate is in the solution but runs nowhere automatic.** Found
  2026-08-27 while re-measuring the suite. `tests/nativeweb.signin.epass.tests.aot` sets
  `IsTestProject=false` and holds no tests, so `dotnet test` collects nothing from it and exits 0
  in silence — a per-project run prints not one line. CI does not cover it either: the
  `aot-publish` job publishes `apps/web/nativeweb.web.csproj` only, and `apps/web` has **no
  ProjectReference to `nativeweb.signin.epass`**, so the library's IL never reaches that link.
  Its assertion is `dotnet publish tests/nativeweb.signin.epass.tests.aot -c Release`, which is
  exactly what spec 008 T14 built it for: catching a `Map*` call the Request Delegate Generator
  did not see inside the library, before a consumer's publish finds it six months later. Run by
  hand on 2026-08-27 and green — 0 warnings, no IL2026, no IL3050 — but a gate whose only trigger
  is someone remembering it is not a gate. Either add it to the `aot-publish` job or say in
  `README.md` that the suite does not include it.
- **`SdkPinTests.NoWorkflowInstallsAnSdk` scans gitignored scratch.** Found 2026-08-25 while
  verifying spec 010. It walks the whole repository for `.github/workflows/*.yml`, so a checkout
  placed under `tmp/` — exactly where AGENTS.md says scratch belongs — turns the suite red with
  "setup-dotnet is back in: tmp/<whatever>/.github/workflows/ci.yml". The message names a real rule
  and a file nobody committed, which is the worst kind of false positive. It should skip anything
  git ignores, the same way `NewProductStampTests` already asks git what the skeleton is.
- **The stamped product's README omits the two things its test tiers need.** Found 2026-08-25 by
  stamping one and running them. (a) The integration tier reads `<Product>__Db__ConnectionString`
  from the environment; without it `/readyz` reports `ServiceUnavailable` and one fact fails.
  (b) When the product is not beside NativeWeb, `NativeWebRoot`/`JauntyQRoot` must be **environment
  variables** — `-p:` flags satisfy `dotnet build` and then fail the e2e tier, because its fixture
  shells out to its own `dotnet publish` and command-line properties do not cross process
  boundaries. Both are now in `docs/00-quick-start/getting-started.md` Path A;
  `skeleton/README.md` should say them too.

- **`apps/web`'s only launch profile is HTTP, and the app is HTTPS-only in practice.** Found while
  verifying spec 010's getting-started commands, 2026-08-25. `dotnet run --project apps/web` serves
  `/healthz` 200 and every page carrying a form 500, with
  `InvalidOperationException: AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current
  request is not an SSL request`. The 200 on `/healthz` makes it read as one broken page rather
  than a wrong scheme. `--urls https://localhost:5444` fixes it and the README now says so, but the
  profile in `apps/web/Properties/launchSettings.json` should probably just be HTTPS. One-line fix,
  deliberately not taken inside spec 010's scope.

Small, clear, and could be picked up without a design decision.

- **`NativeWeb.slnx` cannot be built from a `.worktrees/<branch>` checkout.** Lines 31–32 carry
  `../jauntyq/src/...` as literal solution-relative paths, which resolve to `extrode-legacy-org.com/jauntyq`
  from the main checkout and to `NativeWeb/.worktrees/jauntyq` from a worktree, where nothing
  exists. Restore fails as MSB3202 before a line of the branch's own code is compiled.
  **Per-project builds work** (`dotnet build tests/<project>`) and that is how spec 009 was built
  and tested; only `dotnet build NativeWeb.slnx` is affected.

  Not fixed with 009, because no single relative path is correct from both locations — the fix is
  a decision about how the solution references JauntyQ at all. Two candidates: reference JauntyQ
  as a package the way justsmtp already does (it pins `v0.3.0` and dropped its checkout on
  2026-08-18), or drop the two JauntyQ projects from the solution and let the ProjectReferences in
  `libs/nativeweb.data` pull them in. The first is the direction the org is already going.

  The `Directory.Build.props` half of this **was** fixed in 009: `JauntyQRoot` now probes two
  depths, so a worktree resolves it with no `-p:` flag.

- **`docs/specs/008-epass-signin-library/008-spec.md` still says `Status: draft`** — and after the
  review, **it should**. Reviewed 2026-08-25, `docs/008-spec-status`; the R-by-R table and the
  evidence are in the spec's new "Acceptance review" section. Twelve of fifteen are met and
  evidenced. Three cannot be flipped by anyone but the user, because in each the shipped code took
  a deliberate decision the criterion does not describe:
  - **R1** "under 10 lines" vs justsmtp's real 32-line wiring block — 5 lines of library contract
    plus 27 of hand-rolled `IConfiguration` reading, because `AddEpassSignIn` has no
    `IConfigurationSection` parameter.
  - **R11** "None acquires a library-supplied default" vs `CookieName`, which has one, and a good
    one.
  - **R12** "no behavioural change" vs two: the `v1`→`v2` ticket format signs every justsmtp user
    out once at cutover, and the "check your email" page no longer shows the address.

  R12's other half **is** closed: `eb06a3a` in justsmtp.com deleted eight of the nine `Auth/`
  files, rewrote the ninth as its `ILoginFlowStore`, and is merged into that repo's `dev`.
  Four T18 design calls from 2026-08-19 are also still unanswered in `~/.claude/away-questions.md`
  — a public `BeginLoginAsync`, `OnSignedIn` → `WithResolver`, splitting `LoginPagePath` out of
  `LoginPath`, and rejecting a non-loopback `http://` BaseUrl. None blocks the status flip; all
  four are cheaper now than after a second consumer.

- ~~`skeleton/.github/workflows/ci.yml` pins `ilammy/msvc-dev-cmd@v1`, a mutable tag.~~ **Done
  2026-08-17**, `chore/skeleton-action-pin`: pinned to
  `0b201ec74fa43914dc39ae48a89fd1d8cb592756`, resolved with
  `gh api repos/ilammy/msvc-dev-cmd/git/ref/tags/v1` rather than copied on faith, and it matches
  the SHA this repo already pinned. `ActionPinTests` now guards **both** workflows: every
  non-`actions/*` `uses:` must be a 40-hex SHA. `actions/*` stays on tags deliberately -- GitHub
  publishes it from the trust boundary already running the workflow, so a SHA buys nothing there.
  The test has two guards against passing vacuously: one asserting the scan finds both
  repositories' workflows, one asserting at least one third-party action is actually under test.

- ~~**Two `migrate.sh` runs at once will double-apply a migration, silently.**~~ — **done
  2026-08-18**, `fix/migrate-run-lock`. A coprocess psql holds a session-level
  `pg_advisory_lock` for the whole run, taken before the first `is_tracked` read and released
  by an EXIT trap; a second runner prints `wait  another migrate.sh is running against <db>`
  and queues. Verified for real by `db/test-run-lock.sh` (7 checks, ~1 min against an
  already-migrated database), with three structural facts in `MigrateRunnerTests` so deleting
  the lock also fails the normal suite. Two things measured on the way, both recorded in the
  script: psql's stdout is block-buffered through a pipe so the readiness marker has to come
  back on stderr via `RAISE WARNING`, and psql on Windows writes CRLF so the marker read has to
  strip the CR.

- ~~**Migrations now hold locks for a whole file — check the long ones before the cutover.**~~ —
  **reviewed 2026-08-18**, `docs/02-reference/migration-lock-review.md`. Nothing to change today;
  the review
  is the deliverable. `0021_realm_scoping` is the one that would hurt: for six tables it does
  ADD COLUMN → whole-table UPDATE → SET NOT NULL → ADD FK → CREATE INDEX, so wrapped it holds
  ACCESS EXCLUSIVE on every table a sign-in touches at once. `0013_cascades_and_fk_indexes` is
  second. Every CONCURRENTLY migration already carries `@no-transaction` and is unaffected. The
  doc lists what to do if migrations are ever pointed at a live database rather than a
  provisioning one.

- **The JauntyQ source generator drops its whole output on incremental rebuild, intermittently.**
  Hit twice on 2026-08-17, both times in `libs/nativeweb.auth`: the entire `JauntyQ.Generated`
  namespace vanished, so every file in the project failed CS0246 — including files the edit had
  not touched. A clean rebuild fixed it both times, and nothing was wrong with the source.
  Dangerous because of what it looks like: the second occurrence buried a genuine compile error
  of mine under 40 spurious ones, and the instinct is to "fix" code that was never broken. If
  the whole namespace disappears at once, rebuild clean before believing the errors.
  Not diagnosed. Reproducing it means catching the flake with binlogs on, which nothing here
  does routinely; the generator lives in the JauntyQ checkout, so the investigation is over
  there rather than in this repo. Recorded 2026-08-17.

- **JauntyQ package skew: watch for it recurring.** Closed for now — JauntyQ 0.2.0 published
  2026-08-17 and the pins here moved to match, verified by building the solution in package mode
  against the 0.2.0 nupkgs (0 warnings, 0 errors) and reproducing the old `JNT8004 No index
  covers ...` failures against 0.1.0 to prove the probe reaches the generator.

  Worth keeping in mind because **nothing in the build can detect this class of skew.** The
  packaged generator is a compile-time source generator, so its diagnostics fire in *this* repo,
  not in JauntyQ's suite — a green JauntyQ build says nothing about whether consumers compile.
  0.1.0 predated migration-parser index-DDL support (JauntyQ's own `CHANGELOG.md` recorded it as
  a known limitation), and since NativeWeb declares its whole schema in migration DDL, the
  packaged generator saw every index as absent. The local checkout was 1079 commits past the tag
  and still stamped `<Version>0.1.0</Version>`, identical to the published package, so no version
  string distinguished them.

  If package-mode CI ever fails on a diagnostic that a local checkout build does not reproduce,
  suspect skew before suspecting the code, and check how far the checkout has drifted past the
  pinned tag (`git describe`). Do **not** suppress `JNT8004` to get a green badge: it guards
  security-sensitive lookup paths, and suppressing it hides the skew instead of closing it.

- ~~IdP: `Redirects.To` answers a no-JS POST with 302 where 303 is correct~~ — **done
  2026-08-18**, `fix/redirect-see-other`, on the user's call. A POST now gets 303, a GET keeps
  302, and the pair is asserted deliberately in `RedirectStatusTests` so a later blanket edit to
  either shows up. Only two existing assertions had to change, both of which were asserting the
  status of a no-JS POST. Nothing observable changes in a browser; the reason to say it is that
  the OAuth surface is followed by native and machine clients whose stacks are less uniform
  about the 302 convention.

- ~~`AppProcessFixture.DeleteCert` catches only `IOException` … and `_certPath` is never
  nulled~~ — **fixed 2026-08-16** in `fix/v9-fable-findings`; it catches every exception and
  clears `_certPath` either way.

- ~~`RunAsync` in `AppProcessFixture` puts no overall timeout on the inner `dotnet publish`~~ —
  **done 2026-08-17**, `fix/idp-correctness-backlog`. 15-minute ceiling, killed with
  `entireProcessTree: true` so the compiler and linker it spawned do not keep holding the output
  directory. Output now drains through the `OutputDataReceived` events into a `BoundedLogTail`
  rather than two `ReadToEndAsync` calls, so a timeout can report the last 200 lines instead of
  nothing. `ProcessRunTests` covers all three exits.
- ~~No configuration reference exists~~ — **already done; entry was stale, checked 2026-08-17.**
  `docs/02-reference/configuration.md` (201 lines) documents all 32 `NativeWeb:` keys plus
  `ConnectionStrings:NativeWeb`, and gives `ForwardedHeaders:KnownProxies` its own section
  precisely because its default collapses rate limiting into one shared partition. It is
  self-guarding: `tests/nativeweb.web.tests.unit/ConfigurationReferenceTests.cs` asserts in *both*
  directions — every key the source reads is listed, and every listed key is one something reads
  — with a third test that fails if either scan finds nothing, so the pair cannot pass by
  comparing two empty sets. `NativeWeb:Smtp:*` comes from `SmtpOptions` by reflection rather than
  from source literals, since the section is bound and its sub-keys appear as literals nowhere.
  The only apparent gap, `NativeWeb:EpassLegacyMode`, is not a real key: it is a negative fixture
  in `StartupConfigGuardTests` proving the legacy-key guard matches `Epass` as a whole segment
  only, and documenting it would be wrong.

## Later

Real, but needs a decision, a dependency, or more information first. Say which.

- **Strong-name the sign-in assemblies, or accept that `InternalsVisibleTo` is a simple-name
  grant.** Spec 009 R3 says the bound on ticket minting "is the compiler's". Against an accident
  it is; against someone who deliberately names an assembly `nativeweb.signin.epass.testing` it is
  not, because neither assembly is strong-named. **Needs a decision** — strong-naming is repo-wide,
  affects every package this repo ships, and is not something 009 should have taken on its own.
  Raised by 009's review, 2026-08-25.
- **`OnSignedIn` has the same last-wins defect `OnAddressSubmitted` just had.** A second call
  appends a second `SignInResolver` and `GetService` returns it, so the first is silently
  unreachable — no throw at registration, at container build, or in `Validate()`. Fixed for the
  policy under 009 because that seam is 009's; **left for the resolver because it is 008 behaviour
  a consumer may already depend on**, and changing it is a decision rather than a fix. One line in
  `EpassSignInBuilder.OnSignedIn` when taken. Raised by 009's review, 2026-08-25.
- **`ClearRejected()` sets no `no-store`.** `SessionAuth.Clear` now does, for both sign-out paths,
  but the authenticate path writes its own cookie deletion when it rejects a cookie and does not.
  Lower stakes — that response is whatever the consumer's page was — but it is the same drift.
  Noted rather than fixed: out of 009's scope. Raised by 009's review, 2026-08-25.

- ~~**The three `uses:` actions in `ci.yml` still target the Node 20 runtime, which GitHub has
  deprecated.**~~ **Done 2026-08-20**, branches `chore/bump-actions-node24`,
  `chore/drop-msvc-dev-cmd`, `chore/drop-setup-dotnet`. Two of the three were deleted rather than
  bumped, which is why the entry's "blocked on upstream releases" premise never had to hold:
  `msvc-dev-cmd` set up a toolchain the ILCompiler locates itself, and `setup-dotnet` installed an
  SDK both hosted images already ship. `actions/checkout@v7` is the only `uses:` step left in
  either workflow. The SDK requirement now lives in `global.json`. Full reasoning and the
  measurements in `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.

- ~~**Antiforgery is validated before `HasFormContentType` at all 23 POST sites — decide, don't
  drift.**~~ **Decided and done 2026-08-17** (user: "swap to 415 first"), branch
  `fix/415-before-antiforgery`. The count was 26 validating handlers, not 23: 17 carried the
  400/415 pair and were swapped, six read the form through a tolerant ternary with no 415, and
  three accept a request that may or may not be a form (WebAuthn enroll/verify, sign-out) and
  had to keep accepting both. The "lets a caller learn whether its content type would have been
  accepted" argument was checked and does not hold: the 415 decision reads only the caller's own
  `Content-Type`, never server or session state, so it is the same answer for everyone. Guarded
  by a source-order sweep (`ContentTypeGuardOrderingTests`) plus HTTP-level facts
  (`ContentTypeGuardTests`).

- ~~**The six tolerant-ternary handlers still answer a misleading 400 for a JSON body.**~~
  **Decided and done 2026-08-18** (user: "extend the 415 sweep to the six handlers"), branch
  `fix/415-tolerant-ternary-handlers`. `AdminEndpoints.PostApplicationDisable` /
  `PostApplicationRotateSecret` and `SettingsEndpoints.PostTwoFactorDisable` /
  `PostRecoveryCodesRegenerate` / `PostWebAuthnRemove` / `PostSessionsRevokeOthers` each got the
  same guard ahead of `ValidateRequestAsync`, and the ternary they read the form through became
  a plain `ReadFormAsync` — there is no longer a path where the form is absent. 17 guarded
  handlers became 23, three remain deliberately unguarded (WebAuthn enroll/verify, sign-out).
  Behaviour proved by `AuthenticatedContentTypeGuardTests`, which needs a session for all six
  and an operator for two; the source-order sweep's 415 floor moved 15 → 20 so the six cannot
  regress silently.

- **A session key that cannot be a raw `Guid`.** Spec 007 routes every session read, write and
  delete through `SessionToken`, but `Guid.Parse(token.CookieValue)` still recovers the plaintext
  in one hop, so a new write path could bypass the hash and would compile. Making that
  impossible means the data layer taking its own key type rather than `Guid` — a JauntyQ-wide
  change, hence a decision, not a fix. Recorded 2026-08-16, from the spec 007 review; the spec's
  R4 says the same rather than claiming to be met.

- ~~**`migrate.sh` applies and records in separate connections.**~~ **Done 2026-08-17,
  `fix/migrate-record-in-transaction`.** `apply_and_record` now pipes the migration and its
  `schema_migrations` row into one `psql --single-transaction`, so either both land or neither
  does. Files that cannot be wrapped are detected by content rather than listed — `CONCURRENTLY`
  (six migrations; Postgres rejects it inside a transaction block) and an explicit `BEGIN` (0054
  only) — so a new migration of either kind needs no edit to the runner.

  **The consequence was worse than this entry claimed.** Measured against scratch databases with
  a migration that creates a table then calls a missing function: the old runner committed the
  CREATE TABLE, recorded nothing, and then failed on `relation already exists` on *every*
  subsequent run. Not "re-applies it" — a database no re-run can fix without someone editing
  `schema_migrations` by hand. The new runner rolls the whole thing back and fails the next time
  on the real error, so fixing the SQL is the whole recovery. Original note follows.

  `app_psql -f "$f"` then
  `record "$version"` (`db/migrate.sh:53,79-80`), so a crash between the two commits the
  migration and loses the tracking, and the next run re-applies it. Harmless for DDL, which is
  what every migration before 0054 is; not harmless for a data `UPDATE`. 0054 works around it by
  writing its own `schema_migrations` row inside its transaction. The general fix is for the
  runner to record inside the same transaction it applies in, which is a change to how it invokes
  psql. Noted 2026-08-16.

- JauntyQ multi-result-set batching is **deferred upstream** (JauntyQ `80af756`, 2026-08-03; the
  ask is recorded in that repo's `work/todo.md` under "Later"). That was the whole point of
  `docs/jauntyq/2026-08-02-001-result-set-features.md`: round trips, not query cost. What did land
  is the window-function polish — `COUNT(...) OVER (...)` now infers `bigint NOT NULL`, so
  `-- @type` is no longer needed for the paginate-with-total shape, and JNT1008 makes a
  two-statement file an error instead of a silent mis-parse. All three consumers build 0/0 against
  it (verified 2026-08-03); no existing query was mis-parsing and none uses `OVER(` yet.
  **Do not re-propose the perf plan's item 4 (fold count into the page query) on the old
  rationale.** Only its ergonomic objection was removed. The design objection stands — counts
  repeating their list predicate verbatim is deliberate and was reviewed 2026-08-02 — and spec
  006's output cache already took `/u/{username}` to 0.51ms/1.51ms from 7.41ms/20.56ms, so the
  anonymous traffic it would speed up no longer reaches PostgreSQL at all. The case that *is*
  still open is the authenticated list pages (a user's own bookmarks, their tag lists), which are
  never cached and still run count-then-page. Different case, needs its own measurement.
  **Not measurable here any more, checked 2026-08-17: those pages are not in this repo.** Spec 003
  extracted the bookmarking app to manzil.my, and `apps/` now holds `web` alone — no bookmark or
  tag endpoint, route or query file survives, and the IdP has no paged list page at all. So the
  item is misfiled rather than open: it belongs to manzil.my's backlog, against manzil.my's schema
  and traffic, and any measurement taken here would be of code that no longer exists. Carry it
  across before acting on it; do not re-open it as NativeWeb work.
- CSRF (user, 2026-07-30): the framework antiforgery is kept for its BREACH-masked token, but the
  user dislikes how the `<meta name="request-verification-token">` + hidden-input plumbing looks.
  Candidate: a Rails-style per-session token behind a small helper so the markup reads cleaner —
  needs a decision on trading away per-request masking.
- Render engine deferred review findings (V2 review, 2026-07-31; the fixable ones were fixed
  in-branch, these need design work):
  - Layout-as-partial params: `EmitPartialCall` binds `target.Params` rather than the composed
    parameter list, so a `@layout` page called as a partial gets the wrong signature
    (pre-existing, found in the spec 004 B1 round-2 review, 2026-08-01; no current template
    hits it).
  - URL-context safety: `href="{{Url}}"` with a `javascript:` value passes both encoder paths
    untouched (same gap as Razor). Full fix is HTML-context-aware generation; interim is the
    documented convention that URL values from user data get scheme-validated in C#.
  - ~~Unquoted-attribute interpolation (`<a href={{Url}}>`) is injectable via space/`=`; the
    generator could detect-and-reject interpolation into unquoted attributes.~~ Done
    2026-08-17 (`feat/render-diagnostics-locations`) as NWT016, which covers the
    attribute-name position (`<div {{X}}>`) too — the same escape by a shorter route.
  - ~~NWT diagnostics all report at `Location.None`; parser would need to carry line/col offsets
    to point at the token.~~ Done 2026-08-17. Parse-stage findings point at the token;
    post-parse ones (layout not found, class collision) point at the file, which is still a
    path an editor can open.
  - ~~`@param` type text goes into the generated signature unvalidated (typo = raw C# error, not
    NWT003).~~ Done 2026-08-17 — a plausibility check, not a type parser: it catches a type
    with a space in it (the one mistake `@param`'s last-space split creates) and leaves a
    well-formed name that does not exist to the C# compiler, which words it better.
  - No reference page lists the NWT diagnostics for template authors; each descriptor's message
    is the only documentation, and there is now no one place to see the sixteen of them.
  - No escape hatch for a literal `{{` in template text (would break Vue-style inline syntax if
    ever needed; Alpine/htmx don't use it).
  - Diagnostics tests discard the output compilation, so a template emitting invalid C# only
    fails via the test project's own build, not a targeted assert; no incremental-caching
    step-reuse test either.
- i18n deferred review findings (V3 review, 2026-07-31; the mechanical ones were fixed
  in-branch, these need design work or a future trigger):
  - Locale negotiation runs before `UseAuthentication`, so the (deliberately unwired)
    `UserPreference` hook would always see an unauthenticated context — move the middleware
    after auth when the hook lands with IdP page localization (comment in Program.cs marks it).
  - One NWT014/NWT015 suppresses the whole generated `Locales` class, so every `Locales.*`
    reference cascades CS errors that bury the real diagnostic; a best-effort/empty-catalog
    emission would keep it legible. Same shape for missing en.json → every `{{t:}}` fires NWT013.
  - Locale ids are two-subtag only: `zh-hans-cn` would get wrong `Lang` casing and inherit from
    `zh`, never `zh-hans` (documented at `LocaleCatalog.ToHtmlLang`); extend if a script-tagged
    locale ever ships.
  - Sanitize boundary is inconsistent in mail: renderer args are control-char-stripped but
    catalog-sourced subject/text are not (MimeKit encodes headers, so not injectable), and
    `Sanitize(linkUrl)` mangles a control-char URL into a different URL rather than rejecting it.
  - Templates and locales share one `RegisterSourceOutput`, so editing one .json re-emits every
    template and vice versa (pre-existing shape; incremental win currently nil).
  - `users.locale` is unbounded free text by design (unknown degrades to English); whatever
    preference-setting endpoint lands later must validate against `Locales.All`.
- Strongly typed IDs (deferred from spec 005 scoping, 2026-08-02): `readonly record struct`
  value objects (`UserId`, `BookmarkId`, …) through both apps. Zero runtime cost, but gated on
  JauntyQ converter support for the wrapper types, and low urgency here: ownership-scoped SQL
  already turns a swapped id into a 404, so today's failure mode is a silent no-op, not a leak.
- Analyzer risk pair (from V1 review, 2026-07-31): `TreatWarningsAsErrors` with unpinned
  `AnalysisLevel=latest-recommended` means an SDK bump can hard-break the build on new rules;
  consider pinning the level or adding a `WarningsNotAsErrors` escape hatch before the cutover.
- ~~.editorconfig naming rules cover interfaces/types/members only — private fields, constants,
  locals and parameters are unenforced (V1 review note).~~ **Done 2026-08-18**,
  `chore/editorconfig-naming-rules`: rules added for const fields and static readonly fields
  (PascalCase), private fields (`_camelCase`), locals and parameters (camelCase). The measurement
  the user asked for first: **one violation across the whole solution** — a local const named
  `Yaml` in `ActionPinTests` — so the rules went in enforcing rather than advisory. The real find
  was that none of the naming rules ran at all: IDE1006 has to be enabled by
  `dotnet_diagnostic.IDE1006.severity`, and without it a rule's own severity is decorative, which
  is why `interface_should_be_begins_with_i` had been silent since the file was written.
  `EditorConfigNamingTests` guards both that line and dangling rule references. Not measured:
  `skeleton/`, which is not in `NativeWeb.slnx` and cannot build unstamped.

## Dropped

Not doing, with the reason. Kept so the same idea does not get re-raised every quarter.

- BEM rename (`feature__title` -> `feature-title`/`feature_title`): costed 2026-08-02 (~40
  names / 205 occurrences / 23 files + three-repo sweep); user decided 2026-08-02 to keep BEM.

- Unused vendored `webauthn-ceremony.js` in manzil.my: removed 2026-08-03 (`1d795e1`). Not
  needed by the eventual epass-backed auth either — WebAuthn credentials are RP-scoped, so the
  ceremony runs on the IdP's own pages and a delegating relying party never calls
  `navigator.credentials` itself.

- IdP passphrase reset showing a blank page with scripting off (H5 nit, 2026-07-31): already
  fixed by `0ba2d04`, the spec 004 no-JS retrofit — `Redirects.To` returns a real redirect when
  `HX-Request` is absent. Nothing asserted it, because every test in the file sent `HX-Request`;
  `ResetStart_Then_Verify_WithoutScripting_RedirectsAtEveryStep` now holds both steps in place.

- `InternalsVisibleTo` for the DevMarks unit tests: done in manzil.my 2026-08-03 (`e299972`).
  The grant already existed in `apps/manzil/AssemblyInfo.cs`; only the reflection and the
  comments claiming otherwise were left, and both are gone.

- `devmarks_dev` migration drift on 0006: not a code defect. `manzil_dev` has 0001-0006 all
  recorded and `search_match` present, and nothing in either repo references `devmarks_dev` any
  more — it is the pre-rename database, superseded by the extraction. Making 0006
  `CREATE OR REPLACE` was rejected: 0002-0005 are equally non-idempotent by design, and the
  runner's `schema_migrations` tracking is what makes re-runs safe, so a lone idempotent
  migration would only mute the divergence signal the others keep. Dropping the stale database
  is a cleanup step instead — `scripts/cleanup/devmarks-app.ps1 --execute --drop-database`.

## Deferred from V4 review (2026-07-31)
- Per-field errors: endpoints surface validation failures as page-level banners (parity with the
  old markup); form-field.html's Error/aria-invalid path is ready but every caller passes
  Error="". Thread real per-field errors when the UX is revisited.
- Fragment-assembled sentences in bookmarks/index.html empty states and bookmark-row's
  "Saved {date}" fix English word order across locales; replace with parameterized keys +
  precomputed C# strings.
- ~~Results.Redirect on auth-expired paths ignores HX-Request across all endpoint files~~ —
  **done 2026-08-17**, `fix/idp-correctness-backlog`. One site was live: `PostVerify` in
  `TwoFactorEndpoints` returned a bare 302 when the TOTP challenge cookie had expired, and that
  form opts into htmx, so the sign-in document was swapped into the 2FA card on the most ordinary
  failure the endpoint has. The other eight were latent — every `data-nw-*` element in the app is
  a `method="post"` form, so no GET is htmx-driven today, but `boot.js` derives `hx-get` from any
  anchor carrying the attribute, so one added link would make them live. All now route through
  `Redirects.To`. The note's "RedirectResult's comment says 303 but emits 302" was stale: there is
  no `RedirectResult`, and `Redirects.To`'s comment says 302 and emits 302. The separate 302-vs-303
  question above is still deliberately unchanged.
- ~~ValidateAsync (4 files) checks antiforgery before HasFormContentType, so a non-form POST
  gets 400 not the intended 415 (pre-existing).~~ Fixed 2026-08-17 — see the decided entry
  above. It was seven files and 17 handlers, not four.
- Anonymous pages (home, /u/*) mint an antiforgery cookie via Chrome.For/GetAndStoreTokens,
  making the crawlable pages uncacheable (pre-existing convention) — revisit in V7 perf work.
- Home route sits outside the "public" rate-limit group (pre-existing) — revisit in V5/V6.
- "1 tags" on /tags (tags.count has no singular variant) and count_saved shown for
  tag-filtered lists (both parity with old markup) — wording pass later.
- Nav shows Bookmarks/Tags/Import to signed-out visitors and never marks Settings active
  (both parity with the old layout) — UX pass later.
- ~~GenericFailureMessage const duplicates error.signin_generic for the integration tests;
  point the tests at Locales.Get("en") and drop the const.~~ Not this repo — checked
  2026-08-17 and neither `GenericFailureMessage` nor `error.signin_generic` exists anywhere
  here. Like the rest of this V4 block it belongs to manzil.my, which is now its own
  repository; carry it there or drop it.
- Single-page lists render a pagination landmark with only position text (parity).
- tools/convert-*.py are the R42 conversion scripts, kept as reference for the IdP template
  migration spec; delete with that spec's close-out.
- ~~(V6 review, fable) apps/web stores the session cookie value as the bff_store row's
  plaintext UUID PK, so any DB read yields live bearer tokens; devmarks stores only the
  SHA-256 of its token. Hash the IdP's at rest the same way~~ — **done 2026-08-16**, spec 007
  (`docs/specs/007-session-token-at-rest/`). Rows are keyed by `SessionToken.StorageId`;
  migration 0054 converted the existing ones in place, so no session was invalidated.
- (V6 review, fable) devmarks has no DataProtection key persistence, so every restart
  invalidates outstanding antiforgery tokens; mirror web's PersistKeysToFileSystem block.
- (V6 review, fable) devmarks loads user-secrets unconditionally rather than
  Development-only (web gates it); align.
- ~~(V8 review, opus) robots.txt/sitemap.xml hardcode the production hosts (epass.io,
  manzil.my) and are served verbatim in every environment, so staging advertises a
  production Sitemap URL~~ — **closed 2026-08-27, spec 011 T1/T2.** Both static files are
  deleted; `apps/web/SiteMetadataEndpoints.cs` renders them from
  `NativeWeb:CanonicalBaseUrl`, and unset falls back to the request's own scheme and host, so
  staging advertises staging. The skeleton's copies were never a hardcode — they carry the
  `__product__.example` placeholder the stamper rewrites.
- (V8 review, opus) apps/web `/` still serves the temporary "Phase 0 foundation is up"
  landing; ship the real landing before submitting the sitemap anywhere. (The epass.io half of
  this entry is closed — see above; only the landing page remains.)
- (V8) The plan's "`/` → landing where the product wants it" was deliberately deferred:
  both apps keep a dynamic session-aware `/`; manzil.my decides its own static landing
  during the V10 extraction.
- ~~Skeleton e2e process fixture runs its inner `dotnet publish` without the UsePackages flags,
  so a stamped product's CI e2e step stays checkout-mode even once a package feed exists —
  fold into the feed-gap fix (spec 003 R6).~~ **Stale, checked 2026-08-17.** The fixture's
  command line indeed omits them (`__PRODUCT__ProcessFixture.cs:35`), but it does not need
  them: `skeleton/.github/workflows/ci.yml` sets `NativeWebUsePackages` and `JauntyQUsePackages`
  as *env vars* on the e2e step, MSBuild reads bare env vars as properties, and env is
  inherited by the child publish. Same wiring NativeWeb's own e2e step got on 2026-08-03.
  (Superseded 2026-08-17: those two env vars are gone. The mechanism is unchanged and is now
  what the whole workflow rests on — `NativeWebRoot`/`JauntyQRoot` at workflow level.)

- ~~**The skeleton has the gap NativeWeb just closed, one level up, and it needs a decision.**
  `libs/*` here are all `IsPackable` with `NativeWeb.*` package ids, and the skeleton builds
  against them with `-p:NativeWebUsePackages=true` — but this repo has no `publish.yml`
  (only `ci.yml`), so nothing has ever pushed a `NativeWeb.*` package anywhere, and the
  skeleton has no `nuget.config` to find one with. It is exactly the shape fixed on
  2026-08-17 for JauntyQ, and it is why the skeleton's suites have never run. Not a fix:
  it needs a call on whether NativeWeb versions and releases itself independently
  (jauntyq's `publish.yml` on `v*` tags is the pattern to copy), which is a release-process
  decision rather than a wiring one.~~ **Done 2026-08-17**, `chore/skeleton-local-mode-ci`,
  and it turned out not to need the release decision at all: CI now builds in **checkout
  mode**, checking `extrode-legacy-org/NativeWeb` and `extrode-legacy-org/jauntyq` out as SIBLINGS of the product
  (which moves into `product/`) and pointing `NativeWebRoot` / `JauntyQRoot` at them as
  workflow-level *environment* variables (one
  definition reaches `dotnet list package`, which takes no `-p:`, and the e2e fixture's child
  publish). The skeleton also now ships the missing `nuget.config`. The publish-vs-not
  decision is deferred, not answered — package mode still exists, still has no feed, and is
  simply no longer what CI depends on. Requires a `FRAMEWORK_CHECKOUT_TOKEN` secret on each
  stamped product repo, since both framework repos are private.
- **Neither framework checkout in `skeleton/.github/workflows/ci.yml` is pinned to a tag** —
  both use `ref: dev`, because neither repo publishes tags (NativeWeb has no `main` at all).
  Until they do, a push to `dev` in either framework repo can turn every stamped product's CI
  red with nothing having changed in that product. Pin to tags when the first release lands.
  Raised 2026-08-17.
- ~~`.gitignore` does not ignore `tmp/`~~ — done 2026-08-17, `feat/enable-telemetry`. Escalated
  from warning to error the moment that branch put a throwaway NativeAOT publish there: 159
  scratch files exposed to `git add -A` on the commit about to be made.
- ~~V9 review findings that are pre-existing on dev~~ — done 2026-08-03,
  `chore/v9-pre-existing-findings`. (a) root `Directory.Build.props` dead `JauntyQUsePackages`
  auto-default deleted (it could never fire: the probe above it always assigns `JauntyQRoot`)
  and `EnsureJauntyQRoot` now probes the generator csproj, so an empty checkout dir fails at the
  gate rather than as CS0246. (b) `AppProcessFixture` drains stdout/stderr and keeps a 200-line
  tail, which the startup `TimeoutException` now reports. (c) `ilammy/msvc-dev-cmd` pinned to
  `0b201ec74fa43914dc39ae48a89fd1d8cb592756`, and the clang/zlib1g-dev install landed after all
  — it is not the (extracted) devmarks job that needs it but the surviving `test` job's e2e
  step, whose fixture runs an inner NativeAOT publish on the Linux runner. That same step now
  sets `JauntyQUsePackages` as an env var, because `$DOTNET_BUILD_ARGS` reaches only the outer
  `dotnet test` and MSBuild reads bare env vars as properties. Both copied from
  `skeleton/.github/workflows/ci.yml`, and both were **unverifiable until the CI package-feed
  gap was resolved** — that gap closed 2026-08-17 (`fix/ci-package-feed`), so the next push
  is the first run that can actually exercise them. Still unverified until one goes green,
  and it needs the org package-access grant to be in place first. (d) `/readyz` is bounded by `ReadinessCache` (1s, `NativeWeb:Readiness:CacheSeconds`)
  rather than by a rate limit: partitioning is per-IP, and until `ForwardedHeaders:KnownProxies`
  is configured every client shares the proxy's partition *including the load balancer's own
  probe*, so a limiter would have let anyone spend the budget and make the next real probe 429
  — a remote off-switch for a healthy instance. A test pins that neither probe is rate limited.
  **Still open, in manzil.my:** its `/` endpoint has no rate-limit policy and its `/readyz` is
  the same unbounded DB round trip; `ReadinessCache` is the shape to pattern-copy.
- ~~V10 removal review: `Directory.Packages.props` pins `Microsoft.AspNetCore.OpenApi` 10.0.7 and
  `Microsoft.OpenApi` 2.7.5 with no remaining consumer in any csproj (the transitive-override
  comment is now false); removing pins counts as dependency removal, so left for a confirmed
  cleanup. Also trivial: `check-publish-budgets.ps1` `-Port` default 5999 is unreachable (sole
  caller passes 5998).~~ Both done 2026-08-17 (`chore/test-and-skeleton-hygiene`), the pin
  removal on explicit approval. The port default is 5998 now, matching ci.yml, and
  `PublishBudgetScriptTests` fails if the two ever drift again.
- Dev-loop bug (found 2026-08-01, applies to any app using MapStaticAssets + fingerprinted CSS,
  so manzil.my and apps/web alike): under `dotnet run` in Development,
  `StaticAssetDevelopmentRuntimeHandler` resolves the precompressed gzip variant to
  `wwwroot/<name>.css.gz` (publish-only file), throws FileNotFoundException, and the browser
  request path swallows it into an empty 200 with no Content-Type — `nosniff` then blocks every
  stylesheet, so `dotnet run` pages render unstyled. Direct `.gz` route 500s with the real
  exception. Workaround: run the published Release output (serves correctly). Investigate
  whether it is a known .NET 10 issue or triggered by our pipeline ordering.
- spec 004 review note: consider dropping the `antiforgeryField = ""` / `botTraps = ""` defaults on Render*Form/Fragment helpers so the compiler catches an omitted token field (SettingsEndpoints.cs:119 regression class); ~140 unit-test call sites would need updating. Also pre-existing: enumeration-defense StripVaryingTokens blanks every content="..." attribute, wider than the token meta.
- integration: SettingsEndpointsTests fail 11/13 when run with --filter in isolation (TrapForm static canary fallback GETs /auth/signin with an authenticated client); full-suite runs pass. Pre-existing order dependence, verified at B4 HEAD 0ba2d04.
- formatter (2026-08-02 review findings, non-blocking): (a) the AmbiguousLineStart guard only
  accepts a literal "\n" next — a CRLF checkout (no .gitattributes LF force on *.html) bails
  every banner-guard template silently; accept "\r\n" too. (b) depth sets keep entries from
  callers that later bail (CascadeBails never prunes them), which is why MaxDepthVariants had to
  go 4->8 and up to ~5 dead RenderAtD overloads per hot partial ship; recomputing depth sets
  over surviving callers after each cascade would make 4 sufficient again. (c)
  bookmarks/index.html and public/profile.html have never formatted: a {{for}} body ending in a
  partial splice doesn't return to its start state; supporting that shape would format the two
  remaining manzil pages.
- ~~`tests/nativeweb.web.tests.e2e/AppProcessFixture.cs` never drains the redirected stdout/stderr~~ — done 2026-08-03, `chore/v9-pre-existing-findings`, same shape as manzil's spec 006 T9 fix. Worth carrying back to the skeleton and to manzil: both discard the drained output (`static (_, _) => { }` / no tail in the timeout message), so a startup failure there still reports nothing but the elapsed 60 seconds.
- ~~CSP hash generator, remaining findings from the 2026-08-03 review~~ — **all three done**.
  (a) comment / raw-text / attribute-value awareness, and (c) the app-level lockstep that
  recomputes from the served response, landed 2026-08-16 in `fix/csp-hash-scan-context`
  (`docs/plans/2026-08-16-003-csp-hash-scan-context.md`). (b) the `IsExecutableScript` MIME
  allowlist landed 2026-08-17 in `fix/loose-ends`: it now carries the HTML standard's whole
  JavaScript MIME type set, so `text/jscript`, `application/x-javascript`, `text/livescript`,
  the `text/javascript1.x` versioned spellings and the `x-` variants are hashed rather than
  silently blocked. `templates/csp/legacytypes.html` pins it, with a `text/template` control in
  the same fixture that must stay unhashed.
- ~~Skeleton templates are CRLF in the working tree although `.gitattributes:15` says
  `*.html text eol=lf`; the blobs are LF, so the two disagree. `git add --renormalize` would fix
  it but touches every template, so it is its own change.~~ **Stale — measured 2026-08-24, there
  is nothing to fix.** `git ls-files --eol` reports `i/lf w/lf attr/text eol=lf` for every
  skeleton `*.html`: index and worktree agree, and both agree with the attribute. The
  `--renormalize` this entry proposed would have rewritten every template for no change. Whatever
  produced the CRLF worktree copy was undone before the entry was written or by a later checkout.
  Still true, and worth keeping: the CRLF/LF disagreement is why the skeleton once emitted
  browser-mismatched CSP hashes, and the hash generator's normalization is what makes that
  unable to recur.
- ~~The skeleton's integration and e2e suites have never been run: they only exist inside a stamped
  product, which needs its own database. That is why `SmokeTests` kept asserting a nonce in the
  home page CSP for the whole of spec 006 (fixed 2026-08-03 by inspection, still unexecuted).
  Worth a CI job that stamps, creates a throwaway DB, and runs all three layers.~~ **Done
  2026-08-25**, `feat/ci-skeleton-and-scaffold`: the `skeleton-stamp` job stamps a `Ciprobe`
  product with `tools/new-product.ps1`, provisions `ciprobe_dev`, migrates, and runs all three
  layers (47 unit / 7 integration / 2 e2e locally). The whole path was verified end to end on
  this machine before any YAML was written. Two findings came out of it:
  - ~~`tools/new-product.ps1` copies the skeleton's `bin/` and `obj/` into the stamped product, so
    a product stamped from a built checkout starts life carrying the skeleton's build output.~~
    **Fixed 2026-08-25**, `fix/new-product-excludes-build-output`: the enumeration is now
    `git ls-files --cached --others --exclude-standard`, so the stamped set is what git tracks
    plus what `skeleton/.gitignore` does not exclude — 168 files, not 714. Worse than the size:
    the old enumeration would have carried a `.env`, a `*.pfx` or `secrets/` into a new product's
    *first commit*, since `git add -A` runs right after the copy and the ignore file was the only
    thing that had ever excluded any of it. `NewProductStampTests` runs the script's own dry run
    and asserts set equality against git in both directions, so a "no build output" regression
    and a "silently stamps nothing" regression both fail.
  - `PackageFeedConfigTests.Workflow_AuthenticatesTheSourceNameNuGetConfigActuallyDeclares` had
    to widen to the union of `nuget.config` and `skeleton/nuget.config`: the stamp job
    authenticates `framework-github`, which only the skeleton's config declares.
- The same CI branch found that **CI was running none of the scaffolding tiers** — not the
  generator's unit tests, not the integration tier, not the e2e tier — a second gap beyond the
  skeleton one. All four now run in the `test` job, the e2e tier under `JauntyQUsePackages`.
- `Directory.Build.targets` disables precompressed static-asset generation in Debug as a
  workaround for a framework defect: `StaticAssetDevelopmentRuntimeHandler` resolves compressed
  variants against `wwwroot` instead of the manifest content roots, so a gzip-accepting client got
  a zero-length body. Remove the property once the SDK resolves the variant correctly. The Debug
  build no longer exercises compressed-asset serving at all, so only `dotnet publish` covers that
  path — **the Release smoke check now exists** (2026-08-18, `chore/release-compressed-asset-check`):
  `CompressedAssetTests` in the e2e tier, against the published binary the fixture launches, asserts
  a gzip-accepting client gets `Content-Encoding: gzip`, a body smaller than the identity response,
  and one that gunzips to exactly that response. What remains is removing the workaround itself.
- manzil.my needs the same `Directory.Build.targets` workaround; it has the identical defect.
- ~~A fresh **production** database has the same first-party-application gap CI hit.~~ **Closed
  2026-08-17** by `db/migrations/0055_first_party_application.sql`: every deployment now gets the
  row, guarded on "realm 1 has no applications" so existing deployments neither duplicate it nor
  gain a dead row. The secret is generated with `gen_random_uuid()` inside the migration and
  never recorded, which is safe because `ResolveFirstPartyAsync` matches on `client_id` and reads
  `app.Id` without ever consulting `secret_hash`. Measured: every migration but 0055 applied gives
  33/258 integration failures, the exact CI set; with 0055 it is 258/258.
- ~~`db/seed/ci/` and the `Apply CI seed` step in `ci.yml` are now redundant.~~ **Done
  2026-08-17**, `chore/retire-ci-seed`: the workflow step is gone, `CiSeedTests` was rewritten to
  assert ci.yml applies no seed at all (re-adding one would make CI pass while a fresh production
  database stayed broken), and the seed file itself is left to
  `scripts/cleanup/ci-seed-retirement.ps1` for the user to run.
- ~~`RealmResolver` throwing an `InvalidOperationException` for missing *configuration*.~~ **Done
  2026-08-17**, `fix/realm-resolver-consistency`. `ResolveFirstPartyAsync` now returns null and
  both call sites answer 503, the same status an unconfigured connection string or an absent
  `PairwiseSubjectDeriver` already gives, so one class of problem has one status. A boot-time
  check (`StartupConfigGuards.FailStartupIfFirstPartyApplicationMissing`) refuses to start outside
  Development when the database answers and the row is absent.

  The check is deliberately three-valued. Anything that stops it *reaching* the database --
  unconfigured, unreachable, unmigrated, wrong credentials -- is `Indeterminate` and does **not**
  fail startup: refusing to boot because Postgres is not up yet breaks an ordinary rolling deploy,
  and `/readyz` already reports that state far better than a dead process does. "Cannot tell" must
  never be reported as "wrong". Only `Missing` exits.
- ~~`skeleton/.github/workflows/ci.yml` applies migrations only, so a stamped product carries the
  same gap.~~ **Not a gap -- closed 2026-08-17.** Checked while pinning the action above: the
  skeleton has no `RealmResolver`, no first-party application and no `db/seed/` at all, so there
  is no row to seed. More to the point, migrations-only is now the *correct* posture rather than
  an oversight: NativeWeb resolved its own version of this by moving the row into migration 0055
  and retiring `db/seed/ci` entirely, so the skeleton was already doing the right thing. Copying
  a seed step across would have propagated the mistake, not the fix.
- ~~`ActionPinTests.ThirdPartyActions_ArePinnedToASha` scans workflows for the `uses:` key without
  skipping comments~~ — **done 2026-08-18**, `fix/action-pin-comment-scan`. The pattern is
  anchored to the start of a line with only whitespace and an optional `- ` sequence marker
  allowed before the key, which is what excludes `#`. Nine tests: four commented shapes that must
  yield nothing, four real step shapes, and one document carrying both. The comment reworded
  around it in `skeleton/.github/workflows/ci.yml` can now say whatever it likes.

## Security audit findings, 2026-08-19 (`review` on Opus + `glm-think` on GLM-5.3)

Two independent audits of the `apps/web` request-handling surface, run after the antiforgery
400/500 fix. Recorded, not fixed — each needs its own branch and a decision on priority. Both
audits independently derived the structural point the T1.2 spike measured: **endpoint filters run
after argument binding**, so `RequireForm()` and `RequireServices<>()` cannot guard a `[FromForm]`
bind. That is why the antiforgery fix is middleware. `RequireServices<>()`'s 503-degrade has the
same ordering flaw and is still unaddressed.

**Converged (both audits):**
- `apps/web/Auth/SignInEndpoints.cs:39` — `POST /auth/signout` is the only auth-surface form POST
  with no `.RequireRateLimiting()` and no content-type guard, reachable unauthenticated, and does
  a `bff_store` delete per request. Antiforgery tokens are free from any GET, so the token is no
  throttle.
- `apps/web/Program.cs:428-431` — `AddAntiforgery` pins only `HeaderName`. The antiforgery cookie
  keeps framework defaults: no `__Host-` prefix, `SecurePolicy` left at `SameAsRequest`. It is the
  one cookie in the app not pinned Secure, and the other half of the CSRF pair.

**Enumeration oracles** (the register flow never got the defences the reset flow engineered):
- `RegisterEndpoints.cs:173`, `:182` — "That username is already taken." for both a `users` row
  and a pending `temp_users` row: a direct, unauthenticated account-existence oracle on the same
  identifier used to sign in. Returns *before* `Passphrases.Hash`, so it is distinguishable by
  timing as well as by body. Contradicts the email defence 40 lines below at `:205-225`.
- `RegisterEndpoints.cs:238` — the genuine-registration branch awaits `codeSender.SendAsync` (SMTP)
  and `breachChecker.CheckAsync` (HIBP, up to 2s) while the already-registered branch pays only a
  dummy Argon2 hash. `PassphraseEndpoints.cs:254-264` backgrounds the send precisely to avoid this;
  register did not get the same treatment. Latency is the oracle.
- `RegisterEndpoints.cs:296` — register-verify returns a distinct "Too many attempts" message only
  when a `temp_codes` row was staged, i.e. only when the email is *not* already registered.
  `PassphraseEndpoints.cs:401-411` fixed this exact bug on purpose.

**Correctness / functionality:**
- `SecurityHeadersMiddleware.cs:57-61` vs `OAuthEndpoints.cs:404-405` — the CSP rationale claims
  consent POSTs never redirect cross-origin via a 302. False: `Redirects.To` returns a real 303 to
  the cross-origin `redirect_uri` for a request without `HX-Request` (`Redirects.cs:45-60`).
  Chromium enforces `form-action` against redirect targets, so the no-JS consent path
  `OAuthAuthorizePage.cs:14-18` deliberately supports is blocked there. Either widen the CSP to the
  registered redirect origins or replace the comment with an honest statement of the tradeoff.
- `SignInEndpoints.cs:49` — bare `Results.Redirect("/")` instead of `Redirects.To`, which is the
  exact case `Redirects.cs:14-18` calls out: htmx chases the 302 by XHR and swaps a whole document
  into a fragment slot. Latent until an anchor carries `data-nw-*`.

**Cookie and rate-limit consistency:**
- `SignInEndpoints.cs:400`, `WebAuthnEndpoints.cs:437`, `ReturnUrlAuth.cs:23` — challenge and
  return-url cookies use `Secure = !environment.IsDevelopment()`, while the session cookie
  (`SessionAuth.cs:89-96`) pins `Secure = true` unconditionally on the stated grounds that browsers
  treat `http://localhost` as a secure context. The gate buys nothing and is a way to lose the flag;
  these two carry pre-session step-up bearer tokens.
- Full list of mapped routes with no `.RequireRateLimiting()`: `Program.cs:728` (`GET /`, known),
  `:713`, `:717`, `:722`, `SignInEndpoints.cs:32`, `RegisterEndpoints.cs:37`, `:46`,
  `TwoFactorEndpoints.cs:30`, `:39`, `WebAuthnEndpoints.cs:37`, `:46`, `PassphraseEndpoints.cs:43`,
  `:50`, `:57`, `SettingsEndpoints.cs:23`, `AdminEndpoints.cs:25`, `SecurityTxtEndpoint.cs:21`,
  `:22`. `/readyz` (`:803`) is a documented exclusion. The unauthenticated page-rendering GETs each
  mint a data-protected antiforgery token per request and are the cheapest amplification surface in
  the app; `GET /auth/webauthn/verify` also issues two DB queries once a challenge cookie is present.

**Lower, recorded for completeness:**
- `OAuthEndpoints.cs:38`, `:180-187` — `GET /oauth/authorize` is a state-changing GET and the
  session cookie is `SameSite=Lax`, so it is sent on cross-site top-level navigation. Where
  silent-SSO conditions hold, a cross-site link can drive a signed-in user into a grant with no
  consent interaction. PKCE does not help; the attacker is the client. Conditional on realm config.
- `AdminEndpoints.cs:484-510` — `imageUri`, `website`, `companyWebsite` validated by length only,
  never parsed as URIs, and `imageUri` is emitted as `<img src>` on the public consent screen.
  `default-src 'self'` blocks the load today, so this is stored junk rather than XSS — one CSP
  change away from mattering.
- `Layout.cs:69` — `{{antiforgeryToken}}` interpolated raw into a `<meta content="...">`, the only
  unencoded request-derived interpolation in the Render layer. Safe because the token is base64url,
  i.e. safe by a framework charset rather than by the encoder every sibling uses.
- `PassphraseEndpoints.cs:295`, `:331` — `maskedDestination` round-trips through the client
  unvalidated and is reflected into "We sent a code to {X}" on an unauthenticated reset page.
  HTML-encoded, so content spoofing rather than XSS — on the one page whose job is telling the user
  where a takeover code went. It is server-derivable from `hashedUsername`.
- `WebAuthnEnrollPage.cs:44`, `PassphraseChangePage.cs:114`, `WebAuthnVerifyPage.cs:36` — WebAuthn
  options JSON interpolated raw into `<script type="application/json">`, carrying a user-chosen
  `username` with no charset restriction. Safe only because Fido2NetLib's `ToJson()` uses
  System.Text.Json's HTML-escaping encoder — asserted in a comment, pinned by no test.
- `CspReportEndpoint.cs:27-51` — no content-type guard and no origin check; `Sanitize` at `:59`
  neutralises CR/LF and C0/C1 so the residual is log noise only.
- `Program.cs:642-650` — `StaticPagesMiddleware` and `UseRouting` sit outside `ErrorPageMiddleware`,
  so an exception in the clean-URL rewrite or in route matching bypasses both the error shell and
  the security headers.

## Scaffolding POC follow-ups, 2026-08-19

- ~~`AntiforgeryValidationMiddleware` is duplicated: `apps/web/Middleware/` has the real one and
  `apps/sample/Program.cs` inlines a copy.~~ — **half done 2026-08-19**,
  `feat/scaffold-host-extensions`. The sample's copy is gone, replaced by
  `app.UseAntiforgeryRejection()` in `libs/nativeweb.scaffold`, composed into `UseScaffolding()`
  alongside authn/authz/antiforgery. **`apps/web` still has its own** and was deliberately not
  touched: it is a different implementation with its own tests, and moving a live IdP onto a new
  middleware is a change that wants its own branch and its own review. Do that when
  `apps/web` next needs work in that area.
- ~~`apps/sample`'s `IScaffoldStore<T>` is in-memory, which contradicts the
  JauntyQ-for-all-data-access rule.~~ — **done 2026-08-25**, `feat/scaffold-jauntyq-store`.
  `JauntyArticleStore` satisfies the same six methods with JauntyQ queries over PostgreSQL, and
  `ConnectionStrings:Sample` selects between the two: unset keeps the in-memory store, so the demo
  and the whole suite still run with no database. `Tag` stays in-memory deliberately — two models
  behind one seam with two entirely different implementations is the claim, and it is only visible
  if they actually differ. Schema lives in `db/sample/`, apart from the identity schema; see that
  README for why and for the local setup.
  Three things the work turned up, all recorded where they happened:
  - `CreateSlimBuilder` does not see `WebApplicationFactory`'s in-memory config (already known, in
    `NativeWebFactory`) — but `UseSetting` does, and unlike an env var it is per-host, which is what
    lets the in-memory tests keep running in the same process.
  - `DisableParallelization` orders a collection's tests, not its fixtures. Two fixtures publishing
    one project raced ILC; the publish is now shared via `Lazy<Task<string>>`.
  - The read-only timestamp has *two* independent defences — the generator's
    `Build(form, existing)` and `Update.sql`'s SET list — and each hides the other when only one is
    broken. Both confirmed by breaking each in turn; written up in `Update.sql`.
- Hand-written `BindAsync` is a foot-gun the generator avoids and a human would not: nothing warns
  when a `[FromForm]`-free handler loses its antiforgery metadata. An analyzer should flag an
  `IBindableFromHttpContext` type reached from a POST with no `IAntiforgeryMetadata`.
- Scaffolded pages carry no rate-limit policy. `apps/web`'s `RateLimitCoverageTests` would catch
  that in that app; the generator should attach a policy name so it is not per-app vigilance.
- `ScaffoldLayout.Page` embeds its own stylesheet rather than going through `nativeweb.render`.
  Right for a self-contained demo, wrong once a consumer wants its own chrome — `Page` is the seam
  to make overridable.
- ~~No e2e (Playwright) tier for the scaffolding. Unit + integration only.~~ — **done 2026-08-24**,
  `feat/scaffold-e2e-tier`. `tests/nativeweb.scaffold.tests.e2e` publishes `apps/sample` and drives
  the real binary. It exists for one thing the tiers below cannot reach: the generated delete form
  carries `onsubmit="return confirm('Delete this record?')"`, and an `HttpClient` has no JavaScript
  engine, so the integration tier POSTs straight past that guard and would pass identically if the
  attribute were deleted.
- **The scaffolded delete confirm is an inline event handler, so a strict CSP silently removes it.**
  Found while writing the e2e tier. `onsubmit="return confirm(…)"` needs `unsafe-inline` in
  `script-src`, or a hash — `apps/sample` sets no CSP at all, so it works there and the new tier
  passes, but a consumer with `apps/web`-grade headers gets a delete button that deletes on the
  first click with no dialog and nothing in the console to say why. The scaffolding is a library,
  so the consumer's CSP is not ours to choose: the fix is to stop emitting an inline handler.
  Needs a decision on what replaces it (a submit listener from a hashed script the library ships,
  or a confirm step as its own page).
- ~~`ilammy/msvc-dev-cmd` is the last node20 action in either workflow, and there is nothing to
  bump to.~~ — **done 2026-08-20**, `chore/drop-msvc-dev-cmd`. Deleted rather than replaced: the
  ILCompiler locates MSVC itself via its bundled `findvcvarsall.bat`, so the step was redundant,
  and it was exporting `Platform=x64` into the publish path. Both workflows are now node24
  throughout and use no third-party action at all.

## epass.io cutover readiness, probed 2026-08-24

Probed before scheduling the cutover, on the reasoning that finding unfinished work afterwards is
far more expensive than finding it now. ~~The application side is ready; the deploy side was never
started. Four blockers, none of them code in `apps/web`:~~ **"The application side is ready" was
wrong, and badly — see the capability gap below, added 2026-08-25.** ~~The four deploy blockers are
still real and still unstarted:~~ **Three of the four are addressed by spec 011
(`docs/specs/011-production-deployment/`), merged 2026-08-27 — but "addressed" means written,
not executed. Read each note below for which half survives.**

- ~~**Slice 10's deploy half does not exist.**~~ **Written, never run.** Spec 011 ships
  `scripts/deploy/` — `provision.sh`, blue/green `deploy.sh`, `rollback.sh`, encrypted offsite
  `backup.sh`/`restore.sh`, the systemd units, the Caddyfile and the nftables ruleset — plus the
  runbook at `docs/02-reference/deployment.md`. **No host has ever been provisioned from it and
  no backup has ever been restored** (011 T15, the operator's to run), so the cutover still
  cannot be scheduled against it. `MemoryDenyWriteExecute=yes` in the unit is likewise unverified
  (011 T14). Two further gaps are open by decision: `BACKUP_HEARTBEAT_URL` is unset, so R15 is
  unmet and a backup that stops running is invisible; and one host with nightly dumps accepts up
  to 24 hours of data loss and no failover.
- ~~**No OTLP collector endpoint.**~~ **Enforced rather than closed.** `deploy.sh` refuses to
  promote a release when neither `OTEL_EXPORTER_OTLP_ENDPOINT` nor
  `NativeWeb__Telemetry__Enabled=false` is set, so the silent-retry state can no longer reach
  production by omission — **but nobody has stood up a collector**, and choosing which of the two
  to set is still a decision. Original note kept for the mechanism: telemetry became opt-**out**
  on 2026-08-17
  (`NativeWeb:Telemetry:Enabled` is compared case-insensitively against the literal `"false"`, so
  unset means on — `docs/02-reference/configuration.md:106`, `apps/web/Program.cs:272,303`). A
  production host
  with no `OTEL_EXPORTER_OTLP_ENDPOINT` therefore stands up an exporter and retries against a
  collector that is not there. Point it somewhere or set the flag; do not leave it unset.
- ~~**`wwwroot/sitemap.xml` and `robots.txt:12` already name `https://epass.io`.**~~ **Closed
  2026-08-27, spec 011 T1/T2.** Both files are gone; the two routes render from
  `NativeWeb:CanonicalBaseUrl`, which startup rejects unless it is an absolute http/https origin
  with no path, query or fragment. Unset means each response names the requesting host, so a
  staging host can no longer advertise production. Note the neighbouring hardcode this did **not**
  touch: `apps/web/SecurityTxtEndpoint.cs` still emits `security@epass.io` literally — out of
  011's scope, and correct only after the cutover.
- **CI cannot restore in package mode.** Unchanged and still deferred: no reachable feed publishes
  `JauntyQ.*`. Only `-p:JauntyQUsePackages=true` paths are affected and CI is green in checkout
  mode, but a deploy pipeline that assumes package mode will not build. **011 sidesteps rather
  than closes it**: `build.sh` pins `-p:JauntyQUsePackages=false` and builds from the local
  checkout, which is why the release build is a workstation task and not a CI one.

Not blockers, verified green the same day: web-unit 494/494, integration 298/298, web-e2e 64/64,
CI run 2026-08-24T07:11Z green on both `aot-publish` and `test`.

### The capability gap nobody had measured, 2026-08-25

**Moved to `docs/architecture/002-epass-cutover-capability-gap.md`.** `apps/web` is not a
drop-in replacement for epass.io: it is a server-rendered redesign of epass.io's *browser*
surface, while epass.io is a JSON API with five clients hanging off it. The capability delta, the
per-client route delta, epass.io's own unmerged work, and what was and was not verified are all
in that doc.

**The cutover therefore waits on a scope decision nobody has taken:** whole replacement, or
browser surface only with `apps/api` left serving the native clients and the addon. The four
deploy blockers above are downstream of whichever answer wins.

## Perf re-measurement, 2026-08-24

The 2026-08-02 perf plan closed by asking for a re-measure before the epass.io cutover, on the
grounds that the box was noisy (±30% between identical runs). Done. **Both budgets pass with
room, and the k6 half of that plan is not this repo's to re-run.**

| Measure | Baseline 2026-08-01 | Now | Budget | Headroom |
| --- | --- | --- | --- | --- |
| Binary size | 36,918,272 B | 37,038,080 B (35.3 MB) | 42,500,000 B | 13% under |
| Time to `/healthz` | 881 ms | 537 / 544 / 550 / 593 ms | 5,000 ms | 89% under |

Binary grew 119,808 B (+0.32%) across two shipped specs and telemetry becoming opt-out, which is
noise rather than a trend. Startup roughly halved and, unlike the 2026-08-02 runs, was **stable**:
four samples inside a 56 ms spread, so the earlier ±30% caveat does not apply to this measurement
and no averaging is being hidden here.

The throughput numbers in `docs/plans/2026-08-02-002-perf-tail-work.md` (k6, 32 VUs, the
`pub_list`/`pub_tag` median-to-p95 gap) **cannot be re-taken here**: spec 003 extracted the
bookmarking app to manzil.my and `apps/` holds `web` alone, so the DB-backed list pages those
scenarios drove no longer exist in this repo. Same finding as the 2026-08-17 note at "Later" —
carry it to manzil.my's backlog rather than re-opening it as NativeWeb work. What remains here is
the publish-budget gate above, which `aot-publish` already runs on every CI build.
- **The antiforgery-rejection guard exists twice and nothing enforces that they agree.**
  `apps/web/Middleware/AntiforgeryValidationMiddleware.cs` and
  `NativeWeb.Scaffold.UseAntiforgeryRejection` are the same ten lines. Reviewed 2026-08-25
  (`fix/web-antiforgery-dedup`, Fable) and the duplicate was kept deliberately — apps/web does
  not reference the scaffolding library and should not acquire that edge for one extension
  method; `nativeweb.security` would drag libsodium into every stamped product, and a new
  library buys a package id and a CI packaging lane for one method. Both files now cross-
  reference each other. The residual risk is repo policy drift (a body, a log line,
  problem-details), which a comment cannot catch. The cheap enforcement, if it ever earns its
  keep: one test in `nativeweb.web.tests.unit` running both through the same three feature
  states and asserting they agree — the *test* project takes the scaffold reference, so the
  production dependency graph stays truthful. At a third copy, stop and give the guard one home
  (shared-source `<Compile Include>` link, or a real library).
- Both guards are a workaround for the RDG form binder not consulting
  `IAntiforgeryValidationFeature` before reading the form. Worth finding or filing the
  dotnet/aspnetcore issue and tagging both copies with its number, so they carry a deletion
  trigger rather than living forever. No issue number verified yet — do not cite one unchecked.

- **`Directory.Build.props`'s JauntyQ probes hard-code `extrode-legacy-org.com`.** Both fallbacks are
  `..\..\extrode-legacy-org.com\jauntyq` and `..\..\..\..\extrode-legacy-org.com\jauntyq` (lines 47-48), so the
  "more forgiving per-project build" is only more forgiving on a machine whose source root has
  that name; everywhere else the probes never match and `EnsureJauntyQRoot` names a path the
  reader has never had. `NativeWeb.slnx` by contrast uses the name-agnostic `../jauntyq/...`.
  Found by Fable's read-back on spec 010; the documentation was corrected to state the constraint
  rather than paper over it (`README.md` prerequisites, `docs/00-quick-start/getting-started.md`
  step 1). The
  fix in the build file — probe `..\..\jauntyq` and `..\..\..\..\jauntyq` too, before the named
  ones — was left out of a documentation branch deliberately.
- **`AppProcessFixture`'s 15-minute publish budget assumes an idle machine.** All 13
  `nativeweb.scaffold.tests.e2e` facts failed at exactly `00:15:00` on 2026-08-25 with a NativeAOT
  publish that had not finished, while a `review-deep` worker was saturating the CPU. Re-run alone
  on the same commit: 13/13 green in 2 s. So the tier is not flaky in itself — it has no headroom
  under load, and a CI runner sharing a host would look exactly like this. Either raise the budget
  or make the timeout message say "publish exceeded its budget" rather than surfacing as thirteen
  assertion failures.
