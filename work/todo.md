# Todo — NativeWeb

The unscheduled backlog: everything known to be worth doing that nobody has committed to a date.
Items enter here from anywhere (a review comment, an out-of-scope note, a bug found in passing)
and leave by being promoted into `status/next.md` or dropped with a reason.

Keep it honest rather than tidy. A backlog that only contains things someone intends to do is a
wish list; the value is in recording the ones nobody has got to yet.

## Now-ish

Small, clear, and could be picked up without a design decision.

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
  **reviewed 2026-08-18**, `docs/migration-lock-review.md`. Nothing to change today; the review
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
  `docs/configuration.md` (201 lines) documents all 32 `NativeWeb:` keys plus
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

- **The three `uses:` actions in `ci.yml` still target the Node 20 runtime, which GitHub has
  deprecated.** Every run now annotates "being forced to run on Node.js 24". Nothing is broken
  and nothing in this repo is involved: a GitHub Action of the common kind is a bundled
  `dist/index.js` that the *runner* executes with a Node binary it ships itself, so
  `setup-dotnet` installing .NET is a Node program, and no part of this repo depends on Node.
  The fix is a version bump to releases whose manifest says `node24` — `actions/checkout`,
  `actions/setup-dotnet` — once those exist and have been read. `ilammy/msvc-dev-cmd` is the one
  that actually needs watching: it is third-party and SHA-pinned deliberately, so it cannot drift
  on its own, but it also gets no fix unless upstream publishes one and the pin is moved. If
  Node 20 is removed before that happens, the `aot-publish` job breaks.
  **Deno is not the escape hatch here**, though it is the standing preference everywhere else in
  this repo (see `AGENTS.md`): the runner's `runs.using` accepts `node20`, `node24`, `docker` and
  `composite`, and there is no Deno JS-action runtime to select. Deno can run *inside* a step;
  it cannot replace the runtime that executes `checkout`. Blocked on upstream releases.

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

- **The six tolerant-ternary handlers still answer a misleading 400 for a JSON body.** Noted by
  Fable while reviewing the sweep above: `AdminEndpoints.PostApplicationDisable` /
  `PostApplicationRotateSecret` and `SettingsEndpoints.PostTwoFactorDisable` /
  `PostRecoveryCodesRegenerate` / `PostWebAuthnRemove` / `PostSessionsRevokeOthers` validate
  antiforgery, then read the form through a ternary that tolerates a non-form request rather
  than answering 415. A JSON POST with no header token therefore gets exactly the 400 this
  branch removed elsewhere. Deliberately left alone — extending the sweep to them is a second
  decision about six more endpoints, not a follow-through on the first.

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
- .editorconfig naming rules cover interfaces/types/members only — private fields, constants,
  locals and parameters are unenforced (V1 review note).

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
- (V8 review, opus) robots.txt/sitemap.xml hardcode the production hosts (epass.io,
  manzil.my) and are served verbatim in every environment, so staging advertises a
  production Sitemap URL; acceptable-if-intentional but unrecorded — decide at cutover.
- (V8 review, opus) apps/web sitemap.xml advertises https://epass.io/ while `/` still
  serves the temporary "Phase 0 foundation is up" landing; ship the real landing before
  submitting the sitemap anywhere.
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
- Skeleton templates are CRLF in the working tree although `.gitattributes:15` says
  `*.html text eol=lf`; the blobs are LF, so the two disagree. `git add --renormalize` would fix
  it but touches every template, so it is its own change. Harmless now that the hash generator
  normalizes, but it is why the skeleton emitted browser-mismatched CSP hashes.
- The skeleton's integration and e2e suites have never been run: they only exist inside a stamped
  product, which needs its own database. That is why `SmokeTests` kept asserting a nonce in the
  home page CSP for the whole of spec 006 (fixed 2026-08-03 by inspection, still unexecuted).
  Worth a CI job that stamps, creates a throwaway DB, and runs all three layers.
- `Directory.Build.targets` disables precompressed static-asset generation in Debug as a
  workaround for a framework defect: `StaticAssetDevelopmentRuntimeHandler` resolves compressed
  variants against `wwwroot` instead of the manifest content roots, so a gzip-accepting client got
  a zero-length body. Remove the property once the SDK resolves the variant correctly. Note the
  Debug build no longer exercises compressed-asset serving at all, so only `dotnet publish`
  covers that path — a Release smoke check is what would catch a regression there.
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
