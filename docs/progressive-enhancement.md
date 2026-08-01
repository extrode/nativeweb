# Lean Native Web: Progressive Enhancement Layer

The frontend enhancement conventions for Lean Native Web (spec 004, R1–R8). Mandatory for all
browser-facing pages in every LNW app. Decided 2026-08-01: attributes are `data-nw-*`,
endpoints are POST-only, and `boot.js` is the only enhancement mechanism.

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
`htmx:beforeProcessNode` listener for fragments swapped in by htmx. The second hook has a
subtlety: htmx snapshots the elements it will initialize *before* this event fires, so an
element that becomes enhanceable only through the attributes the sweep just added (say, a
swapped-in `<a data-nw-target>`) is not in that snapshot — forms and submit buttons are, since
htmx selects those unconditionally. boot.js therefore re-submits every element it modified to
`htmx.process()` on a microtask after the current pass; htmx's per-element init guard makes
that a no-op for anything already wired. Normalization is idempotent.

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
- A form with **no `action` attribute gets no derived transport** — htmx resolves an empty
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

## Guarantee

Every feature must satisfy: works without JavaScript; works with JavaScript; htmx only improves
responsiveness; HTML remains valid; browser behavior remains standards-compliant; no duplicated
business logic. The JS-off e2e suites are the enforcement, not a review checklist.
