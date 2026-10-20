# 025 — Plan

Spec: ./025-spec.md

## Approach

A subject is resolved by one small singleton, `PermissionSubjectResolver`, which `PermissionContext`
consults in place of its hard-coded `user.FindFirstValue(ClaimTypes.NameIdentifier)`. The resolver
holds either a claim name or a host-supplied function, never both, and the choice is made once at
registration rather than per request.

The configuration point is an overload of `AddPermissions` taking an `Action<PermissionSubjectOptions>`.
That is where R3's mutual exclusion is enforced: the delegate runs during registration, so a host
that sets both gets an `InvalidOperationException` naming both before the application is built,
which is the startup failure the spec asks for. An options-validation seam (`IValidateOptions`)
would defer the same failure to the first request that touched the permission layer, which is a
403 in production rather than a crash at boot.

The alternative considered and rejected was letting a host replace `IPermissionContext` outright.
It is already possible — the registration is `TryAddScoped` — and it is what the spec calls the
cost to avoid: reimplementing the memo, the registry intersection and the `EnsureSubjectAsync`
call to change one string.

R4's observability is an `ILogger` on `PermissionContext`, one warning per context instance, fired
only when the principal is authenticated and no subject came back. `Microsoft.Extensions.Logging.Abstractions`
arrives with the existing `FrameworkReference`, so nothing new is referenced, and the message is
declared with `[LoggerMessage]` so no reflection or boxing enters the NativeAOT path. An anonymous
principal does not log (R5): being unauthenticated is the ordinary state of a public page, and a
warning per anonymous request would bury the fault this exists to surface.

## Tech / dependencies

- .NET 10 / C# 14, NativeAOT-clean, `TreatWarningsAsErrors` on.
- `Microsoft.Extensions.Logging.Abstractions` — already present through
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` in
  `libs/nativeweb.permissions/nativeweb.permissions.csproj`. No package added.
- No new project, no change to `IPermissionStore`, no change to the schema.

## Changes by area

- **`libs/nativeweb.permissions/PermissionSubjectOptions.cs`** (new) — `ClaimName` (string, defaults
  to `ClaimTypes.NameIdentifier`) and `Resolve` (`Func<ClaimsPrincipal, string?>?`). Covers R1, R2.
- **`libs/nativeweb.permissions/PermissionSubjectResolver.cs`** (new) — sealed, singleton, built from
  the options once. `Of(ClaimsPrincipal)` returns the subject or null: null when the principal is
  not authenticated (R5), null when the claim is absent, null when the result is empty or
  whitespace (R6), and otherwise the string as it came back, untrimmed and unnormalised (R7).
  Construction throws when both options are set, naming both (R3).
- **`libs/nativeweb.permissions/PermissionContext.cs`** — takes the resolver and an
  `ILogger<PermissionContext>`, calls `resolver.Of(user)` where it read the claim directly, and logs
  once per instance when an authenticated principal yields nothing (R4). The subject-keyed memo
  from the second fable pass stays as it is.
- **`libs/nativeweb.permissions/PermissionsServiceCollectionExtensions.cs`** — the new
  `AddPermissions(catalog, configure)` overload; the existing two-argument form delegates to it with
  no configuration, so a host that configures nothing is on the same path it was (R1's default).
- **`libs/nativeweb.permissions/README.md`** — replaces the "not configurable yet, and the failure is
  silent" paragraph with how to configure it, and keeps the silent-403 warning for the case where
  the configured claim is still absent (R8).
- **`tests/nativeweb.permissions.tests.unit/PermissionSubjectTests.cs`** (new) — the resolver's own
  facts, one per acceptance criterion.
- **`tests/nativeweb.permissions.tests.unit/PermissionContextTests.cs`** — the context under a
  configured claim, and the log fired once for an authenticated principal with no subject and not
  at all for an anonymous one.
- **`tests/nativeweb.scaffold.tests.integration/`** — one fact that a generated gated route answers
  for a host whose subject arrives as `sub`, which is the story the spec opens with, proved through
  a real pipeline rather than against the resolver alone.

## Data / interfaces

```csharp
public sealed class PermissionSubjectOptions
{
    public string ClaimName { get; set; } = ClaimTypes.NameIdentifier;
    public Func<ClaimsPrincipal, string?>? Resolve { get; set; }
}

public sealed class PermissionSubjectResolver
{
    public PermissionSubjectResolver(PermissionSubjectOptions options);
    public string? Of(ClaimsPrincipal user);
}

services.AddPermissions(PermissionCatalog.All, options => options.ClaimName = "sub");
services.AddPermissions(PermissionCatalog.All, options => options.Resolve = user => Compose(user));
```

Setting `ClaimName` to a value other than the default *and* `Resolve` is the R3 failure. Leaving
`ClaimName` at its default while setting `Resolve` is the ordinary R2 case and is not a conflict,
so the resolver compares against the default rather than against null.

## Risks & mitigations

- **A host-supplied `Resolve` runs on every request and is the host's code.** It is called inside
  `HeldAsync`, so an exception from it surfaces as a failed request rather than a silent refusal —
  which is the correct direction, and is stated in the XML docs rather than caught.
- **The claim name is host input, not request input.** Nothing here reads a claim name off the
  wire, so a configured name cannot be influenced by a caller.
- **Logging a subject would log an identifier for a person.** The warning names the claim that was
  looked for and the authentication scheme, and never the claim values, so the diagnostic does not
  become a retention question of its own.
- **`ClaimsPrincipal.FindFirstValue` on a mutated identity** already interacts with the memo; the
  memo is keyed on the resolved subject since the second fable pass, so a resolver returning a new
  value for the same principal is answered about the new value.

## Coverage check

| Requirement | Covered by |
| --- | --- |
| R1 configurable claim, default unchanged | `PermissionSubjectOptions.ClaimName`, `AddPermissions` overload |
| R2 host-supplied function | `PermissionSubjectOptions.Resolve` |
| R3 both is a startup failure | `PermissionSubjectResolver` constructor, called during registration |
| R4 absent subject observable, once per request | `ILogger` on `PermissionContext`, guarded per instance |
| R5 anonymous is not a fault | `PermissionSubjectResolver.Of` returns null before resolving, no log |
| R6 empty or whitespace is no subject | `PermissionSubjectResolver.Of` |
| R7 the subject reaches the store unchanged | `PermissionContext` passes what it was given; no trim |
| R8 documented on the surface and in the README | XML docs on both new types, README section |

No requirement is uncovered.
