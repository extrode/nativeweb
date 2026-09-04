# 029 — Plan
Spec: ./029-spec.md
Branch: `feat/029-framework-templates`

## Context

Spec 015 R7 moved the framework's stylesheets and `boot.js` out of every consumer's `wwwroot/`
into `libs/nativeweb.web`, delivered by the build, on the argument that "a framework whose CSS fix
reaches a product only when somebody re-runs a copy is a framework that cannot ship a CSS fix". It
stopped at static assets. Four HTML partials that render framework markup from framework code were
left as per-product copies:

| template | rendered from | `apps/sample` | `skeleton/` |
|---|---|---|---|
| `shared/antiforgery.html` | `Chrome.For` → `IAntiforgery` | yes (11 call sites) | yes, byte-identical |
| `shared/bot-traps.html` | `Chrome.For` → `BotTraps.IssueCanary` | yes | yes, byte-identical |
| `shared/banner-ok.html` | `Flash.Take` | **no** | yes |
| `shared/banner-error.html` | `Flash.Take` | **no** | yes |

The sharp end is `bot-traps.html`: it hardcodes `contact_website`, `fax_number`,
`company_website`, `phone_extension` and `form_session` as literal text, and those are the names
`BotTraps` defines in `libs/nativeweb.security` and reads back on submit. Rename a constant and
every product's copy silently stops matching — no compile error, no test failure, no runtime
error, just bot detection quietly off. `AssetParityTests` covers `wwwroot/css` and
`wwwroot/vendor`; it does not compare `templates/`.

The user chose, 2026-09-05: deliver all four from the framework, **and** wire the sample so it can
render a flash banner, which it cannot today.

## Grounding facts (verified this session)

- **`RenderTemplateGenerator.LogicalPath` keys on the last `/templates/` segment of the physical
  path**, wherever that path is (`libs/nativeweb.render.generator/RenderTemplateGenerator.cs:71`).
  A file at `libs/nativeweb.web/templates/shared/antiforgery.html` resolves to
  `shared/antiforgery` — the name it has today. **No generator change is needed for naming, and no
  call site changes.**
- **Out-of-tree `AdditionalFiles` already work here**: `apps/sample/nativeweb.sample.csproj:120`
  declares `..\..\db\sample\queries\**\*.sql`.
- **The delivery mechanism exists**: `libs/nativeweb.web/build/NativeWeb.Web.targets` contributes
  `wwwroot/**` as linked `Content`, gated on `NativeWebImportAssets`. Packed by
  `nativeweb.web.csproj:68-69` as two `<None … Pack="true">` lines, with `build/` and `wwwroot/`
  as siblings in both the package and the repo, so one relative path serves `PackageReference` and
  `ProjectReference` alike.
- **A same-logical-path collision is silent today.** `RenderTemplateGenerator.cs:54` does
  `models[path] = …`, so two files at one logical path collapse to one entry *before* the emitter
  runs its NWT009 check — and NWT009 only catches *different* paths colliding on a class name.
  Last-wins, ordered by MSBuild item order. This is what R4 forbids, and it is the one real design
  problem in the spec.
- **The targets is imported before the product declares its own templates**
  (`nativeweb.sample.csproj:63` vs `:102`; skeleton `:107` vs `:111`). So a collision check cannot
  be an `ItemGroup` in the targets — it has to be a `Target`, which runs after evaluation.
- **The two layouts have diverged.** `skeleton/…/templates/layout/page.html` takes eight
  parameters including `FlashOk`/`FlashError` and renders the banners at lines 54-55;
  `apps/sample/templates/layout/page.html` takes two, `Title` and `AntiforgeryToken`. The sample
  has 11 layout call sites (`grep -c 'AntiforgeryToken:' apps/sample/*.cs`) and
  `apps/sample/PublicForms.cs` has no redirect, so there is no post-redirect-get to carry a flash.
- `Flash` has unit coverage (`tests/nativeweb.web.tests.unit/FlashTests.cs`) and no rendering
  coverage anywhere.
- The banner CSS classes `.banner`, `.banner--ok`, `.banner--err` are already framework-owned
  (`libs/nativeweb.web/wwwroot/_nativeweb/css/components.css:175-193`).

## Deliverables

### 1. The four templates, owned by the framework (R1–R3)

- **`libs/nativeweb.web/templates/shared/{antiforgery,bot-traps,banner-ok,banner-error}.html`** —
  moved with `git mv` from `apps/sample` and `skeleton/` so history follows. The skeleton's copies
  are the source for the two banners; either copy serves for the other two, which are identical.
- **`libs/nativeweb.web/nativeweb.web.csproj`** — one line beside the existing pair:
  `<None Include="templates\**\*" Pack="true" PackagePath="templates\" />`.
- **`build/NativeWeb.Web.targets`** — a `NativeWebTemplatesDirectory` property mirroring
  `NativeWebAssetsDirectory`, and an item group contributing `$(NativeWebTemplatesDirectory)**\*.html`
  as `AdditionalFiles`.

### 2. Override, deterministic and diagnosed (R4, R5)

Resolved in MSBuild, before the generator sees a collision. Two switches, both mirroring the
naming of the existing `NativeWebImportAssets`:

- **`NativeWebImportTemplates=false`** — contribute nothing (R5). A call site naming a framework
  template then fails at compile time, which is the right failure.
- **`<NativeWebTemplateOverride Include="shared\bot-traps.html" />`** — the product declares it is
  replacing one. The targets removes that file from the framework set, and a `<Message>` names
  what was overridden and by which project.

**An undeclared collision is a build error**, raised from a
`<Target BeforeTargets="CoreCompile">` — it must be a target, not an item group, because the
product's own `<AdditionalFiles>` are declared after the import. The target intersects the
framework set against the product's `templates/**/*.html` on logical path and errors listing every
undeclared clash, telling the author to declare the override or delete the copy.

**Rejected: teaching the generator instead**, via
`<CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="…" />` and a new NWT
diagnostic. It works, and it is worse: it moves a build-configuration question into the compiler,
and the failure arrives as a diagnostic against generated source rather than a build error naming
two file paths. MSBuild already owns which files are `AdditionalFiles`; the answer belongs where
the question is.

### 3. Both consumers stop carrying them (R6)

Delete the four from `apps/sample/templates/shared/` and
`skeleton/apps/__product__/templates/shared/`. Neither csproj changes: each already globs
`templates\**\*.html`, and the framework's arrive through the import. The 11 sample call sites and
the skeleton's layout are untouched — the logical names are identical.

### 4. The sample renders a flash banner (R9)

- **`apps/sample/templates/layout/page.html`** — `@param string FlashOk` and
  `@param string FlashError`, and the two guarded partial calls the skeleton's layout already has
  at its lines 54-55. The `{{if}}` stays at the call site, not in the partial:
  `Flash.cs:65` records why — a partial whose body is a bare else-less `{{if}}` bails the
  build-time HTML formatter for every page.
- **The 11 call sites** in `apps/sample/*.cs` each gain `FlashOk:` and `FlashError:` arguments.
  Anonymous/cacheable pages pass `""` for the reason `Chrome.Anonymous` documents: a flash is a
  per-response byte and an output-cached page must not carry one.
- **One form becomes post-redirect-get** so there is a real flash to show. The contact form is the
  natural one — it already has a success path and `apps/sample/PublicForms.cs` currently renders a
  result page inline.

### 5. Tests (R7, R8, R9, R10)

- **`tests/nativeweb.web.tests.unit`** — the constant/markup coupling, both directions: every
  `BotTraps` field-name constant appears as a `name="…"` in `bot-traps.html`, and every
  `name="…"` in the template is a constant `BotTraps` defines. Read the constants off the type,
  not a hand-copied list, or the test is the third copy of the same strings.
- **`tests/nativeweb.repo.tests.unit`** — a framework-owned template does not reappear under a
  consumer's `templates/` without a declared `NativeWebTemplateOverride` (R8). This is the
  `AssetParityTests` pattern extended to the dimension it does not cover; whether it joins that
  class or gets its own is the implementer's call.
- **`tests/nativeweb.sample.tests.integration`** — a request that sets a flash, followed by the
  redirect that reads it, yields a response carrying the banner markup and expiring the flash
  cookie in the same response (R9). Asserted through a real request, not by the file existing.
- **`tools/stamp-check.ts`** — passes unchanged, every step `ok` with a non-zero test count (R10).
  A stamped product is the only place `PackageReference` delivery is proved, since both in-repo
  consumers use `ProjectReference`.

### 6. Documentation (R12)

`docs/02-reference/breaking-changes.md` (a product carrying its own copy must now declare the
override or delete it), `libs/nativeweb.web/README.md`, and `libs/nativeweb.assets/README.md`,
whose "each app still vendors its own templates" framing this supersedes. Plus `CHANGELOG.md`
under Unreleased, `work/status/current.md`, and spec → `Status: done`.

## Risks

- **`PackageReference` delivery is unproven until a stamped product restores from the feed**, and
  nothing has been published — no `v*` tag exists, and the publish gate now requires three green
  CI jobs on the tagged commit while all three fail on a JauntyQ package `403`. The stamp check
  runs in mixed mode (NativeWeb by checkout), so it proves the `ProjectReference` path only. State
  this in the spec's acceptance rather than claiming R2 in full.
- **The 11-call-site edit is where a silent mistake would hide.** Passing a live flash into an
  output-cached page would make a per-visitor string part of a shared cache entry. The anonymous
  pages must pass `""`, and `PublicPageCacheTests` is the tier that should catch it if they do not.
- **`git mv` across two source trees into one destination**: two of the four exist twice. Move one
  copy and delete the other, rather than moving both and letting the second overwrite.
- **AOT/trim**: no new code paths, but `TreatWarningsAsErrors` is repo-wide, so any new warning is
  a build failure rather than a review finding.

## Coverage check

R1–R3 → §1. R4, R5 → §2. R6 → §3. R7, R8 → §5. R9 → §4 and §5. R10 → §5. R11 → risks, enforced by
the build. R12 → §6. **R2 is partly unprovable here** — see the first risk.

## Verification

- `-p:JauntyQUsePackages=true` on every local `dotnet` command; there is no jauntyq checkout on
  this machine. **`dotnet test NativeWeb.slnx` fails MSB3202 regardless** — the slnx hardcodes
  `../jauntyq/src/…` and has no conditional — so run tiers per-project.
- Each new test falsified: break the subject, confirm red, `git checkout --` **after committing**.
- `deno run -A tools/stamp-check.ts` after the first task, not at the end.
- `deno run -A ~/.claude/tools/structure-audit.js` — 0 errors.
- Full two-lens `/code-review` on the branch before merge, then `--no-ff` into `dev`, unpushed.

Branch: `feat/029-framework-templates`, already carrying the spec. On approval this is recorded as
`029-plan.md`, derived into `029-tasks.md`, and built to merge.
