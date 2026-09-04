# 028 — Versioned package releases

Status: draft
Created: 2026-09-04

## Problem

A product that wants to build on NativeWeb has one way to do it: hold a checkout of this
repository and build against whatever `dev` happens to be. That is gap **G1** in
`docs/architecture/004-consumer-readiness.md`, and it is what broke the two consumers that tried:
`justsmtp.com` pins `NativeWeb.*` at `0.1.0` with a comment saying those versions "resolve to
nothing", and `ghosthive.com` recorded a migration plan naming two libraries that have since left
the framework. Neither could have pinned a known-good version, because none has ever been
published.

The groundwork is further along than the gap description says. Thirteen projects under `libs/`
set `IsPackable` and a `PackageId`; `Directory.Build.targets` already centralises `Authors`,
`Copyright`, `PackageProjectUrl` and the ISL-NETC license file; `skeleton/nuget.config` already
declares the `framework-github` source and source-maps `NativeWeb.*` to it; and
`PackageMetadataTests` already guards the central metadata. `dotnet pack` on a library succeeds
today and produces a well-formed `.nupkg`.

What is missing is everything that turns a pack into a **release**: a version that is declared
once rather than thirteen times, a way to tell a released build from any other build, a workflow
that publishes on a tag, and a written statement of what a version number promises.

[Decision 004](../../decisions/2026-08-30-004-package-mode-for-consumers.md) approved package
mode on 2026-08-30 and listed four obligations. This spec discharges the second — a release
process for a library rather than an application — and makes the first provable locally without
depending on the feed grant it names.

## Goals

- One version number for the framework, in one file, with a test that keeps it there.
- A `.nupkg` that says where it came from: repository URL, commit, and the library's own README.
- A tag-triggered publish that pushes every packable project to the private feed, and pushes
  nothing on any other trigger.
- A stated versioning policy: what a major, minor and patch mean here, what counts as breaking,
  and how a library is deprecated before it is removed.
- A stamped product whose pinned versions agree with what the framework publishes.

## Non-goals

- **Publishing to nuget.org.** `nativeweb.security.csproj` states why: the packages are
  proprietary and some carry JauntyQ-generated output, which the JauntyQ license limits to
  self-hosted use. The private feed is the only destination.
- **Running a release.** Building the workflow is this spec; tagging and pushing is the user's
  call and is not done here.
- **The JauntyQ feed grant.** Obligation 1 of decision 004 is an org-side permission, not work in
  this repository.
- **Changing `justsmtp.com` or `ghosthive.com`.** Obligation 4 belongs in those repositories, as
  `docs/architecture/004-consumer-readiness.md` §5 states.
- **Adding or removing any library.** The packable set is what it is today.

## User stories

- As an operator of a downstream product, I want to depend on a fixed NativeWeb version, so that
  a framework change cannot break my build the moment it merges.
- As a maintainer of NativeWeb, I want one place to set the version, so that a release is one
  edit rather than thirteen that can disagree.
- As a consumer reading a package on the feed, I want its README and its source commit, so that I
  can tell what it does and which code is in it.
- As a maintainer cutting a release, I want the publish to happen from a tag and nowhere else, so
  that a merge to `dev` cannot ship a package by accident.

## Requirements

- **R1** No packable project declares its own version. A single version property in a repository
  file is the source of truth for every `NativeWeb.*` package, and all of them carry the same
  version.
- **R2** A build that is not from a release tag produces a version a consumer cannot mistake for
  a release, and a build from a release tag produces the stable version that tag names.
- **R3** Every produced package records the repository URL and the commit it was built from.
- **R4** A library whose project directory holds a `README.md` ships that file as the package
  README. A library without one packs without failing.
- **R5** Every packable project packs without warnings, in package mode, from one command that a
  person can run locally and that CI can run unchanged.
- **R6** Publishing happens only on a version tag. No other trigger — push, pull request, manual
  dispatch of the existing CI — pushes a package anywhere.
- **R7** The version the skeleton pins agrees with the source of truth in R1, and disagreement
  fails a test rather than a stamped product's restore.
- **R8** The repository states its versioning policy: the meaning of each semver component for
  this framework, what constitutes a breaking change to a library seam, the support window for a
  released minor, and the deprecation path for a library that is going away.
- **R9** Each of R1 through R7 is asserted by a test that fails when the thing it guards is
  broken.

## Acceptance criteria

- **R1** — Given a packable csproj, when it declares a `<Version>`, then the repository test tier
  fails naming that project. Given the source-of-truth property is changed, when every packable
  project is packed, then every `.nupkg` carries the new version.
- **R2** — Given a build with no release tag information, when a package is produced, then its
  version carries a pre-release suffix. Given a build told it is release `X.Y.Z`, then the
  version is exactly `X.Y.Z`.
- **R3** — Given any produced `.nupkg`, when its nuspec is read, then `repository` carries both a
  `url` and a `commit`.
- **R4** — Given `libs/nativeweb.render` has a `README.md`, when it is packed, then the package
  contains that file and its nuspec names it as the readme. Given a library with no `README.md`,
  when it is packed, then pack succeeds and emits no NU5039.
- **R5** — Given a clean tree, when the pack command runs, then thirteen `.nupkg` files are
  produced and the command's output contains no `NU5` or `MSB` warning.
- **R6** — Given the publish workflow, when its triggers are read, then the only one that reaches
  a push step is a tag matching the release pattern.
- **R7** — Given `skeleton/Directory.Packages.props`, when any `NativeWeb.*` pin disagrees with
  the source-of-truth version, then the repository test tier fails naming the pin.
- **R8** — Given the versioning document, when a reader asks what a minor bump promises, what
  breaks a seam, how long a minor is supported and how a library is retired, then each has a
  stated answer.

## Open questions

None. The destination feed, the license position and the scope boundary are all settled by
decision 004 and by `AGENTS.md`.
