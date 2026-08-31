# 015 — Plan

Spec: ./015-spec.md

## Approach

**Assert, then move, then adopt, then shrink.** The order is the design: the invariants have to
exist as tests in `tests/` before a file changes directory, because the whole risk of this spec is
that a move breaks a property nothing was checking. T7 is therefore not preparation for the work —
it is the work's safety net, and it gates everything after it.

The alternative considered and rejected was moving first and testing the result. It is faster and
it proves nothing: a test written after the move asserts what the moved code does, which is the
question being begged.

**One library, not five.** `libs/nativeweb.web` takes all of it. Splitting by concern
(`nativeweb.web.security`, `nativeweb.web.cache`, …) produces five projects whose only consumer is
each other and a pipeline whose ordering constraints span package boundaries, which is where an
ordering constraint stops being enforceable. The house style the scaffold already sets is the one
followed: *"this method hides nothing; it is exactly four calls, every one of them public and
individually callable."*

**Extract from `skeleton/`, not from `apps/web`.** The assessment described this as extracting from
the reference application; that application left with the identity provider on 2026-08-31, and
every file named in the spec now exists only under `skeleton/apps/__product__/`. Verified by
`git grep` on 2026-08-31: `PublicPageCache`, `SecurityHeadersMiddleware`, `ErrorPageMiddleware`,
`Chrome`, `AssetUrls`, `Flash`, `StartupConfigGuards` and `ReadinessCache` have no other home.

**`apps/sample` adopts the seam in the same spec.** Otherwise the library's only consumer is a
directory no local build compiles, and the first real exercise of the moved code is in CI on a
stamped product — which is the position this spec exists to end.

## Constraint found in T1: `Chrome` binds to a per-product generated symbol

`Chrome.For` and `Chrome.Anonymous` both call `Locales.Get(context.GetLocale())`
(`skeleton/apps/__product__/Render/Chrome.cs:33,65`). `Locales` is not a framework type — the
render generator emits it into `build_property.RootNamespace` from that project's own
`locales/*.json` (`libs/nativeweb.render.generator/TemplateEmitter.cs:222-224`), so it exists once
per product and nowhere else. **`Chrome` therefore cannot move into `libs/nativeweb.web`
unchanged**, because a framework library has no `locales/` to generate from.

Found by compiling the skeleton sources into `tests/nativeweb.web.tests.unit` and getting two
CS0103s. The test project works around it the way a product does — `RootNamespace` set to
`__PRODUCT__` and its own `locales/en.json` plus an `fr.json` the locale-separation assertion
needs — which proves the coupling rather than removing it.

Two ways out for T4, to be decided there rather than here:

- **The caller supplies the catalog.** `Chrome.For(context, assets, antiforgery, LocaleCatalog)`.
  The framework stops knowing about `Locales` at all; every call site gains an argument.
- **`Chrome` stays product-side.** It is 66 lines and its anonymous/authenticated split is the
  product's own decision about what a page shows. R2 lists it as moving, so choosing this is a
  spec amendment, not a silent narrowing.

Either way T1's assertions hold unchanged, which is what writing them first bought.

## Tech / dependencies

No new dependencies. `libs/nativeweb.web` targets the same `net10.0`, `IsAotCompatible`, and
`TreatWarningsAsErrors` the repo sets in `Directory.Build.props`. It takes a `FrameworkReference` on
`Microsoft.AspNetCore.App` and a `ProjectReference` on `libs/nativeweb.render` for `Chrome` and the
CSP-hash surface.

Asset delivery needs an MSBuild `.targets` in the library's `build/` folder that copies its
`wwwroot` contents into the consumer's output, because `AssetUrls` fingerprints and precompresses
physical files and embedded resources bypass both (R7).

## Changes by area

- `tests/nativeweb.web.tests.unit` (new) — the spec-006 invariant assertions and the moved skeleton
  unit tests. **R1, R5, R10.**
- `libs/nativeweb.web` (new) — the ten middleware, `Chrome`, `AssetUrls`, `Flash`,
  `StartupConfigGuards`, `ReadinessCache`, `CspReportEndpoint`, `security.txt`. **R2, R6, R7.**
- `libs/nativeweb.web/NativeWebOptions.cs` — the hand-bound options record. **R4.**
- `libs/nativeweb.web/PipelineExtensions.cs` — `AddNativeWebWeb` and `UseNativeWebPipeline`, every
  step public. **R3, R5.**
- `apps/sample/Program.cs` — adopts the two calls. **R9.**
- `skeleton/apps/__product__/` — deletes the moved files, `Program.cs` shrinks. **R8.**
- `docs/02-reference/performance.md` — the before/after measurement. **R11.**

## Data / interfaces

```csharp
public sealed record NativeWebOptions
{
    public required string ContentSecurityPolicy { get; init; }
    public bool Hsts { get; init; }
    // ... bound by hand in FromConfiguration(IConfiguration), no reflection
    public static NativeWebOptions FromConfiguration(IConfiguration config);
}

public static class NativeWebPipeline
{
    public static IServiceCollection AddNativeWebWeb(this IServiceCollection s, NativeWebOptions o);
    public static IApplicationBuilder UseNativeWebPipeline(this IApplicationBuilder app);
    // and every step it calls, public and individually callable
}
```

## Risks & mitigations

- **A move that breaks an invariant nothing asserts.** The whole reason for T7 gating T8. If an
  invariant cannot be expressed as a test, that is a finding to record before moving the code it
  describes, not a reason to move it anyway.
- **The pipeline is security-relevant** — CSP, HSTS, antiforgery cookie hardening, the
  cache/identity separation. Per `~/.claude/AGENTS.md` this is flagged, and per the measured note
  in memory, review goes to the `review` class rather than `review-deep`, which refuses
  security-guard prompts while still billing.
- **`apps/sample` gains ten middleware it does not have today, so it will get slower.** R11 makes
  that measured and recorded rather than prohibited. The baseline to compare against is Phase 1's
  close: `articles` 18,958.6 req/s, `healthz` 23,324.5.
- **A stamped product is the only integration test of the skeleton half**, and CI is the only place
  that runs it. Mitigated by `apps/sample` adopting the same seam, which a local build does compile.

## Coverage check

| Requirement | Covered by |
|---|---|
| R1 | `tests/nativeweb.web.tests.unit`, T7 |
| R2 | `libs/nativeweb.web`, T8 |
| R3 | `PipelineExtensions.cs`, T8 |
| R4 | `NativeWebOptions.cs`, T8 |
| R5 | `PipelineExtensions.cs` + ordering test, T7 and T8 |
| R6 | T8's CSP-hash handling; asserted against `apps/sample`'s own templates |
| R7 | the `.targets` in T8 |
| R8 | T9 |
| R9 | T9 (`apps/sample` adoption) |
| R10 | T7 (moved tests) |
| R11 | T11, the measurement |
| R12 | every task; the build gates it |

No requirement is uncovered.

## Constraint found in T7: two assets at one target path is a build error

R7 assumed the framework's assets could keep the paths they already had —
`/css/base.css`, `/vendor/nativeweb/boot.js` — with the library's copy replacing each
app's vendored one. Measured on 2026-08-31 against a scratch consumer: it cannot. The
static web assets pipeline treats a target-path collision as an error, not a precedence
question:

```
Microsoft.NET.Sdk.StaticWebAssets.targets(640,5): error : Conflicting assets with the
same target path 'vendor/nativeweb/boot#[.{fingerprint}]?.js'.
```

So adopting the `.targets` at the old paths would have required deleting every app's
vendored copy in the same commit, or the build fails outright. Two ways out:

1. **Delete the copies with the change.** Correct end state, but removal in this repo is
   a script the user runs, so the branch would sit broken between the commit and that run.
2. **Give the framework a base path no app occupies** — chosen. Assets are served under
   `/_nativeweb/` (`/_nativeweb/boot.js`, `/_nativeweb/css/base.css`), which is
   underscore-prefixed the way `_framework` and `_content` are, and cannot collide with an
   app's own `wwwroot/css/base.css`. The vendored copies become unreferenced leftovers,
   listed for `scripts/cleanup/015-framework-web-seam.ps1`, and the build is green before
   and after that script runs.

Consequences, all landed in T7: the three stylesheets and `boot.js` moved from
`libs/nativeweb.assets/` to `libs/nativeweb.web/wwwroot/_nativeweb/`; the sample's and the
skeleton's markup point at the new paths; `AssetParityTests` stops asserting that N copies
agree — there is one copy — and instead asserts the framework ships what it claims,
that every `/_nativeweb/` URL in consumer markup resolves to a delivered file, and that any
leftover copy has not drifted (absence passes: the copies are transitional).

A `build/*.targets` is auto-imported by NuGet for a `PackageReference` and not for a
`ProjectReference`, so the checkout mode imports it by path — one `<Import>` line in
`apps/sample/nativeweb.sample.csproj` and one, conditioned on `NativeWebUsePackages`, in
the skeleton's.

Verified by publishing `apps/sample`: `wwwroot/_nativeweb/` in the publish output carries
`boot.js`, `css/{base,components,tokens}.css` and a `.br` and `.gz` beside each, and the
endpoint manifest carries a fingerprinted route per asset.
