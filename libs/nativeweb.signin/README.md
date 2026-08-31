# nativeweb.signin

OAuth 2.1 / OIDC **relying party** for NativeWeb: the authorization-code grant with PKCE, an
opaque server-side session behind a `__Host-` cookie, token refresh, sign-out, and a bearer
introspection filter for API endpoints.

**NativeWeb is the client, never the server.** There is no token issuer here, no consent screen,
no realm resolution, no client registry and no identity schema. The provider is whatever
conforming issuer the configuration names — one is as good as another, and none of them is named
anywhere in this library's source.

```xml
<ProjectReference Include="..\..\libs\nativeweb.signin\nativeweb.signin.csproj" />
```

Published as `NativeWeb.SignIn` for consumers building against packages. The skeleton carries
both forms; `tools/new-product.ps1` stamps a product with the section already wired.

## The whole wiring

```csharp
builder.Services.AddSignIn(builder.Configuration.GetSection("MyProduct:SignIn"));

app.UseNativeWebPipeline(Locales.All, auth =>
{
    auth.UseAuthentication();
    auth.UseAuthorization();
});

app.MapSignIn();
```

Three lines and a protected route works:

```csharp
app.MapGet("/account", (HttpContext http) => http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value)
   .RequireAuthorization();
```

`AddSignIn` registers a real authentication scheme, which is what `RequireAuthorization` needs.
Without it a protected route answers a bare 401 to everybody, with no way to sign in. A challenge
redirects to `LoginPath` carrying `?returnTo=` for where the visitor was going.

**`NameIdentifier` is the only claim on the principal**, and it carries whatever `OnSignedIn`
returned — the provider's subject when no resolver is registered. `User.Identity.Name` is null:
the library mints no name claim, because a display name is the product's to fetch and store, not
something to take on the provider's word on every request.

### Authentication goes in the pipeline's own slot

`auth => { … }` is not decoration: `UseAuthentication` has to run **before antiforgery**, because
the antiforgery check compares a token against the user as it stands when the check runs. Wire it
later and a signed-in visitor is anonymous to antiforgery, and the sign-out form is refused with a
400 that says nothing about ordering. `UseNativeWebPipeline` gives the slot so a product cannot
get the order wrong by hand.

## Configuration

Every key is a string in one section. `AddSignIn(IConfiguration)` reads them one at a time — there
is no `Bind()`, no configuration-binder source generator and no reflecting extension, because a
reflecting binder is the one dependency that would stop this library publishing AOT-clean. A key
that will not parse throws at startup naming its own full path, not at the first request.

| Key | Required | Default | What it is |
| --- | --- | --- | --- |
| `AuthorizationEndpoint` | yes | — | Where the browser is sent. From the provider's discovery document. |
| `TokenEndpoint` | yes | — | Where the code is exchanged. |
| `UserInfoEndpoint` | yes | — | Where the subject is read after exchange. |
| `IntrospectionEndpoint` | for `RequireBearerToken` | — | Where an API's bearer token is checked. |
| `ClientId` | yes | — | This product's registration at the provider. |
| `ClientSecret` | for a confidential client | — | Environment variable or user-secrets. Never `appsettings.json`. |
| `RedirectUri` | yes | — | Absolute, and registered at the provider. Must resolve to `CallbackPath`. |
| `Scopes` | no | `openid profile email` | An array. **Replaces** the defaults wholesale rather than adding to them. |
| `ClientAuthentication` | no | `ClientSecretBasic` | `ClientSecretBasic`, `ClientSecretPost` or `None` for a public client. |
| `CookieName` | no | `__Host-signin` | Must keep the `__Host-` prefix; startup refuses one that does not. |
| `LoginPath` | no | `/signin` | |
| `CallbackPath` | no | `/signin/callback` | |
| `LogoutPath` | no | `/signout` | POST only, antiforgery-checked. |
| `SignedOutPath` | no | `/` | Where sign-out lands. |
| `DefaultReturnPath` | no | `/` | Where a sign-in with no `returnTo` lands. |
| `FailurePath` | no | `/` | Where a FAILED sign-in lands, carrying `?error=`. Cannot be `LoginPath` or `CallbackPath`; startup refuses either. |
| `FlowLifetimeSeconds` | no | 900 | How long an in-flight sign-in may take. |
| `SessionLifetimeSeconds` | no | 1209600 | Fourteen days. |
| `RefreshSkewSeconds` | no | 120 | How early an access token is refreshed. |
| `IntrospectionCacheLifetimeSeconds` | no | 30 | Short on purpose: a revoked token stays live for this long. |
| `StoreEncryptionKey` | see below | — | Base64 of 32 random bytes. |
| `RetainTokens` | no | `true` | False discards the provider's tokens after userinfo, for a product that reads the subject once and never calls the provider again. Nothing to refresh, and nothing to leak. |
| `AllowEphemeralStoreEncryptionKey` | no | `false` | Development only. |

An empty string counts as absent, so a stamped `appsettings.json` full of `""` changes nothing.
The same options are settable in code — `AddSignIn(options => { … })` — which is what the sample
does, because its endpoints are only known once the process knows what port it bound.

### The store encryption key

The provider's tokens sit in the session store, and this key is what protects them at rest.
Supply 32 random bytes, base64, the same way the client secret is supplied:

```csharp
Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
```

Rotating it signs every session out. Startup refuses a key that is a single repeated byte, because
that is a placeholder somebody meant to replace. `AllowEphemeralStoreEncryptionKey` mints one per
process instead — which means sessions do not survive a restart, and it is for a demo.

## What the endpoints do

`MapSignIn()` maps three, at the configured paths:

| Method | Path | What it does |
| --- | --- | --- |
| GET | `LoginPath` | Mints `state` and a PKCE verifier, records them, redirects to the provider. |
| GET | `CallbackPath` | Checks `state` at-most-once, exchanges the code, reads the subject, mints the session, redirects to `returnTo`. |
| POST | `LogoutPath` | Antiforgery-checked. Drops the session and the cookie, redirects to `SignedOutPath`. |

`LoginPath` is a GET and carries no antiforgery token, which is not an oversight: it takes no
action on the user's behalf and reads no session, so the worst a forced navigation achieves is a
login prompt the visitor can decline. `state` is what defends the callback, which is the request
that acts.

`?returnTo=` on `LoginPath` is where the visitor lands afterwards. It is sanitised to a local path
— an absolute URL, a scheme-relative one and a backslash-smuggled one all fall back to
`DefaultReturnPath`, so the endpoint is not an open redirect.

A failed sign-in lands at `FailurePath` as `?error=<code>`, one of
`callback_incomplete`, `state_used`, `state_expired`, `state_unknown`, `signin_failed`,
`provider_unavailable`, `refused` (`SignInFailureCode`). **No provider-supplied string is ever
rendered**: the provider's own `error` and `error_description` are upstream text, and a page that
echoes them is a page that renders whatever the provider felt like sending. Render your own copy
per code.

**`FailurePath` cannot be `LoginPath`, and startup refuses the pairing.** `GET LoginPath` is the
flow trigger this library owns; it never reads `?error=`, so a failure delivered there starts the
flow over. Anything that fails the same way every time — an unconfigured provider, a `SignInPolicy`
that refuses, a `SignInResolver` returning null — then bounces the browser until it gives up with
`ERR_TOO_MANY_REDIRECTS`, and the code reaches nothing that reads it.

The default is `/`, which never loops but is also unlikely to explain anything. Point it at a page
you render and read the code there.

## API endpoints: bearer introspection

```csharp
app.MapGet("/api/things", () => Things()).RequireBearerToken();
```

Reads the `Authorization: Bearer` header, introspects it at `IntrospectionEndpoint`, caches the
verdict for `IntrospectionCacheLifetimeSeconds` and answers 401 when it is not active. Use
`RequireBearerToken()` and not `AddEndpointFilter<BearerIntrospectionFilter>()` — the latter
activates the filter by type from the container, and everything the filter needs is registered
keyed on the scheme name, so it fails at the first request with a message naming `OAuthClient`
rather than the missing scheme.

## The seams

`AddSignIn` returns a builder. Every seam on it is optional, and a product that takes none gets
in-memory stores, the provider's subject as the session subject, and no policy — a working sign-in
for a single-instance product, and the shape the sample runs.

| Seam | Take it when |
| --- | --- |
| `.WithFlowStore<T>()` | More than one process. The in-memory default cannot see a flow another instance started, so a callback landing on the wrong instance is refused. `ILoginFlowStore` is `StartAsync`, `TakeAsync` and `SweepAsync`, and the take must be at-most-once. |
| `.WithSessionStore<T>()` | More than one process, or sessions that outlive a restart. `ISessionStore` is `CreateAsync`, `GetAsync`, `ReplaceIfMatchAsync`, `RemoveAsync`. |
| `.OnSignedIn(resolver)` | You keep a local user table. Returns the local key, or null to refuse — in which case no session is minted. |
| `.WithSignInPolicy(policy)` | You want to refuse some visitors before troubling the provider. Fails closed: a throwing policy refuses. Called concurrently, so hold no per-request state. |

### `OnSignedIn` and the only safe key

```csharp
.OnSignedIn(async (http, subject, contact, ct) => await FindOrCreateAsync(subject.Value, ct));
```

The two payload parameters are named for what they are, and the naming is the guard. **Key on
`VerifiedSubject`. Never on `UnverifiedContactInfo.Email`** — a find-or-create keyed on the address
lets anyone who registers at the provider with a victim's address take over the local account.
The address is contact information and nothing else.

**A subject is unique within one provider only, and with a pairwise provider within one client
only.** A product configuring two schemes has to store which scheme a subject came from alongside
it, or two providers' users collide on one key. This is the failure that looks like nothing until
the second provider is added.

## Two schemes in one app

`AddSignIn(scheme, …)` registers everything keyed on the scheme name, so two calls configure two
independent providers rather than the second overwriting the first. A session minted under one is
a value the other's store has never heard of.

What the library cannot supply is **which scheme answers a request that named neither** — that is
the product's decision, and getting it wrong breaks antiforgery first and least obviously. Choose
with a policy scheme:

```csharp
builder.Services
    .AddAuthentication(o => o.DefaultScheme = "either")
    .AddPolicyScheme("either", "session when there is one, cookie otherwise",
        policy => policy.ForwardDefaultSelector = http =>
            http.Request.Cookies.ContainsKey("__Host-signin")
                ? SessionAuthenticationHandler.SchemeName
                : CookieAuthenticationDefaults.AuthenticationScheme);
```

`apps/sample` runs this beside its own cookie demo; `apps/sample/DemoSignIn.cs` is the worked
example, including a whole authorization server in-process so `dotnet run` completes a real round
trip with no external service.

## What a product must supply

- `ClientId`, and a `ClientSecret` unless the client is public.
- The four endpoint URLs, from the provider's discovery document.
- A `RedirectUri` registered at the provider, resolving to `CallbackPath`.
- A `StoreEncryptionKey`, as an environment variable or user-secrets.
- A login page rendering your own copy for each `SignInFailureCode`.
- A sign-out **form**, POSTing to `LogoutPath` with an antiforgery token. Not a link.
- `.WithFlowStore<T>()` and `.WithSessionStore<T>()` if it runs more than one instance.
- A policy scheme, if it has a second authentication scheme.
- The order in the pipeline, which `UseNativeWebPipeline(…, auth => …)` gives it.

## Testing your stores

`libs/nativeweb.signin.testing` carries the conformance suites. Derive from
`SessionStoreConformance` or `LoginFlowStoreConformance`, hand it your implementation, and it
asserts the contract the library relies on — the at-most-once take in particular, which is the
one an obvious implementation gets wrong under concurrency.

## NativeAOT

The library publishes with no IL2xxx/IL3xxx warnings, and
`tests/nativeweb.signin.tests.aot` is the gate: it publishes a probe host referencing nothing else
and fails on any analyser warning. Two rules keep it that way, and both are about the consumer's
publish rather than this one:

- **Route handlers are static lambdas written at the call site.** A route built in a loop, or
  handed a `Delegate` variable, falls back to the reflection overload and fails the consumer's
  publish gate with IL2026/IL3050.
- **No serializer over an anonymous or reflected type.** Fixed-shape payloads are written out.
