# 009 — Plan
Spec: ./009-spec.md

## Approach

Three independent seams, each added where the library already makes the neighbouring decision, and
each optional so a consumer that ignores it sees no change (R7). None introduces a new concept: the
sign-out seam is the framework's own interface, the address seam is the exact shape of the
`SignInResolver` that already exists, and the test seam is a visibility grant to a package that is
already in the repository for this purpose.

Rejected alternatives, so they are not reopened:

- **A public `SessionAuth` or a public `SessionCookie.Mint`.** Both close the test gap and both
  hand every consumer an authentication bypass. `SessionAuth`'s own remarks already say so.
- **Documented middleware position for the address check.** A consumer would have to re-parse the
  form, re-validate antiforgery, and reproduce the failure redirect — three chances to differ from
  the library, for a check the library is already standing in front of.
- **A `SignInFailureCode` return from the address policy.** The enumeration oracle in spec R6.

## Tech / dependencies

No new packages. `System.Runtime.CompilerServices.InternalsVisibleTo` is a first-party attribute;
`Microsoft.AspNetCore.Authentication.IAuthenticationSignOutHandler` is already in the framework
reference the library uses for `AuthenticationHandler<T>`.

## Changes by area

- **`libs/nativeweb.signin.epass/SessionAuthenticationHandler.cs`** — the handler additionally
  implements `IAuthenticationSignOutHandler`; `SignOutAsync` calls the same `SessionAuth.Clear`
  the logout endpoint calls (R1, R2).
- **`libs/nativeweb.signin.epass/EpassSignInBuilder.cs`** — a new `AddressPolicy` delegate beside
  `SignInResolver`, and `OnAddressSubmitted` registering it as a singleton, mirroring `OnSignedIn`
  exactly (R5).
- **`libs/nativeweb.signin.epass/SignInEndpoints.cs`** — `PostLoginAsync` resolves the optional
  policy after the size bound and before `StartLoginLinkAsync`, and redirects with the existing
  `AddressUnusable` on refusal (R5, R6).
- **`libs/nativeweb.signin.epass/nativeweb.signin.epass.csproj`** — `InternalsVisibleTo` for the
  testing assembly, with the reason in a comment (R3).

  > **Corrected during T4.** The plan named an `AssemblyInfo.cs`. The repository's convention is
  > the MSBuild `<InternalsVisibleTo Include="..." />` item, already used by this project for the
  > unit tier and by `nativeweb.render` — and an `AssemblyInfo.cs` beside it would have emitted a
  > duplicate attribute for the unit tier and failed to compile. The grant went in the csproj.

- **`Directory.Build.props`** — a two-depth `JauntyQRoot` probe, so a `.worktrees/<branch>`
  checkout resolves.

  > **Not planned, and not optional.** With only the single-depth probe, every build from a
  > worktree in this repository fails at restore before compiling a line of the branch's own code.
  > justsmtp's `NativeWebRoot` probe has carried the two-depth pair for exactly this reason since
  > it was written. Fixed here because it blocked the branch.
  >
  > It does **not** make a solution-wide build work from a worktree: `NativeWeb.slnx` line 31–32
  > carry `../jauntyq/...` as literal solution-relative paths, which resolve correctly only from
  > the main checkout, and no single relative path is correct from both. Per-project builds work
  > with no flag; `dotnet build NativeWeb.slnx` from a worktree does not. Logged in `work/todo.md`
  > rather than fixed, because the fix is a decision about how the solution references JauntyQ at
  > all.
- **`libs/nativeweb.signin.epass.testing/TestSession.cs`** (new) — `Mint`, `MintExpired`,
  `Tamper`, and the cookie name, over the library's own `SessionCookie` (R3, R4).
- **`tests/nativeweb.signin.epass.tests.unit/`** — policy wiring, sign-out attribute equality,
  and the testing helper's own round trip.
- **`tests/nativeweb.signin.epass.tests.integration/`** — all six acceptance criteria that need a
  host: the sign-out seam end to end within a request, the header-for-header equality of R2, the
  no-provider-call assertion of R5, and the identical-response assertion of R6.
- **`tests/nativeweb.signin.epass.tests.e2e/`** — the sign-out seam through a real browser (R10).
- **`tests/nativeweb.signin.epass.tests.aot/`** — exercise all three seams so the AOT gate covers
  them (R8).

## Data / interfaces

```csharp
public delegate ValueTask<bool> AddressPolicy(
    HttpContext http, string email, CancellationToken ct);

public EpassSignInBuilder OnAddressSubmitted(AddressPolicy policy);

// SessionAuthenticationHandler : AuthenticationHandler<...>, IAuthenticationSignOutHandler
public Task SignOutAsync(AuthenticationProperties? properties);

// NativeWeb.SignIn.Epass.Testing
public static class TestSession
{
    public static string CookieName(EpassSignInOptions options);
    public static string Mint(EpassSignInOptions options, string subject, DateTimeOffset now);
    public static string Mint(EpassSignInOptions options, string subject,
                              DateTimeOffset issuedAt, DateTimeOffset expiresAt);
    public static string MintExpired(EpassSignInOptions options, string subject, DateTimeOffset now);
    public static string Tamper(string token);
}
```

No schema and no wire change. The ticket format is untouched, which is the point of R3: the
testing package mints through `SessionCookie` rather than beside it.

## Risks & mitigations

- **The address policy becomes an enumeration oracle.** Closed structurally: the policy returns
  `bool` and the endpoint maps every refusal to the one existing code, so the consumer has no way
  to vary the response. R6's acceptance criterion asserts the two responses are identical in
  status, location and header set — the same shape justsmtp's own R9 test uses.
- **The policy is handed an unbounded string.** It is not: the existing 254-byte bound runs first,
  and the plan puts the policy call after it deliberately. A consumer's regex is therefore never
  handed a 4 MB form value, which is where a catastrophic-backtracking denial of service would
  come from.
- **A throwing policy takes down the login POST.** Consumer code on an unauthenticated path.
  Wrapped: an exception is logged and treated as a refusal, never as an acceptance, so a broken
  policy fails closed.
- **`InternalsVisibleTo` widens the security surface.** To one assembly, which is not deployed:
  the testing project sets `IsAotCompatible=false`, carries xunit, and exists precisely so xunit
  stays out of an application's closure. A consumer that references it in production has made that
  choice explicitly.
- **The sign-out seam and the logout endpoint drift.** R2 exists to prevent it, and the
  implementation shares one method rather than two that agree today.

## Coverage check

| R | Covered by |
| --- | --- |
| R1 | `SessionAuthenticationHandler` implements `IAuthenticationSignOutHandler` |
| R2 | Both paths call `SessionAuth.Clear`; integration test compares the two headers |
| R3 | `AssemblyInfo.cs` + `TestSession.Mint` |
| R4 | `TestSession.MintExpired`, `TestSession.Tamper` |
| R5 | `OnAddressSubmitted` + the call site in `PostLoginAsync` before `StartLoginLinkAsync` |
| R6 | The endpoint maps refusal to `AddressUnusable`; policy returns `bool` |
| R7 | Every seam optional — `GetService` not `GetRequiredService`; 008 suite unchanged |
| R8 | The AOT test project exercises all three |
| R9 | justsmtp branch, sequenced after this merges |
| R10 | Unit + integration for all three, e2e for sign-out |

No uncovered requirement.
