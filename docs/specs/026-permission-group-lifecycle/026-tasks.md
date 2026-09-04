# 026 — Tasks

Plan: ./026-plan.md

- [ ] T1 The seam gains two operations — files: `libs/nativeweb.permissions/IPermissionStore.cs` —
      covers: R1, R2, R3 — done when: `DeleteGroupAsync` and `ForgetSubjectAsync` are declared, each
      documenting what it removes and that a thing the store does not hold is
      `PermissionConflictException`.
- [ ] T2 The in-memory store implements them — files:
      `libs/nativeweb.permissions/InMemoryPermissionStore.cs` — covers: R1, R2, R3, R9, R10, R11 —
      done when: deleting a group removes it, its memberships and its group grants; forgetting a
      subject removes it, its memberships and its direct grants; neither touches anything else; and
      a thing not held raises.
- [ ] T3 The in-memory facts — files:
      `tests/nativeweb.permissions.tests.unit/InMemoryPermissionStoreTests.cs` — covers: R1, R2, R3,
      R9, R10, R11 — done when: a member keeps its own grants after its group goes, a group survives
      a forgotten member, a forgotten subject seen again holds nothing, and both not-found cases
      raise.
- [ ] T4 The queries — files: `db/sample/queries/Permissions/DeleteGroup.sql`,
      `db/sample/queries/Permissions/ForgetSubject.sql` — covers: R3, R5 — done when: each is a
      single `DELETE ... RETURNING`, the cascades handle the dependents, and no dependent table is
      named in either file.
- [ ] T5 The PostgreSQL store implements them — files: `apps/sample/JauntyPermissionStore.cs` —
      covers: R1, R2, R3, R4, R5 — done when: both call the generated methods and turn an empty
      result set into `PermissionConflictException`.
- [ ] T6 The stores agree — files: `tests/nativeweb.permissions.tests.integration/JauntyStoreSeamTests.cs` —
      covers: R3, R4 — done when: deleting a group and forgetting a subject that neither store holds
      raises the same exception from both.
- [ ] T7 The cascade is asserted, not assumed — files: `tests/nativeweb.permissions.tests.integration/` —
      covers: R5, R9, R10 — done when: against real PostgreSQL, a deleted group leaves no membership
      and no group grant, a forgotten subject leaves no membership and no direct grant, and the
      groups it belonged to are still there.
- [ ] T8 The admin exposes them — files: `libs/nativeweb.permissions/AccessAdmin.cs` — covers: R6, R7,
      R8 — done when: both posts exist under the group and subject paths, both carry
      `RequiresAntiforgery.Instance`, both go through `WrittenAsync`, both redirect to the index, and
      the rendered controls state the counts the store supplied.
- [ ] T9 The admin's facts — files: `tests/nativeweb.permissions.tests.unit/AccessAdminTests.cs`,
      `tests/nativeweb.permissions.tests.unit/AccessAdminAntiforgeryTests.cs` — covers: R6, R7, R8 —
      done when: the delete redirects, a stale delete answers 409, the control names the counts, and
      both posts are refused without a token.
- [ ] T10 Documentation and record — files: `libs/nativeweb.permissions/README.md`, `CHANGELOG.md` —
      covers: R1, R2 — done when: the README says sixteen methods and describes the two destructive
      controls, the changelog names the interface change under Unreleased, and `DocsClaimsTests`
      passes.
