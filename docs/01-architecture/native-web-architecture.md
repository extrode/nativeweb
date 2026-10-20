# Lean Native Web (LNW) — Architecture Specification & Handoff

## Status

The sections below are the design; this block is what of it exists. Everything §2 states as a
principle is enforced by something that fails when it is broken:

| | Where | Proved by |
|---|---|---|
| Minimal APIs, server-rendered HTML | `libs/nativeweb.web`, `libs/nativeweb.render` | the test projects under `tests/` |
| NativeAOT, no trim warnings | `PublishAot`, `TreatWarningsAsErrors` repo-wide | the publish itself; a warning is an error |
| Compile-time HTML, forms and CRUD | the source generators under `libs/` | the generated code is the only code path; no runtime reflection outside the one assembly-attribute read behind `/version` |
| JauntyQ for all data access | `db/sample`, `apps/sample` | wired and running, not a placeholder list |
| HTMX + Alpine, vendored | framework assets in `libs/nativeweb.web/wwwroot/_nativeweb/`; htmx and Alpine.js in `apps/sample/wwwroot/vendor/` | served from the app's own files, no CDN. Alpine.js is loaded but no page uses it yet |
| The reference application | `apps/sample` | the only deployable project in the tree |
| The template a product is stamped from | `skeleton/`, `tools/new-product.ps1` | CI stamps and tests a product on every push |

**Decided, and deliberately absent.** Auth is not the framework's to pick, so §10's JWT-bearer
default is a recommendation to a product rather than a description of this repo: `apps/sample`
uses a cookie and a demo sign-in, and the framework registers no authentication scheme.
`libs/nativeweb.signin` is a relying party (client) only; there is no identity provider in this
repository ([decision 007](../decisions/2026-08-31-007-idp-leaves-nativeweb.md)). The line
NativeWeb holds is Django's, where `contrib.auth` ships and the OAuth2/OIDC server does not.

§16's checklist is the original list of open risks, kept for the reasoning in it, not as a
current to-do.

------

## 1. Vision

Lean Native Web (LNW) is a server-first architecture for ASP.NET Core that prioritizes
explicit code, NativeAOT compatibility, predictable performance, and long-term
maintainability. It intentionally avoids unnecessary framework abstractions while keeping
the mature parts of ASP.NET Core that are difficult or risky to replace.

------

## 2. Core Principles

1. **NativeAOT First.** Do not add libraries or patterns that require runtime code
   generation, runtime reflection, assembly scanning, dynamic proxies, or plugin loading.
   Everything should be trim-safe and compile-time friendly.
2. **Server Owns the Application.** The server owns business logic, routing,
   authentication, authorization, validation, HTML generation, and database access. The
   browser is a presentation layer.
3. **HTML First.** Prefer HTML → Browser over JSON → JavaScript → HTML for the
   application's own UI. Use JSON only for public APIs, third-party integrations, mobile
   clients, and external consumers.
4. **Explicit Over Magic.** Avoid assembly scanning, automatic registration,
   reflection-based discovery, and hidden conventions. Prefer explicit object creation,
   explicit endpoint registration, explicit routing, explicit configuration.
5. **Lean by Default.** Every dependency must justify its existence. Do not introduce
   abstraction unless it solves a demonstrated problem.

------

## 3. Technology Stack

- **Backend:** .NET 10, C# 14, ASP.NET Core Minimal APIs, NativeAOT,
  System.Text.Json source generation
- **Frontend:** HTMX, Alpine.js, static HTML/CSS, optional Web Components (Lit, or
  Svelte compiled to a CSR-only custom element — never Svelte SSR; see §13)
- **Database:** SQLite for development, PostgreSQL for production
- **Data access:** JauntyQ (§6) — not Dapper, not Dapper.AOT, not EF Core
- **Authentication:** ASP.NET Core Authentication, **JWT Bearer as the default** (§10)
- **Authorization:** ASP.NET Core Authorization Policies

------

## 4. Technologies Explicitly Avoided

Unless there is a documented, specific reason:

- MVC, Controllers, Razor Pages, Blazor (Server or WASM) — Razor's runtime view
  compilation and Blazor Server's SignalR-circuit model are not what NativeAOT's Minimal-API
  path was built around; confirmed unsupported for Native AOT as of .NET 10/11
- MediatR, AutoMapper — indirection with no demonstrated problem behind it here
- Repository Pattern as `IRepository<T>` — a named, per-aggregate class
  (`TodoRepository`) is fine; a generic interface forcing one shape onto every aggregate
  is not (JauntyQ's `.sql`-per-query model makes this doubly true — a generic repository
  would fight the whole point of hand-written, validated SQL per query)
- **Dapper** — vanilla Dapper maps rows to objects using runtime reflection and
  `Reflection.Emit`-generated IL, which directly violates principle 1. `Dapper.AOT` fixes
  this via build-time interceptors, but that's a second package bolted on to reach a bar
  JauntyQ already clears natively, with no interceptor step required
- **EF Core** — not Native AOT-compatible; this is a hard blocker, not a style preference
- Assembly scanning, reflection-based serializers, runtime code generation, Service
  Locator, dynamic plugins

------

## 5. Project Structure

```text
LeanNativeWeb/
├── Program.cs
├── LeanNativeWeb.csproj
├── appsettings.json
│
├── Endpoints/          one static class per feature (Home.cs, Auth.cs, Todo.cs, Admin.cs)
├── Render/              HTML-returning functions per feature, no template engine required
├── Queries/             JauntyQ .sql files, one per query, versioned alongside the code
│                        that calls them (see §6)
├── Auth/
├── Models/
└── wwwroot/
    ├── vendor/              htmx, Alpine.js and fonts, vendored, not CDN-loaded
    ├── css/
    └── components/       hand-written or Lit-based Web Components (see §9)
```

Organize by feature, not by technical layer — `Endpoints/`, `Render/`, and `Queries/` each
hold one file per feature, not one folder per feature containing mixed concerns. A single
`Auth/` folder is the one deliberate exception, since authentication is infrastructure
shared across every feature, not a feature itself.

Each endpoint should: receive the request, validate input, execute business logic, return
`TypedResults`. `TypedResults` (not the untyped `Results` helpers) is the AOT- and
analyzer-friendly choice — it gives OpenAPI/analyzer tooling real compile-time type
information instead of an `IResult` box.

Simple request flow: **Endpoint → JauntyQ → HTML.** Not Controller → Service → Repository →
Mapper → DTO → Database.

------

## 6. Data Access — JauntyQ

JauntyQ is a compile-time SQL-to-C# source generator: you write real SQL in versioned
`.sql` files; at build time JauntyQ parses them against a committed schema snapshot and
emits typed C# methods. If a column is renamed or a table drops, **the build breaks, not
production.**

The generated code is performance-aligned by construction:
- Column values are read by ordinal (`GetInt32(0)`, `GetString(1)`), never by name.
- Parameters are bound with explicit `DbType` from the schema — no provider type inference
  at runtime.
- No reflection, zero per-row boxing, no runtime SQL parsing. (Parameter values box once
  per call at the ADO.NET boundary — `DbParameter.Value` is `object`, a fixed cost every
  data library pays regardless of ORM. Once per call, not once per row, which is why it
  does not scale with result size.)
- A one-time shape guard validates result-set column names and count on the first call per
  query, then never again.

Because there's no reflection and no runtime code generation, the emitted code is Native
AOT and trim compatible **by construction** — no separate `.AOT` package, no interceptor
step, unlike Dapper. `Extrode.JauntyQ.Runtime` is marked `IsAotCompatible`, and
`samples/JauntyQ.Aot.Smoke` publishes to a self-contained native binary — verified in CI —
running the full auto-CRUD, identity-return, and `@stream` paths against SQLite.

**The database provider must also be AOT-friendly** — the ORM being AOT-safe doesn't make
the driver underneath it AOT-safe automatically:
- Development (SQLite): `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlite3`
- Production (PostgreSQL): **Npgsql**, confirmed fully NativeAOT/trimming-compatible since
  8.0 — a deliberate, large rewrite of the driver's internals, current as of Npgsql 10.0.
  Use Npgsql directly, not through EF Core's Npgsql provider (EF Core itself still isn't
  AOT-compatible, so the moment EF Core is in the graph, AOT is off the table regardless of
  which database driver sits underneath it).

------

## 7. Dependency Injection

Minimize the built-in container. Prefer direct object construction:

```csharp
var jaunty = new JauntyContext(connectionString);
var jwt = new JwtService(secret);
```

instead of

```csharp
builder.Services.AddSingleton<JauntyContext>();
```

The container should only be used where framework integrations require it — authentication
middleware being the clearest example (§10).

**Where this needs a caveat the principle doesn't state on its own:** this is directionally
supported by what was actually measured in this process — stripping DI (along with routing
and middleware) going from Minimal APIs to raw Kestrel produced a real ~1.8–2.2x throughput
gain (§13) — but that experiment changed DI, routing, and middleware together, so "DI
specifically, holding routing constant" was never isolated as its own variable. Two costs
the principle doesn't weigh: **scoped lifetimes** (a per-request JauntyQ connection/
transaction is exactly where a container earns its keep for free; hand-rolling that
lifetime via closures or `HttpContext.Items` is very doable but is ceremony DI would
otherwise absorb) and **testability** (substituting a test double is trivial with
container-based injection and comparatively awkward with closures over manually
constructed singletons). Minimizing DI is a reasonable default here; treating it as
costless in every case is not.

------

## 8. Where HTMX goes, where Alpine goes, where neither goes

The heuristic: **HTMX when the server has the answer. Alpine when the state is purely local
and the server never needs to know it. An island (Lit, or Svelte compiled to a CSR-only
custom element, no SSR) when you need real client-side reactive computation over data
already on the page.**

| Interaction | Tool | Why |
|---|---|---|
| Add / edit / delete / list, navigation (`hx-boost`), search/filter/sort, live-updating regions, infinite scroll | HTMX | Server owns the data; a round trip is the cheapest model to reason about |
| Modal visibility, dropdowns, tabs, accordions, a "new item" form toggle | Alpine | Pure ephemeral view state, never persisted, server never needs it |
| Computed preview (subtotal, char counter), a handful of interdependent form fields | Alpine | Needs to feel instant; manageable in `x-data` without a round trip |
| **Inline edit-in-place** | **HTMX, by default** | See note below — this is not as clean a case as it first looks |
| Drag-and-drop with live visual feedback, canvas editor, spreadsheet-like grid | Island | Needs continuous client state neither htmx nor Alpine is built for |
| Form logic that's outgrown `x-data` (rule of thumb: >~15 lines) | Island | Sign of rebuilding a reactive framework badly in an attribute |

**A note on inline editing specifically**, because it's genuinely a compound interaction
and worth getting right rather than filing under a single tool by reflex: the well-known,
idiomatic htmx "click to edit" pattern handles the whole thing as two server round trips —
one `hx-get` for the edit-mode fragment, one `hx-post`/`hx-put` to save and return the
display-mode fragment — with zero client-side state at any point. That's the default here,
because it's the version most consistent with principle 2 (server owns the application):
the server never has to trust a client-computed value it hasn't validated, and there's
nothing to keep in sync. Routing the display↔edit toggle through Alpine instead is legitimate
only when the round-trip latency for that toggle specifically is a demonstrated problem —
it's the exception, not the default, and it should be a deliberate choice, not a reflex.

**Anti-patterns:**
- **Don't mirror server state in Alpine.** The rendered DOM (from htmx) *is* the state. A
  separate `x-data` array duplicating what htmx already fetched is two sources of truth.
- **Don't force a round trip for something obviously local** "for consistency."
- **Don't let one Alpine component's `x-data` grow past ~15 lines** — reach for an island.
- **Don't rewrite a working Svelte component into htmx+Alpine mid-migration** if it's
  genuinely rich-interaction — recompile it as a CSR custom element instead (§12.6).

------

## 9. Web Components

Two distinct, non-conflicting uses:

1. **Reuse and encapsulation** — Navbar, Sidebar, Dialog, Toast, Avatar, Loading Spinner,
   Theme Toggle: shared UI repeated across many server-rendered pages, worth encapsulating
   once. These can be hand-written vanilla Custom Elements or Lit — either way, no build
   step is required if shipped as native ES modules.
2. **Rich-interaction islands** (§8's "Island" row) — genuinely stateful widgets where
   htmx/Alpine would mean fighting the tool.

**Do not create business-domain components** — `CustomerList`, `InvoicePage`,
`OrdersScreen`. Those belong on the server; a component-per-page here is just an SPA wearing
a Web Components costume, undermining principle 2 entirely.

Prefer Lit over Svelte for anything in category 1: Lit ships as plain ES modules and needs
no compile step, keeping "no build pipeline unless absolutely necessary" (§11) intact.
Reach for Svelte-as-custom-element only for category 2, and only compiled `customElement:
true`, never SSR (§12.6) — SSR is the one thing that reintroduces the Node/Deno dependency
this whole architecture exists to avoid.

------

## 10. Authentication & Authorization

Always use ASP.NET Core's built-in authentication and authorization. Never manually
implement JWT parsing, signature verification, cookie encryption, or claims validation —
keep Microsoft's security implementation; this is the sharpest exception to §7's
DI-minimization stance, and the framework registration this needs is exactly the
"required by framework integrations" case §7 already carves out.

**JWT Bearer is the default here, not "JWT Bearer or Cookie Authentication" as
interchangeable options** — they are not equivalent under NativeAOT. JWT bearer
authentication is confirmed AOT-clean. Cookie and OIDC handlers have shown up as
unsupported in NativeAOT compatibility tables historically. If cookie-based auth is a hard
requirement, verify it directly: publish with `PublishAot=true` against the actual auth
configuration and read the trim-analyzer output before committing, rather than assuming
either way.

Use Authorization Policies (`.RequireAuthorization("Admin")`) over manual claim inspection.

------

## 11. Static Assets, Serialization, Configuration

**Static assets:** `wwwroot/`, vendored htmx/Alpine, no CDN dependency, no npm chain for a
simple application. Use `MapStaticAssets` (not `UseStaticFiles`) for build-time
compression and content-hash fingerprinting.

**Serialization:** always source-generated `System.Text.Json` (a `JsonSerializerContext`
with `[JsonSerializable]` per DTO). Reflection-based serialization is prohibited.

**Configuration:** strongly typed configuration objects, loaded once, not `IConfiguration`
injected throughout. One thing worth being explicit about, since it's easy to get
half-right: `IConfiguration.Bind()`/`.Get<T>()` are reflection-based **by default** and
throw real IL2026/IL3050 trim warnings under AOT. Set
`<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`
explicitly to get the source-generated, reflection-free path — the same category of gap as
Dapper vs. JauntyQ: the destination is right, the mechanism that gets you there under
NativeAOT has to be turned on, it isn't automatic.

------

## 12. Error Handling

Centralized middleware. Two different response shapes for two different consumers, decided
deliberately rather than defaulted into one:

- **JSON API routes** (external/mobile/third-party): `ProblemDetails`, RFC 7807, appropriate
  HTTP status codes.
- **HTML/htmx routes**: a real HTML fragment shaped for the target it'll be swapped into —
  not `ProblemDetails` JSON, which would render as a broken blob of text in the DOM.

This distinction matters: htmx 4 swaps 4xx/5xx responses into the target by default. The layouts
set `htmx-config` `noSwap` to `[204, 304, "4xx", "5xx"]`, so a guard rejection (415, 400
antiforgery) does not render its body inside the page — see §15. Decide the error-fragment shape
for htmx routes, but don't leave it undecided in a codebase that serves both response types from
the same host.

Avoid exception-driven business logic; logging stays on the built-in `ILogger`, added only
where needed.

------

## 13. Migration from Svelte + Deno BFF

The reassuring part first: **the C# NativeAOT API layer doesn't need to change.** This
migration replaces the BFF + frontend layer; the domain/service logic underneath is reused
close to as-is, which drastically lowers the risk relative to a full rewrite.

1. **Classify every existing screen** against §8's table: CRUD-shaped → port to htmx;
   genuinely rich-interaction → keep the Svelte component, change only delivery (§13.6).
2. **Stand up the new ASP.NET Core service alongside the running system.** Reuse the
   existing C# domain/service classes directly.
3. **Re-home auth** into ASP.NET Core directly (§10) — verify AOT compatibility against the
   real scheme in use, don't assume.
4. **Strangler-fig cutover, one route at a time**, behind a reverse proxy (Caddy, or YARP
   to stay in .NET) — fits an Incus/LXC setup directly: new service in its own container, a
   small proxy container path-routing between old and new.
5. **Data layer is untouched.** Same Postgres/SQLite, same schema — this migration doesn't
   touch data at all.
6. **For screens staying rich-interaction: recompile, don't rewrite.** Drop the SSR compile
   target, add `customElement: true`
   (`<svelte:options customElement={{ tag: "...", shadow: "none" }} />`), point data
   fetching directly at the C# Minimal API (no more BFF to proxy through), build once with
   Vite, ship the output as a static file next to `htmx.min.js`.
7. **Validate each cut-over route before the next** — before/after latency with `wrk`/`k6`
   (§14), plus a Playwright smoke pass.
8. **Retire Deno once everything's ported** — decommission the BFF process and the
   SvelteKit SSR build entirely.

------

## 14. Measured Benchmarks

Not a projection — a minimal but complete version of both stacks was built (same
conceptual todo functionality, JSON on one side and HTML fragments on the other) and
load-tested with `wrk`, three runs per test, a fixed 15-item dataset throughout (a growing
dataset inflates repeated-write numbers). Single-vCPU sandbox — absolute numbers are a
floor, not what real server hardware shows, but the relative comparison holds regardless
of hardware.

| | req/s (avg of 3 runs) | avg latency |
|---|---|---|
| C# JSON API direct (the floor, no BFF at all) | 26,233 | 2.0ms |
| HTMX + ASP.NET Core — GET `/` (full page) | 21,757 | 2.4ms |
| HTMX + ASP.NET Core — POST toggle | 18,624 | 2.7ms |
| Svelte+Deno — POST toggle (Deno→C# API hop, no render) | 3,398 | 14.3ms |
| Svelte+Deno — SSR GET `/` (full page) | 1,578 | 34ms |

**Where the cost comes from, isolated:** the Deno→C# hop alone (a mutation, no rendering)
is ~5.5x slower than doing the same thing in-process in ASP.NET Core; Svelte's SSR
rendering on top of that hop costs another ~2.2x — SSR, not the network hop, is the more
expensive half. End to end: **HTMX+ASP.NET Core is ~13.8x faster than Svelte+Deno** for the
same page, ~16.6x faster than the theoretical C#-API-only floor.

**Memory (RSS) under load:** htmx-skeleton 49MB, C# JSON API 49.6MB, the Deno process
**186MB** — 3.8x either C# process.

**V8 warm-up, caught without a dedicated test for it:** the SSR runs climbed 1,291 → 1,651
→ 1,793 req/s across three back-to-back runs on the same still-warming process. NativeAOT
has no equivalent curve.

**A separate, narrower experiment** (§ raw Kestrel vs. Minimal API, not part of the
recommendation): stripping ASP.NET Core down to raw Kestrel — no routing engine, no DI, no
middleware — measured ~1.8–2.2x higher throughput and ~28% less idle memory than Minimal
APIs. Real, but not adopted as the default: it costs the "boring, AI/human-legible, huge
corpus" property that makes Minimal APIs the maintainability winner, and per §7, "DI alone"
was never isolated as the specific variable responsible for the gain.

**These figures are frozen.** They came from a purpose-built pair of minimal apps load-tested
with `wrk` in a single-vCPU sandbox. Neither app is in this repo, so nothing can reproduce them
and nothing should be compared against them. They stand as the relative comparison they were
made for: HTMX+ASP.NET Core against Svelte+Deno, ~13.8x on the same page. Not as a target.

**For regression work, use `tools/bench/`.** A Deno load driver that measures `apps/sample`
against a committed `baseline.json`; `--check` exits 1 on a regression (20% down on throughput,
25% up on RSS). The baseline is only comparable on the machine and harness settings it was
recorded with. `tools/bench/README.md` has the commands and exit codes.

The htmx 4 upgrade changed the client asset: the minified library went from 51,238 B to
36,716 B, a 28.3% reduction. It does not move a server-side throughput number: htmx runs in the
browser and does not appear in the request path either harness measures.

------

## 15. htmx 4

htmx isn't a package dependency here: one vendored file (`wwwroot/vendor/htmx/htmx.min.js` in
`apps/sample` and in the skeleton) referenced from a single `<script src>`, plus `hx-*` attribute
strings inside templates. The vendored version is htmx 4, which ships under the `next` dist-tag
while 2.x holds `latest`. The previous 2.x file is kept at
`libs/nativeweb.assets/rollback/htmx-2.0.10.min.js`, outside every `wwwroot` so a superseded
library is not served or shipped in the published output. Rollback is copying it over the
vendored `htmx.min.js` files.

How the codebase meets htmx 4's changed defaults:

- **No implicit inheritance.** Every `hx-*` attribute is derived by boot.js onto the same
  element that carries the `data-nw-*`, so no parent configures a descendant and `:inherited` is
  needed nowhere. `implicitInheritance` stays at its `false` default.
- **4xx/5xx are not swapped.** `noSwap` takes an *array* defaulting to `[204, 304]`; the matcher
  tests the status against `"404"`, `"40x"` and `"4xx"` in turn, so the value is
  `[204, 304, "4xx", "5xx"]`, not a bare `['4xx','5xx']`. Validation errors return `200 OK` with
  a fragment and are unaffected; the 4xx paths are guards (415 content-type, 400 antiforgery),
  whose bodies would otherwise render inside the page. htmx 4 also has a per-element
  `hx-status:4xx` attribute if a finer rule is wanted.
- **No `hx-boost`.** Nothing in the templates uses it.
- **`hx-disable` means something else.** In htmx 4 it means "disable this control during the
  request" (htmx 2's `hx-disabled-elt`), and htmx 2's "don't process this subtree" moved to
  `hx-ignore`. Same attribute name, different job, no error if you get it wrong.
- **Timeouts.** `NativeWebOptions.RequestTimeout` defaults to 30 s, inside htmx 4's 60,000 ms
  `defaultTimeout`, so no allowance is set.
- **No compat shim.** `implicitInheritance: true` is not used.

**What to watch for in an upgrade:** not the renames. Three behaviours changed under the
same-looking API. `after:request` stopped covering error/timeout/abort (restore moved to
`finally:request`), `after:swap` moved its dispatch from the swapped-in element to the issuing
one, and every payload moved from flat `detail.*` fields onto `detail.ctx`. The first two fail
silently — a mechanical rename leaves a permanently disabled button and a focus bug, both green
in CI.

**Watch, don't act on yet:** htmx 4's declarative signals may let some of §8's Alpine rows move
to plain htmx, and a proposed `hx-drag` extension could shrink the "Island" row for simple
drag-and-drop cases. The `hx-alpine-compat` extension is not used.

------

## 16. Handoff Checklist — the original open risks, kept as a record

> **This list is historical, not a to-do.** It was written before any of this existed. Most of it
> has since been answered by building the thing: the AOT publish runs in CI against the real
> dependency graph, JauntyQ drives `apps/sample` and `db/sample`, and there is no strangler-fig
> window because there was no legacy stack to strangle. It is kept for the reasoning inside each
> item, which still applies.

- [ ] **Auth AOT compatibility** for whatever scheme is actually configured (§10) — the one
  item in this spec not yet verified against a real setup.
- [ ] **Every NuGet dependency the real application will carry** — re-run the AOT publish
  against the actual dependency graph, not the toy skeleton's. JauntyQ + Npgsql/
  Microsoft.Data.Sqlite are confirmed clean paths (§6); anything added beyond those needs
  its own check.
- [ ] **Session/auth state during the strangler-fig window** (§13) — if a session must
  survive a request being routed to a *different* backend mid-session, the session store
  needs to be readable by both stacks during the transition.
- [ ] **Confirm the config-binding source generator and `TypedResults`** are actually used
  in the real codebase, not just specified here (§5, §11) — both are opt-in, not automatic.
