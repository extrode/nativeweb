# 014 — Tasks

Plan: ./014-plan.md

Order is dependency order. Each task ends green, committed, and with its new tests
mutation-checked per global `AGENTS.md` — **commit before mutating**, because an uncommitted
`git checkout --` reverts to the parent branch and loses the work (cost that once already on
2026-08-30).

- [x] T1 The runtime library skeleton — files: `libs/nativeweb.forms/nativeweb.forms.csproj`,
      `FormAttributes.cs` — covers: R1, R4 — done when: `[Form]` and `[FormField]` exist, the
      project builds with zero warnings under `TreatWarningsAsErrors`, and it is referenced from
      the solution.

- [x] T2 The 415 and the metadata — files: `libs/nativeweb.forms/FormBinding.cs`,
      `libs/nativeweb.forms/RequiresAntiforgery.cs` — covers: R2, R3 — done when: the throw helper
      produces a `BadHttpRequestException` at 415, `RequiresAntiforgery` implements
      `IAntiforgeryMetadata` with `RequiresValidation => true`, and `.RequireFormAntiforgery()`
      attaches it. Unit-tested directly.

- [x] T3 The generator project — files:
      `libs/nativeweb.forms.generator/nativeweb.forms.generator.csproj`, `FormGenerator.cs`
      (skeleton) — covers: R5 — done when: an empty incremental generator is wired the way
      `nativeweb.scaffold.generator` is (`netstandard2.0`, `IsRoslynComponent`,
      `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`) and a consuming project builds.

- [x] T4 Parse the model — files: `libs/nativeweb.forms.generator/FormModel.cs` — covers: R1, R4,
      R8 — done when: a `[Form]` record yields its members and wire names, and each malformed case
      (unsupported member type, no accessible constructor, duplicate wire name) yields a
      diagnostic rather than an emit. One test per diagnostic.

- [x] T5 Emit `BindAsync` — files: `libs/nativeweb.forms.generator/FormEmitter.cs` — covers: R1,
      R2, R6 — done when: the emitted partial implements `IFormModel<TSelf>` (**not**
      `IBindableFromHttpContext<TSelf>`, which the plan named: measured 2026-08-30, RDG runs in the
      same pass and cannot see this partial, so it bound the model as a JSON body and routing
      answered a form POST 415 — see `014-plan.md`), the
      `HasFormContentType` check is the **first** statement, only declared members are read, and
      the emitted text contains no `Map` call. Snapshot-tested.

- [x] T6 The analyzer — files:
      `libs/nativeweb.forms.generator/MissingAntiforgeryAnalyzer.cs` — covers: R7 — done when: a
      `Map*` call whose parameter type is `[Form]`-annotated reports an **error** naming the
      route and the fix, and `MapFormPost` reports nothing. Widened from the plan's wording after
      the T5 measurement: `.RequireFormAntiforgery()` no longer excuses a plain `Map*`, because the
      missing metadata was never the whole defect. This is the task that closes §12's foot-gun;
      without it the feature is net-negative.

- [x] T7 Prove it end to end in the sample — files: `apps/sample/**` — covers: R5 — done when:
      `apps/sample` declares one `[Form]` record on a real route and
      `dotnet publish -c Release -r win-x64` completes with **zero** warnings. This is the gate
      §14's failure was invisible to `dotnet build` for.

- [x] T8 Convert `/auth/register` — files: `apps/web/Auth/RegisterEndpoints.cs` — covers: R9, R10
      — done when: the two POST routes bind through a `[Form]` record, the domain logic is
      untouched, and every existing unit, integration and e2e assertion passes **unedited**. An
      assertion that had to change means behaviour moved and fails the task.

- [ ] T9 Pin the three statuses — files:
      `tests/nativeweb.web.tests.integration/RequireServicesFilterTests.cs` (extend) or a new file
      — covers: R10 — done when: 415 for a JSON POST, 400 for a tokenless form POST and 503 for a
      route with a gated service absent are each asserted on the converted routes and each
      mutation-checked.

- [ ] T10 Records and close-out — files: `docs/specs/014-declarative-form-binding/014-spec.md`
      (Status), `docs/brainstorms/001-framework-direction.md` (T1.2 resolved),
      `work/status/current.md`, `CHANGELOG.md`, `scripts/cleanup/014-declarative-form-binding.ps1`
      — done when: acceptance criteria are re-read against what was built, `structure-audit`
      reports 0 errors, and the cleanup script exists unrun.

## Analyze — requirement coverage

| R# | Tasks | |
| --- | --- | --- |
| R1 declared model binds | T1, T4, T5 | covered |
| R2 415 before the handler | T2, T5, T9 | covered |
| R3 antiforgery metadata | T2, T9 | covered |
| R4 undeclared field unreachable | T1, T4 | covered |
| R5 zero AOT warnings | T3, T7 | covered |
| R6 no emitted `Map*` | T5 | covered |
| R7 missing metadata fails the build | T6 | covered |
| R8 malformed model diagnoses | T4 | covered |
| R9 behaviour unchanged | T8 | covered |
| R10 three statuses preserved | T9 | covered |

No orphan tasks: every task traces to a plan section. No uncovered requirement.

**One risk the ordering carries deliberately.** T8 converts a live authentication endpoint, and it
comes after T6 rather than before it, so the analyzer that makes the missing-metadata case a build
error already exists when the first real route is converted. Converting first and adding the
analyzer later would mean the one endpoint most worth protecting spent the intervening commits
protected by nothing but review.
