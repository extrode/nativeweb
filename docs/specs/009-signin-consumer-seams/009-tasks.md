# 009 — Tasks
Plan: ./009-plan.md

- [x] T1 Sign-out handler — files: `libs/nativeweb.signin.epass/SessionAuthenticationHandler.cs` — covers: R1, R2 — done when: the handler implements `IAuthenticationSignOutHandler` and `SignOutAsync` calls `SessionAuth.Clear`, the same method the logout endpoint calls.
- [x] T2 Address policy delegate and registration — files: `libs/nativeweb.signin.epass/EpassSignInBuilder.cs` — covers: R5 — done when: `AddressPolicy` exists beside `SignInResolver` and `OnAddressSubmitted` registers it, mirroring `OnSignedIn`.
- [x] T3 Address policy call site — files: `libs/nativeweb.signin.epass/SignInEndpoints.cs` — covers: R5, R6, R7 — done when: `PostLoginAsync` resolves the policy optionally, calls it after the size bound and before `StartLoginLinkAsync`, redirects with `AddressUnusable` on refusal, and treats a throwing policy as a refusal.
- [x] T4 Internals visible to the testing package — files: `libs/nativeweb.signin.epass/nativeweb.signin.epass.csproj` — covers: R3 — done when: the testing assembly can reach `SessionCookie` and nothing else can.
- [x] T5 `TestSession` helper — files: `libs/nativeweb.signin.epass.testing/TestSession.cs` — covers: R3, R4 — done when: `Mint`, `MintExpired`, `Tamper` and `CookieName` exist and mint through `SessionCookie`, not beside it.
- [x] T6 Unit tests for all three seams — files: `tests/nativeweb.signin.epass.tests.unit/` — covers: R1, R3, R4, R5 — done when: each seam has unit coverage including its failure path, and each new test is proved red by perturbation.
- [x] T7 Integration tests — files: `tests/nativeweb.signin.epass.tests.integration/` — covers: R1, R2, R5, R6, R7 — done when: sign-out mid-request works against a real host, the two sign-out headers are compared, the provider is proved uncalled on refusal, and two refused addresses are proved indistinguishable.
- [ ] T8 e2e sign-out — files: `tests/nativeweb.signin.epass.tests.e2e/` — covers: R10 — done when: a real browser is signed out by consumer code that never posts to the logout path.
- [ ] T9 AOT gate — files: `tests/nativeweb.signin.epass.tests.aot/Program.cs` — covers: R8 — done when: all three seams are exercised in the AOT test app and the publish is warning-free.
- [ ] T10 Docs and visibility — files: `README.md`, `CHANGELOG.md`, `work/status/`, `docs/specs/009-*/` — covers: — done when: the three seams are documented for a consumer, and the status files carry the work.

**Analyze.** Every requirement R1–R10 maps to at least one task; every task traces to a plan
section. R9 (justsmtp adopts all three) is deliberately **not** a task here — it is a separate
branch in a separate repository, sequenced after this one merges, and the plan says so. No orphans.
