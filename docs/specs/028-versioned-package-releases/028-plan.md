# 028 — Plan

Spec: ./028-spec.md

## Approach

Plain MSBuild, one new Deno tool, one new workflow. **No versioning package** — not
Nerdbank.GitVersioning, not MinVer. Both would put a build-time dependency and a git-history read
into a repository that has `TreatWarningsAsErrors` on everywhere and a hand-audited restore graph,
and both solve a problem this framework does not have: a version derived from history is useful
when releases are frequent and unattended, and this one is tagged by a person who already decides
the number. The cost of the manual form is one edit per release, and R7's test makes that edit
impossible to half-do.

The version therefore lives in `Directory.Build.props` as `VersionPrefix`, and a **release build
is an opt-in**: `IsReleaseBuild=true` clears the suffix, and anything else produces
`X.Y.Z-dev`. That direction matters. The alternative — default to a stable version and add a
suffix in the non-release case — makes an unmarked local pack indistinguishable from a release,
and the one thing a feed must never receive is a package that claims to be a release and is not.
Opt-in means the accident produces `-dev`, which no consumer pins and the publish workflow
refuses to push.

Pack verification does not go in the xUnit tiers. Packing thirteen projects is a build, not a
unit test, and asserting a nuspec requires the pack to have happened. Instead the split is:

| what | asserted where | why |
|---|---|---|
| static wiring: no local `<Version>`, tag-only trigger, skeleton pin agreement | `tests/nativeweb.repo.tests.unit`, structural XML/YAML parse | cheap, runs in every suite, catches the edit that breaks it |
| produced `.nupkg` contents: version, repository url + commit, README | `tools/pack.ts`, which packs and then opens each package | the only place the fact exists is inside the file that was produced |

`tools/pack.ts` is Deno per `AGENTS.md`, sits beside `tools/mutation-triage.ts` and takes a
`tools/pack.test.ts` beside it the same way. It is the R5 command: one invocation a person runs
locally and CI runs unchanged.

## Tech / dependencies

- **No new NuGet package.** Everything is SDK-native MSBuild.
- **Deno 2.9.5** (present), for `tools/pack.ts`. Standard library only; a `.nupkg` is a zip, and
  reading one needs `DecompressionStream` plus the central-directory parse, both in the runtime.
- **.NET SDK 10.0.203** (present). `dotnet pack -p:IsReleaseBuild=true`.
- GitHub Actions: `actions/checkout@v7`, the only `uses:` the repo permits
  (`docs/decisions/2026-08-20-001-node-runtime-in-ci.md`).

## Changes by area

### `Directory.Build.props` — R1, R2

Add a version block: `VersionPrefix` as the single source of truth, and

```xml
<VersionSuffix Condition="'$(IsReleaseBuild)' != 'true'">dev</VersionSuffix>
```

with a comment stating the opt-in direction and why it is that way round. `VersionPrefix` goes
here rather than in `Directory.Build.targets` because it is unconditional — every project gets a
version, packable or not — while the pack metadata beside it in `.targets` is conditioned on
`IsPackable`, which is not yet known when `.props` is evaluated.

### `libs/*/*.csproj` × 13 — R1

Delete `<Version>0.1.0</Version>` from each. Thirteen deletions, no other change.

### `Directory.Build.targets` — R3, R4

Extend the existing `Condition="'$(IsPackable)' == 'true'"` group:

- `RepositoryUrl` and `RepositoryType`. The nuspec today carries `commit` and no `url`, so a
  package names a revision nobody can resolve to a repository.
- `PackageReadmeFile` set only when `$(MSBuildProjectDirectory)\README.md` exists, with the
  matching `None ... Pack="true"`. Nine of the thirteen have one; the four generators do not, and
  the `Exists` guard is what keeps their pack from emitting NU5039 for a file it was told about.
- `ContinuousIntegrationBuild` when `$(CI)` is set, for a deterministic build.

### `tools/pack.ts` (new) + `tools/pack.test.ts` (new) — R5, and the R3/R4 assertion

Enumerates `libs/*/*.csproj`, keeps those with `IsPackable`, runs `dotnet pack` per project into
`artifacts/packages/`, then opens each produced `.nupkg` and asserts: the version matches the
resolved `VersionPrefix` (+ suffix), the nuspec `repository` carries both `url` and `commit`, and
a project with a `README.md` produced a package that contains it and names it. Any `NU5` or
`warning` line in pack output is a failure. `--release` passes `-p:IsReleaseBuild=true`.

The test file covers the parsing and predicate logic against fixtures — which csproj is packable,
which pack output counts as a warning, how a nuspec is read — without invoking `dotnet`.

### `.github/workflows/publish.yml` (new) — R6

`on: push: tags: ['v[0-9]+.[0-9]+.[0-9]+']` and nothing else. `permissions: contents: read,
packages: write`. Steps: checkout, authenticate the feed the way `ci.yml` already does, a step
that reads `VersionPrefix` out of MSBuild and **fails when the tag does not match it**, then
`deno run -A tools/pack.ts --release`, then push every `.nupkg`. The tag check is the guard that
makes R1's single source of truth mean something at release time rather than only in a test.

### `.github/workflows/ci.yml` — R5

One step in the existing `test` job: `deno run -A tools/pack.ts`. It proves the pack path on
every push without publishing anything, which is what stops the publish workflow from being the
first place a pack failure is discovered.

### `skeleton/Directory.Packages.props` — R7

The five `NativeWeb.*` pins move from the literal `0.1.0` to whatever `VersionPrefix` says. They
are equal today, so this is a no-op edit that the new test makes load-bearing.

### `tests/nativeweb.repo.tests.unit/` — R9

- `PackageVersionTests` (new): exactly one `VersionPrefix` in `Directory.Build.props`; no packable
  csproj declares a `<Version>`; every `NativeWeb.*` pin in `skeleton/Directory.Packages.props`
  equals it. Shaped like `PackageMetadataTests`, whose `PackableProjects` helper and `RepoRoot`
  it reuses in form.
- `PublishWorkflowTests` (new): the publish workflow's triggers are tags only, its permissions
  include `packages: write`, and no other workflow in `.github/workflows/` contains a push step.
  Parsed structurally, not by substring: a read that answers "which triggers exist" rather than
  "does this string appear".

### `docs/02-reference/versioning.md` (new) — R8

What major/minor/patch mean for this framework, what a breaking change to a seam is (spec 017's
`IScaffoldStore<T>.ListAsync` change is the worked example, already recorded in
`breaking-changes.md`), the support window for a released minor, and the deprecation path for a
library that is leaving — the case `nativeweb.signin.epass` went through with no path at all.
Linked from `docs/02-reference/breaking-changes.md` and from the decision-004 obligations.

### Record — R8 and the flow's own requirements

`CHANGELOG.md` under Unreleased, `work/status/current.md`, `work/milestones.md` (milestone 6),
`docs/architecture/004-consumer-readiness.md` G1 marked closed with what closed it, and G6
corrected while there — it says seven of nine libraries have no README, and ten of fourteen have
one now.

## Data / interfaces

No runtime API changes. The contract this spec adds is a build-time one:

| property | set by | effect |
|---|---|---|
| `VersionPrefix` | `Directory.Build.props` | the version of every `NativeWeb.*` package |
| `IsReleaseBuild` | the publish workflow only | clears the `-dev` suffix |
| `NativeWebUsePackages` | consumer, unchanged | already exists in `skeleton/Directory.Build.props` |

## Risks & mitigations

- **A release built by accident.** The whole reason the suffix is opt-in. A local
  `dotnet pack` produces `-dev`, and the publish workflow refuses a tag that does not match
  `VersionPrefix`.
- **Publishing something the license does not permit.** `nativeweb.security` and the two
  generators that reference JauntyQ carry generated output whose license limits it to
  self-hosted use. The feed is private and stays private; the workflow pushes to
  `nuget.pkg.github.com/extrode` and has no nuget.org path. `docs/02-reference/versioning.md`
  states this rather than leaving it in one csproj comment.
- **The publish workflow cannot be proved without running it**, and running it is a push. It will
  be built, reviewed and left untriggered; the pack half is proved locally and in CI, which is
  the part that can be. Stated in the report rather than implied.
- **`dotnet pack` on a generator project.** An analyzer packs to `analyzers/dotnet/cs`, not
  `lib/`, and each of the four already sets that up for its own reasons; verifying the produced
  layout is part of `tools/pack.ts` rather than assumed.

## Coverage check

R1 → `Directory.Build.props`, the 13 csproj deletions, `PackageVersionTests`. R2 → the
`IsReleaseBuild` block, `tools/pack.ts --release`. R3, R4 → `Directory.Build.targets`,
`tools/pack.ts`. R5 → `tools/pack.ts`, the ci.yml step. R6 → `publish.yml`,
`PublishWorkflowTests`. R7 → `skeleton/Directory.Packages.props`, `PackageVersionTests`.
R8 → `docs/02-reference/versioning.md`. R9 → the two new test classes plus `tools/pack.test.ts`.
Nothing uncovered.
