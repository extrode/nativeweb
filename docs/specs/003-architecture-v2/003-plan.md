# Architecture v2 + manzil.my extraction (spec 003) — technical plan

> Plan for `docs/specs/003-architecture-v2/003-spec.md` (R1–R45). On approval this file is
> promoted to `docs/specs/003-architecture-v2/003-plan.md` and committed in the first branch's
> first commit. (Supersedes the spec 002 plan that previously occupied this scratch file —
> that one was promoted and shipped.)

## Context

Spec 002 closed the IdP backlog; before any further product work, the architecture itself gets
its v2: externalized compiled templates, i18n with dialect fallthrough, a11y and bot-defense
defaults, security/perf baseline, static pages, and a product skeleton — then DevMarks is
extracted as manzil.my (fresh repo, full rename) as the first product stamped from it.
Sequencing confirmed by the user: v2 lands in NativeWeb first, mechanical work scripted (R42),
IdP template migration deferred to its own spec.

## Core designs

### Render engine (R10–R13, R41, R43–R44) — `libs/nativeweb.render` + `libs/nativeweb.render.generator`
Source generator (netstandard2.0 analyzer project, JauntyQ philosophy) over
`templates/**/*.html` registered as AdditionalFiles. Per template:
- Header comment declares typed params: `<!--#template @layout layout/page  @param string Title  @param Html Body -->`
- `{{Name}}` — HTML-encoded write. Raw output ONLY via the `Html` value type (`Html.Raw(...)`
  in C#, grep-able, R11); a `string` param can never emit unencoded.
- `{{t:auth.signin_title}}` — localized string, key compile-checked against the en catalog.
- Partials `{{> shared/form-field label=... name=...}}` compile to generated method calls
  (named args, type-checked). Layouts declare `{{slot Main}}` (+ named, `@required` → build
  error if unfilled, HEEx model).
- Generator emits static classes mirroring the dir tree; static chunks as UTF-8
  `ReadOnlySpan<byte>` literals written to a `TemplateWriter` (`ref struct` over
  `IBufferWriter<byte>`, values via `TextEncoder.EncodeUtf8`) — no intermediate page string
  (R41). Convenience `IResult`-producing overloads write straight to the response body; a
  string-materializing overload exists for e-mail bodies and tests.
- Diagnostics (all errors): unknown placeholder, type mismatch, missing required slot, unknown
  partial, unknown `t:` key.
- Well-formedness gate (R13): unit-test helper parses every template's rendered output with
  AngleSharp (test-only dependency) across representative param values.

### Localization (R14–R17, R45) — catalog generator in the same generator project
`locales/*.json` (AdditionalFiles): `en.json` is the root; dialects (`en-US.json`) and
languages (`ms.json`, `ar.json`) hold only overrides. Fallthrough resolved **at build time**
into per-locale `FrozenDictionary` tables: dialect → base language → en. Missing en key
referenced anywhere = build error; other catalogs partial by design. Locale metadata carries
text direction (`ar` → rtl) → `<html lang dir>`. `CultureNegotiationMiddleware`: user pref
(users.locale) → cookie → Accept-Language → default; stores `RequestCulture` in HttpContext
items; every template Render takes it. E-mail (R45): content templates move out of
`libs/nativeweb.mail/EmailTemplates.cs` into app `templates/emails/`; mail lib keeps only
transport (`SmtpTransport`, senders take `EmailContent`), rendered in the recipient's locale.

### Bot traps (R21–R24) — `libs/nativeweb.security` `BotTraps`
`BotTraps.Fields(context)` (string overload for the not-yet-migrated IdP renders; partial
`shared/bot-traps` for templates) emits: 4 honeypots (hidden+value, hidden+empty,
offscreen+value, offscreen+empty; tempting names like `contact_website`, `fax_number`;
`autocomplete="off" tabindex="-1" aria-hidden="true"`; offscreen pair hidden by a
`components.css` class, no inline styles) + a time canary signed with `ITimeLimitedDataProtector`
(min age 3 s, max 24 h, configurable). `BotTraps.Inspect(form)` → verdict; a tripped trap takes
the endpoint's success path with no side effects and logs a structured trap event (R23). e2e
asserts computed visibility of every trap field (R24, the Rails-7.1 regression class).

### Security baseline (R25–R31)
In `SecurityHeadersMiddleware`: HSTS (prod only), restrictive `Permissions-Policy`,
`report-to`/`report-uri` → new `POST /csp-report` endpoint (logged, rate-limited). Session
cookie renamed with `__Host-` prefix. Kestrel `MaxRequestBodySize` lowered globally (128 KB)
with per-endpoint raises where needed; `AddRequestTimeouts` default policy. Migration **0052**:
`audit_log` append-only table + JauntyQ queries; IdP admin/operator endpoints write to it.
`security.txt` + ASVS-derived checklist at `docs/security-baseline.md`. CI: `dotnet list
package --vulnerable --include-transitive` gate step.

### Perf/ops (R32–R37)
Response compression (Brotli, `text/html`, `EnableForHttps=true`; BREACH posture documented in
`docs/conventions.md` alongside the R30 no-shared-cache/no-ETag rule for token-bearing pages).
OTel: `AddOpenTelemetry()` metrics+traces (AspNetCore, Runtime, Npgsql instr., OTLP exporter)
behind `NativeWeb:Telemetry:Enabled=false`. `/healthz` (liveness) + `/readyz` (DB round-trip).
All options classes get `ValidateOnStart` (closes the AOT config-hang lesson). Publish knobs in
`Directory.Build.props`: `StripSymbols=true`, `OptimizationPreference=Speed`. Conn strings gain
`Max Auto Prepare=20`. `AssetUrls` dictionary → `FrozenDictionary`. CI records published binary
size + time-to-`/healthz` (scripts under `scripts/ci/`), failing on large regressions.

### Static pages (R38–R40)
`wwwroot/pages/*.html` hand-authored; a small route map serves clean URLs (`/` → landing where
the product wants it) via the static-file pipeline: ETag revalidation (`Cache-Control:
no-cache`), precompressed, nonce-free stricter CSP branch in `SecurityHeadersMiddleware`.
Static-styled 404/500 wired as `ErrorPageMiddleware` fallback so error pages never depend on
the renderer. `robots.txt`, `sitemap.xml`, `security.txt` served.

### Style (R8–R9)
`EnforceCodeStyleInBuild=true` in `Directory.Build.props`; audit/extend `.editorconfig`
(var-when-apparent rule at error severity, plus existing rules raised to build-relevant
severity). Every app's `Program.cs` becomes an explicit `Program` class with `Main`.

### Skeleton + consumption (R1–R3)
`skeleton/` dir in NativeWeb: tokenized (`__PRODUCT__`) app shell (Program, middleware wiring,
templates/, locales/, wwwroot with vendored htmx/Alpine, appsettings shape, db/migrate.sh,
ci.yml, three test-tier fixtures, AGENTS.md/README/work/ operating files, systemd unit +
Windows-service install script under `deploy/`). `tools/new-product.ps1` stamps it.
`Directory.Build.props` in a product mirrors the JauntyQ block with `$(NativeWebRoot)` probe +
`-p:NativeWebUsePackages=true`; NativeWeb libs get pack metadata. Package-mode CI wired but
gated on the known feed gap (recorded in the product README, per R6/memory).

### Extraction (R4–R7, R42)
`scripts/extract-manzil.ps1` (dry-run default, `--execute`): creates
`C:\home\code\extrode-legacy-org.com\manzil.my`, copies `apps/devmarks`, `libs/nativeweb.devmarks.data`,
`tests/nativeweb.devmarks.tests.*` through a rename map
(`nativeweb.devmarks`→`manzil`, `NativeWeb.DevMarks`→`Manzil`, DB `nativeweb_devmarks`→
`manzil`, role → `manzil_app`, branding DevMarks→Manzil), repoints csproj refs to
`$(NativeWebRoot)`, stamps skeleton operating files, `git init` + initial commit on `main`,
`dev` branched. Then, in NativeWeb, `chore/remove-devmarks` deletes the moved projects +
solution entries (authorized by R7). manzil.my proves v2: templates, ms+en catalogs, traps,
static landing page, axe gate.

## Branch sequence (each off `dev`, code+tests, review worker, merge `--no-ff`)

| # | Branch | Delivers |
|---|--------|----------|
| V1 | `chore/style-enforcement` | R8–R9 + fallout fixes repo-wide |
| V2 | `feat/render-engine` | generator, runtime, TemplateWriter, AngleSharp gate (R10–R13, R41, R43–R44) |
| V3 | `feat/i18n` | catalog generator, negotiation, lang/dir, en+ms seeds, mail refactor (R14–R17, R45) |
| V4 | `feat/devmarks-templates` | scripted conversion of the 16 DevMarks render files; axe into its e2e (R18–R20, R42) |
| V5 | `feat/bot-traps` | machinery + wired into all forms in both apps (R21–R24) |
| V6 | `feat/security-baseline` | headers, __Host-, limits, CSP report, audit (0052), CVE gate, docs (R25–R31) |
| V7 | `feat/perf-ops` | compression, OTel, health, ValidateOnStart, publish knobs, CI budgets (R32–R37) |
| V8 | `feat/static-pages` | static pipeline, error pages, robots/sitemap (R38–R40) |
| V9 | `feat/skeleton` | skeleton/, new-product script, deploy units, conventions/ASVS docs (R1–R3, R29) |
| V10 | `feat/manzil-extraction` + `chore/remove-devmarks` | extract script run, manzil.my green, DevMarks removed (R4–R7) |

Order: style first (touches most, risks least), engine before i18n-consuming template
conversion, DevMarks conversion proves V2+V3 before the security/perf branches build on the
shared partials, skeleton after every v2 piece exists, extraction last. IdP keeps its inline
renders this body of work (only shared machinery like `BotTraps.Fields` string overload).

Migration numbers: current max 0051 → **0052** audit_log, **0053** `users.locale`.

## Verification
- Per branch: unit + integration (+ e2e where flows change); suites green before merge.
- End state: all three suites green in **both** repos; zero-warning Release NativeAOT publish
  of `apps/web` and manzil.my's app; axe gate green; screenshot check of manzil.my pages
  (Malay + English), static landing, and trap invisibility; compression/OTel/health verified
  with curl against the published exe (run from its publish dir — known CWD gotcha);
  binary-size/startup numbers recorded as the budget baseline. CI legs verified on the user's
  next push.
- Cleanup script per convention at `scripts/cleanup/architecture-v2.ps1` (written, never run).
