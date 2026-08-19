# 008 — Plan
Spec: ./008-spec.md

## Approach

One new library, `libs/nativeweb.signin.epass`, referencing **only** `libs/nativeweb.security`.
The ring split lands `Pkce.cs` and `Masking.cs` into `nativeweb.security` rather than into a new
micro-library: that project is already ring 0, BCL-only, has no project references, and already
holds two of the four primitives a client needs (`OpaqueTokens`, `Sha256Hex`). `nativeweb.auth`
already references it, so `AuthFlows.cs:48` keeps `Masking` with a `using` change and **no cycle is
possible**. A `nativeweb.oauth.primitives` library would hold two static classes and duplicate a
ring that exists.

The alternative considered and rejected was leaving forwarding shims in `nativeweb.auth`. Every
call site is inside this repository and countable (15 of them, listed below), so the move is atomic
and the shim would be permanent debt bought for nothing.

The second decision shaping everything is that **the library owns the security-critical path and
does not expose it as opt-out helpers**. A public `ReturnPath.Sanitise(candidate, fallback)` is a
guard a consumer can simply not call; sanitisation therefore happens inside the callback endpoint,
and the fallback is validated at startup. The same reasoning governs the result type, the cookie
name, and the provider's error strings.

## Tech / dependencies

- .NET 10, C# 14, NativeAOT, `TreatWarningsAsErrors` repo-wide. No new packages.
- `Microsoft.AspNetCore.App` framework reference for `AuthenticationHandler`, `HttpContext`,
  endpoint routing.
- JSON through a hand-written `JsonSerializerContext` ported from `EpassDtos.cs`, using
  `HttpClient` overloads that take `JsonTypeInfo<T>`. Never a reflection overload.
- **No `IConfiguration.Bind()`** — credential keys are read manually in `AddEpassSignIn`, matching
  how `apps/web/Program.cs` reads config. The config-binder source generator is the alternative and
  manual is simpler.
- No source generator ships in this library. The measured RDG failure (a generator's `Map*` calls
  are invisible to the Request Delegate Generator) does not apply: `MapEpassSignIn` is hand-written
  C# in this project's own syntax trees, so RDG sees and intercepts it. A NativeAOT publish gate
  runs before any consumer exists, to catch this rather than assume it.

## Changes by area

### A. Ring split — one atomic commit (R10)

- `libs/nativeweb.auth/Pkce.cs` → `libs/nativeweb.security/Pkce.cs`, namespace change.
- `libs/nativeweb.auth/Masking.cs` → `libs/nativeweb.security/Masking.cs`, namespace change.
- `using` updated at 15 measured call sites: `libs/nativeweb.auth/AuthFlows.cs:48`;
  `apps/web/Auth/` — `OAuthTokenEndpoints.cs:161`, `OAuthLoginLinkEndpoints.cs:113,224`,
  `OAuthEndpoints.cs:114`, `OAuthDeviceEndpoints.cs:95,200`, `FakeIdentifierResponses.cs:50,75`,
  `PassphraseEndpoints.cs:254,268`; tests — `OAuthTokenEndpointsTests.cs:46`,
  `OAuthEndpointsTests.cs:33`, `OAuthFlowTests.cs:39,78,115`, `OAuthMachineFlowTests.cs:125,184`.
- `PassphrasePolicy` stays in `nativeweb.auth`: it is a policy of the identity provider, not an
  OAuth-client primitive, and no client uses it.
- `OpaqueTokens` needs nothing — it is already in `nativeweb.security`, not in the library being
  carved up.

The live IdP never sees an intermediate state because the move and every reference update are one
commit.

### B. The session ticket — format change (R9)

`SessionCookie` ports over, but the wire format changes and this is the one deliberate behavioural
break in the plan.

Measured today: the payload is `v1.{userId}.{issuedAt}.{expiresAt}`, delimiter-joined and parsed by
a strict 5-field split. A `long` is delimiter-free by construction; **a string subject containing
`.` yields 6 fields and is rejected as `Malformed`**. That fails closed — it is a lockout, not a
forgery — but it means the library silently cannot support a whole class of subject.

**Decision: `v2`, with each field base64url-encoded before joining.** The delimiter then cannot
occur in a field. Consequences, both accepted:

- Existing justsmtp session cookies stop verifying at cutover; every signed-in user logs in once.
  Recorded in `~/.claude/away-questions.md` because spec R12 says sign-in behaviour is unchanged,
  and a one-time forced re-login is arguably a violation of that.
- The subject is a length-capped, charset-validated string. Uncapped, a large subject pushes the
  cookie past 4KB and browsers drop it silently, which presents as an unexplained sign-in loop.

Signature verification stays constant-time and stays *before* any payload field is believed.

### C. The flow store contract (R3, R4)

```csharp
public enum FlowTake { Taken, Unknown, AlreadyUsed, Expired }
public readonly record struct LoginFlow(string Verifier, string ReturnTo);
public readonly record struct FlowTakeResult(FlowTake Outcome, LoginFlow? Flow);

public interface ILoginFlowStore
{
    Task StartAsync(string state, string verifier, string returnTo, CancellationToken ct);
    Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct);
    Task SweepAsync(CancellationToken ct);
}
```

Four shape decisions, each closing a way a consumer could lose the single-use property:

1. **`Flow` is nullable and null unless `Taken`.** The measured tuple forces a consumer to
   fabricate a `LoginFlow` for `Unknown`/`AlreadyUsed`/`Expired`, and a consumer returning the real
   verifier alongside `AlreadyUsed` would hand a used flow's PKCE verifier to the callback path.
2. **`LoginFlow` gets an explicit `ToString` that redacts `Verifier`.** The compiler-generated one
   prints every field, so a consumer structured-logging the flow, or putting it in an exception
   message, writes the PKCE verifier into logs.
3. **Atomicity is contractual and tested, not documented.** Returning `Taken` asserts a
   compare-and-swap consumed the row atomically; a read-then-mark implementation compiles, passes
   single-threaded tests, and signs a replayed link in twice under concurrency. The library ships an
   **abstract conformance fixture** consumers inherit — `Two concurrent takes of one state yield
   exactly one Taken` — so the property is exercised by the consumer's own store rather than
   promised in a doc comment.
4. **Expiry is enforced inside `TakeAsync`, not in `SweepAsync`.** Sweep is garbage collection. If
   expiry lives only there, unswept rows stay live indefinitely and `Expired` is never returned.

`SweepAsync` is driven by a library-owned `IHostedService`, so no consumer behaviour depends on
remembering to call it; an uncalled sweep is unbounded table growth reachable from an unauthenticated
endpoint.

### D. The result type (R5, R6, R7)

```csharp
public enum EpassOutcome { Ok, Refused, Unavailable }

public readonly struct EpassResult<T>
{
    public EpassOutcome Outcome { get; }
    public TOut Match<TOut>(Func<T, TOut> ok, Func<TOut> refused, Func<TOut> unavailable);
}
```

No `IsOk`, no `Success`, no `Value`, no implicit conversion. `Match` with three mandatory delegates
is the only way to reach `T`, so the three-way distinction cannot be collapsed to a bool. A
`switch` over `Outcome` missing an arm is CS8509, which `TreatWarningsAsErrors` turns into a build
break.

**No provider-supplied string appears on any public type** — not `error_description`, and not
`error`, which is also upstream text. Both stay in internal fields the library logs through its own
`ILogger`. `Refused` carries a library-owned closed enum mapped from provider codes, with unmapped
codes collapsing to a generic value. A property named `Description` or `Message` on a public result
gets rendered into a page inside a year; the signature is the documentation.

`IsConfigured` stays a plain `bool` on the injected client, because a consumer's login page needs it
to render or suppress the button. An unconfigured client returns `Unavailable` from every call
rather than throwing (R6).

### E. Registration and endpoints (R1, R2, R11)

```csharp
public static EpassSignInBuilder AddEpassSignIn(
    this IServiceCollection services,
    IConfigurationSection credentials,
    Action<EpassSignInOptions> configure);

public sealed class EpassSignInBuilder
{
    public EpassSignInBuilder WithFlowStore<TStore>() where TStore : class, ILoginFlowStore;
    public EpassSignInBuilder OnSignedIn(SignInResolver resolver);
}

public delegate ValueTask<string?> SignInResolver(
    HttpContext http, VerifiedSubject subject, UnverifiedContactInfo contact, CancellationToken ct);

public static IEndpointRouteBuilder MapEpassSignIn(this IEndpointRouteBuilder endpoints);
```

- The resolver returns the local user key as a string; **null means locally refused** and no session
  is minted.
- `VerifiedSubject` / `UnverifiedContactInfo` are named for what they are. A bare
  `(sub, email)` signature invites a find-or-create keyed on **email**, which lets anyone who
  registers at the IdP with a victim's address take over the local account. Provisioning must key on
  the subject, and the parameter type says so.
- `EpassSignInOptions` carries the paths (start, callback, logout, and the consumer's login page as
  the failure-redirect target), the cookie name, and the return-path fallback. All are validated
  when the builder is finalised:
  - **cookie name must start with `__Host-`**, rejected otherwise. `Secure`, `HttpOnly` and
    `SameSite` are not consumer-settable at all. `apps/web/Auth/SignInEndpoints.cs:405-414` already
    sets `Secure` unconditionally for exactly this reason.
  - **return-path fallback must be a relative path**, so the open redirect cannot re-enter through
    the parameter meant to close it.
  - **signing key: no key and no explicit opt-in throws at startup.** The measured behaviour today
    generates an ephemeral key and logs a warning, which as a library default means a production app
    that forgot a config entry boots fine, loses every session on restart and on every scale-out,
    and signals it only in a log line nobody reads. Ephemeral requires
    `AllowEphemeralSigningKey = true`, which templates wire to `IsDevelopment()`. Supplied keys are
    checked for a 32-byte minimum.
- The GET login **page** stays with the consumer (R11: markup is theirs). The library redirects to
  it with a machine-readable code on failure, never provider text.
- Any in-memory `ILoginFlowStore` ships in the test project, not on the library's public surface. A
  convenient public one gets registered in production, where it loses flows on recycle and returns
  `Unknown` across instances.

### F. Tests (R15)

- **Unit** — ticket round-trip and every `SessionReadResult`; a one-byte mutation is `BadSignature`,
  distinguishable from absence; subject charset and length limits; `ReturnPath` against off-site,
  protocol-relative and scheme-bearing candidates; `Match` exhaustiveness; unconfigured client
  returns `Unavailable`.
- **Integration** — against real PostgreSQL, with a JauntyQ-backed `ILoginFlowStore` in the test
  project. This is also the first real proof of the store seam. Includes the concurrency
  conformance fixture, and a test that renders a provider failure and asserts no provider-supplied
  text appears in the body (R7).
- **E2E** — Playwright over the full sign-in, replayed-link, and sign-out paths.

## Data / interfaces

The library defines no schema and issues no query. `ILoginFlowStore` is the entire data seam; the
consumer owns the table and the JauntyQ queries behind it. This mirrors `IScaffoldStore<T>` in
`libs/nativeweb.scaffold` and for the same reason.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Consumer implements `TakeAsync` non-atomically, losing replay resistance | Inheritable concurrency conformance fixture, not a doc comment |
| Ticket format change signs everyone out at cutover | Accepted and recorded; alternative (dual `v1`/`v2` read) is more code and a deprecation to remember |
| Ring split breaks the live IdP | One atomic commit, all 15 call sites enumerated from a measured search, full suite before merge |
| Provider text reaches a page | No provider string on any public type; asserted by an integration test |
| Consumer keys provisioning on email | Parameter types named `VerifiedSubject` / `UnverifiedContactInfo` |
| Ephemeral key ships to production | Throws at startup without explicit opt-in |
| PKCE verifier leaks through logs | `LoginFlow.ToString` redacts it |
| Cross-repo cutover: justsmtp CI restores NativeWeb as a package with no reachable feed | Known gap in `work/todo.md`; the cutover uses a sibling-checkout project reference. Flagged in the handoff |

## Coverage check

| Requirement | Covered by |
|---|---|
| R1 consumer writes ~8 lines | §E |
| R2 library provides endpoints, consumer picks paths | §E |
| R3 at-most-once, four outcomes | §C |
| R4 no query language in the contract | §C, Data/interfaces |
| R5 three outcomes distinguishable | §D |
| R6 boots unconfigured | §D, §E |
| R7 no provider text rendered | §D, §F |
| R8 return path sanitised, fallback validated | §A approach, §E |
| R9 ticket tamper-evident | §B, §F |
| R10 no dependency on the IdP library | §A |
| R11 consumer owns user, cookie name, markup, assets | §E |
| R12 justsmtp migrates, nine files gone | Handed off to its own session; handoff doc written |
| R13 AOT-clean | Tech/dependencies; publish gate in the build order |
| R14 name claims no standard | Settled in the spec |
| R15 three test tiers | §F |

**R12 is not built in this repository.** It is the one requirement this session cannot close, by
design — it is a change to a different repository with its own branch and CI. The handoff at
`~/.claude/handoffs/justsmtp.com/2026-08-19-epass-signin-migration.md` carries the full inventory,
the pre-authorization and its conditions.

## Build order

1. Ring split (§A), atomic, full suite, merge.
2. Library skeleton + bucket A port + ticket `v2` (§B), unit tests, **NativeAOT publish gate**.
3. Contract, result type, registration, endpoints (§C, §D, §E).
4. Integration tier with a JauntyQ-backed store (§F).
5. E2E tier (§F).
6. Review: architecture by Fable, security by glm-5.3, each finding verified here before acting.
