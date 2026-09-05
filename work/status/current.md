# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

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
