# 029 — Framework-owned templates
Status: draft
Created: 2026-09-05

## Problem

Spec 015 R7 moved the framework's browser assets out of every consumer's `wwwroot/` and into
`libs/nativeweb.web`, delivered by the build, because "a framework whose CSS fix reaches a product
only when somebody re-runs a copy is a framework that cannot ship a CSS fix"
(`libs/nativeweb.assets/README.md`). It stopped at stylesheets and `boot.js`. The HTML partials
that render the framework's own markup were left as per-product copies, and they are the half of
that argument with teeth.

Four templates are framework output rendered from framework code:

| template | rendered by | in `apps/sample` | in `skeleton/` |
|---|---|---|---|
| `shared/antiforgery.html` | `Chrome.For` supplies `FieldName`/`Token` from `IAntiforgery` | yes | yes, byte-identical |
| `shared/bot-traps.html` | `Chrome.For` supplies `Canary` from `BotTraps.IssueCanary` | yes | yes, byte-identical |
| `shared/banner-ok.html` | `Flash.Take` | **no** | yes |
| `shared/banner-error.html` | `Flash.Take` | **no** | yes |

Three problems follow, in ascending order of cost.

**The copies can drift and nothing notices.** `AssetParityTests` (371 lines) compares
`wwwroot/css` and `wwwroot/vendor` between consumers; it does not compare `templates/`. The two
templates present in both trees are byte-identical today by luck rather than by assertion.

**`bot-traps.html` duplicates C# constants as literal text.** It hardcodes `contact_website`,
`fax_number`, `company_website`, `phone_extension` and `form_session` — the field names
`BotTraps` defines in `libs/nativeweb.security` and reads back on submit. A constant renamed in
the library and not in every product's copy produces no compile error, no test failure and no
runtime error: the trap fields simply stop matching, every submission looks human, and bot
detection is quietly off.

**A fix cannot be shipped.** This is the 015 argument unchanged. A hardening change to the trap
markup, or an accessibility fix to a banner's `role`, reaches an existing product only when
someone re-copies a file they have no reason to know changed.

The banners make the gap visible from the other side: `Flash` is framework, its CSS classes are in
the framework stylesheet, and `apps/sample` carries neither partial — so the reference application
cannot render a flash banner, and nothing in the repo proves the framework's own flash path draws
anything at all.

## Goals

- One copy of each framework-owned template in the repository.
- A change to one reaches an existing product through a package upgrade, with no file copying.
- The bot-trap markup and the `BotTraps` constants cannot disagree without something failing.
- `apps/sample` can render every piece of chrome the framework defines, including flash banners.

## Non-goals

- **Product-owned templates stay product-owned.** `layout/page.html`, `wwwroot/pages/error.html`
  and everything under `templates/<feature>/` differ between consumers deliberately and are not in
  scope. `page.html` differs by 58 lines and `error.html` by 41; both are correct as they are.
- **No template language change.** Nothing about `<!--#template`, parameters, or the emitter's
  output shape changes.
- **No new library.** `libs/nativeweb.assets` holds a README and one rollback file and is not in
  `NativeWeb.slnx`; this does not revive it.
- **Third-party vendored files are untouched.** htmx, Alpine and the fonts stay per-consumer
  copies under `wwwroot/vendor/`, guarded by the existing parity test, for the reason the assets
  README gives: they are upstream releases, not the framework's to own.
- **Not a migration of existing consumers.** epass.io and any other repository adopting the change
  is that repository's work.

## User stories

- As a framework maintainer, I want to fix the bot-trap markup once, so that every product picks
  the fix up on its next package upgrade instead of never.
- As a product author, I want the antiforgery and banner partials to exist without my writing
  them, so that a stamped product renders correct chrome on the first build.
- As a reviewer, I want a renamed `BotTraps` constant to break the build, so that a silent
  detection failure is impossible rather than merely unlikely.
- As a product author, I want to override a framework template when my product genuinely needs
  different markup, and to be told clearly when I have done so.

## Requirements

- **R1** The four templates above exist once in the repository, owned by `libs/nativeweb.web`.
- **R2** A consumer that references `libs/nativeweb.web` receives them without declaring them, by
  the same delivery mechanism `NativeWeb.Web.targets` already uses for static web assets — both
  as a `ProjectReference` and as a `PackageReference`.
- **R3** A delivered template compiles to the same logical name it has today, so no call site
  changes: `shared/antiforgery`, `shared/bot-traps`, `shared/banner-ok`, `shared/banner-error`.
- **R4** A product may override a delivered template by declaring its own file at the same logical
  path. The outcome is deterministic and diagnosed: the build states which copy won, and does not
  silently pick one.
- **R5** Delivery is opt-out by a documented MSBuild property, matching the existing
  `NativeWebImportAssets` switch, so a product that wants none of it can say so.
- **R6** `apps/sample` and `skeleton/` no longer carry any of the four files, and both still build,
  render and pass every tier.
- **R7** A test fails if `bot-traps.html` stops naming every field `BotTraps` defines, or names a
  field it does not define.
- **R8** A test fails if a framework-owned template appears again as a copy in a consumer's own
  `templates/` tree without an accompanying declared override.
- **R9** `apps/sample` renders a flash banner through the framework's `Flash` path, proved through
  a real request rather than by the file existing.
- **R10** The stamped product's chrome is unchanged: `tools/stamp-check.ts` passes, and the
  `skeleton-stamp` CI job's assertions about rendered markup still hold.
- **R11** No new trim or AOT warning. `TreatWarningsAsErrors` is repo-wide, so this is a build
  failure rather than a review finding.
- **R12** The change is recorded where a consumer will find it: the breaking-change log, the
  `libs/nativeweb.web` README, and `libs/nativeweb.assets/README.md`, whose "each app still
  vendors its own templates" framing this supersedes.

## Acceptance criteria

- **R1–R3** Given a product referencing `libs/nativeweb.web` with no `templates/shared/` of its
  own, when it builds, then `shared/antiforgery`, `shared/bot-traps`, `shared/banner-ok` and
  `shared/banner-error` are available to render with the parameters they take today.
- **R4** Given a product that declares its own `templates/shared/bot-traps.html`, when it builds,
  then exactly one of the two is compiled, the build says which, and the result does not depend on
  item ordering.
- **R5** Given a product that sets the opt-out property, when it builds, then no framework
  template is contributed, and a call site naming one fails at compile time rather than at render.
- **R6** Given `apps/sample` and a freshly stamped product, when every tier runs, then all pass and
  neither tree contains the four files.
- **R7** Given a rename of any `BotTraps` field-name constant without the template changing, when
  the suite runs, then a test fails naming the constant and the template.
- **R8** Given a copy of a delivered template reintroduced under a consumer's `templates/` with no
  declared override, when the repo tier runs, then a test fails naming the file.
- **R9** Given a request that sets a flash and a redirect that reads it, when the sample's
  integration tier runs, then the response body carries the banner markup and the flash cookie is
  expired in the same response.
- **R10** Given `deno run -A tools/stamp-check.ts`, when it runs, then every step reports `ok` with
  a non-zero test count, and the stamped product's rendered chrome matches what it renders today.

## Open questions

None. R4's collision policy is a design decision for `029-plan.md`, not an unresolved question
about intent: the requirement fixes the property (deterministic and diagnosed) and leaves the
mechanism — framework-wins, product-wins, or a hard diagnostic — to the plan.
