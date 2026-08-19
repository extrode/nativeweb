# 008 — Tasks
Plan: ./008-plan.md

Built in this order; each task's tests ship in the same branch as its code.

- [x] **T1** Move `Pkce` and `Masking` into `nativeweb.security` — files: `libs/nativeweb.auth/{Pkce,Masking}.cs` → `libs/nativeweb.security/`, plus `using` updates at 15 call sites (`libs/nativeweb.auth/AuthFlows.cs:48`; `apps/web/Auth/{OAuthTokenEndpoints,OAuthLoginLinkEndpoints,OAuthEndpoints,OAuthDeviceEndpoints,FakeIdentifierResponses,PassphraseEndpoints}.cs`; 4 test files) — covers: R10 — done when: solution builds with 0 warnings, the full existing suite passes unchanged, and `nativeweb.auth` no longer declares either type.

- [x] **T2** Library skeleton — files: `libs/nativeweb.signin.epass/nativeweb.signin.epass.csproj`, `NativeWeb.slnx` — covers: R10, R13 — done when: it references `nativeweb.security` and nothing else, builds clean, and a dependency check confirms `nativeweb.auth` is absent from its graph.

- [x] **T3** Session ticket, format `v2` — files: `libs/nativeweb.signin.epass/SessionCookie.cs` — covers: R9 — done when: each field is base64url-encoded before joining so the delimiter cannot occur in a field; subject is charset- and length-validated; signature is verified constant-time before any field is believed; `SessionReadResult` distinguishes Valid/Malformed/BadSignature/Expired; a one-byte mutation reads as `BadSignature` and absence as `Malformed`.

- [x] **T4** Return-path sanitiser — files: `libs/nativeweb.signin.epass/ReturnPath.cs` — covers: R8 — done when: off-site, protocol-relative (`//evil`), scheme-bearing and backslash-variant candidates all fall back; the fallback itself is rejected at startup unless relative.

- [x] **T5** Flow store contract — files: `libs/nativeweb.signin.epass/ILoginFlowStore.cs` — covers: R3, R4 — done when: `FlowTakeResult.Flow` is null unless `Taken`; `LoginFlow.ToString` redacts `Verifier`; the interface carries no schema or query type; XML docs state that `Taken` asserts an atomic compare-and-swap and that expiry is enforced in `TakeAsync`, not `SweepAsync`.

- [x] **T6** Concurrency conformance fixture — files: `libs/nativeweb.signin.epass/Testing/LoginFlowStoreConformance.cs` — covers: R3 — done when: an abstract xUnit fixture a consumer inherits proves two concurrent takes of one state yield exactly one `Taken`, and it fails against a deliberately non-atomic read-then-mark store.

- [x] **T7** Result type — files: `libs/nativeweb.signin.epass/EpassResult.cs` — covers: R5, R7 — done when: `Match` is the only way to reach `T`; there is no `IsOk`/`Success`/`Value`/implicit conversion; no provider-supplied string (`error` or `error_description`) appears on any public member.

- [x] **T8** Wire DTOs and JSON context — files: `libs/nativeweb.signin.epass/EpassDtos.cs` — covers: R13 — done when: every serializer call goes through a hand-written `JsonSerializerContext`, no reflection overload is used, and provider error fields are internal.

- [x] **T9** Epass client — files: `libs/nativeweb.signin.epass/EpassClient.cs` — covers: R5, R6, R7 — done when: unconfigured returns `Unavailable` from every call rather than throwing; transport failure and timeout map to `Unavailable`; a provider refusal maps to `Refused` carrying a closed enum, never provider text; provider strings reach `ILogger` only.

- [x] **T10** Options, validation and registration — files: `libs/nativeweb.signin.epass/EpassSignInOptions.cs`, `EpassSignInBuilder.cs`, `ServiceCollectionExtensions.cs` — covers: R1, R6, R11 — done when: a cookie name not starting `__Host-` is rejected at startup; `Secure`/`HttpOnly`/`SameSite` are not settable; a missing signing key throws unless `AllowEphemeralSigningKey` is set; a supplied key under 32 bytes is rejected; config is read manually with no `Bind()`.

- [x] **T11** Authentication handler — files: `libs/nativeweb.signin.epass/SessionAuthenticationHandler.cs` — covers: R9, R11 — done when: it reads the consumer-configured cookie name, and a bad signature is logged and treated as anonymous rather than throwing.

- [x] **T12** Endpoints and mapping — files: `libs/nativeweb.signin.epass/SignInEndpoints.cs` — covers: R1, R2, R7, R8 — done when: start/callback/logout map at consumer-chosen paths; sanitisation happens inside the callback and is not reachable as an opt-out helper; failures redirect to the consumer's login page with a machine code; `SignInResolver` returning null mints no session; the sweep runs as a hosted service.

- [x] **T13** Unit tier — files: `tests/nativeweb.signin.epass.tests.unit/` — covers: R15 — done when: ticket, return path, result type, options validation and client outcomes are all covered, and every new green test has been perturbed to confirm it fails.

- [ ] **T14** NativeAOT publish gate — files: `apps/sample` or a probe app — covers: R13 — done when: a publish of a consumer of this library exits 0 with zero IL2026/IL3050, proving RDG sees the hand-written `Map*` calls.

- [ ] **T15** Integration tier — files: `tests/nativeweb.signin.epass.tests.integration/` — covers: R3, R7, R15 — done when: a JauntyQ-backed `ILoginFlowStore` runs against real PostgreSQL, passes the T6 conformance fixture, and a rendered provider failure contains no provider-supplied text.

- [ ] **T16** E2E tier — files: `tests/nativeweb.signin.epass.tests.e2e/` — covers: R15 — done when: Playwright drives sign-in, a replayed link, and sign-out against a running host.

- [ ] **T17** README and docs — files: `libs/nativeweb.signin.epass/README.md`, `CHANGELOG.md`, `work/status/current.md` — covers: R14 — done when: the README shows the ~8-line consumer wiring, states the library is epass-specific and claims no standards conformance, and documents the `v2` ticket format change.

- [ ] **T18** Reviews — covers: all — done when: Fable has reviewed architecture and seams, glm-5.3 has reviewed the security-critical code, every finding has been verified here before being acted on, and the outcome is recorded.

- [ ] **T19** Update the justsmtp handoff — files: `~/.claude/handoffs/justsmtp.com/2026-08-19-epass-signin-migration.md` — covers: R12 (hand-off only) — done when: §5 names the real API instead of TO BE FILLED, and the `v2` re-login consequence is stated.

## Analyze — requirement coverage

| Requirement | Tasks |
|---|---|
| R1 | T10, T12 |
| R2 | T12 |
| R3 | T5, T6, T15 |
| R4 | T5 |
| R5 | T7, T9 |
| R6 | T9, T10 |
| R7 | T7, T9, T12, T15 |
| R8 | T4, T12 |
| R9 | T3, T11 |
| R10 | T1, T2 |
| R11 | T10, T11 |
| R12 | **T19 only — the migration itself is out of this repo** |
| R13 | T2, T8, T14 |
| R14 | T17 |
| R15 | T13, T15, T16 |

No orphan tasks: every task traces to a plan section. One gap, stated rather than hidden — **R12
cannot be closed here**, because it is a change to justsmtp.com. This repository's share of it is
the handoff (T19); the spec cannot reach `Status: done` until that other session lands.
