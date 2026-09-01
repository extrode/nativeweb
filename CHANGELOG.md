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
- **The docs site is versioned and committed.** `scripts/build-docs.sh` passes `--versioned` to the
  shared Docs generator, so `docs-site/` is a root holding one frozen site per release rather than a
  flat directory overwritten on every build — and unlike every other consumer of that generator, it
  is tracked. A release's documentation survives without anyone rebuilding it, and an old version is
  rebuilt by checking out its tag: the generator reads `git describe --exact-match` on this repo and
  refuses to label an untagged tree, so nothing can claim a version this repo never cut. There is no
  semver tag yet, so today's build is `unversioned/`. 14 pages, 519 KB, 11 dropped links — all of
  them paths outside `docs/` that a flat site cannot serve, which the build lists on every run.
  `tools/docs-site/check.ts` asserts the committed site is intact: the manifest parses, every
  version it names is on disk with its stylesheet, and nothing in the site references a CDN.
- **`libs/nativeweb.signin` — the client half of OAuth, as framework.** A product signs users in
  against any conforming issuer without writing the relying party itself: authorization-code with
  PKCE, an opaque `__Host-` session cookie over an `ISessionStore` the product implements, silent
  refresh with compare-and-swap, sign-out, and a bearer-introspection endpoint filter with a
  short-TTL cache. It registers a real authentication scheme, so a scaffolded route's
  `RequireAuthorization` works in a stamped product, which it did not before. Provider-agnostic
  and configuration-driven — no token issuance, no consent screen, no realm resolution and no
  client registry: NativeWeb is the client, never the server. NativeAOT-clean, with unit,
  integration and Playwright tiers plus an AOT publish probe.

### Fixed
- **The deploy chain has now released to a real host, and doing it found two defects in the
  chain.** `apps/sample` is serving at `https://nw-dev.example.com` — built by `build.sh` on a
  Linux box, promoted to blue by `deploy.sh` in **1.774 s**, `/readyz` 200. Both defects are
  fixed with tests. `ConditionPathExists` sat in `[Service]`, where systemd logs "Unknown key
  name" and ignores the line, so the silent-skip a colour with no release on disk was supposed to
  get had never happened on any host for as long as the unit file has existed. And the release
  directory's mode was set subtractively with `chmod -R g-w,o-rwx`, which cannot restore group
  bits that `cp -a` from a `mktemp -d` stage never granted — the directory arrived `drwx------`
  and systemd failed the unit at `status=200/CHDIR`. Spec 011's R1 is met; R16 and R17 still are
  not, and the runbook now carries the measured figures rather than `NOT YET MEASURED`.
- **The deploy chain reads its configuration prefix off the artifact instead of guessing it.**
  `scripts/deploy/`'s preflight demanded ten keys spelled with a `NativeWeb` prefix: nine are read
  by no code in this repository — they left with the identity provider — and the tenth named a
  section no binary binds, so a deploy of the scripts' own default project invented values for a
  departed product while the two checks that mattered passed with their settings unset.
  `build.sh` now stamps `config_prefix` and `migrations_dir` into the artifact's `RELEASE`,
  `deploy.sh` derives every key from the stamp, `provision.sh` takes `--prefix`, and overriding
  `PROJECT` without the other three is an error. The telemetry gate was inverted, asserting an
  opt-out posture the binder cannot produce.
- **Data protection keys no longer land in a directory nothing can write.** `NativeWebOptions`
  composed the key path from `Environment.SpecialFolder.UserProfile`, which is empty for a systemd
  unit whose user has no home directory; `Path.Combine` dropped the segment and the result was
  relative to `WorkingDirectory`, read-only under `ProtectSystem=strict`. Keys were regenerated on
  every restart and every form POST after a deploy answered 400. It now refuses to start, and
  `deploy.sh` checks the configured directory sits under `/var/lib/nativeweb`.
- **The scaffold generator no longer emits code the consumer cannot compile.** `ScaffoldEmitter`'s
  default arm assigned a string to any type it did not recognise — `byte`, `ushort`, an enum,
  `DateOnly`, `TimeSpan`, a consumer's own type — which is CS0029 in generated source on a model
  that had done nothing wrong. Unconvertible types are now display-only. Indexers and properties
  without a public setter reached the emitter as well and produced CS0200.
- **The reference store stopped reporting 404 for a row that exists.** `DemoStore.UpdateAsync`
  used a single `ConcurrentDictionary.TryUpdate`, which compares by reference and so failed when a
  concurrent save had replaced the value; `IScaffoldStore.UpdateAsync` defines `false` strictly as
  "no such row" and the handler maps it to 404, so two admins saving the same record left the
  loser told it did not exist.
- **`skeleton/db/migrate.sh` carries the guards this repo's runner has.** It was a 97-line copy
  missing the advisory run lock, the `is_tracked` failure guard and the atomic transaction wrap, so
  every stamped product shipped with the runner the fixes had never reached. It is now generated
  from `db/migrate.sh` and held there by line-by-line parity.
- **The remote deploy path never read the stamp the fix above added.** `deploy.sh` re-executes
  itself on the far side of both transports and forwards `MIGRATIONS_DIR` unconditionally, so the
  host half always saw it set — to the empty string — and a `+` test counted that as an override.
  Every `--host` and `--incus` deploy would have died at the migrate step; only `--local` reached
  the stamp. Both scripts now capture with `:+`.
- **Every fresh stamped product would have failed its first migration.** The runner named
  `0002_extensions.sql` while `skeleton/db/migrations` ships `0001_extensions.sql`, so the file
  fell through to the main loop and was applied as the app role, which cannot `CREATE EXTENSION`.
  The superuser step is found by pattern now, not by number.
- **The key-directory guard did not fire on the host it was written for.** A system account
  records a home in `passwd` that was never created, so `UserProfile` comes back non-empty and the
  emptiness test passed; the ring resolved under a directory nothing can write, which is the
  silent 400 the guard exists to prevent. The fallback is probed rather than inferred — and the
  probe requires the home directory to already exist, because a process with enough privilege
  creates `/nonexistent` happily and would have passed a probe that created what it tested.
- **A second extensions migration would have been applied as the app role.** Discovering the
  superuser step by pattern fixed the wrong-number case but took the first match, so a schema that
  adds an extension after launch hit the same failure with a later number on it. That directory is
  refused with a message; `EXT_VERSION` holds one version, and half-supporting two is worse than
  saying so.
- **Five tests whose comments claimed more than their assertions proved.** Each asserted that a
  substring appeared somewhere in a file, which stays true when the branch containing it is
  deleted. They now parse the structure they name — branch slices, line-by-line parity, a derived
  value compared against the source it derives from — and each was mutation-checked against the
  change it claims to catch.
- **The four coverage gaps the 021 audit named, closed — two of them with a different answer than
  the audit assumed.** The generator's emitted source is now compiled at pinned language versions
  rather than only at the toolchain's default, and the floor turns out to be **C# 11**, not the
  C# 10 the audit proposed: `ScaffoldRuntime.g.cs` emits a raw string literal and the form binder
  implements `IBindableFromHttpContext<T>`, whose `BindAsync` is a static abstract interface
  member. The generated update handler's second 404 — `UpdateAsync` returning false after
  `GetAsync` already returned the row — is executed rather than read out of the emitted source, via
  a stub store that makes the concurrent delete deterministic. A refused configuration is proved at
  the process boundary: exit 78, the key named on stderr, and no modal block, which is the
  NativeAOT failure mode `lessons/2026-07-26` records. The fourth gap, `[ScaffoldSearch]` wiring,
  **was already covered** — emitting an empty `SearchFields` set turns two integration tests red,
  measured by mutation rather than assumed. Every new test was mutation-checked.
- **A missing static error shell now exits 78 instead of crashing out of `Main`.**
  `StaticErrorShell.Load(IWebHostEnvironment)` is the overload `NativeWebPipeline` calls while
  building the pipeline, so its `InvalidOperationException` escaped `Main` and the host died with
  `0xE0434352` — under NativeAOT on Windows that is the modal box `StartupConfigGuards` exists to
  prevent, which a supervisor reads as a hung service rather than an error. It now catches that
  and `IOException` and routes both to `FailStartup`, so a missing or unreadable
  `wwwroot/pages/error.html` leaves by the same stderr message and exit code as a refused
  configuration value. The `IFileProvider` overload still throws and stays the unit tier's.

### Changed
- **The epass.io cutover is closed as a NativeWeb concern, and the term is retired here.**
  Checked against the real sibling checkout on 2026-08-31 rather than taken on report: epass.io has
  built and merged its own browser surface and is ahead of this repo on it, consumes only
  `libs/nativeweb.render` and `libs/nativeweb.security`, and uses nothing the identity provider's
  departure deleted from either — it carries its own `PairwiseSubject` and `Pkce`. The render
  library's public API has not moved since the `NATIVEWEB_REF` it pins, and that pin is reachable
  on `origin/dev` today, so epass.io is not blocked on anything here. Recorded in
  [decision 008](docs/decisions/2026-08-31-008-epass-cutover-closed.md); `work/status/current.md`,
  `work/milestones.md` (milestone 4 → shipped), `work/tasklist.md`, `work/todo.md`,
  `docs/02-reference/deployment.md` and spec 011's non-goals all updated to stop presenting it as
  open. The one outstanding input is a push of `dev`, which is the user's.

### Added
- **OAuth sign-in as a framework primitive (`libs/nativeweb.signin`).** The relying-party half of
  OAuth 2.1 / OIDC, provider-agnostic and configuration-driven: the authorization-code grant with
  PKCE, an opaque server-side session behind a `__Host-` cookie over a store the product supplies,
  token refresh, sign-out, and a bearer-introspection filter with a short TTL cache for API
  endpoints. It registers a real authentication scheme, which is what a scaffolded route's
  `RequireAuthorization` needed and did not have — before this a protected route in a stamped
  product answered a bare 401 with no way to sign in. NativeWeb is the client and never the
  server: no issuer, no consent screen, no realm resolution, no client registry, and no provider
  named anywhere in the shipped source, which
  `tests/nativeweb.repo.tests.unit/ProviderNeutralityTests.cs` asserts over
  `libs/nativeweb.signin`, `libs/nativeweb.signin.testing` and `skeleton`.
  Configuration is read key by key from an `IConfiguration` section rather than bound, because a
  reflecting binder is the one dependency that would stop the library publishing AOT-clean;
  `tests/nativeweb.signin.tests.aot` publishes a probe host and fails on any IL2xxx/IL3xxx
  warning, measured at 21 s with none. The skeleton stamps the section and registers the scheme
  only when a provider is configured, so a product stamped before it has one still boots.
  Documented in `libs/nativeweb.signin/README.md`; `apps/sample` runs the whole round trip against
  a demo authorization server it hosts itself, so `dotnet run` needs no external service.

- **The framework web seam (`libs/nativeweb.web`).** One `AddNativeWebWeb` + `UseNativeWebPipeline`
  + `MapNativeWebEndpoints` composes what every NativeWeb product used to copy into its own
  `Program.cs`: Kestrel limits, forwarded headers, the pooled data source, telemetry, data
  protection, antiforgery, bot traps, rate limiting, the public-page output cache, the middleware
  order, `/healthz`, `/version`, `/readyz`, `/csp-report` and `/.well-known/security.txt`. The
  skeleton's entry point went from 487 lines to 39 code lines, held there by a ceiling test, and a
  security fix now reaches a stamped product by upgrading a library. Nothing is hidden: every step
  is a public extension method.
- **Framework static assets are delivered by MSBuild.** `build/NativeWeb.Web.targets` contributes
  `wwwroot/_nativeweb/**` as linked `Content`, fingerprinted and precompressed into the consumer's
  publish output by the static web assets pipeline. Served under `/_nativeweb/`, because two assets
  at one target path is a hard build error rather than a precedence question.
- **An authentication slot in the pipeline.** `UseNativeWebPipeline(locales, auth => …)` runs the
  callback in front of antiforgery validation, which is the only position where an authenticated
  form post validates — who may reach a route stays the product's decision.
- **`apps/sample` renders through the framework's own compiled templates**, styled, with the
  article filter posting over htmx and swapping only the results list. The same form without
  JavaScript posts to the same route and gets the whole page back, through one code path.
- **`docs/02-reference/performance.md`** — every load measurement taken here, the control scenario
  beside each one, and the bottlenecks found.
- **One command for the edit-and-see-it loop**, in the README and the quick start:
  `dotnet watch run --project apps/sample --no-hot-reload`.

### Changed
- **Compression moved from the app to Caddy**, measured rather than assumed: through the proxy every
  product is deployed behind, `encode zstd gzip` served the reference page at 9,002 req/s where the
  app compressing it managed 6,255, and the composed pipeline recovered `articles` from 6,440.5 to
  16,528.8 req/s overall. Compressing in-process also stripped the `Content-Length` the renderer
  declares and re-ran on every output-cache hit. `AddNativeWebResponseCompression` stays public, so a
  host with no compressing proxy adds one line. `docs/02-reference/performance.md` bottleneck 4 has
  the five-variant A/B.
- **`/version` and the telemetry service version take the product's assembly as a parameter**
  (`typeof(Program).Assembly`) instead of loading it by name from `IHostEnvironment.ApplicationName`.
  The name lookup does resolve under NativeAOT — the published binary answered with the stamped
  value — but it answered a rename with a silent fallback rather than a compile error.
- **Antiforgery relaxes to `SameAsRequest` and drops the `__Host-` prefix under Development.**
  `Always` over plain http 500s the first page carrying a form, which broke `dotnet run`, every
  loopback host and the Playwright tier at once. Driven by `IHostEnvironment`, so no settings file
  can select the weak value.
- **The bench baseline is re-recorded for the pipeline** (`articles` 18,958.6 → 6,440.5 req/s).
  Response compression is the whole of the loss and `docs/02-reference/performance.md` bottleneck 4
  bisects it; a baseline that is red on every run gates nothing.
- **Template pages declare `Content-Length`.** `TemplateResults.Html` buffered the whole body and
  then said nothing about its length, making every page a chunked response and costing 35% of
  throughput: 11,779 req/s against 18,105 for the same bytes. Nothing was ever being streamed.

### Fixed
- **The deploy chain named an application that had left the repository.**
  `scripts/deploy/build.sh` published `apps/web/nativeweb.web.csproj`, so every run failed on a
  missing project file. It now publishes `apps/sample` by default, takes `PROJECT=<csproj>` for a
  stamped product's own app, and installs the executable under the fixed name `nativeweb` —
  `deploy.sh`, `nativeweb@.service` and the Caddy files name no project at all. systemd will not
  expand a variable in the first token of `ExecStart`, so one rename in the build is what buys a
  unit file nobody has to edit per product.
- **The migration runner pointed at a deleted directory and demanded a migration that need not
  exist.** `db/migrate.sh` resolved `db/migrations/` and named `0002_extensions.sql`
  unconditionally, so it aborted on every run and took `db/test-run-lock.sh` with it. It now
  defaults to `db/sample/migrations`, takes `MIGRATIONS_DIR`, refuses a missing or migration-less
  directory before any glob reaches `psql`, and applies the superuser extensions step only if the
  schema has one.
- **The OTel-under-AOT budget gate had stopped gating.**
  `scripts/ci/check-publish-budgets.ps1` set `NativeWeb__Telemetry__Enabled`, which was right while
  the target was `apps/web`; `apps/sample` binds its options from the `Sample` section, so the key
  was unread and the OpenTelemetry registration — the only one that runs inside a real NativeAOT
  image — never ran. Nothing was red. Measured on the published binary:
  `Sample__Telemetry__Enabled=maybe` refuses startup with exit 78 while
  `NativeWeb__Telemetry__Enabled=maybe` starts the app normally. Fixed with `-ConfigPrefix`.
- **A rejected form came back showing values nobody typed, under the wrong heading.** The error
  path rebuilt the page from the converted item, so "Views must be a whole number." sat next to a
  field reading `0` — the fallback the validator had just refused — and a rejected *create* was
  titled "Edit Article" for a row that had never existed. The submitted form is now carried down
  and rendered; the create path passes no row.
- **A store that returned no key made a create look successful.** The empty string produced a
  redirect to `BasePath + "/"`, which is the list route, so the visitor landed on a page that did
  not contain their row. The contract says the implementation assigns the key; a store that breaks
  it now gets an `InvalidOperationException` naming it.
- **A nullable `bool` property emitted `row is not null && row.Flag`** — `bool?` and `bool` under
  `&&`, which does not compile. Now `row?.Flag == true`.
- **`SearchFields` was emitted as a collection expression.** Generated source is compiled with the
  *consumer's* `LangVersion`, so a product on C# 11 got source that would not build. Emitted as an
  explicit array.
- **`DemoStore.UpdateAsync` was check-then-act on a `ConcurrentDictionary`.** It tested
  `ContainsKey` and then assigned through the indexer, so a row deleted between the two came back
  and the handler reported success — the exact race the new 404-on-false exists to report. Now
  `TryGetValue` plus `TryUpdate` against the row it read.
- **The generated admin index listed every model to everyone.** Each per-model route was guarded and
  the index was not, so an anonymous visitor read off the name and URL of every guarded model and
  was refused only on the click. A link is now emitted only when the caller satisfies that model's
  read requirement, and the index itself requires an identity when every model does. The default
  posture for a model with no `[Auth]` is unchanged — whether it should invert is D2 in the
  readiness plan, still open.
- **`short` and `float` properties generated code that did not compile.** Both fell through the
  conversion table to the string case, so `short Quantity = ScaffoldValues.Text(…) ?? string.Empty`
  — CS0029, on a model that declared nothing unusual. Nullable `double` and nullable `Guid` failed
  the same way. A test now compiles the generator's output for every supported type, which also
  found the generated files relying on `ImplicitUsings`: they now state their own usings.
- **`[ScaffoldSearch]` reached the search box's placeholder and nothing else.** `IScaffoldStore`
  takes the searchable property names on `ListAsync`/`CountAsync`; before this every store
  re-declared the searchable columns by hand, and removing the attribute changed the placeholder
  while leaving the column searched.
- **Malformed numeric, `Guid` and `DateTime` input was silently coerced.** "abc" in a number field
  was stored as 0 and "not-a-date" as the current instant, answered with the same 302 a valid
  submission gets. It is now an error shown on the form. Blank still takes the documented fallback.
- **`DeleteAsync` and `UpdateAsync` returning false were discarded**, so deleting a row that was not
  there — or saving one deleted between the load and the save — redirected as if it had worked. Both
  answer 404.
- **`?page` was clamped low and not high**, so a page number in the hundreds of millions overflowed
  `(page - 1) * PageSize` to a negative offset from an anonymous GET.
- **The key `CreateAsync` returns was discarded.** The contract now states that the store assigns a
  unique key, and the generated create redirects to the key it returned, so a store handing back a
  stale or empty one fails visibly instead of overwriting a row later.
- **The pooled `NpgsqlDataSource` was registered as a built instance**, so the container never
  disposed it: the pool outlived the host and connections closed by the socket rather than by
  Npgsql. A factory registration is the fix.
- **`Telemetry:Enabled` read `1`, `yes` and `on` as off.** An equality check against `"true"` meant
  an operator could believe telemetry was exporting when it was not; an unparseable value is now a
  named startup error.
- **The scaffolded admin could not render under a nonce CSP.** Its delete confirmation was an inline
  `onsubmit` no policy without `unsafe-hashes` will run, and its inline `<style>` carried no nonce.
  Only the Playwright tier could see it, and did: "the delete form did not raise a confirm dialog at
  all".
- **`work/milestones.md` claimed nothing compiles the skeleton.** CI's `skeleton-stamp` job stamps
  a product and builds and tests all three of its tiers. The real gap is narrower: no *local* build
  does, so a drift is a CI-only finding.
- **The `apps/sample` launch profile opened `/signin`** rather than the feed the reference
  application exists to show.

### Removed
- **The identity provider left NativeWeb.** `apps/web`, `libs/nativeweb.auth`,
  `libs/nativeweb.data`, `libs/nativeweb.signin.epass`, `.signin.epass.testing`, `db/migrations`,
  `db/queries`, `db/schema`, `db/seed` and seven test projects are gone: 622 files, 69,322 lines.
  NativeWeb's charter said it was "both a reusable reference template and the full ground-up
  redesign of epass.io", and that second half is how a token issuer, a consent screen, realms, SSO
  modes and a users-and-tokens schema came to live here under framework names — the 2026-08-19
  neutral-naming rename turned `libs/epass.auth` into `libs/nativeweb.auth` without one line of it
  becoming framework code. **epass.io is its own repository and owns epass**; the copy here was a
  diverged fork and was not ported anywhere. What each side had that the other did not — nine
  migrations, one source file, eighteen server-rendered pages — is tabled in
  `docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md`, which is the index for anyone
  cherry-picking from history later. `libs/nativeweb.security` kept its framework primitives and
  lost `Pkce` and `PairwiseSubject`; `libs/nativeweb.mail` kept its SMTP transport and lost the two
  senders that implemented auth interfaces. `apps/sample` is now the reference application and the
  only deployable project. Solution builds 0 warnings / 0 errors and 444 tests pass afterwards,
  across seven suites — the seventh being `tests/nativeweb.repo.tests.unit`, a new home for the
  repository-level guards that had been living in the deleted tier and would have gone with it.
- **The `Fido2` and `NSec.Cryptography` central pins**, which the extraction left without a single
  `PackageReference`. WebAuthn is an identity-provider concern and lives in epass.io. A comment
  stands where they were: reintroducing `Fido2` means reintroducing the `NSec` pin with it, since
  `Fido2` floats a minimum on NSec 25.4.0 whose libsodium range conflicts with Geralt's exact
  `[1.0.22, 1.0.23)` and fails restore with NU1107.

### Added
- **Declarative form binding for hand-written endpoints** (spec 014). A `[Form]`-annotated
  partial record gets a source-generated binder, and `MapFormPost<TForm>` (with `Put`, `Patch`
  and `Delete` siblings) maps the route with the 415 content-type guard, the antiforgery metadata
  and the accepts metadata attached by construction. A field the model does not declare cannot
  reach the handler. `NWF010` makes a `[Form]` model on a plain `Map*` a build error, because that
  shape does not merely lose the guards — the Request Delegate Generator cannot see a binder
  another generator emitted, so it binds the model as a JSON body and routing answers every form
  POST 415. Both `/auth/register` POSTs are converted with no assertion edited.
- **Deploying behind a TLS-terminating edge.** `provision.sh --edge <addr>` installs
  `scripts/deploy/caddy/Caddyfile.edge` — `auto_https off`, listening on `:80`, trusting the edge
  for `X-Forwarded-*` — instead of the variant that holds `:443` and runs its own ACME. The
  blue/green promotion is unchanged: both configs import the same `/etc/caddy/upstream.caddy`,
  so `deploy.sh` and `rollback.sh` do not know which mode they are running under.
- **`deploy.sh --port <n>`** for a host whose sshd is not on 22. Validated before anything is
  copied, and reaching both `ssh` calls as `-p` and both `scp` calls as `-P` — the two spellings
  differ, and a deploy where they disagree ships the artifact to one host and re-executes on
  another. Refused with `--incus`, which opens no connection a port could apply to. Omitted,
  `ssh_config`'s `Port` for the host still applies.
- **`deploy.sh --incus <instance>`**, a transport for a guest that runs no sshd because it is
  managed through `incus exec`. `incus file push` for the artifact and this script, `incus exec`
  to re-enter as `--local`. The `--host` ssh path is untouched.

### Fixed
- **`NWF010` no longer fires on somebody else's `MapPost`.** It matched by method name alone, so a
  project's own routing table — a message bus, a CLI verb table — registering a handler that
  happened to take a `[Form]`-annotated record was a build error with no available fix. The
  receiver must now be, or implement, `Microsoft.AspNetCore.Routing.IEndpointRouteBuilder`.
- **The Deno-tooling guard no longer races the tests running beside it.** Its walk used lazy
  enumeration, which throws when a directory is removed mid-iteration, and it descended into
  `tmp/`, which is where every other test in the tier writes scratch. It failed once in a
  full-suite run and passed on the immediate re-run — the shape of flake that gets a real rule
  muted.
- **Three silent deaths in the deploy scripts.** Under `set -euo pipefail` a command substitution
  whose pipeline fails ends the script at the assignment, before the `die` written to explain it.
  A mistyped `--incus <instance>` exited 127 with no output at all; a guest with no sshd — the
  fleet's normal case — died before the message about deriving the firewall's SSH allowance. Both
  now reach their message. A failed `incus file push` no longer skips the cleanup and leaves a
  partial artifact in the guest.
- **The firewall install no longer fails open.** `/etc/nftables.conf` was written and the service
  enabled before `nft -f` proved the ruleset parses, so a bad render left the host with no ruleset
  at all after a reboot. It is now checked with `nft -c` before anything is installed.
- **`provision.sh --edge` restricts the public surface.** It changed the Caddyfile and the app
  config but left the ruleset accepting `{ 80, 443 }` from any source, while the config's own
  comment said only the edge could reach it. Edge mode now renders
  `ip saddr <edge> tcp dport 80 accept`, and says so when it cannot.
- **The 503-degrade is tested, and a comment about it corrected.** `RequireServicesFilter` — the
  filter that keeps a fresh checkout runnable before any secrets exist — had no test in either
  tier. Ten now cover it, including that an unconfigured box still rejects a form POST with no
  antiforgery token. Measurement while writing them showed the filter's own comment was wrong:
  the 415 on both register routes comes from the Request Delegate Generator's content-type check
  during binding, not from `RequireForm()`, which is redundant on both its call sites and kept
  only as defence in depth.
- **Client IPs survive two proxy hops.** `ForwardedHeadersOptions.ForwardLimit` was never set, so
  it defaulted to one hop. Behind an edge, `X-Forwarded-For` arrives as `<client>, <edge>` and one
  hop leaves `RemoteIpAddress` on the edge — collapsing every client in the world into a single
  rate-limit partition, shared with the health probe, with nothing logged. Configurable as
  `NativeWeb:ForwardedHeaders:ForwardLimit`; the default is unchanged at 1, and `provision.sh`
  seeds it together with `KnownProxies` because neither setting does anything without the other.
- **The host firewall no longer hardcodes SSH port 22.** `scripts/deploy/nftables/nativeweb.nft`
  now carries `define ssh_ports`, and `provision.sh` rewrites it from `sshd -T` before installing,
  so the ruleset opens whatever sshd is bound to. Found on `web-host`, where sshd listens on
  2222 and 22 is closed upstream: provisioning would have dropped the operator's own connection
  mid-step, on a host with no console failover. The script now refuses to install rather than
  guess if `sshd -T` reports no port or the substitution does not take.
- **Nineteen defects from a two-lane audit sweep**, each confirmed by a refutation pass before
  being fixed. Eight in `scripts/deploy/**`, which has never run against a real host and where the
  first four each stop the runbook dead on a clean Debian: Caddy never reading the environment file
  `provision.sh` writes, an access-log directory owned by the wrong user, an nftables guard that
  skipped on every first provision because Debian's own package ships the file it checks for, and
  `sudo VAR=value` under `env_reset`. Also a disaster-recovery restore that could only find backups
  under the dead host's name (`restore.sh --from-host`), a `pg_hba` map that let the production
  overwrite authenticate but not the drill, a `db/migrate.sh` that was mode 100644 in the index,
  and a systemd template that enabled both colours before either existed.
- **Ten drifts between `skeleton/` and `apps/web`**, every one a fix the real app made and the
  template never took, so every stamped product inherited it. `skeleton/` is in no solution, so
  none of them broke a build: a failed antiforgery check answering 500 rather than 400, an
  unguarded `AddUserSecrets` outranking a deployment's own environment variables, `/readyz`
  spending a pooled connection per anonymous request, OpenTelemetry exporting unredacted query
  strings and no service resource, startup errors thrown rather than exited (a fail-fast that hangs
  a Windows service on a modal box under NativeAOT), unlimited `security.txt` routes, a bare
  antiforgery cookie with no `__Host-` prefix, JauntyQ pinned a minor behind, a single unguarded
  `JauntyQRoot` probe that breaks every worktree build, and three references naming other products.
  `tests/nativeweb.web.tests.unit/SkeletonParityTests.cs` is the first parity coverage in the repo.
- **`tools/audit`'s `assertionless-test` check read a project-local `Assert*` helper as no
  assertion**, reporting 52 of this repository's own tests. The daily audit was spending its
  refutation budget on them; the mechanical sweep now yields 83 candidates across 130 slices
  instead of 124.

### Security
- **The 2026-08-19 audit backlog on `apps/web` is worked through.** Seven fixes, each with tests
  and a mutation that kills its own fact; six more findings turned out to have been fixed by
  earlier sessions and were verified rather than re-fixed. One is left open on `work/todo.md`
  because every fix for it is a design change: `GET /oauth/authorize` is a state-changing GET
  under a `SameSite=Lax` session cookie.
  - **CSP `form-action` is widened for the OAuth consent screen**, to the origin of the
    `redirect_uri` it has already matched against the application's registered list. The directive
    is enforced against a form submission's whole redirect chain rather than its first hop, so
    `'self'` was blocking the no-JS consent path whose decision POST 303s to the client. The
    header names an origin, never a path, and every other page stays at `'self'`.
  - **The passphrase-reset masked destination is signed** rather than round-tripped through the
    client, on the one unauthenticated page whose job is telling a user where a takeover code
    went. An `ITimeLimitedDataProtector` over the code's own 24h window carries it; a token that
    does not verify drops both the displayed text and the hidden field. Signed rather than
    server-derived on purpose — deriving it from `temp_codes.sent_to` would have built a new
    enumeration oracle, since a real account's mask would change once its code expired and an
    unknown identifier's never would.
  - **Application `imageUri`, `website` and `companyWebsite` are parsed as URLs**, not just
    length-checked. Absolute, http or https only, cleartext accepted for a loopback host and
    nowhere else, no `user:pass@`. `imageUri` is rendered as `<img src>` on the public consent
    screen, where `default-src 'self'` was the only thing making stored `javascript:` inert.
  - **`POST /csp-report` requires `application/csp-report` or `application/reports+json`** and
    answers 415 otherwise. No origin check, deliberately: `report-uri` sends no `Origin` and
    `report-to` sends the violating document's, which is `null` under a sandbox.
  - **`ErrorPageMiddleware` and `SecurityHeadersMiddleware` now wrap the clean-URL rewrite and the
    router.** An exception thrown in either previously escaped both the error shell and the
    security headers. The headers survive the move because they are written from an `OnStarting`
    callback, so the static-page marker set downstream is still readable when it runs.
  - **The antiforgery token in `<meta content>` goes through `HtmlEncoder`** like every other
    interpolation in the Render layer. It changes no byte of today's output — the token is
    base64url — which is the point: it was the one request-derived value held safe by a
    framework's choice of alphabet rather than by the encoder.
  - **Fido2NetLib's HTML escaping is pinned by a test**, not by a comment. Three pages interpolate
    its options JSON raw into a `<script type="application/json">` block carrying a user-chosen
    username; a release switching to the relaxed encoder would have turned that username into
    markup with every suite still green.

- **The two-lens review of that branch found seven more, all fixed on it.** Four were holes in
    the fixes themselves: the `POST /csp-report` 415 was body-less, so `ErrorPageMiddleware`
    filled it with the 849-byte HTML error page on the one route whose caller is a browser's
    reporting agent (`ErrorPageMiddleware.SuppressItemKey` is the opt-out, and 415 gained a
    `Describe` arm for the form routes that do want a page); the `form-action` widening was
    interpolated into the header with no validation, so the middleware now validates the one
    value another component may contribute and refuses anything that is not a bare http(s)
    origin, IPv6 literals included, because CSP3's host-source grammar has no case for a
    bracketed literal and a browser drops the whole expression; the consent screen's
    `redirect_uri` was parsed with an unguarded `new Uri`, which would turn a page that used to
    render into a 500 for a row no admin form ever validated; and the masked-destination token
    borrowed the codes-per-day rate-limit window for its lifetime, coupling two policies that
    mean nothing alike.
  - **An application's logo is now checked at render as well as at write.** `ValidateHttpUrl`
    closed the create form and nothing backfills the rows already in the table, so the consent
    screen refuses to emit a `src` it cannot vouch for rather than resting on `default-src
    'self'` staying exactly as it is.
  - **The same fixes are propagated into `skeleton/`**, which had the old pipeline order, no
    content-type guard on its report sink, and the same false comment about `form-action 'self'`
    costing nothing. Three parity facts, three mutations; a stamped product builds with 0
    warnings.
  - **One review finding was refuted by measurement and the fix backed out.** `ValidateHttpUrl`
    was said to admit an empty authority, since `Uri.IsLoopback` is true for an empty host --
    true of `file:///c:/x`, and unreachable here: `Uri.TryCreate(UriKind.Absolute)` refuses
    every empty-authority http(s) form on .NET 10.0.7, so the check would have been dead code.
    The inputs are pinned as refused anyway, because the reason they are safe lives in someone
    else's parser.

### Changed
- **Re-synced to ISL-NETC 1.1**, SPDX `LicenseRef-ISL-NETC-1.1`. The point of 1.1 is that §1.2 is a
  `[LICENSOR]` placeholder the adopter completes, and its *Notice on Reproducing This License* says
  in terms that filling the bracketed fields "is not a modification of this text for the purposes
  of this notice". `LICENSE.md` is therefore an unmodified 1.1 reproduction naming **Extrode LLC**
  in §1.2 — it differs from the published text at that one line, verified by `diff`. That retires
  the `NOTICE.md` workaround that had been reconciling a Foundation-named §1.2 with a third-party
  licensor; the "two copyrights" table stays, because completing a field transfers nothing and the
  text's own copyright is still Ikhbat Foundation's. `NOTICE.md` and `README.md` now cite the
  version-pinned `/isl-netc/1.1/LICENSE.md` rather than the unversioned URL, which serves whichever
  text is current and had already drifted from 1.0 to 1.1 under them. `CONTRIBUTING.md` rows (a)
  and (d) restated: (d) survives, since the ISL-CLA asks who the Licensor is in its own right and
  not only as a rescue for a hardcoded §1.2. ISL-CLA adoption is untouched — still 1.1 by
  reference, still **DRAFT — not yet in force** pending their counsel review.
- **Verified both ISL 1.1 adoptions against the published texts**, which the re-sync above could
  not do because it was taken from a local licence checkout. islamiclicense.org deployed on
  2026-08-30; `/isl-netc/1.1/LICENSE.md` and `/isl-cla/1.1/CLA.md` are byte-identical to that
  checkout, so the caveat is discharged. One correction fell out of it: `CONTRIBUTING.md` rows (c)
  and (e) described the governing law and the forum as completing two ISL-CLA §9.2 placeholders,
  which is 1.0's shape — 1.0 §9.2 carries a literal `[JURISDICTION]`, and 1.1 carries none, stating
  that the Agreement "carries no blanks to fill" and deferring both to the adoption section.
  Reworded; the stated values, **Commonwealth of Virginia** and its courts, are unchanged.

### Added
- **The back button through the OAuth authorize flow is now walked by a test**, closing the
  question 012 left open when htmx 4 removed the `localStorage` page-history cache. The worry was
  that back would re-request a one-time-ish page; the answer is that htmx's history layer never
  touched this flow — nothing in `apps/web` sets `hx-push-url`, `hx-boost` or `hx-history`, so back
  is the browser's own navigation and always was. What the test pins is the server's response to a
  re-issued authorize GET after the code has been redeemed: a fresh consent screen rather than a
  500 or a stale replay. It counts the requests (1, then 2) instead of reading the rendered page,
  because a bfcache hit would render identically.
- **The two antiforgery-rejection guards are now held to agreeing.** `apps/web`'s
  `AntiforgeryValidationMiddleware` and `NativeWeb.Scaffold.UseAntiforgeryRejection` are the same
  guard written twice, kept apart on purpose so the identity provider does not depend on the CRUD
  scaffolding library. The risk the 2026-08-25 review left open was repo policy drift, which a
  cross-referencing comment cannot catch. One theory now runs both through the three feature
  states — absent, valid, rejected — and asserts they agree *and* that what they agree on is a 400
  with the endpoint not reached. The scaffold copy goes through a built `ApplicationBuilder`, so
  its registration is covered too. The test project takes the scaffold reference; `apps/web` still
  does not.

### Fixed
- **A busy submit control stayed disabled forever when the response swapped away the region that
  issued the request.** `boot.js` parked its reference to the disabled control on
  `ctx.sourceElement` at `htmx:before:request` and read it back at `htmx:finally:request` — but
  htmx reassigns `ctx.sourceElement` to the swap target while swapping, so the read landed on an
  element that had never been given a reference. Measured on one signin submission: the property
  is `FORM.auth-card` at `before:request` and `DIV.auth-board` at `finally:request`. The reference
  now rides on the per-request `ctx` object, which htmx does not reassign. Nothing in `apps/web`
  has the shape that shows this — every submit sits inside its own swap target and is replaced
  along with it — so the regression test builds it, attaching a submit from outside the swapped
  board with `form="..."`. The 012 review had logged the symptom with a different mechanism
  (dispatch on a detached node never reaching `document`) and a different fix (bind at-target with
  `{once: true}`); both were wrong, and the proposed fix fails this test. htmx's own `trigger`
  ends `isConnected ? element : document`, so detachment was never the problem.
- **The bench harness no longer publishes into the e2e fixture's directory.** `tools/bench` ran
  `dotnet publish apps/web -c Release` with the default output path — the same
  `apps/web/bin/Release/net10.0/<rid>/publish` that `AppProcessFixture` publishes into and
  launches from, so a bench run interleaved with the e2e tier had the two writing one directory.
  The quieter half was `--skip-publish`, which would benchmark whatever binary that tier last
  built and report the number as the harness's own. Output is now pinned to `tmp/bench/publish`
  and `findPublishedExe` looks only there; an e2e binary in the old location is refused by name.
  `tmp/`, not the repo-root `publish/`, which this repo deliberately does not gitignore.

- **`nativeweb.scaffold.tests.e2e` had no headroom under load: its NativeAOT publish budget was
  15 minutes.** On 2026-08-25 all 13 facts failed at `00:15:00` while another process saturated
  the CPU, and 13/13 passed in 2 s when re-run alone on the same commit — the tier was starved,
  not flaky. `SampleProcessFixture` now allows 30 minutes, which is what `AppProcessFixture` in
  the web e2e tier had already been raised to; the two fixtures do the same publish and there was
  no reason for them to disagree. A CI runner sharing a host looks exactly like the 2026-08-25
  failure, so the budget is sized for a busy machine rather than an idle one.
- **Two stale references to the retired `extrode-legacy-org` org, and the two test gaps that hid them.**
  `<Authors>extrode-legacy-org</Authors>` sat in **ten** packable `libs/nativeweb.*` csproj files, one line
  from a `Copyright` that already read Extrode LLC — because that one was central in
  `Directory.Build.targets` and Authors was not. Authors is now central too and the ten local lines
  are gone; `dotnet pack libs/nativeweb.render` produces `<authors>Extrode LLC</authors>`.
  `skeleton/nuget.config` still pointed its `framework-github` source at
  `https://nuget.pkg.github.com/extrode-legacy-org`, which a stamped product would have restored against.
  Neither was caught: `OrgReferenceTests` bans the old org only in things that *resolve*, and its
  feed check scanned the root `nuget.config` and the workflows but not the skeleton one, so it was
  green on the single file that had rotted. That candidate list now names both configs and asserts
  each exists; new `PackageMetadataTests` requires one central `<Authors>` and forbids a
  per-project one, which `Directory.Build.targets` would override silently.

### Added
- **The repository has a license.** NativeWeb is licensed under the **Islamic Software License -
  Network Copyleft (ISL-NETC) 1.0** (SPDX `LicenseRef-ISL-NETC-1.0`), copyright Extrode LLC,
  governing jurisdiction the Commonwealth of Virginia. `LICENSE.md` is the text verbatim — the
  ISL family permits reproduction only unmodified, so the defect that its §1.2 defines "Licensor"
  as Ikhbat Foundation is overridden in `NOTICE.md` rather than patched, which is the reading the
  ISL-CLA §1.2 note itself blesses. `NOTICE.md` must be retained by every copy under §3.1(c), and
  the packable `libs/nativeweb.*` now pack it and the license (verified: `NativeWeb.Render`'s
  nuspec carries `<license type="file">LICENSE.md</license>` and both files are in the .nupkg).
  Two obligations reach further than a permissive license and are called out in the README: §3.6
  network copyleft, and the §§4–5 ethical use restrictions that cannot be relicensed away.
- **`CONTRIBUTING.md` adopts ISL-CLA 1.1, including optional §6 — no AI-generated contributions.**
  Adoption is by reference and states the five things the agreement requires: the variant
  (ISL-NETC 1.0, so its ISL-R-only §2 is inert), that §6 **is** adopted, the §9.2 jurisdiction, who
  the Licensor is — Extrode LLC, not the Ikhbat Foundation the ISL-NETC 1.0 text names in its §1.2
  — and the forum, §9.2's second placeholder: the courts located in the Commonwealth of Virginia,
  matching Extrode's other licenses.
  The asymmetry is stated openly rather than hidden: this repository is developed with AI
  assistance under the Licensor's own review, which §6.4(3) frames as a decision about the
  Licensor's own liability and not one it can make on a contributor's behalf. Written contributions
  are the wanted route; the maintainers implement. `CONTRIBUTORS.md` is the signing sheet.
  **Nothing is accepted yet** — ISL-CLA 1.1 is published in draft and says not to accept
  contributions under it until it is marked final, and `CONTRIBUTING.md` says so at the top.
  The version is 1.1 because the agreement's own adoption mechanism is identification by name and
  version, and 1.0 was frozen back to a four-item text on 2026-08-29: the fifth item and the §9.2
  forum placeholder this adoption completes exist only in 1.1.
- **`tools/bench/` — a load harness for `apps/web`, with a committed baseline.** Publishes the
  Release binary, drives it over https with a throwaway certificate, and averages three ten-second
  passes per scenario against `tools/bench/baseline.json`: `/healthz` as the no-render floor and
  `GET /` as the full page render. `--check` exits non-zero on a regression; a run on another
  machine, or with different settings, records its numbers and says there is nothing to compare
  against. This closes spec 012's R16, which had asked for a benchmark re-run that was impossible
  — §14's figures came from apps that are not in this repo and a sandbox that no longer exists —
  and turns it into something re-runnable for every future upgrade. **Spec 012 is now `done`.**
  First baseline (bench-host, 16 cpu, win-x64): `/healthz` 18,067.2 req/s / 74.4 MB peak RSS, `home`
  6,998.0 req/s / 60.9 MB, zero errors. Not comparable to §14, and not a CI job: the measured
  run-to-run spread is 16.3%, so the tolerances are 20% and 25% rather than 5%.

### Fixed
- **Four ways the bench harness could pass without checking anything**, found by a third review
  pass (Fable) over already-reviewed code. Each is now a named failure with a test that dies when
  the fix is reverted: `--check --duration 5` against a ten-second baseline took the record-only
  branch and **exited 0 having compared nothing** — it is exit 2 now, while a *machine* mismatch
  stays record-only because nothing the operator types changes which machine they are on; a
  **measured RSS of 0** from a dead sampler satisfied `0 > baseline × 1.25` and passed the memory
  half of every check; a hand-edited `"rps": "6998"` passed the envelope-only shape check and made
  every later comparison NaN-unfailable; and certificate **reuse checked expiry but not the
  chain**, so a mint interrupted between the CA and the leaf left a mismatched-but-unexpired set
  that every later run reused, failing every request as a transport error for up to a year.
  Minting now stages into `tmp/bench/mint/` and moves into place. **95 harness tests, 0 failed**
  (was 87). Verified live: `--check --skip-publish` exits 0, the same run with `--duration 5`
  exits 2.
- **The skeleton produced products that could not build.** `skeleton/Directory.Build.props` carried
  `--` inside an XML comment, which XML forbids, so MSBuild refused to load the file (`MSB4024`)
  and every stamped product failed on its own README's first command before compiling a line.
  Nothing read the skeleton as XML until a product existed, so nothing caught it. Every tracked
  `.props`, `.targets`, `.csproj`, `.slnx`, `nuget.config` and `.nuspec` is now parsed by the
  suite.
- **`security.txt` named one deployment's mailbox in everybody's source.** `apps/web` shipped
  `security@epass.io` and a stamped product shipped `security@<product>.example`. Both are now
  `<Product>:SecurityContact`, and unset serves no `security.txt` at all rather than one naming a
  mailbox that does not read the reports: a wrong `Sitemap:` line is discarded by the crawler, a
  wrong `Contact:` line is acted on and the reporter never learns it went nowhere.
- **`apps/web` would not run without a flag.** Its only launch profile was HTTP and the app is
  HTTPS-only in practice, so `dotnet run --project apps/web` served `/healthz` 200 and every page
  with a form 500 — a combination that reads as one broken page rather than a wrong scheme. The
  profile is HTTPS; `README.md` and the quick start drop the `--urls` workaround.
- **A checkout under `tmp/` turned the suite red.** `SdkPinTests` walked the whole tree for
  workflows, so scratch placed where `AGENTS.md` says scratch belongs was reported as a CI
  violation. It asks git now.
- **20 cleanup scripts targeted a path this repository left** at the extrode move, and would each
  have run `git -C` against a directory that does not exist.
- **The sign-in library's NativeAOT gate ran nowhere.** `tests/nativeweb.signin.epass.tests.aot`
  asserts by publishing and holds no tests, so `dotnet test` collected zero facts and exited 0 in
  silence — and the existing `aot-publish` job publishes `apps/web`, which holds no
  ProjectReference to the library, so its IL never reached that link either. Two mechanisms that
  looked like coverage, neither of which was. It runs in CI now, and the gate list is derived from
  the projects rather than maintained by hand.

### Added
- **Continuous read-only audit** (spec 013). A nightly unattended pass over one slice of the
  repository, run on a separate host, producing findings a session triages by hand. Read-only
  because of how it is arranged rather than because it is asked to be: the run works on a
  throwaway clone of a mirror the workstation pushes, and the audit host holds no credential
  that reaches `origin`. Six mechanical checks plus one bounded model reading pass; every
  candidate is put to a second model prompted to refute it, and what survives is redacted before
  it is written. Finding identity is hashed over the normalised excerpt rather than the line
  number, so an edit above a defect does not resurrect it, and `rejected` is absorbing so a
  dismissal costs one decision rather than one a day. `tools/audit/`, `scripts/audit/`, operator
  guide in `docs/02-reference/continuous-audit.md`. The schedule is not running yet: it starts
  when `scripts/audit/provision-audit-host.sh --execute` is run on the audit host.

### Changed
- **htmx 2.0.10 → 4.0.0** (spec 012), vendored into `apps/web` and the skeleton. htmx 4 is a
  rewrite onto `fetch()`: lifecycle events are renamed to `htmx:phase:action` and carry their
  payload as `detail.ctx`. Because no `hx-*` attribute is authored anywhere in this repo — all of
  them are derived at runtime from `data-nw-*` — the change is confined to `boot.js`, the
  antiforgery glue and the `htmx-config` meta. Behaviour is held constant, with one deliberate
  addition: `noSwap: [204, 304, "4xx", "5xx"]`, so a guard rejection body is not swapped into an
  authenticated page, which 4.0 would otherwise do by default. The minified library is 28.3%
  smaller, 51,238 B → 36,716 B, and htmx 4 no longer caches page HTML in `localStorage`.
  Morph swaps and `hx-alpine-compat` were evaluated and declined; the 2.0.10 build is retained at
  `libs/nativeweb.assets/rollback/`.

### Added
- **A way to put `apps/web` on a host, and to get it back after losing one** (spec 011) — the deploy
  half Slice 10 shipped without. `scripts/deploy/` provisions a bare Debian host, releases
  blue/green behind Caddy, rolls back, and takes nightly `age`-encrypted dumps to S3-compatible
  storage; `docs/02-reference/deployment.md` is the runbook.
  - **Blue/green rather than restart-in-place**, because the requirement says the previous release
    keeps serving *throughout*. Two systemd instances on loopback ports; a deploy stages into the
    idle colour, migrates, proves `/readyz`, then rewrites one Caddy import and reloads. Every step
    before the last leaves production untouched, so a failed release never reaches the public.
  - **`deploy.sh` refuses to promote on two settings** rather than documenting them, because
    neither has a symptom anyone would notice: `KnownProxies` unset behind a proxy puts every
    client in the world into one rate-limit partition, and telemetry with no endpoint retries
    forever against a collector that is not there.
  - **`rollback.sh` refuses a release that applied migrations.** Nothing records whether they
    expanded or contracted, and this script must not guess.
  - **`robots.txt` and `sitemap.xml` are rendered, not static** — from `NativeWeb:CanonicalBaseUrl`,
    which startup rejects unless it is an origin with no path, query or fragment. This retires the
    hardcoded `https://epass.io` that staging hosts had been advertising. The sitemap keeps
    `text/xml`, matching the file it replaces.
  - **Three of the four epass.io cutover deploy blockers are addressed; none of them by execution.**
    No host has been provisioned from this document, no backup restored, and
    `MemoryDenyWriteExecute=yes` is unverified — T14 and T15, both outstanding, both stated in the
    runbook's own "Known gaps" table. `BACKUP_HEARTBEAT_URL` is unset, so a backup that stops
    running is still invisible, and `backup.sh` says so on every run.
- **`docs/` is a buildable site, not a flat pile of markdown.** The shared Docs generator discovers
  sections `TopDirectoryOnly`, so a flat `docs/` published the 12 root files and nothing else. The
  reader-facing documents are now grouped into `00-quick-start`, `01-architecture` and
  `02-reference` with a landing `docs/00-quick-start/README.md` that `index.html` prefers, and
  `scripts/build-docs.sh` renders the tree to `dist/docs-site` (gitignored, overwritten each
  build). The superseded brainstorm and the admin handoff moved into subdirectories so they stay in
  the repo without shipping. Site builds to 11 pages; the 9 dropped links are all paths outside
  `docs/`, which a flat site cannot serve, and the generator lists them — a free audit.
  - **Two test suites read doc files as fixtures by hard-coded path** — the configuration and
    template-diagnostics references — so their paths and assertion messages moved with the files.
    Proved to reach the fixtures by hiding both: 3 tests fail in each suite.
  - `skeleton/README.md` still points at `docs/security-baseline.md`, and should: the skeleton
    ships its own flat copy, which did not move.
- **An onboarding path** (spec 010) — the repo had 2,227 lines of reference documentation and no
  way in; `README.md` still carried four unfilled template placeholders. Four documents now form an
  ordered path, and **every command in them was executed in the form written before it was written
  down.**
  - `README.md` filled: what this is, the prerequisites table, run/build/test. The JauntyQ
    prerequisite is stated as what measurement showed it to be — the solution build needs `jauntyq`
    as a *literal sibling*, and `-p:JauntyQUsePackages=true` does not rescue it, because
    `NativeWeb.slnx` lists JauntyQ's two projects as solution members unconditionally.
  - `docs/00-quick-start/getting-started.md`: clone to a green suite, forking once and explicitly
    into Path A (stamp a product from `skeleton/`) and Path B (work on `apps/web`). Both were
    walked rather than described; the two Path A footnotes — the integration tier needing
    `<Product>__Db__ConnectionString`, and roots passed as environment variables rather than `-p:`
    flags because command-line properties do not cross into the e2e fixture's child `dotnet
    publish` — are findings from that walk.
  - `docs/00-quick-start/tutorial-first-feature.md`: one feature through every layer, backed by a
    feature actually built for it rather than an illustration. Nine sections, no layer deferred.
  - `docs/README.md`: an index over every human-facing document, grouped by what the reader is
    trying to do. It records two divergences rather than smoothing them: `docs/constitution.md` is
    still the unfilled template, and `conventions.md`'s cleanup-script filename rule differs from
    the global one the scripts follow.
- **A public article feed in `apps/sample`** — `GET /articles`, the tutorial's worked example and
  the demo's first hand-written page, standing against the generated admin UI over the same table.
  A narrowed `PublishedArticle` read model behind an `IPublishedArticles` seam, with both
  implementations the store choice already implies; a `ListPublished.sql` the generator turns into
  a typed method; a hand-written renderer that calls `HtmlEncoder` explicitly; and an anonymous,
  constant-page-size endpoint. The query is the repo's first use of JauntyQ's `-- @allow-sort
  <reason>` directive: `ORDER BY created_at` on a table indexed only by primary key raises
  `JNT8007`, which `TreatWarningsAsErrors` makes fatal, and on a three-row demo table the honest
  answer is to accept the sort and record why rather than add an index PostgreSQL would ignore. The
  reason is mandatory and the directive self-expires — add a covering index and `JNT8012` fires on
  that line. 19 new facts across the three tiers (sample-unit 8 → 16, scaffold-integration 24 → 31,
  scaffold-e2e 9 → 13), every one perturbed and proved red before being kept.
- **CI runs the two suites it never ran** — a `skeleton-stamp` job stamps a throwaway product from
  `skeleton/`, provisions its database, migrates it and runs all three of its layers, closing a gap
  that had let the skeleton's integration and e2e tests sit unexecuted since they were written. The
  `test` job gained the four scaffolding tiers, which CI had likewise never run. Both fixtures were
  hardened for a runner in the process: `SampleFactory` and `SampleProcessFixture` now force the
  store selection rather than relying on `ConnectionStrings__Sample` being absent, since a job that
  exports it for one tier hands it to every child process in the same job.
- **A JauntyQ-backed store for the scaffolding demo** — `apps/sample` now proves both sides of the
  `IScaffoldStore<T>` seam. `JauntyArticleStore` satisfies the same six methods with JauntyQ
  queries over PostgreSQL, and `ConnectionStrings:Sample` chooses between it and the in-memory
  store; unset keeps the demo and the whole test suite running with no database at all. Nothing
  above the seam changes and nothing is regenerated. `Tag` stays in-memory on purpose, so one app
  demonstrates two models with entirely different persistence behind one contract. The demo's
  schema, queries and snapshot live in `db/sample/`, deliberately apart from the identity schema
  in `db/`. New `tests/nativeweb.sample.tests.unit`; the scaffolding integration and e2e tiers each
  gained a JauntyQ-backed pass, the latter being the only place in the repo where JauntyQ-generated
  code runs under NativeAOT.
- **Three consumer seams for the sign-in library** (spec 009) — the gaps its first consumer found.
  All three are optional; a consumer that adopts none behaves exactly as under 008.
  - **Ending a session mid-request.** `SessionAuthenticationHandler` now implements
    `IAuthenticationSignOutHandler`, so `http.SignOutAsync(SessionAuthenticationHandler.SchemeName)`
    works from a filter or middleware. Under 008 the only sanctioned sign-out was a form post to
    `LogoutPath`, which a filter cannot perform — so a consumer hand-copied the cookie attributes,
    and a `__Host-` deletion missing `Secure` or `Path=/` is a no-op the browser never reports.
    Both paths share one implementation and cannot drift.
  - **`OnAddressSubmitted`** — refuse an address before a login link is attempted, so a customer
    is not told a link is on its way to somewhere it cannot arrive. It returns a `bool` rather
    than a failure code **on purpose**: a policy that could choose the code could choose a
    different one for an address it recognises, which is an account-enumeration oracle built out
    of a feature. That bound covers the **code**; the rest of the response does not bound itself,
    because the policy holds the live `HttpContext` — the README says so rather than claiming
    otherwise. Called after the 254-**character** bound (`string.Length`, so UTF-16 code units),
    so a consumer's regex is never handed a multi-megabyte form value. A policy that throws is a
    refusal, not an acceptance; a policy that cancels its own work is a broken policy and likewise
    refuses; and a second `OnAddressSubmitted` throws rather than silently replacing the first.
  - **`TestSession`** in `NativeWeb.SignIn.Epass.Testing` (**0.2.0**) — `Mint`, `MintExpired`,
    `Tamper` and `CookieName`, over the library's own `SessionCookie` via `InternalsVisibleTo`.
    Under 008 a consumer's integration tests reimplemented the ticket format by hand, which keeps
    passing when the format changes. **No minting API was added to the production surface**: a
    public `Mint` is an authentication bypass callable with any subject. The package is now marked
    `DevelopmentDependency`, which is what actually keeps it and its xunit reference out of an
    application's closure.

  Coverage is now 177 unit facts, 32 integration and 3 Playwright journeys — up from 164 / 16 / 2
  at 008. Every new test was perturbed and proved red first. The AOT probe exercises both new seams
  and publishes native clean.

- **Sign in with epass.io** (spec 008) — `libs/nativeweb.signin.epass`, the relying-party half of
  the handshake, extracted so an application can consume epass.io without depending on
  `nativeweb.auth`, which *is* an identity provider. A consumer supplies two things: an
  `ILoginFlowStore` (their table) and an `OnSignedIn` resolver (their user provisioning). The
  library owns the start, callback and sign-out endpoints at consumer-chosen paths, the signed
  session cookie, and the open-redirect guard. Failures reach the consumer as one of eight machine
  codes; no provider-supplied string ever reaches a page, a query string or a header.
  `libs/nativeweb.signin.epass.testing` ships the conformance fixture that proves a consumer's
  store takes atomically — the one property no single-threaded test can see.
  Three tiers cover it: 164 unit facts, 16 integration facts through a real Kestrel host, a
  JauntyQ store on real PostgreSQL and a stub provider, and 2 Playwright journeys in Chromium.
  The integration host runs on HTTPS with a self-minted certificate, not for realism but because
  Chromium accepts `Secure` from insecure localhost and **refuses the `__Host-` prefix there** —
  over http the browser silently drops the session and the failure reads as a library bug.
- **Session ticket format `v2`** — every field base64url-encoded before joining, so an arbitrary
  string subject is representable. **Existing `v1` cookies stop verifying at cutover and every
  signed-in user signs in once more.** There is no dual-read path.
- **CRUD scaffolding, generated** (brainstorm 001 T1.3) — `libs/nativeweb.scaffold` (attributes +
  the six-method `IScaffoldStore<T>` seam) and `libs/nativeweb.scaffold.generator` (an incremental
  source generator). A model carrying `[Scaffold]` and `[Auth]` gets a full admin CRUD UI — list,
  search, paging, create, edit, delete — with no hand-written route, handler, form or view.
  `apps/sample` is the demo: two decorated classes and one `app.MapScaffolding()` call.
  The emitted binder is a `record` implementing `IBindableFromHttpContext`, which is the one shape
  that binds a form under NativeAOT; the record's parameter list *is* the mass-assignment allowlist,
  so a key or a `[ScaffoldReadOnly]` property cannot be bound at all. Routes are emitted as
  `RequestDelegate`s rather than route handlers, because the Request Delegate Generator cannot see
  another generator's output and the `Delegate` overloads it would otherwise fall back to carry
  `RequiresUnreferencedCode`/`RequiresDynamicCode`. `apps/sample` publishes NativeAOT clean.
  Every mutating route re-attaches `IAntiforgeryMetadata`, which a `BindAsync`-bound handler would
  otherwise lack, and the content-type guard lives inside `BindAsync` because binding runs before
  the endpoint-filter pipeline.
  Host wiring is two calls — `AddScaffolding()` and `UseScaffolding()` — that compose public
  primitives rather than hiding them; `UseScaffolding()` expands to four individually callable
  methods, one of which, `UseAntiforgeryRejection()`, supplies the 400 that `UseAntiforgery()`
  does not (it records a failed validation and calls next, so the form reader throws and the
  caller sees a 500). Authentication is deliberately excluded: who may reach `/admin` stays a
  visible line in the application.

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
- **The skeleton probes both spellings of the framework checkout.** `NativeWebRoot` looked only
  for `..\NativeWeb`, while the repository on disk is `nativeweb`. On Windows the two are the same
  directory and the bug cannot be seen at all; on Linux — where the skeleton's own CI runs —
  exactly one resolves, so a stamped product built on a case-sensitive filesystem failed at
  `EnsureNativeWebRoot` depending on how its author had spelled the clone. Both spellings are now
  probed, lowercase first, with the fallback naming the same path. The docs are the reason both
  exist: `getting-started.md` says to clone as `NativeWeb` and the README draws it that way.
  Pinned by three tests — including one that asserts on the probe *expressions* rather than on
  filesystem resolution, because a resolution test cannot fail on Windows and would have shipped
  green against a single-casing probe.
- **The JauntyQ checkout is probed relatively, so a renamed source root no longer breaks every
  local build.** Both probes in `Directory.Build.props`, the fallback, and the skeleton's single
  probe named an ancestor directory literally called `extrode-legacy-org.com`. When that directory was
  renamed to `extrode.com` on 2026-08-26 and jauntyq moved with it, the paths broke twice over and
  every per-project build needed `-p:JauntyQRoot` — while the solution build kept working, because
  `NativeWeb.slnx` had always named `../jauntyq/...` relatively. The probes are now `..\jauntyq`
  and `..\..\..\jauntyq` (the worktree depth), which is what the solution file already meant.
  Eight tests in `JauntyQRootProbeTests` resolve the probe expressions against a synthetic tree
  rather than pattern-matching the strings, and refuse any probe naming an org directory —
  `extrode.com` included, since writing that in would be the same mistake with a fresher name.
- **Stamping a new product no longer copies the skeleton's build output — or its secrets.**
  `tools/new-product.ps1` enumerated everything on disk under `skeleton/`, so a product stamped
  from a built checkout was handed 714 files instead of 168, and `git add -A` committed the
  skeleton's `bin/` and `obj/` on the new repo's very first commit. The same enumeration would
  have carried a `.env`, a `*.pfx` or `secrets/` the same way, since `skeleton/.gitignore` was
  the only thing that had ever excluded any of it. The file list now comes from
  `git ls-files --cached --others --exclude-standard`, which is tracked files plus untracked ones
  the ignore file does not exclude — so a skeleton file added but not yet committed is still
  stamped, and the exclusions stay declared in exactly one place.
- **Signing out no longer weakens a consumer's cache headers.** `SignOutAsync` and `POST /logout`
  *assigned* `Cache-Control: no-store`, so an app that had already set something stricter —
  justsmtp's dashboard gate sets `no-store, no-cache, must-revalidate, max-age=0` before the lookup
  that decides to end a session — got one directive of four back, with its `Pragma: no-cache` left
  beside it. The directive is now added rather than written over: absent, it is appended; present,
  the header is untouched. Found by the first consumer's review one day after 009 merged, and it
  could not have been found here — the library's own tests had no consumer header to overwrite.
- **Eleven findings from 009's own review**, all raised against code written on that branch and
  confirmed by a shell-equipped refuter before being fixed. The four that mattered: a policy whose
  own work timed out escaped as a **500 on an unauthenticated POST** because the
  `OperationCanceledException` rethrow was unfiltered; `SignOutAsync` deleted the cookie but left
  `HttpContext.User` **authenticated for the rest of the request**, so the filter the seam exists
  for still rendered the signed-in page; the testing package was **genuinely publishable** while
  four separate places called it undeployable; and a second `OnAddressSubmitted` **silently
  disabled the first**. Also: `no-store` moved into the one shared sign-out implementation so the
  two paths cannot drift, `Tamper` no longer throws `IndexOutOfRangeException` on a five-field
  non-ticket, and the docs no longer describe a character bound as a byte bound.
- **The .editorconfig naming rules now actually run** -- IDE1006 has to be enabled by
  `dotnet_diagnostic.IDE1006.severity`; a rule's own `severity = warning` decides how loud a
  violation is, not whether the analyzer runs. Without that line the whole naming section was
  decorative, which is why `interface_should_be_begins_with_i` had reported nothing since the file
  was written (measured: renaming a field to `BadlyNamedLines` built clean before, IDE1006 after).
  Rules added for const and static readonly fields (PascalCase), private fields (`_camelCase`),
  locals and parameters (camelCase). Enforcing rather than advisory, because the solution measured
  at exactly one violation -- a local const named `Yaml` -- fixed in the same commit.
  `EditorConfigNamingTests` guards the enable line and catches a rule pointing at a symbol spec or
  style that does not exist, the other way one goes quiet without failing anything.
- **Compressed static assets are covered in Release** -- `Directory.Build.targets` disables
  precompressed variants in Debug to work around a framework defect, so no Debug build, and
  therefore no unit or integration test, ever exercised the compressed path. `CompressedAssetTests`
  in the e2e tier asks the published binary directly: a gzip-accepting client must get
  `Content-Encoding: gzip`, a body smaller than the identity response, and one that gunzips to
  exactly that response. The size check is the point -- if publish quietly stops emitting variants
  when the workaround is removed, every client falls back to identity and nothing else anywhere
  says so.
- **A form POST with the wrong media type is answered 415, not 400** -- antiforgery reads its
  token from the form as well as the header, so `ValidateRequestAsync` on a JSON body threw and
  the handler blamed the token. The content-type guard now runs first at all 23 handlers in
  `apps/web/Auth` that carry both -- 17 in the first pass, and the six signed-in ones that had
  read the form through a tolerant ternary swept in after (2026-08-18): `PostApplicationDisable`,
  `PostApplicationRotateSecret`, `PostTwoFactorDisable`, `PostRecoveryCodesRegenerate`,
  `PostWebAuthnRemove`, `PostSessionsRevokeOthers`. Their ternary became a plain `ReadFormAsync`,
  since the guard leaves no path on which the form is absent. Not a security change: a request
  that IS a form reaches antiforgery unchanged, and the 415 path is reachable only by requests
  antiforgery was going to reject anyway; the 415 decision reads the caller's own `Content-Type`
  and no server state, so it is not an oracle. Three handlers are deliberately untouched --
  WebAuthn enroll/verify and sign-out accept either a JSON body with a header token or a real
  form, so a 415 guard would reject a request that has to work. Held by a source-order sweep over
  every handler plus HTTP-level facts on anonymous endpoints and on all six signed-in ones.
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
- **`actions/setup-dotnet`, from both workflows.** Ubuntu 24.04 and Windows Server 2025 each ship
  .NET SDK 10.0.110, 10.0.204 and 10.0.302 preinstalled, so `dotnet-version: "10.0.x"` installed
  nothing. The requirement moved to a new `global.json` (`10.0.100`, `rollForward: latestFeature`),
  which covers local builds the workflow never did; `SdkPinTests` holds it there and fails if a
  workflow starts naming a second version. With this and the action below gone,
  `actions/checkout@v7` is the only `uses:` step in either workflow — see
  `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.
- **Every third-party GitHub Action.** `ilammy/msvc-dev-cmd` was the last one, in both this
  repo's CI and the skeleton's, and it was doing nothing the toolchain does not do itself: the
  ILCompiler shells its own bundled `findvcvarsall.bat`, asks `vswhere` for the C++ tools and
  hands the linker an absolute path — measured by publishing `apps/web` from a shell with no
  `VCINSTALLDIR` and no `cl.exe` on `PATH` (links, exit 0, 56s). It was also the source of a real
  defect: it exported `Platform=x64`, which MSBuild reads as a property, adding a segment to the
  publish path that the budget step then could not find. `ActionPinTests` now asserts the count
  of third-party actions is zero rather than at least one, so adding one is a visible decision.
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

- `Redirects.To` answers a no-JS POST with **303 See Other** instead of 302. A GET still
  gets 302. Nothing changes in a browser; it matters for the non-browser clients that follow
  the OAuth surface.

- `db/migrate.sh` now serialises itself with a PostgreSQL advisory lock held for the whole
  run, so two concurrent runners cannot both apply the same migration. A second runner says it
  is queuing and waits. `db/test-run-lock.sh` verifies it.
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
