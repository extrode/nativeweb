# 020 — Plan
Spec: ./020-spec.md

## Approach

One new file in the sample, `apps/sample/AdminSignIn.cs`, owns the passphrase route pair, and
`Program.cs` chooses between it and the Development one-click route on `IsDevelopment()`. The
secret is read once at startup from `Sample:Admin:Passphrase`; Production with no value calls
`StartupConfigGuards.FailStartup` naming the key, which is the framework's own convention for a
fatal configuration gap. Comparison is `CryptographicOperations.FixedTimeEquals` over the
SHA-256 of both sides, so neither the length nor the content of the secret shapes the timing, and
an unset secret in a non-Production environment compares against nothing and always refuses.

The form is a render-generator template, `templates/signin/form.html`, in the sample's existing
page layout with the antiforgery partial; the POST goes through `MapFormPost<T>` so it gets the
binder, the content-type check and the antiforgery metadata the rest of the sample already relies
on. A signed-in visitor opening `/signin` sees who they are and a sign-out form, which is the
sample's only sign-out and satisfies R7 without touching the generated admin chrome. The POST
carries its own rate-limit policy, registered by the sample with a second `AddRateLimiter` call
(the options compose) and a lower permit than the framework's general policy.

Rejected: a framework primitive (the Django line; the skeleton uses the relying party);
storing a hash (needs a tool to mint it, and `secrets.env` already holds the database
credentials at the same mode); adopting `libs/nativeweb.signin` in the sample (no provider to
talk to on the deployed host).

## Tech / dependencies

.NET 10, `System.Security.Cryptography` (`SHA256`, `CryptographicOperations`), the sample's
existing `nativeweb.render.generator` and `nativeweb.forms` references, ASP.NET Core rate
limiting already registered by `AddNativeWebWeb`. No new packages.

## Changes by area

- `apps/sample/AdminSignIn.cs` — new: `AdminPassphrase` (reads the key, `Matches(string?)`
  constant-time, `Required(environment)` startup guard) and `MapAdminSignIn` (GET form, POST
  verify, POST `/signout`). (R1, R2, R3, R5, R7)
- `apps/sample/templates/signin/form.html` — new template: passphrase field, fixed refusal
  message, or the signed-in state with a sign-out button. (R1, R3, R7)
- `apps/sample/Program.cs` — the rate-limit policy registration; `IsDevelopment()` picks the
  one-click route, everything else maps `MapAdminSignIn`. (R4, R5, R6)
- `scripts/deploy/provision.sh` — `${CONFIG_PREFIX}__Admin__Passphrase=` seeded blank in
  `secrets.env` under a REQUIRED comment. (R8)
- `scripts/deploy/deploy.sh` — the preflight refuses a blank value, same shape as the
  `KnownProxies` check. (R8)
- `docs/02-reference/configuration.md` — the key in the application-keys table with its
  per-environment failure mode; `DocsClaimsTests` parity adjusted if that table is checked. (R9)
- `tests/nativeweb.sample.tests.unit/AdminPassphraseTests.cs` — `Matches` for equal, wrong,
  empty, null, different-length and unset-secret inputs. (R3, R5)
- `tests/nativeweb.scaffold.tests.integration/AdminSignInTests.cs` — a `SampleFactory` variant
  with `UseEnvironment("Staging")` and the key set, https base address (Secure cookie); asserts
  R1, R2, R3 byte-identical bodies, R4 429, R5 refusal when unset, R7 sign-out. Production
  fail-startup is asserted through `Program.Main` in a child process or by the guard's message,
  whichever the tier can observe.
- `tests/nativeweb.scaffold.tests.e2e/SampleProcessFixture.cs` and a new
  `ProductionSampleFixture` — environment and extra variables become constructor parameters; a
  third instance on its own port runs as Production with the passphrase set. (R1, R2)
- `tests/nativeweb.scaffold.tests.e2e/AdminSignInTests.cs` — Playwright: form → admin lists
  Articles; wrong passphrase stays on the form. (R1, R2, R3)
- `work/todo.md`, `work/status/current.md`, `CHANGELOG.md`.

## Data / interfaces

```
Sample:Admin:Passphrase        string, no default
GET  /signin                   200 form | 200 signed-in state with sign-out form
POST /signin  passphrase=…     302 → ReturnUrl (local) | /admin ; 401 form on refusal ; 429 over limit
POST /signout                  302 → /
```

Rate-limit policy name `signin`, fixed window, 10 permits per minute per client address.

## Risks & mitigations

- **Cookie is `Secure` outside Development.** The e2e Production fixture runs over
  `http://localhost`; Chromium and Firefox treat localhost as a secure context and accept the
  cookie. The integration tier sets an https base address, as `SampleSignInFactory` already does.
- **Production startup needs more than the passphrase.** The guard runs after
  `NativeWebOptions.FromConfiguration`, so a host missing both reports the framework's key first;
  the e2e Production fixture sets no data-protection directory and relies on the fallback under
  the runner's home, which exists.
- **A test asserts byte-identical refusal bodies.** The antiforgery token differs per response;
  the comparison strips that one field.
- **`DocsClaimsTests` two-way parity.** A sample key is not a `Key("...")` literal; if the
  application-keys table is parsed, the known set gains it in the same commit.

## Coverage check

R1, R2, R3, R7 → `AdminSignIn.cs`, template, integration and e2e tests. R4 → `Program.cs`
policy, integration test. R5 → `AdminPassphrase.Required`, unit and integration tests. R6 →
`Program.cs` branch, existing suites unchanged. R8 → provisioner and deploy preflight. R9 →
configuration.md. None uncovered.
