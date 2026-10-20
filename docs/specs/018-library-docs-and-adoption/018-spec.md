# 018 — Library documentation and the adoption path

Status: done
Created: 2026-09-01

## Problem

A framework nobody can find the seams of gets reimplemented by hand, and this one already has
been. Measured on 2026-09-01 in `docs/architecture/004-consumer-readiness.md`: `epass.io`
references `nativeweb.render` and `nativeweb.security`, uses **zero** NativeWeb types across its
19 source files, and carries five files that each name in a comment the NativeWeb file they were
copied from — the page shell, the CSP-and-nonce middleware, the clean-URL rewrite, flash banners
and content-hashed asset URLs. Every one of them now ships in `libs/nativeweb.web`. The port was
finished the day before the library completed, by a repository that had no way to know the
library was coming.

Two causes, both documentation:

1. **Six of the nine runtime libraries document nothing.** Only `nativeweb.scaffold` and
   `nativeweb.signin` carry a README (`nativeweb.assets` has one and is a vendored asset drop,
   not a library). `nativeweb.web` — the largest seam, and the one that was reimplemented — has
   none.
2. **Every guide assumes a greenfield product.** `getting-started.md` goes clone → build → green
   suite → `tools/new-product.ps1`. Not one of the three consumers can take that path: each has
   an existing application, its own `Directory.Build.props`, its own solution and its own
   deployment. The document that would serve them — how an existing ASP.NET Core app adds
   NativeWeb, what it can delete afterwards, and what it must keep — does not exist.

A third defect compounds both. `docs/02-reference/configuration.md` is 302 lines describing
`apps/web`, an application that left this repository on 2026-08-31. Every key it names is
`NativeWeb:`-prefixed; the deployable binds `Sample:`. It is served live at `/docs` on
`nw-dev.example.com`, so the one page a consumer would open to answer "what can I configure?"
answers it for software that no longer exists.

## Goals

- A developer who has never read this repository can tell, from a library's own README, what it
  does and whether they need it, without opening its source.
- An existing ASP.NET Core application can adopt NativeWeb by following one document, end to end,
  without a NativeWeb contributor answering questions.
- The configuration reference describes the configuration that exists.
- The five things `epass.io` hand-ported are discoverable by name from the README of the library
  that provides them.

## Non-goals

- **Fixing any consumer repository.** `justsmtp.com`'s dead probe path and `ghosthive.com`'s
  stale adoption plan are work in those repositories. This spec makes the documentation they
  would need correct; it does not go and edit them.
- **G1, package mode.** The adoption guide documents the referencing model that works today. It
  does not build a feed, a versioning policy or a release process — that is decision 004's
  obligations 2 through 4, and it stays out of this spec.
- **New framework features.** Whether NativeWeb should ship `robots.txt` and `sitemap.xml` as
  generic primitives is a real question recorded in `work/todo.md`; it is not answered here.
  Documentation may only describe what the code does.
- **A README for the three source generators.** `nativeweb.forms.generator`,
  `nativeweb.render.generator` and `nativeweb.scaffold.generator` are implementation details of
  the runtime libraries that ship them, and a consumer never references one directly. Each is
  covered in its runtime library's README rather than given its own; stated here so `/plan` does
  not treat it as an omission.
- **Rewriting `getting-started.md`.** It is correct for the greenfield path. The adoption guide
  is the other document, not a replacement.

## User stories

- As a developer on an existing ASP.NET Core app, I want one document that tells me which
  NativeWeb libraries to reference and what to call, so that I can adopt the framework without
  reading its source or asking its authors.
- As a developer who has already hand-written a page shell, CSP headers or asset hashing, I want
  a library README that names what it replaces, so that I can delete my copy instead of
  maintaining it.
- As an operator deploying a NativeWeb product, I want a configuration reference whose keys match
  the ones the binary reads, so that a value I set has an effect.
- As a NativeWeb contributor, I want each library's README to state its own scope boundary, so
  that the next feature request lands in the right library or is refused for a stated reason.

## Requirements

- R1 — Each of the six runtime libraries without a README gets one: `nativeweb.web`,
  `nativeweb.render`, `nativeweb.forms`, `nativeweb.security`, `nativeweb.mail`,
  `nativeweb.signin.testing`.
- R2 — Each README states, at minimum: what the library is for, the public entry points a
  consumer calls, a runnable example, what the library deliberately does not do, and which test
  suite proves its behaviour.
- R3 — `nativeweb.web`'s README is written first and names, individually, the five capabilities
  `epass.io` hand-ported, in terms a reader who has written their own version would recognise.
- R4 — Every type, method, option key and file path named in a README exists in the tree at the
  commit that adds it.
- R5 — A new document describes adoption by an existing ASP.NET Core application: how it
  references NativeWeb, the registration calls it makes, what it can delete once adopted, and
  what it must keep.
- R6 — The adoption document states the referencing model that works today, and says which models
  do not work and why, rather than describing an intended future one.
- R7 — `docs/02-reference/configuration.md` is rewritten to describe configuration the tree
  reads: the section-prefixed keys the deployable binds, the options surface the framework
  exposes, and the deploy-time environment contract.
- R8 — No key documented in the rewritten configuration reference is unread by `libs/`, `apps/`
  or `scripts/`, and no key those read is undocumented.
- R9 — Every new or rewritten document is reachable by navigation from `docs/README.md`, and
  every one that lives under `docs/` appears in the generated `docs-site/`.

  **Amended 2026-09-02, by the user's decision.** It read "and appears in the generated
  `docs-site/`" without qualification, which a library README cannot satisfy: `build-docs.sh`
  passes one input tree to the shared generator in the sibling `../docs` repository, and that
  generator takes a single `input-dir`. Meeting it literally would mean either a second copy of
  each README under `docs/` — the duplication this spec exists to stop — or changing a tool three
  projects consume, which is outside these non-goals. A README stays beside its code and is
  reached from the index; the two library READMEs that predate this spec are outside the site on
  the same terms.
- R10 — The claims in the documentation are checked by something that fails when they go stale,
  not by having been true once.

## Acceptance criteria

- **Given** a reader with no prior knowledge of this repository, **when** they open
  `libs/nativeweb.web/README.md`, **then** they can name the five capabilities it provides and
  the call that turns each on, without opening a `.cs` file.
- **Given** the rewritten `configuration.md`, **when** every key it documents is searched for in
  `libs/`, `apps/` and `scripts/`, **then** each is read by at least one of them; and when the
  configuration binding in `apps/sample` and `libs/nativeweb.web` is enumerated, every key it
  reads appears in the document. `CanonicalBaseUrl` is the known case of the first kind — it is
  read nowhere and must not survive the rewrite. `SecurityContact` is **not**: it is read at
  `NativeWebPipeline.cs:545` and feeds `/security.txt`, and a note in `work/todo.md` claiming
  otherwise is wrong and is corrected as part of this work.
- **Given** the adoption guide, **when** its steps are followed against a minimal ASP.NET Core
  application, **then** that application builds, serves a NativeWeb-rendered page and passes its
  own tests, with every command in the guide having been run rather than reasoned about.
- **Given** any document produced by this spec, **when** a type or path it names is renamed or
  removed, **then** a test fails. Amended 2026-09-02, after a review of the merged branch found
  the absolute overstated: the guard covers backticked repository paths, `$(NativeWebRoot)`-rooted
  paths inside fenced blocks, relative links, whole-word symbol names, and configuration keys in
  both directions. It does not cover paths written relative to the reader's own tree (they name
  nothing in this repository), a symbol whose name survives as a whole word elsewhere in the
  sources, or a claim entered in `ExemptClaims` with a reason. `DocsClaimsTests` states each of
  those in place, and `TheKeyExtractors_StillSeeEveryKeyShapedLiteralInTheBinders` is what stops
  the config half of the guard narrowing silently.
- **Given** the docs site build, **when** it runs, **then** the adoption guide and the rewritten
  configuration reference are present in `docs-site/`, the six READMEs are linked from
  `docs/README.md`, and no link in any of them resolves to a missing target. Amended with R9 on
  2026-09-02: the generator takes one input tree and the READMEs live beside their code.

## Open questions

None. `configuration.md`'s fate was decided on 2026-09-01: rewrite it against what exists, over
retiring it or scoping it to the deployable.
