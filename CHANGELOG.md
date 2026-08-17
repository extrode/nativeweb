# Changelog — NativeWeb

What shipped, newest first. One entry per release, not per commit: the git log already records
every commit, and a changelog that repeats it is read by nobody.

An entry is added when `dev` merges to `main` and gets its semver tag. Work that is merged into
`dev` but not yet released belongs in `status/previous.md`, not here.

Format is [Keep a Changelog](https://keepachangelog.com/), versions are semver. Use only the
sections that apply; drop the empty ones rather than leaving them blank.

## [Unreleased]

Merged into `dev`, not yet tagged.

### Added
- **Cacheable public pages** (spec 006) — anonymous `/` and `/u/*` are served from an
  in-process output cache, keyed by (path, `?page=`, negotiated locale) and bypassed on the
  mere *presence* of a session or flash cookie, so a personal response can neither be stored
  nor served to anyone else. Writes evict by owner tag immediately; a 60s TTL is the backstop,
  not the normal latency. Anonymous responses carry no antiforgery pair and no nonce, and are
  authorized instead by a build-generated hash CSP: the render generator now emits
  `InlineScriptCspHashes` per template, so the policy cannot drift from the scripts the page
  ships. Measured at 10 VUs against the published binary: median <0.1ms and p95 580µs on the
  cached path against 1.17ms / 2.11ms rendering from PostgreSQL, with the 192ms DB tail gone
  from anonymous traffic entirely. Mirrored into the skeleton, whose own `/` ships declared
  public so the mechanism is exercised rather than dormant. Hashes are computed over the
  newline-normalized body, because a browser normalizes the input stream before tokenizing —
  otherwise a CRLF checkout emits constants no browser will match.
- **Built-in UX defaults** (spec 005, B1–B4) — busy states and focus-after-swap in
  `boot.js` (re-vendored byte-identical everywhere); one-shot flash messages via a
  `__Host-nw-flash` cookie (IdP nullable-tuple API, manzil/skeleton empty-string tuple API
  feeding template banner partials); `/version` beside `/healthz` with a build-time
  `<Version>+<git-sha>` stamp; access keys on primary actions; native constraint attributes
  mirroring server rules with field-level server errors (`aria-invalid` + `role=alert`;
  credential failures stay generic banners); `FormGuard`/`FormChrome` dedup refactors incl.
  the 415-before-antiforgery order fix; the v2 PE handoff folded into
  `docs/progressive-enhancement.md` (adopted, rejected-with-reasons, R12 any-content-type)
  and deleted. Error pages verified pre-existing (spec 003 R39). Full breakdown:
  `docs/specs/005-ux-defaults/005-tasks.md`.
- **Progressive enhancement layer + formatted HTML output** (spec 004, B1–B5) — the `data-nw-*`
  attribute vocabulary (`target`/`swap`/`trigger`/`confirm`) normalized to `hx-*` at runtime by
  `libs/nativeweb.assets/boot.js` (~100 lines, vendored per app with byte-parity tests, loaded
  `defer` before htmx; never overwrites authored `hx-*`, re-processes swapped-in descendants
  htmx's element snapshot misses); generator pretty-printer emitting indented HTML at build time
  (zero runtime cost, `pre`/`textarea`/`script`/`style` preserved, per-depth partial variants);
  both apps retrofitted — every IdP form now carries a hidden antiforgery input
  (`Layout.AntiforgeryField`) and every post-auth success site returns a real 302 to plain
  browsers (`Redirects.To`), making "works without JavaScript" true rather than aspirational;
  JS-off + normalization e2e coverage in both repos (incl. the settle-window and
  descendant-anchor swap paths); doc rewritten at `docs/progressive-enhancement.md`. Publishes:
  `nativeweb.web.exe` 35.2 MB (budget 42.5 MB), `manzil.exe` 29.1 MB, both zero-warning.
- **Architecture v2** (spec 003, V1–V10) — the template's second generation. Compiled-template
  render engine (`libs/nativeweb.render` + source generator: encoded-by-default HTML, typed
  params, partials/layouts, UTF-8 span literals) with an AngleSharp well-formedness gate;
  build-time i18n catalogs with dialect fallthrough (en + ms shipped, `lang`/`dir` negotiated
  per request); bot defense (4 honeypots + signed time canary on every form, fake-success
  discipline, e2e visibility assertions); security baseline (HSTS, `__Host-` cookie, 128 KB
  body cap, request timeouts, CSP reporting, append-only `audit_log`, `security.txt`, ASVS
  checklist, CI CVE gate); perf/ops (Brotli for HTML, off-by-default OTel, `/healthz` +
  `/readyz`, `ValidateOnStart`, publish knobs, CI size/startup budgets — baseline 36,918,272 B
  / 881 ms to `/healthz`); static-pages pipeline with nonce-free CSP and static 404/500;
  `skeleton/` tokenized product shell + `tools/new-product.ps1`; style enforcement in build.
- **manzil.my extraction** (spec 003 V10) — DevMarks moved out to
  `C:\home\code\extrode-legacy-org.com\manzil.my` as the first product stamped on the v2 architecture
  (`scripts/extract-manzil.ps1`: full manzil rename, fresh history, `$(NativeWebRoot)`
  dual-mode framework refs). Its suites run green there (217 unit / 81 integration / 12 e2e,
  0-warning AOT publish: 30,483,968 B / 977 ms to `/healthz`).

### Security
- **Session tokens are no longer recoverable from the database** (spec 007) — the
  `__Host-NativeWeb.Session` cookie value *was* the `bff_store` primary key, written verbatim,
  so any read of that table — a backup, a replica, a logical-replication slot, a slow-query log,
  a read-only injection primitive — yielded live bearer credentials for the fourteen days of a
  session's life. Rows are now keyed by `SessionToken.StorageId`: the first 128 bits of SHA-256
  over the token, kept as a `UUID` so the column, the index, all eight `db/queries/BffStore/*.sql`
  and the generated data layer are untouched and lookup stays a single point lookup. The
  type never hands the plaintext out as a `Guid`, so a call site cannot pass it to the data layer
  by accident — a speed bump rather than a wall, since `Guid.Parse(CookieValue)` is one hop away;
  closing that needs the session key to be its own type, which is on the backlog. Migration 0054 converted the existing rows in place — sessions live at
  the time it ran kept working, since only what the cookie is compared against changed — and is
  documented as one-way and not re-runnable. Verified against 106 real rows in `nativeweb_dev`.
  The C# and SQL derivations are pinned to each other from both sides, because the obvious
  `new Guid(byte[])` spelling reads the first three fields little-endian and would silently sign
  every user out. Found by the V6 Fable review and left pre-cutover; closed before it became a
  question of invalidating live sessions rather than empty ones.

### Fixed
- **A form POST with the wrong media type is answered 415, not 400** -- antiforgery reads its
  token from the form as well as the header, so `ValidateRequestAsync` on a JSON body threw and
  the handler blamed the token. The content-type guard now runs first at all 17 handlers in
  `apps/web/Auth` that carry both. Not a security change: a request that IS a form reaches
  antiforgery unchanged, and the 415 path is reachable only by requests antiforgery was going to
  reject anyway; the 415 decision reads the caller's own `Content-Type` and no server state, so
  it is not an oracle. Nine handlers are deliberately untouched -- six read the form through a
  tolerant ternary, and three (WebAuthn enroll/verify, sign-out) accept either a JSON body with a
  header token or a real form, so a 415 guard would reject a request that has to work. Held by a
  source-order sweep over every handler plus HTTP-level facts on two endpoints.
- **A freshly migrated database is now serviceable** -- `db/migrations/0055_first_party_application.sql`
  creates the first-party application row for every deployment, closing the gap CI hit and that a
  fresh production database would have hit identically at the epass.io cutover. Guarded on "realm 1
  has no applications", so it is re-runnable and adds no dead row to a deployment that already
  configured its own. The secret is generated by `gen_random_uuid()` inside the migration and never
  recorded anywhere, which is safe because `RealmResolver.ResolveFirstPartyAsync` matches on
  `client_id` and reads `app.Id` without consulting `secret_hash`; the secret only matters on the
  headless `ResolveByClientCredentialsAsync` path, which the first-party application does not take.
  Measured both ways: every migration except 0055 gives 33/258 integration failures, the exact CI
  set; with 0055 applied to the same schema, 258/258.
- **CI is green end to end for the first time** (run 32020940278) -- every tier now actually
  executes on a runner: 409 unit, 258 integration, and 62 E2E, the last of which had never run
  on CI at all. Two defects behind the last red run:
  - `db/migrations` alone does not produce a database the app can serve a request against.
    `RealmResolver.ResolveFirstPartyAsync` throws when the first-party application row is
    absent and no migration creates it, so every first-party realm resolution answered 500 --
    33 integration failures, none of which named the missing row, because `ErrorPageMiddleware`
    renders its own page rather than a stack trace. `db/seed/ci/` now carries exactly that row.
    The existing dev seed was tried instead and rejected by measurement: its `TRUNCATE users
    CASCADE` does not restart the identity sequence, so alice lands on id 2 while
    `TestDatabase.SeededUserId` is 1, failing six `SessionAuthTests`.
  - `AddUserSecrets` appends its source, so the user-secrets store outranked a real environment
    variable. With `ConnectionStrings__NativeWeb` exported the integration fixture wrote rows to
    one database while the app under test read another, reporting "Incorrect username or
    password" with no hint of why -- which meant no CI-only failure could be reproduced locally
    at all. User-secrets are now inserted below the environment-variables source, matching the
    default non-slim host precedence. A Fable review then caught the reordering helper
    corrupting the order for more than one added source (it walked onto the environment source
    and demoted that), a latent contract bug the single-source tests all passed over.

### Removed
- The DevMarks projects (`apps/devmarks`, `libs/nativeweb.devmarks.data`,
  `tests/nativeweb.devmarks.tests.*`, `db/devmarks`), now living in the manzil.my repo; CI,
  solution and docs rescoped to the IdP.
- **DevMarks** (`apps/devmarks`, spec 001) — a complete bookmarking app for software engineers
  and the first full application on the Lean Native Web template: accounts (Argon2id
  passphrases, lockout, breach checking), bookmark CRUD with SSRF-guarded metadata/favicon
  fetch, tags with rename-merge and per-tag visibility, ranked Postgres full-text search
  (title/description/notes/URL/tag names), private-by-default public pages (`/u/{username}`,
  `/u/{username}/t/{tag}`, indistinguishable 404s), Netscape HTML + JSON import/export with
  idempotent re-import and lossless JSON round-trip, fixed-window rate limiting, structured
  request logging, health endpoints. Every core flow works without JavaScript. Own database
  (`db/devmarks`), JauntyQ data layer (`libs/nativeweb.devmarks.data`), three test tiers
  (196 unit / 56 integration / 7 e2e), zero-warning NativeAOT publish, CI coverage.

- **IdP passphrase-reset by email** (spec 002) — the reset flow accepts an email address as well
  as a username, so auto-provisioned passwordless accounts that have never seen their generated
  username can complete a forced reset unaided. Copy updated ("Username or email").
- **Operator un-flag** (spec 002) — a stateless clear form on `/admin/accounts` lifts a forced
  passphrase reset via the same (realm, username) lookup that sets it, with the setter's
  explicit feedback; covered by all three test tiers.

### Changed
- **Telemetry is on by default** (spec 003 R33, `docs/plans/2026-08-17-004-enable-telemetry.md`) —
  `NativeWeb:Telemetry:Enabled` inverts from opt-in to opt-out: anything that is not the literal
  `"false"` exports, including the key being absent. An opt-in flag is one first exercised in
  production, which is precisely what the epass.io cutover could not afford. The resource now
  carries `service.name = nativeweb.web` and `service.version`; before this it was
  `unknown_service:<host process>`, so two instances were indistinguishable at the collector.
  No app-level redaction was added, and that is measured rather than assumed: the ASP.NET Core
  instrumentation already redacts every `url.query` value to `Redacted` (a package default, so
  a test asserts it), and no production route carries a secret in its path. Verified against the
  NativeAOT image, not just the JIT test host — 35.2 MB, 619 ms to `/healthz`, no trim warnings.
  The skeleton deliberately stays opt-in: default-off is right for a template, default-on is a
  deployment decision this product has made.
- **IdP hardening close-out** (spec 002, `docs/specs/002-idp-hardening/`) — the deferred
  security/conformance backlog closed across seven reviewed branches:
  reset-code issuance capped per identifier (30 s floor + 5/24 h rolling window, one atomic
  conditional upsert); reset success writes are transactional; synthetic identifier masks use
  the full IANA TLD registry and contact-shaped identifiers mask as themselves, closing the
  TLD/shape existence oracles; pre-stamping `bff_store` session rows backfilled with their
  owner (migration 0049); the per-sign-in HIBP network check throttled to once per account per
  configurable window (default 24 h, migration 0051 + `BreachResult.NetworkChecked`), with the
  offline common-passphrase list still on every sign-in; token/introspection endpoints brought
  to RFC 6749/7662 conformance on the flagged points (percent-decoded Basic credentials,
  dual-credential rejection, tokenless introspection → 400, active-user re-check on every
  grant); htmx's un-nonced indicator stylesheet disabled so no page produces a CSP violation
  (e2e asserts an empty violation list); the application database role renamed to the neutral
  `nativeweb_app` in migrate.sh, CI and local dev; the e2e app fixture disposes its process on
  the startup-timeout path and derives the RID instead of hardcoding `win-x64`.
  Final state: 319 unit / 206 integration / 44 e2e green, zero-warning NativeAOT publish.

### Added
- **NWT016: interpolation loose in a tag is a build error** — `TemplateWriter` encodes
  `<`, `>`, `&`, `"` and `'` and nothing else, and its own comment says that is safe "in text
  and QUOTED-attribute contexts, which are the only contexts templates may interpolate into".
  Nothing enforced that. A space is not encoded, so `class={{X}}` lets one value close the
  attribute and open `onfocus=…`, and `<div {{X}}>` is the same escape by a shorter route. The
  generator now rejects both. Block tokens are exempt — `<span{{if C}} title="{{X}}"{{/if}}>`
  is what the real layout writes, and a block emits nothing itself; its contents are separate
  tokens, checked on their own. The scanner walks tags and skips comments but is deliberately
  not a tokenizer, and it jumps over every `{{…}}` region wholesale: an expression is C#, not
  markup, and reading its bytes as markup invents structure that is not there. No existing
  template in the repo or the skeleton trips it.

### Removed
- **The two OpenApi package pins** — `Microsoft.AspNetCore.OpenApi` 10.0.7 and
  `Microsoft.OpenApi` 2.7.5 had no `PackageReference` anywhere in the repo, so central version
  management was pinning versions of packages nothing restored. The second existed only to
  hold the first's transitive above a vulnerable 2.0.0, so its comment had been describing a
  relationship that no longer existed. Removed on explicit approval, since a dependency removal
  is not something to do unasked.

### Fixed
- **CI had no reachable source for the JauntyQ packages** — the repo carried no `nuget.config`
  at all, so package-mode restore (`-p:JauntyQUsePackages=true`, the mode CI builds in) could
  only see nuget.org, and `JauntyQ.Generator` / `JauntyQ.Runtime` are published to GitHub
  Packages. It never failed on a developer machine, because `~/.nuget/packages` was already
  populated by a local `dotnet pack` and the cache stood in for the feed; on a clean runner it
  is `NU1101` on both. `nuget.config` now declares the GitHub Packages source and maps
  `JauntyQ.*` to it, which also keeps an ordinary checkout-mode restore from touching an
  authenticated source and stops those ids resolving off nuget.org. Credentials stay out of the
  file: ci.yml authenticates with the job's own `GITHUB_TOKEN` immediately before restore.
  `PackageFeedConfigTests` covers the two ways this rots silently — the source renamed in one
  file but not the other, and a `JauntyQ.*` pattern added under nuget.org, which NuGet accepts
  without complaint and which is the dependency-confusion shape.
- **CI never ran on `dev` or `main`** — the push trigger excluded exactly the two branches
  where work lands, on the theory that anything reaching them had already passed on a feature
  branch. The merge itself was what nothing verified, and since finished work here ends as a
  push straight to `dev`, the ordinary path ran no CI at all.
- **The publish-budget script's port default was dead** — `check-publish-budgets.ps1`
  defaulted `-Port` to 5999 while its only caller, ci.yml, passes 5998. Running it by hand
  therefore used a port the pipeline never touches. Now 5998, with `PublishBudgetScriptTests`
  reading both files so the two cannot drift apart again silently.
- **Every render diagnostic pointed at nothing** — NWT001–NWT015 were all raised with
  `Location.None`, so a template mistake produced an error with no file attached: nothing to
  click in a build log, and an IDE squiggle on the project rather than the line. Templates are
  `AdditionalText`s with no syntax tree to hang a location on, but `Location.Create` takes a
  path and a span directly. The parser now carries character offsets — through the header, the
  body tokens and onto the nodes themselves — so parse-stage findings point at the token that
  caused them, counting the header lines and surviving a BOM and CRLF endings; findings that
  only emerge after parsing (layout not found, a class-name collision) point at the file, which
  is still a path an editor can open.
- **`@param` types went into the generated signature unchecked** — `@param` splits on the last
  space, so `@param My Model model` took `My Model` as the type and copied it verbatim into the
  render method. The author got a C# syntax error naming a line in a file they had never
  written. It is NWT003 against the template line now. The check is a plausibility filter, not
  a type parser: generics, arrays, nullable annotations and `global::`-qualified names all pass,
  and a well-formed name that does not exist stays the compiler's to reject, which it words
  better than we could.
- **An expired 2FA challenge swapped the sign-in page into the 2FA card** — `POST
  /auth/2fa/verify` answered a missing or expired challenge cookie with a bare
  `Results.Redirect`, and that form opts into htmx via `data-nw-target`. htmx chases a 302 by
  XHR and swaps the destination's markup into the fragment target, so taking too long over an
  authenticator produced a whole sign-in document nested inside the 2FA board rather than a
  navigation. It now goes through `Redirects.To`, which sends `HX-Redirect` to htmx and the
  real 302 to a plain browser. Eight further bare redirects across the register, passphrase,
  2FA and WebAuthn GET handlers were converted with it: those are latent rather than live only
  because nothing links to them yet — `boot.js` derives `hx-get` from any anchor carrying a
  `data-nw-*` attribute, so one added link is all it would take. `Redirects`' own comment now
  says which redirects are exempt (OAuth's navigation to a client's `redirect_uri`) instead of
  reading as post-success-only advice.
- **A hung NativeAOT publish killed the e2e job with no explanation** — `AppProcessFixture`
  waited on `dotnet publish` forever and drained its two pipes in sequence, so a full output
  buffer could deadlock the child outright and any CI timeout that eventually fired reported
  nothing about what had been running. The wait is now bounded (30 minutes — 15 was tried first
  and failed the whole suite on a healthy build, because a cold publish here measures 15m47s and
  the ceiling has to sit above the normal case, not near it), both streams are
  drained by event so neither can block the other, the last output lines survive into the
  exception message, and the child is killed with `entireProcessTree: true` — `dotnet publish`
  is a launcher, so killing it alone leaves compiler and linker children holding the output
  directory. `ProcessRunTests` covers all three exits, and asserts the timeout returns *near*
  its budget, since without the cancellation reaching the wait the test would still throw —
  after the publish finished — and pass for the wrong reason.
- **Legacy JavaScript MIME types went unhashed, so a CSP blocked its own scripts** —
  `IsExecutableScript` recognised five `type` spellings; the HTML standard names sixteen. A
  script typed `text/jscript`, `application/x-javascript`, `text/livescript`, `text/javascript1.5`
  or an `x-` variant still executes in every browser, but got no hash, so on a nonce-free public
  page it was blocked by the very policy meant to allow it — in production only, with the whole
  suite green. The full set is now carried, transcribed independently on both sides so the
  lockstep can still disagree with itself, and pinned by `templates/csp/legacytypes.html`, which
  keeps a `text/template` control in the same fixture that must stay unhashed. Last of the three
  2026-08-03 CSP review findings.
- **Cleanup scripts rejected the `--execute` spelling their own headers documented** — PowerShell
  strips one leading dash, so `--execute` and every second flag bound to nothing and died with "a
  parameter cannot be found". Twelve of the fourteen scripts in `scripts/cleanup/` had the gap;
  all now carry the `ValueFromRemainingArguments` shim, so `-e` and `--execute` are one flag in
  both shells and an unknown argument exits 2 rather than being absorbed. Two of them —
  `remove-webauthn-scratch-scripts.ps1` and `remove-scratch-inspect-tool.sh` — predated the
  interface entirely and deleted on a bare invocation with no dry run at all; both now print by
  default, gate the deletion behind `--execute` plus a second named flag, and refuse to touch a
  path that turns out to be tracked or a `TEMP` that is not a directory.
- **A commented-out script authorized itself in the CSP** — `InlineScriptHashes` searched the
  emitted bytes for a literal `<script` with no idea what HTML context it sat in, so static text
  a browser never treats as markup was hashed into `script-src` anyway: inside a comment, inside
  a raw-text or RCDATA element (`textarea`, `title`, `style`, …), and inside another tag's
  attribute value. Each is an allowlist entry for a script the page does not ship, and the
  commented-out case is the one that happens — comment a script out while debugging and the page
  keeps authorizing those exact bytes. `Phase.Outside` now walks tags instead of searching for a
  literal, carrying comment / raw-text / in-tag states across node boundaries; it is deliberately
  not a tokenizer, so anything it does not recognise stays content and can only cost a hash,
  never add one. The class comment's claim that "nothing here can over-authorize" — true only of
  dynamic content — is corrected.
  Found because the backstop the file names did not exist: both `SmokeTests` and the render tests
  compared the generated constants against the CSP header built from those same constants, so a
  scan that misframes a body agrees with itself and the suite stays green while the browser blocks
  the page. Two real lockstep layers replace it — an AngleSharp theory over every `Csp/*` fixture
  asserting each emitted hash matches an executable inline script the parser actually found, and
  the skeleton's app-level test recomputing the hashes from the served response and asserting set
  equality with the header. Reverting the scanner fails the first on all three shapes; injecting a
  stray hash into the policy fails the second, verified against a stamped product.

- **Debug builds served every static asset empty to a gzip-accepting client** — in Development
  `MapStaticAssets` re-stats each asset per request via `StaticAssetDevelopmentRuntimeHandler`,
  which resolves it as wwwroot + route; the SDK writes the precompressed variants under
  `obj/<config>/<tfm>/compressed/` instead, so every one threw `FileNotFoundException` and any
  real browser got HTTP 200 with a zero-length body and no `Content-Type`. `Directory.Build.targets`
  disables build compression in Debug only (publish is untouched and still compresses); tests are
  written against `Accept-Encoding` rather than the build property, so they fail on the symptom
  whichever layer regresses.
- **V9 pre-existing findings** — the e2e app fixture redirected the app's stdout/stderr and never
  drained them, so a full pipe buffer would block the app on its next write and hang the suite
  rather than fail it; it now drains, keeps a 200-line tail reported from the startup timeout,
  bounds its kill-and-wait so the diagnostic path cannot itself hang, and cleans up on every exit
  path. `/readyz` is bounded by a 1s `ReadinessCache` — collapsing concurrent probes onto one
  round trip — rather than by a rate limit, which would have let anyone spend the shared partition
  and make the balancer's next probe 429 a healthy instance out of rotation. `Directory.Build.props`
  lost a `JauntyQUsePackages` auto-default that could never fire and now probes the JauntyQ
  generator csproj instead of the bare directory. CI pins `ilammy/msvc-dev-cmd` to a commit, and
  the e2e step gained the clang/zlib prerequisites and the `JauntyQUsePackages` env var its inner
  publish needs — both unverifiable until the package-feed gap is closed. A follow-up Fable
  review then found the cache was reading `DateTimeOffset.UtcNow`: a backward clock step (NTP
  correction, VM resume) extends the window by the size of the step, so a dead database would
  have kept reporting ready for exactly as long as the guarantee said it could not. The clock is
  now monotonic and typed as elapsed-since-an-origin so a wall clock no longer compiles; the
  fixture's `DisposeAsync` goes through the same hardened stop path as the failure paths rather
  than repeating the unguarded kill it documents as unsafe; and the log tail is its own type so
  its bound is tested by pushing past it rather than by hoping the app logged enough.

---

## [0.1.0] — <date>

### Added
- (nothing yet)
