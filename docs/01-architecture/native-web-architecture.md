# Lean Native Web (LNW) — Architecture Specification & Handoff

## Status

**Built and verified in this process** (real code, compiled, AOT-published, load-tested —
not projected): a Minimal API + HTMX + Alpine skeleton (`htmx-skeleton.zip`), a raw-Kestrel
variant built purely for comparison (`bare-kestrel.zip`, not the recommendation — see §5),
a JSON-returning API variant and a SvelteKit+Deno app built specifically to get real
benchmark numbers against (§13), not to adopt.

**Decided, not yet wired into a real codebase:** JauntyQ as the data-access layer (§6) —
the skeleton currently uses a plain in-memory list as a placeholder; JWT bearer auth as the
default (§10) — not yet implemented against a real identity provider.

**Still open, needs verification against the real (non-toy) codebase before shipping:**
auth AOT compatibility for whatever's actually configured, every real NuGet dependency's AOT
compatibility (not just the toy skeleton's), session/auth state continuity during the
strangler-fig migration window. See §16 for the full checklist.

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

```
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
    ├── htmx.min.js, alpine.min.js   vendored, not CDN-loaded
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
  data library pays regardless of ORM; see `docs/99-design/design-decisions.md`.)
- A one-time shape guard validates result-set column names and count on the first call per
  query, then never again.

Because there's no reflection and no runtime code generation, the emitted code is Native
AOT and trim compatible **by construction** — no separate `.AOT` package, no interceptor
step, unlike Dapper. `JauntyQ.Runtime` is marked `IsAotCompatible`, and
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
compression and content-hash fingerprinting — see §14 for the caching detail.

**Serialization:** always source-generated `System.Text.Json` (a `JsonSerializerContext`
with `[JsonSerializable]` per DTO). Reflection-based serialization is prohibited — this is
already how the JSON-API variant built in this process works.

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

This distinction matters more than it used to: htmx 4.0 swaps 4xx/5xx responses into the
target by default (2.x silently drops them) — see §15. Decide the error-fragment shape for
htmx routes now, or explicitly set `htmx.config.noSwap` after upgrading, but don't leave it
undecided in a codebase that serves both response types from the same host.

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
floor, not what real Provider hardware shows, but the relative comparison holds regardless
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

**Never re-run, and never re-runnable — these figures are frozen.** They came from a
purpose-built pair of minimal apps load-tested with `wrk` in a single-vCPU sandbox. Neither app
is in this repo and the sandbox is gone, so nothing can reproduce them and nothing should be
compared against them. They stand as the relative comparison they were made for: HTMX+ASP.NET
Core against Svelte+Deno, ~13.8x on the same page. Not as a target.

**What replaced them for regression work: `tools/bench/`** (spec 012 R16, 2026-08-29). A Deno
load driver against the published `apps/web` binary, with a committed `baseline.json` and a
`--check` mode. First baseline, bench-host, 16 cpu, win-x64: `/healthz` 18,067.2 req/s, 0.83 ms p50,
74.4 MB peak RSS; `GET /` — the full page render — 6,998.0 req/s, 2.11 ms p50, 60.9 MB. Zero
errors. **These are a new baseline, not a re-measurement of the table above**: different apps,
different load tool, different hardware, and a `fetch`-based driver whose numbers are a floor
for the server rather than its ceiling. Tolerances are 20% down on throughput and 25% up on RSS,
sized from a measured 16.3% run-to-run spread. `tools/bench/README.md` has the rest.

What the htmx upgrade itself changed was the client asset, and that was measured directly — the
minified library went from 51,238 B to 36,716 B, a 28.3% reduction, one fewer 14 KB download per
cold page load. It could not have moved a server-side throughput number: htmx runs in the browser
and does not appear in the request path either harness measures.

------

## 15. Upgrading to htmx 4.0

**The mechanical part is cheap, by construction** — and it was, but it was not the cost. htmx
isn't a package dependency here: one vendored file (`wwwroot/vendor/htmx/htmx.min.js`)
referenced from a single `<script src>`, plus `hx-*` attribute strings inside C# string
literals. Upgrading never forces a C# recompile on its own. What it did force was rewriting
every htmx event listener in the enhancement layer, because 4.0 changed the payload those
listeners read and, in two places, when they fire at all. See "What the upgrade actually cost"
below.

**Done, 2026-08-29 (spec 012).** htmx 4.0.0 was released 2026-08-28 and is vendored here now.
4.0 ships as the `next` dist-tag; 2.x holds `latest` until early 2027, so this was an early
adoption taken deliberately rather than a forced move. The checklist below is closed out.

**Checklist, closed:**
- [x] Re-verify no `hx-*` attribute relies on implicit inheritance from a parent — **clean, and
  structurally so.** Every `hx-*` attribute in this codebase is derived by boot.js onto the same
  element that carries the `data-nw-*`, so no parent ever configures a descendant and
  `:inherited` is needed nowhere. `implicitInheritance` left at its `false` default.
- [x] Decide the 4xx/5xx response shape — **`noSwap` set.** Correction to the note this
  checklist carried: the config takes an *array*, defaulting to `[204, 304]`, and the matcher
  tests the status against `"404"`, `"40x"` and `"4xx"` in turn, so the value is
  `[204, 304, "4xx", "5xx"]` and not a bare `['4xx','5xx']`. Validation errors here already
  return `200 OK` with a fragment and were never affected; the 4xx paths are guards (415
  content-type, 400 antiforgery, OAuth RFC JSON), whose bodies would otherwise have rendered
  inside the page. Pinned by `GuardRejection_DoesNotSwapItsBodyIntoThePage`. htmx 4 also adds a
  per-element `hx-status:4xx` attribute if a finer rule is ever wanted.
- [x] If `hx-boost` is in use — **not in use.** Zero occurrences outside the vendored library.
- [x] Grep for `hx-vars` and `hx-disable` — **neither appears.** Recorded for anyone starting
  from the skeleton: in htmx 4 `hx-disable` means "disable this control during the request"
  (htmx 2's `hx-disabled-elt`), and htmx 2's "don't process this subtree" meaning moved to
  `hx-ignore`. Same attribute name, different job, no error if you get it wrong.
- [x] Check for any endpoint that could run past 60 seconds — **none can.** `Program.cs` sets a
  `RequestTimeoutPolicy` default of 30 s and Npgsql's command timeout is 30 s, both inside
  htmx 4's new 60,000 ms `defaultTimeout`. No allowance set.
- [x] Pull the new file from the project's own distribution — **done**, and the beta-era warning
  about the filename is void: 4.0.0 ships `dist/htmx.min.js` as before. (`htmax.js` is a
  separate bundle of htmx plus extensions, not a rename.)
- [x] Stage with a compat shim — **declined.** No compat layer exists for 4.0 anyway, and the
  inheritance audit above was cheap enough that a flag would have been a permanent fixture in a
  template other projects copy. `implicitInheritance: true` was considered and rejected.
- [ ] **Re-run §14's benchmark suite — NOT DONE, and not doable as written.** §14's figures came
  from an ad-hoc comparative experiment (htmx-skeleton vs C# JSON API vs Deno) run during
  architecture exploration; no runnable harness was ever committed, so there is nothing here to
  re-run and no reproducible 2.x baseline to compare against. The one measured client-side
  delta: the minified library went **51,238 B → 36,716 B, 28.3% smaller**. Server throughput and
  RSS under 4.0 are **unmeasured**. Spec 012 R16 is therefore unmet.
- [x] Keep the previous file around — **kept at `libs/nativeweb.assets/rollback/htmx-2.0.10.min.js`**,
  outside every `wwwroot` so a superseded library is not served or shipped in the published
  output. Rollback is copying it over the two vendored `htmx.min.js` files.

**What the upgrade actually cost**, for the next time: not the renames. Three behaviours changed
under the same-looking API. `after:request` stopped covering error/timeout/abort (restore moved
to `finally:request`), `after:swap` moved its dispatch from the swapped-in element to the
issuing one, and every payload moved from flat `detail.*` fields onto `detail.ctx`. The first
two fail silently — a mechanical rename leaves a permanently disabled button and a focus bug,
both green in CI.

**Watch, don't act on yet:** 4.0's declarative signals may let some of §8's Alpine rows
move to plain htmx; a proposed `hx-drag` extension could shrink the "Island" row for simple
drag-and-drop cases. Worth a second pass over §8's table now that 4.0 has shipped. The
`hx-alpine-compat` extension is evaluated and declined in spec 012 — see there.

------

## 16. Handoff Checklist — open risks to close before real implementation

- [ ] **Auth AOT compatibility** for whatever scheme is actually configured (§10) — the one
  item in this spec not yet verified against a real setup.
- [ ] **Every NuGet dependency the real application will carry** — re-run the AOT publish
  against the actual dependency graph, not the toy skeleton's. JauntyQ + Npgsql/
  Microsoft.Data.Sqlite are confirmed clean paths (§6); anything added beyond those needs
  its own check.
- [ ] **Session/auth state during the strangler-fig window** (§13) — if a session must
  survive a request being routed to a *different* backend mid-session, the session store
  needs to be readable by both stacks during the transition.
- [ ] **Wire JauntyQ into the actual skeleton** — the built-and-verified skeleton (§ Status)
  still uses a placeholder in-memory list; the `.sql`-file/schema-snapshot workflow hasn't
  been exercised against it yet.
- [ ] **Confirm the config-binding source generator and `TypedResults`** are actually used
  in the real codebase, not just specified here (§5, §11) — both are opt-in, not automatic.
