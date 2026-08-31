# 016 — Plan

Spec: [`./016-spec.md`](./016-spec.md)

## Approach

**Recover `nativeweb.signin.epass` from `f743436^`, delete the provider-specific half, and replace
the session model.** Three-quarters of the removed library is provider-neutral already and carries
findings that took two specs to establish — `ReturnPath.Sanitise` (rejects `\` and `:` anywhere
rather than enumerating schemes), `ILoginFlowStore`'s atomic-take contract, the constant-time
signature compare, the `__Host-` prefix enforced at startup, the placeholder-key refusal, the
`RedirectUri`/`CallbackPath` pairing check, the degradation-versus-misconfiguration split, and the
manual (non-reflecting) configuration read that keeps it AOT-clean. Rewriting those from a spec
would lose most of them; that is the argument decision 008 made for extracting them in the first
place.

Two things do not survive the move:

**1. The sign-in entry point.** The old `POST {LoginPath}` took an email address and called
`StartLoginLinkAsync` against `urn:epass:grant:login-link` — a custom, unregistered grant. That is
the provider's flow and is a spec non-goal. It is replaced by `GET {LoginPath}` → 302 to the
provider's configured authorization endpoint with `response_type=code`, `state`, `code_challenge`
and `code_challenge_method=S256`. The callback, the code exchange and the userinfo call carry over
almost unchanged; they were already the standard grant.

**2. The session model, which is the one substantive redesign.** The old cookie was a stateless
HMAC-SHA256 ticket — `v2.subject.issuedAt.expiresAt` plus signature, no server-side record — and
it discarded the access token immediately after one userinfo call. It never refreshed. Spec R7
(opaque, no user data), R10 (pluggable store) and R13 (silent refresh) together rule it out, and
the deciding factor is not refresh but **rotation**: the provider rotates refresh tokens and
revokes the entire token family when a consumed one is re-presented. The alternative considered
and rejected was carrying the encrypted token pair in the cookie itself — no store, and R8 met
trivially — but that puts a 14-day rotating refresh token on the client channel, and with no
shared row there is nowhere to serialize rotation, so the concurrent-refresh race below becomes
structural rather than an edge case, with family-wide revocation as its failure mode.

So: **an opaque 256-bit cookie token over an `ISessionStore` the product implements.** The row is
keyed by `SHA-256(token)`, so the cookie value is not recoverable from the store, its backups or
its replication stream (R8). The hash is unsalted and uniterated on purpose — salting and
iteration compensate for low-entropy secrets, and a 256-bit uniform random token has nothing to
compensate for; a slow KDF would only add latency to the hot per-request lookup. Access and
refresh tokens in the row are encrypted with the existing `StoreDataProtector` (AES-GCM), so a
store dump yields neither the cookie value nor a usable provider credential.

**Only one session model ships.** A second, stateless one would mean two cookie formats, two
validation paths and a doubled test matrix — and it would not be an alternative model but a
different capability wearing the same interface, since it cannot refresh under rotation. A product
that wants the old sign-in-and-discard behaviour gets it as a `RetainTokens = false` mode on the
one pipeline, not as a second format.

## Tech / dependencies

Verified present, nothing new added:

| dependency | version / source | why |
|---|---|---|
| .NET | 10, C# 14, `PublishAot=true` | repo-wide, `Directory.Build.props` |
| `Microsoft.AspNetCore.Authentication` | framework reference | `AuthenticationHandler<>`, `IAuthenticationSignOutHandler` |
| `IHttpClientFactory` | framework | already used by the recovered `AddHttpClient<EpassClient>` with a 10 s timeout |
| `System.Text.Json` source generation | framework | the recovered client already uses a `JsonSerializerContext`; required for AOT |
| `libs/nativeweb.security` | in-repo | `OpaqueTokens.Generate`/`Hash` (R7), `StoreDataProtector` (R8) |
| `libs/nativeweb.web` | in-repo | pipeline and options conventions to match |

**No JWT or JWKS library.** Spec non-goal; the provider issues no `id_token`. This is what keeps
the AOT gate cheap.

**No configuration binder.** Options are filled by a consumer delegate and validated manually, as
the recovered `AddEpassSignIn` already does. A reflecting binder is the one dependency that would
break `PublishAot` (R25).

## Changes by area

New library, mirroring the recovered layout. Namespace `NativeWeb.SignIn`; `libs/nativeweb.auth`
is deliberately **not** reused as a name — it belonged to the identity provider and carries the
wrong meaning.

| path | what | covers |
|---|---|---|
| `libs/nativeweb.signin/ReturnPath.cs` | recovered verbatim; namespace only | R5 |
| `libs/nativeweb.signin/ILoginFlowStore.cs` | recovered verbatim; namespace only. `FlowTake`, `FlowTakeResult`, `LoginFlow` with its redacting `ToString` | R2, R3 |
| `libs/nativeweb.signin/LoginFlowSweeper.cs` | recovered; hosted service, GC only | R4 |
| `libs/nativeweb.signin/SignInOptions.cs` | from `EpassSignInOptions`. `BaseUrl` → explicit `AuthorizationEndpoint`, `TokenEndpoint`, `UserInfoEndpoint`, `IntrospectionEndpoint`; adds `Scopes`, `ClientAuthentication`, `RefreshSkew`, `RetainTokens`. Keeps `Validate()` whole: `__Host-` prefix, placeholder-key refusal, relative-path guards, `RedirectUri`/`CallbackPath` pairing, `IsConfigured` degradation | R17, R19, R30 |
| `libs/nativeweb.signin/SignInEndpoints.cs` | `GET {LoginPath}` → 302 with PKCE+state (**new**); `GET {CallbackPath}` → take flow, exchange, userinfo, mint session (recovered); `POST {LogoutPath}` (recovered) | R1, R2, R3, R5 |
| `libs/nativeweb.signin/OAuthClient.cs` | from `EpassClient`. Keeps `SendAsync`/`ReadErrorAsync` and the refused-vs-unavailable split. `StartLoginLinkAsync` **deleted**. `ExchangeCodeAsync` and `GetUserInfoAsync` generalized; adds `RefreshAsync` and `IntrospectAsync` | R1, R13, R14, R31 |
| `libs/nativeweb.signin/OAuthDtos.cs` | from `EpassDtos`. `TokenResponse` gains `refresh_token`, `expires_in`, `scope`; `UserInfo` is `sub`/`email`/`email_verified`; `IntrospectionResponse` models `active`, `scope`, `sub`, `exp`, `client_id` **and ignores every other field** | R6, R31 |
| `libs/nativeweb.signin/SignInResult.cs` | from `EpassResult`; the refused/unavailable distinction 008 found and a rewrite drops | R16 |
| `libs/nativeweb.signin/ISessionStore.cs` | **new.** The R10 seam, with the compare-and-swap primitive R13 needs — see Data / interfaces | R7, R8, R10, R13 |
| `libs/nativeweb.signin/InMemorySessionStore.cs` | **new.** The shipped implementation: single-instance and test use, no schema, no migration | R10 |
| `libs/nativeweb.signin/SessionManager.cs` | **new.** Mint, read, refresh-with-CAS, revoke. Replaces `SessionCookie`'s minting; keeps its base64url alphabet check and `SessionReadResult` shape | R7, R8, R9, R13 |
| `libs/nativeweb.signin/SessionAuthenticationHandler.cs` | recovered; now reads through `SessionManager`. Implements `IAuthenticationSignOutHandler` so `SignOutAsync` works mid-request — 009 R1/R2 | R11, R12 |
| `libs/nativeweb.signin/BearerIntrospectionFilter.cs` | **new.** `IEndpointFilter`; introspect + TTL cache never outliving the reported `exp`; one indistinguishable refusal | R14, R15, R16 |
| `libs/nativeweb.signin/ServiceCollectionExtensions.cs` | recovered `AddEpassSignIn` → `AddSignIn(name, configure)`; named schemes for R18; keeps `AddAntiforgery`, `TryAddSingleton(TimeProvider.System)`, `AddAuthorization`, and the startup-check hosted service registered first | R11, R18, R19 |
| `libs/nativeweb.signin/SignInBuilder.cs` | recovered; `WithFlowStore`, `WithSessionStore`, `OnSignedIn`, `WithSignInPolicy` | R22, R23, R24 |
| `libs/nativeweb.signin/README.md` | from the recovered 263-line README, de-provider'd; adds the pairwise-subject warning R6 requires | R6, R29 |
| `libs/nativeweb.signin.testing/` | recovered `TestSession.cs` + `LoginFlowStoreConformance.cs`; adds `SessionStoreConformance.cs` (must include the concurrent-CAS case) | R20, R21 |
| `tests/nativeweb.signin.tests.unit/` | new suite | R26 |
| `tests/nativeweb.signin.tests.integration/` | `WebApplicationFactory` + stub provider | R26, R27 |
| `tests/nativeweb.signin.tests.e2e/` | Playwright: redirect round trip, cookie attributes, sign-out | R26 |
| `tests/nativeweb.signin.tests.aot/` | recovered pattern; publishes AOT and fails on any warning | R25 |
| `apps/sample/` | a signed-in page and a public page against the stub | R28 |
| `skeleton/apps/__product__/Program.cs` | register the scheme, so a stamped product authenticates | R11, R28 |
| `NativeWeb.slnx`, `docs/README.md`, `CHANGELOG.md`, `work/` | wiring and visibility | — |

## Data / interfaces

**`ISessionStore`** — the one place the design departs from a naive `Get`/`Set`:

```
Task CreateAsync(string keyHash, SessionRecord record, CancellationToken ct);
Task<SessionRecord?> GetAsync(string keyHash, CancellationToken ct);
Task<SessionReplace> ReplaceIfMatchAsync(
    string keyHash, string expectedRefreshTokenHash, SessionRecord replacement, CancellationToken ct);
Task RemoveAsync(string keyHash, CancellationToken ct);
Task SweepAsync(CancellationToken ct);
```

`SessionRecord` = subject, issued-at, expires-at, protected access token, protected refresh token,
access-token expiry, refresh-token hash. `SessionReplace` = `Replaced` | `Conflict` | `Gone`.

`ReplaceIfMatchAsync` is a contract in the same sense `ILoginFlowStore.TakeAsync` is, and the
XML-doc must say so: **a read-then-write implementation compiles, passes every single-threaded
test, and destroys sessions in production.** The reason is in Risks below.

**Wire contract the client is written against** (measured against epass.io on 2026-09-01, in
`tmp/epass-rp-contract.log`; treated as one conforming provider, not as the specification):
authorization endpoint takes `client_id`, `redirect_uri`, `response_type=code`, `scope`, `state`,
`code_challenge`, `code_challenge_method=S256` (`plain` refused); token endpoint is
form-encoded with `grant_type` ∈ {`authorization_code`, `refresh_token`}, client auth by
`client_secret_basic` | `client_secret_post` | `none`; token response is
`access_token`/`token_type`/`expires_in`/`refresh_token`/`scope`; errors are
`{error, error_description}`; userinfo returns `sub`/`email`/`email_verified`; introspection is
form-encoded `token` with confidential-client auth, returning `active` plus optional
`scope`/`client_id`/`sub`/`exp` **and provider-specific fields the client must ignore**.

## Risks & mitigations

| risk | mitigation |
|---|---|
| **Concurrent refresh revokes the whole family.** Two requests find the access token expired, both present the same refresh token; the provider consumes the first and treats the second as reuse, revoking every token in the chain. A benign race signs the user out. | `ReplaceIfMatchAsync` compare-and-swap: one writer wins, the loser re-reads and proceeds on the winner's fresh token. An in-process semaphore per session key is an optimization, never the guarantee — it does not hold across instances. The conformance suite must include the concurrent case, or a product ships a read-then-write store and finds this in production. |
| **A store implementation that is not atomic.** Same shape as the `ILoginFlowStore` hazard 008 documented. | `SessionStoreConformance` in the testing package, exercising both CAS and single-use take under concurrency. A product runs it against its own store. |
| **The open redirect re-enters through the fallback.** | `ReturnPath.Sanitise` stays `internal` and `IsUsableFallback` validates every configured redirect target at startup — recovered behaviour, kept. |
| **A test-minting seam becomes an authentication bypass.** | Minting lives in the testing package only; the production assembly exposes none. 009 R3, recovered. |
| **Pairwise subject treated as a portable user key.** Not a defect we can prevent in code: the provider derives `sub` per application, and a provider-side mode change re-derives every one. | R6 documentation, stated in the README next to the subject property rather than in a guide nobody opens. |
| **Introspection cache outliving a revoked token.** | Cache entry expiry is `min(configured TTL, reported exp)`; revocation inside the window is accepted as the cost of caching and is documented, with the TTL configurable so a product can choose zero. |
| **Refused/unavailable collapse.** A rewrite drops the distinction and a login page throws instead of degrading. | `SignInResult` recovered whole, and the unconfigured-box degradation path (`IsConfigured`) with it. |
| **Cookie `SameSite` too strict for the provider return.** `Strict` drops the cookie on the redirect back. | `Lax`, as the recovered options fix it; not settable, tested at e2e tier where a browser is the only honest judge. |
| **Two consumers cannot adopt this.** ghosthive's control plane needs the bearer filter and its web app needs the full flow; justsmtp needs the login-link grant, which is out of scope. | Stated in the spec's non-goals and in both handoff documents. justsmtp's path is an epass-specific package layered on this one. |

## Coverage check

| R# | covered by |
|---|---|
| R1, R2, R3 | `SignInEndpoints`, `ILoginFlowStore` |
| R4 | `LoginFlowSweeper` |
| R5 | `ReturnPath` + startup validation |
| R6 | `OAuthDtos.UserInfo`, README |
| R7, R8 | `SessionManager` + `OpaqueTokens.Hash` + `StoreDataProtector` |
| R9 | `SignInOptions` fixed cookie attributes, e2e |
| R10 | `ISessionStore`, `InMemorySessionStore` |
| R11 | `ServiceCollectionExtensions`, skeleton |
| R12 | `SessionAuthenticationHandler` : `IAuthenticationSignOutHandler` |
| R13 | `OAuthClient.RefreshAsync` + `ReplaceIfMatchAsync` |
| R14, R15, R16 | `BearerIntrospectionFilter` |
| R17, R18, R19 | `SignInOptions`, named schemes, `Validate()` |
| R20, R21 | `nativeweb.signin.testing` |
| R22, R23, R24 | `SignInBuilder.WithSignInPolicy` |
| R25 | `tests/nativeweb.signin.tests.aot` |
| R26, R27 | three test suites + stub provider |
| R28 | `apps/sample`, skeleton |
| R29 | `libs/nativeweb.signin/README.md` |
| R30 | `SignInOptions.ClientAuthentication` |
| R31 | `OAuthDtos` ignoring unmodelled fields |

**No uncovered requirement.** Two carry a caveat rather than a clean cover: R15's cache cannot
observe a revocation inside its window (mitigated by bounding to the reported `exp` and allowing a
zero TTL), and R6 is documentation because the hazard is the provider's semantics, not this code's
behaviour.
