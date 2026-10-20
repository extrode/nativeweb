# NativeWeb — Admin Panel & CMS Module Handoff

## Status

- **Builds on:** the existing `ARCHITECTURE.md` (Lean Native Web spec) — this doc does not
  restate those decisions, it inherits them. If anything here conflicts with
  `ARCHITECTURE.md`, `ARCHITECTURE.md` wins; flag the conflict instead of silently picking one.
- **What's decided here:** source-generator-based scaffolding approach for both Admin and
  CMS, the block-type-as-code/page-as-data split, and the process-topology plan.
- **What's new work:** everything in this doc. Nothing has been built yet — this is a spec
  and phase plan, not a report on existing code.
- **What's explicitly open:** naming convention between `[AdminModel]` and JauntyQ query
  methods, block-dispatch discriminator scheme, media storage choice, page-versioning schema.
  Flagged inline in §8 — resolve and document the decision when each is hit, don't guess
  silently.

---

## 0. Inherited context (do not re-derive, just use)

- Stack: ASP.NET Core NativeAOT, HTMX + Alpine.js, JauntyQ as sole data-access layer
  (compile-time SQL-to-C# source generator, no reflection, no runtime SQL parsing).
- Auth: JWT Bearer is the default scheme. Cookie/OIDC handlers have historical NativeAOT
  gaps — don't introduce them without re-verifying AOT compatibility first.
- DB driver: Npgsql direct (not via EF Core's provider) for Postgres; Microsoft.Data.Sqlite
  for dev/local. Both confirmed AOT/trim-clean.
- Config binding: reflection-based `IConfiguration.Bind()`/`.Get<T>()` are off-limits by
  default under AOT. Use the source-generated path
  (`<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`).
- Caching: `HybridCache` and Output Caching (`MapStaticAssets` for static content) are the
  first-class .NET primitives to use — don't reach for a third-party cache layer.
- Project structure: organize by feature, not by technical layer. Don't repeat the LNW
  spec's earlier mistake of listing `Auth/`, `Data/`, `Models/` as top-level folders.
- No React, no Tailwind, no client-side framework anywhere in this module. HTMX partials +
  Alpine for local UI state only.

---

## 1. Hard constraint — read this before designing anything

**NativeAOT cannot dynamically load or execute new managed code at runtime.** No
`Assembly.LoadFile`, no `Reflection.Emit`, no `AssemblyLoadContext`-based plugin loading —
these are unsupported on NativeAOT, not just discouraged, and there is no supported
workaround as of .NET 10. This is a hard boundary, not a performance tradeoff to optimize
around.

**Consequence for this module:** any new *logic* (new block type, new admin model, new
validation rule) requires a recompile and redeploy of the binary that owns it. There is no
"drop in a DLL" plugin story here. Design around this rather than fighting it — see §3.3 for
how process topology absorbs the cost instead.

Do not attempt to build a generic/dynamic plugin loader as part of this module. If a task
seems to require one, stop and flag it instead of implementing a workaround (e.g. loading
raw IL via a custom interpreter, or scripting via Roslyn scripting APIs at runtime — both
reintroduce the reflection/JIT cost this stack is built to avoid).

---

## 2. Core architectural split

Two things are easy to conflate and must stay separate:

| | Admin panel | CMS |
|---|---|---|
| Unit of extension | Model (`Article`, `User`, ...) | Block type (`Hero`, `RichText`, ...) |
| Known at | Compile time, fixed set | Compile time, fixed set |
| What varies at runtime | Row data only | Page composition (which blocks, in what order, with what field values) |
| Recompile needed for | New model | New block type |
| No recompile needed for | New/edited rows | New/edited pages |

Both are the same underlying pattern: **a source generator turns a fixed, compile-time-known
set of typed things into zero-reflection CRUD/render code; end-user-facing flexibility lives
entirely in data, never in code.**

---

## 3. Component design

### 3.1 Shared: attribute + incremental generator foundation

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class AdminModelAttribute : Attribute
{
    public string? DisplayName { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class AdminListDisplayAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class AdminSearchableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class AdminListFilterAttribute : Attribute { }
```

One `IIncrementalGenerator` (`AdminScaffoldGenerator`) scans for `[AdminModel]`-decorated
partial classes via `ForAttributeWithMetadataName`, and per model emits:

- A minimal API route group (`list` / `add` / `edit` / `delete`) under `/admin/{model}`.
- Calls into JauntyQ query methods for that table. **Open item (§8):** decide and document
  the naming convention that lets the generator find the right JauntyQ query for a given
  model — don't invent a new discovery mechanism if JauntyQ already has one.
- An HTML partial renderer for the list/edit views. Build these as direct string
  construction (`string.Create`, `StringBuilder` pooled via `ObjectPool<T>`, or a
  `IBufferWriter<byte>`-based writer) — no Razor runtime compilation, since that would
  reintroduce reflection.

### 3.2 Admin panel specifics

- Views: list (search/filter/sort), add, edit, delete-confirm, per-record history.
- History/audit: a `content_versions`-style append-only table via JauntyQ, written on every
  add/edit/delete. Don't build this as a generic reflection-based "diff any object" —
  generate the diff/log call per model, same as everything else here.
- Auth: gate the whole `/admin` route group with `[Authorize]` on the JWT Bearer scheme.

### 3.3 CMS specifics

**Block types = code.** Each block type is a compiled type decorated similarly to
`[AdminModel]` (e.g. `[CmsBlock]`), with its own field schema, JauntyQ storage mapping, and
a source-generated render partial. Adding a new block type is new logic — same
recompile/redeploy path as a new admin model.

**Pages = data.** A page is a slug + an ordered list of block *instances* (block-type
discriminator + field values), stored as rows. Creating, editing, reordering, and
publishing pages is pure JauntyQ data manipulation — it never touches the compiled binary.
This is the entire point of the split: editorial flexibility without recompilation.

**Rendering dispatch:** request → resolve slug via a catch-all route → JauntyQ loads the
page + its block instances → each instance's discriminator dispatches to its precompiled
renderer via a source-generated switch or `FrozenDictionary<string, RenderFn>` (built once
at startup from the known block-type set — no `Activator.CreateInstance`, no reflection).
**Open item (§8):** decide the discriminator representation (string type-name vs. small
int enum) — affects both the JauntyQ schema and the generator's dispatch table shape.

**Versioning:** draft/published pointer per page (e.g. `content_versions` with a `status`
column), resolved at read time. Pure data concern, no codegen involved.

**Auth:** authoring routes gated (JWT Bearer, same as admin); delivery/render routes
ungated by default — make this explicit per-route rather than assuming.

---

## 4. Process topology

Two deployable services, not one:

- **Authoring service** — admin panel + CMS editor UI. Lower/burstier traffic, needs auth.
- **Delivery service** — renders published CMS content only. No auth, cache-aggressive,
  needs to scale independently under public load.

Both share one compiled block-renderer library (a project reference, not a plugin) so
render logic isn't duplicated — but deploy as separate NativeAOT binaries in separate Incus
containers on the existing private network mesh. Route via a gateway (YARP or equivalent) keyed
on path or subdomain. This is how "modularity" is achieved without fighting NativeAOT's
no-dynamic-loading constraint: the process boundary is the extension point, not the
assembly boundary.

Cache invalidation on publish: emit an event (simplest: a small message over the existing
private network mesh, or a shared table the delivery service polls/subscribes to) that busts the
`HybridCache`/output-cache entry for the affected slug.

---

## 5. Phased build plan

**Phase 1 — Admin generator skeleton**
Get `AdminScaffoldGenerator` emitting working list/add/edit/delete endpoints for exactly one
real model end to end, wired to real JauntyQ queries (not a placeholder in-memory list).
Acceptance: one model, all four operations working against Postgres or SQLite, AOT-published
and verified to run as a native binary.

**Phase 2 — Admin generalization**
Extend the generator to handle N models, list filtering/search/sort, and the audit/history
table. Acceptance: two or more unrelated models scaffolded correctly with no per-model
hand-written glue beyond the attribute decoration.

**Phase 3 — Admin auth**
Wire JWT Bearer gating across the `/admin` route group. Acceptance: unauthenticated requests
rejected, authenticated requests pass through, verified against the real auth scheme (not
mocked) under AOT.

**Phase 4 — CMS block-type generator**
Build `[CmsBlock]` + its generator, starting with 2–3 real block types (e.g. Hero,
RichText, ImageGallery). Acceptance: each block type has a working field schema, JauntyQ
storage mapping, and render partial, all source-generated.

**Phase 5 — Page/version data model + dispatch**
Build the page/block-instance schema, the catch-all rendering route, and the dispatch
table. Acceptance: a page composed of multiple block instances renders correctly end to end,
draft vs. published resolves correctly.

**Phase 6 — Process split**
Split into authoring/delivery services, deploy as separate Incus containers, wire gateway
routing. Acceptance: public delivery traffic never touches the authoring container; both
independently AOT-published and running.

**Phase 7 — Caching + invalidation**
Wire `HybridCache`/output caching on delivery routes, publish-triggered invalidation.
Acceptance: cache hit rate verified under load, stale content not served past invalidation.

Each phase should be independently AOT-published and smoke-tested before moving to the
next — don't accumulate unverified NativeAOT compatibility risk across phases.

---

## 6. Explicit non-goals

- No dynamic/runtime plugin loading of any kind (see §1).
- No EF Core, no Dapper/Dapper.AOT — JauntyQ only.
- No React, no Tailwind, no client-rendered SPA behavior anywhere in admin or CMS.
- No generic/reflection-based "admin over anything" fallback — every scaffolded model or
  block type goes through the generator; nothing is handled dynamically at request time.

---

## 7. Open items to resolve during implementation (not before)

1. Naming convention linking `[AdminModel]` types to their JauntyQ query methods.
2. Block-instance discriminator representation (string vs. int enum) and its effect on the
   dispatch table.
3. Media/object storage choice (self-hosted MinIO container vs. the provider's object storage) —
   pure infra decision, no codegen impact either way.
4. Exact `content_versions` schema shared (or not) between admin audit history and CMS
   page versioning — decide whether these are one mechanism or two.

Resolve each when the relevant phase is reached; document the decision in this file or in
`ARCHITECTURE.md` (whichever it more naturally extends) rather than leaving it implicit in
code.
