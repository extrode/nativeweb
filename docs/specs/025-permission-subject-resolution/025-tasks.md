# 025 — Tasks

Plan: ./025-plan.md

- [x] T1 The options and the resolver — files: `libs/nativeweb.permissions/PermissionSubjectOptions.cs`,
      `libs/nativeweb.permissions/PermissionSubjectResolver.cs` — covers: R1, R2, R3, R5, R6, R7 —
      done when: the resolver returns the claim by default, the configured claim when named, the
      function's answer when supplied, null for anonymous, null for empty or whitespace, and throws
      naming both when both are configured.
- [x] T2 The resolver's facts — files: `tests/nativeweb.permissions.tests.unit/PermissionSubjectTests.cs` —
      covers: R1, R2, R3, R5, R6, R7 — done when: one fact per acceptance criterion in the spec that
      concerns resolution, each red without T1.
- [x] T3 The context reads through the resolver — files: `libs/nativeweb.permissions/PermissionContext.cs` —
      covers: R1, R2, R7 — done when: nothing in the context names `ClaimTypes.NameIdentifier`, and
      the subject it hands the store is the resolver's answer unchanged.
- [x] T4 The absent subject is recorded — files: `libs/nativeweb.permissions/PermissionContext.cs` —
      covers: R4, R5 — done when: an authenticated principal yielding no subject logs one warning per
      context instance naming the claim looked for and not its value, and an anonymous one logs
      nothing.
- [x] T5 Wiring — files: `libs/nativeweb.permissions/PermissionsServiceCollectionExtensions.cs` —
      covers: R1, R2, R3 — done when: `AddPermissions(catalog, configure)` exists, the two-argument
      form delegates to it, the resolver is a singleton, and configuring both throws during
      registration.
- [x] T6 The context's facts — files: `tests/nativeweb.permissions.tests.unit/PermissionContextTests.cs` —
      covers: R1, R2, R4, R5 — done when: a host naming `sub` resolves grants, the warning fires once
      and only for an authenticated principal, and the existing facts still pass unchanged.
- [x] T7 Through a real pipeline — files: `tests/nativeweb.scaffold.tests.integration/` — covers: R1 —
      done when: a generated gated route answers for a principal whose subject arrives as `sub` under
      a host that configured it, which is the spec's opening story.
- [x] T8 Documentation — files: `libs/nativeweb.permissions/README.md` — covers: R8 — done when: the
      "not configurable yet" paragraph is replaced by how to configure it, the silent-403 warning
      survives for a claim that is configured and still absent, and `DocsClaimsTests` passes.
