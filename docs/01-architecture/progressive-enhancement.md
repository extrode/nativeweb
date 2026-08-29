# Lean Native Web: Progressive Enhancement Layer

The frontend enhancement conventions for Lean Native Web (spec 004, R1–R8; extended by spec
005's built-in UX defaults). Mandatory for all browser-facing pages in every LNW app. Decided
2026-08-01: attributes are `data-nw-*`, endpoints are POST-only, and `boot.js` is the only
enhancement mechanism. The v2 conventions handoff was folded in here on 2026-08-02 (adopted
items merged below, rejected items recorded at the end) and deleted.

---

## Philosophy

Every page must be fully functional without JavaScript. JavaScript exists solely to
progressively enhance the user experience.

Applications must never require JavaScript for authentication, login, logout, CRUD, forms,
navigation, search, or pagination.

Without JavaScript: the application remains completely usable — all forms submit normally, all
links navigate normally, all actions complete.

With JavaScript: htmx upgrades interactions so pages avoid full reloads. Only the user
experience changes; business logic never changes.

**Build order, always:** semantic HTML → server functionality → HTMX enhancement → Alpine.js
enhancement. Never reverse it.

---

## The enhancement layer: boot.js

Each app serves a vendored copy of `boot.js` (canonical source:
`libs/nativeweb.assets/boot.js`; vendored at `wwwroot/vendor/nativeweb/boot.js`, guarded by a
byte-parity unit test). It is loaded with `defer` **before** htmx, so its normalization runs
before htmx processes the document.

Its only responsibility is an HTML normalization pass:

- Detect elements that opt in with a `data-nw-*` attribute.
- Convert those semantic attributes into the matching `hx-*` attributes.
- Derive the htmx transport from the HTML that already works without JavaScript.

It is not a frontend framework and not a rendering engine. It never renders business UI, never
stores application state, never duplicates server validation, never replaces server routing.

Two hooks cover every element: a `DOMContentLoaded` sweep for the initial document, and an
`htmx:before:process` listener for fragments swapped in by htmx. Normalization is idempotent.

Under htmx 2 the second hook needed a workaround: htmx snapshotted the elements it would
initialize *before* the event fired, so an element that became enhanceable only through the
attributes the sweep had just added (say, a swapped-in `<a data-nw-target>`) was not in that
snapshot, and boot.js had to re-submit everything it modified to `htmx.process()` on a
microtask. **htmx 4 dispatches `htmx:before:process` and only then queries the subtree**, so
attributes added by the sweep are picked up by the very pass that follows and the
re-submission is gone (spec 012 T4, removed on a browser assertion rather than on reading the
source).

---

## Attribute conventions

An element opts in by carrying any `data-nw-*` attribute. The vocabulary:

| You write | boot.js sets | Meaning (htmx semantics) |
| --- | --- | --- |
| `data-nw-target="#list"` | `hx-target="#list"` | where the response fragment lands |
| `data-nw-swap="outerHTML"` | `hx-swap="outerHTML"` | how the fragment is swapped in |
| `data-nw-trigger="revealed"` | `hx-trigger="revealed"` | what event fires the request |
| `data-nw-confirm="Delete?"` | `hx-confirm="Delete?"` | confirm dialog before the request |

Transport is never written by hand — it comes from the element itself:

- `<form method="post" action="/x" data-nw-…>` → `hx-post="/x"`; any other method → `hx-get`.
- `<a href="/x" data-nw-…>` → `hx-get="/x"`.
- A form with a **missing or empty `action` gets no derived transport** — htmx resolves an empty
  path against the base URL and merges query params where native submission replaces them, so
  the two paths would diverge. Enhanced forms always carry an explicit `action`.

Example:

```html
<form method="post" action="/bookmarks" data-nw-target="#list" data-nw-swap="beforeend">
```

Without JavaScript the form POSTs and the server redirects (Post/Redirect/Get). With
JavaScript, boot.js adds `hx-post="/bookmarks" hx-target="#list" hx-swap="beforeend"` and htmx
takes over.

---

## Core rules

- **Opt-in only.** Elements without a `data-nw-*` attribute are never touched. There is no
  automatic boosting of plain links or forms.
- **Authored `hx-*` always wins.** boot.js never overwrites an existing `hx-*` attribute —
  per attribute, not per element. Developers may always use htmx directly; `data-nw-*` is an
  optional convenience, never a replacement.
- **Sources stay.** `data-nw-*` attributes are never removed or altered, so the served HTML
  stays valid and the enhancement remains inspectable.

---

## POST-only endpoints

State changes are `POST` to a verb-named route — one route, one handler, one test surface:

```text
POST /todos/5/delete
```

There are no parallel REST routes (`DELETE /todos/5`), no `data-nw-method`, and no virtual
PUT/PATCH/DELETE. A browser without JavaScript can only GET and POST; a second spelling of the
same action would double the endpoint surface and its tests to serve requests the enhanced path
also makes as POST.

Endpoints inspect the `HX-Request` header: an htmx request gets an HTML fragment (or
`HX-Redirect`); anything else gets a complete page or a normal HTTP redirect. Business logic is
identical on both paths.

---

## HTML ownership

The server owns HTML generation. htmx only transports HTML. Alpine.js only manages local UI
state (menus, toggles, purely visual concerns). Business state never lives in JavaScript.

---

## Built-in UX defaults (spec 005)

The layer every LNW app gets for free, on top of normalization. All shipped in both apps and
the skeleton; the e2e suites enforce them.

### Busy states (R3)

boot.js owns in-flight feedback: on `htmx:before:request` it resolves the triggering submit
control (from `ctx.request.submitter`) and sets `disabled`, `aria-busy="true"` and class
`nw-busy`; on **`htmx:finally:request`** it restores, and re-focuses the control when the swap
dropped focus to `<body>`. No per-form markup, no `hx-indicator` repetition; each app styles
`.nw-busy` in its own `components.css`. **Accepted posture:** busy treatment covers submit
controls only — links and background triggers stay unstyled.

**Restore is on `finally:`, not `after:`, and the difference is the one that matters.** htmx 2's
`afterRequest` fired on success, error, timeout and abort alike. htmx 4's `after:request` fires
only once the response body has been read and is cancelable, so a dropped connection skips it
entirely — restoring there would leave the submit control disabled with no way back.
`finally:request` sits in the request's actual `finally` block and covers every path, including
a `before:request` that another listener cancelled. Pinned by
`BusyState_RestoredWhenTheRequestFailsOutright`.

### Focus after swap (R6)

boot.js recovers focus only when it saw the swap take it: at `htmx:before:swap` it records
whether the focused element (or the busy-disabled submitter, whose `disabled` is what drops
focus to `<body>`) sits inside the swap target; on `htmx:after:swap` with that record set it
focuses the first `[autofocus]` inside the swapped content, else the swapped root via
`tabindex="-1"`. A polling or load-triggered swap that fires while focus is already on
`<body>` deliberately does not autofocus, and focus that survived the swap is never stolen.

**Where the swapped content is read from changed in htmx 4.** htmx 2 dispatched `afterSwap` on
each swapped-in element, so the handler could fall back to `event.target`. htmx 4 dispatches
`after:swap` on `ctx.sourceElement` — the element that *issued* the request — so `event.target`
is now the wrong node and may not be in the swapped region at all. The live content is reached
through `event.detail.ctx.target`. This one fails silently rather than loudly: the old fallback
still resolves to *an* element, so focus would land somewhere plausible and accessibility would
regress with nothing erroring.

Layouts must not carry their own `after:swap` autofocus script — boot.js is the single owner.

### Flash messages (R4)

`Flash.Set(context, kind, message)` before a success redirect writes a `__Host-nw-flash`
cookie ("ok:"/"err:" prefix, escaped, 300-char cap with surrogate-safe truncation, 5-minute
MaxAge); the next full-page render takes it read-once (expiring the cookie in the same
response) and shows the standard banner. One mechanism covers both transports because success
navigation is already unified: a real redirect no-JS, `HX-Redirect` enhanced — either way the
next request renders the full layout. `Flash.cs` is pattern-copied per app (the established
cross-app C# convention); the take happens explicitly at full-page render sites, never in a
fragment path, or a fragment render would consume the flash invisibly. **Accepted posture:**
under same-site prerender a flash can double-show or be consumed silently — cosmetic, accepted.

### Keyboard (R5)

Skip link first in the tab order, `main tabindex="-1"`, `:focus-visible` rings — plus two or
three access keys per app on primary actions, carried as `accesskey` + `aria-keyshortcuts` +
the combo in `title`. Chosen letters: IdP — settings `Alt+S`, admin `Alt+A`; manzil — new
bookmark `Alt+N`, search `Alt+S`, settings `Alt+O`.

### Validation (R7, R8)

Server validation is authoritative, always; the browser's native layer mirrors it for free.
Form fields carry `required`, `minlength`/`maxlength`/`pattern`/`inputmode` matching the server
rules (a unit test pins the mirrored constants where they duplicate server values). Server-side
failures render **field-level errors**: the input gets `aria-invalid="true"` and
`aria-describedby="{id}-error"` (chaining any always-on hint), and an adjacent
`<p class="field__hint field__hint--error" id="{id}-error" role="alert">` carries the message.
Non-field failures keep the page banner. **Boundary:** sign-in and other credential failures
stay generic banners — enumeration defense forbids saying which part was wrong; integration
tests pin this.

### `/version` and error pages (R1, R2)

Designed error pages (400–500 range) via `ErrorPageMiddleware` + `StaticErrorShell`, HX-aware.
`/version` beside `/healthz` returns `InformationalVersion` (`<Version>` + short git SHA
stamped by the per-app `StampSourceRevision` MSBuild target, "dev" fallback), unauthenticated,
in robots.txt disallow. **Accepted posture:** the SHA is disclosed deliberately — it identifies
a public commit, not a secret.

### Browser-native first

Before reaching for JavaScript or a library, check whether the browser already ships the
feature. Prefer `<dialog>`, `<details>`/`<summary>`, popover, `<template>`, native form
validation, lazy loading, and view transitions over rebuilding them; do not build replacements
for browser capabilities.

### Any content type (R12)

LNW apps serve whatever media types they need on the Minimal-API result primitives — no
framework machinery, no content-negotiation layer. `Results.Text` with a content type,
`Results.Bytes`, or a streamed writer is the whole convention, already proven by
`security.txt` (text/plain), favicon bytes (image/*), and the transfer export endpoints
(JSON and Netscape HTML). An app that wants RSS/Atom/sitemap-XML builds the document and
returns it the same way; the framework's job is only to not get in the way.

---

## Considered and rejected (from the v2 handoff)

Recorded so they aren't re-proposed:

- **Bare attribute mapping** (`target=` → `hx-target=`): rejected — `target` is a real HTML
  attribute with its own meaning; the `data-nw-*` prefix keeps served HTML valid and the
  opt-in explicit.
- **`data-lnw-method` + dual routes** (canonical `DELETE /x` plus fallback `POST /x/delete`):
  rejected — doubles the endpoint surface and its tests; POST-only verb routes are the
  convention (see above).
- **Automatic `#errors` container**: rejected — errors belong beside the field or in the
  page's own banner slot; a global sink invites blind `innerHTML` and detaches messages from
  their inputs.
- **Route-generation DSL** (`<a route="Todo.Details">`): rejected — hardcoded hrefs plus the
  `Hrefs` helper class per app are simpler and visible in the served HTML.
- **`RequestContext` wrapper**: rejected — the per-request `Chrome`/`FormChrome` values built
  at render sites already carry what templates need without a context object drifting toward
  a service locator.
- **Strongly-typed IDs**: deferred, not rejected — waiting on JauntyQ converter support
  (tracked in `work/todo.md`).
- **Resource-oriented `Features/` folders**: rejected — both apps and the skeleton already
  follow the flat `apps//libs//tests/` layout with per-area endpoint files; a restructure
  buys navigation churn, not clarity.
- **Fragment URLs** (`GET /todos:list`) **as first-class outputs**: rejected as a URL scheme —
  the goal (one endpoint serving page and fragment) already holds via the `HX-Request` header
  switch; a second URL per resource doubles the route surface.
- **`Html.Page` / `Html.Fragment` rendering abstraction**: rejected — the source-generated
  template `Result(...)` calls are already typed, AOT-safe and layout-aware; a stringly-named
  renderer would trade compile-time checking for indirection.
- **"Keep the framework small" prohibition list** (no custom ORM, migrations, auth, DI,
  template language, state management, JS framework): adopted in substance — it restates the
  stack rules in `AGENTS.md` and `docs/constitution.md`, which remain the binding record.

The v2 items that were adopted — busy states, flash, native validation,
browser-native-first, error pages, `/version`, asset versioning, CSP nonces (the latter two
already existed) — live in the sections above.

---

## Guarantee

Every feature must satisfy: works without JavaScript; works with JavaScript; htmx only improves
responsiveness; HTML remains valid; browser behavior remains standards-compliant; no duplicated
business logic. The JS-off e2e suites are the enforcement, not a review checklist.
