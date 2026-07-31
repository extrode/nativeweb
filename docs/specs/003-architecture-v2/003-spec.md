# 003 — Architecture v2 and the first product extraction

Status: approved (plan approved 2026-07-31; see 003-plan.md and 003-tasks.md)
Created: 2026-07-31

## Problem

NativeWeb has proven the Lean Native Web architecture through two apps (the IdP and DevMarks),
but everything that makes it an *architecture* — the app shell, security posture, render
conventions, test gates — lives as in-repo discipline, not as reusable mechanism. DevMarks is
ready to become a real product (manzil.my) in its own repository, and a set of improvements the
IdP work surfaced (template externalization, bot defenses, localization, accessibility gates,
perf/caching defaults) should be baked into the architecture *before* it is stamped onto other
apps — otherwise every product ports them by hand, and the ports stop happening.

## Goals

- Any product consumes the shared framework libraries from the NativeWeb checkout (packages in
  CI) and owns a copied shell; improvements to the framework reach every product on rebuild.
- DevMarks ships as manzil.my: its own repository, fully renamed, green on its own CI, running
  on architecture v2.
- HTML output is authored in template files, not C# string literals; changing a layout means
  editing a template and rebuilding.
- Every form is bot-resistant by default; every page is localizable and passes automated
  accessibility checks; static marketing pages serve at near-zero cost.
- The architecture's security and performance defaults are decided once, here, and inherited.

## Non-goals

- The production cutover replacing epass.io (separate, sign-off-gated body of work).
- Full migration of the IdP (`apps/web`) onto externalized templates — its own body of work;
  this spec delivers the machinery and proves it on manzil.my.
- Output/fragment caching of dynamic HTML, streaming HTML, CAPTCHA or WAF-style filtering,
  CDN/edge configuration.
- Migrating epass.* legacy projects in this repo.
- A `dotnet new` template package (revisit when a third product exists; a skeleton dir suffices).

## User stories

- As the architecture owner, I want framework improvements to flow to all products
  automatically, so hardening is done once.
- As a product developer, I want to start a new app by copying a skeleton that already carries
  the security, i18n, a11y and perf decisions, so day one is features, not plumbing.
- As a designer/maintainer, I want to change page layout by editing a template file, so visual
  changes don't require touching application code.
- As an operator, I want telemetry, health probes and an audit trail available the day traffic
  arrives, so incidents are observed rather than instrumented mid-crisis.
- As a non-English user of manzil.my, I want the UI in my language.
- As a screen-reader user, I want swapped-in content announced and forms properly labelled.

## Requirements

### A — Consumption model and skeleton
- R1: Shared framework libraries (auth, security, mail, data) are consumed by products via a
  probed local NativeWeb checkout for co-development and pinned packages in CI (dual-mode, as
  JauntyQ resolves today). Copying framework code into a product is not a supported mode.
- R2: NativeWeb carries a product skeleton (app shell, wiring, asset/vendor layout, config
  shape, migration runner, CI definition, test fixtures for all three tiers, repo operating
  files) that a new product copies once and thereafter owns.
- R3: The skeleton includes service/deployment unit files for the supported hosts (systemd,
  Windows service).

### B — Product extraction: manzil.my
- R4: DevMarks moves to its own repository at `c:\home\code\extrode-legacy-org.com\manzil.my`, fresh
  history, standard repo layout, on architecture v2.
- R5: Full rename: projects, namespaces, database and user-facing branding become manzil;
  no devmarks-named artifact remains in the new repo.
- R6: manzil.my's three test tiers pass in its own repo, and its CI is green in checkout mode;
  package-mode CI is wired but gated on the existing feed gap (recorded, not solved here).
- R7: NativeWeb's repo no longer contains the DevMarks app, its data library or its tests once
  the extraction completes; NativeWeb's own suites stay green after removal.

### C — Code style
- R8: No top-level statements: every executable has an explicit Program class with Main.
- R9: The `.editorconfig` is build-enforced (violations fail the build under the repo's
  warnings-as-errors policy), including the var rule: var only when the type is apparent from
  the right-hand side, otherwise the explicit type name.

### D — Externalized templates
- R10: All HTML output is authored in per-file templates under a structured template tree
  (layout / shared partials / per-feature), compiled at build time; no HTML page or fragment
  markup remains inline in C# code. Editing a template and rebuilding changes the output.
- R11: Template interpolation HTML-encodes by default; emitting raw markup requires an
  unmistakably named, grep-able primitive.
- R12: Template composition supports layouts, partials and named slots; a missing required
  slot, unknown placeholder or type mismatch is a build error, not a runtime blank.
- R13: Every rendered page parses as well-formed HTML, asserted by an automated test layer.
- R43: Templates emit clean semantic, design-agnostic markup: correct landmarks and element
  semantics, class names as the only styling hooks, no inline styles or presentational markup.
  All design lives in the token/stylesheet layer, so a redesign is a CSS change, not a
  template restructure.
- R44: Markup prefers platform-native UI primitives (dialog, popover, details, native form
  controls) over scripted reimplementations, and the pipeline places no obstacle to emitting
  custom elements (web components) in future — component scripts load as external same-origin
  modules under the existing CSP.

### E — Localization
- R14: UI strings live in per-locale catalogs with dialect fallthrough: a dialect catalog
  (e.g. en-US, en-GB) defines only what differs from its base language (en); a key missing
  from any locale or dialect resolves through base language and finally English. English is
  the root: a key missing from the English catalog is a build error; other catalogs may be
  partial by design. Templates and C# reference typed keys, not literal UI text.
- R15: The response culture is negotiated (explicit user choice, then cookie, then
  Accept-Language, then default); the document language and text-direction attributes reflect
  it (right-to-left locales render with the correct direction).
- R16: Invariant globalization remains the default (binary-size/startup win); the trade-off and
  the switch for locale-aware formatting are documented per product.
- R17: manzil.my ships with English and Malay catalogs as the proof of mechanism.
- R45: Outbound email content (verification and reset messages) renders through the same
  template and localization machinery as pages, in the recipient's locale.

### F — Accessibility
- R18: Every e2e-covered page passes automated WCAG checks (axe) as a red/green test gate.
- R19: Form fields render through a shared partial that wires label, error association and
  invalid-state announcement; content swapped in without navigation is announced to assistive
  technology.
- R20: Reduced-motion preference and visible focus styles are honored by the shared tokens.

### G — Bot defenses
- R21: Every form carries, by default via the shared form machinery: four honeypot fields
  (hidden-with-value, hidden-empty, offscreen-with-value, offscreen-empty) and a signed
  render-timestamp trap (minimum and maximum submission age).
- R22: Trap fields are invisible to real users and assistive technology, excluded from
  autofill and tab order, named to tempt bots rather than reveal themselves, and hidden by
  stylesheet rather than inline markers.
- R23: A tripped trap yields a response indistinguishable from success, and the event is
  recorded for tuning.
- R24: A test asserts the traps remain visually hidden (guarding against the sanitizer/CSS
  regression class other frameworks have hit).

### H — Security baseline
- R25: Defaults on every product: HSTS, a restrictive Permissions-Policy, request body-size
  limits on all POST endpoints, request timeouts, and the `__Host-` prefix on the session
  cookie.
- R26: CSP violations are reported to an endpoint we own and logged.
- R27: Operator/admin actions are written to an append-only audit record.
- R28: CI fails on known-vulnerable dependencies.
- R29: A security-baseline checklist (ASVS-derived) lives in docs and each product ticks it.
- R30: Responses bearing per-request secrets (CSRF token, CSP nonce) are never shared-cache
  eligible and are excluded from conditional-GET validators; this rule is recorded in
  conventions.
- R31: `security.txt` is served.

### I — Performance and operations
- R32: Dynamic HTML responses are compressed, including over TLS; pages that would combine a
  non-masked secret with reflected user input are the documented exception (BREACH posture:
  the framework's per-request token masking is the load-bearing mitigation).
- R33: OpenTelemetry metrics and traces are wired but disabled by default; enabling is a
  config change, no rebuild. Statement parameters and secrets never appear in telemetry.
- R34: Liveness and readiness endpoints (readiness includes a database check) ship in the
  skeleton.
- R35: Published binaries are symbol-stripped and speed-optimized; hot queries benefit from
  automatic statement preparation.
- R36: All options types validate at startup; invalid config terminates with a clear message
  (closes the known NativeAOT config-error hang).
- R37: CI tracks published binary size and startup time so regressions surface at PR time.
- R41: Compiled templates emit their static content as UTF-8 at build time; rendering a page
  writes to the response without allocating an intermediate whole-page string.
- R42: The extraction, rename and template conversions are performed by repeatable scripts,
  not hand edits, so the same work is never done twice across apps.

### J — Static pages
- R38: Non-interactive pages (landing, marketing, errors) are served as static files with
  clean URLs, revalidation-based caching, and a stricter nonce-free CSP; they render without
  invoking the dynamic render path or database.
- R39: Error pages (404/500) are static-styled and do not depend on the renderer.
- R40: `robots.txt` and `sitemap.xml` are served alongside.

## Acceptance criteria (selected)

- Given a fresh clone of manzil.my beside a NativeWeb checkout, `dotnet build` succeeds with
  project references; `-p:NativeWebUsePackages=true` restores the pinned packages (feed
  availability permitting, per R6).
- Given a template edit (text or structure) and a rebuild, the served page reflects it with no
  C# change; given a typo'd placeholder or missing required slot, the build fails.
- Given a form submission with any trap tripped, the response is byte-indistinguishable from
  the success path and a trap event is recorded; given a password-manager-assisted legitimate
  submission, no trap trips.
- Given `Accept-Language: ms` and no user preference, manzil.my renders Malay with `lang="ms"`.
- Given an axe violation introduced on a covered page, the e2e suite fails.
- Given the landing page under load, requests are served from static files with 304s on
  revalidation and no database connections used.
- Given `Telemetry:Enabled=true` and a local collector, request percentiles, runtime metrics
  and per-query spans arrive; given the default config, no telemetry leaves the process.
- All three suites green in both repos; zero-warning NativeAOT Release publish of every app.

## Open questions

(none — sequencing confirmed 2026-07-31: v2 lands in NativeWeb first, then the extraction, with
the mechanical work scripted per R42; IdP template migration deferred to its own spec.)
