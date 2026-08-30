# Previous

The task completed most recently, kept for one rotation so the next one has context.

- **Security audit backlog** — `fix/security-audit-2026-08-19`, 2026-08-31. The whole 2026-08-19
  `apps/web` audit worked through in one branch, seven commits. **744 web-unit / 346
  web-integration / 157 render / 64 scaffold-unit / 38 scaffold-integration / 85 forms / 184
  signin / 36 sample, 0 failed, 0 skipped**; solution build 0 warnings, 0 errors; structure-audit
  0 errors and the same 3 pre-existing warnings. Nine mutations, each killing its own fact.
  - **The backlog was stale and that is the finding worth keeping.** Six of its thirteen items had
    already been fixed by earlier sessions and never struck through — the antiforgery cookie, the
    `Secure = !IsDevelopment()` gates, `/auth/signout` rate limiting, the three register
    enumeration oracles, the bare `Results.Redirect("/")`, and the whole rate-limit route list,
    which `RateLimitCoverageTests` now enforces as a property rather than a list. Verifying each
    against the code first was most of the branch's value; fixing what was already fixed would
    have been the rest of it wasted.
  - **`form-action` is enforced against a submission's whole redirect chain, not its first hop.**
    That is why `'self'` was blocking the no-JS OAuth consent path, and why the fix is a
    per-response widening to the client's already-registered origin rather than a blanket
    allowance or an honest-comment retreat.
  - **The masked destination is signed rather than server-derived**, and the reason is a second
    oracle: derived from `temp_codes.sent_to`, a real account's mask would change the moment its
    code expired while an unknown identifier's never would. The first cut of the signing fix
    still failed its own test — the display fell back to "your email" but the hidden field kept
    echoing the attacker's string, so an unverifiable token now drops both.
  - **Two of the four WebAuthn assertions were wrong on the first run and the code was right.**
    System.Text.Json escapes as `<`, not as a literal `<`, and inert text like `onerror`
    survives verbatim inside the JSON string. A test failing is not evidence the code is.
  - **One item left open, needing a decision:** `GET /oauth/authorize` is a state-changing GET
    under a `SameSite=Lax` session cookie, so where silent-SSO conditions hold a cross-site link
    can drive a signed-in user into a grant with no consent interaction. Every fix is a design
    change. The register username-taken message stays as it is — a registration form that cannot
    say a username is taken cannot complete a registration, and the timing half is already closed.

- **Backlog leftovers** — `fix/014-leftovers`, 2026-08-31. Five `work/todo.md` items closed, none
  of them needing a host or a decision: the `NWF010` false positive, the two fixture-arithmetic
  assertions in `DeclarativeFormTests`, the `DenoToolingPolicyTests` race, `deploy.sh --port`, and
  the back-reference in `DeployScriptTests`. **706 web-unit / 339 web-integration / 85 forms /
  157 render / 64 scaffold-unit / 38 scaffold-integration / 36 sample, 0 failed**; solution build
  0 warnings; structure-audit 0 errors. Seven mutations, each killing its own fact.
  - **Two measurements worth keeping.** IOperation hands an analyzer the UNREDUCED symbol for both
    extension-call spellings, so `ReducedFrom` is null and the `this` is `Parameters[0]`; a first
    cut read `ReducedFrom` and the branch never ran, which only a mutation showed. And the
    `--help` output of every deploy script is a `sed` line range over its own header, so adding
    three comment lines truncated it — caught by `AssertHelpRangeIsIntact`, which exists for that.
  - **One item was opened rather than closed.** The runtime packages not referencing their
    generators is all three families, not just Forms, and fixing it means choosing a publishing
    model. Left on `todo.md` as a decision.

- **Spec 014 — declarative form binding** — `feat/014-declarative-form-binding`, 2026-08-30,
  **all ten tasks ticked and `014-spec.md` is `done`**. Brainstorm 001 T1.2 closed. A `[Form]`
  record gets a generated binder; `MapFormPost<TForm>` attaches the 415, the antiforgery metadata
  and the accepts metadata by construction; `NWF010` makes any other shape a build error. Both
  `/auth/register` POSTs are converted. **699 unit / 339 integration / 73 e2e, 0 failed, and no
  existing assertion edited**; `dotnet publish -c Release -r win-x64` of `apps/sample` and
  `apps/web` both 0 warnings.
  - **The plan's mechanism was disproved by measurement and replaced mid-build.** It assumed the
    Request Delegate Generator would resolve an `IBindableFromHttpContext` that this generator
    emits. It cannot: both generators run in the **same** pass over the same input compilation, so
    RDG never sees a partial another generator added — it recorded `hasBindAsync: false`, treated
    the model as a JSON body, and `ConsumesMatcherPolicy` answered every form POST **415 at
    routing**, before a line of this feature ran. Five of seven integration tests were red on that
    and nothing else said a word. `IFormModel<TSelf>` + `MapFormPost` replaced it; the correction
    is at the top of `014-plan.md`. This is §14's rule from the other direction, and the second
    time this repo has paid for the same fact.
  - **The route is a `RequestDelegate` because the alternative does not build.** The
    `Map*(…, Delegate)` overloads carry `RequiresDynamicCode`, and RDG cannot rewrite a call inside
    a generic method, so the first `MapFormPost` failed the AOT publish with IL2026 + IL3050. The
    `RequestDelegate` overloads carry neither attribute, and return `IEndpointConventionBuilder`
    rather than `RouteHandlerBuilder` — which is why `RequireServices<T1,T2,T3>` gained an overload
    on the interface beside the existing one. No existing call site changed.
  - **`AcceptsMetadata` is what decides which rejection wins**, and it was found by three existing
    assertions going red. RDG attached it for `[FromForm]` routes; without it,
    `ConsumesMatcherPolicy` no longer answers a tokenless JSON POST 415 at routing and the
    antiforgery middleware answers 400 first. `MapForm` attaches it.
  - **Recorded rather than chosen:** `IFormCollection` is keyed case-insensitively, so a field
    posted as `ConfirmPassphrase` binds a member whose wire name is `confirmPassphrase`. A form
    model cannot declare two members whose wire names differ only in case. Found by an assertion
    written the other way.
  - Nine mutations run across the branch, each killing the facts it should: the analyzer's matched
    verbs, the antiforgery attach, the accepts attach, the emitted content-type guard, the emitted
    wire names, the 503-degrade, and the sample's page-size ceiling and case-insensitive query.
  - **Pre-merge two-lens review, 2026-08-30/31.** Correctness and consequence lenses over the
    whole branch diff; the generator findings were verified by reading `FormModel.cs` and
    `FormGenerator.cs` rather than by refutation prompt. **Eleven code fixes and four weak
    assertions**, each landing with the fact that would have caught it — eleven further mutations
    run, every one killing its own fact. The code fixes: `SymbolDisplay.FormatLiteral` for wire
    names (hand-rolled escaping emitted a raw newline into a string literal); `@`-prefixing
    keyword identifiers (a model written `@class` emitted `partial record class`); the duplicate
    wire-name check moved to `OrdinalIgnoreCase` to match how `IFormCollection` reads;
    `NullableAnnotation.Annotated` rather than "not NotAnnotated", which had let a plain `string`
    in a nullable-disabled file bind null silently; `[property: FormField]` read as well as the
    parameter attribute, which is what `014-plan.md`'s own example wrote; file-local types
    rejected by NWF005; structural `IEquatable<>` on `FormModel`, `FormFieldModel` and `Parsed`,
    without which the incremental cache never hits; `Map` and `MapFallback` added to NWF010's
    matched set; null guards on `MapForm`'s pattern and method; and two corrected remarks in
    `FormBinding`. The four assertion defects: a theory that called the guard and asserted
    nothing, a `NotEqual(BadRequest)` that a 500 would have satisfied, an undisambiguated 400,
    and a binding fact that posted `Name` — testing case-insensitivity by accident rather than
    the naming convention on purpose. Three `WebApplication` hosts now disposed.
    **Final: 82 forms-unit / 36 sample-unit / 699 web-unit / 339 integration / 73 e2e, 0 failed;
    both AOT publishes 0 warnings; structure-audit 0 errors.**

- **htmx 4.0.0 upgrade** (spec 012, `feat/012-htmx-4-upgrade`, merged 2026-08-29, unpushed).
  All fourteen tasks ticked; **`012-spec.md` is not `done` — R16 is unmet.** htmx 4 is a rewrite
  from `XMLHttpRequest` onto `fetch()`: every lifecycle event was renamed to `htmx:phase:action`
  and every payload now arrives as `detail.ctx` and nothing else. The migration surface turned out
  to be small because no `hx-*` attribute is authored anywhere in this repo — all of them are
  derived at runtime from `data-nw-*` by `boot.js`, so the upgrade was three files of glue plus the
  vendored library, not a sweep of the render tree.
  - **Three changes would have failed silently under a mechanical rename**, which is why the
    translation was done by hand. Restoring the busy state on `after:request` leaves a permanently
    disabled button after any network failure, because 4.0 does not fire that event when the
    request never completes — it moved to `finally:request`. `after:swap` now dispatches on
    `ctx.sourceElement`, so the old `event.target` fallback would focus the wrong node with no
    error. And 4.0 **swaps 4xx/5xx bodies into the target by default**, which would have rendered
    guard rejections inside authenticated pages; `noSwap: [204, 304, "4xx", "5xx"]` in the
    `htmx-config` meta closes that, and it had to land in the same commit as the library.
  - **A pre-existing test blind spot was exposed by mutation, not by review.** Reverting the
    antiforgery listener to its 2.x event name left 67/67 green: the hidden
    `__RequestVerificationToken` field covers every form post regardless of whether the header is
    sent. `AntiforgeryToken_IsSentAsAHeaderOnHtmxRequests` now pins the header itself. All four new
    e2e tests were mutation-checked; two of the four rounds initially produced bogus results from
    stale build-server state, so each mutation run is preceded by
    `dotnet build-server shutdown` plus a full build.
  - **Two features were evaluated and declined on evidence.** Morph swaps: zero eligible targets,
    because all five swap-target shapes contain their own forms and their own error banner, and
    `#settings-sessions` — the one pure-list candidate — holds antiforgery and bot-trap hidden
    inputs inside the swap region that morph's input preservation would leave stale
    (`docs/decisions/2026-08-29-003-morph-swaps-not-adopted.md`). `hx-alpine-compat`: `git grep`
    finds zero tracked Alpine directives in `apps/web` or `skeleton`, so it resolves a conflict
    that cannot occur here.
  - **What was measured:** the minified library, 51,238 B → 36,716 B, **28.3% smaller**. What was
    not: §14's throughput and RSS figures, whose harness is two purpose-built minimal apps in a
    sandbox that no longer exists — and which measure the server, where a browser-side library does
    not appear. R16 is recorded unmet in §14 and in `012-tasks.md` T11 rather than filled with a
    number nobody produced.
  - The 2.0.10 build is retained at `libs/nativeweb.assets/rollback/htmx-2.0.10.min.js`, outside
    every `wwwroot` so it is kept without being reachable or served. The `rollback/` carve-out is
    documented in that library's `README.md`, which otherwise bars third-party files.

- **Production deployment infrastructure** (spec 011, `feat/011-production-deployment`, merged
  2026-08-27, unpushed). T1-T14 and T16 ticked; **T15 is outstanding and is a person's to run**,
  so `011-spec.md` stays `draft`. The deploy half Slice 10 never started: `scripts/deploy/`
  provisions a bare Debian host, releases blue/green behind Caddy, rolls back, and takes nightly
  `age`-encrypted dumps to S3-compatible storage, with `docs/02-reference/deployment.md` as the
  runbook. Blue/green over symlink-flip because R10 says the previous release keeps serving
  *throughout*, which a restart gap cannot satisfy.
  - **The two-lens review found fifteen real defects in this branch's own work, and both lenses
    independently put the same one first.** `deploy.sh` invoked `db/migrate.sh` with no
    environment, so it inherited the development database and role -- `nativeweb_dev` and
    `nativeweb_app`, neither of which `provision.sh` creates. Every first deploy on a provisioned
    host would have aborted at the migration step. The nightly backup would have failed on the
    first timer run of every host for the same class of reason: it runs as root and no PostgreSQL
    role called root exists. Both preflight guards checked the two keys `provision.sh` seeds
    *populated*, so neither could ever fire while the blank ones went unchecked;
    `StartLimitIntervalSec` sat in `[Service]`, where systemd ignores it; and `live_colour` read
    the first port-shaped string in the upstream file, which was a comment. Full list in
    `011-spec.md`, "Acceptance review".
  - **T14 was measured rather than reasoned.** `MemoryDenyWriteExecute=yes` -- the one hardening
    line that can stop the service booting -- was verified against a real `linux-x64` NativeAOT
    publish on Debian 12 under WSL2: it started, answered `/healthz` 200 and `/readyz` 503 (the
    no-database state), indistinguishable from the same binary with the filter off. The lazy
    `dlopen` of `libsodium.so` behind Argon2id does not run at startup, so it was probed
    separately under the same filter: `dlopen` succeeded, `sodium_init()` returned 0. Caveat on
    record: a `--user` unit on the WSL2 kernel, not a `--system` unit on the target host.
  - **Running `build.sh` for real found two defects a reading would not have.** A Debian WSL image
    with no `zstd` compiled the whole NativeAOT publish (34,632,768 B) and then died on the tar,
    leaving 4,096 bytes of a 34 MB artifact at the exact path `deploy.sh` is told to ship. Tool
    preflight added, ordered after the Windows-shell refusal because git-bash has no zstd either;
    the EXIT trap now covers the artifact and is narrowed again once tar returns 0.
  - **Three requirements are unmet and none can be met from a session.** R1 (provisioning
    walkthrough), R16 (restore drill) and R17 (disaster recovery) all need a real host. R15 is
    conditional as well: `BACKUP_HEARTBEAT_URL` is unset, so a backup that silently stops running
    is not noticed, and `backup.sh` says so in its own output on every run.
  - Suite: 611 web-unit / 307 web-integration / 67 web-e2e, 0 failed. Four mutations confirmed
    the new facts are not vacuous.

- **Documentation and tutorials** (spec 010, `docs/010-documentation-and-tutorials`, merged
  2026-08-25, unpushed). T1–T10, all ticked. Four documents — a filled `README.md`,
  `docs/00-quick-start/getting-started.md`, `docs/00-quick-start/tutorial-first-feature.md` and
  `docs/README.md` — plus the feature the tutorial is built on, `GET /articles` in `apps/sample`.
  (Both quick-start paths were flat `docs/*.md` when 010 shipped; `feat/docs-site` moved them on
  2026-08-27.)
  - **R12 was the expensive requirement and the one that paid.** Every command executed before it
    was written down, which is how the README's JauntyQ note came to say what it says: a clean
    clone cannot build `NativeWeb.slnx` unless `jauntyq` is a literal sibling, and
    `-p:JauntyQUsePackages=true` does *not* rescue it, because the solution lists JauntyQ's two
    projects as members unconditionally. Path A was walked by stamping a real product into
    `../probe` (47 unit / 7 integration / 2 e2e, 0 failed); its two footnotes —
    `<Product>__Db__ConnectionString` in the environment, and roots as environment variables rather
    than `-p:` flags, because command-line properties do not cross into the e2e fixture's child
    `dotnet publish` — are findings from that run, not inferences.
  - **The tutorial's feature is real, and it needed JauntyQ to ship something.** `ORDER BY
    created_at` on a table indexed only by primary key raises `JNT8007`, fatal here under
    `TreatWarningsAsErrors`. JauntyQ added `-- @allow-sort <reason>` (`bf37907` in that checkout),
    mirroring `@allow-unindexed`: mandatory reason, bare form rejected `JNT3008`, and a
    self-expiring `JNT8012` when the directive suppresses nothing. Verified rather than assumed by
    putting it on `List.sql`, which sorts on the primary key, and watching `JNT8012` quote the
    reason back. Chosen over an index migration because the table is three seeded rows in a demo,
    where PostgreSQL would ignore the index; the four precedent migrations that *did* take the
    index (`0023`, `0033`, `0034`, `0048`) are named in the query's own comment.
  - **19 new facts, every one perturbed** — sample-unit 8 → 16, scaffold-integration 24 → 31,
    scaffold-e2e 9 → 13, 0 failed, 0 skipped. Twelve distinct perturbations. Two method findings
    are recorded in `010-tasks.md` and repeated in the tutorial's ninth section because they
    generalise: perturbing `WHERE` and `ORDER BY` together failed four integration tests rather
    than two, `ORDER BY ASC` having pushed the future-dated rows off the twenty-row page, so two of
    them failed for a reason unrelated to what they assert; and the `.RequireAuthorization()`
    perturbation failed in 96 ms, fast enough to suspect the host had never started — it had, and
    the message proved it.
  - **T10's read-back found three divergences, all recorded rather than smoothed.**
    `docs/constitution.md` is still the unfilled template while `docs/README.md` lists it as
    binding; `conventions.md` names cleanup scripts `cleanup-<milestone>.ps1` where the global rule
    and the actual files use `<NNN>-<slug>.ps1`; and the tutorial's page carries no `data-nw-*` and
    loads no `boot.js`, which is correct for a page with no interaction and is stated as such.
    One thing was corrected instead: `db/sample/README.md`'s "the tests do not truncate" section,
    made stale by this branch's own `DeleteByIdPrefixAsync`.
  - `scripts/cleanup/010-documentation-and-tutorials.ps1` was written and run by the user: 26
    scratch databases and the role `probe_app` dropped, leaving `nativeweb_dev`,
    `nativeweb_sample_dev` and `nativeweb_sample_test`.

- **CI Node-runtime cutover** (three branches, merged 2026-08-20; pushed 2026-08-24). GitHub is
  withdrawing the node20 action runtime, and the useful question turned out not to be "what do we
  bump" but "what is running Node at all". Two of the three actions were **deleted** rather than
  bumped: `ilammy/msvc-dev-cmd` set up a toolchain the ILCompiler locates itself through its own
  bundled `findvcvarsall.bat` (measured — a full AOT publish of `apps/web` from a shell with no
  `VCINSTALLDIR` and no `cl.exe` on `PATH` links and exits 0 in 56s), and it was actively harmful,
  exporting `Platform=x64` into MSBuild and inserting a phantom path segment into the publish
  output; `actions/setup-dotnet` installed an SDK both hosted images already ship (10.0.110,
  10.0.204, 10.0.302 on Ubuntu 24.04 and Windows Server 2025). The SDK requirement moved to
  `global.json` (`10.0.100`, `rollForward: latestFeature`) in both this repo and the skeleton,
  which is strictly wider coverage than the workflow had — it governs local builds too. That pin
  is load-bearing and was proved so: perturbed to `rollForward: patch`, the SDK resolver itself
  refuses with `sdk-not-found` before any test runs. `actions/checkout@v7` is now the only `uses:`
  step across both workflows, so **no code from any account but GitHub's runs with the workflow
  token**. `AtLeastOneThirdPartyActionIsPinned` could only survive by being weakened, so it was
  inverted into `NoThirdPartyActionsAreUsed`; the SHA theory beside it stays as a latent guard.
  494/494 web-unit green, build 0 warnings / 0 errors. The one thing local measurement could not
  cover — whether `vswhere` finds the C++ tools on the hosted image with no setup step — **went
  green on the first run after the push**, 2026-08-24T07:11Z, both jobs, on image
  `windows-2025-vs2026` 20260818.207.1. Reasoning and every measurement:
  `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.

- **epass sign-in library** (spec 008, `feat/008-signin-library`, merged 2026-08-19). T1–T19, the
  relying-party half of the epass handshake as `libs/nativeweb.signin.epass` — a consumer supplies
  an `ILoginFlowStore` and an `OnSignedIn` resolver and gets the start, callback and sign-out
  endpoints, the signed `v2` session ticket and the open-redirect guard. `Pkce` and `Masking`
  moved to `nativeweb.security` first (T1), so the library's graph does not touch `nativeweb.auth`.
  Measured green: 164 unit / 16 integration / 2 e2e, full build with no warnings, AOT publish
  exit 0 with zero IL2026/IL3050. Three findings from the reviews were real and fixed rather than
  noted: `AddEpassSignIn` never called `AddAuthorization`, so the README's own wiring block threw
  at first request; an unbounded login POST relayed whatever the form carried to the provider under
  the client's credentials (now 254 bytes, RFC 5321, refused before any call); and an all-one-byte
  signing key passed validation — the fix broke 20 fixtures that were using `new byte[32]`, which
  is the finding proving itself. The conformance fixture lost its `ExpireAsync` hook: it now starts
  a flow already expired, which additionally pins that a store honours the expiry it was *handed*
  rather than inventing one. Two deliberate deviations from the approved plan: the fixture ships as
  its own project (xunit must not be in a production package's closure) and `AddEpassSignIn` takes
  only a configure delegate. The justsmtp.com migration (R12) is handed off, not done here.

- **Away backlog slate** (seven branches, merged 2026-08-18). The items the user picked on the
  way out, plus the two they decided rather than deferred. `db/migrate.sh` now takes a
  session-level advisory lock through a psql coproc before it reads anything, so a second runner
  queues and says so instead of racing `is_tracked`; verified by `db/test-run-lock.sh` rather than
  an xUnit fact, because launching the runner from .NET on Windows deadlocks on the redirected
  pipe and the failure would look exactly like the bug. `Redirects.To` answers a POST with 303 and
  a GET with 302 -- Minimal APIs has no 303 helper, so `Location` is set by hand. The 415 sweep
  reached the last six handlers, taking guarded handlers from 17 to 23 and turning six tolerant
  ternaries into plain `ReadFormAsync`. `CompressedAssetTests` covers the one asset path only
  `dotnet publish` exercises. `.editorconfig` gained field/local/parameter naming rules -- and the
  find behind them: IDE1006 was off, so every naming rule in that file, including one written at
  `severity = warning` on day one, had never run. The solution measured at a single violation.
  `docs/02-reference/migration-lock-review.md` records which migrations would hurt against a live
  database.

- **Bounded backlog run** (four branches, merged 2026-08-17). Taken simplest-first while the user
  was away, on the scope they set on the way out. `fix/migrate-record-in-transaction` made
  `db/migrate.sh` apply and record in one transaction, asks the server whether the wrap survived
  rather than trusting its log, and lets a concurrent double-apply fail loudly.
  `chore/skeleton-local-mode-ci` rebuilt the skeleton's CI in checkout mode — the package mode it
  forced could never restore, because the private feed does not exist — with the framework checked
  out as a SIBLING of the product: nested, `Directory.Packages.props` discovery walks up out of the
  clone into the product's copy and restore dies NU1008 on both JauntyQ projects, measured. That
  run also closed a vacuous pass of my own: a `(?m)` job-header regex that would not step over a
  `\r`, so the sweep found no jobs and reported green. `fix/415-before-antiforgery` is the
  400-vs-415 decision below, now taken. The JauntyQ generator flake is written up.
- **CI closeout and the first-party application gap** (many branches, merged 2026-08-17). The CI
  chain went green end to end for the first time, including `aot-publish` and all three test
  tiers. Behind it: migration 0055 creates the first-party application row for every deployment,
  which is what `db/seed/ci` had been papering over for CI alone — measured 33/258 integration
  failures with 0055 withheld against 258/258 with it. `RealmResolver.ResolveFirstPartyAsync`
  now returns null rather than throwing, both call sites answer 503, and a three-valued boot
  check refuses to start outside Development only when the database *answers* and the row is
  absent. `db/seed/ci` is retired and deleted; `CiSeedTests` was rewritten to assert the
  inverse, that ci.yml applies no seed at all. Fable's review caught a real defect in
  `DemoteBelowEnvironmentVariables`, which was wrong for more than one added source and had four
  passing tests that were all single-source. The skeleton's `msvc-dev-cmd` is SHA-pinned, guarded
  by `ActionPinTests` over both repositories' workflows. Cleanup ran: 97 merged branches, 29
  scratch files and 8 repro databases removed.
- **Overnight backlog** (three branches, merged 2026-08-17). `chore/test-and-skeleton-hygiene`
  removed the two dead OpenApi package pins (approved beforehand: dependency removal) and made
  `check-publish-budgets.ps1` default to the port ci.yml actually passes, guarded by a test that
  reads both files. `feat/render-diagnostics-locations` gave every NWT diagnostic a real file and
  line — they were all `Location.None`, so nothing in a build log was clickable — validated
  `@param` types so a type with a space in it is NWT003 rather than a C# error in generated code,
  and added NWT016, which rejects interpolation inside a tag but outside a quoted attribute value.
  That last one makes `TemplateWriter`'s own safety comment checkable for the first time.
- **IdP correctness backlog** (`fix/idp-correctness-backlog`, merged 2026-08-17). `POST
  /auth/2fa/verify` answered an expired challenge with a bare 302, and that form is htmx-driven,
  so the sign-in page's whole document was swapped into the 2FA card. Now `Redirects.To`, along
  with eight GET-side redirects that are latent only because nothing links to them yet.
  `AppProcessFixture` gained a 15-minute publish budget, event-based draining of both pipes (the
  sequential drain could deadlock the child), the child's last output lines in the exception, and
  `Kill(entireProcessTree: true)`; `ProcessRunTests` covers all three exits. The 400-vs-415
  ordering was examined and deliberately left alone — it is consistent at all 23
  `ValidateRequestAsync` sites, so it is a design decision to make, not a slip to fix; recorded in
  `work/todo.md`.
- Before that: `fix/loose-ends` (the last 2026-08-03 CSP review finding, plus the cleanup-script
  `--execute` interface), spec 007 (session tokens hashed at rest) and `fix/csp-hash-scan-context`.
