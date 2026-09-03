# 024 — Plan
Spec: ./024-spec.md

## Approach

Two new projects. `libs/nativeweb.permissions` holds the runtime half: the declaration attribute,
a `Permission` value, the store seam, a per-request context, the endpoint filter, an in-memory
store, the DI entry point, and the hand-written access admin. `libs/nativeweb.permissions.generator`
holds the build-time half: one static class of constants per marked model, one per-assembly
catalog, and the diagnostics. This mirrors the scaffold split exactly, down to the generator's
`netstandard2.0` / `IsRoslynComponent` packaging, and it is the split the repo already proves.

Route enforcement is an **endpoint filter appended after the existing authorization suffix**,
not a manufactured authorization policy and not an inline check in the handler body. Authorization
middleware runs before any endpoint filter, so `[Auth]` decides 401 or 403 first and the
permission filter only sees principals that already passed it. The AND composition the spec
requires falls out of the ordering rather than being implemented. A policy provider loses on three
counts: `[Auth(Policy = ...)]` already owns the single policy slot, a policy name cannot carry the
resource identity R5 requires, and it drags in `AddAuthorization` against R17. An inline check
loses because the gate stops being endpoint metadata, so neither a test nor the admin index can
see it, and it duplicates into seven handler bodies per model.

The alternative considered and rejected for storage was shipping a PostgreSQL store in the
library. The JauntyQ licence bars shipping its generated output from a framework library, and a
second hand-written data path would be the ADO.NET the stack rules forbid. So the library ships
only an in-memory store and the seam, `apps/sample` implements the real one over JauntyQ, and the
DDL lives in `db/sample/migrations/`. That is the `IScaffoldStore<T>` precedent unchanged.

## Tech / dependencies

- .NET 10 / C# 14, NativeAOT, `TreatWarningsAsErrors`, no runtime reflection.
- `libs/nativeweb.permissions`: `FrameworkReference Microsoft.AspNetCore.App` only, matching
  `libs/nativeweb.scaffold/nativeweb.scaffold.csproj`. Plus a project reference to
  `nativeweb.scaffold`, for the access admin to reuse `ScaffoldLayout` and `ScaffoldEndpoints`.
  The arrow is permissions -> scaffold at runtime, and scaffold-generator -> permissions-attribute
  by metadata name only at build time. The scaffold library itself gains no reference.
- `libs/nativeweb.permissions.generator`: `netstandard2.0`, `IsRoslynComponent`,
  `EnforceExtendedAnalyzerRules`, `NoWarn RS2008`, `IsAotCompatible=false`,
  `Microsoft.CodeAnalysis.CSharp` and `.Analyzers` with `PrivateAssets=all`, packed to
  `analyzers/dotnet/cs`. Copied from `nativeweb.scaffold.generator.csproj`, which is the repo's
  only working example.
- No new NuGet dependency anywhere. No JauntyQ reference in either new project.

## Changes by area

- **`libs/nativeweb.permissions/PermissionsAttribute.cs`** (new) — `[Permissions]` on a class,
  `AttributeTargets.Class`, `AllowMultiple = false`, `Inherited = false`, `params string[]` of
  extra permission names. Covers R1, R2, R16.
- **`libs/nativeweb.permissions/PermissionScopeAttribute.cs`** (new) — `[assembly:
  PermissionScope("sample")]`, one per assembly, matching `ScaffoldRootAttribute`'s shape and its
  reason for being assembly-level. Covers R4.
- **`libs/nativeweb.permissions/Permission.cs`** (new) — a readonly struct wrapping the string.
  The R5 call and the filter both take `Permission`, never `string`, which is the half of R14 that
  makes a removed declaration a compile error at every use site.
- **`libs/nativeweb.permissions/IPermissionStore.cs`** (new) — the persistence seam, roughly ten
  methods: `EnsureSubjectAsync`, `GrantedAsync(subject)` returning direct and group grants
  together, plus the group, membership, grant and revoke methods R13's admin needs. Covers R6, R7,
  R13.
- **`libs/nativeweb.permissions/IPermissionContext.cs`** (new) — scoped, one method
  `ValueTask<IReadOnlySet<string>> HeldAsync()`, memoised for the request, backed by a single
  `GrantedAsync` call intersected with the catalog. This is what makes R12 possible against a
  synchronous HTML builder, and what stops the admin index issuing a store call per model. With no
  `NameIdentifier` claim it returns the empty frozen set without touching the store. Covers R5,
  R12.
- **`libs/nativeweb.permissions/PermissionEndpointFilter.cs`** (new) — carries one `Permission`,
  reads the resource identity from the `{id}` route value where present, resolves
  `IPermissionContext` from `RequestServices`. No subject is 401, subject without the grant is
  403. Covers R5, R8, R16.
- **`libs/nativeweb.permissions/InMemoryPermissionStore.cs`** (new) — dictionaries, no database.
  The only store the library ships, for the reason in Approach. Covers the unit tier.
- **`libs/nativeweb.permissions/PermissionsServiceCollectionExtensions.cs`** (new) —
  `AddPermissions(PermissionCatalog.All)`. The catalog is passed in rather than discovered,
  because a static registry is shared by two hosts in one process, which is the hazard
  `ScaffoldRootAttribute`'s own doc comment records. Covers R14.
- **`libs/nativeweb.permissions/AccessAdmin.cs`** (new) — hand-written route group at
  `<scaffold root>/access`, three screens: groups, group members, grants. Built on `ScaffoldLayout`
  and `ScaffoldEndpoints` so it does not fork the admin's look. Not `[Scaffold]`-generated: a
  grant is not a keyed row `IScaffoldStore<T>`'s six methods can express, catalog validation has no
  seam there, and it would force the app to implement three more stores. Refuses a permission
  outside the catalog before reaching the store. Covers R13, R14.
- **`libs/nativeweb.permissions.generator/PermissionNaming.cs`** (new) — the single naming rule.
  `<Compile Include>`-linked into `nativeweb.scaffold.generator` rather than placed in a new
  shared library, because no such library exists and one holding a single file would be a
  directory the layout has no row for. Covers R3, R4.
- **`libs/nativeweb.permissions.generator/PermissionsGenerator.cs`** and `PermissionsEmitter.cs`
  (new) — per model, `static class <Model>Permissions` in the model's own namespace with
  `static readonly Permission View, Add, Change, Delete` plus PascalCased extras; per assembly,
  `PermissionCatalog.All`. Diagnostics for an extra colliding with the four, an extra outside
  `[a-z][a-z0-9_]*`, and an assembly name that cannot form a scope with no explicit
  `[PermissionScope]`. Emits zero sources when no type is marked. Covers R1, R2, R3, R14, R15.
- **`libs/nativeweb.scaffold/ScaffoldAttributes.cs`** — one new property on `AuthAttribute`,
  `bool PublicRead`, lifting the permission floor from the list and detail routes. A typed flag
  rather than a sentinel string in a slot documented to hold role names. Every existing property
  keeps meaning a role or policy name. Covers R9, R11.
- **`libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs`** — appends
  `.AddEndpointFilter(new PermissionEndpointFilter(<Model>Permissions.<Action>))` after the
  existing suffix at lines 171, 174, 177, 181, 184 and 188. The suffix is always non-empty
  (`.WithMetadata()` when there is no `[Auth]`, line 205), so composition is uniform. Loads the
  held set before the list HTML at line 293 and guards the New button at 295, the Edit link at 335
  and the Delete form at 343-345. Emits all of this only when the `[Permissions]` attribute type
  is present in the compilation and the model carries it. Covers R8, R10, R12, R15.
- **`libs/nativeweb.scaffold/ScaffoldAccess.cs`** — `CanReadAsync` gains the held set and ANDs
  `View` into its answer, keeping the index's rule and the route's rule in one place, which is
  what its own doc comment asks for. Covers R12.
- **`apps/sample/`** — a JauntyQ `IPermissionStore`, `AddPermissions(PermissionCatalog.All)`,
  `[assembly: PermissionScope("sample")]`, and `[Permissions]` on at least one existing model so
  the feature is proved through the real entry point. Covers R6, R8, R12, R13.
- **`db/sample/migrations/0006_permissions.sql`** (new) — five tables and the first operator's
  grant. Covers R6, R7, R13.
- **`tests/nativeweb.permissions.tests.unit`**, **`.integration`**, **`.e2e`** (new), plus
  additions to `tests/nativeweb.scaffold.tests.unit` and `.integration`. See Coverage check.
- **`docs/specs/024-model-permissions/024-schema.sql`** (new) — the same DDL as a reference the
  library points at and nothing applies, since the library ships no migration runner and the repo
  has none.

## Data / interfaces

Naming rule, R4: `{scope}:{model}.{action}`. Scope is `[assembly: PermissionScope("...")]`, else
the compilation's assembly name lower-cased. A colon separates scope from the rest so that a dot
in an assembly name is unambiguous. Model is the type's simple name lower-cased, not pluralised.
Actions are `view`, `add`, `change`, `delete`, plus declared extras. So `Article` in an assembly
scoped `sample` yields `sample:article.change`, reached in code as `ArticlePermissions.Change`.

Tables, all owned by the consuming application:

    nw_subject(subject text primary key, first_seen timestamptz not null)
    nw_group(id bigint primary key generated always as identity, name text unique not null)
    nw_group_member(subject text, group_id bigint, primary key (subject, group_id))
    nw_subject_grant(subject text, permission text, primary key (subject, permission))
    nw_group_grant(group_id bigint, permission text, primary key (group_id, permission))

Two grant tables rather than one with nullable columns and a check constraint, so that neither
grant kind can be half-formed.

`IPermissionStore.GrantedAsync(subject)` returns direct and group grants in one call, so the
per-request resolution is a single round trip. `IPermissionContext.HeldAsync()` memoises that for
the request and intersects it with the catalog, which is what makes a grant naming an undeclared
permission inert.

`IPermissions.AllowsAsync(ClaimsPrincipal, Permission, string? resourceId = null)` is the R5 call.
The resource identity is accepted and carried today and is not consulted by the shipped stores; it
exists so that a resource-level store can be added later without changing a single call site.

## Risks & mitigations

- **The scaffold generator derives a constant identifier by a rule it does not own.** Fable named
  this as the most likely thing to bite, and it is right: a divergence on a nested type, a generic
  or an extra surfaces as CS0117 inside generated code the author cannot edit, with nothing
  pointing at the cause. Mitigated by linking one `PermissionNaming.cs` into both generators
  rather than writing the rule twice, by emitting the constant reference rather than the string so
  a divergence fails at compile time instead of silently gating on a permission nobody can be
  granted, and by a scaffold-generator diagnostic whenever it cannot derive a name it is confident
  of.
- **The list decides at model level.** A future resource-level store would leave a visible Edit
  control on a row the subject cannot change, and a 403 on click. Recorded in the spec's own
  wording rather than fixed, because fixing it means a store call per row.
- **Bootstrap.** A fresh database has no subject holding the permission needed to reach the access
  admin, so nobody can grant the first grant. The sample migration seeds the first operator. Any
  other application must do the same, and the library README must say so, or the admin is
  unreachable on first deploy.
- **Stale grants.** A grant row naming a permission no longer declared stays in the database, is
  dropped by the catalog intersection so it grants nothing, and appears in the access admin marked
  undeclared with a revoke control. Removal is a cleanup script, never automatic.
- **Input handling.** Every permission name reaching the store from the admin is checked against
  the catalog first, so a hand-posted form cannot create a grant for an arbitrary string. Group
  names are free text and are HTML-encoded on render like every other scaffolded value.
- **External calls.** None. The permission layer makes no network call; the subject arrives as a
  claim that `nativeweb.signin` has already validated, and R17 means the layer does not care how.
- **No production key material, no credential, and no profile field is stored.** The only personal
  datum is the provider's opaque subject string.

## Coverage check

| R# | Covered by |
| --- | --- |
| R1 | `PermissionsAttribute`, `PermissionsEmitter`; generator unit tier |
| R2 | `PermissionsAttribute` params array, extras diagnostics; generator unit tier |
| R3 | `PermissionsEmitter` per-model static class; generator unit tier |
| R4 | `PermissionScopeAttribute`, `PermissionNaming`; generator unit tier, two-assembly case |
| R5 | `IPermissions.AllowsAsync`, `IPermissionContext`; library unit tier |
| R6 | `IPermissionStore.EnsureSubjectAsync`, `nw_subject`; integration tier |
| R7 | `GrantedAsync` returning both grant kinds; library unit and integration tiers |
| R8 | `ScaffoldEmitter` filter append at 171-188; scaffold generator unit, integration 401/403 |
| R9 | `AuthAttribute.PublicRead`; scaffold generator unit, integration anonymous list |
| R10 | Filter ordering after `.RequireAuthorization`; integration, both directions refused |
| R11 | `Authorization()` output unchanged when `[Permissions]` is present; generator unit |
| R12 | Held set at 293, guards at 295/335/343-345, `ScaffoldAccess`; integration on real HTML, one e2e |
| R13 | `AccessAdmin`, store group and grant methods; integration round trip |
| R14 | `Permission` struct at every use site plus catalog validation; generator unit asserting CS0117 |
| R15 | Generator output compared with and without the attribute type present; zero sources when nothing is marked |
| R16 | Filter and `AllowsAsync` on a model with no `[Scaffold]`; integration on a hand-written endpoint |
| R17 | No reference to `nativeweb.signin` from either new project; unit test supplying a subject claim directly |

No requirement is uncovered.

Test tiering beyond the table: the unit tiers run entirely on `InMemoryPermissionStore` and need
no database. The integration tier runs the sample's JauntyQ store against real PostgreSQL through
the same host `tests/nativeweb.scaffold.tests.integration` boots. One Playwright fact in the e2e
tier: grant a permission through the access admin and see the Delete control appear. The existing
AOT publish suite gains one gated request, so the filter is proved to survive NativeAOT.
