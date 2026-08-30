# 014 — Declarative form binding for hand-written endpoints

Status: draft
Created: 2026-08-30

Implements **T1.2** from [brainstorm 001](../../brainstorms/001-framework-direction.md), whose two
measurement spikes (§12, 2026-08-19; §14, same day) settled the design and are the input to this
spec rather than work it repeats.

## Problem

A form POST in this codebase carries a preamble of guards that the platform will not supply and
will not warn about. `RegisterEndpoints.cs` declares four routes in 417 lines; `SignInEndpoints`,
`PassphraseEndpoints`, `SettingsEndpoints` and `AdminEndpoints` repeat the same opening moves.

The cost is not the line count. §12 measured that: rewriting `RegisterEndpoints.cs` with stock
ASP.NET ergonomics took it from 393 lines to 378 plus a 59-line reusable filter, because roughly
250 of those lines are enumeration defence, bot-trap timing equalisation, stale-registration
reclaim and code-attempt handling — domain logic no generator removes. **The estimate that
motivated T1.2 ("393 → roughly 120") was wrong by a wide margin, and this spec does not restate
it as a goal.**

The cost is that **two of the guards fail silently when they are forgotten, and the platform's
declarative binding paths are the ones that forget them.** Measured, §12:

| Binding shape | AOT-clean | Binds form values | 415 on wrong content type | 400 on missing token |
| --- | --- | --- | --- | --- |
| `[FromForm] ComplexType` | **No** — RDG003 + IL2026 + IL3050 | — | — | — |
| `[AsParameters]` + `[FromForm]` members | Yes | **No — silently all null** | **No — 200 OK** | **No — 200 OK** |
| `[FromForm]` scalars + `RequireForm()` | Yes | Yes | Yes | **No — 500, not 400** |
| `BindAsync` / `IBindableFromHttpContext<T>` | Yes | Yes | **No — 500** | **No — 200 OK** |
| `BindAsync` + reattached guards (**variant 5b**) | Yes | Yes | Yes | Yes |

A form POST with **no antiforgery token returned 200 OK** under two of those shapes. Nothing
warned. The declared-model API the framework wants is reachable under NativeAOT only through
`BindAsync`, and reaching it by hand costs both guards unless the author remembers to reattach
them — which is a foot-gun, not an ergonomic gain.

Three facts constrain any fix, all measured rather than argued:

1. **Argument binding runs upstream of the entire endpoint-filter pipeline.** A guard written as
   an `AddEndpointFilter` cannot protect the bind. This is why the antiforgery fix
   (`AntiforgeryValidationMiddleware`, 2026-08-19) is middleware, and it was re-confirmed on
   2026-08-30: with all gated services removed, a JSON POST to `/auth/register` answers 415, not
   the 503 the first-chained filter would give. `RequireForm()` turned out to be **redundant on
   both routes carrying it** — the RDG's own `HasFormContentType` check is what rejects.
2. **The Request Delegate Generator does not run on another generator's output** (§14). Roslyn
   does not feed one incremental generator's `AddSource` into another's input, so an emitted
   `MapPost` binds the reflection overload and fails the build under repo-wide
   `TreatWarningsAsErrors` — 22 IL2026/IL3050 errors on the first sample publish. Emitted
   endpoints must be `RequestDelegate`s.
3. **The shape already works here.** `nativeweb.scaffold.generator` emits exactly this — a
   `HasFormContentType` check, `ScaffoldEndpoints.Handle(...)` as a `RequestDelegate`, and
   `RequiresAntiforgery.Instance` (an `IAntiforgeryMetadata`) on the route — and publishes AOT
   clean. What does not exist is a way for a **hand-written** endpoint to get the same thing.

## Goals

- A hand-written endpoint can declare its form as a record and receive it bound, with the
  content-type guard and the antiforgery metadata attached by construction rather than by memory.
- Every generated artefact publishes NativeAOT with zero warnings under `TreatWarningsAsErrors`.
- Omitting a guard becomes a **build failure**, not a silent 200.
- The domain logic in the existing endpoints is untouched; this changes how a request becomes a
  typed value, not what the handler then does with it.

## Non-goals

- **Line-count reduction as a success measure.** §12 settled that; the win is guard integrity.
- Replacing `nativeweb.scaffold.generator`, or generating CRUD screens (that is T3.1).
- A validation-attribute framework. Field-level validation stays where it is unless a requirement
  below names it.
- Rewriting `AdminEndpoints.cs`, `SettingsEndpoints.cs` or the OAuth endpoints. One endpoint file
  is converted as the proof; the rest is follow-on work.
- Removing `RequireForm()` / `RequireServices<T>()`. They stay as defence in depth for shapes
  whose binder does not check.

## User stories

- As a NativeWeb application author, I want to declare a form as a record and have the framework
  bind it, so that I cannot ship a route that accepts a JSON body or a missing antiforgery token
  because I forgot two lines.
- As a reviewer, I want a route that hand-writes its own `BindAsync` to fail the build when it
  omits the antiforgery metadata, so that review is not the thing standing between a foot-gun and
  production.
- As a maintainer publishing under NativeAOT, I want generated endpoints to carry no trim or AOT
  diagnostic, so that a publish is not where I discover a generator's mistake.

## Requirements

- **R1** A form model declared in application code is bound from the request body without the
  author writing a `BindAsync` body by hand.
- **R2** The generated binder rejects a request whose content type is not a form, with 415, before
  any handler code runs.
- **R3** The generated route carries antiforgery metadata, so a form POST with no valid token is
  rejected with 400.
- **R4** An undeclared field is unreachable from the handler. The declaration is the contract
  (Rails' strong parameters, enforced by the compiler rather than at runtime).
- **R5** Every generated artefact compiles and publishes NativeAOT with **zero** warnings, under
  the repo's existing `TreatWarningsAsErrors`.
- **R6** Generated endpoints are emitted as `RequestDelegate`s, never as route handlers, so the
  Request Delegate Generator's absence from generated trees cannot reintroduce IL2026/IL3050.
- **R7** A hand-written route that binds a form without antiforgery metadata **fails the build**
  with a diagnostic naming the route and the missing metadata.
- **R8** The generator reports a diagnostic, rather than emitting broken code, when a declared
  form model is malformed (unsupported member type, no accessible constructor, duplicate binding
  name).
- **R9** One existing endpoint file is converted to the new shape with its behaviour unchanged, as
  demonstrated by the existing unit, integration and e2e suites passing without modification to
  their assertions.
- **R10** The existing guards' behaviour is preserved: the measured statuses for a JSON POST
  (415), a tokenless form POST (400) and a route whose gated services are absent (503) are the
  same before and after.

## Acceptance criteria

- **R1–R4** Given a declared form record and a route that uses it, when a well-formed form POST
  arrives with a valid token, then the handler receives the typed value with every declared field
  populated; and when the same POST carries an extra undeclared field, then it is not observable
  from the handler.
- **R2** Given the same route, when a JSON body is posted, then the response is 415 and no handler
  code runs. Asserted by observing the status, and by a test that fails if the check moves behind
  the bind.
- **R3** Given the same route, when a form POST arrives with no antiforgery token, then the
  response is 400. Mutation-checked: removing the metadata turns this red.
- **R5** `dotnet publish -c Release -r <rid>` of the app containing a generated endpoint completes
  with zero warnings. This is a publish-time gate, not a build-time one — §14's failure was
  invisible to `dotnet build`.
- **R7** Given a route that hand-writes a form bind and omits the metadata, when the solution is
  built, then the build fails and the message names the route.
- **R9** Given the converted endpoint file, when the full unit, integration and e2e suites run,
  then they pass with no assertion changed. A test whose assertion had to be edited to
  accommodate the new shape is a behaviour change and fails this criterion.
- **R10** The three measured statuses are asserted directly, and each is mutation-checked.

## Open questions

None blocking. Two decisions are deferred to `014-plan.md` because they are HOW, not WHAT: which
library the generated code lands in (`nativeweb.forms` beside `nativeweb.scaffold`, or inside an
existing one), and whether R7's diagnostic is an analyzer or a check inside the generator.
