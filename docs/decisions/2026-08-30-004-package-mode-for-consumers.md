# 004 — Consumers take NativeWeb as packages, not as a checkout

Date: 2026-08-30
Status: accepted
Answers: Q1 in `docs/brainstorms/001-framework-direction.md` §10

## Decision

Downstream products consume NativeWeb through pinned `NativeWeb.*` packages
(`-p:NativeWebUsePackages=true`). Checkout mode stays, but as a **co-development** convenience
for someone changing NativeWeb and a consumer in the same sitting — not as the way a consumer
normally builds.

## Why

Checkout mode makes every consumer build against `HEAD`, with no pinning. That is tolerable
across two repos and unpleasant across four, and its failure mode is silent: MSBuild only *warns*
(MSB9008) on a `ProjectReference` whose path does not resolve, so an unresolved root drops
projects from a build that still reports green. `EnsureNativeWebRoot` sentinel probes exist in
each consumer precisely because of that.

The concrete evidence arrived the same day this was decided. **justsmtp.com's checkout-mode build
has been broken for an unknown period and nobody noticed**: its `Directory.Build.props:51-52`
probes for NativeWeb at `..\..\extrode-legacy-org.com\NativeWeb` and `..\..\..\..\extrode-legacy-org.com\NativeWeb`, and
neither path has existed since this repo moved to `extrode.com/nativeweb`. A consumption model
whose breakage is invisible until someone happens to build is the wrong default.

## What this obliges, and does not

Package mode is **approved, not working**. Making it real requires, in rough order:

1. **The feed gap.** Package mode has never restored end to end, for NativeWeb or for JauntyQ.
   The same class of failure is already visible in CI: the JauntyQ restore returns 403 because
   the org grants no package read to this repo.
2. **A release process for a library, not an application.** Today `dev` → `main` plus a semver
   tag is an *application* release. A consumed package needs stated semver guarantees, a support
   window and a deprecation path, none of which exist.
3. **What may be published at all.** `AGENTS.md` forbids redistributing JauntyQ source or
   binaries. A `NativeWeb.*` package that carries JauntyQ-generated output is fine for
   self-hosted use by our own products, which is what Q0 already scoped this to (internal
   framework, decided in the brainstorm §0). A public feed is a different question and is not
   reopened here.
4. **Repointing justsmtp.com**, which needs it twice over: to package mode, and away from the
   stale `extrode-legacy-org.com` path either way.

## Rejected alternative

**Require every consumer to hold a checkout and accept lockstep upgrades.** Simpler, no feed, no
versioning policy — and it makes a NativeWeb change able to break three products at once with no
gate between them, while giving no consumer a way to stay on a known-good version while it
adapts. The justsmtp breakage above is the small version of that failure.
