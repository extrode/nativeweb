# 004 — Progressive enhancement layer + formatted HTML output
Status: draft
Created: 2026-08-01

## Problem
The Lean Native Web architecture already guarantees no-JS functionality, but the way pages opt
into HTMX enhancement is ad hoc: every enhanced form hand-writes `hx-post`/`hx-target`/`hx-swap`
alongside its real `action`/`method`, duplicating intent in every tag and leaving no stated
convention for the next page or the next product. A drafted conventions document
(`docs/progressive-enhancement.md`) proposes a semantic-attribute layer, but as written it
conflicts with HTML semantics (bare `target=` names a browsing context without JS; bare
`swap`/`trigger`/`confirm` are invalid HTML) and mandates dual REST+fallback endpoints the
user has rejected. Separately, the rendered HTML source is unformatted — flat, unindented,
with blank lines where template directives sat — which makes view-source and diff-based
debugging needlessly hard.

## Goals
- One documented, binding convention for how pages declare enhancement, applied uniformly
  across both apps (manzil.my and the IdP) and the skeleton.
- Enhancement declared once per element in semantic `data-nw-*` attributes; a small
  normalization script upgrades them to HTMX at load time.
- Every page remains fully functional with JavaScript disabled, unchanged from today.
- Served HTML source is properly indented and readable, at zero runtime cost.

## Non-goals
- Dual REST + POST-fallback endpoints (decided: POST-only; the fallback route is the route).
- Virtual HTTP methods (`data-nw-method`) — dropped with the dual-endpoint model.
- A client-side framework, renderer, or state store of any kind.
- Runtime (per-request) HTML formatting.
- Reformatting the template *source* files beyond what the retrofit itself touches.

## User stories
- As a developer, I want to write one semantic attribute set per element, so that enhancement
  intent is declared once instead of duplicated across `action`/`hx-*` pairs.
- As a developer, I want elements that already carry `hx-*` attributes left untouched, so that
  direct HTMX use remains available where the convention doesn't fit.
- As a user without JavaScript, I want every flow — auth, CRUD, search, navigation, pagination —
  to work exactly as it does today.
- As a developer debugging a page, I want view-source to show properly indented HTML, so that
  I can read and diff server output directly.

## Requirements
- R1. The conventions document is revised to the decided model and becomes binding: all
  enhancement attributes are `data-nw-*` prefixed; POST-only endpoints; build order
  semantic HTML → server functionality → HTMX enhancement → Alpine enhancement.
- R2. A single small vendored script ("boot") normalizes `data-nw-*` attributes into their
  HTMX equivalents at page load: at minimum target, swap, trigger, and confirm.
- R3. Normalization never modifies an element that already carries the corresponding `hx-*`
  attribute, and never removes or alters the semantic attributes it read.
- R4. The boot script complies with the existing CSP (no inline eval, no style injection) and
  is vendored into the repo like htmx and Alpine — no CDN.
- R5. All existing enhanced pages in both apps are retrofitted to the convention: hand-written
  `hx-*` duplication is replaced by `data-nw-*` where the convention covers it.
- R6. With JavaScript disabled, every flow in both apps behaves identically to today: forms
  submit, links navigate, all actions complete. Covered by end-to-end tests that run with
  JavaScript disabled.
- R7. With JavaScript enabled, enhanced interactions behave identically to the current
  hand-written `hx-*` behavior (same targets, same swaps, same confirm prompts).
- R8. The skeleton (`skeleton/`) ships the convention: boot script vendored, its templates
  using `data-nw-*`, the conventions doc referenced from the product's operating files.
- R9. Served HTML for template-rendered pages is consistently indented per element nesting
  depth, with no stray blank lines from template directives.
- R10. Formatting is resolved entirely at build time; the per-request rendering work is
  unchanged from today.
- R11. Formatting preserves whitespace-sensitive content exactly (preformatted text, script
  and style bodies, textarea content) and does not alter rendered appearance of inline
  text flow.
- R12. The existing well-formedness gate (every template's output parsed as valid HTML) and
  the accessibility gate remain green over the reformatted output.
- R13. All three test layers cover the boot script's normalization behavior: unit-level
  attribute-mapping cases (including the leave-`hx-*`-alone rule), integration coverage of
  a retrofitted page, and end-to-end coverage of at least one enhanced flow with and
  without JavaScript.

## Acceptance criteria
- Given a form with `data-nw-target` and `data-nw-swap` and no `hx-*` attributes, when the
  page loads with JS, then the form carries the equivalent `hx-*` attributes and submits via
  HTMX; when JS is disabled, then the form submits as a normal POST to its `action`.
- Given an element with both `hx-target` and `data-nw-target`, when the page loads, then
  `hx-target` retains its authored value.
- Given the retrofitted apps, when the JS-disabled e2e suite runs, then every covered flow
  passes without modification to endpoint behavior.
- Given any template-rendered page, when its source is viewed, then block-level elements are
  indented by nesting depth and no blank directive lines remain.
- Given the published binary, when a page is requested, then response bytes are copied from
  precomputed literals exactly as today (no formatting pass at request time).

## Open questions
- (none — attribute spelling, endpoint model, formatting approach, and retrofit scope were
  decided with the user on 2026-08-01)
