# Lean Native Web: Progressive Enhancement Layer

The frontend enhancement conventions for Lean Native Web. Mandatory for all browser-facing
pages in every LNW app. Attributes are `data-nw-*`, endpoints are POST-only, and `boot.js` is
the only enhancement mechanism.

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

`boot.js` lives in `libs/nativeweb.web/wwwroot/_nativeweb/` and reaches every app through
`libs/nativeweb.web/build/NativeWeb.Web.targets`, which serves it at `/_nativeweb/boot.js`. It is
loaded with `defer` **before** htmx, so its normalization runs before htmx processes the
document.

Its only responsibility is an HTML normalization pass:

- Detect elements that opt in with a `data-nw-*` attribute.
- Convert those semantic attributes into the matching `hx-*` attributes.
- Derive the htmx transport from the HTML that already works without JavaScript.

It is not a frontend framework and not a rendering engine. It never renders business UI, never
stores application state, never duplicates server validation, never replaces server routing.

Two hooks cover every element: a `DOMContentLoaded` sweep for the initial document, and an
`htmx:before:process` listener for fragments swapped in by htmx. Normalization is idempotent.

htmx 4 dispatches `htmx:before:process` and only then queries the subtree, so attributes added
by the sweep are picked up by the very pass that follows. boot.js needs no `htmx.process()`
re-submission.

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

## Built-in UX defaults

The layer every LNW app gets for free, on top of normalization. It ships in the sample app and
the skeleton.

### Busy states

boot.js owns in-flight feedback: on `htmx:before:request` it resolves the triggering submit
control (from `ctx.request.submitter`) and sets `disabled`, `aria-busy="true"` and class
`nw-busy`; on **`htmx:finally:request`** it restores, and re-focuses the control when the swap
dropped focus to `<body>`. No per-form markup, no `hx-indicator` repetition; each app styles
`.nw-busy` in its own `components.css`. **Accepted posture:** busy treatment covers submit
controls only — links and background triggers stay unstyled.

**Restore is on `finally:`, not `after:`.** htmx 4's `after:request` fires only once the response
body has been read and is cancelable, so a dropped connection skips it entirely — restoring there
would leave the submit control disabled with no way back.
`finally:request` sits in the request's actual `finally` block and covers every path, including
a `before:request` that another listener cancelled..

### Focus after swap

boot.js recovers focus only when it saw the swap take it: at `htmx:before:swap` it records
whether the focused element (or the busy-disabled submitter, whose `disabled` is what drops
focus to `<body>`) sits inside the swap target; on `htmx:after:swap` with that record set it
focuses the first `[autofocus]` inside the swapped content, else the swapped root via
`tabindex="-1"`. A polling or load-triggered swap that fires while focus is already on
`<body>` deliberately does not autofocus, and focus that survived the swap is never stolen.

htmx 4 dispatches `after:swap` on `ctx.sourceElement` — the element that *issued* the request —
so `event.target` may not be in the swapped region at all. boot.js reads the live content through
`event.detail.ctx.target`. Reading `event.target` fails silently: focus lands somewhere
plausible and accessibility regresses with nothing erroring.

Layouts must not carry their own `after:swap` autofocus script — boot.js is the single owner.

### Flash messages

`Flash.Set(context, kind, message)` before a success redirect writes a `__Host-nw-flash`
cookie ("ok:"/"err:" prefix, escaped, 300-char cap with surrogate-safe truncation, 5-minute
MaxAge); the next full-page render takes it read-once (expiring the cookie in the same
response) and shows the standard banner. One mechanism covers both transports because success
navigation is already unified: a real redirect no-JS, `HX-Redirect` enhanced — either way the
next request renders the full layout. `Flash` lives in `libs/nativeweb.web`; the take happens explicitly at full-page render sites, never in a
fragment path, or a fragment render would consume the flash invisibly. **Accepted posture:**
under same-site prerender a flash can double-show or be consumed silently — cosmetic, accepted.

### Keyboard

Skip link first in the tab order, `main tabindex="-1"`, `:focus-visible` rings. An app may add
access keys on its primary actions, carried as `accesskey` + `aria-keyshortcuts` + the combo in
`title`.

### Validation

Server validation is authoritative, always; the browser's native layer mirrors it for free.
Form fields carry `required`, `minlength`/`maxlength`/`pattern`/`inputmode` matching the server
rules. Server-side
failures render **field-level errors**: the input gets `aria-invalid="true"` and
`aria-describedby="{id}-error"` (chaining any always-on hint), and an adjacent
`<p class="field__hint field__hint--error" id="{id}-error" role="alert">` carries the message.
Non-field failures keep the page banner. **Boundary:** sign-in and other credential failures
stay generic banners — enumeration defense forbids saying which part was wrong; integration
tests pin this.

### `/version` and error pages

Designed error pages (400–500 range) via `ErrorPageMiddleware` + `StaticErrorShell`, HX-aware.
`/version` beside `/healthz` returns `InformationalVersion` (`<Version>` + short git SHA
stamped by the `StampSourceRevision` MSBuild target in the product's csproj, "dev" fallback), unauthenticated,
in robots.txt disallow. **Accepted posture:** the SHA is disclosed deliberately — it identifies
a public commit, not a secret.

### Browser-native first

Before reaching for JavaScript or a library, check whether the browser already ships the
feature. Prefer `<dialog>`, `<details>`/`<summary>`, popover, `<template>`, native form
validation, lazy loading, and view transitions over rebuilding them; do not build replacements
for browser capabilities.

### Any content type

LNW apps answer the web's text formats through one typed helper each, and offer several
representations of one route through one mapping call
([decision 009](../decisions/2026-09-02-009-response-formats-in-the-framework.md)).

- **One call per format.** `FormatResults` in `libs/nativeweb.render` has `Text`, `Markdown`,
  `Json` (source-generated `JsonTypeInfo<T>` only, so it trims), `Xml` (an `XmlWriter` callback),
  `Csv` (RFC 4180, CRLF, formula-neutralising), `Atom`, `Rss` and `Sitemap` (structured input,
  absolute links resolved against the request's public base). Every helper buffers, declares
  `Content-Length` and writes once, for the throughput reason `TemplateResults.Html` records.
- **One route, several representations.** `MapNegotiated("/articles", [Html, Json, Csv], ...)`
  in `libs/nativeweb.web` maps `/articles` and `/articles.{format}` to one handler that is told
  which format was chosen. Precedence is the extension, then `Accept` by quality and
  specificity, then the first offer. An extension the route does not offer is a 404 and an
  `Accept` that admits nothing is a 406, both `text/plain` naming the offers and neither
  reaching the handler. The extensionless route says `Vary: Accept` on every response.
- **A page is cached only when every render is the same page.** `apps/sample` declares `/`,
  `/tags`, `/tags/{slug}`, `/feed`, `/sitemap.xml`, `/robots.txt` and `/about` with
  `AsPublicPage` and leaves `/articles/{slug}` out of the cache on purpose: that page counts a
  view per render, and a cached copy would count one visit a
  minute. The three 404s an article slug can earn — unknown, unpublished, malformed — are one
  response, so the page never says which.
- **The cache keys on the choice.** `PublicPageCachePolicy` runs the same chooser and varies its
  key by the chosen media type, and refuses to store a response whose `Content-Type` is not the
  chosen format's. Two cookie-free representations of one path each get their own entry.
- **What stays on the raw primitives.** Bytes, streams and any body that cannot know its length
  stay on `Results.Bytes` and `Results.Stream`, as `favicon` does. What a sitemap lists and what
  `robots.txt` disallows are the application's, because the framework owns no route inventory;
  `apps/sample/PublicDocuments.cs` is the model.

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
- **Strongly-typed IDs**: deferred, not rejected — waiting on JauntyQ converter support.
- **Resource-oriented `Features/` folders**: rejected — the sample and the skeleton already
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
