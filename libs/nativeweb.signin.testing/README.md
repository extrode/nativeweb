# nativeweb.signin.testing

Test support for the signin library, not production code. A product that implements its own
`ILoginFlowStore` or `ISessionStore` for libs/nativeweb.signin references this package from its
test project and inherits a conformance suite that asserts its implementation obeys the store
contract. The same package mints sessions for that product's integration tests.

```xml
<ProjectReference Include="..\..\libs\nativeweb.signin.testing\nativeweb.signin.testing.csproj" />
```

## The whole API

| Type | Kind | For |
| --- | --- | --- |
| `LoginFlowStoreConformance` | abstract class | Derive with your flow store; the inherited `[Fact]` methods assert the take-once contract |
| `SessionStoreConformance` | abstract class | Derive with your session store; the inherited `[Fact]` methods assert the row contract |
| `ConformanceClock` | sealed class | A `TimeProvider` the suites move, so an expiry case costs no wall-clock time |
| `TestSession` | static class | Mints sessions for integration tests, through the framework's own minting code |

Each conformance class has one abstract member, `CreateStore`, which builds an empty store
reading the clock it is handed. Derive, override, run: xunit discovers the inherited facts.

### What `LoginFlowStoreConformance` asserts

| Case | Expected |
| --- | --- |
| A flow just started | `TakeAsync` returns its verifier and return path, `FlowTake.Taken` |
| A state nothing started | Refused, outcome not `FlowTake.Taken` |
| A second take of the same state | Refused |
| A take past the expiry | `FlowTake.Expired` |
| One of two flows taken | The other is still takeable |
| Fifty concurrent takes | A single `FlowTake.Taken` among them |
| `SweepAsync` on a live flow | The flow stays takeable |
| `SweepAsync` on an empty store | Not an error |

Unknown and already used are not distinguished on the refusal path: a store that removes before
judging, which is what makes a concurrent take safe, cannot tell them apart afterwards, and the
contract does not ask it to.

### What `SessionStoreConformance` asserts

| Case | Expected |
| --- | --- |
| A row just created | `GetAsync` returns it |
| A hash nothing created | Null |
| Two rows | Kept apart by hash |
| A row past its expiry | Still returned; expiry is enforced where a session is read |
| `RemoveAsync` | Ends the session |
| `RemoveAsync` on a missing row | Not an error |
| `ReplaceIfMatchAsync`, expected hash | `SessionReplace.Replaced`, row updated |
| `ReplaceIfMatchAsync`, stale hash | `SessionReplace.Conflict`, row left alone |
| `ReplaceIfMatchAsync`, row gone | `SessionReplace.Gone` |
| Fifty concurrent replaces | One `SessionReplace.Replaced`, the rest `SessionReplace.Conflict` |
| `SweepAsync` on a live row | The row survives |
| `SweepAsync` on an empty store | Not an error |

### `TestSession`

| Member | Returns |
| --- | --- |
| `MintAsync` | The cookie value of a live session for a subject |
| `MintExpiredAsync` | The cookie value of a session no reader will accept |
| `Tamper` | A value of the same shape that no store will match |
| `CookieHeader` | The `Cookie` request header carrying a minted value |

## Deriving a suite against your own store

One class per store, one override, and the suite runs with the rest of your tests:

```csharp
using NativeWeb.SignIn.Testing;

public sealed class JauntyQLoginFlowStoreConformance : LoginFlowStoreConformance
{
    protected override ILoginFlowStore CreateStore(TimeProvider clock) =>
        new JauntyQLoginFlowStore(clock);
}

public sealed class JauntyQSessionStoreConformance : SessionStoreConformance
{
    protected override ISessionStore CreateStore(TimeProvider clock) =>
        new JauntyQSessionStore(clock);
}
```

The store under test must read the clock it was handed rather than `DateTimeOffset.UtcNow`.
The suite starts flows at `clock.GetUtcNow() + Lifetime` and moves the clock itself with
`Advance`, so a store judging expiry against its own idea of now fails the expiry fixture and
cannot be reasoned about under clock skew either.

For integration tests, mint through the host's own services and present the header:

```csharp
string cookie = await TestSession.MintAsync(services, "user-1");
string header = TestSession.CookieHeader(services, cookie);

string dead = await TestSession.MintExpiredAsync(services, "user-1", TimeSpan.FromMinutes(5));
string nobody = TestSession.Tamper(cookie);
```

`scheme` defaults to `SessionAuthenticationHandler.SchemeName` on every member that takes one.

## What this library does not do

- Ships no production code and no store implementation. The contracts live in
  libs/nativeweb.signin; this package asserts them.
- Asserts nothing store-specific: no index choice, no SQL shape, no latency budget. A store that
  passes here is contract-obedient, not proven fast.
- Does not reimplement the session format. `TestSession` mints through `SessionManager`, so a
  format change stops a consumer's test compiling instead of letting it pass against a
  hand-rolled cookie it no longer exercises.
- Does not expose minting to production code. Minting is not public on the library on purpose: a
  public mint taking a subject is an authentication bypass any caller can invoke with any
  identity. `libs/nativeweb.signin` grants `InternalsVisibleTo` to two assemblies, its own unit
  tests and this one, and this one is a `DevelopmentDependency`, so nothing in a consumer's
  production closure can reach a minting API.
- `Tamper` forges no signature, because there is none to forge. The cookie is an opaque random
  string whose only meaning is the row it keys, so tampered and made up are the same input to a
  reader, and both are refused as unknown.

## Proved by

The suites and helpers here are exercised by tests/nativeweb.signin.tests.unit and
tests/nativeweb.signin.tests.integration.
