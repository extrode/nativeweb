# 014 — Plan

Spec: ./014-spec.md

> **Correction, 2026-08-30 — the mechanism below was disproved during T5 and replaced.**
> The plan assumed the Request Delegate Generator would resolve a `BindAsync` that this
> generator emits. It does not: both generators run in the **same** pass over the same input
> compilation, so RDG never sees a partial another generator added. Measured on the converted
> route, RDG recorded `hasBindAsync: false`, listed the model in `jsonBodyOrServiceTypeTuples`
> with `AcceptsMetadata(application/json)`, and `ConsumesMatcherPolicy` then answered a form
> POST **415 at routing**, before any code in this feature ran.
>
> What replaced it, and what is built:
>
> | plan said | shipped |
> | --- | --- |
> | `IBindableFromHttpContext<TSelf>`, resolved by RDG | `NativeWeb.Forms.IFormModel<TSelf>`, a `static abstract` this library owns |
> | application writes `MapPost("/x", (MyForm f, …) => …)` | application writes `MapFormPost<MyForm>("/x", (f, ctx) => …)` |
> | `.RequireFormAntiforgery()` on the route, forgetting it is a build error | `MapFormPost` attaches the metadata itself; the analyzer now rejects **any** plain `Map*` with a `[Form]` parameter, because `.RequireFormAntiforgery()` no longer fixes the binding |
> | route returns `RouteHandlerBuilder` | route is a `RequestDelegate`, returning `IEndpointConventionBuilder` — the `Map*(…, Delegate)` overloads carry `RequiresDynamicCode`, and RDG cannot rewrite a call inside a generic method, so that shape failed the AOT build with IL2026 + IL3050 |
>
> This is §14's rule arriving from the other direction. There it was "a generator cannot emit a
> `Map*` call"; here it is "a generator cannot give a type a binder RDG will honour". Same cause,
> and the same fix: the route takes only `HttpContext`, so nothing is inferred.
>
> Every requirement R1–R10 is unchanged and all are met. Read the rest of this plan as the
> design that was approved, with the binding hook substituted.

## Approach

A source-generator pair, `libs/nativeweb.forms` (runtime) and `libs/nativeweb.forms.generator`
(build-time), mirroring the `nativeweb.scaffold` / `nativeweb.scaffold.generator` split that
already ships in this repo. The generator turns a `[Form]`-annotated partial record into
**variant 5b** from brainstorm 001 §12: a static `BindAsync` implementing
`IBindableFromHttpContext<TSelf>`, carrying its own `HasFormContentType` check.

**The one design point that is easy to get wrong.** §14's rule — "emit `RequestDelegate`s, never
route handlers" — is narrower than it reads, and applying it everywhere would make this feature
worse than it needs to be. The rule exists because the Request Delegate Generator only rewrites
`Map*` calls in the compilation's **own** syntax trees, so a `Map*` that a generator *emits* is
invisible to it and falls back to the reflection overload.

Here the `Map*` call is **hand-written, in the application's own tree**. RDG sees it, resolves the
`BindAsync` through a compile-time static call, and emits no diagnostic — §12 measured exactly
this as variant 5/5b: *AOT-clean, 0 warnings, correct values*. So:

- What the generator emits is the **form record's `BindAsync` body**, not the route registration.
- Applications keep writing ordinary `MapPost("/path", (MyForm form, HttpContext ctx) => …)`.
- R6 still binds `nativeweb.scaffold.generator`, which does emit route tables, and is the reason
  that rule is written into `ScaffoldEndpoints`' remarks as a general lesson. Nothing in 014
  emits a `Map*` call, so R6 is satisfied by not acquiring the problem.

**Why an analyzer is needed anyway (R7).** The generated `BindAsync` cannot attach route metadata
— it is a static method on a type, and metadata belongs to the endpoint. §12's caveat is the whole
risk of this feature: *variant 5 compiles clean, binds correctly, and answers 200 OK to a POST
with no antiforgery token.* Making binding ergonomic without closing that hole ships a foot-gun
with better ergonomics. So the second half of the deliverable is a Roslyn analyzer that fails the
build on a `Map*` whose parameter type is `[Form]`-annotated and whose chain carries no
antiforgery metadata.

Alternatives rejected:

- **An endpoint filter carrying the guards.** Structurally impossible, measured twice: filters run
  after binding (§12 finding 4, re-confirmed 2026-08-30). This is the same reason
  `AntiforgeryValidationMiddleware` is middleware.
- **Complex-type `[FromForm]`.** Not NativeAOT-compatible at all (RDG003 + IL2026 + IL3050),
  upstream-deliberate ([dotnet/aspnetcore#61382](https://github.com/dotnet/aspnetcore/issues/61382)).
- **`[AsParameters]`.** Binds every form field to `null` in silence and skips the content-type
  check. Worse than the boilerplate it replaces.
- **Extending `nativeweb.scaffold.generator`.** Scaffolding generates whole screens from a data
  model; this binds a declared form on a route someone wrote by hand. Merging them would couple a
  general binding facility to CRUD.

## Tech / dependencies

Verified against what the repo already builds, not assumed:

| | |
| --- | --- |
| Generator TFM | `netstandard2.0`, `IsRoslynComponent`, `EnforceExtendedAnalyzerRules`, `NoWarn=RS2008` — copied from `nativeweb.scaffold.generator.csproj` |
| Runtime TFM | `net10.0`, matching `nativeweb.scaffold` |
| Roslyn | `Microsoft.CodeAnalysis.CSharp` + `.Analyzers`, `PrivateAssets="all"`, versions from the existing `Directory.Packages.props` — **not pinned by hand** |
| Binding hook | ~~`IBindableFromHttpContext<TSelf>`, resolved statically by RDG~~ — **`NativeWeb.Forms.IFormModel<TSelf>`**, a `static abstract` called from `MapFormPost`. See the correction above. |
| Metadata | `IAntiforgeryMetadata`; the framework's own `AntiforgeryMetadata` is internal, so 014 mirrors `RequiresAntiforgery` in `ScaffoldValues.cs` |
| 415 mechanism | `BadHttpRequestException(…, 415)`, which `ErrorPageMiddleware` already surfaces at its own status |
| AOT gate | `dotnet publish -c Release -r win-x64`; verified working on this machine 2026-08-30 (`apps/sample` → 24.3 MB native exe, exit 0) |

## Changes by area

| Path | What changes | Requirement |
| --- | --- | --- |
| `libs/nativeweb.forms/FormAttributes.cs` | `[Form]` marker, and `[FormField(Name = …)]` for a wire name that differs from the property | R1, R4 |
| `libs/nativeweb.forms/FormBinding.cs` | Runtime helpers the emitted code calls: read a field, the 415 throw, the tri-state "absent vs empty vs present" | R2 |
| `libs/nativeweb.forms/RequiresAntiforgery.cs` | `IAntiforgeryMetadata` implementation, and the `.RequireFormAntiforgery()` extension that attaches it | R3 |
| `libs/nativeweb.forms/IFormModel.cs` | The `static abstract` binder contract, replacing `IBindableFromHttpContext` | R1 |
| `libs/nativeweb.forms/FormEndpointRouteBuilderExtensions.cs` | `MapFormPost`/`Put`/`Patch`/`Delete`: maps a `RequestDelegate`, calls the binder, attaches the antiforgery metadata and `AcceptsMetadata` | R2, R3 |
| `libs/nativeweb.forms.generator/FormGenerator.cs` | Incremental generator: find `[Form]` records, validate, emit | R1, R5, R8 |
| `libs/nativeweb.forms.generator/FormModel.cs` | The parsed model — members, wire names, diagnostics | R8 |
| `libs/nativeweb.forms.generator/FormEmitter.cs` | Emits `BindAsync` with the content-type check first | R1, R2, R6 |
| `libs/nativeweb.forms.generator/MissingAntiforgeryAnalyzer.cs` | `Map*` + `[Form]` parameter + no metadata → build error | R7 |
| `apps/web/Auth/RegisterEndpoints.cs` | Converted as the proof, domain logic untouched | R9, R10 |
| `tests/nativeweb.forms.tests.unit/` | Generator snapshot + diagnostic tests, analyzer tests | R1–R8 |
| `tests/nativeweb.web.tests.integration/` | The three measured statuses on the converted routes | R9, R10 |
| `apps/sample` | One generated form, so the AOT publish gate has something to compile | R5 |

## Data / interfaces

```csharp
[Form]
public sealed partial record RegisterForm(
    string? FirstName,
    string? LastName,
    string? Username,
    string? Email,
    string? Passphrase,
    [FormField("confirmPassphrase")] string? ConfirmPassphrase);   // positional, not Name =
```

Generated (shape, not final text):

```csharp
partial record RegisterForm : IFormModel<RegisterForm>
{
    public static async ValueTask<RegisterForm> BindFormAsync(HttpContext context)
    {
        // FIRST. Not a filter, not middleware: binding runs upstream of the filter
        // pipeline, so this is the only place the check can sit and still precede
        // the read.
        if (!context.Request.HasFormContentType)
        {
            throw new BadHttpRequestException("Expected a form content type.", StatusCodes.Status415UnsupportedMediaType);
        }
        …
    }
}
```

Route, hand-written, and the shape is the correction:

```csharp
endpoints.MapFormPost<RegisterForm>("/auth/register", (form, ctx) => …);
// Both guards come with the mapping call. A plain MapPost with a [Form] parameter
// is a build error, because it does not merely lose the metadata -- it binds the
// model as a JSON body and answers a form POST 415 at routing.
```

## Risks & mitigations

| Risk | Mitigation |
| --- | --- |
| **The foot-gun survives.** A hand-written `BindAsync` without metadata answers 200 OK to a tokenless POST — measured. | R7's analyzer, and it is a build **error**, not a warning. Mutation-checked: a route missing the call must fail the build. |
| **AOT breakage invisible to `dotnet build`.** §14's 22 IL2026/IL3050 errors only appeared on publish. | A publish gate in CI and in `/verify`, not a build gate. `apps/sample` carries a generated form so there is something to publish. |
| **Converting a live auth endpoint changes behaviour.** `/auth/register` carries enumeration defence and bot traps. | R9 forbids editing any existing assertion. The suites are the contract; a test that needed changing means behaviour moved. |
| **Ordering regression.** The 415 must precede the antiforgery rejection, or a JSON POST with no token reports the wrong one. §12 already saw this flip. | The three statuses are asserted directly and mutation-checked (R10), extending the pattern `RequireServicesFilterTests` established. |
| **Generator emits for a malformed model.** | R8: diagnostics, with a test per diagnostic asserting the build fails rather than that broken code compiles. |
| Security-adjacent surface (antiforgery, content type, an auth endpoint). | Per global `AGENTS.md`, review for correctness goes to the Claude subscription (`review`), and Fable's `review-deep` is asked layering questions with the framing that worked on 2026-08-30. |

## Coverage check

| Requirement | Covered by |
| --- | --- |
| R1 declared model binds | `FormEmitter`, `FormAttributes`; generator unit tests |
| R2 415 before the handler | `FormEmitter` content-type check; integration status assertion |
| R3 antiforgery metadata | `RequiresAntiforgery` + `RequireFormAntiforgery()`; mutation-checked |
| R4 undeclared field unreachable | Emitter binds only declared members; a test posts an extra field |
| R5 zero AOT warnings | `apps/sample` publish gate |
| R6 no emitted `Map*` | Satisfied by construction — the generator emits no route registration; asserted by a test that the generated output contains no `Map` call |
| R7 missing metadata fails the build | `MissingAntiforgeryAnalyzer` + analyzer tests |
| R8 malformed model diagnoses | `FormModel` validation + one test per diagnostic |
| R9 behaviour unchanged | Existing suites, assertions unedited |
| R10 three statuses preserved | Integration tests, each mutation-checked |

**No uncovered requirement.**
