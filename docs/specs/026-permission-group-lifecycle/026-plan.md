# 026 — Plan

Spec: ./026-spec.md

## Approach

Two methods on `IPermissionStore` — `DeleteGroupAsync(long)` and `ForgetSubjectAsync(string)` — and
two posts on the access admin that call them. Nothing else moves: the schema already carries the
four `ON DELETE CASCADE` clauses this makes reachable, so the PostgreSQL implementation is one
`DELETE` per operation and the dependents go with it (R5).

The not-found decision follows what 024 settled rather than inventing a third convention. Deleting
something the store does not hold is `PermissionConflictException` on both stores (R3), which the
admin already renders as 409 through `AccessAdmin.WrittenAsync`. Detecting it on PostgreSQL is
`DELETE ... RETURNING id`: JauntyQ maps a returning statement to a list, so an empty list is the
row that was not there. That is why these two queries return rather than being bare deletes.

The in-memory store's composite keys are the only real work. Its five dictionaries are keyed on
length-prefixed strings, so removing a group means sweeping `_members` and `_groupGrants` for
matching keys rather than dropping a subtree. That sweep is what the cascade does for free on
PostgreSQL, and the two behaviours are held together by the seam tests in
`tests/nativeweb.permissions.tests.integration/JauntyStoreSeamTests.cs`, which already exists for
exactly this pairing.

R7 — a delete control that states what it takes — is answered with the counts the store can supply
and no new reader. The group page already loads its members and its grants, so both counts are in
hand. A subject's grants are loaded on the subject page; the number of groups it belongs to is not,
and `IPermissionStore` has no groups-of-subject reader, so the subject control names the grant
count and says that every membership goes with it. Adding a reader for one label would widen the
seam every host implements.

## Tech / dependencies

- .NET 10 / C# 14, NativeAOT-clean.
- JauntyQ for the PostgreSQL half: two new `.sql` files under `db/sample/queries/Permissions/`,
  generated methods consumed from `apps/sample/JauntyPermissionStore.cs`. No hand-written ADO.NET.
- No migration. `db/sample/migrations/0006_permissions.sql` already declares every cascade this
  relies on; nothing in the schema changes.

## Changes by area

- **`libs/nativeweb.permissions/IPermissionStore.cs`** — `DeleteGroupAsync` and `ForgetSubjectAsync`,
  each documenting the conflict case the way the existing members do (R1, R2, R3).
- **`libs/nativeweb.permissions/InMemoryPermissionStore.cs`** — both operations over the composite
  keys: drop the group and sweep `_members` and `_groupGrants`; drop the subject and sweep
  `_members` and `_subjectGrants`. Not held is `PermissionConflictException` (R1–R4, R9, R10).
- **`db/sample/queries/Permissions/DeleteGroup.sql`** (new) — `DELETE FROM nw_group WHERE id = @groupId
  RETURNING id`, the cascades doing the rest (R5).
- **`db/sample/queries/Permissions/ForgetSubject.sql`** (new) — the same shape on `nw_subject`.
- **`apps/sample/JauntyPermissionStore.cs`** — both methods, empty result set becoming
  `PermissionConflictException` so the two stores answer alike (R3, R4).
- **`libs/nativeweb.permissions/AccessAdmin.cs`** — `POST {base}/groups/{id}/delete` and
  `POST {base}/subjects/{subject}/delete`, each carrying `RequiresAntiforgery.Instance` like the
  seven that exist (R6), each going through `WrittenAsync` so a stale post is 409 (R8), each
  redirecting to the index because the page they were posted from is gone. The rendered controls
  carry the counts and sit apart from the non-destructive forms (R7).
- **`libs/nativeweb.permissions/README.md`** — the seam is sixteen methods, not fourteen, and the
  access admin has two destructive controls.
- **`tests/nativeweb.permissions.tests.unit/InMemoryPermissionStoreTests.cs`** — the store's own
  facts, including R9 (a member keeps its direct grants), R10 (a group survives its member) and R11
  (a forgotten subject seen again is new and empty).
- **`tests/nativeweb.permissions.tests.unit/AccessAdminTests.cs`** — the two routes over the
  TestServer: the redirect, the 409 on a stale post, and the counts in the rendered control.
- **`tests/nativeweb.permissions.tests.unit/AccessAdminAntiforgeryTests.cs`** — the two new posts
  refused without a token, in the sweep that already covers the other seven (R6).
- **`tests/nativeweb.permissions.tests.integration/JauntyStoreSeamTests.cs`** — the not-found case
  raising the same exception from both stores (R3, R4).
- **`tests/nativeweb.permissions.tests.integration/`** — a cascade fact against real PostgreSQL:
  after a delete, the dependent rows are gone rather than assumed gone (R5).

## Data / interfaces

```csharp
/// <summary>Removes a group, its memberships and its group grants.</summary>
ValueTask DeleteGroupAsync(long groupId, CancellationToken ct);

/// <summary>Removes a subject's record, its memberships and its direct grants.</summary>
ValueTask ForgetSubjectAsync(string subject, CancellationToken ct);
```

Both raise `PermissionConflictException` when the store does not hold the thing named. Neither
takes or returns a `Permission`, for the reason `IPermissionStore` already states about strings.

## Risks & mitigations

- **Two new methods on a public interface break every host that implements it.** The framework is
  pre-1.0 and ships one implementation; `apps/sample` is the only other. Named in the changelog
  under Unreleased rather than hidden behind a default implementation, which would let a host
  silently ship an admin whose delete control does nothing.
- **A delete is irreversible and the control is one post away from a group's page.** R7's counts are
  the mitigation the spec chose; there is no undo and the non-goals say so.
- **`{subject}` is opaque provider text in a route.** The existing subject routes already escape it
  with `Uri.EscapeDataString` and read it back through `ScaffoldEndpoints.RouteValue`; the two new
  posts use the same pair rather than a second convention.
- **The cascade is the schema's, so a host that stamped the tables without it loses the dependents
  silently.** The integration fact asserts the dependents are gone after a delete, which fails for
  such a host rather than leaving orphans.
- **Deleting the group an operator is in can remove that operator's own access.** True of
  `RemoveMemberAsync` today. Not guarded here: the spec's non-goals rule out a bootstrap-protection
  rule, and the migration's seeded first operator holds direct grants rather than group grants.

## Coverage check

| Requirement | Covered by |
| --- | --- |
| R1 group deletion removes memberships and group grants | `IPermissionStore`, both stores |
| R2 subject deletion removes memberships and direct grants | `IPermissionStore`, both stores |
| R3 not held is `PermissionConflictException` on both | `RETURNING` on PostgreSQL, existence check in memory |
| R4 the two stores behave alike | `JauntyStoreSeamTests` |
| R5 PostgreSQL relies on the cascades, asserted | `DeleteGroup.sql`, `ForgetSubject.sql`, integration fact |
| R6 both posts carry antiforgery metadata | `AccessAdmin`, `AccessAdminAntiforgeryTests` |
| R7 the control states what it removes | `AccessAdmin` rendering, `AccessAdminTests` |
| R8 a stale delete answers 409 | `WrittenAsync`, `AccessAdminTests` |
| R9 members lose the group's permissions, keep their own | `InMemoryPermissionStoreTests`, integration |
| R10 deleting a subject leaves its groups | `InMemoryPermissionStoreTests`, integration |
| R11 a forgotten subject seen again is new and empty | `InMemoryPermissionStoreTests` |

No requirement is uncovered.
