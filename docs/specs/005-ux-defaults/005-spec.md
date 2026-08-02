# 005 — Built-in UX defaults: error pages, busy states, flash, keyboard, validation
Status: draft
Created: 2026-08-02

## Problem
Spec 004 delivered the progressive-enhancement layer; the second conventions document
(`docs/progressive-enhancement-2.md`) proposes the ergonomics that should come "for free" in a
Lean Native Web app, and most of the structural ones already exist. (Correction at planning:
designed error pages already shipped in spec 003 — `ErrorPageMiddleware` + `StaticErrorShell`
cover 400–500 in both apps and the skeleton, so R1 is verify-only.) What is missing is the
baseline UX layer every page deserves without per-feature work: no feedback while a request is
in flight, success messages hand-rolled differently per app, no deliberate keyboard support, and
validation errors that surface only as page-level banners even though the field-level path
exists. Each app solves (or skips) these independently today.

## Goals
- A user hitting a missing, forbidden, or broken page sees a designed page, not a framework
  default, in both apps.
- Every in-flight request gives visible busy feedback and prevents double submission, with zero
  per-form markup.
- Success/notice messaging after an action works identically with and without JavaScript, via
  one shared convention instead of two per-app hand-rolls.
- Both apps are fully operable by keyboard: visible focus, skip link, focus not lost across
  fragment swaps, and browser-native access keys on primary actions.
- Validation is server-authoritative with browser-native client validation for free: constraint
  attributes in the markup mirror the server rules, and server-side failures render at the field
  they belong to.
- Operations can ask a running instance what version it is.
- One conventions document describes all of it; the v2 draft is folded in and superseded.

## Non-goals
- Strongly typed IDs — deferred, gated on JauntyQ converter support (`work/todo.md`).
- Method spoofing (`data-lnw-method`) and dual REST+fallback routes — rejected in spec 004;
  POST-only stands.
- Bare attribute names (`target=`, `swap=`) — rejected in spec 004; `data-nw-*` stands.
- A shared `#errors` container — field-adjacent and form-banner errors are strictly better.
- Named-route generation (`<a route="...">`) and a `RequestContext` type — fail the
  "removes more code than it adds" rule.
- Any client-side validation library or duplicated validation logic in JavaScript.
- Performance tuning — its own follow-up body of work after this ships.

## User stories
- As a visitor, I want a helpful page when something is missing or broken, so that a bad link is
  not a dead end.
- As a user on a slow connection, I want the button I pressed to show it is working and refuse a
  second press, so that I neither wonder nor double-submit.
- As a user, I want confirmation that my action succeeded after the page changes, whether or not
  JavaScript ran.
- As a keyboard user, I want to reach and operate everything without a mouse, without losing my
  place when part of the page updates.
- As a user who mistyped a field, I want the error at the field, and my browser to catch the
  obvious cases before a round trip.
- As an operator, I want to query a running instance's version.

## Requirements
- R1. Both apps serve designed, app-styled pages for 404, 401, 403, and 500 responses that reach
  the browser as navigations. Fragment (enhanced) requests that fail keep their existing
  fragment-level error behavior.
- R2. Both apps expose an unauthenticated version endpoint reporting the build's informational
  version; the value is fixed at build time.
- R3. While an enhanced request is in flight, the submitting control is disabled and marked busy,
  and an in-flight indication is visible; state is restored on completion, success or failure.
  This requires no per-form markup and adds no work when no request is in flight.
- R4. A completed action can carry a one-shot notice ("flash") that renders exactly once after
  the next page render, on both the no-JS redirect path and the enhanced fragment path, through
  one shared convention used by both apps.
- R5. Both apps are keyboard-operable: every interactive element reachable in a logical tab
  order with visible focus; a skip-to-content link is the first focusable element; primary
  actions carry browser-native access keys that are discoverable to assistive technology.
- R6. A fragment swap does not strand keyboard focus: after a swap replaces the focused
  element's container, focus lands somewhere sensible inside the new content.
- R7. Form fields carry native constraint attributes that mirror the server-side rules
  (requiredness, length, pattern, input type), so browsers validate before submission with no
  JavaScript. Server-side validation remains authoritative and is never weakened or skipped.
- R8. Server-side validation failures render at the field they belong to (with the matching
  accessibility state), not only as a page-level banner. Page-level banners remain for
  non-field failures.
- R9. The conventions from `docs/progressive-enhancement-2.md` that were adopted, and the ones
  rejected with reasons, are folded into the main progressive-enhancement document; the v2 file
  is superseded.
- R10. Everything above holds in both apps and the skeleton, ships with the standard three test
  layers, and keeps the zero-warning NativeAOT publish and existing publish-size budgets.
- R11. C# code this work touches is left less repetitive than found: duplicated blocks are
  extracted and parameterized where that shortens and clarifies them. Untouched files are not
  rewritten; the refactoring boundary is the work's own footprint.
- R12. The conventions document records that endpoints serve any content type (plain text, XML,
  RSS, bytes) through the standard result primitives, with the existing non-HTML endpoints as
  the reference examples.

## Acceptance criteria
- Given a URL that matches no route, when a browser navigates to it, then an app-styled 404 page
  renders (both apps); same shape for 401/403/500.
- Given a running published instance, when the version endpoint is requested, then the build's
  version string returns without authentication.
- Given any enhanced form, when it is submitted, then its submit control is disabled and busy
  feedback shows until the response lands, and a second click during flight does nothing.
- Given an action that redirects (no-JS), when the next page renders, then the notice shows
  once, and a subsequent reload does not show it again. Given the same action enhanced, the
  swapped fragment carries the same notice.
- Given a keyboard-only session, when tabbing from the top of any page, then the first stop is
  a skip link, focus is always visible, and every flow (sign-in, CRUD, settings) completes
  without a pointer.
- Given an enhanced form whose container is swapped on error, when the swap completes, then
  focus is inside the swapped content, not lost to the document body.
- Given a field whose server rule is "required, max 50", when JavaScript is off, then the
  browser blocks empty submission before a round trip; when the server still rejects, the error
  renders at that field with the correct accessibility state.
- Given both apps and the skeleton, when published, then the publish is zero-warning and within
  the existing size budgets.

## Open questions
(none — scope, rejections, and sequencing decided with the user 2026-08-02)
