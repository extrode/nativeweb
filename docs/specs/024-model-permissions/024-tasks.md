# 024 — Tasks
Plan: ./024-plan.md

Order is dependency order. The library's own surface lands before the generator that references
it, the generator before the scaffold changes that emit calls into it, and the sample and its
migration before any tier that needs a database.

- [x] T1 Permissions library project and declaration attributes — files: `libs/nativeweb.permissions/nativeweb.permissions.csproj`, `libs/nativeweb.permissions/PermissionsAttribute.cs`, `libs/nativeweb.permissions/PermissionScopeAttribute.cs`, `libs/nativeweb.permissions/Permission.cs` — covers: R1, R2, R3, R4, R16 — done when: the project builds with no trim or AOT warning, `[Permissions]` takes a `params string[]` of extras, `[assembly: PermissionScope]` is single-use, and `Permission` is a readonly struct wrapping the name.
- [x] T2 Store seam and in-memory store — files: `libs/nativeweb.permissions/IPermissionStore.cs`, `libs/nativeweb.permissions/InMemoryPermissionStore.cs` — covers: R6, R7, R13 — done when: `GrantedAsync(subject)` returns direct and group grants from one call, `EnsureSubjectAsync` is idempotent, and the group, membership, grant and revoke methods the access admin needs are present with no query language.
- [x] T3 Per-request context, the decision call, and DI — files: `libs/nativeweb.permissions/IPermissionContext.cs`, `libs/nativeweb.permissions/IPermissions.cs`, `libs/nativeweb.permissions/PermissionsServiceCollectionExtensions.cs` — covers: R5, R12, R14, R17 — done when: `HeldAsync()` calls the store at most once per request and intersects with the catalog, returns the empty set with no `NameIdentifier` claim without touching the store, `AllowsAsync(principal, permission, resourceId)` resolves through it, `AddPermissions` takes the catalog as an argument rather than discovering it, and the project references nothing from `nativeweb.signin`.
- [x] T4 Endpoint filter — files: `libs/nativeweb.permissions/PermissionEndpointFilter.cs` — covers: R5, R8, R16 — done when: the filter carries one `Permission`, reads the `{id}` route value as the resource identity where present, returns 401 with no subject and 403 with a subject lacking the grant, and throws a named `InvalidOperationException` when `AddPermissions` was never called.
- [x] T5 The naming rule, linked into both generators — files: `libs/nativeweb.permissions.generator/nativeweb.permissions.generator.csproj`, `libs/nativeweb.permissions.generator/PermissionNaming.cs`, `libs/nativeweb.scaffold.generator/nativeweb.scaffold.generator.csproj` — covers: R3, R4 — done when: the generator project matches `nativeweb.scaffold.generator.csproj`'s packaging, `{scope}:{model}.{action}` and the constant identifier both come from this one file, and the scaffold generator compiles it in by `<Compile Include>` link with no project reference added.
- [x] T6 Constants, catalog and diagnostics — files: `libs/nativeweb.permissions.generator/PermissionsGenerator.cs`, `libs/nativeweb.permissions.generator/PermissionsEmitter.cs`, `libs/nativeweb.permissions.generator/PermissionsDiagnostics.cs` — covers: R1, R2, R3, R14, R15 — done when: a marked model emits `static class <Model>Permissions` in the model's own namespace with `View`, `Add`, `Change`, `Delete` plus PascalCased extras, one `PermissionCatalog.All` is emitted per assembly, an extra colliding with the four or failing `[a-z][a-z0-9_]*` raises a diagnostic, an assembly name that cannot form a scope with no explicit `[PermissionScope]` raises one, and a compilation with no marked type emits zero sources.
- [x] T7 Generator unit tier — files: `tests/nativeweb.permissions.tests.unit/nativeweb.permissions.tests.unit.csproj`, `tests/nativeweb.permissions.tests.unit/NamingTests.cs`, `tests/nativeweb.permissions.tests.unit/GeneratorOutputTests.cs`, `tests/nativeweb.permissions.tests.unit/DiagnosticsTests.cs` — covers: R1, R2, R3, R4, R14, R15 — done when: the naming rule, the emitted constants and catalog, and every diagnostic are asserted; two compilations with identically named models and different scopes produce different permission names; and removing a declaration from a driven compilation produces CS0117 at a use site.
- [x] T8 Library unit tier — files: `tests/nativeweb.permissions.tests.unit/PermissionContextTests.cs`, `tests/nativeweb.permissions.tests.unit/EndpointFilterTests.cs` — covers: R5, R7, R14, R17 — done when: a direct grant and a group-only grant each allow, a grant naming an undeclared permission is inert, the empty-set path with no subject makes no store call, 401 and 403 are distinguished, and one test supplies a subject claim without any sign-in library type.
- [ ] T9 Public read flag — files: `libs/nativeweb.scaffold/ScaffoldAttributes.cs` — covers: R9, R11 — done when: `AuthAttribute` gains `bool PublicRead` with a doc comment saying it lifts the permission floor from list and detail only, and every existing string property still documents itself as a role or policy name.
- [ ] T10 Route gating in the scaffold generator — files: `libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs` — covers: R8, R10, R11, R15 — done when: each of the six routes gains `.AddEndpointFilter(new PermissionEndpointFilter(<Model>Permissions.<Action>))` after the existing suffix, the emitted constant is a reference and never a string literal, `PublicRead` suppresses it on the read routes, `Authorization()`'s own output is byte-identical to before, and nothing is emitted unless the `[Permissions]` attribute type is in the compilation and the model carries it.
- [ ] T11 Held set and control suppression — files: `libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs` — covers: R12 — done when: the held set is loaded once before the list HTML, the New button, the per-row Edit link and the per-row Delete form are each guarded by their permission, and a model with no `[Permissions]` renders identical HTML to today.
- [ ] T12 Index link gate — files: `libs/nativeweb.scaffold/ScaffoldAccess.cs`, `libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs` — covers: R12 — done when: `CanReadAsync` ANDs the view permission into its answer through the same held set, the index issues no store call per model, and the route's rule and the index's rule remain the one rule its doc comment asks for.
- [ ] T13 Scaffold generator unit additions — files: `tests/nativeweb.scaffold.tests.unit/PermissionGatingTests.cs` — covers: R8, R9, R10, R11, R12, R15 — done when: the emitted filter text, the `PublicRead` exemption, the role-plus-permission case and the guarded controls are each asserted, and running the generator over the same model source with and without the permissions attribute type present yields identical output.
- [ ] T14 Access admin — files: `libs/nativeweb.permissions/AccessAdmin.cs` — covers: R13, R14 — done when: a route group under the scaffold root at `/access` serves groups, group members and grants on `ScaffoldLayout`, every permission name posted is rejected unless it is in the catalog, a grant naming an undeclared permission is listed as undeclared with only a revoke control, and group names are HTML-encoded on render.
- [ ] T15 Schema — files: `db/sample/migrations/0006_permissions.sql`, `docs/specs/024-model-permissions/024-schema.sql` — covers: R6, R7, R13 — done when: the five tables exist with the keys the plan states, the migration seeds the first operator's grant so the access admin is reachable on a fresh database, and the spec-side copy is identical and applied by nothing.
- [ ] T16 Sample wiring — files: `apps/sample/Program.cs`, `apps/sample/PermissionStore.cs`, one existing model under `apps/sample/` — covers: R6, R8, R12, R13 — done when: `[assembly: PermissionScope("sample")]` is declared, a JauntyQ-backed `IPermissionStore` is registered when a connection string is present and the in-memory store otherwise, `AddPermissions(PermissionCatalog.All)` is called, and one scaffolded model carries `[Permissions]`.
- [ ] T17 Integration tier — files: `tests/nativeweb.permissions.tests.integration/nativeweb.permissions.tests.integration.csproj` and its tests, `tests/nativeweb.scaffold.tests.integration/PermissionGatingTests.cs` — covers: R6, R7, R8, R9, R10, R12, R13, R16 — done when: against real PostgreSQL a first-sight subject row appears, a direct and a group grant each admit, 401 and 403 are returned as specified, an anonymous list renders under `PublicRead`, a role-plus-permission route refuses in both directions, the rendered HTML omits the controls the subject may not use, a hand-written endpoint on an admin-less model is gated, and a grant round-trips through the access admin.
- [ ] T18 End-to-end tier — files: `tests/nativeweb.permissions.tests.e2e/nativeweb.permissions.tests.e2e.csproj` and its test — covers: R12, R13 — done when: one Playwright run grants a permission through the access admin and the Delete control appears for that subject.
- [ ] T19 AOT proof — files: `tests/nativeweb.signin.tests.aot/` (one added request) — covers: R8 — done when: the existing published NativeAOT sample serves one gated request and refuses one, with no trim or AOT warning in the publish.
- [ ] T20 Documentation and visibility — files: `libs/nativeweb.permissions/README.md`, `docs/README.md`, `CHANGELOG.md`, `work/status/current.md`, `docs/specs/024-model-permissions/024-spec.md` — covers: R13, R15 — done when: the README states the bootstrap obligation and the model-level list trade-off, the nav lists it, the change is under `## [Unreleased]`, and the spec's acceptance criteria are re-read and its status set.

## Analyze

Every requirement R1 to R17 maps to at least one task, and every task traces to a plan section.

| R# | Tasks |
| --- | --- |
| R1 | T1, T6, T7 |
| R2 | T1, T6, T7 |
| R3 | T1, T5, T6, T7 |
| R4 | T1, T5, T7 |
| R5 | T3, T4, T8 |
| R6 | T2, T15, T16, T17 |
| R7 | T2, T8, T15, T17 |
| R8 | T4, T10, T16, T17, T19 |
| R9 | T9, T10, T13, T17 |
| R10 | T10, T13, T17 |
| R11 | T9, T10, T13 |
| R12 | T3, T11, T12, T13, T16, T17, T18 |
| R13 | T2, T14, T15, T16, T17, T18, T20 |
| R14 | T3, T6, T7, T8, T14 |
| R15 | T6, T10, T11, T13, T20 |
| R16 | T1, T4, T17 |
| R17 | T3, T8 |

No orphan task, no uncovered requirement.
