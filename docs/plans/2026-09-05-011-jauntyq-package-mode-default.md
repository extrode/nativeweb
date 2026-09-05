# Plan — JauntyQ package mode becomes the default

Standard tier. Branch: `chore/jauntyq-package-default`.

## Context

**JauntyQ split, and this repository's default build path points at the half that left.**
`../jauntyq` on its `dev` now holds five projects — `Extrode.JauntyQ.{Registry,Schema.Contract,
Explain,Licensing,Cli.Premium}` — and its `JauntyQ.slnx` lists only those. The generator and
runtime sources moved to a public core repository that is **not** checked out anywhere under
`/c/home/code/extrode.com/`. (`../jauntyq/tmp/public-core-3/` is that repo's own scratch, and
`../jauntyq/.worktrees/consolidate-lessons/` still carries the pre-split names. Neither is a
checkout we can build against.)

Measured consequences, all present on `dev` today:

| site | state |
|---|---|
| `Directory.Build.props:118` | sentinel probes `src\JauntyQ.Generator\JauntyQ.Generator.csproj`; never matches, so **every** local build fails fast unless `-p:JauntyQUsePackages=true` is passed |
| `NativeWeb.slnx:42-43` | names both dead csproj paths as solution members, so a bare root `dotnet build` cannot even restore, flag or no flag |
| `apps/sample/nativeweb.sample.csproj:76,80` | `ProjectReference`s the same two dead paths in checkout mode |
| `skeleton/libs/__product__.data/__product__.data.csproj:24,28` | same, inherited by every stamped product |
| `scripts/deploy/build.sh:231` | passes `-p:JauntyQUsePackages=false`, selecting the mode that cannot restore |
| package mode | **works** — `JauntyQ.Generator` / `JauntyQ.Runtime` **0.4.0** from `~/.nuget/packages`; package IDs are unchanged by the split |

My earlier `work/todo.md` note called this a project rename. It is a repository split, and the fix
is not a new sentinel path.

Intended outcome: **a bare `dotnet build` works again**, using the pinned packages. Checkout mode
survives as an opt-in for whoever does clone the public core, so nothing is deleted — only the
default inverts.

## Approach

Flip the polarity of one property everywhere it is read: `JauntyQUsePackages` defaults to `true`,
and checkout mode is selected with `-p:JauntyQUsePackages=false`. The probe block, the two-depth
`.worktrees` probe and `EnsureJauntyQRoot` all stay exactly as written — they are correct, they
just stop firing on the default path.

Rejected: repointing the probes at the public core's layout. Its real project paths on its own
`dev` are not knowable from here (`tmp/public-core-3` shows `Extrode.JauntyQ.Generator`, but that
is scratch), and guessing a path is what produced the `extrode-legacy-org.com` breakage `JauntyQRootProbeTests`
exists to prevent.

## Changes by area

**`Directory.Build.props`** — add `<JauntyQUsePackages Condition="'$(JauntyQUsePackages)' == ''">true</JauntyQUsePackages>`
to the resolution `PropertyGroup`; `EnsureJauntyQRoot`'s condition becomes `== 'false'`. Rewrite
the "Package mode is opt-in ONLY" comment block to say why the default moved, citing the split and
the new decision doc. Add a `ReportJauntyQResolution` `<Message Importance="normal">` target
mirroring `skeleton/Directory.Build.props`'s `ReportFrameworkResolution`, so the resolved mode is
visible in a build log rather than inferred.

**`NativeWeb.slnx`** — delete lines 42-43. This is the change that makes a root `dotnet build`
possible; 34 project members remain. Nothing reads the solution for its contents — the eleven
tests that mention it use it only as a repo-root marker (`ActionPinTests.cs:247` is representative).

**`apps/sample/nativeweb.sample.csproj:75-86`** — the two `ItemGroup` conditions invert to
`'$(JauntyQUsePackages)' == 'false'` (ProjectReference) and `!= 'false'` (PackageReference).

**`skeleton/Directory.Build.props` and `skeleton/libs/__product__.data/__product__.data.csproj`** —
the same flip, **for JauntyQ only**. `NativeWebUsePackages` stays opt-in: the `NativeWeb.*`
packages have never been published (no `v*` tag; decision 004 obligation 1 is the org-side read
grant), so a stamped product still needs the framework checkout.

**`scripts/deploy/build.sh:222-231`** — `-p:JauntyQUsePackages=false` becomes `=true`, and the
comment paragraph is rewritten: it currently explains that `false` "IS the default" and guards
against inheriting package mode by accident, which inverts.

**`skeleton/.github/workflows/ci.yml`** — the jauntyq clone step and the workflow-level
`JauntyQRoot` become dead weight pointing at a repo without the projects. Remove both; keep every
`NativeWebRoot` step untouched.

**`tools/stamp-check.ts:86-96`** — already sets `JauntyQUsePackages: "true"` explicitly and clears
`JauntyQRoot`; leave the explicit setting, drop the now-pointless `JauntyQRoot: ""` line only if it
reads cleaner. `tools/new-product.ps1:111`'s reminder text drops `JauntyQRoot`.

**CI (`.github/workflows/ci.yml`, `publish.yml`)** — left as-is. Six explicit
`JauntyQUsePackages: "true"` sites become redundant rather than wrong, and they document the mode
at the point of use. Noted in the decision doc so the redundancy is deliberate rather than missed.

**Docs** — the prose everywhere says a checkout is required and packages are CI-only:
`README.md:20-48` (prerequisite table, the three bullets, the layout diagram, the MSB3202 claim
that removing the slnx members retires), `docs/00-quick-start/getting-started.md:40-45`,
`docs/00-quick-start/adopting-in-an-existing-app.md:20-34`, project `AGENTS.md:48`,
`skeleton/AGENTS.md:37`, `skeleton/README.md:119`.

**`docs/decisions/2026-09-05-012-jauntyq-package-mode-default.md`** — the split, the six broken
sites measured above, the flip, what stays opt-in and why, and the two things left open below.

**Visibility** — `CHANGELOG.md` under `## [Unreleased]`; `work/status/current.md`;
`work/todo.md`, where the "renamed its projects to `Extrode.JauntyQ.*`" note is corrected to a
repo split and closed.

## Tests

New `tests/nativeweb.repo.tests.unit/JauntyQResolutionModeTests.cs`, in the shape of
`FrameworkTemplateDeliveryTests` — it drives real MSBuild rather than asserting over file text,
which is the only way this fact can fail honestly:

- `dotnet msbuild apps/sample/nativeweb.sample.csproj -getProperty:JauntyQUsePackages` with no
  flags returns `true`.
- The same with `-p:JauntyQUsePackages=false` returns `false`, and
  `-getItem:ProjectReference` then contains a `JauntyQ` path while `-getItem:PackageReference`
  does not — the two modes really are exclusive.
- The skeleton's copy answers the same way, and its `NativeWebUsePackages` still defaults empty.

`SkeletonFrameworkResolutionTests` loses its `JauntyQRoot` half (the `foreach` over
`{ "NativeWebRoot", "JauntyQRoot" }` at line 53); its `NativeWebRoot` assertions stay. Check
`JauntyQRootProbeTests` still passes untouched — the probe block is not being edited.

Falsified per the standing order: green, commit, then break each of the three new facts (remove
the default assignment; leave one condition un-inverted; put a `JauntyQ` PackageReference in the
checkout-mode ItemGroup) and confirm red, then `git checkout --`.

## Verification

Every command with **no** `-p:JauntyQUsePackages` flag, which is the whole point:

```
dotnet build NativeWeb.slnx -v q --nologo
dotnet build apps/sample/nativeweb.sample.csproj -v q --nologo
dotnet test --project tests/<tier> -v q
```

Tiers compared against recorded baselines, not just green: render **219**, forms **148**,
scaffold **268**, permissions **151**, sample unit **231**, repo unit **183** plus the new file.
A tier green at a different count means discovery changed and is investigated first.

Then `deno run -A tools/stamp-check.ts` (the one place a stamped product is built and tested),
`deno run -A ~/.claude/tools/structure-audit.js`, and a full two-lens `/code-review` before
`--no-ff` into `dev`.

The solution build is the headline assertion: it has never succeeded in package mode
(`docs/specs/010-.../010-tasks.md:122` records `MSB3202` on exactly these two members), and
removing them is what changes that.

## Risks

- **CI cannot prove any of this.** All jobs are already red on the JauntyQ package `403`; the
  workflow edits are reviewed, not exercised.
- **Package mode restores from the local NuGet cache, not a reachable feed.** 0.4.0 is present in
  `~/.nuget/packages`; a clean machine gets `NU1101`. That gap is pre-existing and documented in
  `NuGet.config`'s own comment — this change makes the default path depend on it, which is a real
  widening of an existing hole and is stated as such in the decision doc rather than fixed here.
- **The pin stays at 0.4.0.** jauntyq is holding 0.5.0 (`c64e809`, "record the 0.5.0 hold"), and
  bumping across a repo split with no reachable feed is separate work.

## Needs decision (not changed by this branch)

- **`AGENTS.md`: "JauntyQ is used from a local checkout only. Permitted use is self-hosted-SaaS use
  of the generated output."** After this branch the default build uses packages, so the first
  sentence is no longer true of how we build. It is a **licence** statement, not a build-mode one,
  and rewriting a licence claim is not mine to do. Flagged, left verbatim.
- **Whether to clone the public JauntyQ core as a sibling.** If it is cloned, checkout mode needs
  its real project paths and `EnsureJauntyQRoot`'s sentinel updated to match. Needs the repo URL
  and its `src/` layout from you; I will not guess either.
