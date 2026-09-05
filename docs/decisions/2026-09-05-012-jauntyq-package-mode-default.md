# 012 — JauntyQ resolves from packages by default

Date: 2026-09-05
Status: accepted
Supersedes: nothing. It records a reversal of the default set in
`../../Directory.Build.props`, whose comment block carried the old reasoning and now carries this.

## Decision

**`JauntyQUsePackages` defaults to `true`.** A bare `dotnet build` resolves JauntyQ from the pinned
`JauntyQ.Generator` / `JauntyQ.Runtime` packages; `-p:JauntyQUsePackages=false` selects a local
checkout. It was the other way round until today.

`NativeWebUsePackages` in `skeleton/Directory.Build.props` is **not** flipped with it. No
`NativeWeb.*` package has published — there is no `v*` tag, and obligation 1 of decision
`2026-08-30-004` is still the org-side package read grant — so a stamped product still needs the
framework checkout.

## Why

JauntyQ split. `../jauntyq` on its `dev` now holds five projects — `Extrode.JauntyQ.Registry`,
`.Schema.Contract`, `.Explain`, `.Licensing` and `.Cli.Premium` — and its `JauntyQ.slnx` lists only
those. The generator and runtime sources moved to a public core repository, which is not checked
out anywhere under the org directory. `../jauntyq/tmp/public-core-3/` is that repository's own
scratch and `../jauntyq/.worktrees/consolidate-lessons/` still carries the pre-split names; neither
is a checkout anything can build against.

Six sites here referenced the half that left. Measured on `dev` before the change:

| site | state |
|---|---|
| `Directory.Build.props` `EnsureJauntyQRoot` | sentinel probes `src\JauntyQ.Generator\JauntyQ.Generator.csproj`, never matches, so **every** build failed fast without the flag |
| `NativeWeb.slnx:42-43` | both dead csproj paths as solution members, so a root `dotnet build` could not restore, flag or no flag |
| `apps/sample/nativeweb.sample.csproj:76,80` | `ProjectReference` to both, in checkout mode |
| `skeleton/libs/__product__.data/__product__.data.csproj:24,28` | the same, inherited by every stamped product |
| `scripts/deploy/build.sh:231` | `-p:JauntyQUsePackages=false`, selecting the mode that cannot restore |
| `skeleton/.github/workflows/ci.yml` | cloned `extrode/jauntyq` into a `JauntyQRoot` that would not carry either project |

The package IDs are unchanged by the split, so package mode kept working throughout. That is the
whole argument: the default moved to the path that works.

## What was not changed, and why

**The probe block and the sentinel.** `Directory.Build.props`'s two-depth `JauntyQRoot` probe is
correct and `JauntyQRootProbeTests` still pins it; it simply stops firing on the default path. The
sentinel still names the **pre-split** csproj. Repointing it would mean guessing the public core's
layout from a scratch directory, and a probe pointing somewhere nobody verified is the
`extrode-legacy-org.com` breakage that test exists to prevent. Whoever clones the public core updates the
sentinel in the same commit.

**The six explicit `JauntyQUsePackages: "true"` sites in `.github/workflows/ci.yml` and
`publish.yml`.** Redundant now rather than wrong, and they document the mode at the point of use.
Deliberate, not missed.

**The pin, at 0.4.0.** jauntyq is holding 0.5.0 (`c64e809`, "record the 0.5.0 hold"), and bumping
across a repo split with no reachable feed is separate work.

## What this removed

`NativeWeb.slnx` listed JauntyQ's two projects unconditionally so a checkout could co-develop them,
and that listing is what made a solution-wide restore fail on a runner with no sibling. `ci.yml`
worked around it with a pair of steps — `dotnet sln remove` before restore, `git checkout --`
before the first test step, the second only because the test guarding the first read the file the
first had emptied. The removal step's own comment said to delete it if the projects ever went for
real. They have, and it is gone, along with its partner and the two tests that paired them.

`skeleton/.github/workflows/ci.yml` loses the `extrode/jauntyq` clone from both jobs and the
workflow-level `JauntyQRoot`. Nothing there needed a token for JauntyQ any more.

## What was verified

Every command with **no** flag on the command line, which is the point:

| check | result |
|---|---|
| `dotnet build NativeWeb.slnx` | **succeeded, 0 warnings, 0 errors** — it had never succeeded in package mode before (`docs/specs/010-documentation-and-tutorials/010-tasks.md:122` records `MSB3202` on exactly these two members) |
| `dotnet build apps/sample/nativeweb.sample.csproj` | succeeded, 0 warnings, 0 errors, under repo-wide `TreatWarningsAsErrors` |
| render / forms / scaffold / permissions / sample unit tiers | 219 / 148 / 268 / 151 / 231, all at baseline, 0 failed, 0 skipped |
| repo unit tier | 189 — baseline 183, plus 7 new, minus the merged pair |

`JauntyQResolutionModeTests` asks MSBuild for the resolved property and the resolved item lists
rather than reading the props file, because a test over the XML asserts that the file says what it
says, which is true of an inverted condition too. Five breaks were checked and each failed the
test that names it: the default assignment removed, one `ItemGroup` condition left un-inverted, a
JauntyQ member put back in the solution, the `dotnet sln remove` step put back in `ci.yml`, and the
skeleton's default assignment removed.

The third of those found a defect in the new test itself: the external-project check ran the path
through `Normalize()`, which trims the leading dots, so `p.StartsWith("..")` could never be true
and the assertion was vacuous. Fixed in `cd14c7d` and re-broken to confirm it fires.

## Open

- **`AGENTS.md`: "JauntyQ is used from a local checkout only."** After this change the default
  build resolves packages, so that sentence no longer describes how we build. It is a **licence**
  statement rather than a build-mode one, and it is left verbatim: rewriting a licence claim is the
  user's call, not a session's.
- **Package mode restores from the machine's NuGet cache, not a reachable feed.** 0.4.0 is present
  in `~/.nuget/packages`; a clean machine gets `NU1101`. Pre-existing and documented in
  `NuGet.config`'s own comment — but the default path depends on it now, which is a real widening
  of an existing hole.
- **Whether to clone the public JauntyQ core as a sibling.** If it is cloned, checkout mode needs
  that repository's real project paths in both `ProjectReference` pairs and in both `Ensure*Root`
  sentinels.
