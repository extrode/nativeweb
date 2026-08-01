# Progressive enhancement layer + formatted HTML output (spec 004) — technical plan

> Plan for `docs/specs/004-progressive-enhancement/004-spec.md` (R1–R13). On approval this file
> is promoted to `docs/specs/004-progressive-enhancement/004-plan.md` and committed in the first
> branch's first commit. (Supersedes the spec 003 plan previously in this scratch file — shipped.)

## Context

The user drafted `docs/progressive-enhancement.md` proposing a semantic-attribute enhancement
layer. Decisions taken with the user (2026-08-01): attributes are **`data-nw-*`** (the doc's bare
`target=`/`swap=` conflict with real HTML semantics); **POST-only** endpoints (no dual
REST+fallback routes, no `data-nw-method`); HTML formatting fixed **at generation time** (zero
runtime cost); scope is **doc + boot.js + retrofit of both apps** (manzil.my and the IdP) plus
the skeleton. Separately, served HTML is unindented because the R42-converted template sources
are flat and the generator emits static chunks verbatim.

Grounding facts (verified):
- htmx **2.0.10** vendored; it emits `htmx:beforeProcessNode` (present in the minified source).
- CSP is `script-src 'self' 'nonce-{n}'` (`apps/web/Middleware/SecurityHeadersMiddleware.cs:64`)
  — an external vendored script needs no CSP change.
- Layouts are **inlined at generation time** (`TemplateEmitter.InlineSlots`); partials compile to
  shared `Render(ref writer, …)` method calls (`EmitPartialCall`); static markup becomes UTF-8
  literals via `writer.WriteRaw(Utf8Literal(text))` (`TemplateEmitter.cs` EmitNodes).
- Current `hx-*` usage: manzil has 3 forms (`templates/auth/sign-in-form.html`,
  `auth/register-form.html`, `settings/board.html`); the IdP has ~20 inline render sites
  (`apps/web/Render/*.cs`, `Program.cs:622`, `Auth/OAuthEndpoints.cs:16`), all
  hx-post/hx-target/hx-swap plus one hx-confirm (`AdminPage.cs:316`).
- Skeleton templates contain no `hx-*`; it only vendors htmx.
- JS-disabled e2e already exists: `manzil.my/tests/manzil.tests.e2e/NoJsFlowTests.cs`
  (`JavaScriptEnabled = false` context) — extend, don't invent.
- manzil layout scripts: `templates/layout/page.html:37-38` loads htmx then Alpine, both `defer`.

## Design

### 1. boot.js — attribute normalization (R2–R4)
A single small plain-JS file (~60 lines, no deps, no eval, no style injection).

**Canonical home:** `libs/nativeweb.assets/boot.js` (new dir + README). Each app vendors a copy
at `wwwroot/vendor/nativeweb/boot.js` (same pattern as htmx/Alpine); a unit test in each app's
test suite asserts the vendored copy is byte-identical to the canonical file (guards drift
without build-system risk; `MapStaticAssets` fingerprints it automatically).

**Load order:** `<script src=…/vendor/nativeweb/boot.js defer>` inserted **before** the htmx
script tag (defer preserves execution order), in manzil's `templates/layout/page.html` and the
IdP's page chrome.

**Convention (goes in the rewritten doc):** an element opts in by carrying any `data-nw-*`
attribute. Normalization per element:
- `data-nw-target|swap|trigger|confirm` → the matching `hx-*` attribute.
- A `<form data-nw-*>` additionally gets its transport from what's already there:
  `hx-post="{action}"` when `method="post"`, `hx-get` otherwise.
- An `<a data-nw-*>` gets `hx-get="{href}"`.
- **Never** set an `hx-*` attribute that already exists on the element (R3); never remove or
  alter the `data-nw-*` source attributes.

**Mechanism:** two hooks, registered at script evaluation:
1. `DOMContentLoaded` sweep — `querySelectorAll('[data-nw-target],[data-nw-swap],[data-nw-trigger],[data-nw-confirm]')`,
   normalize each. boot.js loads before htmx, so its listener runs before htmx's init.
2. `htmx:beforeProcessNode` listener — normalizes swapped-in fragments before htmx wires them.
   (Belt-and-braces: initial-load nodes are normalized by whichever fires first; normalization is
   idempotent by construction of the never-overwrite rule.)

### 2. Generator pretty-printer (R9–R11)
A formatting pass in `nativeweb.render.generator`, run **after** layout/slot inlining and before
`EmitNodes`, over each page's composed node list. It rewrites `StaticNode.Text` only:

- Tokenize static text across node boundaries, tracking HTML element nesting depth (handles void
  elements, self-closing syntax, comments, doctype).
- Rewrite each line's **leading whitespace** to `depth × 2` spaces. Never insert or delete line
  breaks inside text flow — inline rendering semantics are untouched by definition (existing
  leading whitespace collapses to one space either way).
- Drop lines that are empty/whitespace-only because a directive (`{{if}}`, `{{/if}}`, header
  comment…) was removed — i.e. a StaticNode line with no visible content adjacent to a removed
  directive position.
- **Skip** (emit verbatim) everything inside `<pre>`, `<textarea>`, `<script>`, `<style>`.
- **Conservative bail-out:** if depth tracking becomes ambiguous (e.g. an `{{if}}` branch with
  unbalanced tags), leave the affected chunks untouched rather than misformat. The AngleSharp
  well-formedness gate and axe gate stay as the safety net (R12).

**Partials** (shared `Render` methods, call-site depth unknown at partial compile time): the
generator computes the set of distinct call-site depths per partial across all pages. One depth
(the overwhelmingly common case) → the partial's chunks are indented at that depth. Multiple
depths → emit one `Render` overload per depth (`RenderAtD{n}` naming, chunks re-indented per
variant, call sites bound to the matching one). Zero per-request work either way (R10);
binary-size cost is a few duplicated small literal sets.

**Known fallout:** any render unit tests asserting exact output strings (75 in
`nativeweb.render` tests, plus manzil/web integration assertions on markup) must be updated to
the formatted expectations in the same branch.

### 3. Retrofit (R5–R7)
- **manzil** (3 forms): replace `hx-post/hx-target/hx-swap` with `data-nw-target`/`data-nw-swap`
  (transport derived from the existing `method`/`action`). Add boot.js script line to
  `layout/page.html`.
- **IdP** (~20 sites in `Render/*.cs`, `Program.cs`, `OAuthEndpoints.cs`): same substitution in
  the inline render strings; verify each form carries `method`+`action` (the no-JS-first
  architecture means they should — fix any that don't). `hx-confirm` at `AdminPage.cs:316` →
  `data-nw-confirm`. Add boot.js to the IdP page chrome.
- **Skeleton:** vendor boot.js under `skeleton/apps/__product__/wwwroot/vendor/nativeweb/`,
  reference it in the skeleton layout, point the skeleton AGENTS/README at the convention doc.

### 4. Doc rewrite (R1)
Rewrite `docs/progressive-enhancement.md` to the decided model: `data-nw-*` spelling table,
POST-only rule (with the one-route example), boot.js contract (opt-in, never-overwrite,
mechanism), build order (semantic HTML → server → HTMX → Alpine), and drop the virtual-methods
and dual-endpoint sections. Cross-link from `docs/conventions.md`.

### 5. Tests (R6, R13)
- **boot.js mapping cases:** a Playwright e2e over a dedicated fixture page (served by the app in
  Development only, or a retrofitted real page plus injected fixture markup) asserting each
  `data-nw-*` → `hx-*` mapping, the form/anchor transport derivation, and the
  authored-`hx-*`-wins rule. A swap-path case asserts a fragment swapped in via htmx gets
  normalized (covers the `beforeProcessNode` hook).
- **JS-on behavior parity:** existing enhanced-flow e2e (sign-in swap etc.) must stay green
  unchanged — that *is* R7.
- **JS-off:** extend `NoJsFlowTests.cs` to cover every retrofitted flow; add the equivalent
  JS-off coverage to the IdP e2e suite.
- **Pretty-printer:** unit tests in the render test project — indentation goldens, `<pre>`/
  `<textarea>`/`<script>` preservation, blank-directive-line removal, multi-depth partial
  variants, bail-out case. AngleSharp + axe gates re-run over both apps.
- **Vendored-copy parity test** per app (canonical `libs/nativeweb.assets/boot.js` vs wwwroot
  copy).

## Branch sequence (each off `dev`, code+tests, review before merge, `--no-ff`)

| # | Repo | Branch | Delivers |
|---|------|--------|----------|
| B1 | NativeWeb | `feat/render-formatting` | pretty-printer + render-test fallout (R9–R12) |
| B2 | manzil.my | `chore/formatting-fallout` | any manzil test assertions broken by B1's output change |
| B3 | NativeWeb | `feat/boot-js` | boot.js, doc rewrite, skeleton wiring, IdP chrome script tag, parity tests (R1–R4, R8) |
| B4 | NativeWeb | `feat/nw-retrofit-web` | IdP retrofit + IdP e2e (JS-off + normalization) (R5–R7) |
| B5 | manzil.my | `feat/nw-retrofit` | manzil layout + 3 forms retrofit, NoJsFlowTests extension, fixture e2e (R5–R7, R13) |

B1 first (touches most output, everything downstream asserts against it). B3 before B4/B5
(retrofit needs boot.js in place). B2 rides immediately after B1.

Also: remove the now-resolved "formatted-HTML-source retrofit" entry from `work/todo.md` when B1
merges; new `work/tasklist.md` opens for this body of work; cleanup script (if anything
accumulates) at `scripts/cleanup/progressive-enhancement.ps1`, written never run.

## Verification
- All three suites green in **both** repos after each branch and at end state.
- Zero-warning Release NativeAOT publish of `apps/web` and manzil (publish budgets stay under
  the CI ceilings — the indented output grows static literals slightly; confirm size delta).
- view-source screenshot of manzil home + one IdP page showing indented output (screenshot
  skill against the **published** binary — dev-loop `dotnet run` has the known unstyled-CSS bug).
- e2e: enhanced flows JS-on unchanged; full flows JS-off; normalization fixture assertions;
  trap-visibility and axe gates still green.
- Manual: sign-in via HTMX swap and via no-JS POST against the published binary.
