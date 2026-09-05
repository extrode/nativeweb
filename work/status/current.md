# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Ten outdated NuGet pins bumped. Done on `chore/package-bumps`, 2026-09-05.** Raised from the
  NuGet Solution "Updates" list: AngleSharp, Geralt, Mvc.Testing, TestHost, Logging.Abstractions,
  Playwright and the four OpenTelemetry packages, in both `Directory.Packages.props` files.
  Playwright 1.49 -> 1.62 is the largest jump and the scaffold e2e tier passes 31/31 on the
  installed chromium. Two of the twelve offered were refused with a measurement:
  `Microsoft.CodeAnalysis.CSharp` 5.9.0 and `.Analyzers` 5.9.0 cannot load, because the SDK at
  `C:/home/lang/dotnet/sdk/10.0.203` ships Roslyn 5.3.0 and a generator referencing a newer
  `Microsoft.CodeAnalysis` than its host compiler fails CS9057. Staying at 4.12.0 rather than
  moving to 5.3.0 is the user's call, taken 2026-09-05: an analyzer package's pin is a
  compatibility floor for consumers of the four published generator packages, and nothing in them
  calls a 5.x API. Every tier re-run at its recorded baseline, repo 183 with the new drift guard.

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
