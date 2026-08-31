# 016 — Tasks

Plan: [`./016-plan.md`](./016-plan.md)
Spec: [`./016-spec.md`](./016-spec.md)

Recovered sources are extracted at `tmp/recovered-signin/` from `f743436^`. "Recovered" below
means the file carries over with its comments and its reasoning intact; the namespace changes and
provider-specific names go. Deleting a comment from recovered code is a regression, not tidying —
each one records a finding from spec 008 or 009.

## Foundation

- [x] **T1** Library and test projects — files: `libs/nativeweb.signin/nativeweb.signin.csproj`, `libs/nativeweb.signin.testing/nativeweb.signin.testing.csproj`, `NativeWeb.slnx` — covers: R25 — done when: `dotnet build NativeWeb.slnx` succeeds with both projects present, `IsPackable`/`PackageId` set to match the other eight libraries, and zero warnings.
- [x] **T2** `ReturnPath` — files: `libs/nativeweb.signin/ReturnPath.cs`, `tests/nativeweb.signin.tests.unit/ReturnPathTests.cs` — covers: R5 — done when: `Sanitise` is internal, `IsUsableFallback` public, and tests refuse `https://evil.test/x`, `//evil.test`, `/\evil.test`, `/x:y`, a control character, empty and null, while accepting `/`, `/a/b?q=1#f`.
- [x] **T3** Login-flow store seam — files: `libs/nativeweb.signin/ILoginFlowStore.cs`, `tests/nativeweb.signin.tests.unit/FlowTakeResultTests.cs` — covers: R2, R3 — done when: `FlowTake`, `FlowTakeResult` (private ctor, four factories) and `LoginFlow` with its redacting `ToString` are recovered, and a test asserts `ToString()` does not contain the verifier.
- [x] **T4** Flow sweeper — files: `libs/nativeweb.signin/LoginFlowSweeper.cs`, `tests/nativeweb.signin.tests.unit/LoginFlowSweeperTests.cs` — covers: R4 — done when: the sweep body is reachable independently of the timer, a store that throws is swept again on the next tick rather than stopping the host, and the loop is driven over several short intervals to prove it repeats. **Amended during the build:** the original wording said "against a fake `TimeProvider`". The repo's `FixedTimeProvider` pins `UtcNow` only and cannot drive a `PeriodicTimer`, so asserting through a timer fake would have meant writing one; the sweep body is tested directly instead and the loop is driven on a short real interval. `TimeProvider` is still injected, so the seam exists for a consumer that has a timer-capable fake.

## Configuration and wire types

- [x] **T5** `SignInOptions` and `Validate()` — files: `libs/nativeweb.signin/SignInOptions.cs`, `tests/nativeweb.signin.tests.unit/SignInOptionsTests.cs` — covers: R9, R17, R19, R30 — done when: `BaseUrl` is replaced by four explicit endpoint URLs, `Scopes`/`ClientAuthentication`/`RefreshSkew`/`RetainTokens` exist, cookie attributes are fixed and unsettable, and tests cover each `Validate()` refusal separately: non-`__Host-` cookie name, absent key without the ephemeral opt-in, short key, single-repeated-byte key, off-site `DefaultReturnPath`, each non-relative route, unparseable endpoint URL, and `RedirectUri`/`CallbackPath` disagreement.
- [x] **T6** Wire DTOs and JSON context — files: `libs/nativeweb.signin/OAuthDtos.cs`, `tests/nativeweb.signin.tests.unit/OAuthDtosTests.cs` — covers: R6, R31 — done when: `TokenResponse`, `UserInfo` and `IntrospectionResponse` deserialize through a source-generated `JsonSerializerContext`, and a test feeds each one a payload carrying unmodelled fields (`realm`, `realm_id`, `operator`, `enrollment_credential_id`) and asserts success.
- [x] **T7** `SignInResult` — files: `libs/nativeweb.signin/SignInResult.cs`, `tests/nativeweb.signin.tests.unit/SignInResultTests.cs` — covers: R16 — done when: refused and unavailable are distinguishable to the caller, and a test asserts the failure carries no provider detail.

## Provider client

- [x] **T8** Code exchange and userinfo — files: `libs/nativeweb.signin/OAuthClient.cs`, `tests/nativeweb.signin.tests.unit/OAuthClientExchangeTests.cs` — covers: R1, R30 — done when: `ExchangeCodeAsync` and `GetUserInfoAsync` work against a stub `HttpMessageHandler`; the recovered `SendAsync`/`ReadErrorAsync` refused-vs-unavailable split is kept; and a test per client-authentication mode asserts credentials appear in the Authorization header, in the form body, or nowhere, and nowhere else.
- [x] **T9** Refresh and introspection calls — files: `libs/nativeweb.signin/OAuthClient.cs`, `tests/nativeweb.signin.tests.unit/OAuthClientRefreshTests.cs` — covers: R13, R14 — done when: `RefreshAsync` posts `grant_type=refresh_token` and returns the rotated pair, `IntrospectAsync` posts a form-encoded `token` with confidential-client auth, and a provider `invalid_grant` is surfaced as refused rather than thrown.

## Session

- [x] **T10** `ISessionStore` and the in-memory implementation — files: `libs/nativeweb.signin/ISessionStore.cs`, `libs/nativeweb.signin/InMemorySessionStore.cs`, `tests/nativeweb.signin.tests.unit/InMemorySessionStoreTests.cs` — covers: R8, R10 — done when: the interface carries `CreateAsync`/`GetAsync`/`ReplaceIfMatchAsync`/`RemoveAsync`/`SweepAsync` with `SessionReplace` = `Replaced`|`Conflict`|`Gone`; the XML doc on `ReplaceIfMatchAsync` states that a read-then-write implementation passes every single-threaded test and destroys sessions in production; and a test drives 50 concurrent `ReplaceIfMatchAsync` calls with the same expected hash and asserts exactly one `Replaced`.
- [x] **T11** `SessionManager` mint, read, revoke — files: `libs/nativeweb.signin/SessionManager.cs`, `tests/nativeweb.signin.tests.unit/SessionManagerTests.cs` — covers: R7, R8, R9 — done when: the cookie value is `OpaqueTokens.Generate()`, the row is keyed by `OpaqueTokens.Hash` of it, tokens are protected with `StoreDataProtector`, and a test dumps every stored row and asserts the cookie value appears in none of them and that no stored field is the plaintext access or refresh token.
- [x] **T12** Refresh with compare-and-swap — files: `libs/nativeweb.signin/SessionManager.cs`, `tests/nativeweb.signin.tests.unit/SessionRefreshTests.cs` — covers: R13 — done when: a token inside `RefreshSkew` of expiry refreshes with no redirect; a provider refusal removes the session rather than leaving a dead token; and a test drives two concurrent requests through refresh against a store counting provider calls, asserting the provider is called **once** and the loser proceeds on the winner's fresh token.
- [x] **T13** Authentication handler and sign-out — files: `libs/nativeweb.signin/SessionAuthenticationHandler.cs`, `tests/nativeweb.signin.tests.unit/SessionAuthenticationHandlerTests.cs` — covers: R11, R12 — done when: the handler authenticates through `SessionManager`, implements `IAuthenticationSignOutHandler`, and a test compares the `Set-Cookie` from `SignOutAsync` against the one from the logout endpoint byte-for-byte.

## Endpoints

- [x] **T14** Sign-in redirect — files: `libs/nativeweb.signin/SignInEndpoints.cs`, `tests/nativeweb.signin.tests.unit/SignInRedirectTests.cs` — covers: R1, R2 — done when: `GET {LoginPath}` records a flow and returns 302 to the authorization endpoint carrying `response_type=code`, a random `state`, `code_challenge` and `code_challenge_method=S256`; two consecutive calls produce different `state` and different challenges; and an unconfigured host degrades rather than throwing.
- [x] **T15** Callback — files: `libs/nativeweb.signin/SignInEndpoints.cs`, `tests/nativeweb.signin.tests.unit/SignInCallbackTests.cs` — covers: R1, R3, R5 — done when: a valid callback takes the flow, exchanges, calls userinfo and mints a session; an unknown `state` is refused with no token request made; a replayed callback is refused; and the landing path is sanitised through `ReturnPath`.
- [x] **T16** Sign-out endpoint — files: `libs/nativeweb.signin/SignInEndpoints.cs`, `tests/nativeweb.signin.tests.unit/SignOutEndpointTests.cs` — covers: R12 — done when: `POST {LogoutPath}` validates antiforgery, removes the store row, expires the cookie and redirects to `SignedOutPath`.
- [x] **T17** Bearer introspection filter — files: `libs/nativeweb.signin/BearerIntrospectionFilter.cs`, `tests/nativeweb.signin.tests.unit/BearerIntrospectionFilterTests.cs` — covers: R14, R15, R16 — done when: two requests inside the TTL cause one provider call; a token whose reported `exp` falls inside the TTL is not served from cache past it; a TTL of zero disables caching; and unknown, revoked and expired tokens produce responses identical in status, body and header set.

## Wiring

- [x] **T18** `AddSignIn` — files: `libs/nativeweb.signin/ServiceCollectionExtensions.cs`, `tests/nativeweb.signin.tests.unit/AddSignInTests.cs` — covers: R11, R18, R19 — done when: registration takes a scheme name and a configure delegate with no reflecting binder, keeps `AddAntiforgery`/`TryAddSingleton(TimeProvider.System)`/`AddAuthorization`/startup-check-first, `Validate()` runs at registration, and a test registers two providers and asserts a session for one does not authenticate against the other.
- [x] **T19** `SignInBuilder` and the consumer policy — files: `libs/nativeweb.signin/SignInBuilder.cs`, `tests/nativeweb.signin.tests.unit/SignInPolicyTests.cs` — covers: R22, R23, R24 — done when: `WithFlowStore`/`WithSessionStore`/`OnSignedIn`/`WithSignInPolicy` exist; a policy refusal makes no provider call; two refused subjects — one with a local account, one without — produce responses identical in status, location and header set; and a host registering no policy behaves as if the seam did not exist.

## Testing package and tiers

- [x] **T20** Testing package — files: `libs/nativeweb.signin.testing/TestSession.cs`, `libs/nativeweb.signin.testing/LoginFlowStoreConformance.cs`, `libs/nativeweb.signin.testing/SessionStoreConformance.cs` — covers: R20, R21 — done when: a test can mint a valid, an expired and a tampered session through the testing package; the production assembly exposes no minting API (asserted by a test that references only it); and `SessionStoreConformance` includes the concurrent compare-and-swap case from T10.
- [x] **T21** Integration tier and stub provider — files: `tests/nativeweb.signin.tests.integration/` — covers: R26, R27 — done when: a `WebApplicationFactory` host completes the full round trip against an in-process stub provider, with no network and no external service, and the suite passes on a machine with no route to any identity provider.
- [x] **T22** End-to-end tier — files: `tests/nativeweb.signin.tests.e2e/` — covers: R9, R26 — done when: a real browser completes the redirect round trip, the session cookie carries `__Host-`, `HttpOnly`, `Secure` and `SameSite=Lax` and survives the provider return, and sign-out leaves the next request anonymous.
- [x] **T23** AOT publish gate — files: `tests/nativeweb.signin.tests.aot/` — covers: R25 — done when: `dotnet publish -p:PublishAot=true` of a host referencing the library completes with zero trim or AOT analyzer warnings, following the recovered project's pattern.

## Consumers and documentation

- [x] **T24** Reference application — files: `apps/sample/` — covers: R28 — done when: the sample serves a page requiring sign-in and a public page, against the stub provider, with tests at the sample's own tier.
- [x] **T25** Skeleton registers the scheme — files: `skeleton/apps/__product__/Program.cs`, `skeleton/apps/__product__/appsettings.json` — covers: R11, R28 — done when: a freshly stamped product challenges an anonymous request to a scaffolded `[Auth]` route and serves an authenticated one, where today it returns a bare 401 to everybody.
- [ ] **T26** Library README — files: `libs/nativeweb.signin/README.md` — covers: R6, R29 — done when: configuration, wiring, every seam and what a product must supply are documented, and the pairwise-subject warning sits next to the subject property rather than in a section nobody opens.
- [ ] **T27** Provider-neutrality gate and visibility — files: `tests/nativeweb.repo.tests.unit/`, `docs/README.md`, `CHANGELOG.md`, `work/status/current.md`, `work/todo.md` — covers: R17 — done when: a repository test searches the shipped library source for the provider's name and hostname and fails on any match outside test fixtures and documentation examples, and the visibility files record what shipped.

## Analyze

Every requirement maps to at least one task, and every task traces to a "Changes by area" row in
the plan.

| R# | tasks | | R# | tasks |
|---|---|---|---|---|
| R1 | T8, T14, T15 | | R17 | T5, T27 |
| R2 | T3, T14 | | R18 | T18 |
| R3 | T3, T15 | | R19 | T5, T18 |
| R4 | T4 | | R20 | T20 |
| R5 | T2, T15 | | R21 | T20 |
| R6 | T6, T26 | | R22 | T19 |
| R7 | T11 | | R23 | T19 |
| R8 | T10, T11 | | R24 | T19 |
| R9 | T5, T22 | | R25 | T1, T23 |
| R10 | T10 | | R26 | T21, T22 |
| R11 | T13, T18, T25 | | R27 | T21 |
| R12 | T13, T16 | | R28 | T24, T25 |
| R13 | T9, T12 | | R29 | T26 |
| R14 | T9, T17 | | R30 | T5, T8 |
| R15 | T17 | | R31 | T6 |
| R16 | T7, T17 | | | |

**No orphans and no gaps.** 31 requirements, 27 tasks, every task named in the plan.
