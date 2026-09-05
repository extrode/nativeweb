# Previous

The task completed most recently, kept for one rotation so the next one has context.

- **`Socket.OSSupportsIPv6` was the wrong gate, and justsmtp.com found it. Done on
  `fix/ipv6-loopback-trial-bind`, 2026-09-05.** Both port allocators skipped the `::1` probe when
  that flag was false. The flag is computed by creating an IPv6 socket, so it reports that the
  stack exists, not that the loopback address can be bound: a machine with IPv6 off by group
  policy answers true and then refuses every bind. All 32 attempts would fail for one cause and
  throw "No port free on both loopback addresses after 32 attempts", sending whoever debugged it
  at the exclusion ranges. Both allocators now bind `::1` once at static init and remember the
  answer, and both `PortAllocationTests` use that same value so a test cannot be stricter than
  the allocator. The exchange went both ways: this repo sent the wildcard correction that
  justsmtp had already reproduced independently, and got this back. **"Only half the fix is testable
  here" was wrong, and the user caught it.** The obstacle was never that this machine has IPv6 --
  it does, which is precisely why the false branch never runs here. The obstacle was that the
  decision was hard-wired into the allocator, so a differently-configured machine was the only way
  in. Both allocators take the `::1` check as a parameter now, on `fix/ipv6-gate-testable`, and
  both sides are asserted: a predicate that never accepts must exhaust the loop and say
  "32 attempts" rather than return a port, and `CanBindV6` returning false is proved by holding
  `::1` on a port and asking about that port, which needs no special host. A platform limit was
  claimed where there was a design limit -- but only partly, and the fable review said so: the
  ternary that picks the real probe or a no-op still runs one arm here, so the seam moved the
  untestable part rather than removing it. The remarks say that now. Fable also killed the
  always-true test (no mutation kills it that the other two miss) and caught the held-port test
  binding `::1` in its own arrange step with no gate, which would have made it fail on the one
  host class this whole change is for -- stricter than the allocator it stands in for, the exact
  fault this file's remarks warn against. Both fixed; six findings, five acted on, one refuted in
  part. Scaffold 35/35, sign-in 8/8, permissions 3/3, forms 7/7, sample unit 231, solution build
  0 warnings, structure-audit 0 errors and the 3 pre-existing kebab-case warnings.

- **No e2e fixture names a port any more, and the sign-in tier's allocator was wrong. Done on
  `fix/scaffold-e2e-os-allocated-ports`, 2026-09-05.** Five fixtures across three tiers held
  literals -- 5347, 5348, 5351 in scaffold, 5352 in permissions, 5353 in forms -- all through one
  linked base class, so one change closed all five. The scaffold tier had been failing 31/31 on
  `address already in use` because `justsmtp.web.exe` from another repository was listening on
  5348; it runs 32/32 now with that process still up. The review earned its cost twice. It caught
  that `TcpListener.Create(0)` is the wrong probe: it binds the dual-stack wildcard, and a
  deliberate break returning an occupied 5348 was **accepted** by the wildcard and refused by the
  loopback pair, so the OS can report free a port Kestrel cannot bind. Yesterday's sign-in
  allocator had that defect and is fixed too. It also caught that three fixtures draw
  concurrently -- this file's own `PublishedExe` remarks record that xUnit does not serialize a
  collection's fixtures -- so a process-wide record of handed-out ports guards the window between
  the probe closing and Kestrel binding. **One test was written and then deleted for lying**: with
  the dedup guard removed, sixteen sequential draws stayed distinct, because Windows cycles the
  ephemeral range rather than reissuing what was just released. The guard stays, documented as
  unproven, rather than carrying a green test that cannot fail. Falsified four ways that do fail:
  an occupied port, an excluded port, port 0, and an excluded port through the new allocator.

- **The sign-in e2e tier is green for the first time on this workstation. Done on
  `fix/signin-e2e-excluded-port`, 2026-09-05.** It failed 4/4 with "An attempt was made to access
  a socket in a way forbidden by its access permissions" and had done since before the xunit v3
  move. Nothing held the ports: `SignInProcessFixture` named 5349 and 5350, and both sit in this
  machine's Windows TCP exclusion ranges - on the IPv6 list as well as the IPv4 one, which an
  IPv4-only probe would have missed. The fixture asks for port 0 twice now, holding both listeners
  open until both ports are known so the second call cannot be handed the first's port back. It
  used `TcpListener.Create` when this was written, on the reasoning that a dual-stack probe
  matches the `localhost` binding it feeds; that was wrong and was corrected later the same day
  -- see the bullet above. Allocation moved out of the constructor and behind the publish, which had put a
  30-minute lock wait between observing a port free and binding it. 5/5 in 6.4 s warm, 48 s cold.
  One review finding was acted on and then reversed by measurement: putting the new port fact in
  the e2e collection made a broken allocator fail it with the fixture's "Hosting failed to start"
  rather than the assertion, and the collection already sets `DisableParallelization`, so nothing
  ran in parallel with it to begin with. **The scaffold e2e tier is red for the same class of
  reason and was not touched**: it hardcodes 5348, and `justsmtp.web.exe` from another repository
  was listening on it, so 31/31 failed on `address already in use`. One line in `../todo.md`.

- **`build.sh` names its own remedy when the JauntyQ feed refuses it. Done on
  `fix/build-sh-jauntyq-credential`, 2026-09-05.** The sharp end of the package-mode default, and
  measurement moved it: this is not a cold-host risk, it is a release that could not run here. The
  feed is credentialed in the Windows user-level `NuGet.Config`, so a restore into an empty package
  folder succeeds on Windows in 11.6 s; `build.sh` runs in WSL, whose profile holds no
  `packageSourceCredentials` and whose 255 cached packages include no JauntyQ, and a real run died
  `NU1301` / `401 (Unauthorized)` at 9.57 s. The script now restores before it publishes and, on an
  auth failure, prints the one `dotnet nuget update source` command that fixes it. It carries no
  token: a `%VAR%` credential block in `nuget.config` would override the working Windows credential
  whenever the variable is unset, and having the script credential the source itself would leave a
  plaintext token under `$HOME` as a silent side effect. Two facts in `DeployTargetTests`, both
  cross-referential rather than substring checks - the preflight and the publish select the same
  JauntyQ mode, and the remedy names the source `nuget.config` maps `JauntyQ.*` to. Repo tier 192.

- **JauntyQ package mode is the default. Done on `chore/jauntyq-package-default`, 2026-09-05 (reviewed, two lenses, 2 confirmed defects fixed).**
  JauntyQ split its generator and runtime out to a public core repository, so `../jauntyq` no
  longer carries either csproj this repository referenced. Six sites pointed at the half that
  left, `NativeWeb.slnx` among them, and every local build failed fast until
  `-p:JauntyQUsePackages=true` was passed by hand. The flip inverts one property: package mode is
  the default, `-p:JauntyQUsePackages=false` selects a checkout. The probe block and
  `EnsureJauntyQRoot` are untouched, so checkout mode is intact for the day the public core is
  cloned. `NativeWeb.slnx` loses its two dead members, which is what makes a root `dotnet build`
  work at all, and with them go the `dotnet sln remove` / `git checkout --` pair in
  `.github/workflows/ci.yml` and the `extrode/jauntyq` clone in the skeleton's. Every tier at
  baseline with no flag on the command line - render 219, forms 148, scaffold 268, permissions
  151, sample 231, repo 189 (183 plus seven new, minus a merged pair) - and the solution builds 0
  warnings, which it had never done in package mode. Two things left for the user: the
  pre-split sentinel, and the `AGENTS.md` licence sentence that no longer describes how we build.
  Plan in `docs/plans/2026-09-05-011-jauntyq-package-mode-default.md`, decision 012.

- **Ten outdated NuGet pins bumped. Done on `chore/package-bumps`, 2026-09-05.** Raised from the
  NuGet Solution "Updates" list: AngleSharp, Geralt, Mvc.Testing, TestHost, Logging.Abstractions,
  Playwright and the four OpenTelemetry packages, in both `Directory.Packages.props` files, plus
  `Microsoft.CodeAnalysis.Analyzers` 3.11.0 -> 5.9.0 on `chore/analyzers-bump`. Playwright
  1.49 -> 1.62 is the largest jump and the scaffold e2e tier passes 31/31 on the installed
  chromium. Every tier re-run at its recorded baseline, repo 183 with the new drift guard.

  **The twelfth was held back for a day and then moved.** `Microsoft.CodeAnalysis.CSharp` stayed
  at 4.12.0 on 2026-09-05 because the version the four `IsRoslynComponent` projects compile against
  is a floor on every consumer of the published analyzer packages, and `justsmtp.com` was under it:
  it pins `NativeWeb.Render.Generator` and its `global.json` had no `sdk` section. That repository
  pinned `10.0.100` / `latestFeature` the same day, the consumer set was re-checked here (nothing
  else consumes a generator), and the pin moved to 5.3.0 on `chore/roslyn-csharp-5-3`. 5.9.0 is
  still out of reach: our SDK runs Roslyn 5.3.0 and refuses a newer analyzer with CS9057. Every
  generator-backed tier re-ran at baseline - render 219, forms 148, scaffold 268, permissions 151,
  sample 231, repo 183 - and `apps/sample` builds 0 warnings, which under repo-wide
  `TreatWarningsAsErrors` is what proves no generator stopped loading.

  The first telling of this lumped `.Analyzers` in with it, and that was wrong: it is
  `PrivateAssets="all"`, never reaches a consumer, and is a rule pack rather than a compiler API
  surface, so neither argument touches it. A `qwen-review` audit reached the same conclusion
  independently; a later `glm-think` pass found the comment block's own account of CS9057
  incomplete - a consumer that consumes generated symbols does not get a silent green build, it
  gets CS0103 on its own source for every one of them. Both NU1605 bounds are quoted from the
  restore output rather than guessed (CSharp 4.12.0 requires Analyzers >= 3.3.4, CSharp 5.3.0
  requires >= 5.3.0-2.25625.1). Decisions 010 and 011 in `docs/decisions/`; 011 also records the
  two things 010 got wrong about `justsmtp.com`, both corrected by measurement from that repository.

- **xunit 2.9.3 -> xunit.v3 4.0.0. Done on `chore/xunit-v3`, 2026-09-05.** Not a pin bump: v3 is
  Microsoft.Testing.Platform, and the .NET 10 SDK will not run it through VSTest, so
  `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are dropped and every `dotnet test` in
  the repository changes shape. 23 test projects, 26 `IAsyncLifetime` signatures to `ValueTask`,
  and 30 command lines in CI, the docs and `tools/stamp-check.ts`. Every tier green at its recorded
  baseline (repo 181, web unit 177, sample unit 231, five integration tiers, four e2e tiers), and
  `tools/stamp-check.ts` proves a stamped product builds and runs 1 unit and 6 integration tests
  under the new platform. Two things it left: xUnit1051 is suppressed in the two test-tier
  `Directory.Build.props` with the reason stated, and `nativeweb.signin.tests.e2e` fails 4/4 on a
  Windows excluded port range, identically on `dev` under the old stack. Both in `../todo.md`.
  Plan in `docs/plans/2026-09-05-010-xunit-v3.md`.

- **Spec 029 - framework-owned templates. Done on `feat/029-framework-templates`, 2026-09-05.**
  Applies spec 015 R7's argument to the four HTML partials it stopped short of:
  `shared/{antiforgery,bot-traps,banner-ok,banner-error}.html` move into `libs/nativeweb.web` and
  are contributed to every consumer as `AdditionalFiles` by `NativeWeb.Web.targets`, the way that
  file already contributes `wwwroot/**`. `RenderTemplateGenerator.LogicalPath` keys on the last
  `/templates/` segment of a physical path, so the logical names and all 11 call sites are
  unchanged. The reason it matters is `bot-traps.html`, which duplicates five `BotTraps` field-name
  constants as literal text: rename one and every product's copy stops matching with no compile
  error, no test failure and no runtime error. Also closes the gap that `apps/sample` cannot render
  a flash banner at all: the contact form is post-redirect-get now, and `ContactFlashTests` drives
  both banners through a real request. All eleven tasks done, R2's package half stated as
  unproven. The one gap the run left open is closed on `chore/029-template-delivery-switch`:
  `FrameworkTemplateDeliveryTests` runs `dotnet msbuild -getItem:AdditionalFiles` against a
  throwaway project and pins what `NativeWebImportTemplates` delivers at each setting, which no
  build in this repository does. Spec, plan and tasks in `docs/specs/029-framework-owned-templates/`.

- **A Fable review of both merged branches, and the twelve fixes it produced.** Fable had not
  seen either branch: 028 and the stamp check both went to `review` (opus) on two lenses. Its
  fourteen findings settled to twelve real, one refuted and one not a finding, every one of them
  verified with a shell rather than taken on trust. Merged on `fix/fable-review-findings`.

- **Milestone 1 - the skeleton builds here, not only in CI. `fix/skeleton-stamp-authorization`,
  2026-09-05.** `tools/stamp-check.ts` stamps a product, builds it and runs its unit tier against
  this working tree; the integration tier is opt-in behind `--with-database` because it needs the
  stamped product's own Postgres role. Its first run found the reason the gap mattered: a stamped
  product registered the authorization services only when a sign-in provider was configured and
  died at startup without one. Both are fixed and merged. The half of this that read "nothing
  replaces `SkeletonParityTests`" was retired on 2026-09-05: that suite compared twenty copied C#
  files against a twin, spec 015 removed the copies and the twin left with the identity provider,
  so the invariant is vacuous. What is left of the milestone is the integration tier's database.

- **Spec 028 - versioned package releases. Building on `feat/028-package-releases`, 2026-09-04.**
  Closes G1 in `docs/architecture/004-consumer-readiness.md` and discharges obligation 2 of
  `docs/decisions/2026-08-30-004-package-mode-for-consumers.md`: one `VersionPrefix` in
  `Directory.Build.props` in place of thirteen per-project `<Version>` lines, an opt-in
  `IsReleaseBuild` that keeps every unmarked pack on `-dev`, repository provenance and the
  library's own README inside each `.nupkg`, `tools/pack.ts` as the one pack command, a
  tag-only `publish.yml`, and `docs/02-reference/versioning.md` stating what a version promises.
  Spec, plan and tasks in `docs/specs/028-versioned-package-releases/`.
