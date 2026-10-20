# Versioning and release

What a `NativeWeb.*` version number promises, what breaks one, how long a released minor is
supported, and how a library is retired. It is the release process for a library, as required by
[decision 004](../decisions/2026-08-30-004-package-mode-for-consumers.md).

## One version, every package

Every `NativeWeb.*` package carries the same version, from a single `VersionPrefix` in
`Directory.Build.props`. There is no per-library version and there will not be one.

That is a deliberate loss of precision. `NativeWeb.Mail` changes far less often than
`NativeWeb.Scaffold`, so lockstep versioning means it gets minor bumps that changed nothing in
it. The alternative costs more: libraries that reference each other, versioned
independently, produce a matrix where `NativeWeb.Web 0.4.0` and `NativeWeb.Render 0.2.0` restore
together, compile, and were never built or tested together once. `PackageVersionTests` enforces
the single version, and `tools/pack.ts` refuses to run when `Directory.Build.props` declares more
than one.

| version | what it means |
|---|---|
| `X.Y.Z` | a release. Built from a `vX.Y.Z` tag by `.github/workflows/publish.yml`, and the only thing ever pushed to the feed |
| `X.Y.Z-dev` | every other build. What a local `deno run -A tools/pack.ts` produces, and what CI packs on every push |

The suffix is opt-in in the safe direction: a release requires `IsReleaseBuild=true`, which only
the publish workflow passes. An unmarked pack cannot produce something that looks like a release.

## What each component means

Pre-1.0, and this framework is 0.x, semver's own rule is that anything may change in a minor.
That is not a useful promise to a consumer, so the stricter reading below applies now and becomes
the literal semver reading at 1.0.

| bump | means |
|---|---|
| **major** | a seam changed shape. Consumer code stops compiling, or compiles and behaves differently. Pre-1.0 this is the **minor** position: `0.1.x` → `0.2.0` |
| **minor** | new capability, nothing removed and nothing re-shaped. Pre-1.0 this shares the minor position, so a `0.x` minor bump may or may not break; the entry in `breaking-changes.md` is what says which |
| **patch** | a defect fixed or a behaviour corrected with no signature change. `0.1.0` → `0.1.1` |

**The reliable answer is not the number, it is
[`breaking-changes.md`](breaking-changes.md).** Read it top-down from the version you are on and
stop at the entry you have already taken. Every entry names the seam, what changed and
the before/after a consumer edits by.

## What counts as breaking

A change is breaking when a consumer who upgrades and rebuilds gets a compile error, or gets a
build that succeeds and does something else. Concretely, in this framework:

- **A public seam's signature changes.** `IScaffoldStore<T>.ListAsync` gaining an ordering and a
  cursor (see `breaking-changes.md`) is the worked example — every hand-written store had to be edited, and
  nothing generated did.
- **A generator's diagnostic set gains a member that fires on code that used to build.** `NWF006`
  is not breaking (nothing could declare a file field before it existed); a new `NWF00n` that
  rejects a model shape that used to compile is.
- **A configuration key changes name, default, or failure mode.** Everything in
  [`configuration.md`](configuration.md) is a seam, and the **On a bad value** column is the part
  of it a deployment depends on. Turning a silent fallback into a startup failure is breaking
  even though no code changed.
- **A library is removed**, which is the case with the longest reach and is covered below.

Not breaking: an added optional parameter with a default; a new type; a widened accepted input; a
performance change; anything under `apps/sample`, `skeleton/` or `tests/`, none of which a
consumer references.

**A breaking change is not a defect**, and this repository would rather break a seam than carry a
shim that makes the wrong shape survivable. What is owed instead is the `breaking-changes.md`
entry.

## Support window

**The current minor, and the one before it.** When `0.3.0` releases, `0.2.x` still receives
security fixes and defect fixes that apply cleanly; `0.1.x` receives nothing.

That is short on purpose. The cost of an upgrade is a rebuild and a read of
`breaking-changes.md`. A longer window would commit this repository to backporting into a shape it
has already moved past. If consumers need more time than that, this section is the first thing
that has to change.

A fix to a supported older minor releases as a patch on that minor: `0.2.4` alongside `0.3.1`.
`VersionPrefix` is bumped on a branch cut from that minor's tag, and the same tag-triggered
publish runs.

## Retiring a library

The path a departing library takes:

1. **Deprecate in place, in a minor.** The library still ships and still works. Its `README.md`
   gains a line at the top naming what replaces it and the version it is expected to go in, and
   an entry lands in `breaking-changes.md` under the deprecation date.
2. **Ship at least one release in that state**, so a consumer that upgrades sees the notice before
   the library disappears rather than at the same moment.
3. **Remove in the next breaking bump**, with a second `breaking-changes.md` entry naming the
   removal and the migration.

A consumer pinned to `0.2.x` is not moved by a removal in `0.3.0` until it chooses to be.
[Decision 007](../decisions/2026-08-31-007-idp-leaves-nativeweb.md) records a removal that
skipped these steps.

## Where the packages go, and where they do not

`.github/workflows/publish.yml` pushes to `https://nuget.pkg.github.com/<owner>/index.json`
(GitHub Packages), and there is no path in this repository to nuget.org. A package pushed there
inherits the visibility of the repository named in its nuspec, so the workflow checks that the
repository is private and publishes nothing otherwise.

**No packable library carries JauntyQ-generated output.** `grep -rn -i jauntyq libs/ --include=*.csproj`
finds comments only; the two projects that reference JauntyQ are `apps/sample` and the stamped
product under `skeleton/`, neither of which is packable. `tools/pack.ts` refuses a package
containing a JauntyQ path, which keeps that claim true: a library that grows a JauntyQ reference
would otherwise reach the feed, and the JauntyQ licence bars that (see `AGENTS.md`).

## Cutting a release

1. Read the branch's `CHANGELOG.md` under `## [Unreleased]` and decide the number against
   **What each component means** above.
2. Bump `VersionPrefix` in `Directory.Build.props` and the five `NativeWeb.*` pins in
   `skeleton/Directory.Packages.props` — `PackageVersionTests` fails if they disagree.
3. Move the changelog's `## [Unreleased]` entries under a `## [X.Y.Z]` heading, dated.
4. Merge `dev` into `main` with `--no-ff`, tag `vX.Y.Z`, and push the tag.

The tag is what publishes. `publish.yml` refuses a tag that disagrees with `VersionPrefix` before
it packs anything, so a mistyped tag costs a failed run rather than a burned version — and a
version on GitHub Packages cannot be replaced, only deleted, which breaks any consumer that has
already restored it.

## Related

- [`breaking-changes.md`](breaking-changes.md) — what changed at each seam, and the edit.
- [`configuration.md`](configuration.md) — the settings this document treats as seams.
- [`../decisions/2026-08-30-004-package-mode-for-consumers.md`](../decisions/2026-08-30-004-package-mode-for-consumers.md)
  — why consumers take packages rather than a checkout.
- `tools/pack.ts` — the pack command, and what it verifies in each produced `.nupkg`.
